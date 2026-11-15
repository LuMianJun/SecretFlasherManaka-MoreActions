#if PRODUCTION_AVAILABLE
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SFM.Mmd;
using SFM.Mmd.Upstream;
using UnityEngine;

// The oracle compiles the pinned, unmodified upstream sources. Production is
// always constructed with its public default; there is no legacy test mode.
internal static class NativeParityComparison
{
    const double PositionToleranceM=.0001, RotationToleranceDegrees=.05;
    internal static void Run(string directory,string reportPath,Action<bool,string> check)
    {
        var files=Directory.GetFiles(directory,"motion.vmd",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        check(files.Length>0,"Native parity motions exist");
        var fixturePath=Path.Combine(AppContext.BaseDirectory,"Fixtures/native-model-bind.json");
        using var fixture=JsonDocument.Parse(File.ReadAllText(fixturePath));
        var results=new List<CaseResult>();
        foreach(var path in files)
        {
            using var oracleInput=new CompatibleInput(path);
            var motion=UpstreamInputCompatibility.Apply(MmdMotion.Read(path));
            foreach(float height in new[]{.9f,1.125f,1.2f})foreach(bool aPose in new[]{true,false})
            {
                using var originalFixture=new NativeFixture(fixture.RootElement,height,aPose);
                using var actualFixture=new NativeFixture(fixture.RootElement,height,aPose);
                using var original=new NativeOriginal(oracleInput.Path,originalFixture);
                using var actual=new UpstreamBodySolver(new UpstreamAvatar(actualFixture.Actor.transform,actualFixture.Humans),motion,aPose);
                var result=new CaseResult{Motion=Path.GetFileName(Path.GetDirectoryName(path)),Source=Path.GetFullPath(path),SourceSha256=Hash(path),EffectiveInputSha256=Hash(oracleInput.Path),
                    Reference=aPose?"A":"T",HeightMultiplier=height,Bones=actualFixture.Paths.Count-2,ComparedNodes=actualFixture.Paths.Count,FrameCount=original.Player.VMDReader.FrameCount,
                    SupportedKeys=original.Player.VMDReader.BoneKeyFrameGroups.Sum(g=>g.BoneKeyFrames.Count)};
                check(result.FrameCount>0&&result.SupportedKeys>0,"Native parity effective input has playable standard keys: "+path);
                // Calls have the same monotonically increasing n/n+1 sampling
                // history, including two noninteger samples within every n.
                // The original Update stops before FrameCount; compare its
                // playable interval, then separately verify identical end hold.
                for(int frame=0;frame<result.FrameCount;frame++)
                    foreach(float fraction in new[]{0f,.375f,.75f})
                        Compare(frame+fraction,original,actual,originalFixture,actualFixture,result);
                Compare(result.FrameCount,original,actual,originalFixture,actualFixture,result);
                result.Passed=result.MaxWorldPositionM<PositionToleranceM&&result.MaxWorldRotationDegrees<RotationToleranceDegrees
                    &&result.MaxLocalPositionM<PositionToleranceM&&result.MaxLocalRotationDegrees<RotationToleranceDegrees;
                results.Add(result);
                Console.WriteLine($"NATIVE_PARITY {(result.Passed?"PASS":"FAIL")} motion={result.Motion} reference={result.Reference} height={height} queries={result.Queries} bones={result.Bones} maxWorldP={result.MaxWorldPositionM:R} maxWorldQ={result.MaxWorldRotationDegrees:R} maxLocalP={result.MaxLocalPositionM:R} maxLocalQ={result.MaxLocalRotationDegrees:R}");
            }
        }
        var referencePath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../reference"));
        var sources=new[]{"UnityVMDPlayer.cs","UnityVMDReader.cs","UnityVMDQuaternionExtentions.cs"}.ToDictionary(n=>n,n=>Hash(Path.Combine(referencePath,n)));
        var summary=new{schema=1,fixedUpstreamCommit="2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8",upstreamSourcesSha256=sources,
            nativeFixtureSha256=Hash(fixturePath),productionDefault=true,sourceTransform="Independent fixed-width binary name canonicalization for original Player; production UpstreamInputCompatibility.Apply on raw input. Both sides receive the same effective standard tracks. Raw-input parity is also checked separately by --motions.",
            referenceExplanation="A uses original Play initialization. T sets the offline Animator.isHuman=false solely to skip its optional 30-degree A arm rotation; the same original restore and solver code executes.",
            scope="Standard upstream solve on the full native hierarchy. Optional game grants/morphs/native output leases have separate compatibility tests.",
            sampleExplanation="Sequential 30fps integer frames, plus n+0.375 and n+0.75 within every source frame; same n/n+1 call history. Final FrameCount holds as upstream Update does.",
            positionToleranceM=PositionToleranceM,rotationToleranceDegrees=RotationToleranceDegrees,cases=results.Count,
            queries=results.Sum(r=>r.Queries),boneComparisons=results.Sum(r=>(long)r.Queries*r.Bones),nodeComparisons=results.Sum(r=>(long)r.Queries*r.ComparedNodes),passed=results.All(r=>r.Passed),results};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath,JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
        check(results.All(r=>r.Passed),"Native default-production parity: "+Path.GetFullPath(reportPath));
    }

    static void Compare(float frame,NativeOriginal original,UpstreamBodySolver actual,NativeFixture expectedFixture,NativeFixture actualFixture,CaseResult result)
    {
        original.Evaluate(frame);actual.Evaluate(frame);result.Queries++;
        bool exact=true;
        foreach(var (path,e) in expectedFixture.Paths)
        {
            var a=actualFixture.Paths[path];
            double p=Vector3.Distance(a.localPosition,e.localPosition),q=Degrees(a.localRotation,e.localRotation);
            if(!double.IsFinite(p)||!double.IsFinite(q))throw new Exception($"Nonfinite native parity frame={frame} path={path}");
            if(p>result.MaxLocalPositionM){result.MaxLocalPositionM=p;result.WorstLocalPosition=new(frame,path);}
            if(q>result.MaxLocalRotationDegrees){result.MaxLocalRotationDegrees=q;result.WorstLocalRotation=new(frame,path);}
            // Exact locals (including ancestors) imply identical world values.
            // Avoid recomputing recursive scene matrices in the common exact
            // case; every differing frame is checked in world space below.
            if(!Exact(a.localPosition,e.localPosition)||!Exact(a.localRotation,e.localRotation)||!Exact(a.localScale,e.localScale))exact=false;
        }
        if(exact)return;
        foreach(var (path,e) in expectedFixture.Paths)
        {
            var a=actualFixture.Paths[path];
            double p=Vector3.Distance(a.position,e.position),q=Degrees(a.rotation,e.rotation);
            if(!double.IsFinite(p)||!double.IsFinite(q))throw new Exception($"Nonfinite native world parity frame={frame} path={path}");
            if(p>result.MaxWorldPositionM){result.MaxWorldPositionM=p;result.WorstWorldPosition=new(frame,path);}
            if(q>result.MaxWorldRotationDegrees){result.MaxWorldRotationDegrees=q;result.WorstWorldRotation=new(frame,path);}
        }
    }
    internal static double Degrees(Quaternion a,Quaternion b)
    {
        var aa=a.normalized;var bb=b.normalized;
        double plus=Math.Pow(aa.x+bb.x,2)+Math.Pow(aa.y+bb.y,2)+Math.Pow(aa.z+bb.z,2)+Math.Pow(aa.w+bb.w,2);
        double minus=Math.Pow(aa.x-bb.x,2)+Math.Pow(aa.y-bb.y,2)+Math.Pow(aa.z-bb.z,2)+Math.Pow(aa.w-bb.w,2);
        return 4*Math.Asin(Math.Min(1,Math.Sqrt(Math.Min(plus,minus))/2))*180/Math.PI;
    }
    static bool Exact(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
    static bool Exact(Quaternion a,Quaternion b)=>a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;
    static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    sealed record Point(float Frame,string BonePath);
    sealed class CaseResult
    {
        public string Motion{get;set;}public string Source{get;set;}public string SourceSha256{get;set;}public string EffectiveInputSha256{get;set;}public string Reference{get;set;}
        public float HeightMultiplier{get;set;}public int Bones{get;set;}public int ComparedNodes{get;set;}public int FrameCount{get;set;}public int SupportedKeys{get;set;}public int Queries{get;set;}public bool Passed{get;set;}
        public double MaxWorldPositionM{get;set;}public double MaxWorldRotationDegrees{get;set;}public double MaxLocalPositionM{get;set;}public double MaxLocalRotationDegrees{get;set;}
        public Point WorstWorldPosition{get;set;}public Point WorstWorldRotation{get;set;}public Point WorstLocalPosition{get;set;}public Point WorstLocalRotation{get;set;}
    }
    sealed class CompatibleInput:IDisposable
    {
        internal readonly string Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"sfm-native-oracle-"+Guid.NewGuid().ToString("N")+".vmd");
        internal CompatibleInput(string source)=>CompatibleVmd.Write(source,Path);
        public void Dispose(){if(File.Exists(Path))File.Delete(Path);}
    }
    internal sealed class NativeFixture:IDisposable
    {
        internal readonly GameObject Actor=new("NativeBindParityAvatar");
        internal readonly Dictionary<string,Transform> Paths=new(StringComparer.Ordinal);
        internal readonly Dictionary<HumanBodyBones,Transform> Humans=new();
        internal readonly Animator Animator;
        internal NativeFixture(JsonElement fixture,float height,bool aPose)
        {
            var armature=new GameObject("Armature").transform;armature.SetParent(Actor.transform,false);
            // Production's hidden control parent is upright metric identity.
            foreach(var row in fixture.GetProperty("bones").EnumerateArray().OrderBy(r=>r.GetProperty("path").GetString()!.Count(c=>c=='/')))
            {
                string path=row.GetProperty("path").GetString()!;int slash=path.LastIndexOf('/');
                var bone=new GameObject(path[(slash+1)..]).transform;bone.SetParent(Actor.transform.Find(path[..slash]),false);
                if(row.GetProperty("bind").GetBoolean()){bone.position=V(row,"position")*height;bone.rotation=Q(row,"rotation");}
                else{var local=row.GetProperty("local");bone.localPosition=V(local,"position");bone.localRotation=Q(local,"rotation");bone.localScale=V(local,"scale");}
                Paths.Add(path,bone);if(GameHumanoidMap.TryGet(path,out var role))Humans.Add(role,bone);
            }
            Animator=Actor.AddComponent<Animator>();Animator.isHuman=aPose;
            foreach(var pair in Humans)Animator.Bones.Add(pair.Key,pair.Value);
            Animator.avatar.humanDescription=new(){skeleton=UpstreamAvatar.Descendants(Actor.transform).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray()};
            // Include both parents in the local comparison: equality of all
            // ancestors is required for the exact-local fast path above.
            Paths.Add("",Actor.transform);Paths.Add("Armature",armature);
        }
        static Vector3 V(JsonElement r,string n){var a=r.GetProperty(n);return new(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle());}
        static Quaternion Q(JsonElement r,string n){var a=r.GetProperty(n);return new(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle(),a[3].GetSingle());}
        public void Dispose()=>UnityEngine.Object.Destroy(Actor);
    }
    internal sealed class NativeOriginal:IDisposable
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        internal readonly UnityVMDPlayer Player;
        internal NativeOriginal(string path,NativeFixture fixture){Player=new(){gameObject=fixture.Actor};Invoke("Start");Player.Play(path);}
        void Invoke(string method)=>typeof(UnityVMDPlayer).GetMethod(method,Flags)!.Invoke(Player,null);
        internal void Evaluate(float frame)
        {
            typeof(UnityVMDPlayer).GetProperty("FrameNumber",Flags)!.SetValue(Player,(int)frame);
            typeof(UnityVMDPlayer).GetField("internalFrameNumber",Flags)!.SetValue(Player,frame);Time.deltaTime=0;Invoke("Update");
        }
        public void Dispose()=>Player.Stop();
    }
}
#endif
