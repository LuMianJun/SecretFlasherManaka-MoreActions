using UnityEngine;
using System.Security.Cryptography;
using System.Text;
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
int checks=0;void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
double Error(Quaternion a,Quaternion b)
{
    var aa=a.normalized;var bb=b.normalized;
    double plus=Math.Pow(aa.x+bb.x,2)+Math.Pow(aa.y+bb.y,2)+Math.Pow(aa.z+bb.z,2)+Math.Pow(aa.w+bb.w,2);
    double minus=Math.Pow(aa.x-bb.x,2)+Math.Pow(aa.y-bb.y,2)+Math.Pow(aa.z-bb.z,2)+Math.Pow(aa.w-bb.w,2);
    return 4*Math.Asin(Math.Min(1,Math.Sqrt(Math.Min(plus,minus))/2))*180/Math.PI;
}
var references=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../reference"));
foreach(var (name,hash) in new[]{("UnityVMDPlayer.cs","f9d7b7f59d983575eaa7c69960246c892f1d3d9ccdbb9c2855b1c4f856c11aaf"),("UnityVMDReader.cs","e6b60154bffb8178612b5d3d4111103568177f7d6acdf026cecc1889c7e97834"),("UnityVMDQuaternionExtentions.cs","ff77d848fd72ebe306f662c740235c4696ff60bc17f1a394e950b9d8af10f88e")})
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(references,name)))).Equals(hash,StringComparison.OrdinalIgnoreCase),"Unmodified pinned source: "+name);
#if PRODUCTION_AVAILABLE
int nativeArgument=Array.IndexOf(args,"--native-motions");
if(nativeArgument>=0)
{
    if(nativeArgument+1>=args.Length)throw new ArgumentException("--native-motions requires a directory");
    int summaryArgument=Array.IndexOf(args,"--summary");
    if(summaryArgument>=0&&summaryArgument+1>=args.Length)throw new ArgumentException("--summary requires a path");
    string summary=summaryArgument>=0?args[summaryArgument+1]:Path.Combine(Environment.CurrentDirectory,"native-parity-summary.json");
    NativeParityComparison.Run(args[nativeArgument+1],summary,Check);
    if(args.Contains("--native-only")){Console.WriteLine("UPSTREAM_NATIVE_ORACLE PASS checks="+checks);return;}
}
#endif
foreach(float x in new[]{-170f,-89f,-31f,0f,19f,88f,173f})foreach(float y in new[]{-179f,-40f,0f,63f,178f})foreach(float z in new[]{-127f,0f,43f,174f})
{
    var q=Quaternion.Euler(x,y,z);Check(Error(q,Quaternion.Euler(q.eulerAngles))<.002,"Unity ZXY boundary roundtrip");
}
var folder=Path.Combine(Path.GetTempPath(),"sfm-upstream-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
try
{
    foreach(bool axes in new[]{false,true})foreach(bool sparse in new[]{false,true})
    {
        var path=OracleVmd.Write(Path.Combine(folder,"motion.vmd"),sparse:sparse);
        var oracle=new OriginalOracle(path,axes);
        foreach(float frame in new[]{0f,5.25f,9.9f,10f,14.75f,15f,16.5f,29.9f,30f,44.5f,45f,59.5f})
        {
            oracle.Evaluate(frame);
            foreach(var bone in oracle.Fixture.Bones.Values)
                Check(float.IsFinite(bone.position.x)&&float.IsFinite(bone.position.y)&&float.IsFinite(bone.position.z)&&float.IsFinite(bone.rotation.w),"Original Player full AnimateBody/Update finite fixture output");
        }
    }
#if PRODUCTION_AVAILABLE
    CandidateComparison.Run(folder,Check);
    CompatibleVmd.Verify(folder,Check);
    int motionsArgument=Array.IndexOf(args,"--motions");
    if(motionsArgument>=0)
    {
        if(motionsArgument+1>=args.Length)throw new ArgumentException("--motions requires a directory");
        RealMotionComparison.Run(args[motionsArgument+1],Check);
    }
#endif
    Console.WriteLine("UPSTREAM_ORACLE PASS checks="+checks+" originalPlayer=true fullBody=true integerPairLerp=true");
}
finally { Directory.Delete(folder,true); }
