#if PRODUCTION_AVAILABLE
using UnityEngine;
using SFM.Mmd;
using SFM.Mmd.Upstream;

internal static class RealMotionComparison
{
    internal static void Run(string directory,Action<bool,string> check)
    {
        var files=Directory.GetFiles(directory,"motion.vmd",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        check(files.Length>0,"Real motions exist");
        foreach(var path in files)
        {
            RunCase(path,path,false,check);
            string compatible=Path.Combine(Path.GetTempPath(),"sfm-compatible-"+Guid.NewGuid().ToString("N")+".vmd");
            try { CompatibleVmd.Write(path,compatible);RunCase(path,compatible,true,check); }
            finally { if(File.Exists(compatible))File.Delete(compatible); }
        }
    }
    static void RunCase(string path,string oraclePath,bool compatible,Action<bool,string> check)
    {
            var motion=MmdMotion.Read(path);
            if(compatible)motion=UpstreamInputCompatibility.Apply(motion);
            var original=new OriginalOracle(oraclePath,true,1.125f);var fixture=new OracleFixture(true,1.125f);
            using var candidate=new UpstreamBodySolver(new UpstreamAvatar(fixture.Actor.transform,fixture.Bones),motion);
            Console.WriteLine($"REAL_INPUT compatible={compatible} motion={Path.GetFileName(Path.GetDirectoryName(path))} originalFrameCount={original.Player.VMDReader.FrameCount} candidateFrameCount={candidate.Reader.FrameCount} supportedKeys={original.Player.VMDReader.BoneKeyFrameGroups.Sum(g=>g.BoneKeyFrames.Count)}");
            for(int i=0;i<original.Player.VMDReader.BoneKeyFrameGroups.Count;i++)
            {
                var e=original.Player.VMDReader.BoneKeyFrameGroups[i].BoneKeyFrames;var a=candidate.Reader.BoneKeyFrameGroups[i].BoneKeyFrames;
                check(e.Count==a.Count,$"Real Reader group {i} count expected={e.Count} actual={a.Count} file={path}");
                bool exact=true;
                for(int j=0;j<e.Count;j++)
                {
                    var x=e[j];var y=a[j];
                    if(x.Name!=y.Name||x.FrameNumber!=y.FrameNumber||x.Position.x!=y.Position.x||x.Position.y!=y.Position.y||x.Position.z!=y.Position.z||x.Rotation.x!=y.Rotation.x||x.Rotation.y!=y.Rotation.y||x.Rotation.z!=y.Rotation.z||x.Rotation.w!=y.Rotation.w){exact=false;break;}
                }
                check(exact,$"Real Reader group {i} exact raw key values/order file={path}");
            }
            double maxPosition=0,maxQuaternion=0;int queries=0;
            var frames=Enumerable.Range(0,121).Select(i=>i*.5f).Where(f=>f<motion.lastFrame)
                .Concat(Enumerable.Range(1,32).Select(i=>motion.lastFrame*i/32f).Where(f=>f>60&&f<motion.lastFrame)).Distinct().OrderBy(f=>f);
            foreach(float frame in frames)
            {
                original.Evaluate(frame);candidate.Evaluate(frame);queries++;
                bool frameMatches=true;
                foreach(var (name,expected) in original.Fixture.Bones)
                {
                    var actual=fixture.Bones[name];
                    double P(Vector3 a,Vector3 b)=>Vector3.Distance(a,b);
                    double Q(Quaternion a,Quaternion b)=>Math.Min(Math.Sqrt(Math.Pow(a.x-b.x,2)+Math.Pow(a.y-b.y,2)+Math.Pow(a.z-b.z,2)+Math.Pow(a.w-b.w,2)),Math.Sqrt(Math.Pow(a.x+b.x,2)+Math.Pow(a.y+b.y,2)+Math.Pow(a.z+b.z,2)+Math.Pow(a.w+b.w,2)));
                    double p=Math.Max(P(actual.position,expected.position),P(actual.localPosition,expected.localPosition));
                    double q=Math.Max(Q(actual.rotation,expected.rotation),Q(actual.localRotation,expected.localRotation));
                    maxPosition=Math.Max(maxPosition,p);maxQuaternion=Math.Max(maxQuaternion,q);
                    if(p>=.00005||q>=.00005){frameMatches=false;Console.WriteLine($"REAL_DIFFERENCE motion={Path.GetFileName(Path.GetDirectoryName(path))} frame={frame} bone={name} position={p} quaternion={q}");}
                    check(double.IsFinite(p)&&double.IsFinite(q),"Real motion finite output");
                }
                check(frameMatches,$"Real motion {path} frame={frame}");
            }
            Console.WriteLine($"UPSTREAM_REAL PASS compatible={compatible} motion={Path.GetFileName(Path.GetDirectoryName(path))} queries={queries} maxPositionM={maxPosition} maxQuaternionComponentDistance={maxQuaternion}");
    }
}
#endif
