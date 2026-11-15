using System.Text.Json;
using SFM.Mmd;
using SFM.Mmd.Upstream;
using UnityEngine;

// Standard pose semantics are checked against the compiled, unchanged upstream
// source in UpstreamValidation. These checks cover our explicit Reset wrapper;
// they do not require arbitrary seeks to erase the upstream's pose history.
internal static class PlaybackLifecycleRegression
{
    readonly record struct State(Vector3 Position,Quaternion Rotation,Vector3 Scale)
    {
        internal State(Transform node):this(node.localPosition,node.localRotation,node.localScale){}
        internal bool Matches(Transform node)=>Vector3.Distance(Position,node.localPosition)<.00001f
            &&Quaternion.Angle(Rotation,node.localRotation)<.1f&&Vector3.Distance(Scale,node.localScale)<.00001f;
    }
    static MmdMotion.Key Key(uint frame,Vector3 p,Quaternion q)
    {
        var curve=new byte[64];
        for(int channel=0;channel<4;channel++)
        {curve[channel]=20;curve[channel+4]=20;curve[channel+8]=107;curve[channel+12]=107;}
        return new(){frame=frame,p=p,q=q,interpolation=curve};
    }
    internal static void Run(Action<bool,string> check)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/native-model-bind.json")));
        foreach(var reference in new[]{MmdReferencePose.A,MmdReferencePose.T})
        {
            MmdRigCache.Clear();
            using var actor=new NativeBundleRegression.NativeActor(document.RootElement,1.125f);
            var rig=MmdRigCache.Rent(actor.Root,reference);
            try
            {
                var motion=new MmdMotion{lastFrame=60};
                motion.tracks["センター"]=[Key(0,Vector3.zero,Quaternion.identity),Key(60,new(.5f,.3f,-.2f),Quaternion.identity)];
                motion.tracks["首"]=[Key(0,Vector3.zero,Quaternion.identity),Key(60,Vector3.zero,Quaternion.AngleAxis(36,Vector3.up))];
                motion.tracks["頭"]=[Key(0,Vector3.zero,Quaternion.AngleAxis(17,Vector3.right)),Key(60,Vector3.zero,Quaternion.AngleAxis(-12,Vector3.forward))];
                motion.RawIkFrames.Add((0,true,new(){new("左足ＩＫ",false),new("右足ＩＫ",false),new("左つま先ＩＫ",false),new("右つま先ＩＫ",false)}));
                using var solver=new UpstreamBodySolver(rig.avatar,rig.BindMotion(motion),reference==MmdReferencePose.A);
                Dictionary<string,State> Snapshot()=>rig.bindings.ToDictionary(b=>b.path,b=>new State(b.sample));
                var initial=Snapshot();
                var sequence=new[]{0f,7.25f,26f,44.5f,12.75f,58.25f};
                var expected=new List<Dictionary<string,State>>();
                foreach(float frame in sequence)
                {
                    solver.Evaluate(frame);expected.Add(Snapshot());
                    solver.Evaluate(frame);
                    check(expected[^1].All(p=>p.Value.Matches(rig.bindings.Single(b=>b.path==p.Key).sample)),
                        "Repeated core sampling retains the cached requested pose: "+reference+" frame="+frame);
                }
                solver.Reset();
                check(initial.All(p=>p.Value.Matches(rig.bindings.Single(b=>b.path==p.Key).sample)),
                    "Explicit Reset restores the post-initialization native sampling tree: "+reference);
                for(int i=0;i<sequence.Length;i++)
                {
                    solver.Evaluate(sequence[i]);
                    check(expected[i].All(p=>p.Value.Matches(rig.bindings.Single(b=>b.path==p.Key).sample)),
                        "Explicit Reset reproduces an identical query history: "+reference+" frame="+sequence[i]);
                }
            }
            finally{MmdRigCache.Return(rig);}
        }
        MmdRigCache.Clear();
    }
}
