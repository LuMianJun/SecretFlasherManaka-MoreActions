using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SFM.Actions;
using SFM.Mmd;
using SFM.Mmd.Upstream;
using UnityEngine;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if(args.Length!=3||args[0] is not ("baseline" or "extended" or "groups"))
    throw new ArgumentException("Usage: PlaybackParityValidation <baseline|extended|groups> <six-motion directory> <summary.json>");
bool extended=args[0]=="extended";
using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/native-model-bind.json")));
var expectedHashes=new Dictionary<string,string>{
    ["UnityVMDPlayer.cs"]="f9d7b7f59d983575eaa7c69960246c892f1d3d9ccdbb9c2855b1c4f856c11aaf",
    ["UnityVMDReader.cs"]="e6b60154bffb8178612b5d3d4111103568177f7d6acdf026cecc1889c7e97834",
    ["UnityVMDQuaternionExtentions.cs"]="ff77d848fd72ebe306f662c740235c4696ff60bc17f1a394e950b9d8af10f88e"};
foreach(var pair in expectedHashes)
    if(Hash(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../..","UpstreamValidation/reference",pair.Key)))!=pair.Value)
        throw new Exception("Pinned upstream source changed: "+pair.Key);
var cases=new List<object>();
bool allPassed=true;
var files=Directory.GetFiles(Path.GetFullPath(args[1]),"motion.vmd",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
if(files.Length==0)throw new Exception("No motion.vmd inputs found.");
if(args[0]=="groups")
{
    ExtensionGroupComparison.Run(files,fixture.RootElement,args[2]);
    return;
}
foreach(string path in files)
{
    string temporary=Path.Combine(Path.GetTempPath(),"sfm-playback-parity-"+Guid.NewGuid().ToString("N")+".vmd");
    CompatibleVmd.Write(path,temporary);
    try
    {
        var source=UpstreamInputCompatibility.Apply(MmdMotion.Read(path));
        var common=CoreOnly(source);
        foreach(var reference in new[]{MmdReferencePose.A,MmdReferencePose.T})
        {
            MmdRigCache.Clear();
            using var oracleActor=new NativeBundleRegression.NativeActor(fixture.RootElement,1.125f);
            using var liveActor=new NativeBundleRegression.NativeActor(fixture.RootElement,1.125f);
            var oracleRig=MmdRigCache.Rent(oracleActor.Root,reference);
            try
            {
                using var original=new Original(temporary,oracleRig,reference);
                // Establish the original first-frame anchor independently of
                // RuntimeVmdPose's horizontal controller-motion extraction.
                original.Evaluate(0);
                var sourceOrigin=oracleRig.sampleHip.position;
                var originCorrection=sourceOrigin-oracleRig.ReferenceHipPosition;originCorrection.y=0;
                var nativeRoot=liveActor.Root.transform.position;
                var heading=liveActor.Root.transform.rotation;
                using var playback=new RuntimeVmdPose(liveActor.Root,new ActionDefinition{Id=args[0]+"-"+Path.GetFileName(Path.GetDirectoryName(path)),WriteFaceMorphs=false,Loop=false},extended?source:common,reference);
                var records=oracleRig.bindings.ToDictionary(b=>b.path,b=>new Difference());
                int queries=0;
                int limit=original.FrameCount;
                if(limit<=0)throw new Exception("The normalized upstream input has no playable standard keys: "+path);
                foreach(float frame in Frames(limit))
                {
                    // Both players must see the same single-precision time.
                    // Seconds -> frame can round an integer just below n;
                    // sampling original n against production n-epsilon would
                    // otherwise compare different upstream query histories.
                    float seconds=frame/30f,effectiveFrame=seconds*30f;
                    original.Evaluate(effectiveFrame);
                    playback.Apply(seconds);
                    queries++;
                    var travel=Quaternion.Inverse(heading)*playback.RootDisplacement;
                    foreach(var binding in oracleRig.bindings)
                    {
                        var live=liveActor.Bones[binding.path];
                        var p=Quaternion.Inverse(heading)*(live.position-nativeRoot)+travel+originCorrection;
                        var q=Quaternion.Inverse(heading)*live.rotation;
                        records[binding.path].Add(frame,Vector3.Distance(p,binding.sample.position),Degrees(q,binding.sample.rotation));
                    }
                }
                double maxP=records.Values.Max(r=>r.PositionM),maxQ=records.Values.Max(r=>r.RotationDegrees);
                bool? parityPassed=extended?null:maxP<.0001&&maxQ<.05;
                if(parityPassed==false)allPassed=false;
                var changed=records.Where(p=>p.Value.PositionM>=.0001||p.Value.RotationDegrees>=.05).ToDictionary(p=>p.Key,p=>p.Value);
                cases.Add(new{motion=Path.GetFileName(Path.GetDirectoryName(path)),sourceSha256=Hash(path),effectiveInputSha256=Hash(temporary),reference=reference.ToString(),heightMultiplier=1.125,
                    queries,bones=records.Count,maxPositionM=maxP,maxRotationDegrees=maxQ,parityPassed,comparisonCompleted=true,changedBones=changed.Count,differences=records,
                    firstFrameControllerOriginCorrection=V(originCorrection)});
                Console.WriteLine($"PLAYBACK_{args[0].ToUpperInvariant()} {(extended?"MEASURED":parityPassed==true?"PASS":"FAIL")} motion={Path.GetFileName(Path.GetDirectoryName(path))} reference={reference} queries={queries} bones={records.Count} maxP={maxP:R} maxQ={maxQ:R} changedBones={changed.Count}");
            }
            finally{MmdRigCache.Return(oracleRig);}
        }
    }
    finally{File.Delete(temporary);}
}
var summary=new{schema=1,stage=args[0],fixedUpstreamCommit="2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8",productionDefault=true,
    upstreamSourcesSha256=new[]{"UnityVMDPlayer.cs","UnityVMDReader.cs","UnityVMDQuaternionExtentions.cs"}.ToDictionary(n=>n,n=>Hash(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../..","UpstreamValidation/reference",n)))),
    scope=extended?"Full optional-track playback compared against the same unmodified upstream standard baseline. Nonzero extension effects are reported, not called parity errors or automatically accepted as correct; separate extension contracts verify their intended effect.":"RuntimeVmdPose with unsupported extension tracks removed, compared against unmodified original Player reading the same effective standard input. All 80 native bone points and orientations are checked after real game-output transfer and horizontal controller displacement reconstruction.",
    sampling="Same single-precision seconds on both sides: every third source frame and n+0.375, plus the supported endpoint. Original input frame is (frame/30f)*30f, exactly as RuntimeVmdPose. Full source-frame core parity is a separate gate.",
    coordinateConvention="Native heading undone. Horizontal controller travel reconstructed, plus only the fixed initial source-origin offset; no per-frame or per-bone alignment, no floor correction.",
    positionToleranceM=.0001,rotationToleranceDegrees=.05,parityPassed=extended?(bool?)null:allPassed,comparisonCompleted=true,cases};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllText(args[2],JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
MmdRigCache.Clear();
if(!allPassed)Environment.ExitCode=1;

static IEnumerable<float> Frames(int limit)
{
    var frames=new SortedSet<float>{0,limit};
    for(int frame=0;frame<limit;frame+=3){frames.Add(frame);if(frame+.375f<limit)frames.Add(frame+.375f);}
    return frames;
}
static MmdMotion CoreOnly(MmdMotion source)
{
    var motion=new MmdMotion{lastFrame=source.lastFrame,modelName=source.modelName,BodyInputCompatible=true};
    foreach(var pair in source.tracks.Where(p=>UpstreamMotionSupport.Contains(p.Key)))motion.tracks.Add(pair.Key,pair.Value);
    // Keep every original Reader-recognized track for identical FrameCount
    // and cursor input, including eyes/twist that its body does not drive.
    // Only the game extension lookup dictionary above is restricted.
    motion.RawBoneKeys.AddRange(source.RawBoneKeys.Where(k=>UnityVMDReader.VMDReader.BoneKeyFrameGroup.StringBoneNames.Contains(k.Name)));
    motion.RawIkFrames.AddRange(source.RawIkFrames);
    return motion;
}
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static float[] V(Vector3 p)=>[p.x,p.y,p.z];
static double Degrees(Quaternion a,Quaternion b)
{
    var aa=a.normalized;var bb=b.normalized;
    double plus=Math.Pow(aa.x+bb.x,2)+Math.Pow(aa.y+bb.y,2)+Math.Pow(aa.z+bb.z,2)+Math.Pow(aa.w+bb.w,2);
    double minus=Math.Pow(aa.x-bb.x,2)+Math.Pow(aa.y-bb.y,2)+Math.Pow(aa.z-bb.z,2)+Math.Pow(aa.w-bb.w,2);
    return 4*Math.Asin(Math.Min(1,Math.Sqrt(Math.Min(plus,minus))/2))*180/Math.PI;
}
internal sealed class Difference
{
    public double PositionM{get;set;}
    public float PositionFrame{get;set;}
    public double RotationDegrees{get;set;}
    public float RotationFrame{get;set;}
    public int ChangedSamples{get;set;}
    internal void Add(float frame,double p,double q)
    {
        if(!double.IsFinite(p)||!double.IsFinite(q))throw new Exception("Nonfinite playback output frame="+frame);
        if(p>PositionM){PositionM=p;PositionFrame=frame;}
        if(q>RotationDegrees){RotationDegrees=q;RotationFrame=frame;}
        if(p>=.0001||q>=.05)ChangedSamples++;
    }
}
internal sealed class Original:IDisposable
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    readonly UnityVMDPlayer player;
    internal int FrameCount=>player.VMDReader.FrameCount;
    internal Original(string path,RuntimeVmdRig rig,MmdReferencePose reference)
    {
        var animator=rig.proxy.AddComponent<Animator>();animator.isHuman=reference==MmdReferencePose.A;
        foreach(var binding in rig.bindings.Where(b=>b.human.HasValue))animator.Bones.Add(binding.human.Value,binding.sample);
        animator.avatar.humanDescription=new(){skeleton=UpstreamAvatar.Descendants(rig.proxy.transform).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray()};
        player=new(){gameObject=rig.proxy};Invoke("Start");player.Play(path);
    }
    void Invoke(string name)=>typeof(UnityVMDPlayer).GetMethod(name,Flags).Invoke(player,null);
    internal void Evaluate(float frame)
    {
        typeof(UnityVMDPlayer).GetProperty("FrameNumber",Flags).SetValue(player,(int)frame);
        typeof(UnityVMDPlayer).GetField("internalFrameNumber",Flags).SetValue(player,frame);
        Time.deltaTime=0;Invoke("Update");
    }
    public void Dispose()=>player.Stop();
}
