using System.Text.Json;
using UnityEngine;
using SFM.Mmd;

// Independent iterative reference for diagnostic traces, never loaded in game.
// Only converged reference results can support a directional comparison; neither
// this nor the adapted FK input is the original author's model/finished pose.
internal static class PoseTraceReplay
{
    static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    static Quaternion Q(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle(),e[3].GetSingle());
    static float[] Array(Vector3 v)=>[v.x,v.y,v.z];
    internal static MmdSkeleton Rig(JsonElement header)
    {
        var rig=new MmdSkeleton();
        foreach(var b in header.GetProperty("bones").EnumerateArray())
        {
            var bone=new MmdSkeleton.Bone{name=b.GetProperty("name").GetString(),parent=b.GetProperty("parent").GetInt32(),
                tail=b.GetProperty("tail").GetInt32(),type=b.GetProperty("type").GetInt32(),rest=V(b.GetProperty("rest"))};
            rig.bones.Add(bone);rig.byName.Add(bone.name,bone);
            if(b.TryGetProperty("append",out var append))
            {bone.append=append.GetInt32();bone.appendRatio=b.GetProperty("appendRatio").GetSingle();bone.afterIk=b.GetProperty("afterIk").GetBoolean();}
        }
        foreach(var i in header.GetProperty("iks").EnumerateArray())rig.iks.Add(new(){target=i.GetProperty("target").GetInt32(),
            effector=i.GetProperty("effector").GetInt32(),links=i.GetProperty("links").EnumerateArray().Select(x=>x.GetInt32()).ToArray(),
            iterations=i.GetProperty("iterations").GetInt32(),limit=i.GetProperty("limit").GetSingle(),limitKnee=i.GetProperty("limitKnee").GetBoolean(),
            kneeMin=i.GetProperty("kneeMin").GetSingle(),kneeMax=i.GetProperty("kneeMax").GetSingle()});
        rig.CreateTransforms();return rig;
    }
    static void ReferenceCcd(MmdSkeleton rig)
    {
        foreach(var ik in rig.iks)
        {
            var effector=rig.bones[ik.effector].node;var goal=rig.bones[ik.target].node;
            if(ik.limitKnee)foreach(int index in ik.links)
                if(rig.bones[index].name.Contains("ひざ"))rig.bones[index].node.localRotation=MmdSkeleton.LimitKnee(rig.bones[index].node.localRotation,Quaternion.identity,ik.kneeMax,ik.kneeMin);
            for(int step=0;step<800;step++)
            {
                bool moved=false;
                foreach(int index in ik.links)
                {
                    var link=rig.bones[index].node;var inverse=Quaternion.Inverse(link.rotation);
                    var from=(inverse*(effector.position-link.position)).normalized;var to=(inverse*(goal.position-link.position)).normalized;
                    var cross=Vector3.Cross(from,to);float sine=cross.magnitude;
                    float angle=(float)(Math.Atan2(sine,Vector3.Dot(from,to))*180/Math.PI);
                    if(angle<.001f||sine<1e-8f)continue;
                    var previous=link.localRotation;
                    var candidate=(previous*Quaternion.AngleAxis(Math.Min(angle,ik.limit),cross/sine)).normalized;
                    if(ik.limitKnee&&rig.bones[index].name.Contains("ひざ"))candidate=MmdSkeleton.LimitKnee(candidate,previous,ik.kneeMax,ik.kneeMin);
                    link.localRotation=candidate;moved=true;
                }
                if(!moved||Vector3.Distance(effector.position,goal.position)<.001f)break;
            }
        }
    }
    internal static int Analyze(string input,string output)
    {
        var headers=new Dictionary<string,JsonElement>();var rows=new List<object>();var lateSamples=new List<JsonElement>();
        int malformed=0;double replayMax=0;
        foreach(string line in File.ReadLines(input))
        {
            int h=line.IndexOf("MMD_POSE_RIG ",StringComparison.Ordinal),s=line.IndexOf("MMD_POSE_SAMPLE ",StringComparison.Ordinal),l=line.IndexOf("MMD_POSE_LATE ",StringComparison.Ordinal);
            if(h<0&&s<0&&l<0)continue;
            JsonDocument doc;
            try{doc=JsonDocument.Parse(line.Substring(h>=0?h+"MMD_POSE_RIG ".Length:s>=0?s+"MMD_POSE_SAMPLE ".Length:l+"MMD_POSE_LATE ".Length));}
            catch(JsonException){malformed++;continue;}
            using(doc)
            {
                var sample=doc.RootElement;string trace=sample.GetProperty("traceId").GetString();
                if(sample.GetProperty("schema").GetInt32()!=1)throw new InvalidDataException("Unsupported trace schema");
                if(h>=0){headers[trace]=sample.Clone();continue;}
                if(l>=0){lateSamples.Add(sample.Clone());continue;}
                if(!headers.TryGetValue(trace,out var header))throw new InvalidDataException("Missing rig header for trace "+trace);
                using var analytic=Rig(header);using var ccd=Rig(header);using var legOnly=Rig(header);
                float scale=header.GetProperty("scale").GetSingle();var keys=new MmdMotion{ExactBoneSamples=true};
                var recorded=sample.GetProperty("bones").EnumerateArray().ToArray();
                if(recorded.Length!=analytic.bones.Count)throw new InvalidDataException("Incomplete bone sample");
                for(int i=0;i<recorded.Length;i++)
                {
                    var b=analytic.bones[i];var rest=b.rest-(b.parent<0?Vector3.zero:analytic.bones[b.parent].rest);
                    bool raw=recorded[i].TryGetProperty("inputRotation",out var inputRotation);
                    keys.tracks[b.name]=[new(){q=raw?Q(inputRotation):Q(recorded[i].GetProperty("fkLocalRotation")),p=raw?V(recorded[i].GetProperty("inputPosition")):V(recorded[i].GetProperty("fkLocalPosition"))-rest,interpolation=new byte[64]}];
                }
                var active=sample.GetProperty("activeIkTargets").EnumerateArray().Select(x=>x.GetString()).ToHashSet();
                analytic.iks.RemoveAll(i=>!active.Contains(analytic.bones[i.target].name));
                ccd.iks.RemoveAll(i=>!active.Contains(ccd.bones[i.target].name));
                legOnly.iks.RemoveAll(i=>!i.limitKnee||!active.Contains(legOnly.bones[i.target].name));
                analytic.useIk=true;analytic.Evaluate(keys,0);ccd.useIk=false;ccd.Evaluate(keys,0);ReferenceCcd(ccd);
                legOnly.useIk=true;legOnly.Evaluate(keys,0);
                double replay=analytic.bones.Select((b,i)=>(double)Vector3.Distance(b.node.position,V(recorded[i].GetProperty("solvedPosition")))*scale).Max();
                replayMax=Math.Max(replayMax,replay);
                var legs=new List<object>();var toes=new List<object>();
                var rootRotation=Q(sample.GetProperty("rootRotation"));var liveHip=V(sample.GetProperty("liveHip"));
                Vector3 World(MmdSkeleton rig,Vector3 p)=>liveHip+rootRotation*(p-rig.byName["下半身"].node.position)*scale;
                foreach(var ik in analytic.iks.Where(i=>i.limitKnee))
                {
                    var a=analytic.bones[ik.links[1]].node;var k=analytic.bones[ik.links[0]].node;var e=analytic.bones[ik.effector].node;var t=analytic.bones[ik.target].node;
                    var ca=ccd.bones[ik.links[1]].node;var ck=ccd.bones[ik.links[0]].node;var ce=ccd.bones[ik.effector].node;
                    Vector3 Pole(Transform hip,Transform knee,Transform ankle){var axis=(ankle.position-hip.position).normalized;var v=knee.position-hip.position;return v-axis*Vector3.Dot(v,axis);}
                    var pole=Pole(a,k,e);var cp=Pole(ca,ck,ce);
                    float ccdError=Vector3.Distance(ce.position,t.position)*scale;
                    legs.Add(new{target=analytic.bones[ik.target].name,analyticGoalErrorM=Vector3.Distance(e.position,t.position)*scale,
                        ccdGoalErrorM=ccdError,referenceConverged=ccdError<.001f,
                        poleWellDefined=Math.Min(pole.magnitude,cp.magnitude)*scale>.015f,poleAngleDegrees=Vector3.Angle(pole,cp),
                        kneeDifferenceM=Vector3.Distance(k.position,ck.position)*scale,
                        analyticKneeWorld=Array(World(analytic,k.position)),ccdKneeWorld=Array(World(ccd,ck.position)),
                        analyticAnkleWorld=Array(World(analytic,e.position)),ccdAnkleWorld=Array(World(ccd,ce.position))});
                }
                foreach(var ik in analytic.iks.Where(i=>!i.limitKnee))
                {
                    var e=analytic.bones[ik.effector].node;var t=analytic.bones[ik.target].node;
                    var ce=ccd.bones[ik.effector].node;
                    var foot=analytic.bones[ik.links[0]].node;var cfoot=ccd.bones[ik.links[0]].node;var lfoot=legOnly.bones[ik.links[0]].node;
                    toes.Add(new{target=analytic.bones[ik.target].name,analyticToeWorld=Array(World(analytic,e.position)),
                        ccdToeWorld=Array(World(ccd,ce.position)),goalWorld=Array(World(analytic,t.position)),
                        // A transported bind-up vector, not a measured sole
                        // normal. It exposes roll left unconstrained by a
                        // one-link positional toe target.
                        analyticAnkleRotation=new[]{foot.rotation.x,foot.rotation.y,foot.rotation.z,foot.rotation.w},
                        ccdAnkleRotation=new[]{cfoot.rotation.x,cfoot.rotation.y,cfoot.rotation.z,cfoot.rotation.w},
                        analyticBindUpHeading=Array(foot.rotation*Vector3.up),ccdBindUpHeading=Array(cfoot.rotation*Vector3.up),
                        beforeToeIkBindUpHeading=Array(lfoot.rotation*Vector3.up),
                        analyticGoalErrorM=Vector3.Distance(e.position,t.position)*scale,ccdGoalErrorM=Vector3.Distance(ce.position,t.position)*scale});
                }
                var live=sample.GetProperty("live").EnumerateArray().Where(b=>b.GetProperty("controlled").GetBoolean()).ToArray();
                rows.Add(new{id=sample.GetProperty("id").GetString(),seconds=sample.GetProperty("seconds").GetSingle(),
                    analyticReplayErrorM=replay,legs,toes,
                    liveErrors=live.Select(b=>new{name=b.GetProperty("name").GetString(),errorM=b.GetProperty("retargetErrorM").GetSingle()}).ToArray(),
                    sceneQueryConfigured=sample.GetProperty("sceneQueryConfigured").GetBoolean(),segments=sample.GetProperty("segments").Clone()});
            }
        }
        if(rows.Count==0)throw new InvalidDataException("No MMD_POSE_SAMPLE records found; use a historical trace captured by an older diagnostic build.");
        File.WriteAllText(output,JsonSerializer.Serialize(new{schema=1,rigs=headers.Count,samples=rows.Count,malformedLines=malformed,
            analyticReplayMaxM=replayMax,referenceMeaning="independent converged CCD comparison, not author ground truth",rows,lateSamples}));
        Console.WriteLine($"POSE_TRACE rigs={headers.Count} samples={rows.Count} malformed={malformed} replayMax={replayMax:F8}m output={output}");
        return rows.Count;
    }
}
