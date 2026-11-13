using System.Reflection;
using SFM.Mmd;
using UnityEngine;

internal static class SamplingRegression
{
    static byte[] Linear()
    {
        var result=new byte[64];
        for(int channel=0;channel<4;channel++)result[channel*16+8]=result[channel*16+12]=127;
        return result;
    }
    static MmdMotion Fixture(int variant)
    {
        var motion=new MmdMotion{lastFrame=60,PreparedForPlayback=true,ExactBoneSamples=variant==2};
        foreach(string name in new[]{"センター","下半身","左足","右足","左足首D","左足ＩＫ","右足ＩＫ","左つま先ＩＫ","左腕捩","左親指１","左親指２"})
        {
            var axis=name.Contains("足")?new Vector3(1,0,0):Vector3.up;
            motion.tracks[name]=[
                new(){frame=0,q=Quaternion.identity,interpolation=Linear()},
                new(){frame=30,p=new(.1f,variant*.2f,.3f),q=Quaternion.AngleAxis(20+variant*11,axis),interpolation=Linear()},
                new(){frame=60,p=new(-.1f,-.2f,0),q=Quaternion.AngleAxis(-15,axis),interpolation=Linear()}];
        }
        if(variant==1)motion.tracks["左親指０"]=motion.tracks["下半身"];
        var switches=(Dictionary<string,List<KeyValuePair<uint,bool>>>)typeof(MmdMotion).GetField("ikSwitches",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(motion)!;
        switches["左足ＩＫ"]=[new(5,false),new(20,true),new(45,false)];
        return motion;
    }
    internal static void Run(Action<bool,string> check,IReadOnlyDictionary<string,Vector3> geometry,string resources)
    {
        var numeric=new MmdMotion();numeric.tracks["curve"]=[
            new(){frame=10,p=new(1,2,3),q=Quaternion.identity,interpolation=Linear()},
            new(){frame=40,p=new(4,8,-3),q=Quaternion.AngleAxis(60,Vector3.up),interpolation=Linear()}];
        foreach(float frame in new[]{-10f,10f,10.25f,17.5f,25f,39.99f,40f,100f})
        {
            var sample=numeric.SampleValue("curve",frame);float t=Math.Clamp((frame-10)/30,0,1);
            check(Vector3.Distance(sample.p,Vector3.Lerp(new(1,2,3),new(4,8,-3),t))<.00002f&&Quaternion.Angle(sample.q,Quaternion.AngleAxis(60*t,Vector3.up))<.1f,"Value sampling preserves independently calculated linear positions and rotations");
            var legacy=numeric.Sample("curve",frame);
            check(Vector3.Distance(sample.p,legacy.p)==0&&Quaternion.Angle(sample.q,legacy.q)<.1f,"Public key sampling and allocation-free numeric sampling agree at endpoints and between keys");
        }
        check(ReferenceEquals(numeric.Sample("curve",0),numeric.tracks["curve"][0])&&ReferenceEquals(numeric.Sample("curve",50),numeric.tracks["curve"][1]),"Public Sample retains existing endpoint object identity");
        check(numeric.SampleValue("missing",10).p.sqrMagnitude==0&&Quaternion.Angle(numeric.SampleValue("missing",10).q,Quaternion.identity)==0,"Missing numeric tracks retain identity and zero translation");
        for(int i=0;i<2048;i++)numeric.SampleValue("curve",25);
        long before=GC.GetAllocatedBytesForCurrentThread();float consumed=0;
        for(int i=0;i<50000;i++)consumed+=numeric.SampleValue("curve",11+i%28).p.x;
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0&&consumed>0,"Warmed numeric bone sampling allocates no temporary key objects");
        Console.WriteLine("SAMPLING_ALLOCATION samples=50000 bytes="+allocated);

        var positions=geometry.ToDictionary(p=>p.Key,p=>new System.Numerics.Vector3(p.Value.x,p.Value.y,p.Value.z));
        using var generic=GameMmdSkeleton.CreateData(positions);generic.CreateTransforms();generic.useIk=true;
        using var prepared=GameMmdSkeleton.CreateData(positions);prepared.CreateTransforms();prepared.useIk=true;
        void Compare(MmdMotion motion,IEnumerable<float> frames)
        {
            prepared.PrepareMotion(motion);
            foreach(float frame in frames)
            {
                generic.Evaluate(motion,frame);prepared.Evaluate(motion,frame);
                check(generic.bones.Zip(prepared.bones).All(pair=>Vector3.Distance(pair.First.node.position,pair.Second.node.position)<.00001f&&Quaternion.Angle(pair.First.node.rotation,pair.Second.node.rotation)<.1f),"Prepared sampling preserves all bone poses, IK switches, append chains and thumb selection after seeking");
            }
        }
        var frames=new[]{0f,5f,5.5f,30f,60f,19.75f,45f,20f,0f,59.5f};
        var motions=Enumerable.Range(0,20).Select(i=>Fixture(i%3)).ToArray();
        foreach(var motion in motions)Compare(motion,frames);
        foreach(var motion in motions.Reverse())Compare(motion,frames.Reverse());
        check(!motions[0].IkEnabled("左足ＩＫ",5)&&motions[0].IkEnabled("左足ＩＫ",20)&&!motions[0].IkEnabled("左足ＩＫ",45)&&motions[0].IkEnabled("左足ＩＫ",0),"Binary IK switch lookup preserves exact boundaries and backward seeks");

        var mutable=Fixture(0);mutable.PreparedForPlayback=false;prepared.PrepareMotion(mutable);
        mutable.tracks["左親指０"]=[new(){q=Quaternion.AngleAxis(77,Vector3.up),interpolation=Linear()}];
        generic.Evaluate(mutable,0);prepared.Evaluate(mutable,0);
        check(Quaternion.Angle(prepared.byName["左親指０"].node.rotation,generic.byName["左親指０"].node.rotation)<.1f,"Preparing a public mutable motion does not freeze later dictionary or thumb topology edits");
        if(resources!=null)
        foreach(string file in Directory.GetFiles(resources,"motion.vmd",SearchOption.AllDirectories))
        {
            var motion=MmdMotion.Read(file);motion.PreparedForPlayback=true;
            Compare(motion,new[]{0f,motion.lastFrame*.1f,motion.lastFrame*.65f,motion.lastFrame*.5f,(float)motion.lastFrame,0f});
        }
    }
}
