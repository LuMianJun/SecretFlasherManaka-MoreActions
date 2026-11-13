using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SFM.Actions;
using NVector = System.Numerics.Vector3;

namespace SFM.Mmd;

// Live references are collected separately from persistent shape/solver data.
internal sealed class MmdTargetSnapshot
{
    internal readonly GameObject Target;
    internal Transform Root, Armature;
    internal readonly Dictionary<string, Transform> Bones = new();
    internal readonly Dictionary<string, Matrix4x4> Rest = new();
    internal readonly Dictionary<string, Vector3> ReferencePositions = new();
    internal readonly List<(SkinnedMeshRenderer renderer, Mesh mesh)> Meshes = new();
    internal SkinnedMeshRenderer Face;
    internal readonly Dictionary<string, NVector> Positions = new();
    internal float[] Shape;
    internal string ShapePaths;
    internal Vector3 ArmaturePosition, ArmatureScale;
    // Actual world metric in heading-neutral root axes. No world translation
    // participates, so actor movement/reload cannot contaminate the shape key.
    internal Vector3 MetricX, MetricY, MetricZ;
    internal Quaternion ArmatureRotation;
    internal MmdTargetSnapshot(GameObject target) { Target = target; }
    static readonly string[] presetPaths = MotionRig.BonePaths().OrderBy(p=>p.Count(c=>c=='/')).ThenBy(p=>p,StringComparer.Ordinal).ToArray();
    static readonly HashSet<string> requiredPaths = MotionRig.RequiredBonePaths().ToHashSet(StringComparer.Ordinal);

    sealed class BindLayout
    {
        internal Mesh Mesh;
        internal string[] Slots=Array.Empty<string>();
        internal readonly List<(string Path,int Index,Matrix4x4 Matrix)> Bones=new();
    }
    static readonly Dictionary<int,BindLayout> bindLayouts=new();

    internal IEnumerable<int> Capture()
    {
        if (Target == null) throw new ArgumentException("VMD target missing");
        Root = Target.transform;
        Armature = Root.Find("Armature") ?? throw new ArgumentException("Fixed model Armature missing");
        Face=FixedModelFace.Find(Target);
        ArmaturePosition = Armature.localPosition; ArmatureRotation = Armature.localRotation; ArmatureScale = Armature.localScale;
        var rootWorld=Root.localToWorldMatrix;var inverseHeading=Quaternion.Inverse(Root.rotation);
        MetricX=inverseHeading*(Vector3)rootWorld.GetColumn(0);
        MetricY=inverseHeading*(Vector3)rootWorld.GetColumn(1);
        MetricZ=inverseHeading*(Vector3)rootWorld.GetColumn(2);
        var resolved = new Dictionary<string, Transform> { ["Armature"] = Armature };
        var scopes = new Dictionary<int, Dictionary<string, List<Transform>>>();
        foreach (var path in presetPaths)
        {
            int slash = path.LastIndexOf('/');
            if(!resolved.TryGetValue(path.Substring(0,slash),out var parent))continue;
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
                if (!requiredPaths.Contains(path))continue;
                throw new ArgumentException("Required live body bone missing: " + path);
            }
            if (matches.Count != 1) throw new ArgumentException("Ambiguous body bone: " + path);
            var selected = matches[0];
            Bones.Add(path, selected); resolved.Add(path, selected); yield return 0;
        }
        var byId=Bones.ToDictionary(p=>p.Value.GetInstanceID(),p=>p.Key);
        // The two original model paths supply bind poses. Never walk clothing,
        // physics bones or other meshes to discover renderers / morph names.
        foreach(string meshPath in new[]{"Body.001","Selestia_body"})
        {
            var node=Root.Find(meshPath);var renderer=node==null?null:node.GetComponent<SkinnedMeshRenderer>();
            if(renderer==null||renderer.sharedMesh==null)continue;
            var mesh=renderer.sharedMesh;Meshes.Add((renderer,mesh));
            var bones=renderer.bones;
            if(bones==null)continue;
            // Validate the complete path/index layout, including previously
            // unmapped slots. A later optional binding must not be hidden by
            // a cache entry that only recorded its old supported subset.
            var slots=new string[bones.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var bone=bones[i];
                if(bone!=null&&byId.TryGetValue(bone.GetInstanceID(),out var path))slots[i]=path;
                if(i%8==0)yield return 0;
            }
            int id=mesh.GetInstanceID();
            bool cached=bindLayouts.TryGetValue(id,out var layout)&&layout.Mesh==mesh&&layout.Slots.SequenceEqual(slots);
            if(!cached)
            {
                var bind=mesh.bindposes;
                if(bind==null||bones.Length!=bind.Length)continue;
                layout=new BindLayout{Mesh=mesh,Slots=slots};
                for(int i=0;i<bones.Length;i++)
                {
                    if(slots[i]!=null)layout.Bones.Add((slots[i],i,bind[i].inverse));
                    if(i%8==0)yield return 0;
                }
                foreach(int dead in bindLayouts.Where(p=>p.Value.Mesh==null).Select(p=>p.Key).ToArray())bindLayouts.Remove(dead);
                if(bindLayouts.Count>=16)bindLayouts.Remove(bindLayouts.Keys.First());
                bindLayouts[id]=layout;
            }
            var rootToMesh=Matrix4x4.TRS(node.localPosition,node.localRotation,node.localScale);
            foreach(var entry in layout.Bones)
                if(!Rest.ContainsKey(entry.Path))Rest.Add(entry.Path,rootToMesh*entry.Matrix);
            yield return 0;
        }
        foreach (var path in requiredPaths) if (!Rest.ContainsKey(path)) throw new ArgumentException("Target bind pose missing: " + path);
        var values = new List<float>();
        foreach(var path in presetPaths)
        {
            if(!Rest.TryGetValue(path,out var matrix))continue;
            for(int c=0;c<4;c++){Vector3 v=matrix.GetColumn(c);values.Add(v.x);values.Add(v.y);values.Add(v.z);}
            Vector3 local=matrix.GetColumn(3);
            int slash=path.LastIndexOf('/');string parentPath=path.Substring(0,slash);
            if(ReferencePositions.TryGetValue(parentPath,out var parentPosition))
            {
                Vector3 parentBind=Rest[parentPath].GetColumn(3);
                float length=1;
                // Native customization sets these Y scales to ThighsLength /
                // LowerLegLength. Its reciprocal wrapper cancels inherited
                // scale, then LateUpdate multiplies the child's translation.
                // Cache invalidation alone did not apply this length to IK.
                string name=path.Substring(slash+1);
                if(name.StartsWith("LowerLeg_",StringComparison.Ordinal)||name.StartsWith("Foot_",StringComparison.Ordinal)||name=="Chest")
                    length=Bones[parentPath].localScale.y;
                if(!float.IsFinite(length)||length<=0)throw new ArgumentException("Invalid customized leg length: "+path);
                local=parentPosition+(local-parentBind)*length;
                values.Add(length);
            }
            ReferencePositions.Add(path,local);
            Vector3 p=MetricX*local.x+MetricY*local.y+MetricZ*local.z;
            Positions.Add(path, new NVector(p.x, p.y, p.z)); yield return 0;
        }
        values.AddRange(new[] { ArmaturePosition.x, ArmaturePosition.y, ArmaturePosition.z, ArmatureRotation.x, ArmatureRotation.y, ArmatureRotation.z, ArmatureRotation.w, ArmatureScale.x, ArmatureScale.y, ArmatureScale.z });
        values.AddRange(new[] { MetricX.x,MetricX.y,MetricX.z,MetricY.x,MetricY.y,MetricY.z,MetricZ.x,MetricZ.y,MetricZ.z });
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
