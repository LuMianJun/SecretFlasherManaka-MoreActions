using SFM.Mmd;
using SFM.Mmd.Upstream;
using UnityEngine;

// Independent geometry expectations: unlike a second solver with the same
// parent frame, these checks derive the answer from the initial world pose.
internal static class ControlFrameRegression
{
    static readonly HashSet<HumanBodyBones> implicitLegControllers=new()
    {
        HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,
        HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,HumanBodyBones.RightToes
    };

    internal static void Run(Action<bool,string> check,IReadOnlyDictionary<string,Vector3> geometry)
    {
        foreach(float nativeMetric in new[]{.9f,1f,1.125f})
        {
            // Literal native Armature rotation, as captured from Manustia.
            using(var legacy=new Fixture(geometry,nativeMetric,-90,false,Vector3.zero))
            {
                var hips=legacy.Bones[HumanBodyBones.Hips];
                legacy.Solver.Evaluate(0);
                check(MathF.Abs(Quaternion.Angle(Quaternion.identity,hips.rotation)-90)<.02f,
                    "Literal native -90 Armature reproduces the extra zero-track hip rotation");
                var spine=legacy.Bones[HumanBodyBones.Spine];
                var expected=legacy.Initial[HumanBodyBones.Hips].position+
                    Quaternion.AngleAxis(-90,Vector3.right)*(legacy.Initial[HumanBodyBones.Spine].position-legacy.Initial[HumanBodyBones.Hips].position);
                check(Vector3.Distance(spine.position,expected)<.0001f,
                    "Literal native -90 Armature rotates the zero-track spine around the hips");
            }
            using(var normalized=new Fixture(geometry,nativeMetric,-90,true,Vector3.zero))
            {
                normalized.Solver.Evaluate(0);
                var implicitChanges=new List<string>();int observed=0;
                foreach(var (human,bone) in normalized.Bones)
                {
                    var initial=normalized.Initial[human];observed++;
                    float position=Vector3.Distance(initial.position,bone.position),rotation=Quaternion.Angle(initial.rotation,bone.rotation);
                    if(implicitLegControllers.Contains(human))
                    {
                        // Upstream always executes leg/toe IK, including
                        // missing tracks. Report its effects, do not define
                        // expected anatomy by comparing it to itself.
                        if(position>.0001f||rotation>.1f)implicitChanges.Add(human+":p="+position+",q="+rotation);
                    }
                    else
                    {
                        check(position<.0001f,"Neutral control parent preserves zero-track world position: "+human);
                        check(rotation<.1f,"Neutral control parent preserves zero-track world rotation: "+human);
                    }
                }
                check(observed==51,"Independent zero-track audit observes all 51 mapped native bones");
                Console.WriteLine("CONTROL_FRAME_ZERO metric="+nativeMetric+" observed="+observed+
                    " implicitLegChanges="+string.Join(";",implicitChanges));
            }
            using(var normalized=new Fixture(geometry,nativeMetric,-90,true,Vector3.up))
            {
                normalized.Solver.Evaluate(0);
                var expected=normalized.Initial[HumanBodyBones.Hips].position+Vector3.up*.08f;
                check(Vector3.Distance(normalized.Bones[HumanBodyBones.Hips].position,expected)<.0001f,
                    "Baked native metric preserves one VMD Y unit as .08 world metres");
            }
        }
    }

    sealed class Fixture:IDisposable
    {
        readonly GameObject root;
        internal readonly Dictionary<HumanBodyBones,Transform> Bones=new();
        internal readonly Dictionary<HumanBodyBones,(Vector3 position,Quaternion rotation)> Initial=new();
        internal readonly UpstreamBodySolver Solver;
        internal Fixture(IReadOnlyDictionary<string,Vector3> geometry,float metric,float parentAngle,bool normalized,Vector3 translation)
        {
            root=new GameObject("IndependentControlFrame");
            root.transform.localScale=Vector3.one*(normalized?1:metric);
            var armature=new GameObject("Armature").transform;armature.SetParent(root.transform,false);
            armature.localRotation=Quaternion.AngleAxis(normalized?0:parentAngle,Vector3.right);
            var byPath=new Dictionary<string,Transform>{{"Armature",armature}};
            foreach(var (path,p) in geometry.OrderBy(p=>p.Key.Count(c=>c=='/')).ThenBy(p=>p.Key,StringComparer.Ordinal))
            {
                int slash=path.LastIndexOf('/');var sample=new GameObject(path[(slash+1)..]).transform;
                sample.SetParent(byPath[path[..slash]],false);
                sample.position=p*metric;sample.rotation=Quaternion.identity;byPath.Add(path,sample);
                if(GameHumanoidMap.TryGet(path,out var human))
                {Bones.Add(human,sample);Initial.Add(human,(sample.position,sample.rotation));}
            }
            var motion=new MmdMotion{lastFrame=2};
            motion.tracks["センター"]=[Key(0,translation),Key(2,translation)];
            Solver=new UpstreamBodySolver(new UpstreamAvatar(root.transform,Bones),motion,false);
        }
        static MmdMotion.Key Key(uint frame,Vector3 position)=>new()
        {frame=frame,p=position,q=Quaternion.identity,interpolation=new byte[64]};
        public void Dispose(){try{Solver?.Dispose();}finally{UnityEngine.Object.Destroy(root);}}
    }
}
