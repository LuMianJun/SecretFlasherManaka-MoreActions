using System;
using System.Collections.Generic;
using UnityEngine;

namespace SFM.Actions;

// Each playback instance owns its sampling skeleton and restoration.
// Keep the native Animator enabled: native size controllers need its fresh pose.
internal sealed class ActionPose : IActionPose, IActionFacePose, IActionBodyPose, IActionRootMotionPose
{
    readonly LoadedAction action;
    readonly GameObject liveRoot;
    GameObject proxy;
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
        action = loaded; this.nativeAnimator = nativeAnimator; liveRoot = FindAvatar(target);
        proxy = new GameObject("MoreActionsSamplingRig"); proxy.hideFlags = HideFlags.HideAndDontSave; proxy.SetActive(false);
        try
        {
            var resolved = MotionSkeleton.ResolveBones(liveRoot.transform);
            if (nativeAnimator) proxy.transform.localScale = liveRoot.transform.lossyScale;
            MotionSkeleton.CopyCanonicalTree(liveRoot.transform, proxy.transform);
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
                var sample = proxy.transform.Find(path);
                if (sample == null) throw new Exception("Selected sampling body bone missing: " + path);
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
                foreach (var renderer in liveRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name != "Body.001" || renderer.sharedMesh == null) continue;
                    var node = proxy.transform.Find(MotionSkeleton.CanonicalPath(renderer.transform, liveRoot.transform));
                    if (node == null) continue;
                    var sample = node.gameObject.AddComponent<SkinnedMeshRenderer>(); sample.enabled = false; sample.sharedMesh = renderer.sharedMesh;
                    foreach (var shape in new[] { "eye_close", "vrc.v_aa", "vrc.v_ih", "vrc.v_ou", "vrc.v_e", "vrc.v_oh" })
                    {
                        int index = renderer.sharedMesh.GetBlendShapeIndex(shape);
                        if (index >= 0) morphs.Add((renderer, sample, index));
                    }
                }
            if (action.Definition.WriteFaceMorphs) face = new ActionFaceMorphLayer(liveRoot, action.Definition);
            var nodes = proxy.GetComponentsInChildren<Transform>(true); sampleNodes = new Transform[nodes.Length];
            sampleNeutral = new Quaternion[nodes.Length]; samplePositions = new Vector3[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) { sampleNodes[i] = nodes[i]; sampleNeutral[i] = nodes[i].localRotation; samplePositions[i] = nodes[i].localPosition; }
            if (nativeAnimator)
            {
                rootBasis = liveRoot.transform.rotation;
                sampler = new AnimatorClipSampler(proxy, target.GetComponent<Animator>(), action.Clip);
            }
            else proxy.AddComponent<Animator>();
            Sample(0); hipOrigin = proxy.transform.Find("Armature/Hips").localPosition;
            worldHipOrigin = proxy.transform.Find("Armature/Hips").position;
            if (HasRootMotion && action.Definition.Loop)
            {
                Sample(action.Duration);
                var delta = rootBasis * (proxy.transform.Find("Armature/Hips").position - worldHipOrigin);
                delta.y = 0; LoopRootDisplacement = delta;
                Sample(0);
            }
        }
        catch { Dispose(); throw; }
    }

    static GameObject FindAvatar(GameObject target)
    {
        if (target == null) throw new Exception("Animation target missing");
        if (target.transform.Find("Armature/Hips") != null) return target;
        foreach (var node in target.GetComponentsInChildren<Transform>(true))
            if (node.Find("Armature/Hips") != null) return node.gameObject;
        throw new Exception("Animation target lacks the canonical game skeleton");
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
        finally
        {
            try { if (body != null) { body.Dispose(); face?.RestoreNativeFace(); } else Restore(); }
            finally { if (proxy != null) UnityEngine.Object.Destroy(proxy); proxy = null; }
        }
    }
}
