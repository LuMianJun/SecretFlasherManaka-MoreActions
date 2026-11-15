using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SFM.Actions;
using SFM.Mmd;
using SFM.Mmd.Upstream;
using UnityEngine;

// Experimental attribution only: isolate each game extension while retaining
// the same effective standard reader input and full playback transfer path.
internal static class ExtensionGroupComparison
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] GroupIds=["upper3","waist/cancel","shoulderP/C","arm/hand twist","thumb0","D/EX","eyes","native"];
    internal static void Run(string[] inputFiles,JsonElement fixture,string outputPath)
    {
        var cases=new List<object>();
        var files=inputFiles.OrderBy(Priority).ThenBy(p=>p,StringComparer.Ordinal).ToArray();
        foreach(var path in files)
        {
            var source=UpstreamInputCompatibility.Apply(MmdMotion.Read(path));
            string temporary=Path.Combine(Path.GetTempPath(),"sfm-extension-groups-"+Guid.NewGuid().ToString("N")+".vmd");
            CompatibleVmd.Write(path,temporary);
            try
            {
                foreach(var reference in new[]{MmdReferencePose.A,MmdReferencePose.T})
                {
                    MmdRigCache.Clear();
                    using var originalActor=new NativeBundleRegression.NativeActor(fixture,1.125f);
                    var rig=MmdRigCache.Rent(originalActor.Root,reference);
                    try
                    {
                        using var original=new Original(temporary,rig,reference);
                        original.Evaluate(0);
                        var correction=rig.sampleHip.position-rig.ReferenceHipPosition;correction.y=0;
                        var paths=rig.bindings.Select(b=>b.path).ToArray();
                        var names=NativeNames(rig);
                        var groups=GroupIds.Select(id=>CreateGroup(id,source,names)).ToArray();
                        int limit=original.FrameCount;
                        if(limit<=0)throw new Exception("No playable upstream standard input: "+path);
                        var times=Frames(limit).Select(frame=>new Query(frame,frame/30f)).ToArray();
                        var snapshots=new List<Snapshot>(times.Length);
                        var fullDifferences=Records(paths);
                        using(var fullActor=new NativeBundleRegression.NativeActor(fixture,1.125f))
                        using(var full=new RuntimeVmdPose(fullActor.Root,Action("groups-full"),source,reference))
                        {
                            CheckReaderFrameCount(full,limit);
                            foreach(var query in times)
                            {
                                original.Evaluate(query.Seconds*30f);full.Apply(query.Seconds);
                                var baseline=rig.bindings.Select(b=>new Point(b.sample.position,b.sample.rotation)).ToArray();
                                var result=Capture(fullActor,full,paths,correction);
                                Compare(query.Frame,result,baseline,paths,fullDifferences);
                                snapshots.Add(new(query,baseline,result));
                            }
                        }
                        var measured=new List<object>();
                        foreach(var group in groups.Where(g=>g.ActiveTracks.Length>0))
                        {
                            var selected=SelectGroup(source,group.Tracks);
                            using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
                            using var playback=new RuntimeVmdPose(actor.Root,Action("groups-"+group.Id),selected,reference);
                            CheckReaderFrameCount(playback,limit);
                            var versusOriginal=Records(paths);var versusFull=Records(paths);
                            foreach(var sample in snapshots)
                            {
                                playback.Apply(sample.Query.Seconds);
                                var actual=Capture(actor,playback,paths,correction);
                                Compare(sample.Query.Frame,actual,sample.Original,paths,versusOriginal);
                                Compare(sample.Query.Frame,actual,sample.Full,paths,versusFull);
                            }
                            var result=new{group=group.Id,tracks=group.Tracks,activeTracks=group.ActiveTracks,
                                queries=snapshots.Count,parityPassed=(bool?)null,comparisonCompleted=true,
                                versusUpstream=Describe(versusOriginal),versusFull=Describe(versusFull)};
                            measured.Add(result);
                            Console.WriteLine($"EXTENSION_GROUP MEASURED motion={Path.GetFileName(Path.GetDirectoryName(path))} reference={reference} group={group.Id} tracks={group.Tracks.Length} maxP={versusOriginal.Values.Max(d=>d.PositionM):R} maxQ={versusOriginal.Values.Max(d=>d.RotationDegrees):R} vsFullP={versusFull.Values.Max(d=>d.PositionM):R} vsFullQ={versusFull.Values.Max(d=>d.RotationDegrees):R}");
                        }
                        var assigned=groups.SelectMany(g=>g.Tracks).ToHashSet(StringComparer.Ordinal);
                        cases.Add(new{motion=Path.GetFileName(Path.GetDirectoryName(path)),sourceSha256=Hash(path),effectiveInputSha256=Hash(temporary),reference=reference.ToString(),
                            heightMultiplier=1.125,bones=paths.Length,readerFrameCount=limit,queries=times.Length,firstFrameControllerOriginCorrection=V(correction),
                            fullVersusUpstream=Describe(fullDifferences),groups=measured,
                            skippedGroups=groups.Where(g=>g.ActiveTracks.Length==0).Select(g=>new{group=g.Id,tracks=g.Tracks,reason="No active selected extension controls; exact neutral detection, not a magnitude threshold."}).ToArray(),
                            unassignedActiveTracks=source.tracks.Where(p=>!UpstreamMotionSupport.Contains(p.Key)&&!assigned.Contains(p.Key)&&Active(p.Value)).Select(p=>p.Key).OrderBy(n=>n,StringComparer.Ordinal).ToArray()});
                        Write(outputPath,cases,files.Length*2,false);
                    }
                    finally{MmdRigCache.Return(rig);}
                }
            }
            finally{File.Delete(temporary);}
        }
        MmdRigCache.Clear();Write(outputPath,cases,files.Length*2,true);
    }
    static int Priority(string path)=>Path.GetFileName(Path.GetDirectoryName(path)) switch{"Shake"=>0,"盛装登场"=>1,"腰振りダンス"=>2,_=>3};
    static ActionDefinition Action(string id)=>new(){Id=id,WriteFaceMorphs=false,Loop=false};
    static IEnumerable<float> Frames(int limit)
    {
        var frames=new SortedSet<float>{0,limit};
        for(int frame=0;frame<limit;frame+=3){frames.Add(frame);if(frame+.375f<limit)frames.Add(frame+.375f);}
        return frames;
    }
    sealed record Group(string Id,string[] Tracks,string[] ActiveTracks);
    static Group CreateGroup(string id,MmdMotion source,Dictionary<string,string> nativeNames)
    {
        var names=source.tracks.Keys.Where(name=>InGroup(id,name,nativeNames)).ToHashSet(StringComparer.Ordinal);
        if(id=="thumb0")
        {
            foreach(string side in new[]{"左","右"})
                if(source.tracks.ContainsKey(side+"親指０"))
                    foreach(string suffix in new[]{"親指０","親指１","親指２"})
                        if(source.tracks.ContainsKey(side+suffix))names.Add(side+suffix);
        }
        var tracks=names.OrderBy(n=>n,StringComparer.Ordinal).ToArray();
        return new(id,tracks,tracks.Where(name=>Active(source.tracks[name])).ToArray());
    }
    static bool InGroup(string group,string name,Dictionary<string,string> nativeNames)
    {
        name=MmdBoneMap.Normalize(name);
        string semantic=SemanticGroup(name);
        if(semantic!=null)return semantic==group;
        return nativeNames.TryGetValue(name,out var nativeGroup)&&nativeGroup==group;
    }
    static string SemanticGroup(string name)
    {
        if(name=="上半身3")return "upper3";
        if(name=="腰"||name is "腰キャンセル左" or "腰キャンセル右")return "waist/cancel";
        if(name is "左肩P" or "右肩P" or "左肩C" or "右肩C")return "shoulderP/C";
        if(name is "左腕捩" or "右腕捩" or "左手捩" or "右手捩")return "arm/hand twist";
        if(name is "左親指０" or "右親指０")return "thumb0";
        if(name is "左足D" or "右足D" or "左ひざD" or "右ひざD" or "左足首D" or "右足首D" or "左足先EX" or "右足先EX")return "D/EX";
        if(name is "左目" or "右目" or "両目")return "eyes";
        return null;
    }
    static Dictionary<string,string> NativeNames(RuntimeVmdRig rig)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);var encoding=Encoding.GetEncoding(932);
        foreach(var b in rig.bindings)
        {
            string group=b.presetSource is "左目" or "右目"?"eyes":b.sample.name.Contains("Arm_twist",StringComparison.Ordinal)?"arm/hand twist":"native";
            foreach(string name in new[]{b.sample.name,b.presetSource})
            {
                if(string.IsNullOrEmpty(name)||UpstreamMotionSupport.Contains(name)||SemanticGroup(MmdBoneMap.Normalize(name))!=null)continue;
                result[MmdBoneMap.Normalize(name)]=group;
                var bytes=encoding.GetBytes(name);
                if(bytes.Length>15)result[MmdBoneMap.Normalize(encoding.GetString(bytes,0,15))]=group;
            }
        }
        return result;
    }
    static bool Active(List<MmdMotion.Key> keys)=>keys.Any(key=>
    {
        if(key.p.x!=0||key.p.y!=0||key.p.z!=0)return true;
        var q=key.q;float length=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
        q=length<1e-16f?Quaternion.identity:q.normalized;
        return q.x!=0||q.y!=0||q.z!=0||(q.w!=1&&q.w!=-1);
    });
    static MmdMotion SelectGroup(MmdMotion source,string[] selectedTracks)
    {
        var selected=selectedTracks.ToHashSet(StringComparer.Ordinal);
        var result=new MmdMotion{lastFrame=source.lastFrame,modelName=source.modelName,BodyInputCompatible=true};
        foreach(var pair in source.tracks.Where(p=>UpstreamMotionSupport.Contains(p.Key)||selected.Contains(p.Key)))result.tracks.Add(pair.Key,pair.Value);
        // All standard-reader inputs remain identical. Unknown raw names from
        // unselected native controls must not reintroduce their lookup aliases.
        result.RawBoneKeys.AddRange(source.RawBoneKeys.Where(key=>UnityVMDReader.VMDReader.BoneKeyFrameGroup.StringBoneNames.Contains(key.Name)||selected.Contains(MmdBoneMap.Normalize(key.Name))));
        result.RawIkFrames.AddRange(source.RawIkFrames);return result;
    }
    static void CheckReaderFrameCount(RuntimeVmdPose pose,int expected)
    {
        var solver=(UpstreamBodySolver)typeof(RuntimeVmdPose).GetField("solver",Flags).GetValue(pose);
        if(solver.Reader.FrameCount!=expected)throw new Exception($"Group reader FrameCount changed: expected={expected}, actual={solver.Reader.FrameCount}");
    }
    static Point[] Capture(NativeBundleRegression.NativeActor actor,RuntimeVmdPose pose,string[] paths,Vector3 correction)
    {
        var inverse=Quaternion.Inverse(actor.Root.transform.rotation);var root=actor.Root.transform.position;
        var travel=inverse*pose.RootDisplacement;
        return paths.Select(path=>new Point(inverse*(actor.Bones[path].position-root)+travel+correction,inverse*actor.Bones[path].rotation)).ToArray();
    }
    static Dictionary<string,Difference> Records(string[] paths)=>paths.ToDictionary(path=>path,_=>new Difference());
    static void Compare(float frame,Point[] actual,Point[] expected,string[] paths,Dictionary<string,Difference> records)
    {
        for(int i=0;i<paths.Length;i++)records[paths[i]].Add(frame,Vector3.Distance(actual[i].Position,expected[i].Position),Degrees(actual[i].Rotation,expected[i].Rotation));
    }
    static object Describe(Dictionary<string,Difference> records)=>new{maxPositionM=records.Values.Max(d=>d.PositionM),maxRotationDegrees=records.Values.Max(d=>d.RotationDegrees),
        changedBones=records.Count(p=>p.Value.ChangedSamples>0),differences=records};
    static void Write(string path,List<object> cases,int expectedCases,bool completed)
    {
        var result=new{schema=1,stage="extension-groups",fixedUpstreamCommit="2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8",productionDefault=true,
            scope="One active extension group at a time over the same standard input. Each group is compared to unchanged upstream and cached full-extension playback. Values measure attribution, not correctness.",
            input="Reader-recognized RawBoneKeys and RawIkFrames are preserved for identical standard input/FrameCount. Only selected group lookups and its unknown native raw keys are added, to avoid alias leakage from other native controls.",
            activeDetection="Same exact normalized quaternion and translation neutral test as MmdAdditionalTracks.Track.Active; no approximate animation threshold.",
            sampling="Every third source frame and n+0.375, supported endpoint; seconds=(frame/30f), effective frame=seconds*30f on both sides.",
            coordinateConvention="Same native heading removed and controller RootDisplacement reconstructed; fixed upstream first-frame horizontal origin only; no per-frame/bone alignment.",
            positionThresholdM=.0001,rotationThresholdDegrees=.05,expectedCases,completedCases=cases.Count,comparisonCompleted=completed,parityPassed=(bool?)null,cases};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
    static double Degrees(Quaternion a,Quaternion b)
    {
        var aa=a.normalized;var bb=b.normalized;
        double plus=Math.Pow(aa.x+bb.x,2)+Math.Pow(aa.y+bb.y,2)+Math.Pow(aa.z+bb.z,2)+Math.Pow(aa.w+bb.w,2);
        double minus=Math.Pow(aa.x-bb.x,2)+Math.Pow(aa.y-bb.y,2)+Math.Pow(aa.z-bb.z,2)+Math.Pow(aa.w-bb.w,2);
        return 4*Math.Asin(Math.Min(1,Math.Sqrt(Math.Min(plus,minus))/2))*180/Math.PI;
    }
    static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static float[] V(Vector3 value)=>[value.x,value.y,value.z];
    readonly record struct Query(float Frame,float Seconds);
    readonly record struct Point(Vector3 Position,Quaternion Rotation);
    sealed record Snapshot(Query Query,Point[] Original,Point[] Full);
}
