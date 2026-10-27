using System;
using System.Collections.Generic;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace Seleka2.Mmd;

// A synthetic MMD topology, sized from this avatar's mesh bind poses. Its
// control nodes are solver-only: the live hierarchy is never modified.
internal static class GameMmdSkeleton
{
    // Share the 0.08 MMD displacement unit with the default camera. Rest positions
    // are converted to MMD units before IK, so keys and link lengths agree.
    internal const float MetersPerMmdUnit = 0.08f;
    internal static IEnumerable<string> SupportedNames()
    {
        foreach (var name in new[] { "全ての親", "センター", "グルーブ", "腰", "下半身", "上半身", "上半身2", "上半身3", "首", "頭" }) yield return name;
        foreach (var side in new[] { "左", "右" })
        {
            foreach (var name in new[] { "肩P", "肩", "肩C", "腕", "腕捩", "ひじ", "手捩", "手首", "足", "ひざ", "足首", "つま先", "足IK親", "足ＩＫ", "つま先ＩＫ" }) yield return side + name;
            foreach (var finger in new[] { "親指", "人指", "中指", "薬指", "小指" })
                for (int i = 0; i < 3; i++) yield return side + finger + (char)('０' + i + (finger == "親指" ? 0 : 1));
        }
    }

    internal static MmdSkeleton Create(IReadOnlyDictionary<string, Matrix4x4> bindPoses, bool createTransforms = true)
    {
        var positions = new Dictionary<string, NVector>();
        foreach (var pair in bindPoses) { Vector3 p = pair.Value.GetColumn(3); positions.Add(pair.Key, new NVector(p.x, p.y, p.z)); }
        var rig = CreateData(positions);
        try { if (createTransforms) rig.CreateTransforms(); return rig; }
        catch { rig.Dispose(); throw; }
    }
    // Only managed numerics and data: suitable for a worker after snapshotting.
    internal static MmdSkeleton CreateData(IReadOnlyDictionary<string, NVector> positions)
    {
        var rig = new MmdSkeleton();
        NVector Position(string path)
        {
            if (!positions.TryGetValue(path, out var p)) throw new ArgumentException("Game reference bind pose missing: " + path);
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) throw new ArgumentException("Nonfinite game reference bind pose: " + path);
            return p / MetersPerMmdUnit;
        }
        int Index(string name) => rig.bones.IndexOf(rig.byName[name]);
        void Add(string name, string parent, NVector position, int type = 0)
        {
            var bone = new MmdSkeleton.Bone { name = name, parent = parent == null ? -1 : Index(parent), tail = -1, type = type, rest = new Vector3 { x = position.X, y = position.Y, z = position.Z } };
            rig.bones.Add(bone); rig.byName.Add(name, bone);
        }
        void Tail(string name, string tail) => rig.byName[name].tail = Index(tail);
        const string hipsPath = "Armature/Hips";
        const string chestPath = hipsPath + "/Spine/Chest";
        var hips = Position(hipsPath); var spine = Position(hipsPath + "/Spine"); var chest = Position(chestPath);
        Add("全ての親", null, NVector.Zero);
        Add("センター", "全ての親", hips);
        Add("グルーブ", "センター", hips);
        Add("腰", "グルーブ", hips);
        Add("下半身", "腰", hips);
        Add("上半身", "腰", spine);
        Add("上半身2", "上半身", NVector.Lerp(spine, chest, 0.5f));
        Add("上半身3", "上半身2", chest);
        Add("首", "上半身3", Position(chestPath + "/Neck"));
        Add("頭", "首", Position(chestPath + "/Neck/Head"));
        foreach (var side in new[] { "左", "右" })
        {
            string suffix = side == "左" ? "_L" : "_R";
            string shoulderPath = chestPath + "/Shoulder" + suffix;
            string armPath = shoulderPath + "/UpperArm" + suffix;
            string elbowPath = armPath + "/LowerArm" + suffix;
            string handPath = elbowPath + "/Hand" + suffix;
            var shoulder = Position(shoulderPath); var arm = Position(armPath); var elbow = Position(elbowPath); var hand = Position(handPath);
            Add(side + "肩P", "上半身3", shoulder);
            Add(side + "肩", side + "肩P", shoulder);
            Add(side + "肩C", side + "肩", arm);
            Add(side + "腕", side + "肩C", arm);
            Add(side + "腕捩", side + "腕", arm, 8);
            Add(side + "ひじ", side + "腕捩", elbow);
            Add(side + "手捩", side + "ひじ", elbow, 8);
            Add(side + "手首", side + "手捩", hand);
            Tail(side + "肩", side + "腕"); Tail(side + "腕", side + "ひじ"); Tail(side + "腕捩", side + "ひじ");
            Tail(side + "ひじ", side + "手首"); Tail(side + "手捩", side + "手首");
            foreach (var finger in new[] { ("Thumb", "親指"), ("Index", "人指"), ("Middle", "中指"), ("Ring", "薬指"), ("Little", "小指") })
            {
                string path = handPath, parent = side + "手首";
                for (int i = 0; i < 3; i++)
                {
                    path += "/" + finger.Item1 + new[] { "Proximal", "Intermediate", "Distal" }[i] + suffix;
                    string name = side + finger.Item2 + (char)('０' + i + (finger.Item1 == "Thumb" ? 0 : 1));
                    Add(name, parent, Position(path));
                    if (i > 0) Tail(parent, name);
                    parent = name;
                }
            }
            string legPath = hipsPath + "/UpperLeg" + suffix;
            string kneePath = legPath + "/LowerLeg" + suffix;
            string footPath = kneePath + "/Foot" + suffix;
            var leg = Position(legPath); var knee = Position(kneePath); var foot = Position(footPath);
            if ((leg - knee).LengthSquared() < 0.000001f || (knee - foot).LengthSquared() < 0.000001f) throw new ArgumentException("Degenerate game reference leg: " + side);
            Add(side + "足", "下半身", leg);
            Add(side + "ひざ", side + "足", knee);
            Add(side + "足首", side + "ひざ", foot);
            Tail(side + "足", side + "ひざ"); Tail(side + "ひざ", side + "足首");
            Add(side + "足IK親", "全ての親", foot);
            Add(side + "足ＩＫ", side + "足IK親", foot);
            // Bind poses can already contain several degrees of knee bend.
            // Reserve that angle so the physical chain stays below 180 degrees.
            var upper=knee-leg;var lower=foot-knee;
            float restBend=(float)(System.Math.Atan2(NVector.Cross(upper,lower).X,NVector.Dot(upper,lower))*180/System.Math.PI);
            float kneeMinimum=(float)System.Math.Clamp(.1-restBend,-179.9,179.8);
            float kneeMaximum=(float)System.Math.Clamp(179.9-restBend,1,179.9);
            rig.iks.Add(new MmdSkeleton.Ik { target = Index(side + "足ＩＫ"), effector = Index(side + "足首"), links = new[] { Index(side + "ひざ"), Index(side + "足") }, iterations = 40, limit = 45f, limitKnee = true, kneeMax = kneeMaximum, kneeMin = kneeMinimum });
            string toePath = footPath + "/Toe" + suffix;
            if (positions.ContainsKey(toePath))
            {
                var toe = Position(toePath);
                if ((toe - foot).LengthSquared() < 0.000001f) throw new ArgumentException("Degenerate game reference toe: " + side);
                Add(side + "つま先", side + "足首", toe); Tail(side + "足首", side + "つま先");
                Add(side + "つま先ＩＫ", side + "足ＩＫ", toe);
                rig.iks.Add(new MmdSkeleton.Ik { target = Index(side + "つま先ＩＫ"), effector = Index(side + "つま先"), links = new[] { Index(side + "足首") }, iterations = 20, limit = 45f });
            }
        }
        return rig;
    }
}
