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
    readonly ActionDefinition definition;
    readonly MmdMotion motion;
    readonly ActionBodyPoseLease body;
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
    {
        rig = MmdRigCache.Rent(target, referencePose);
        definition = action; motion = keys;
        try
        {
            write = new bool[rig.bindings.Count];
            int count = 0;
            for (int i = 0; i < write.Length; i++)
            {
                var b = rig.bindings[i]; write[i] = ActionPolicy.WritesBone(action, b.path);
                if (write[i]) count++;
                if (b.path == "Armature/Hips") writeHip = write[i] && action.WriteHipTranslation;
            }
            BoneCount = count;
            var owned = rig.bindings.Where((b,i)=>write[i]).Select(b=>b.live)
                .Concat(rig.toes.Where(t=>ActionPolicy.WritesBone(action,t.path.Substring(0,t.path.LastIndexOf('/')))).Select(t=>t.live));
            body = new ActionBodyPoseLease(target, owned);
            if (action.WriteFaceMorphs)
            {
                face = new ActionFaceMorphLayer(target, action);
                morphs = MmdMorphBindings.Create(rig.faceRenderer, motion, face, out int ambiguous);
                if (motion.morphs.Count > 0)
                {
                    var bound = morphs.Select(m=>m.Source).ToHashSet(StringComparer.Ordinal);
                    var missing = motion.morphs.Keys.Where(n=>!bound.Contains(n)).ToArray();
                    global::Plugin.LogSource?.LogInfo("MMD_MORPHS_BOUND id="+action.Id+" tracks="+motion.morphs.Count+" boundTracks="+bound.Count+" targets="+morphs.Count+" ambiguousTargets="+ambiguous+" unmapped="+string.Join(",",missing.Take(8)));
                }
            }
            rootBasis = rig.liveRoot.rotation;
            referenceHipHeight = rig.source.byName["下半身"].rest.y;
            rig.source.Evaluate(motion, 0); originHip = rig.source.byName["下半身"].node.position;
            if (writeHip)
                global::Plugin.LogSource?.LogInfo("MMD_VERTICAL_BASELINE id="+action.Id+" mode=reference-rest firstOffsetM="+
                    ((originHip.y-referenceHipHeight)*rig.scale).ToString("F5",System.Globalization.CultureInfo.InvariantCulture));
            if (writeHip && action.Loop)
            {
                rig.source.Evaluate(motion, motion.lastFrame);
                LoopRootDisplacement = HorizontalWorld(rig.source.byName["下半身"].node.position - originHip);
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
        for (int i = 0; i < rig.bindings.Count; i++)
        {
            var b = rig.bindings[i];
            b.sample.localRotation = (Quaternion.Inverse(b.sample.parent.rotation) * rig.source.byName[b.source].node.rotation * b.neutral).normalized;
            if (write[i]) b.live.localRotation = b.sample.localRotation;
        }
        if (writeHip)
        {
            // Parent-of-all/center/groove are composed by the source hierarchy,
            // following UnityVMDPlayer's displacement handling. Controller
            // locomotion anchors X/Z to the first frame. Y uses the reference
            // skeleton, preserving an authored first-frame crouch or jump.
            RootDisplacement = HorizontalWorld(rig.source.byName["下半身"].node.position - originHip);
            float vertical = (rig.source.byName["下半身"].node.position.y - referenceHipHeight) * rig.scale;
            rig.hip.localPosition = rig.hip.localPosition + rig.hip.parent.InverseTransformVector(rig.liveRoot.rotation * Vector3.up * vertical);
        }
        face?.Begin();
        if (face != null) foreach (var m in morphs)
            if (m.Renderer != null) face.Write(m.Renderer, m.Index, motion.Morph(m.Source, seconds));
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
