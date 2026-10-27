using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seleka2.Actions;

// Each playback instance owns its sampling skeleton and restoration.
// Keep the native Animator enabled: native size controllers need its fresh pose.
internal sealed class ActionPose : IActionPose, IActionFacePose
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
    public int BoneCount => bones.Count;

    internal ActionPose(GameObject target, LoadedAction loaded)
    {
        action = loaded; liveRoot = FindAvatar(target);
        proxy = new GameObject("MoreActionsSamplingRig"); proxy.hideFlags = HideFlags.HideAndDontSave; proxy.SetActive(false);
        try
        {
            var resolved = MotionSkeleton.ResolveBones(liveRoot.transform);
            MotionSkeleton.CopyCanonicalTree(liveRoot.transform, proxy.transform);
            foreach (var path in MotionRig.BonePaths())
                if (ActionPolicy.WritesBone(action.Definition, path)) bones.Add((path, resolved[path], proxy.transform.Find(path)));
            if (bones.Count == 0) throw new Exception("Action has no writable bones");
            savedRotations = new Quaternion[bones.Count];
            for (int i = 0; i < bones.Count; i++) savedRotations[i] = bones[i].live.localRotation;
            savedHip = resolved["Armature/Hips"].localPosition;
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
            proxy.AddComponent<Animator>();
            Sample(0); hipOrigin = proxy.transform.Find("Armature/Hips").localPosition;
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
        action.Clip.SampleAnimation(proxy, time);
    }

    public bool Apply(float time)
    {
        if (liveRoot == null || proxy == null || action.Clip == null) return false;
        Sample(Mathf.Clamp(time, 0, action.Duration));
        foreach (var bone in bones)
        {
            if (bone.live == null || bone.sample == null) return false;
            bone.live.localRotation = bone.sample.localRotation;
            if (bone.path == "Armature/Hips" && action.Definition.WriteHipTranslation) bone.live.localPosition = savedHip + bone.sample.localPosition - hipOrigin;
        }
        face?.Begin();
        foreach (var shape in morphs) if (shape.live != null) face?.Write(shape.live, shape.index, shape.sample.GetBlendShapeWeight(shape.index));
        return true;
    }

    public void RestoreNativeFace() => face?.RestoreNativeFace();

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
    public void Dispose() { Restore(); if (proxy != null) UnityEngine.Object.Destroy(proxy); proxy = null; }
}
