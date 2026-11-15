using System.Security.Cryptography;
using System.Text.Json;
using SFM.Actions;
using SFM.Mmd;
using SFM.Mmd.Upstream;
using UnityEngine;

// Optional copyrighted user fixtures stay outside the repository. Their names
// and hashes come from the preserved source manifest beside the six-motion set.
internal static class RealMotionCompatibilityRegression
{
    static readonly string[] Names=["Shake","addiction","盛装登场","腰振りダンス","越南鼓甜舞","queencard"];
    static readonly string[] Extensions=["腰","腰キャンセル左","腰キャンセル右","上半身3","両目","左目","右目",
        "左肩P","右肩P","左肩C","右肩C","左腕捩","右腕捩","左手捩","右手捩","左親指０","右親指０",
        "左足D","右足D","左ひざD","右ひざD","左足首D","右足首D","左足先EX","右足先EX"];
    readonly record struct State(Vector3 P,Quaternion Q,Vector3 S,Vector3 LocalP,Quaternion LocalQ,Vector3 LocalS)
    {
        internal State(Transform node):this(node.position,node.rotation,node.lossyScale,node.localPosition,node.localRotation,node.localScale){}
        internal bool WorldMatches(Transform node)=>Vector3.Distance(P,node.position)<.0001f&&Quaternion.Angle(Q,node.rotation)<.1f
            &&Vector3.Distance(S,node.lossyScale)<.0001f;
        internal bool LocalMatches(Transform node)=>Vector3.Distance(LocalP,node.localPosition)<.0001f
            &&Quaternion.Angle(LocalQ,node.localRotation)<.1f&&Vector3.Distance(LocalS,node.localScale)<.0001f;
    }
    static Dictionary<string,State> Snapshot(NativeBundleRegression.NativeActor actor)=>actor.Bones.ToDictionary(p=>p.Key,p=>new State(p.Value));
    static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
    static bool Finite(Quaternion q)=>float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)
        &&q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.5f;
    static bool Finite(Transform t)=>Finite(t.position)&&Finite(t.rotation)&&Finite(t.lossyScale)
        &&Finite(t.localPosition)&&Finite(t.localRotation)&&Finite(t.localScale);

    internal static void Run(Action<bool,string> check,string directory)
    {
        directory=Path.GetFullPath(directory);
        using var sources=JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory.GetParent(directory).FullName,"motion-sources.json")));
        var manifest=sources.RootElement.EnumerateArray().ToDictionary(p=>p.GetProperty("name").GetString(),StringComparer.Ordinal);
        check(manifest.Count==6&&Names.All(manifest.ContainsKey),"Real-motion manifest contains the six original user motions");
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","native-model-bind.json")));
        foreach(string name in Names)
        {
            string path=Path.Combine(directory,name,"motion.vmd");
            string sha=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            check(sha==manifest[name].GetProperty("sha256").GetString(),"Real-motion SHA256 matches the recorded original: "+name);
            var motion=MmdMotion.Read(path);
            check(motion.RawBoneKeys.Count>0&&motion.lastFrame>30,"Real-motion regression reads nonempty parsed VMD input: "+name);
            foreach(var reference in new[]{MmdReferencePose.A,MmdReferencePose.T})
                CheckMotion(check,document.RootElement,name,sha,motion,reference);
        }
        MmdRigCache.Clear();
    }

    static void CheckMotion(Action<bool,string> check,JsonElement fixture,string name,string sha,MmdMotion original,MmdReferencePose reference)
    {
        string label=name+"/"+reference;
        MmdRigCache.Clear();
        var compatible=UpstreamInputCompatibility.Apply(original);
        string[] consumed,unsupported;
        using(var actor=new NativeBundleRegression.NativeActor(fixture,1.125f))
        {
            var rig=MmdRigCache.Rent(actor.Root,reference);
            try
            {
                var bound=rig.BindMotion(original);
                var extensions=new MmdAdditionalTracks(rig,bound,new ActionDefinition());
                var eligible=new HashSet<string>(Extensions.Select(MmdBoneMap.Normalize),StringComparer.Ordinal);
                foreach(var binding in rig.bindings.Where(b=>!b.human.HasValue||!UpstreamBodySolver.StandardMapping.ContainsKey(b.human.Value)))
                {
                    eligible.Add(binding.sample.name);
                    if(binding.presetSource!=null)eligible.Add(MmdBoneMap.Normalize(binding.presetSource));
                }
                consumed=UpstreamMotionSupport.Tracks(bound).Select(p=>p.Key).Where(n=>!UpstreamMotionSupport.Contains(n)&&eligible.Contains(MmdBoneMap.Normalize(n)))
                    .Distinct().OrderBy(n=>n,StringComparer.Ordinal).ToArray();
                foreach(string track in consumed)
                    check(extensions.ContainsTrack(track)||bound.ResolvedAliases.Contains(track),"Actual extension source is consumed: "+label+" "+track);
                unsupported=UpstreamMotionSupport.Tracks(bound).Where(p=>!UpstreamMotionSupport.Contains(p.Key)&&!extensions.ContainsTrack(p.Key)
                    &&!bound.ResolvedAliases.Contains(p.Key)&&MmdBoneMap.Animated(p.Value)).Select(p=>p.Key).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
            }
            finally{MmdRigCache.Return(rig);}
        }
        MmdRigCache.Clear();
        using var fullActor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        using var coreActor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var native=Snapshot(fullActor);var root=new State(fullActor.Root.transform);var armature=new State(fullActor.Root.transform.Find("Armature"));
        var times=SampleFrames(name,compatible).Select(f=>f/30f).ToArray();
        // Full/core differences observe the added track features. Original
        // source parity is enforced separately by UpstreamValidation.
        float maxDifferenceP=0,maxDifferenceQ=0;
        int different=0,seekCases=0,fullHistoryCases=0,coreHistoryCases=0;
        var replay=new List<(float Seconds,Dictionary<string,State> Bones,Vector3 Displacement)>();
        using(var full=new RuntimeVmdPose(fullActor.Root,new ActionDefinition{Id="real-"+label,WriteFaceMorphs=false},original,reference))
        using(var core=new RuntimeVmdPose(coreActor.Root,new ActionDefinition{Id="core-"+label,WriteFaceMorphs=false},CoreOnly(compatible),reference))
        {
            void Record(float seconds)=>replay.Add((seconds,Snapshot(fullActor),full.RootDisplacement));
            foreach(float seconds in times)
            {
                check(full.Apply(seconds)&&core.Apply(seconds),"Actual motion samples through RuntimeVmdPose: "+label+" "+seconds);
                Record(seconds);
                check(fullActor.Bones.Values.All(Finite)&&Finite(full.RootDisplacement),"Actual full-body output stays finite: "+label+" "+seconds);
                check(root.LocalMatches(fullActor.Root.transform)&&armature.LocalMatches(fullActor.Root.transform.Find("Armature")),
                    "Actual motion leaves native root/Armature owned by the game: "+label+" "+seconds);
                var expected=Snapshot(fullActor);
                full.Apply(seconds);
                Record(seconds);
                check(expected.All(p=>p.Value.WorldMatches(fullActor.Bones[p.Key])),"Actual repeated sample does not accumulate: "+label+" "+seconds);
                foreach(var pair in fullActor.Bones)
                {
                    float distance=Vector3.Distance(pair.Value.position,coreActor.Bones[pair.Key].position);
                    float angle=Quaternion.Angle(pair.Value.rotation,coreActor.Bones[pair.Key].rotation);
                    maxDifferenceP=MathF.Max(maxDifferenceP,distance);maxDifferenceQ=MathF.Max(maxDifferenceQ,angle);
                    if(distance>.0005f||angle>.2f)different++;
                }
            }
            // The pinned upstream retains pose/cursor history. Exercise the
            // same backward/forward queries, record any inherited differences,
            // and verify the complete query sequence can be replayed below.
            foreach(float fraction in new[]{.73f,.19f,.91f,.07f,.51f})
            {
                float seconds=MathF.Max(0,(compatible.lastFrame-1)*fraction/30f);
                full.Apply(seconds);Record(seconds);core.Apply(seconds);var expected=Snapshot(fullActor);var expectedCore=Snapshot(coreActor);
                full.Apply(MathF.Max(0,seconds-.137f));Record(MathF.Max(0,seconds-.137f));full.Apply(seconds);Record(seconds);
                core.Apply(MathF.Max(0,seconds-.137f));core.Apply(seconds);
                var changes=expected.Where(p=>!p.Value.WorldMatches(fullActor.Bones[p.Key])).ToArray();
                var coreChanges=expectedCore.Where(p=>!p.Value.WorldMatches(coreActor.Bones[p.Key])).ToArray();
                if(changes.Length>0)fullHistoryCases++;
                if(coreChanges.Length>0)coreHistoryCases++;
                check(fullActor.Bones.Values.All(Finite)&&coreActor.Bones.Values.All(Finite),
                    "Actual backward/forward query outputs stay finite: "+label+" "+seconds);
                seekCases++;
                var repeated=Snapshot(fullActor);full.Apply(seconds);Record(seconds);
                check(repeated.All(p=>p.Value.WorldMatches(fullActor.Bones[p.Key])),"Actual extension output remains stable after a seek: "+label+" "+seconds);
            }
            full.Apply(compatible.lastFrame/30f);Record(compatible.lastFrame/30f);
            check(fullActor.Bones.Values.All(Finite),"Actual motion endpoint remains finite: "+label);
        }
        check(native.All(p=>p.Value.LocalMatches(fullActor.Bones[p.Key])),"Actual motion stop restores all original native local TRS: "+label);
        using(var repeatedPlayback=new RuntimeVmdPose(fullActor.Root,new ActionDefinition{Id="replay-"+label,WriteFaceMorphs=false},original,reference))
        {
            foreach(var sample in replay)
            {
                repeatedPlayback.Apply(sample.Seconds);
                check(sample.Bones.All(p=>p.Value.WorldMatches(fullActor.Bones[p.Key])),
                    "Actual cached rig reproduces an identical full query history: "+label+" "+sample.Seconds);
                check(Vector3.Distance(sample.Displacement,repeatedPlayback.RootDisplacement)<.0001f,
                    "Actual cached rig reproduces controller travel for an identical query history: "+label+" "+sample.Seconds);
            }
        }
        check(native.All(p=>p.Value.LocalMatches(fullActor.Bones[p.Key])),"Stopping replay restores the same native local TRS: "+label);
        bool animatedExtension=UpstreamMotionSupport.Tracks(compatible).Any(p=>consumed.Contains(p.Key)&&MmdBoneMap.Animated(p.Value));
        check(!animatedExtension||different>0,"Non-neutral actual extensions produce observable output beyond core-only playback: "+label);
        Console.WriteLine("REAL_MOTION_RUNTIME name="+name+" reference="+reference+" sha256="+sha+" samples="+times.Length
            +" extensions="+string.Join(",",consumed)+" unsupportedAnimated="+string.Join(",",unsupported)
            +" differingBoneSamples="+different+" maxDifferenceM="+maxDifferenceP+" maxDifferenceDeg="+maxDifferenceQ+" seekCases="+seekCases
            +" fullHistoryCases="+fullHistoryCases+" coreHistoryCases="+coreHistoryCases+" replayQueries="+replay.Count);
    }

    static IEnumerable<float> SampleFrames(string name,MmdMotion motion)
    {
        var frames=new SortedSet<float>{0,MathF.Max(0,motion.lastFrame-.25f)};
        for(int frame=30;frame<motion.lastFrame;frame+=30)frames.Add(frame+.25f);
        // Reported windows: Shake ankle motion, the final waist sequence and
        // the sitting section of 盛装登场 receive ten samples per second.
        int start=name=="腰振りダンス"?Math.Max(0,(int)motion.lastFrame-450):name=="盛装登场"?1800:0;
        int end=name=="盛装登场"?Math.Min((int)motion.lastFrame,2700):name=="Shake"?(int)motion.lastFrame:0;
        if(name=="腰振りダンス")end=(int)motion.lastFrame;
        for(int frame=start;frame<end;frame+=3)frames.Add(frame+.25f);
        foreach(var frame in motion.RawIkFrames)
            if(frame.Frame<motion.lastFrame)frames.Add(frame.Frame);
        return frames.Where(frame=>frame<motion.lastFrame);
    }

    static MmdMotion CoreOnly(MmdMotion compatible)
    {
        var result=new MmdMotion{lastFrame=compatible.lastFrame,modelName=compatible.modelName,BodyInputCompatible=true};
        foreach(var pair in compatible.tracks.Where(p=>UpstreamMotionSupport.Contains(p.Key)))result.tracks.Add(pair.Key,pair.Value);
        // Reader-recognized eye/twist keys also define the upstream duration,
        // even though this stripped body view does not drive those tracks.
        result.RawBoneKeys.AddRange(compatible.RawBoneKeys.Where(p=>SFM.Mmd.Upstream.Reader.VMDReader.BoneKeyFrameGroup.StringBoneNames.Contains(p.Name)));
        result.RawIkFrames.AddRange(compatible.RawIkFrames);
        return result;
    }
}
