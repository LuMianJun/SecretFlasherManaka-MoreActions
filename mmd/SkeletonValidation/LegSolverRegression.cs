using System.Text;
using UnityEngine;
using SFM.Mmd;

internal static class LegSolverRegression
{
    static MmdSkeleton Rig(Vector3 upper,Vector3 lower,float minimum,float maximum)
    {
        var rig=new MmdSkeleton { useIk=true };
        void Add(string name,int parent,Vector3 rest)
        {
            var bone=new MmdSkeleton.Bone { name=name,parent=parent,rest=rest,tail=-1 };
            rig.bones.Add(bone);rig.byName.Add(name,bone);
        }
        Add("左足",-1,Vector3.zero);Add("左ひざ",0,upper);Add("左足首",1,upper+lower);Add("左足ＩＫ",-1,upper+lower);
        rig.iks.Add(new MmdSkeleton.Ik { target=3,effector=2,links=[1,0],limitKnee=true,kneeMin=minimum,kneeMax=maximum,iterations=40,limit=45 });
        rig.CreateTransforms();return rig;
    }
    static MmdMotion Goal(MmdSkeleton rig,Vector3 target)
    {
        var motion=new MmdMotion { lastFrame=30 };
        motion.tracks["左足ＩＫ"]=[new(){frame=0,p=target-rig.byName["左足ＩＫ"].rest,q=Quaternion.identity,interpolation=new byte[64]}];
        return motion;
    }
    internal static void Run(Action<bool,string> check,IReadOnlyDictionary<string,Vector3> geometry,string resources)
    {
        foreach(var oblique in new[]{false,true})
        using(var rig=Rig(new(oblique?.06f:0,-.4f,.01f),new(oblique?-.04f:0,-.37f,-.02f),-3,160))
        {
            var upper=rig.bones[1].rest;var lower=rig.bones[2].rest-upper;
            foreach(var target in new[]{new Vector3(0,-.5f,0),new(0,-.7699f,0),new(0,-2,0),new(0,.5f,0),new(.000000001f,.5f,0),Vector3.zero,new(.3f,-.2f,.4f)})
            {
                var motion=Goal(rig,target);
                rig.Evaluate(motion,0);
                var foot=rig.bones[2].node.position;
                // Independent angular sweep checks the closest permitted hinge
                // distance, including oblique segments and unreachable targets.
                float nearest=float.PositiveInfinity;
                for(int i=0;i<=20000;i++)
                {
                    float angle=-3+163f*i/20000;
                    var end=upper+Quaternion.AngleAxis(angle,new(1,0,0))*lower;
                    nearest=Math.Min(nearest,Math.Abs(end.magnitude-target.magnitude));
                }
                check(Math.Abs(Vector3.Distance(foot,target)-nearest)<.0003f,"Analytic leg reaches the nearest permitted target, including oblique/reverse/zero goals");
                check(Vector3.Distance(rig.bones[1].node.localPosition,upper)<1e-7f&&Vector3.Distance(rig.bones[2].node.localPosition,lower)<1e-7f,"IK never translates or stretches either segment");
                check(rig.bones[0].node.position.sqrMagnitude==0&&Vector3.Distance(rig.bones[3].node.position,target)<1e-6f,"IK preserves hip and authored goal");
                var rotations=rig.bones.Select(b=>b.node.localRotation).ToArray();
                rig.Evaluate(Goal(rig,new(.2f,.1f,.3f)),20);rig.Evaluate(motion,0);
                check(rig.bones.Zip(rotations).All(p=>Quaternion.Angle(p.First.node.localRotation,p.Second)<.1f),"Seeking returns the same branch without previous-frame state");
            }
            var fk=Goal(rig,new(0,-.4f,.2f));rig.useIk=false;rig.Evaluate(fk,0);
            check(Quaternion.Angle(rig.bones[1].node.localRotation,Quaternion.identity)<.01f,"Disabled IK leaves FK knee untouched");
            rig.useIk=true;fk.tracks.Clear();rig.Evaluate(fk,0);
            check(Quaternion.Angle(rig.bones[1].node.localRotation,Quaternion.identity)<.01f,"Missing IK track leaves FK knee untouched");
        }

        // A known rigid swing of a bent leg must carry its knee along with
        // its ankle. An additional twist about the ankle axis reaches the
        // same goal but changes this independently constructed knee position.
        using(var rig=Rig(new(0,-.4f,.01f),new(0,-.37f,-.02f),-3,160))
        {
            var upper=rig.bones[1].rest;var lower=rig.bones[2].rest-upper;
            var bent=upper+Quaternion.AngleAxis(80,new(1,0,0))*lower;
            foreach(float turn in new[]{0f,55f,140f})
            foreach(float bearing in new[]{0f,40f,90f,150f})
            foreach(float angle in new[]{20f,70f,120f,170f})
            {
                var authored=Quaternion.AngleAxis(turn,new(0,1,0));
                var axis=Quaternion.AngleAxis(bearing,bent.normalized)*new Vector3(1,0,0);
                var swing=Quaternion.AngleAxis(angle,authored*axis);
                var target=swing*(authored*bent);
                var motion=Goal(rig,target);
                motion.tracks["左足"]=[new(){q=authored,interpolation=new byte[64]}];
                rig.Evaluate(motion,0);
                var expectedKnee=swing*(authored*upper);
                check(Vector3.Distance(rig.bones[1].node.position,expectedKnee)<.0001f,"Rigid swing carries the authored knee without extra ankle-axis twist");
                check(Vector3.Distance(rig.bones[2].node.position,target)<.0001f,"Directional regression still reaches its ankle goal");
            }
        }

        using(var rig=Rig(new(0,-.4f,.01f),new(0,-.37f,-.02f),-3,160))
        {
            var reverse=(rig.bones[1].rest+Quaternion.AngleAxis(80,new(1,0,0))*(rig.bones[2].rest-rig.bones[1].rest))*-1;
            foreach(float epsilon in new[]{-.00001f,0f,.00001f})
            {
                rig.Evaluate(Goal(rig,reverse+new Vector3(epsilon,0,0)),0);
                var target=reverse+new Vector3(epsilon,0,0);
                var knee=rig.bones[1].node.position;
                check(float.IsFinite(knee.x)&&float.IsFinite(knee.y)&&float.IsFinite(knee.z),"Exact and near-antipodal goals produce finite knees");
                check(Vector3.Distance(rig.bones[2].node.position,target)<.0001f,"Antipodal fallback reaches its goal");
                rig.Evaluate(Goal(rig,new(.2f,-.3f,.1f)),0);rig.Evaluate(Goal(rig,target),0);
                check(Vector3.Distance(rig.bones[1].node.position,knee)<.00001f,"Antipodal fallback is deterministic after seeking");
            }
        }

        // VMD foot/toe switches are independent; disabled legs keep FK while
        // toe IK can still rotate the ankle, and vice versa on the next frame.
        using(var rig=Rig(new(0,-.4f,.01f),new(0,-.37f,-.02f),-3,160))
        {
            var toe=new MmdSkeleton.Bone{name="左つま先",parent=2,rest=rig.bones[2].rest+new Vector3(0,-.02f,.15f),tail=-1};
            var goal=new MmdSkeleton.Bone{name="左つま先ＩＫ",parent=3,rest=toe.rest,tail=-1};
            rig.Dispose();rig.bones.Add(toe);rig.byName.Add(toe.name,toe);rig.bones.Add(goal);rig.byName.Add(goal.name,goal);
            rig.iks.Add(new MmdSkeleton.Ik{target=5,effector=4,links=[2],limit=45,iterations=20});rig.CreateTransforms();
            string path=Path.Combine(Path.GetTempPath(),"sfm-ik-switch-"+Guid.NewGuid()+".vmd");
            try
            {
                using(var writer=new BinaryWriter(File.Create(path)))
                {
                    void Text(string value,int size){var bytes=Encoding.GetEncoding(932).GetBytes(value);writer.Write(bytes);writer.Write(new byte[size-bytes.Length]);}
                    Text("Vocaloid Motion Data 0002",30);Text("switch",20);writer.Write(2u);
                    foreach(string name in new[]{"左足ＩＫ","左つま先ＩＫ"})
                    {
                        Text(name,15);writer.Write(0u);writer.Write(0f);writer.Write(.2f);writer.Write(0f);
                        writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(1f);writer.Write(new byte[64]);
                    }
                    for(int section=0;section<4;section++)writer.Write(0u);
                    writer.Write(2u);
                    for(uint frame=0;frame<2;frame++)
                    {
                        writer.Write(frame);writer.Write((byte)1);writer.Write(2u);
                        Text("左足ＩＫ",20);writer.Write(frame==1);Text("左つま先ＩＫ",20);writer.Write(frame==0);
                    }
                }
                var switches=MmdMotion.Read(path);rig.Evaluate(switches,0);
                check(Quaternion.Angle(rig.bones[1].node.localRotation,Quaternion.identity)<.01f,"Foot IK switch off leaves FK knee unchanged");
                check(Quaternion.Angle(rig.bones[2].node.localRotation,Quaternion.identity)>1,"Toe IK remains active while foot IK is off");
                rig.Evaluate(switches,1);
                check(Quaternion.Angle(rig.bones[1].node.localRotation,Quaternion.identity)>1,"Foot IK switch on invokes the analytic solve");
                check(Quaternion.Angle(rig.bones[2].node.localRotation,Quaternion.identity)<.01f,"Toe IK switch off retains authored ankle rotation");
            }
            finally{File.Delete(path);}
        }

        // Historical runtime geometry reproducing the November addiction bug.
        const string hips="Armature/Hips";
        var reference=geometry.ToDictionary(p=>p.Key,p=>new System.Numerics.Vector3(p.Value.x,p.Value.y,p.Value.z));
        reference[hips]=new(0,10.0136f*.08f,.4073f*.08f);
        foreach(string suffix in new[]{"_L","_R"})
        {
            float x=suffix=="_L"?-.7815f:.7815f;string leg=hips+"/UpperLeg"+suffix,knee=leg+"/LowerLeg"+suffix,foot=knee+"/Foot"+suffix;
            reference[leg]=new System.Numerics.Vector3(x,8.9125f,.2610f)*.08f;
            reference[knee]=new System.Numerics.Vector3(x,5.1245f,.2239f)*.08f;
            reference[foot]=new System.Numerics.Vector3(x,1.1261f,-.1752f)*.08f;
            reference[foot+"/Toe"+suffix]=reference[foot]+new System.Numerics.Vector3(0,-.04f,.1f);
        }
        reference=reference.ToDictionary(p=>p.Key,p=>p.Value*1.125f);
        using(var rig=GameMmdSkeleton.CreateData(reference))
        {
            rig.CreateTransforms();rig.useIk=true;
            // Reachable floor goal with a lowered hip previously left the
            // near-straight right knee about 3cm below its goal on frame zero.
            var motion=new MmdMotion();
            motion.tracks["センター"]=[new(){q=Quaternion.identity,p=new(0,-.45f,0),interpolation=new byte[64]}];
            foreach(string side in new[]{"左","右"})motion.tracks[side+"足ＩＫ"]=[new(){q=Quaternion.identity,interpolation=new byte[64]}];
            rig.Evaluate(motion,0);
            foreach(string side in new[]{"左","右"})
                check(Vector3.Distance(rig.byName[side+"足首"].node.position,rig.byName[side+"足ＩＫ"].node.position)*.08f<.001f,"Near-straight addiction-style start reaches both foot goals");
        }
        if(resources==null)return;
        foreach(string file in Directory.GetFiles(resources,"motion.vmd",SearchOption.AllDirectories).Order())
        {
            var motion=MmdMotion.Read(file);
            using var rig=GameMmdSkeleton.CreateData(reference);rig.CreateTransforms();rig.useIk=true;
            using var legacy=rig.Instantiate();legacy.useIk=true;
            // Frozen poles below predate support for waist-cancel inheritance.
            // Retain their original input hierarchy to keep testing that solver
            // regression; they cannot grade a different authored FK hint.
            // Frozen diagnostic comparison: 800-step atan2 CCD converged within
            // 0.1mm at these frames. Broad bounds catch the reported large pole
            // deviation; CCD is not the author's original model or exact pose.
            (int frame,string side,Vector3 pole,float tolerance)[] directions=Path.GetFileName(Path.GetDirectoryName(file)) switch
            {
                "盛装登场"=>[
                    (1056,"左",new(.37567213f,-.05935823f,-.92484975f),50),
                    (1371,"左",new(.76152647f,.33923885f,-.552263f),50)],
                "腰振りダンス"=>[
                    (5448,"左",new(.83011645f,-.1304248f,-.54212165f),30),
                    (5448,"右",new(-.830118f,-.13042395f,-.5421196f),30),
                    (5451,"左",new(.8600223f,.25749415f,-.4405206f),30),
                    (5451,"右",new(-.8598916f,.25933838f,-.4396932f),30)],
                _=>[]
            };
            double maxReachableError=0,maxExcess=0;int unreachable=0;
            for(int frame=0;frame<=motion.lastFrame;frame++)
            {
                rig.Evaluate(motion,frame);
                foreach(string side in new[]{"左","右"})
                {
                    if(!motion.tracks.ContainsKey(side+"足ＩＫ")||!motion.IkEnabled(side+"足ＩＫ",frame))continue;
                    var hip=rig.byName[side+"足"].node;var knee=rig.byName[side+"ひざ"].node;var ankle=rig.byName[side+"足首"].node;var goal=rig.byName[side+"足ＩＫ"].node;
                    float a=Vector3.Distance(hip.position,knee.position)*.08f,b=Vector3.Distance(knee.position,ankle.position)*.08f;
                    float distance=Vector3.Distance(hip.position,goal.position)*.08f,error=Vector3.Distance(ankle.position,goal.position)*.08f;
                    float unavoidable=Math.Max(0,Math.Max(distance-a-b,Math.Abs(a-b)-distance));
                    if(unavoidable>.0001f)unreachable++;else maxReachableError=Math.Max(maxReachableError,error);
                    maxExcess=Math.Max(maxExcess,error-unavoidable);
                    check(error-unavoidable<.001f,$"Real VMD closest-target error <=1mm: {file} frame={frame} side={side} error={error} unavoidable={unavoidable}");
                    var limit=rig.iks.First(i=>i.effector==rig.bones.IndexOf(rig.byName[side+"足首"]));
                    double hinge=2*Math.Atan2(knee.localRotation.x,knee.localRotation.w)*180/Math.PI;
                    check(hinge>=limit.kneeMin-.001&&hinge<=limit.kneeMax+.001,"Real VMD knee stays within anatomical limits");
                    foreach(var direction in directions)
                        if(direction.frame==frame&&direction.side==side)
                        {
                            legacy.Evaluate(motion,frame);
                            var lh=legacy.byName[side+"足"].node;var lk=legacy.byName[side+"ひざ"].node;var la=legacy.byName[side+"足首"].node;
                            var axis=(la.position-lh.position).normalized;
                            var pole=lk.position-lh.position;pole-=axis*Vector3.Dot(pole,axis);
                            float angle=Vector3.Angle(pole,direction.pole);
                            check(angle<direction.tolerance,$"Original hierarchy retains reported knee-pole regression: {file} frame={frame} side={side} angle={angle}");
                            Console.WriteLine($"LEG_DIRECTION {Path.GetFileName(Path.GetDirectoryName(file))} frame={frame} side={side}: diagnosticCcdAngle={angle:F3}deg");
                        }
                }
            }
            Console.WriteLine($"LEG_REGRESSION {Path.GetFileName(Path.GetDirectoryName(file))}: maxReachable={maxReachableError:F7}m maxExcess={maxExcess:F7}m unreachableSamples={unreachable}");
        }
    }
}
