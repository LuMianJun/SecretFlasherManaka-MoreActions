using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Seleka2.Actions;
using NVector = System.Numerics.Vector3;

namespace Seleka2.Mmd;

// Live references are collected separately from persistent shape/solver data.
internal sealed class MmdTargetSnapshot
{
    internal readonly GameObject Target;
    internal Transform Root, Armature;
    internal readonly Dictionary<string, Transform> Bones = new();
    internal readonly Dictionary<string, Matrix4x4> Rest = new();
    internal readonly List<(SkinnedMeshRenderer renderer, Mesh mesh)> Meshes = new();
    internal readonly List<(SkinnedMeshRenderer renderer, int index, string source)> Morphs = new();
    internal readonly Dictionary<string, NVector> Positions = new();
    internal float[] Shape;
    internal string ShapePaths;
    internal Vector3 ArmaturePosition, ArmatureScale;
    // Actual world metric in heading-neutral root axes. No world translation
    // participates, so actor movement/reload cannot contaminate the shape key.
    internal Vector3 MetricX, MetricY, MetricZ;
    internal Quaternion ArmatureRotation;
    internal MmdTargetSnapshot(GameObject target) { Target = target; }

    IEnumerable<SkinnedMeshRenderer> RenderCandidates()
    {
        var seen = new HashSet<int>();
        foreach (string name in new[] { "Body.001", "Selestia_body" })
        {
            var node = Root.Find(name);
            var renderer = node == null ? null : node.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null && seen.Add(renderer.GetInstanceID())) yield return renderer;
            else yield return null;
        }
        var queue = new Queue<Transform>(); queue.Enqueue(Root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue(); var renderer = node.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null && seen.Add(renderer.GetInstanceID())) yield return renderer;
            else yield return null;
            for (int i = 0; i < node.childCount; i++) { queue.Enqueue(node.GetChild(i)); if (i % 8 == 0) yield return null; }
        }
    }

    internal IEnumerable<int> Capture()
    {
        if (Target == null) throw new ArgumentException("VMD target missing");
        Root = Target.transform;
        if (Root.Find("Armature") == null)
            Root = Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.Find("Armature") != null)
                ?? throw new ArgumentException("VMD target has no Armature");
        Armature = Root.Find("Armature");
        ArmaturePosition = Armature.localPosition; ArmatureRotation = Armature.localRotation; ArmatureScale = Armature.localScale;
        var rootWorld=Root.localToWorldMatrix;var inverseHeading=Quaternion.Inverse(Root.rotation);
        MetricX=inverseHeading*(Vector3)rootWorld.GetColumn(0);
        MetricY=inverseHeading*(Vector3)rootWorld.GetColumn(1);
        MetricZ=inverseHeading*(Vector3)rootWorld.GetColumn(2);
        var resolved = new Dictionary<string, Transform> { ["Armature"] = Armature };
        var scopes = new Dictionary<int, Dictionary<string, List<Transform>>>();
        var scales = new List<float>();
        var paths = MotionRig.BonePaths().Concat(new[] { "Armature/Hips/UpperLeg_L/LowerLeg_L/Foot_L/Toe_L", "Armature/Hips/UpperLeg_R/LowerLeg_R/Foot_R/Toe_R" }).OrderBy(p => p.Count(c => c == '/')).ThenBy(p => p, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            int slash = path.LastIndexOf('/'); var parent = resolved[path.Substring(0, slash)];
            int id = parent.GetInstanceID();
            if (!scopes.TryGetValue(id, out var children))
            {
                children = new Dictionary<string, List<Transform>>(StringComparer.Ordinal);
                var queue = new Queue<Transform>(); queue.Enqueue(parent);
                while (queue.Count > 0)
                {
                    var scope = queue.Dequeue();
                    for (int i = 0; i < scope.childCount; i++)
                    {
                        var child = scope.GetChild(i);
                        if (child.name == "PlayerBoneScaleAdjuster")
                        {
                            if (Quaternion.Angle(child.localRotation, Quaternion.identity) > .01f || child.localPosition.sqrMagnitude > .000001f)
                                throw new ArgumentException("Unexpected body adjuster translation/rotation");
                            queue.Enqueue(child);
                        }
                        else
                        {
                            if (!children.TryGetValue(child.name, out var list)) children.Add(child.name, list = new List<Transform>());
                            list.Add(child);
                        }
                        if (i % 8 == 0) yield return 0;
                    }
                }
                scopes.Add(id, children);
            }
            string name = path.Substring(slash + 1);
            if (!children.TryGetValue(name, out var matches))
            {
                if (name.StartsWith("Toe", StringComparison.Ordinal)) continue;
                throw new ArgumentException("Required live body bone missing: " + path);
            }
            if (matches.Count != 1) throw new ArgumentException("Ambiguous body bone: " + path);
            var selected = matches[0]; var bodyScale = Vector3.one;
            for (var wrapper = selected.parent; wrapper != parent; wrapper = wrapper.parent)
            {
                if (wrapper == null || wrapper.name != "PlayerBoneScaleAdjuster") throw new ArgumentException("Unexpected body bone ancestor: " + path);
                var s = wrapper.localScale; bodyScale = new Vector3 { x = bodyScale.x * s.x, y = bodyScale.y * s.y, z = bodyScale.z * s.z };
            }
            scales.Add(bodyScale.x); scales.Add(bodyScale.y); scales.Add(bodyScale.z);
            Bones.Add(path, selected); resolved.Add(path, selected); yield return 0;
        }
        var byId = Bones.ToDictionary(p => p.Value.GetInstanceID(), p => p.Key);
        foreach (var renderer in RenderCandidates())
        {
            if (renderer == null) { yield return 0; continue; }
            var mesh = renderer.sharedMesh; if (mesh == null) continue;
            bool preferred = renderer.name == "Body.001" || renderer.name == "Selestia_body";
            if (preferred) Meshes.Add((renderer, mesh));
            if (renderer.name == "Body.001")
                foreach (var pair in new[] { ("まばたき", "eye_close"), ("あ", "vrc.v_aa"), ("い", "vrc.v_ih"), ("う", "vrc.v_ou"), ("え", "vrc.v_e"), ("お", "vrc.v_oh") })
                { int index = mesh.GetBlendShapeIndex(pair.Item2); if (index >= 0) Morphs.Add((renderer, index, pair.Item1)); }
            if (Rest.Count == Bones.Count) break;
            var bones = renderer.bones; var bind = mesh.bindposes;
            if (bones == null || bind == null || bones.Length != bind.Length) continue;
            // Build the relative matrix from local TRS values. World-space
            // cancellation can otherwise make a moved actor look like a new shape.
            var rootToMesh = Matrix4x4.identity;
            for (var node = renderer.transform; node != Root; node = node.parent)
            {
                if (node == null) throw new ArgumentException("Body renderer is outside the avatar");
                rootToMesh = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * rootToMesh;
                yield return 0;
            }
            bool used = false;
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone != null && byId.TryGetValue(bone.GetInstanceID(), out var path) && !Rest.ContainsKey(path))
                { Rest.Add(path, rootToMesh * bind[i].inverse); used = true; }
                if (i % 8 == 0) yield return 0;
            }
            if (used && !preferred) Meshes.Add((renderer, mesh));
            if (Rest.Count == Bones.Count) break;
        }
        foreach (var path in MotionRig.BonePaths()) if (!Rest.ContainsKey(path)) throw new ArgumentException("Target bind pose missing: " + path);
        var values = new List<float>();
        foreach (var pair in Rest.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            for (int c = 0; c < 4; c++) { Vector3 v = pair.Value.GetColumn(c); values.Add(v.x); values.Add(v.y); values.Add(v.z); }
            Vector3 local = pair.Value.GetColumn(3);
            Vector3 p=MetricX*local.x+MetricY*local.y+MetricZ*local.z;
            Positions.Add(pair.Key, new NVector(p.x, p.y, p.z)); yield return 0;
        }
        values.AddRange(new[] { ArmaturePosition.x, ArmaturePosition.y, ArmaturePosition.z, ArmatureRotation.x, ArmatureRotation.y, ArmatureRotation.z, ArmatureRotation.w, ArmatureScale.x, ArmatureScale.y, ArmatureScale.z });
        values.AddRange(new[] { MetricX.x,MetricX.y,MetricX.z,MetricY.x,MetricY.y,MetricY.z,MetricZ.x,MetricZ.y,MetricZ.z });
        values.AddRange(scales);
        if (values.Any(v => !float.IsFinite(v))) throw new ArgumentException("Nonfinite target shape");
        Shape = values.ToArray(); ShapePaths = string.Join("|", Rest.Keys.OrderBy(p => p, StringComparer.Ordinal));
    }
    internal bool Matches(float[] shape, string paths)
    {
        if (shape == null || ShapePaths != paths || shape.Length != Shape.Length) return false;
        for (int i = 0; i < shape.Length; i++)
            if (Math.Abs(shape[i] - Shape[i]) > .00001f * Math.Max(1, Math.Max(Math.Abs(shape[i]), Math.Abs(Shape[i])))) return false;
        return true;
    }
    internal static MmdTargetSnapshot Read(GameObject target)
    { var snapshot = new MmdTargetSnapshot(target); foreach (var _ in snapshot.Capture()) { } return snapshot; }
}
