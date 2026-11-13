using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SFM.Actions;

namespace SFM.Mmd;

// This source shares immutable sparse keys; each playback owns a solver.
public sealed class RuntimeVmdPose : IActionPose, IActionRootMotionPose, IActionFacePose, IActionBodyPose
{
    readonly RuntimeVmdRig rig;
    readonly MmdMotion motion;
    readonly MmdMotion faceMotion;
    readonly ActionBodyPoseLease body;
    readonly MmdSkeleton.Bone sourceHip;
    readonly bool[] write;
    readonly ActionFaceMorphLayer face;
    readonly List<MmdMorphBindings.Binding> morphs = new();
    readonly float referenceHipHeight;
    readonly Vector3 originHip;
    readonly Quaternion rootBasis;
    readonly bool writeHip;
    bool disposed;
    public int BoneCount { get; }
    public bool HasRootMotion => writeHip;
    public Vector3 RootDisplacement { get; private set; }
    public Vector3 LoopRootDisplacement { get; }

    Vector3 HorizontalWorld(Vector3 displacement)
    {
        var result = rootBasis * displacement * rig.scale;
        result.y = 0;
        return result;
    }

    public RuntimeVmdPose(GameObject target, ActionDefinition action, MmdMotion keys, MmdReferencePose referencePose = MmdReferencePose.A)
        : this(target, action, keys, referencePose, null) { }
    internal RuntimeVmdPose(GameObject target, ActionDefinition action, MmdMotion keys, MmdReferencePose referencePose, MmdMotion faceKeys)
    {
        rig = MmdRigCache.Rent(target, referencePose);
        faceMotion = faceKeys ?? keys;
        try
        {
            motion=rig.BindMotion(keys);
            rig.source.PrepareMotion(motion);
            foreach(string path in action.BonePaths)
                if(!rig.bindings.Any(b=>b.path==path))throw new InvalidDataException("MMD target bone missing: "+path);
            write = new bool[rig.bindings.Count];
            int count = 0;
            for (int i = 0; i < write.Length; i++)
            {
                var b = rig.bindings[i];
                bool keyed=motion.tracks.ContainsKey(b.source)||((b.source=="左目"||b.source=="右目")&&motion.tracks.ContainsKey("両目"));
                write[i] = (ActionPolicy.WritesBone(action, b.path)||(b.twist&&b.parent!=null&&ActionPolicy.WritesBone(action,b.parent.path)))&&(!b.optional||keyed||b.twist);
                if (write[i]) count++;
                if (b.path == "Armature/Hips") writeHip = write[i] && action.WriteHipTranslation;
            }
            BoneCount = count;
            var owned = rig.bindings.Where((b,i)=>write[i]).Select(b=>b.live);
            body = new ActionBodyPoseLease(target, owned);
            var unsupported=motion.tracks.Where(p=>!rig.source.byName.ContainsKey(p.Key)&&!motion.ResolvedAliases.Contains(p.Key)&&MmdBoneMap.Animated(p.Value)).Select(p=>p.Key).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
            global::Plugin.LogSource?.LogInfo("MMD_BONES_BOUND id="+action.Id+" targetBones="+rig.bindings.Count+" controlled="+count+" unsupportedAnimated="+string.Join(",",unsupported));
            if (action.WriteFaceMorphs)
            {
                face = new ActionFaceMorphLayer(target, action);
                morphs = MmdMorphBindings.Create(rig.faceRenderer, faceMotion, face, out int ambiguous);
                if (faceMotion.morphs.Count > 0)
                {
                    var bound = morphs.Select(m=>m.Source).ToHashSet(StringComparer.Ordinal);
                    var missing = faceMotion.morphs.Keys.Where(n=>!bound.Contains(n)).ToArray();
                    global::Plugin.LogSource?.LogInfo("MMD_MORPHS_BOUND id="+action.Id+" tracks="+faceMotion.morphs.Count+" boundTracks="+bound.Count+" targets="+morphs.Count+" ambiguousTargets="+ambiguous+" unmapped="+string.Join(",",missing.Take(8)));
                }
            }
            rootBasis = rig.liveRoot.rotation;
            sourceHip = rig.source.byName["下半身"];
            referenceHipHeight = sourceHip.rest.y;
            rig.source.Evaluate(motion, 0); originHip = sourceHip.node.position;
            if (writeHip)
                global::Plugin.LogSource?.LogInfo("MMD_VERTICAL_BASELINE id="+action.Id+" mode=reference-rest firstOffsetM="+
                    ((originHip.y-referenceHipHeight)*rig.scale).ToString("F5",System.Globalization.CultureInfo.InvariantCulture));
            if (writeHip && action.Loop)
            {
                rig.source.Evaluate(motion, motion.lastFrame);
                LoopRootDisplacement = HorizontalWorld(sourceHip.node.position - originHip);
                rig.source.Evaluate(motion, 0);
            }
        }
        catch { body?.Dispose(); MmdRigCache.Return(rig); throw; }
    }

    public bool Apply(float seconds)
    {
        if (disposed || !rig.IsUsable) return false;
        seconds = Mathf.Clamp(seconds, 0, motion.lastFrame / 30f); rig.source.Evaluate(motion, seconds * 30);
        body.ApplyBaseline();
        var heading = rig.liveRoot.rotation;
        for (int i = 0; i < rig.bindings.Count; i++)
        {
            var b = rig.bindings[i];
            b.sample.localRotation = (Quaternion.Inverse(b.sample.parent.rotation) * b.sourceBone.node.rotation * b.neutral).normalized;
            if (write[i])
            {
                if(b.optional&&!b.twist&&b.parent!=null)
                {
                    // Unmapped native control parents (e.g. BreastRotateL)
                    // retain customization. Apply the keyed delta in the
                    // nearest mapped parent's axes, not as its child's local Q.
                    var parentRotation=b.parentSourceBone.node.rotation;
                    var relative=Quaternion.Inverse(parentRotation)*b.sourceBone.node.rotation;
                    var basis=b.parent.live.rotation*Quaternion.Inverse(b.parent.neutral);
                    b.live.rotation=basis*relative*Quaternion.Inverse(basis)*b.live.rotation;
                }
                else b.live.localRotation = b.sample.localRotation;
                if(b.parent!=null)
                {
                    // Transfer only the positional difference introduced by
                    // missing pivots, sibling branches and authored local keys.
                    // The native baseline retains customization and all lengths;
                    // ordinary rigid edges get a zero correction.
                    var source=b.sourceBone;var parent=b.parentSourceBone;
                    var delta=(source.node.position-parent.node.position)-parent.node.rotation*b.restDelta;
                    b.live.localPosition+=b.live.parent.InverseTransformVector(heading*delta*rig.scale);
                }
            }
        }
        if (writeHip)
        {
            // Parent-of-all/center/groove are composed by the source hierarchy,
            // following UnityVMDPlayer's displacement handling. Controller
            // locomotion anchors X/Z to the first frame. Y uses the reference
            // skeleton, preserving an authored first-frame crouch or jump.
            RootDisplacement = HorizontalWorld(sourceHip.node.position - originHip);
            float vertical = (sourceHip.node.position.y - referenceHipHeight) * rig.scale;
            rig.hip.localPosition = rig.hip.localPosition + rig.hip.parent.InverseTransformVector(heading * Vector3.up * vertical);
        }
        face?.Begin();
        if (face != null) foreach (var m in morphs)
            if (m.Renderer != null) face.Write(m.Renderer, m.Index, faceMotion.Morph(m.Source, seconds));
        return true;
    }

    public void RestoreNativeFace() => face?.RestoreNativeFace();
    public void RestoreNativeBody() => body.RestoreNativeBody();
    public void BeginNativeBody() => body.BeginNativeBody();
    public void EndNativeBody(bool succeeded) => body.EndNativeBody(succeeded);
    public IDisposable PreserveNativeCorrection(ActionBoneCorrection correction) => body.PreserveNativeCorrection(correction);
    public bool SuppressKneeOffset => body.SuppressKneeOffset;

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        try
        {
            try { body?.Dispose(); }
            finally { face?.RestoreNativeFace(); }
        }
        finally { MmdRigCache.Return(rig); }
    }
}
