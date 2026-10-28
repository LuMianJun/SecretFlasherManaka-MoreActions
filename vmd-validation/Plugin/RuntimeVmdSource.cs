using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Seleka2.Actions;

namespace Seleka2.Mmd;

// This source shares immutable sparse keys; each playback owns a solver.
public sealed class RuntimeVmdSource : IActionSource, IPreparedActionSource
{
    readonly MmdMotion motion;
    public float Duration => motion.lastFrame / 30f;
    public int RetainedTracks => motion.tracks.Count;
    public int IgnoredTracks { get; }
    internal string SkeletonKey => MmdRigCache.SkeletonKey(ReferencePose);
    internal MmdReferencePose ReferencePose { get; }
    internal void Prepare(GameObject target) => MmdRigCache.Prepare(target, ReferencePose);
    public bool IsPrepared(GameObject target) => MmdRigCache.IsPrepared(target, ReferencePose);
    public RuntimeVmdSource(string vmdPath, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        if (!Enum.IsDefined(typeof(MmdReferencePose), referencePose)) throw new ArgumentOutOfRangeException(nameof(referencePose));
        ReferencePose = referencePose;
        motion = MmdMotion.Read(vmdPath);
        if (motion.tracks.Count == 0 || Duration <= 0) throw new InvalidDataException("VMD contains no playable bone motion");
        {
            var names = new HashSet<string>(GameMmdSkeleton.SupportedNames());
            var ignored = motion.tracks.Keys.Where(n => !names.Contains(n)).ToArray();
            IgnoredTracks = ignored.Length;
            foreach (var name in ignored) motion.tracks.Remove(name);
            if (!motion.tracks.Keys.Any(n => n == "上半身" || n == "下半身" || n == "左腕" || n == "右腕")) throw new InvalidDataException("VMD has no supported standard body tracks");
        }
    }
    public IActionPose CreatePose(GameObject target, ActionDefinition definition) => new RuntimeVmdPose(target, definition, motion, ReferencePose);
}

internal sealed class RuntimeVmdRig
{
    internal sealed class Binding
    {
        public string path, source;
        public Transform live, sample;
        public Quaternion neutral;
    }
    internal readonly List<Binding> bindings = new();
    internal readonly List<(string path, Transform live)> toes = new();
    internal readonly List<(SkinnedMeshRenderer renderer, int index, string source)> morphs = new();
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

    internal bool IsUsable => target != null && liveRoot != null && hip != null && proxy != null
        && source?.root != null && bindings.All(b => b.live != null)
        && meshes.All(m => m.renderer != null && m.renderer.sharedMesh == m.mesh);

    internal bool HelpersValid => proxy != null && source?.root != null;
    internal void Rebind(MmdTargetSnapshot snapshot)
    {
        target = snapshot.Target; liveRoot = snapshot.Root; hip = snapshot.Bones["Armature/Hips"];
        foreach (var b in bindings) b.live = snapshot.Bones[b.path];
        toes.Clear(); toes.AddRange(snapshot.Bones.Where(p=>p.Key.Contains("/Toe_", StringComparison.Ordinal)).Select(p=>(p.Key,p.Value)));
        meshes.Clear(); meshes.AddRange(snapshot.Meshes); morphs.Clear(); morphs.AddRange(snapshot.Morphs);
        Shape = snapshot.Shape; ShapePaths = snapshot.ShapePaths; DetachedOnReturn = false;
    }
    internal void Detach()
    {
        target = null; liveRoot = null; hip = null; meshes.Clear(); morphs.Clear();
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
            foreach (var path in MotionRig.BonePaths().OrderBy(p => p.Count(c => c == '/')))
            {
                if (!rest.TryGetValue(path, out var matrix)) throw new Exception("Target bind pose missing: " + path);
                int slash = path.LastIndexOf('/');
                var sample = new GameObject(path.Substring(slash + 1)).transform;
                sample.SetParent(proxy.transform.Find(path.Substring(0, slash)), false);
                sample.position = matrix.GetColumn(3); sample.rotation = matrix.rotation;
                bindings.Add(new Binding { path = path, live = liveBones[path], sample = sample, neutral = sample.rotation });
                yield return 0;
            }
            foreach (var suffix in new[] { "_L", "_R" })
            {
                var foot = bindings.First(b => b.sample.name == "Foot" + suffix);
                var toePath = foot.path + "/Toe" + suffix;
                if (rest.TryGetValue(toePath, out var matrix))
                {
                    var toe = new GameObject("Toe" + suffix).transform; toe.SetParent(foot.sample, false); toe.position = matrix.GetColumn(3); toe.rotation = matrix.rotation; yield return 0;
                }
            }
            hip = liveBones["Armature/Hips"];
            source = template.CopyData();
            foreach (var step in source.CreateTransformSteps()) yield return step;
            source.useIk = true;
            var map = Map();
            var byName = bindings.ToDictionary(b => b.sample.name);
            var restRotations = bindings.ToDictionary(b => b.sample.name, b => b.sample.rotation);
            foreach (var b in bindings) b.source = map[b.sample.name];
            foreach (var side in new[] { "左", "右" })
            {
                string suffix = side == "左" ? "_L" : "_R";
                foreach (var names in new[] { new[] { "Shoulder", "肩", "腕", "UpperArm" }, new[] { "UpperArm", "腕", "ひじ", "LowerArm" }, new[] { "LowerArm", "ひじ", "手首", "Hand" }, new[] { "UpperLeg", "足", "ひざ", "LowerLeg" }, new[] { "LowerLeg", "ひざ", "足首", "Foot" } })
                {
                    var b = byName[names[0] + suffix];
                    b.neutral = Align(b.sample, byName[names[3] + suffix].sample.position - b.sample.position, source.byName[side + names[2]].rest - source.byName[side + names[1]].rest); yield return 0;
                }
                var hand = byName["Hand" + suffix]; var forearm = byName["LowerArm" + suffix];
                var correction = forearm.neutral * Quaternion.Inverse(restRotations[forearm.sample.name]);
                hand.neutral = correction * restRotations[hand.sample.name];
                foreach (var b in bindings.Where(b => b.path.StartsWith(hand.path + "/", StringComparison.Ordinal))) b.neutral = correction * restRotations[b.sample.name];
                var foot = byName["Foot" + suffix]; var toe = foot.sample.Find("Toe" + suffix); var sourceFoot = source.byName[side + "足首"];
                if (toe != null && sourceFoot.tail > 0 && sourceFoot.tail < source.bones.Count) foot.neutral = Align(foot.sample, toe.position - foot.sample.position, source.bones[sourceFoot.tail].rest - sourceFoot.rest);
            }
            float sourceLength = (source.byName["左足"].rest - source.byName["左ひざ"].rest).magnitude + (source.byName["左ひざ"].rest - source.byName["左足首"].rest).magnitude;
            const string leg="Armature/Hips/UpperLeg_L";
            var upper=snapshot.Positions[leg];var knee=snapshot.Positions[leg+"/LowerLeg_L"];var ankle=snapshot.Positions[leg+"/LowerLeg_L/Foot_L"];
            float targetLength=(upper-knee).Length()+(knee-ankle).Length();
            if (sourceLength < 0.00001f || targetLength < 0.00001f) throw new Exception("Degenerate retarget skeleton");
            scale = targetLength / sourceLength;
            yield return 0;
    }

    static Quaternion Align(Transform target, Vector3 targetDirection, Vector3 sourceDirection)
        => targetDirection.sqrMagnitude < 0.000001f || sourceDirection.sqrMagnitude < 0.000001f ? target.rotation : Quaternion.FromToRotation(targetDirection, sourceDirection) * target.rotation;
    static Dictionary<string, string> Map()
    {
        var map = new Dictionary<string, string> { { "Hips", "下半身" }, { "Spine", "上半身" }, { "Chest", "上半身3" }, { "Neck", "首" }, { "Head", "頭" } };
        foreach (var side in new[] { "左", "右" })
        {
            string suffix = side == "左" ? "_L" : "_R";
            map["Shoulder" + suffix] = side + "肩"; map["UpperArm" + suffix] = side + "腕捩"; map["LowerArm" + suffix] = side + "手捩"; map["Hand" + suffix] = side + "手首";
            map["UpperLeg" + suffix] = side + "足"; map["LowerLeg" + suffix] = side + "ひざ"; map["Foot" + suffix] = side + "足首";
            foreach (var finger in new[] { new[] { "Thumb", "親指" }, new[] { "Index", "人指" }, new[] { "Middle", "中指" }, new[] { "Ring", "薬指" }, new[] { "Little", "小指" } })
                for (int j = 0; j < 3; j++)
                {
                    int n = finger[0] == "Thumb" ? j : j + 1;
                    map[finger[0] + new[] { "Proximal", "Intermediate", "Distal" }[j] + suffix] = side + finger[1] + (char)('０' + n);
                }
        }
        return map;
    }

    internal void Destroy()
    {
        source?.Dispose(); source = null;
        if (proxy != null) UnityEngine.Object.Destroy(proxy); proxy = null;
    }
}

// A lease owns only playback state. The sampling rig is retained between
// actions; independent simultaneous consumers never share mutable solvers.
public sealed class RuntimeVmdPose : IActionPose, IActionRootMotionPose, IActionFacePose, IActionBodyPose
{
    readonly RuntimeVmdRig rig;
    readonly ActionDefinition definition;
    readonly MmdMotion motion;
    readonly ActionBodyPoseLease body;
    readonly bool[] write;
    readonly ActionFaceMorphLayer face;
    readonly float originHeight;
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
            if (action.WriteFaceMorphs) face = new ActionFaceMorphLayer(target, action);
            rootBasis = rig.liveRoot.rotation;
            rig.source.Evaluate(motion, 0); originHip = rig.source.byName["下半身"].node.position; originHeight = originHip.y;
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
            // locomotion consumes X/Z once; only Y remains on the live hips.
            RootDisplacement = HorizontalWorld(rig.source.byName["下半身"].node.position - originHip);
            float vertical = (rig.source.byName["下半身"].node.position.y - originHeight) * rig.scale;
            rig.hip.localPosition = rig.hip.localPosition + rig.hip.parent.InverseTransformVector(rig.liveRoot.rotation * Vector3.up * vertical);
        }
        face?.Begin();
        if (face != null) foreach (var m in rig.morphs)
            if (m.renderer != null) face.Write(m.renderer, m.index, motion.Morph(m.source, seconds));
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
