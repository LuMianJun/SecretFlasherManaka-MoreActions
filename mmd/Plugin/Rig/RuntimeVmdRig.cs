using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SFM.Actions;

namespace SFM.Mmd;

// This source shares immutable sparse keys; each playback owns a solver.
internal sealed class RuntimeVmdRig
{
    internal sealed class Binding
    {
        public string path, source;
        public Transform live, sample;
        public Quaternion neutral;
        public Binding parent;
        public bool optional;
        public bool twist;
        public MmdSkeleton.Bone sourceBone,parentSourceBone;
        public Vector3 restDelta;
    }
    internal readonly List<Binding> bindings = new();
    internal readonly List<(string path, Transform live)> toes = new();
    internal SkinnedMeshRenderer faceRenderer;
    internal GameObject target;
    internal float[] Shape;
    internal string ShapePaths;
    internal string SkeletonKey => MmdRigCache.SkeletonKey(ReferencePose);
    internal bool DetachedOnReturn;
    internal GameObject proxy;
    internal MmdSkeleton source;
    internal Transform liveRoot, hip;
    internal float scale;
    internal bool Leased, Retired;
    internal MmdReferencePose ReferencePose;
    readonly List<(SkinnedMeshRenderer renderer, Mesh mesh)> meshes = new();
    KeyValuePair<string,string>[] aliases=Array.Empty<KeyValuePair<string,string>>();
    readonly Dictionary<MmdMotion,MmdMotion> boundMotions=new();
    readonly Queue<MmdMotion> motionOrder=new();

    internal bool IsUsable => target != null && liveRoot != null && hip != null && proxy != null
        && source?.root != null && bindings.All(b => b.live != null)
        && meshes.All(m => m.renderer != null && m.renderer.sharedMesh == m.mesh);

    internal bool HelpersValid => proxy != null && source?.root != null;
    internal void Rebind(MmdTargetSnapshot snapshot)
    {
        target = snapshot.Target; liveRoot = snapshot.Root; hip = snapshot.Bones["Armature/Hips"];
        foreach (var b in bindings) b.live = snapshot.Bones[b.path];
        toes.Clear(); toes.AddRange(snapshot.Bones.Where(p=>p.Key.Contains("/Toe_", StringComparison.Ordinal)).Select(p=>(p.Key,p.Value)));
        meshes.Clear(); meshes.AddRange(snapshot.Meshes); faceRenderer=snapshot.Face;
        Shape = snapshot.Shape; ShapePaths = snapshot.ShapePaths; DetachedOnReturn = false;
    }
    internal void Detach()
    {
        target = null; liveRoot = null; hip = null; meshes.Clear(); faceRenderer=null;
        foreach (var b in bindings) b.live = null; toes.Clear();
    }
    internal IEnumerable<int> BuildSteps(MmdTargetSnapshot snapshot, MmdSkeleton template, MmdReferencePose referencePose = MmdReferencePose.A)
    {
            ReferencePose=referencePose;
            var rest = snapshot.Rest; var liveBones = snapshot.Bones;
            Rebind(snapshot);
            proxy = new GameObject("MmdTargetSamplingRig"); proxy.hideFlags = HideFlags.HideAndDontSave; proxy.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(proxy); yield return 0;
            var armature = new GameObject("Armature").transform; armature.SetParent(proxy.transform, false);
            armature.localPosition = snapshot.ArmaturePosition;
            armature.localRotation = snapshot.ArmatureRotation;
            armature.localScale = snapshot.ArmatureScale;
            var required=MotionRig.RequiredBonePaths().ToHashSet(StringComparer.Ordinal);
            var bindingPaths=new Dictionary<string,Binding>(StringComparer.Ordinal);
            foreach (var path in rest.Keys.OrderBy(p => p.Count(c => c == '/')).ThenBy(p=>p,StringComparer.Ordinal))
            {
                if (!rest.TryGetValue(path, out var matrix)) throw new Exception("Target bind pose missing: " + path);
                int slash = path.LastIndexOf('/');
                var sample = new GameObject(path.Substring(slash + 1)).transform;
                Binding parent=null;
                string parentPath=path.Substring(0,slash);
                while(parentPath.Contains('/'))
                {if(bindingPaths.TryGetValue(parentPath,out parent))break;parentPath=parentPath.Substring(0,parentPath.LastIndexOf('/'));}
                sample.SetParent(parent?.sample??armature, false);
                sample.position = snapshot.ReferencePositions[path]; sample.rotation = matrix.rotation;
                var binding=new Binding { path = path, live = liveBones[path], sample = sample, neutral = sample.rotation, parent=parent,optional=!required.Contains(path)&&!path.Contains("/Toe_",StringComparison.Ordinal),twist=sample.name.StartsWith("UpperArm_twist",StringComparison.Ordinal)||sample.name.StartsWith("LowerArm_twist",StringComparison.Ordinal) };
                bindings.Add(binding);bindingPaths.Add(path,binding);
                yield return 0;
            }
            hip = liveBones["Armature/Hips"];
            source = template.CopyData();
            foreach (var step in source.CreateTransformSteps()) yield return step;
            source.useIk = true;
            var map = MmdBoneMap.Create(rest.Keys);
            var byName = bindings.GroupBy(b => b.sample.name).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.First());
            var restRotations = bindings.ToDictionary(b => b.path, b => b.sample.rotation);
            foreach (var b in bindings)
            {
                b.source=map[b.path];b.sourceBone=source.byName[b.source];
                if(b.parent!=null){b.parentSourceBone=b.parent.sourceBone;b.restDelta=b.sourceBone.rest-b.parentSourceBone.rest;}
            }
            foreach (var side in new[] { "左", "右" })
            {
                string suffix = side == "左" ? "_L" : "_R";
                foreach (var names in new[] { new[] { "Shoulder", "肩", "腕", "UpperArm" }, new[] { "UpperArm", "腕", "ひじ", "LowerArm" }, new[] { "LowerArm", "ひじ", "手首", "Hand" }, new[] { "UpperLeg", "足", "ひざ", "LowerLeg" }, new[] { "LowerLeg", "ひざ", "足首", "Foot" } })
                {
                    var b = byName[names[0] + suffix];
                    b.neutral = Align(b.sample, byName[names[3] + suffix].sample.position - b.sample.position, source.byName[side + names[2]].rest - source.byName[side + names[1]].rest); yield return 0;
                }
                var hand = byName["Hand" + suffix]; var forearm = byName["LowerArm" + suffix];
                var correction = forearm.neutral * Quaternion.Inverse(restRotations[forearm.path]);
                hand.neutral = correction * restRotations[hand.path];
                foreach (var b in bindings.Where(b => b.path.StartsWith(hand.path + "/", StringComparison.Ordinal))) b.neutral = correction * restRotations[b.path];
                var foot = byName["Foot" + suffix]; var toe = foot.sample.Find("Toe" + suffix); var sourceFoot = source.byName[side + "足首"];
                if (toe != null && sourceFoot.tail > 0 && sourceFoot.tail < source.bones.Count) foot.neutral = Align(foot.sample, toe.position - foot.sample.position, source.bones[sourceFoot.tail].rest - sourceFoot.rest);
                if(toe!=null){var toeBinding=bindings.First(b=>b.sample==toe);toeBinding.neutral=foot.neutral*Quaternion.Inverse(restRotations[foot.path])*restRotations[toeBinding.path];}
            }
            foreach(var b in bindings.Where(b=>b.optional&&b.parent!=null))
                b.neutral=b.parent.neutral*Quaternion.Inverse(restRotations[b.parent.path])*restRotations[b.path];
            float sourceLength = (source.byName["左足"].rest - source.byName["左ひざ"].rest).magnitude + (source.byName["左ひざ"].rest - source.byName["左足首"].rest).magnitude;
            const string leg="Armature/Hips/UpperLeg_L";
            var upper=snapshot.Positions[leg];var knee=snapshot.Positions[leg+"/LowerLeg_L"];var ankle=snapshot.Positions[leg+"/LowerLeg_L/Foot_L"];
            float targetLength=(upper-knee).Length()+(knee-ankle).Length();
            if (sourceLength < 0.00001f || targetLength < 0.00001f) throw new Exception("Degenerate retarget skeleton");
            scale = targetLength / sourceLength;
            aliases=CreateAliases().ToArray();
            yield return 0;
    }

    static Quaternion Align(Transform target, Vector3 targetDirection, Vector3 sourceDirection)
        => targetDirection.sqrMagnitude < 0.000001f || sourceDirection.sqrMagnitude < 0.000001f ? target.rotation : Quaternion.FromToRotation(targetDirection, sourceDirection) * target.rotation;
    internal IEnumerable<KeyValuePair<string,string>> Aliases()=>aliases;
    IEnumerable<KeyValuePair<string,string>> CreateAliases()
    {
        foreach(var b in bindings)yield return new(b.sample.name,b.source);
        // VMD's bone field is 15 Shift-JIS bytes. Resolve truncated native names
        // only when the resulting alias has exactly one possible source bone.
        var encoding=System.Text.Encoding.GetEncoding(932);
        foreach(var b in source.bones)
        {
            var bytes=encoding.GetBytes(b.name);
            yield return new(bytes.Length<=15?b.name:encoding.GetString(bytes,0,15),b.name);
        }
    }

    internal MmdMotion BindMotion(MmdMotion keys)
    {
        if(!keys.PreparedForPlayback)return keys.BindAliases(aliases);
        if(boundMotions.TryGetValue(keys,out var cached))return cached;
        var bound=keys.BindAliases(aliases);
        if(boundMotions.Count>=16)boundMotions.Remove(motionOrder.Dequeue());
        boundMotions.Add(keys,bound);motionOrder.Enqueue(keys);return bound;
    }

    internal void Destroy()
    {
        boundMotions.Clear();motionOrder.Clear();aliases=Array.Empty<KeyValuePair<string,string>>();
        source?.Dispose(); source = null;
        if (proxy != null) UnityEngine.Object.Destroy(proxy); proxy = null;
    }
}

// A lease owns only playback state. The sampling rig is retained between
// actions; independent simultaneous consumers never share mutable solvers.
