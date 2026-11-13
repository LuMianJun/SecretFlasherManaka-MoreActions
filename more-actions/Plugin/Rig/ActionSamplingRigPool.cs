using System;
using System.Collections.Generic;
using UnityEngine;

namespace SFM.Actions;

// The tree and resolved references belong to one concrete avatar instance.
// A lease always refreshes live TRS and owns a separate sampling proxy; graph,
// timing, root displacement and restoration state remain per playback.
internal static class ActionSamplingRigPool
{
    const int MaximumIdle = 2;
    static readonly List<Entry> entries = new();

    internal sealed class Entry : IDisposable
    {
        internal readonly GameObject Target, Root, Proxy;
        internal readonly Dictionary<string, Transform> Live, Sample = new();
        internal readonly Transform[] Nodes;
        internal readonly Quaternion[] Rotations;
        internal readonly Vector3[] Positions;
        readonly Transform[] sourceNodes, sourceParents;
        readonly int[] sourceChildren;
        readonly string[] sourceNames;
        readonly (Transform live, Transform sample)[] pairs;
        readonly Animator animator;
        readonly UnityEngine.Object avatar;
        readonly SkinnedMeshRenderer face;
        readonly Mesh faceMesh;
        int[] faceIndices;
        internal bool InUse, Retired;

        internal Entry(GameObject target)
        {
            Target = target; Root = FindAvatar(target);
            animator = target.GetComponent<Animator>(); avatar = animator == null ? null : animator.avatar;
            var faceNode = Root.transform.Find(FixedModelFace.FacePath);
            face = faceNode == null ? null : faceNode.GetComponent<SkinnedMeshRenderer>();
            faceMesh = face == null ? null : face.sharedMesh;
            Live = MotionSkeleton.ResolveBones(Root.transform);
            sourceNodes = Root.GetComponentsInChildren<Transform>(true);
            sourceParents = new Transform[sourceNodes.Length]; sourceChildren = new int[sourceNodes.Length]; sourceNames = new string[sourceNodes.Length];
            for (int i = 0; i < sourceNodes.Length; i++)
            { sourceParents[i] = sourceNodes[i].parent; sourceChildren[i] = sourceNodes[i].childCount; sourceNames[i] = sourceNodes[i].name; }
            Proxy = new GameObject("MoreActionsSamplingRig"); Proxy.hideFlags = HideFlags.HideAndDontSave; Proxy.SetActive(false);
            try
            {
                var copied = new List<(Transform live, Transform sample)>();
                MotionSkeleton.CopyCanonicalTree(Root.transform, Proxy.transform, copied, Sample);
                pairs = copied.ToArray(); Nodes = new Transform[pairs.Length + 1]; Nodes[0] = Proxy.transform;
                for (int i = 0; i < pairs.Length; i++) Nodes[i + 1] = pairs[i].sample;
                Rotations = new Quaternion[Nodes.Length]; Positions = new Vector3[Nodes.Length];
            }
            catch { UnityEngine.Object.Destroy(Proxy); throw; }
        }

        internal bool IsCurrent()
        {
            if (Target == null || Root == null || Proxy == null) return false;
            var currentAnimator = Target.GetComponent<Animator>();
            if (currentAnimator != animator || (currentAnimator == null ? null : currentAnimator.avatar) != avatar) return false;
            for (int i = 0; i < sourceNodes.Length; i++)
            {
                var node = sourceNodes[i];
                if (node == null || node.parent != sourceParents[i] || node.childCount != sourceChildren[i] || node.name != sourceNames[i]) return false;
            }
            var faceNode = Root.transform.Find(FixedModelFace.FacePath);
            var currentFace = faceNode == null ? null : faceNode.GetComponent<SkinnedMeshRenderer>();
            return currentFace == face && (currentFace == null ? null : currentFace.sharedMesh) == faceMesh;
        }

        internal void Refresh(bool nativeAnimator)
        {
            Hide();
            Proxy.transform.localPosition = Vector3.zero; Proxy.transform.localRotation = Quaternion.identity;
            Proxy.transform.localScale = nativeAnimator ? Root.transform.lossyScale : Vector3.one;
            foreach (var node in sourceNodes)
                if (node.name == "PlayerBoneScaleAdjuster" && (Quaternion.Angle(node.localRotation, Quaternion.identity) > .01f || node.localPosition.sqrMagnitude > .000001f))
                    throw new Exception("Unexpected body adjuster translation/rotation");
            foreach (var pair in pairs)
            {
                pair.sample.gameObject.SetActive(true);
                pair.sample.localPosition = pair.live.localPosition;
                pair.sample.localRotation = pair.live.localRotation;
                pair.sample.localScale = pair.live.localScale;
            }
            for (int i = 0; i < Nodes.Length; i++)
            { Positions[i] = Nodes[i].localPosition; Rotations[i] = Nodes[i].localRotation; }
        }

        internal void Hide()
        {
            if(Proxy==null)return;
            Proxy.SetActive(false);
            if(Sample.TryGetValue(FixedModelFace.FacePath,out var node))
            { var renderer=node.GetComponent<SkinnedMeshRenderer>();if(renderer!=null)renderer.enabled=false; }
        }

        internal int[] FaceIndices(SkinnedMeshRenderer renderer)
        {
            if(faceIndices!=null)return faceIndices;
            var names=new[] { "eye_close", "vrc.v_aa", "vrc.v_ih", "vrc.v_ou", "vrc.v_e", "vrc.v_oh" };
            faceIndices=new int[names.Length];
            for(int i=0;i<names.Length;i++)faceIndices[i]=renderer.sharedMesh.GetBlendShapeIndex(names[i]);
            return faceIndices;
        }

        public void Dispose() => Release(this);
    }

    static GameObject FindAvatar(GameObject target)
    {
        if (target == null) throw new Exception("Animation target missing");
        if (target.transform.Find("Armature/Hips") != null) return target;
        foreach (var node in target.GetComponentsInChildren<Transform>(true))
            if (node.Find("Armature/Hips") != null) return node.gameObject;
        throw new Exception("Animation target lacks the canonical game skeleton");
    }

    internal static Entry Acquire(GameObject target, bool nativeAnimator)
    {
        Prune();
        Entry selected = null;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry.Target != target || entry.Retired) continue;
            if (!entry.IsCurrent())
            {
                entry.Retired = true;
                if (!entry.InUse) { UnityEngine.Object.Destroy(entry.Proxy); entries.RemoveAt(i); }
                continue;
            }
            if (!entry.InUse && selected == null) selected = entry;
        }
        if (selected == null) { selected = new Entry(target); entries.Add(selected); }
        selected.InUse = true;
        try { selected.Refresh(nativeAnimator); return selected; }
        catch { selected.Retired = true; Release(selected); throw; }
    }

    static void Release(Entry entry)
    {
        if (!entry.InUse) return;
        entry.InUse = false;
        try { entry.Hide(); }
        catch
        {
            entry.Retired = true;
            try { if (entry.Proxy != null) UnityEngine.Object.Destroy(entry.Proxy); }
            finally { entries.Remove(entry); }
            throw;
        }
        int idle = 0;
        foreach (var item in entries) if (!item.InUse) idle++;
        if (entry.Retired || entry.Target == null || entry.Root == null || entry.Proxy == null || idle > MaximumIdle)
        {
            entry.Retired = true;
            try { if (entry.Proxy != null) UnityEngine.Object.Destroy(entry.Proxy); }
            finally { entries.Remove(entry); }
        }
    }

    // Cheap lifetime checks only; the full topology check runs on acquisition.
    internal static void Prune()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry.Target != null && entry.Root != null && entry.Proxy != null) continue;
            entry.Retired = true;
            if (entry.Proxy != null) UnityEngine.Object.Destroy(entry.Proxy);
            entries.RemoveAt(i);
        }
    }

    internal static void Clear()
    {
        foreach (var entry in entries) { entry.Retired = true; if (entry.Proxy != null) UnityEngine.Object.Destroy(entry.Proxy); }
        entries.Clear();
    }
}
