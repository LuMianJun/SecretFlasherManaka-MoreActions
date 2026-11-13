using System;
using System.Collections.Generic;
using UnityEngine;

namespace SFM.Actions;

// Each playback instance owns its sampling lease and restoration.
// Keep the native Animator enabled: native size controllers need its fresh pose.
internal sealed class ActionPose : IActionPose, IActionFacePose, IActionBodyPose, IActionRootMotionPose
{
    readonly LoadedAction action;
    readonly GameObject liveRoot;
    GameObject proxy;
    ActionSamplingRigPool.Entry samplingRig;
    readonly List<(string path, Transform live, Transform sample)> bones = new();
    readonly List<(SkinnedMeshRenderer live, SkinnedMeshRenderer sample, int index)> morphs = new();
    Transform[] sampleNodes;
    Quaternion[] sampleNeutral, savedRotations;
    Vector3[] samplePositions;
    ActionFaceMorphLayer face;
    Vector3 savedHip, hipOrigin;
    bool restored;
    readonly bool nativeAnimator;
    AnimatorClipSampler sampler;
    ActionBodyPoseLease body;
    Vector3 worldHipOrigin;
    Quaternion rootBasis;
    public bool HasRootMotion => nativeAnimator && action.Definition.WriteHipTranslation && bones.Exists(b => b.path == "Armature/Hips");
    public Vector3 RootDisplacement { get; private set; }
    public Vector3 LoopRootDisplacement { get; private set; }
    public int BoneCount => bones.Count;

    internal ActionPose(GameObject target, LoadedAction loaded, bool nativeAnimator = false)
    {
        action = loaded; this.nativeAnimator = nativeAnimator;
        try
        {
            samplingRig = ActionSamplingRigPool.Acquire(target, nativeAnimator);
            liveRoot = samplingRig.Root; proxy = samplingRig.Proxy;
            var resolved = samplingRig.Live;
            foreach (var path in action.Definition.BonePaths)
                if (!MotionRig.IsSupportedPath(path) || !resolved.ContainsKey(path))
                    throw new Exception("Selected live body bone missing: " + path);
            foreach (var path in MotionRig.BonePaths())
            {
                if (!ActionPolicy.WritesBone(action.Definition, path)) continue;
                // Runtime clips do not expose their curve bindings. Preserve
                // default ownership of the original body rig; additional
                // preset bones participate only when explicitly selected.
                if (action.Definition.BonePaths.Length == 0 && !MotionRig.IsRequiredPath(path)) continue;
                if (!resolved.TryGetValue(path, out var live)) continue;
                if (!samplingRig.Sample.TryGetValue(path, out var sample)) throw new Exception("Selected sampling body bone missing: " + path);
                bones.Add((path, live, sample));
            }
            if (bones.Count == 0) throw new Exception("Action has no writable bones");
            savedRotations = new Quaternion[bones.Count];
            for (int i = 0; i < bones.Count; i++) savedRotations[i] = bones[i].live.localRotation;
            savedHip = resolved["Armature/Hips"].localPosition;
            // Both clip backends own the same live transforms. Keep the
            // latest native/customization baseline rather than restoring an
            // old head/neck pose captured only when playback began.
            body = new ActionBodyPoseLease(target, bones.ConvertAll(b => b.live));
            if (action.Definition.WriteFaceMorphs)
            {
                var renderer = FixedModelFace.Find(liveRoot);
                if (renderer != null && samplingRig.Sample.TryGetValue(FixedModelFace.FacePath, out var node))
                {
                    var sample = node.GetComponent<SkinnedMeshRenderer>() ?? node.gameObject.AddComponent<SkinnedMeshRenderer>(); sample.enabled = false; sample.sharedMesh = renderer.sharedMesh;
                    foreach (int index in samplingRig.FaceIndices(renderer))
                    {
                        if (index >= 0) morphs.Add((renderer, sample, index));
                    }
                }
            }
            if (action.Definition.WriteFaceMorphs) face = new ActionFaceMorphLayer(liveRoot, action.Definition);
            sampleNodes = samplingRig.Nodes; sampleNeutral = samplingRig.Rotations;
            samplePositions = samplingRig.Positions;
            if (nativeAnimator)
            {
                rootBasis = liveRoot.transform.rotation;
                try { sampler = new AnimatorClipSampler(proxy, target.GetComponent<Animator>(), action.Clip); }
                catch
                {
                    // A failed graph constructor can also have failed its own
                    // cleanup before returning a sampler to this owner.
                    samplingRig.Retired = true;
                    throw;
                }
            }
            else
            {
                var animator = proxy.GetComponent<Animator>() ?? proxy.AddComponent<Animator>();
                animator.avatar = null; animator.enabled = true; animator.applyRootMotion = false; animator.fireEvents = false;
            }
            var sampleHip = samplingRig.Sample["Armature/Hips"];
            Sample(0); hipOrigin = sampleHip.localPosition;
            worldHipOrigin = sampleHip.position;
            if (HasRootMotion && action.Definition.Loop)
            {
                Sample(action.Duration);
                var delta = rootBasis * (sampleHip.position - worldHipOrigin);
                delta.y = 0; LoopRootDisplacement = delta;
                Sample(0);
            }
        }
        catch { Dispose(); throw; }
    }

    void Sample(float time)
    {
        for (int i = 0; i < sampleNodes.Length; i++) { sampleNodes[i].localRotation = sampleNeutral[i]; sampleNodes[i].localPosition = samplePositions[i]; }
        foreach (var shape in morphs) shape.sample.SetBlendShapeWeight(shape.index, 0);
        if (sampler != null) sampler.Sample(time);
        else action.Clip.SampleAnimation(proxy, time);
    }

    public bool Apply(float time)
    {
        if (liveRoot == null || proxy == null || action.Clip == null) return false;
        Sample(Mathf.Clamp(time, 0, action.Duration));
        body?.ApplyBaseline();
        foreach (var bone in bones)
        {
            if (bone.live == null || bone.sample == null) return false;
            bone.live.localRotation = bone.sample.localRotation;
            if (bone.path == "Armature/Hips" && action.Definition.WriteHipTranslation)
            {
                if (nativeAnimator)
                {
                    var displacement = rootBasis * (bone.sample.position - worldHipOrigin);
                    RootDisplacement = new Vector3(displacement.x, 0, displacement.z);
                    bone.live.localPosition += bone.live.parent.InverseTransformVector(new Vector3(0, displacement.y, 0));
                    bone.live.rotation = rootBasis * bone.sample.rotation;
                }
                else bone.live.localPosition += bone.sample.localPosition - hipOrigin;
            }
        }
        face?.Begin();
        foreach (var shape in morphs) if (shape.live != null) face?.Write(shape.live, shape.index, shape.sample.GetBlendShapeWeight(shape.index));
        return true;
    }

    public void RestoreNativeFace() => face?.RestoreNativeFace();
    public void RestoreNativeBody() => body?.RestoreNativeBody();
    public void BeginNativeBody() => body?.BeginNativeBody();
    public void EndNativeBody(bool succeeded) => body?.EndNativeBody(succeeded);
    public IDisposable PreserveNativeCorrection(ActionBoneCorrection correction) => body?.PreserveNativeCorrection(correction);
    public bool SuppressKneeOffset => body?.SuppressKneeOffset ?? false;

    internal void Restore()
    {
        if (restored) return; restored = true;
        for (int i = 0; i < bones.Count; i++) if (bones[i].live != null && savedRotations != null && i < savedRotations.Length)
        {
            bones[i].live.localRotation = savedRotations[i];
            if (bones[i].path == "Armature/Hips" && action.Definition.WriteHipTranslation) bones[i].live.localPosition = savedHip;
        }
        face?.RestoreNativeFace();
    }
    public void Dispose()
    {
        try { sampler?.Dispose(); }
        catch { if (samplingRig != null) samplingRig.Retired = true; throw; }
        finally
        {
            try { if (body != null) { body.Dispose(); face?.RestoreNativeFace(); } else Restore(); }
            catch { if (samplingRig != null) samplingRig.Retired = true; throw; }
            finally
            {
                var released = samplingRig; samplingRig = null; proxy = null;
                released?.Dispose();
            }
        }
    }
}
