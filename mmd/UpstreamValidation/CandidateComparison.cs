#if PRODUCTION_AVAILABLE
using UnityEngine;
using SFM.Mmd;
using SFM.Mmd.Upstream;

internal static class CandidateComparison
{
    internal static void Run(string folder,Action<bool,string> check)
    {
        int queries=0,cases=0;double maxPosition=0,maxDegrees=0;
        double Degrees(Quaternion a,Quaternion b)
        {
            var aa=a.normalized;var bb=b.normalized;
            double plus=Math.Pow(aa.x+bb.x,2)+Math.Pow(aa.y+bb.y,2)+Math.Pow(aa.z+bb.z,2)+Math.Pow(aa.w+bb.w,2);
            double minus=Math.Pow(aa.x-bb.x,2)+Math.Pow(aa.y-bb.y,2)+Math.Pow(aa.z-bb.z,2)+Math.Pow(aa.w-bb.w,2);
            return 4*Math.Asin(Math.Min(1,Math.Sqrt(Math.Min(plus,minus))/2))*180/Math.PI;
        }
        foreach(bool rawKeys in new[]{false,true})foreach(bool axes in new[]{false,true})foreach(float scale in new[]{1f,1.125f})foreach(bool sparse in new[]{false,true})foreach(bool switches in new[]{false,true})foreach(bool controlStress in new[]{false,true})
        {
            var path=OracleVmd.Write(Path.Combine(folder,"motion.vmd"),switches,sparse,rawKeys,controlStress);
            var reference=new OriginalOracle(path,axes,scale);var fixture=new OracleFixture(axes,scale);
            using var solver=new UpstreamBodySolver(new UpstreamAvatar(fixture.Actor.transform,fixture.Bones),MmdMotion.Read(path));
            var expectedIk=reference.Player.VMDReader.RawVMD.IKFrames;
            var actualIk=solver.Reader.RawVMD.IKFrames;
            check(expectedIk.Count==actualIk.Count,$"Raw IK frame count rawKeys={rawKeys} switches={switches}");
            for(int i=0;i<expectedIk.Count;i++)
            {
                var a=actualIk[i];var e=expectedIk[i];
                check(a.Frame==e.Frame&&a.Display==e.Display&&a.IKEnable.Count==e.IKEnable.Count,"Raw IK frame order/header");
                for(int j=0;j<e.IKEnable.Count;j++)check(a.IKEnable[j].IKName==e.IKEnable[j].IKName&&a.IKEnable[j].Enable==e.IKEnable[j].Enable,"Raw IK entry order/value");
            }
            float[][] sequences=
            [
                [0,0.25f,0.75f,1,5.125f,8,9.9f,10,14.75f,15,16.5f,17,24.9f,25,29.99f,30,44.5f,45,59.5f,60,60.5f,70],
                [0,15,30,45,12.5f,30.25f,0,15.75f,45.5f,9.25f],
            ];
            foreach(var sequence in sequences)
            {
                solver.Reset();reference=new OriginalOracle(path,axes,scale);
                foreach(float frame in sequence)
                {
                    reference.Evaluate(frame);solver.Evaluate(frame);queries++;
                    foreach(var (name,expected) in reference.Fixture.Bones)
                    {
                        var actual=fixture.Bones[name];
                        double pos=Vector3.Distance(actual.position,expected.position),degrees=Degrees(actual.rotation,expected.rotation);
                        maxPosition=Math.Max(maxPosition,pos);maxDegrees=Math.Max(maxDegrees,degrees);
                        string label=$"Pinned Player comparison bone={name} frame={frame} axes={axes} scale={scale} sparse={sparse} switches={switches} rawKeys={rawKeys} controlStress={controlStress}";
                        check(pos<.00005&&degrees<.01,label+$" world position={pos} rotation={degrees}");
                        check(Vector3.Distance(actual.localPosition,expected.localPosition)<.00005&&Degrees(actual.localRotation,expected.localRotation)<.01,label+" local pose");
                        check(Vector3.Distance(actual.localScale,expected.localScale)<1e-7,label+" scale");
                    }
                    check(Vector3.Distance(fixture.Actor.transform.position,reference.Fixture.Actor.transform.position)<.00005,"Pinned Player actor root position");
                }
            }
            cases++;
        }
        Console.WriteLine($"UPSTREAM_COMPARISON PASS cases={cases} queries={queries} maxPositionM={maxPosition} maxRotationDegrees={maxDegrees}");
    }
}
#endif
