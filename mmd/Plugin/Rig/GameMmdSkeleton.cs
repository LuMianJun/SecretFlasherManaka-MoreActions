using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace SFM.Mmd;

// A synthetic MMD topology, sized from this avatar's mesh bind poses. Its
// control nodes are solver-only: the live hierarchy is never modified.
internal static class GameMmdSkeleton
{
    // Share the 0.08 MMD displacement unit with the default camera. Rest positions
    // are converted to MMD units before IK, so keys and link lengths agree.
    internal const float MetersPerMmdUnit = 0.08f;
    // UnityVMDPlayer.EnforceInitialPose: mirror a 30-degree upper-arm tilt.
    // Rotate all descendants around the shoulder joint, preserving bone lengths.
    internal const float APoseUpperArmDegrees = 30f;
    internal static IEnumerable<string> SupportedNames()
    {
        foreach (var name in new[] { "全ての親", "センター", "グルーブ", "腰", "下半身", "上半身", "上半身2", "上半身3", "首", "頭" }) yield return name;
        foreach (var side in new[] { "左", "右" })
        {
            yield return "腰キャンセル"+side;
            foreach (var name in new[] { "肩P", "肩", "肩C", "腕", "腕捩", "ひじ", "手捩", "手首", "足", "ひざ", "足首", "つま先", "足IK親", "足ＩＫ", "つま先ＩＫ", "足D", "ひざD", "足首D", "足先EX" }) yield return side + name;
            foreach (var finger in new[] { "親指", "人指", "中指", "薬指", "小指" })
                for (int i = 0; i < 3; i++) yield return side + finger + (char)('０' + i + (finger == "親指" ? 0 : 1));
        }
    }

    internal static MmdSkeleton Create(IReadOnlyDictionary<string, Matrix4x4> bindPoses, bool createTransforms = true, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var positions = new Dictionary<string, NVector>();
        foreach (var pair in bindPoses) { Vector3 p = pair.Value.GetColumn(3); positions.Add(pair.Key, new NVector(p.x, p.y, p.z)); }
        var rig = CreateData(positions, referencePose);
        try { if (createTransforms) rig.CreateTransforms(); return rig; }
        catch { rig.Dispose(); throw; }
    }
    // Only managed numerics and data: suitable for a worker after snapshotting.
    internal static MmdSkeleton CreateData(IReadOnlyDictionary<string, NVector> positions, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        if (!Enum.IsDefined(typeof(MmdReferencePose), referencePose)) throw new ArgumentOutOfRangeException(nameof(referencePose));
        var rig = new MmdSkeleton();
        var referencePositions=new Dictionary<string,NVector>(StringComparer.Ordinal);
        var indexes=new Dictionary<string,int>(StringComparer.Ordinal);
        const string hipsPath = "Armature/Hips";
        const string chestPath = hipsPath + "/Spine/Chest";
        NVector Position(string path)
        {
            if(referencePositions.TryGetValue(path,out var cached))return cached;
            if (!positions.TryGetValue(path, out var p)) throw new ArgumentException("Game reference bind pose missing: " + path);
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) throw new ArgumentException("Nonfinite game reference bind pose: " + path);
            p /= MetersPerMmdUnit;
            if (referencePose == MmdReferencePose.A)
            {
                foreach (var suffix in new[] { "_L", "_R" })
                {
                    string armPath = chestPath + "/Shoulder" + suffix + "/UpperArm" + suffix;
                    if (!path.StartsWith(armPath + "/", StringComparison.Ordinal)) continue;
                    var pivot = Position(armPath);
                    float radians = (suffix == "_L" ? 1 : -1) * APoseUpperArmDegrees * (float)(Math.PI / 180);
                    var rotation = System.Numerics.Quaternion.CreateFromAxisAngle(NVector.UnitZ, radians);
                    p = pivot + NVector.Transform(p - pivot, rotation);
                    break;
                }
            }
            referencePositions.Add(path,p);return p;
        }
        int Index(string name) => indexes[name];
        void Add(string name, string parent, NVector position, int type = 0)
        {
            var bone = new MmdSkeleton.Bone { name = name, parent = parent == null ? -1 : Index(parent), tail = -1, type = type, rest = new Vector3 { x = position.X, y = position.Y, z = position.Z } };
            rig.bones.Add(bone); rig.byName.Add(name, bone);
            indexes.Add(name,rig.bones.Count-1);
        }
        void Tail(string name, string tail) => rig.byName[name].tail = Index(tail);
        void Append(string name,string source,float ratio=1,bool afterIk=false)
        { var b=rig.byName[name];b.append=Index(source);b.appendRatio=ratio;b.afterIk=afterIk; }
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
            Append(side+"肩C",side+"肩P",-1);
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
            Add("腰キャンセル"+side,"下半身",leg);Append("腰キャンセル"+side,"腰",-1);
            Add(side + "足", "腰キャンセル"+side, leg);
            Add(side + "ひざ", side + "足", knee);
            Add(side + "足首", side + "ひざ", foot);
            Tail(side + "足", side + "ひざ"); Tail(side + "ひざ", side + "足首");
            Add(side + "足IK親", "全ての親", foot);
            Add(side + "足ＩＫ", side + "足IK親", foot);
            // Bind poses can already contain several degrees of knee bend.
            // Reserve that angle so the physical chain stays below 180 degrees.
            var upper=knee-leg;var lower=foot-knee;
            float restBend=(float)(System.Math.Atan2(NVector.Cross(upper,lower).X,(double)upper.Y*lower.Y+(double)upper.Z*lower.Z)*180/System.Math.PI);
            float kneeMinimum=(float)System.Math.Clamp(.1-restBend,-179.9,179.8);
            float kneeMaximum=(float)System.Math.Clamp(179.9-restBend,1,179.9);
            rig.iks.Add(new MmdSkeleton.Ik { target = Index(side + "足ＩＫ"), effector = Index(side + "足首"), links = new[] { Index(side + "ひざ"), Index(side + "足") }, iterations = 40, limit = 45f, limitKnee = true, kneeMax = kneeMaximum, kneeMin = kneeMinimum });
            Add(side+"足D","腰キャンセル"+side,leg);Append(side+"足D",side+"足",1,true);
            Add(side+"ひざD",side+"足D",knee);Append(side+"ひざD",side+"ひざ",1,true);
            Add(side+"足首D",side+"ひざD",foot);Append(side+"足首D",side+"足首",1,true);
            string toePath = footPath + "/Toe" + suffix;
            if (positions.ContainsKey(toePath))
            {
                var toe = Position(toePath);
                if ((toe - foot).LengthSquared() < 0.000001f) throw new ArgumentException("Degenerate game reference toe: " + side);
                Add(side + "つま先", side + "足首", toe); Tail(side + "足首", side + "つま先");
                Add(side + "つま先ＩＫ", side + "足ＩＫ", toe);
                rig.iks.Add(new MmdSkeleton.Ik { target = Index(side + "つま先ＩＫ"), effector = Index(side + "つま先"), links = new[] { Index(side + "足首") }, iterations = 20, limit = 45f });
                // The game has a toe joint, not the original model's weighted
                // forefoot pivot. Adapt EX to that real joint; do not rotate the
                // whole ankle or invent another length in the live skeleton.
                Add(side+"足先EX",side+"足首D",toe);
                Append(side+"足先EX",side+"つま先",1,true);
            }
        }
        var map=MmdBoneMap.Create(positions.Keys);
        const string headPath=chestPath+"/Neck/Head";
        var eyes=new[]{headPath+"/LeftEye",headPath+"/RightEye"}.Where(positions.ContainsKey).ToArray();
        if(eyes.Length>0)Add("両目","頭",eyes.Select(Position).Aggregate(NVector.Zero,(a,b)=>a+b)/eyes.Length);
        foreach(var pair in map.OrderBy(p=>p.Key.Count(c=>c=='/')).ThenBy(p=>p.Key,StringComparer.Ordinal))
        {
            if(rig.byName.ContainsKey(pair.Value))continue;
            string parentPath=pair.Key.Substring(0,pair.Key.LastIndexOf('/'));
            while(parentPath.Contains('/')&&(!map.TryGetValue(parentPath,out var candidate)||!rig.byName.ContainsKey(candidate)))parentPath=parentPath.Substring(0,parentPath.LastIndexOf('/'));
            if(!map.TryGetValue(parentPath,out var parent)||!rig.byName.ContainsKey(parent))continue;
            Add(pair.Value,pair.Value=="左目"||pair.Value=="右目"?"両目":parent,Position(pair.Key));
            foreach(var side in new[]{"左","右"})
            {
                string suffix=side=="左"?"_L":"_R";
                if(pair.Value=="UpperArm_twist"+suffix)Append(pair.Value,side+"腕捩",1,true);
                if(pair.Value=="LowerArm_twist"+suffix)Append(pair.Value,side+"手捩",1,true);
            }
        }
        return rig;
    }
}
