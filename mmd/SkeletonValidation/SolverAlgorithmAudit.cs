using System.Text.Json;
using UnityEngine;
using SFM.Mmd;

// Offline observations of production edge cases, not a pass/fail claim about
// the author's intended pose. No experimental solver runs inside the game.
internal static class SolverAlgorithmAudit
{
    static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    static Quaternion Q(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle(),e[3].GetSingle());
    static float[] A(Vector3 v)=>[v.x,v.y,v.z];
    internal static void Run(string input,string output)
    {
        JsonElement header=default,sample=default;double nearest=double.MaxValue;
        foreach(string line in File.ReadLines(input))
        {
            int start=line.IndexOf('{');if(start<0)continue;
            if(line.Contains("MMD_POSE_RIG ")){using var d=JsonDocument.Parse(line[start..]);header=d.RootElement.Clone();}
            if(!line.Contains("MMD_POSE_SAMPLE "))continue;
            using var s=JsonDocument.Parse(line[start..]);double delta=Math.Abs(s.RootElement.GetProperty("seconds").GetDouble()-11.590667);
            if(delta<nearest){nearest=delta;sample=s.RootElement.Clone();}
        }
        if(header.ValueKind==JsonValueKind.Undefined||sample.ValueKind==JsonValueKind.Undefined)throw new InvalidDataException("Missing captured rig/sample");
        MmdMotion Keys()
        {
            var result=new MmdMotion{ExactBoneSamples=true};var names=header.GetProperty("bones").EnumerateArray().ToArray();
            var data=sample.GetProperty("bones").EnumerateArray().ToArray();
            for(int i=0;i<names.Length;i++)result.tracks[names[i].GetProperty("name").GetString()]=[new(){q=Q(data[i].GetProperty("inputRotation")),p=V(data[i].GetProperty("inputPosition")),interpolation=new byte[64]}];
            return result;
        }
        using var a=PoseTraceReplay.Rig(header);using var b=PoseTraceReplay.Rig(header);a.useIk=b.useIk=true;
        var baseKeys=Keys();baseKeys.tracks["右つま先ＩＫ"][0].p=Vector3.zero;baseKeys.tracks["右つま先ＩＫ"][0].q=Quaternion.identity;
        var missing=Keys();missing.tracks.Remove("右つま先ＩＫ");
        a.Evaluate(baseKeys,0);b.Evaluate(missing,0);
        var unkeyed=new{meaning="explicit zero target key versus absent target track; both sample identity but chain activation differs",
            targetDifferenceM=Vector3.Distance(a.byName["右つま先ＩＫ"].node.position,b.byName["右つま先ＩＫ"].node.position)*header.GetProperty("scale").GetSingle(),
            ankleRotationDifferenceDegrees=Quaternion.Angle(a.byName["右足首"].node.rotation,b.byName["右足首"].node.rotation),
            toePositionDifferenceM=Vector3.Distance(a.byName["右つま先"].node.position,b.byName["右つま先"].node.position)*header.GetProperty("scale").GetSingle()};
        // A toe endpoint supplies a direction, leaving rotation around that
        // direction unobserved. Explicit ankle keys still carry that freedom.
        var rolled=Keys();rolled.tracks["右つま先ＩＫ"][0].p=Vector3.zero;rolled.tracks["右つま先ＩＫ"][0].q=Quaternion.identity;
        var axis=(a.byName["右つま先"].rest-a.byName["右足首"].rest).normalized;
        rolled.tracks["右足首"][0].q=rolled.tracks["右足首"][0].q*Quaternion.AngleAxis(90,axis);
        b.Evaluate(rolled,0);
        var rollFreedom=new{meaning="explicit ankle roll around its toe segment can preserve the same goal and endpoint",
            ankleRotationDifferenceDegrees=Quaternion.Angle(a.byName["右足首"].node.rotation,b.byName["右足首"].node.rotation),
            toePositionDifferenceM=Vector3.Distance(a.byName["右つま先"].node.position,b.byName["右つま先"].node.position)*header.GetProperty("scale").GetSingle()};
        using var singular=new MmdSkeleton{useIk=true};
        Vector3 upper=new(0,-.4f,.01f),lower=new(0,-.37f,-.02f);
        foreach(var entry in new[]{("左足",-1,Vector3.zero),("左ひざ",0,upper),("左足首",1,upper+lower),("左足ＩＫ",-1,upper+lower)})
        {var bone=new MmdSkeleton.Bone{name=entry.Item1,parent=entry.Item2,rest=entry.Item3,tail=-1};singular.bones.Add(bone);singular.byName.Add(bone.name,bone);}
        singular.iks.Add(new(){target=3,effector=2,links=[1,0],limitKnee=true,kneeMin=-3,kneeMax=160,iterations=40,limit=45});singular.CreateTransforms();
        var reverse=(upper+Quaternion.AngleAxis(80,new Vector3(1,0,0))*lower)*-1;var cases=new List<object>();Vector3? exact=null;
        foreach(float epsilon in new[]{0f,.000001f,-.000001f})
        {
            var motion=new MmdMotion();var target=reverse+new Vector3(epsilon,0,0);
            motion.tracks["左足ＩＫ"]=[new(){q=Quaternion.identity,p=target-singular.bones[3].rest,interpolation=new byte[64]}];singular.Evaluate(motion,0);
            var knee=singular.bones[1].node.position;exact??=knee;
            cases.Add(new{targetPerturbationM=epsilon,knee=A(knee),kneeDifferenceFromExactM=Vector3.Distance(knee,exact.Value),goalErrorM=Vector3.Distance(singular.bones[2].node.position,target)});
        }
        File.WriteAllText(output,JsonSerializer.Serialize(new{schema=1,capturedSeconds=sample.GetProperty("seconds").GetSingle(),unkeyed,rollFreedom,
            antipodal=new{meaning="synthetic half-turn singularity; not identified as a trigger in captured motions",cases}}));
        Console.WriteLine("SOLVER_AUDIT "+File.ReadAllText(output));
    }
}
