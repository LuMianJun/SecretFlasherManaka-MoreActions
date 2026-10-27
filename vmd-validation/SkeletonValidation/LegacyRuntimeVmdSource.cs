using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Seleka2.Actions;

namespace Seleka2.Mmd;

// This source shares immutable sparse keys; each playback owns a solver.
public sealed class LegacyRuntimeVmdSource : IActionSource
{
    readonly MmdMotion motion;
    readonly string modelPath;
    readonly bool paired;
    public float Duration => motion.lastFrame / 30f;
    public int RetainedTracks => motion.tracks.Count;
    public int IgnoredTracks { get; }
    public LegacyRuntimeVmdSource(string vmdPath, string pmdPath, bool matchedModel)
    {
        if (!File.Exists(pmdPath)) throw new FileNotFoundException("MMD reference skeleton missing", pmdPath);
        motion = MmdMotion.Read(vmdPath); modelPath = pmdPath; paired = matchedModel;
        if (motion.tracks.Count == 0 || Duration <= 0) throw new InvalidDataException("VMD contains no playable bone motion");
        using (var profile = MmdSkeleton.ReadPmd(modelPath, false))
        {
            var names = new HashSet<string>(profile.byName.Keys);
            if (!paired) foreach (var name in new[] { "全ての親", "グルーブ", "腰", "上半身2", "上半身3", "左肩P", "右肩P", "左肩C", "右肩C", "左親指０", "右親指０" }) names.Add(name);
            var ignored = motion.tracks.Keys.Where(n => !names.Contains(n)).ToArray();
            IgnoredTracks = ignored.Length;
            foreach (var name in ignored) motion.tracks.Remove(name);
            if (!motion.tracks.Keys.Any(n => n == "上半身" || n == "下半身" || n == "左腕" || n == "右腕")) throw new InvalidDataException("VMD has no supported standard body tracks");
        }
    }
    public IActionPose CreatePose(GameObject target, ActionDefinition definition) => new LegacyRuntimeVmdPose(target, definition, motion, modelPath, paired);
}

public sealed class LegacyRuntimeVmdPose : IActionPose
{
    sealed class Binding
    {
        public string path, source;
        public Transform live, sample;
        public Quaternion neutral, saved;
        public bool write;
    }
    readonly ActionDefinition definition;
    readonly MmdMotion motion;
    readonly List<Binding> bindings = new();
    readonly List<(SkinnedMeshRenderer renderer, int index, string source, float saved)> morphs = new();
    GameObject proxy;
    MmdSkeleton source;
    Transform liveRoot, hip;
    Vector3 savedHip;
    float scale, originHeight;
    bool disposed;
    public int BoneCount => bindings.Count(b => b.write);

    public LegacyRuntimeVmdPose(GameObject target, ActionDefinition action, MmdMotion keys, string modelPath, bool paired)
    {
        definition = action; motion = keys;
        try
        {
            if (target == null) throw new Exception("VMD target missing");
            liveRoot = FindAvatar(target);
            var liveBones = MotionSkeleton.ResolveBones(liveRoot);
            // Bind poses, rather than the current idle animation, define the
            // target's neutral axes. The player's world yaw is cancelled here.
            var rest = ReadBindPoses(liveRoot);
            proxy = new GameObject("MmdTargetSamplingRig"); proxy.hideFlags = HideFlags.HideAndDontSave; proxy.SetActive(false);
            var armature = new GameObject("Armature").transform; armature.SetParent(proxy.transform, false);
            var liveArmature = liveRoot.Find("Armature");
            armature.localPosition = liveArmature.localPosition;
            armature.localRotation = liveArmature.localRotation;
            armature.localScale = liveArmature.localScale;
            foreach (var path in MotionRig.BonePaths().OrderBy(p => p.Count(c => c == '/')))
            {
                if (!rest.TryGetValue(path, out var matrix)) throw new Exception("Target bind pose missing: " + path);
                int slash = path.LastIndexOf('/');
                var sample = new GameObject(path.Substring(slash + 1)).transform;
                sample.SetParent(proxy.transform.Find(path.Substring(0, slash)), false);
                sample.position = matrix.GetColumn(3); sample.rotation = matrix.rotation;
                bindings.Add(new Binding { path = path, live = liveBones[path], sample = sample, neutral = sample.rotation, saved = liveBones[path].localRotation, write = ActionPolicy.WritesBone(action, path) });
            }
            foreach (var suffix in new[] { "_L", "_R" })
            {
                var foot = bindings.First(b => b.sample.name == "Foot" + suffix);
                var toePath = foot.path + "/Toe" + suffix;
                if (rest.TryGetValue(toePath, out var matrix))
                {
                    var toe = new GameObject("Toe" + suffix).transform; toe.SetParent(foot.sample, false); toe.position = matrix.GetColumn(3); toe.rotation = matrix.rotation;
                }
            }
            hip = liveBones["Armature/Hips"]; savedHip = hip.localPosition;
            source = MmdSkeleton.ReadPmd(modelPath);
            if (!paired) throw new NotSupportedException("Frozen test reference supports paired PMD only"); source.useIk = true;
            var map = Map(source);
            var byName = bindings.ToDictionary(b => b.sample.name);
            var restRotations = bindings.ToDictionary(b => b.sample.name, b => b.sample.rotation);
            foreach (var b in bindings) b.source = map[b.sample.name];
            foreach (var side in new[] { "左", "右" })
            {
                string suffix = side == "左" ? "_L" : "_R";
                foreach (var names in new[] { new[] { "Shoulder", "肩", "腕", "UpperArm" }, new[] { "UpperArm", "腕", "ひじ", "LowerArm" }, new[] { "LowerArm", "ひじ", "手首", "Hand" }, new[] { "UpperLeg", "足", "ひざ", "LowerLeg" }, new[] { "LowerLeg", "ひざ", "足首", "Foot" } })
                {
                    var b = byName[names[0] + suffix];
                    b.neutral = Align(b.sample, byName[names[3] + suffix].sample.position - b.sample.position, source.byName[side + names[2]].rest - source.byName[side + names[1]].rest);
                }
                var hand = byName["Hand" + suffix]; var forearm = byName["LowerArm" + suffix];
                var correction = forearm.neutral * Quaternion.Inverse(restRotations[forearm.sample.name]);
                hand.neutral = correction * restRotations[hand.sample.name];
                foreach (var b in bindings.Where(b => b.path.StartsWith(hand.path + "/", StringComparison.Ordinal))) b.neutral = correction * restRotations[b.sample.name];
                var foot = byName["Foot" + suffix]; var toe = foot.sample.Find("Toe" + suffix); var sourceFoot = source.byName[side + "足首"];
                if (toe != null && sourceFoot.tail > 0 && sourceFoot.tail < source.bones.Count) foot.neutral = Align(foot.sample, toe.position - foot.sample.position, source.bones[sourceFoot.tail].rest - sourceFoot.rest);
            }
            float sourceLength = (source.byName["左足"].rest - source.byName["左ひざ"].rest).magnitude + (source.byName["左ひざ"].rest - source.byName["左足首"].rest).magnitude;
            float targetLength = (byName["UpperLeg_L"].sample.position - byName["LowerLeg_L"].sample.position).magnitude + (byName["LowerLeg_L"].sample.position - byName["Foot_L"].sample.position).magnitude;
            if (sourceLength < 0.00001f || targetLength < 0.00001f) throw new Exception("Degenerate retarget skeleton");
            scale = targetLength / sourceLength;
            source.Evaluate(motion, 0); originHeight = source.byName["下半身"].node.position.y;
            if (action.WriteFaceMorphs)
                foreach (var renderer in liveRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name != "Body.001" || renderer.sharedMesh == null) continue;
                    foreach (var pair in new[] { ("まばたき", "eye_close"), ("あ", "vrc.v_aa"), ("い", "vrc.v_ih"), ("う", "vrc.v_ou"), ("え", "vrc.v_e"), ("お", "vrc.v_oh") })
                    { int index = renderer.sharedMesh.GetBlendShapeIndex(pair.Item2); if (index >= 0) morphs.Add((renderer, index, pair.Item1, renderer.GetBlendShapeWeight(index))); }
                }
        }
        catch { Dispose(); throw; }
    }

    static Transform FindAvatar(GameObject target)
    {
        if (target.transform.Find("Armature") != null) return target.transform;
        foreach (var node in target.GetComponentsInChildren<Transform>(true)) if (node.Find("Armature") != null) return node;
        throw new Exception("VMD target has no Armature");
    }
    static Dictionary<string, Matrix4x4> ReadBindPoses(Transform root)
    {
        var result = new Dictionary<string, Matrix4x4>();
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => r.name == "Body.001" ? 0 : 1))
        {
            if (renderer.sharedMesh == null) continue;
            var bones = renderer.bones; var bind = renderer.sharedMesh.bindposes;
            if (bones == null || bind == null || bones.Length != bind.Length) continue;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || !bones[i].IsChildOf(root)) continue;
                string path = MotionSkeleton.CanonicalPath(bones[i], root);
                if (!result.ContainsKey(path)) result.Add(path, root.worldToLocalMatrix * renderer.localToWorldMatrix * bind[i].inverse);
            }
        }
        return result;
    }
    static Quaternion Align(Transform target, Vector3 targetDirection, Vector3 sourceDirection)
        => targetDirection.sqrMagnitude < 0.000001f || sourceDirection.sqrMagnitude < 0.000001f ? target.rotation : Quaternion.FromToRotation(targetDirection, sourceDirection) * target.rotation;
    static string Choose(MmdSkeleton rig, params string[] names) => names.First(n => rig.byName.ContainsKey(n));
    static Dictionary<string, string> Map(MmdSkeleton rig)
    {
        var map = new Dictionary<string, string> { { "Hips", "下半身" }, { "Spine", "上半身" }, { "Chest", Choose(rig, "上半身3", "上半身2", "上半身") }, { "Neck", "首" }, { "Head", "頭" } };
        foreach (var side in new[] { "左", "右" })
        {
            string suffix = side == "左" ? "_L" : "_R";
            map["Shoulder" + suffix] = side + "肩"; map["UpperArm" + suffix] = Choose(rig, side + "腕捩", side + "腕"); map["LowerArm" + suffix] = Choose(rig, side + "手捩", side + "ひじ"); map["Hand" + suffix] = side + "手首";
            map["UpperLeg" + suffix] = side + "足"; map["LowerLeg" + suffix] = side + "ひざ"; map["Foot" + suffix] = side + "足首";
            foreach (var finger in new[] { new[] { "Thumb", "親指" }, new[] { "Index", "人指" }, new[] { "Middle", "中指" }, new[] { "Ring", "薬指" }, new[] { "Little", "小指" } })
                for (int j = 0; j < 3; j++)
                {
                    int n = finger[0] == "Thumb" && rig.byName.ContainsKey(side + "親指０") ? j : j + 1;
                    if (finger[0] == "Thumb" && !rig.byName.ContainsKey(side + "親指０")) n = Math.Min(n, 2);
                    map[finger[0] + new[] { "Proximal", "Intermediate", "Distal" }[j] + suffix] = side + finger[1] + (char)('０' + n);
                }
        }
        return map;
    }

    public bool Apply(float seconds)
    {
        if (disposed || liveRoot == null || hip == null || proxy == null || source == null || source.root == null) return false;
        seconds = Mathf.Clamp(seconds, 0, motion.lastFrame / 30f); source.Evaluate(motion, seconds * 30);
        // Parent-first solving avoids counting MMD pelvis rotation twice.
        foreach (var b in bindings)
        {
            if (b.live == null) return false;
            b.sample.localRotation = (Quaternion.Inverse(b.sample.parent.rotation) * source.byName[b.source].node.rotation * b.neutral).normalized;
            if (b.write) b.live.localRotation = b.sample.localRotation;
        }
        if (definition.WriteHipTranslation && bindings.First(b => b.path == "Armature/Hips").write)
        {
            float vertical = (source.byName["下半身"].node.position.y - originHeight) * scale;
            hip.localPosition = savedHip + hip.parent.InverseTransformVector(liveRoot.rotation * Vector3.up * vertical);
        }
        foreach (var m in morphs) if (m.renderer != null) m.renderer.SetBlendShapeWeight(m.index, motion.Morph(m.source, seconds));
        return true;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var b in bindings) if (b.write && b.live != null) b.live.localRotation = b.saved;
        if (hip != null && definition.WriteHipTranslation && bindings.Any(b => b.path == "Armature/Hips" && b.write)) hip.localPosition = savedHip;
        foreach (var m in morphs) if (m.renderer != null) m.renderer.SetBlendShapeWeight(m.index, m.saved);
        source?.Dispose(); source = null; if (proxy != null) UnityEngine.Object.Destroy(proxy); proxy = null;
    }
}
