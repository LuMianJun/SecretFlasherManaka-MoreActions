using UnityEngine;
using SFM.Actions;
using SFM.Mmd;
using SFM.Mmd.Upstream;

// The upstream equivalence oracle is tested separately. These checks exercise
// the boundary between that solver and the game's size, axes and pose owner.
internal static class UpstreamCompatibilityRegression
{
    const string Hips="Armature/Hips", Chest=Hips+"/Spine/Chest";
    readonly struct State(Transform bone)
    {
        internal readonly Vector3 Position=bone.localPosition,Scale=bone.localScale;
        internal readonly Quaternion Rotation=bone.localRotation;
        internal bool Matches(Transform bone)=>Vector3.Distance(Position,bone.localPosition)<.00001f
            &&Vector3.Distance(Scale,bone.localScale)<.00001f&&Quaternion.Angle(Rotation,bone.localRotation)<.1f;
    }

    internal static void Run(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        var originalLog=global::Plugin.LogSource;
        try
        {
            // Use nonparallel bone axes and opposite size profiles. An adapter
            // that applies the old neutral quaternion or scale twice fails.
            foreach(var profile in new[]{(height:1f,thigh:1f,shin:1f,axes:0),
                (height:1.125f,thigh:1.3f,shin:.8f,axes:1),
                (height:.9f,thigh:.8f,shin:1.35f,axes:2)})
            foreach(var reference in new[]{MmdReferencePose.T,MmdReferencePose.A})
            foreach(float armatureAngle in new[]{-90f,90f})
            {
                MmdRigCache.Clear();
                var matrices=positions.ToDictionary(p=>p.Key,p=>Matrix4x4.TRS(p.Value,
                    profile.axes==0?Quaternion.identity:Quaternion.AngleAxis(
                        (p.Key.Length%7+1)*(profile.axes==1?17f:-23f),new(1,2,3)),Vector3.one));
                var actor=avatar(positions,matrices);
                SetNativeArmature(actor.avatar,actor.bones,armatureAngle);
                actor.avatar.transform.position=new(3.5f,-.035f,-2.25f);
                actor.avatar.transform.rotation=Quaternion.AngleAxis(73,Vector3.up);
                actor.avatar.transform.localScale=Vector3.one*profile.height;
                CustomizeLegs(actor.bones,profile.thigh,profile.shin);
                var snapshot=MmdTargetSnapshot.Read(actor.avatar);
                var saved=actor.bones.ToDictionary(p=>p.Key,p=>new State(p.Value));
                var savedRoot=new State(actor.avatar.transform);
                var nativeHipPosition=actor.bones[Hips].position;
                var motion=StandardMotion();
                using var oracle=new SolverFixture(snapshot,motion,reference);
                oracle.Solver.Evaluate(0);var oracleOrigin=oracle.Bones[HumanBodyBones.Hips].position;
                // The original Update stops at FrameCount. Its left-hand
                // limit independently checks the controller's endpoint-only
                // adapter without using that adapter to define the answer.
                oracle.Solver.Evaluate(MathF.BitDecrement(60));var oracleLoop=oracle.Bones[HumanBodyBones.Hips].position-oracleOrigin;oracleLoop.y=0;
                oracle.Solver.Reset();oracle.Solver.Evaluate(0);
                using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition{Loop=true},motion,reference))
                {
                    foreach(float frame in new[]{0f,7f,21f,45f,60f,7f,0f})
                    {
                        check(pose.Apply(frame/30),"Upstream game adapter accepts a finite sample");
                        oracle.Solver.Evaluate(frame);
                        foreach(var item in StandardPaths())
                        {
                            if(!actor.bones.TryGetValue(item.path,out var live)||!oracle.Bones.TryGetValue(item.bone,out var sample))continue;
                            var expectedPosition=actor.bones[Hips].position+actor.avatar.transform.rotation*(sample.position-oracle.Bones[HumanBodyBones.Hips].position);
                            var expectedRotation=actor.avatar.transform.rotation*sample.rotation;
                            check(Vector3.Distance(live.position,expectedPosition)<.0001f,
                                "Upstream native geometry reaches the live point once: "+reference+" "+profile+" f="+frame+" "+item.path);
                            check(Quaternion.Angle(live.rotation,expectedRotation)<.12f,
                                "Upstream native bone axes reach the live orientation once: "+reference+" "+profile+" f="+frame+" "+item.path);
                        }
                        check(savedRoot.Matches(actor.avatar.transform),"Pose leaves actor/controller root transform untouched");
                        var expectedHip=nativeHipPosition+actor.avatar.transform.rotation*Vector3.up*
                            (oracle.Bones[HumanBodyBones.Hips].position.y-oracle.ReferenceHipHeight);
                        check(Vector3.Distance(expectedHip,actor.bones[Hips].position)<.0001f,"Hips retain authored initial vertical offset against the native reference height");
                        var expectedDisplacement=actor.avatar.transform.rotation*(oracle.Bones[HumanBodyBones.Hips].position-oracleOrigin);expectedDisplacement.y=0;
                        check(Vector3.Distance(expectedDisplacement,pose.RootDisplacement)<.0001f,"Controller travel uses the upstream absolute hip displacement once");
                        check(Vector3.Distance(actor.avatar.transform.rotation*oracleLoop,pose.LoopRootDisplacement)<.0001f,"Loop travel uses the upstream cycle displacement once");
                        var state=actor.bones.ToDictionary(p=>p.Key,p=>new State(p.Value));
                        var displacement=pose.RootDisplacement;
                        pose.Apply(frame/30);
                        check(state.All(p=>p.Value.Matches(actor.bones[p.Key])),"Repeated sampling does not accumulate positions, rotations or size corrections");
                        check(Vector3.Distance(displacement,pose.RootDisplacement)<.00001f,"Repeated sampling exposes absolute controller displacement");
                        check(pose.RootDisplacement.y==0&&pose.LoopRootDisplacement.y==0,"Controller displacement stays horizontal");
                    }
                    check(pose.RootDisplacement.sqrMagnitude<.00000001f,"Nonzero first-frame travel is the controller origin");
                    check(pose.LoopRootDisplacement.sqrMagnitude>.00001f,"A moving loop exposes its per-cycle controller displacement");
                }
                check(saved.All(p=>p.Value.Matches(actor.bones[p.Key])),"Stopping restores the native local TRS of every owned bone");
                check(savedRoot.Matches(actor.avatar.transform),"Stopping preserves actor/controller root transform");
                UnityEngine.Object.Destroy(actor.avatar);
            }
            CheckNativeControlSpace(check,positions,avatar);
            CheckNativeGeometryVariants(check,positions,avatar);
            CheckExtensions(check,positions,avatar);
            CheckFileAliases(check,positions,avatar);
            CheckOwnership(check,positions,avatar);
            CheckCache(check,positions,avatar);
            CheckFailedConstruction(check,positions,avatar);
        }
        finally { global::Plugin.LogSource=originalLog;MmdRigCache.Clear(); }
    }

    static void SetNativeArmature(GameObject avatar,Dictionary<string,Transform> bones,float angle)
    {
        // Change only the native import frame: preserve each measured world
        // point and axis so the two avatars describe the same physical body.
        var world=bones.ToDictionary(p=>p.Key,p=>(p.Value.position,p.Value.rotation));
        avatar.transform.Find("Armature").localRotation=Quaternion.AngleAxis(angle,Vector3.right);
        foreach(var pair in bones)
        {pair.Value.position=world[pair.Key].position;pair.Value.rotation=world[pair.Key].rotation;}
    }

    static void CheckNativeControlSpace(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        // Model-root bind measurements from the actual Body.001 capture:
        // outputs/mmd-foot-trace-20261115/run-0144/rig.json. In particular,
        // the native legs have ~180-degree axes and the toes another axis.
        // Keep these explicit values independent of MmdTargetSnapshot and of
        // the solver oracle: copying either would hide a frame error again.
        var geometry=new Dictionary<string,Vector3>(positions);
        var matrices=positions.ToDictionary(p=>p.Key,p=>Matrix4x4.TRS(p.Value,Quaternion.identity,Vector3.one));
        foreach(var entry in new[]{
            (path:Hips+"/UpperLeg_L",p:new Vector3(-.06251738f,.7129964f,.0208794f),q:new Quaternion(.999988f,1.7566852e-9f,-1.2016089e-7f,-.004898899f)),
            (path:Hips+"/UpperLeg_R",p:new Vector3(.06251738f,.7129964f,.020879405f),q:new Quaternion(.999988f,6.1424554e-10f,1.1303969e-7f,-.004898899f)),
            (path:Hips+"/UpperLeg_L/LowerLeg_L",p:new Vector3(-.062517375f,.4099597f,.017910201f),q:new Quaternion(.9987631f,5.2457165e-7f,6.5027695e-7f,-.04972189f)),
            (path:Hips+"/UpperLeg_R/LowerLeg_R",p:new Vector3(.062517375f,.40995973f,.017910212f),q:new Quaternion(.9987631f,-5.0245654e-7f,-6.2836386e-7f,-.049721897f)),
            (path:Hips+"/UpperLeg_L/LowerLeg_L/Foot_L",p:new Vector3(-.062517025f,.09008937f,-.014017437f),q:new Quaternion(.8449511f,-4.2622088e-7f,-6.105645e-7f,.5348435f)),
            (path:Hips+"/UpperLeg_R/LowerLeg_R/Foot_R",p:new Vector3(.06251705f,.09008931f,-.014017449f),q:new Quaternion(.8449512f,2.0962561e-7f,3.9395255e-7f,.5348434f)),
            (path:Hips+"/UpperLeg_L/LowerLeg_L/Foot_L/Toe_L",p:new Vector3(-.06251703f,.042620152f,.08625297f),q:new Quaternion(-3.5653413e-6f,.69415134f,.71982914f,-3.3915094e-6f)),
            (path:Hips+"/UpperLeg_R/LowerLeg_R/Foot_R/Toe_R",p:new Vector3(.06251703f,.042620126f,.08625298f),q:new Quaternion(3.5329929e-6f,.6941514f,.7198291f,3.3603153e-6f))})
        {geometry[entry.path]=entry.p;matrices[entry.path]=Matrix4x4.TRS(entry.p,entry.q.normalized,Vector3.one);}
        foreach(float armatureAngle in new[]{-90f,90f})
        foreach(float height in new[]{.9f,1.125f})
        {
            MmdRigCache.Clear();var actor=avatar(geometry,matrices);
            SetNativeArmature(actor.avatar,actor.bones,armatureAngle);
            actor.avatar.transform.position=new(3.5f,-.035f,-2.25f);
            actor.avatar.transform.rotation=Quaternion.AngleAxis(73,Vector3.up);
            actor.avatar.transform.localScale=Vector3.one*height;
            var rootState=new State(actor.avatar.transform);
            var armatureState=new State(actor.avatar.transform.Find("Armature"));
            var rig=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);
            try
            {
                var control=rig.proxy.transform.Find("Armature");
                foreach(var node in new[]{rig.proxy.transform,control})
                    check(node.position.sqrMagnitude<1e-12f&&Quaternion.Angle(node.rotation,Quaternion.identity)<.1f
                        &&Vector3.Distance(node.localScale,Vector3.one)<1e-6f,
                        "Hidden MMD control frame is upright in world metres, independent of native Armature and height");
                check(rig.bindings.Count==51,"The physical fixture covers all 51 native Humanoid paths");
                foreach(var binding in rig.bindings)
                {
                    check(Vector3.Distance(binding.sample.position,geometry[binding.path]*height)<.00001f,
                        "Measured native geometry is scaled once before solving: "+binding.path);
                    check(Quaternion.Angle(binding.sample.rotation,matrices[binding.path].rotation)<.1f,
                        "Measured native world rest axis survives control-frame normalization: "+binding.path);
                }
            }
            finally {MmdRigCache.Return(rig);}
            var nativeWorld=actor.bones.ToDictionary(p=>p.Key,p=>(p.Value.position,p.Value.rotation));
            var zero=new MmdMotion{lastFrame=30};
            zero.tracks["センター"]=[Key(0,Quaternion.identity),Key(30,Quaternion.identity)];
            zero.RawIkFrames.Add((0,true,new(){new("左足ＩＫ",false),new("右足ＩＫ",false),
                new("左つま先ＩＫ",false),new("右つま先ＩＫ",false)}));
            using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),zero,MmdReferencePose.T))
            {
                pose.Apply(0);
                foreach(var pair in actor.bones)
                {
                    check(Vector3.Distance(pair.Value.position,nativeWorld[pair.Key].position)<.0001f,
                        "Zero motion with authored IK disabled preserves the native bind world point: "+pair.Key);
                    check(Quaternion.Angle(pair.Value.rotation,nativeWorld[pair.Key].rotation)<.1f,
                        "Zero motion with authored IK disabled preserves the native bind world axis: "+pair.Key);
                }
            }
            var motion=new MmdMotion{lastFrame=30};
            motion.tracks["センター"]=[Key(0,Quaternion.identity),Key(30,Quaternion.identity,new(1,1,1))];
            using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),motion,MmdReferencePose.T))
            {
                pose.Apply(0);var hip=actor.bones[Hips].position;
                pose.Apply(.5f);
                // One authored MMD unit is 0.08 world metres in the pinned
                // upstream player. Halfway must be 0.04 even on a 1.125 avatar;
                // derive this directly from the public unit contract, not core.
                check(Vector3.Distance(pose.RootDisplacement,actor.avatar.transform.rotation*new Vector3(.04f,0,.04f))<.00001f,
                    "Authored MMD travel is measured once, without native-axis rotation or a second metric scale");
                check(Vector3.Distance(actor.bones[Hips].position,hip+new Vector3(0,.04f,0))<.00001f,
                    "Authored MMD vertical travel stays vertical in the native world frame");
                check(rootState.Matches(actor.avatar.transform)&&armatureState.Matches(actor.avatar.transform.Find("Armature")),
                    "Normalized hidden frames preserve the live controller root and native Armature TRS");
            }
            UnityEngine.Object.Destroy(actor.avatar);
        }
    }

    static IEnumerable<(HumanBodyBones bone,string path)> StandardPaths()
    {
        yield return(HumanBodyBones.Hips,Hips);
        yield return(HumanBodyBones.Spine,Hips+"/Spine");
        yield return(HumanBodyBones.Chest,Chest);
        yield return(HumanBodyBones.Neck,Chest+"/Neck");
        yield return(HumanBodyBones.Head,Chest+"/Neck/Head");
        foreach(var side in new[]{(native:"_L",human:"Left"),(native:"_R",human:"Right")})
        {
            string shoulder=Chest+"/Shoulder"+side.native,arm=shoulder+"/UpperArm"+side.native;
            string elbow=arm+"/LowerArm"+side.native,hand=elbow+"/Hand"+side.native;
            string leg=Hips+"/UpperLeg"+side.native,knee=leg+"/LowerLeg"+side.native,foot=knee+"/Foot"+side.native;
            foreach(var value in new[]{("Shoulder",shoulder),("UpperArm",arm),("LowerArm",elbow),("Hand",hand),
                ("UpperLeg",leg),("LowerLeg",knee),("Foot",foot),("Toes",foot+"/Toe"+side.native)})
                yield return(Enum.Parse<HumanBodyBones>(side.human+value.Item1),value.Item2);
            foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
            {
                string path=hand;
                foreach(string segment in new[]{"Proximal","Intermediate","Distal"})
                {
                    path+="/"+finger+segment+side.native;
                    if(Enum.TryParse<HumanBodyBones>(side.human+finger+segment,out var bone))yield return(bone,path);
                }
            }
        }
    }

    static void CheckNativeGeometryVariants(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        MmdRigCache.Clear();
        var matrices=positions.ToDictionary(p=>p.Key,p=>Matrix4x4.TRS(p.Value,
            Quaternion.AngleAxis((p.Key.Length%7+1)*17,new(1,2,3)),Vector3.one));
        var actor=avatar(positions,matrices);
        actor.avatar.transform.position=new(3.5f,-.035f,-2.25f);
        actor.avatar.transform.rotation=Quaternion.AngleAxis(73,Vector3.up);
        actor.avatar.transform.localScale=Vector3.one*1.125f;

        // Bind matrices are mesh-local. A nonidentity Body.001 TRS must cancel
        // its matching bind transform, rather than rotate/scale the avatar a
        // second time. These matrices use the mesh skinning contract directly.
        var meshTransform=actor.renderer.transform;
        meshTransform.localPosition=new(.08f,.12f,-.2f);
        meshTransform.localRotation=Quaternion.AngleAxis(37,Vector3.up);
        meshTransform.localScale=new(1.1f,.9f,1.2f);
        var meshLocal=Matrix4x4.TRS(meshTransform.localPosition,meshTransform.localRotation,meshTransform.localScale);
        actor.renderer.sharedMesh.bindposes=actor.bones.Keys.Select(path=>matrices[path].inverse*meshLocal).ToArray();

        void CancelScale(Transform bone,Vector3 scale)
        {
            var children=Enumerable.Range(0,bone.childCount).Select(bone.GetChild).ToArray();
            bone.localScale=scale;
            var wrapper=new GameObject("PlayerBoneScaleAdjuster").transform;wrapper.SetParent(bone,false);
            wrapper.localScale=new(1/scale.x,1/scale.y,1/scale.z);
            foreach(var child in children)child.SetParent(wrapper,false);
        }
        // Include the Hips wrapper seen in the native capture, then independent
        // length customization at spine/chest and both leg segments.
        CancelScale(actor.bones[Hips],new(.83f,1.1f,1.08f));
        CancelScale(actor.bones[Hips+"/Spine"],new(.9f,1.2f,1.1f));
        actor.bones[Chest].localPosition*=1.2f;
        CustomizeLegs(actor.bones,1.3f,.8f);

        // Observe the actual native transforms after their shape procedure;
        // this expectation does not read snapshot positions or another solver.
        var inverseHeading=Quaternion.Inverse(actor.avatar.transform.rotation);
        var expected=actor.bones.ToDictionary(p=>p.Key,p=>inverseHeading*(p.Value.position-actor.avatar.transform.position));
        var snapshot=MmdTargetSnapshot.Read(actor.avatar);
        var rig=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);
        try
        {
            check(snapshot.Bones.Count==51,"Snapshot resolves complete human paths through multiple native scale wrappers");
            foreach(var binding in rig.bindings)
            {
                var measured=snapshot.Positions[binding.path];
                check(Vector3.Distance(new Vector3(measured.X,measured.Y,measured.Z),expected[binding.path])<.0001f,
                    "Snapshot matches the customized native joint including mesh-local TRS, scale cancellation and Chest coupling: "+binding.path);
                check(Vector3.Distance(binding.sample.position,expected[binding.path])<.0001f,
                    "Solver geometry matches customized native joints without another scale application: "+binding.path);
            }
        }
        finally {MmdRigCache.Return(rig);UnityEngine.Object.Destroy(actor.avatar);}
    }

    sealed class SolverFixture:IDisposable
    {
        readonly GameObject root;
        internal readonly Dictionary<HumanBodyBones,Transform> Bones=new();
        internal readonly UpstreamBodySolver Solver;
        internal readonly float ReferenceHipHeight;
        internal SolverFixture(MmdTargetSnapshot snapshot,MmdMotion motion,MmdReferencePose reference)
        {
            root=new("UpstreamCompatibilityOracle");
            var armature=new GameObject("Armature").transform;armature.SetParent(root.transform,false);
            // Independent upstream input contract: its authored translations
            // are world metres in an upright frame. The physical positions
            // below already contain the measured metric and native rest axes.
            var samples=new Dictionary<string,Transform>(StringComparer.Ordinal){["Armature"]=armature};
            foreach(string path in snapshot.Rest.Keys.OrderBy(p=>p.Count(c=>c=='/')).ThenBy(p=>p,StringComparer.Ordinal))
            {
                int slash=path.LastIndexOf('/');string parent=path[..slash];
                while(!samples.ContainsKey(parent))parent=parent[..parent.LastIndexOf('/')];
                var sample=new GameObject(path[(slash+1)..]).transform;sample.SetParent(samples[parent],false);
                var position=snapshot.Positions[path];sample.position=new(position.X,position.Y,position.Z);
                sample.rotation=snapshot.Rest[path].rotation;samples.Add(path,sample);
            }
            foreach(var item in StandardPaths())if(samples.TryGetValue(item.path,out var sample))Bones.Add(item.bone,sample);
            ReferenceHipHeight=Bones[HumanBodyBones.Hips].position.y;
            var thighs=(Bones[HumanBodyBones.LeftUpperLeg].position+Bones[HumanBodyBones.RightUpperLeg].position)*.5f;
            var waistPivot=new Vector3(thighs.x,.4f*ReferenceHipHeight+.6f*thighs.y,-ReferenceHipHeight*.02f);
            Solver=new(new UpstreamAvatar(root.transform,Bones),motion,reference==MmdReferencePose.A,controlWaistPivot:waistPivot);
        }
        public void Dispose(){try{Solver.Dispose();}finally{UnityEngine.Object.Destroy(root);}}
    }

    static void CustomizeLegs(Dictionary<string,Transform> bones,float thigh,float shin)
    {
        if(thigh==1&&shin==1)return;
        foreach(string suffix in new[]{"_L","_R"})
        {
            string leg=Hips+"/UpperLeg"+suffix,knee=leg+"/LowerLeg"+suffix,foot=knee+"/Foot"+suffix;
            foreach(var item in new[]{(path:leg,length:thigh),(path:knee,length:shin)})
            {
                var bone=bones[item.path];var children=Enumerable.Range(0,bone.childCount).Select(bone.GetChild).ToArray();
                bone.localScale=new(.9f,item.length,1.1f);
                var wrapper=new GameObject("PlayerBoneScaleAdjuster").transform;wrapper.SetParent(bone,false);
                wrapper.localScale=new(1/.9f,1/item.length,1/1.1f);
                foreach(var child in children)child.SetParent(wrapper,false);
            }
            bones[knee].localPosition*=thigh;bones[foot].localPosition*=shin;
        }
    }

    static MmdMotion StandardMotion()
    {
        var motion=new MmdMotion{lastFrame=60};
        // Keys are already in the parser's converted coordinate system.
        foreach(var (name,axis,angle) in new[]{("下半身",new Vector3(0,1,0),13f),("上半身",new Vector3(1,0,0),-17f),
            ("上半身2",new Vector3(0,0,1),23f),("首",Vector3.up,11f),("頭",new Vector3(1,0,0),-14f),
            ("左肩",new Vector3(0,0,1),9f),("右肩",new Vector3(0,0,1),-12f),
            ("左腕",new Vector3(1,1,0),37f),("右腕",new Vector3(1,1,0),-29f),
            ("左ひじ",new Vector3(0,1,0),45f),("右ひじ",new Vector3(0,1,0),33f),
            ("左手首",new Vector3(0,0,1),19f),("右手首",new Vector3(0,0,1),-22f),
            ("左足",new Vector3(0,1,0),8f),("右足",new Vector3(0,1,0),-7f),
            ("左ひざ",new Vector3(1,0,0),21f),("右ひざ",new Vector3(1,0,0),16f),
            ("左足首",new Vector3(1,0,0),14f),("右足首",new Vector3(1,0,0),-11f),
            ("左親指１",Vector3.up,12f),("右人指２",Vector3.up,18f)})
            motion.tracks[name]=[Key(0,Quaternion.AngleAxis(angle*.2f,axis)),Key(60,Quaternion.AngleAxis(angle,axis))];
        motion.tracks["センター"]=[Key(0,Quaternion.identity,new(.4f,-.6f,.2f)),Key(60,Quaternion.identity,new(1.3f,.3f,-.7f))];
        motion.tracks["グルーブ"]=[Key(0,Quaternion.identity),Key(60,Quaternion.identity,new(.1f,-.2f,.15f))];
        foreach(string side in new[]{"左","右"})
        {
            motion.tracks[side+"足ＩＫ"]=[Key(0,Quaternion.identity),Key(60,Quaternion.AngleAxis(side=="左"?17:-13,Vector3.up),new(.1f,.2f,-.1f))];
            motion.tracks[side+"つま先ＩＫ"]=[Key(0,Quaternion.identity),Key(60,Quaternion.identity,new(.02f,.04f,.03f))];
        }
        return motion;
    }

    static MmdMotion.Key Key(uint frame,Quaternion q,Vector3 p=default)
    {
        var interpolation=new byte[64];
        for(int channel=0;channel<4;channel++)
        {interpolation[channel]=20;interpolation[channel+4]=20;interpolation[channel+8]=107;interpolation[channel+12]=107;}
        return new(){frame=frame,q=q,p=p,interpolation=interpolation};
    }

    static void CheckExtensions(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        MmdRigCache.Clear();
        var actor=avatar(positions,null);var standard=StandardMotion();
        var expected=new Dictionary<float,Dictionary<string,(Vector3 p,Quaternion q)>>();
        using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),standard,MmdReferencePose.T))
        foreach(float seconds in new[]{0f,.5f,1.5f,2f})
        {pose.Apply(seconds);expected[seconds]=actor.bones.ToDictionary(p=>p.Key,p=>(p.Value.position,p.Value.rotation));}
        var extended=StandardMotion();
        // Semi-standard and native extension tracks have dedicated functional
        // checks in AdditionalTracksRegression. Only truly unknown controls
        // must be isolated from standard output here.
        string[] extensions=["UnknownMotionControl","OfflineUnsupportedBone"];
        foreach(var (name,index) in extensions.Select((name,index)=>(name,index)))
            extended.tracks[name]=[Key(0,Quaternion.AngleAxis(15+index*3,new(1,2,3)),new(.5f,-.3f,.2f)),Key(60,Quaternion.AngleAxis(-25-index*2,new(3,1,2)),new(-.2f,.4f,.3f))];
        using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),extended,MmdReferencePose.T))
        foreach(var frame in expected)
        {
            pose.Apply(frame.Key);
            foreach(var item in StandardPaths())
            {
                if(!actor.bones.TryGetValue(item.path,out var bone))continue;
                check(Vector3.Distance(bone.position,frame.Value[item.path].p)<.00005f
                    &&Quaternion.Angle(bone.rotation,frame.Value[item.path].q)<.1f,
                    "Unknown controls cannot alter upstream standard output: "+item.path+" t="+frame.Key);
            }
        }
        check(extensions.All(name=>global::Plugin.LogSource.Messages.Any(line=>line.Contains("unsupportedAnimated=")&&line.Contains(name))),
            "Animated unsupported extensions are reported instead of silently deforming standard bones");
        foreach(var side in new[]{(native:"_L",mmd:"左"),(native:"_R",mmd:"右")})
        {
            var canonical=new MmdMotion{lastFrame=30};canonical.tracks[side.mmd+"足首"]=[Key(0,Quaternion.AngleAxis(31,new(1,0,0))),Key(30,Quaternion.AngleAxis(31,new(1,0,0)))];
            var alias=new MmdMotion{lastFrame=30};alias.tracks["Foot"+side.native]=canonical.tracks[side.mmd+"足首"];
            string foot=Hips+"/UpperLeg"+side.native+"/LowerLeg"+side.native+"/Foot"+side.native;
            Quaternion expectedRotation;Vector3 expectedPosition;
            using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),canonical,MmdReferencePose.T))
            {pose.Apply(0);expectedRotation=actor.bones[foot].rotation;expectedPosition=actor.bones[foot].position;}
            using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),alias,MmdReferencePose.T))
            {
                pose.Apply(0);check(Quaternion.Angle(expectedRotation,actor.bones[foot].rotation)<.1f
                    &&Vector3.Distance(expectedPosition,actor.bones[foot].position)<.00005f,
                    "Native Foot alias selects the standard ankle, independently of old D mapping: "+side.native);
            }
        }
        UnityEngine.Object.Destroy(actor.avatar);
    }

    static void CheckOwnership(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        MmdRigCache.Clear();
        var actor=avatar(positions,null);var animator=actor.avatar.AddComponent<Animator>();
        animator.cullingMode=AnimatorCullingMode.CullCompletely;
        var accessory=new GameObject("UnownedAccessory").transform;accessory.SetParent(actor.bones[Hips],false);
        accessory.localPosition=new(.2f,.3f,.4f);var accessoryState=new State(accessory);
        string knee=Hips+"/UpperLeg_L/LowerLeg_L";
        using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),StandardMotion(),MmdReferencePose.T))
        {
            check(animator.cullingMode==AnimatorCullingMode.AlwaysAnimate,"MMD preserves native animation sampling while it owns body output");
            for(int frame=0;frame<3;frame++)
            {
                pose.RestoreNativeBody();
                actor.bones[knee].localPosition=new(.01f+frame*.001f,-.4f,.02f);
                actor.bones[knee].localRotation=Quaternion.AngleAxis(7+frame,Vector3.up);
                var raw=new State(actor.bones[knee]);
                pose.BeginNativeBody();
                using(pose.PreserveNativeCorrection(ActionBoneCorrection.Knee))
                {
                    actor.bones[knee].localPosition+=new Vector3(0,.1f,0);
                    actor.bones[knee].localRotation=Quaternion.AngleAxis(89,Vector3.up);
                }
                check(raw.Matches(actor.bones[knee]),"Native knee adjustment is scoped around the game's calculation");
                pose.EndNativeBody(true);pose.Apply(.5f);pose.Apply(.5f);pose.RestoreNativeBody();
                check(raw.Matches(actor.bones[knee]),"Withdrawing MMD restores fresh native input without adding size correction twice");
            }
            // Dispose after withdrawal must not restore an older captured pose
            // over an Animator result that arrived after the last output.
            actor.bones[knee].localRotation=Quaternion.AngleAxis(29,Vector3.up);
        }
        check(Quaternion.Angle(actor.bones[knee].localRotation,Quaternion.AngleAxis(29,Vector3.up))<.1f,"Stop after withdrawal retains newer native animation");
        check(animator.cullingMode==AnimatorCullingMode.CullCompletely,"Stop restores original Animator culling");
        check(accessoryState.Matches(accessory),"Accessory local TRS stays outside body ownership");
        foreach(var mask in new[]{ActionBodyMask.UpperBody,ActionBodyMask.LowerBody})
        {
            var saved=actor.bones.ToDictionary(p=>p.Key,p=>new State(p.Value));
            using var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition{BodyMask=mask},StandardMotion(),MmdReferencePose.T);
            pose.Apply(1);
            check(actor.bones.Where(p=>!ActionPolicy.InMask(p.Key,mask)).All(p=>saved[p.Key].Matches(p.Value)),"Body mask preserves unowned local TRS: "+mask);
            check(!pose.HasRootMotion&&pose.RootDisplacement.sqrMagnitude==0,"Partial body output does not seize controller displacement: "+mask);
        }
        UnityEngine.Object.Destroy(actor.avatar);
    }

    static void CheckFileAliases(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        MmdRigCache.Clear();var actor=avatar(positions,null);
        var files=new List<string>();
        string Write(params(string name,float degrees)[] tracks)
        {
            string path=Path.Combine(Path.GetTempPath(),"sfm-native-foot-alias-"+Guid.NewGuid().ToString("N")+".vmd");files.Add(path);
            using var writer=new BinaryWriter(File.Create(path));
            void Text(string value,int size)
            {
                var bytes=new byte[size];var encoded=System.Text.Encoding.GetEncoding(932).GetBytes(value);
                Array.Copy(encoded,bytes,Math.Min(encoded.Length,size));writer.Write(bytes);
            }
            Text("Vocaloid Motion Data 0002",30);Text("NativeAliasFixture",20);writer.Write((uint)(tracks.Length*2));
            foreach(var track in tracks)
            foreach(uint frame in new[]{0u,30u})
            {
                Text(track.name,15);writer.Write(frame);writer.Write(0f);writer.Write(0f);writer.Write(0f);
                var key=Key(frame,Quaternion.AngleAxis(track.degrees,new(1,0,0)));
                writer.Write(key.q.x);writer.Write(key.q.y);writer.Write(key.q.z);writer.Write(key.q.w);writer.Write(key.interpolation);
            }
            writer.Write(0u);writer.Write(0u);writer.Write(0u);writer.Write(0u);writer.Write(0u);return path;
        }
        try
        {
            const string foot=Hips+"/UpperLeg_L/LowerLeg_L/Foot_L";
            string canonicalFile=Write(("左足首",31));var canonical=MmdMotion.Read(canonicalFile);
            var expected=new Dictionary<float,(Vector3 p,Quaternion q,Vector3 toe)>();
            using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),canonical,MmdReferencePose.T))
            foreach(float seconds in new[]{0f,.5f})
            {pose.Apply(seconds);expected.Add(seconds,(actor.bones[foot].position,actor.bones[foot].rotation,actor.bones[foot+"/Toe_L"].position));}
            foreach(var tracks in new[]{
                new[]{("Foot_L\0pad",31f)},
                new[]{("Foot_L\0pad",83f),("左足首\0pad",31f)},
                new[]{("左足首\0pad",31f),("Foot_L\0pad",83f)}})
            {
                var file=Write(tracks);var raw=MmdMotion.Read(file);
                check(raw.RawBoneKeys.Any(key=>key.Name.Contains('\0')),"The file fixture preserves padded raw bone names before input compatibility");
                var compatible=UpstreamInputCompatibility.Apply(raw);
                check(compatible.RawBoneKeys.All(key=>!key.Name.Contains('\0')),"Input compatibility removes only name padding before rig aliases");
                var rig=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);MmdMotion bound;
                try {bound=rig.BindMotion(compatible);}finally{MmdRigCache.Return(rig);}
                var ankleKeys=bound.RawBoneKeys.Where(key=>key.Name=="左足首").Select(key=>key.Key).ToArray();
                check(ankleKeys.Length==2&&ankleKeys.All(key=>Quaternion.Angle(key.q,canonical.RawBoneKeys[0].Key.q)<.1f),
                    "Raw native alias creates the canonical ankle only when an authored standard track is absent");
                using var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),raw,MmdReferencePose.T);
                foreach(var sample in expected)
                {
                    check(pose.Apply(sample.Key),"Raw file alias playback remains usable");
                    check(Vector3.Distance(actor.bones[foot].position,sample.Value.p)<.00005f
                        &&Quaternion.Angle(actor.bones[foot].rotation,sample.Value.q)<.1f
                        &&Vector3.Distance(actor.bones[foot+"/Toe_L"].position,sample.Value.toe)<.00005f,
                        "File read, name compatibility and native alias output match the authored standard ankle");
                }
            }
        }
        finally {foreach(string file in files)File.Delete(file);UnityEngine.Object.Destroy(actor.avatar);}
    }

    static void CheckCache(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        MmdRigCache.Clear();var actor=avatar(positions,null);MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
        int builds=MmdRigCache.BuildCount,bindReads=actor.renderer.sharedMesh.BindReads;
        var firstRig=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);var firstProxy=firstRig.proxy;MmdRigCache.Return(firstRig);
        var leased=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);
        var concurrent=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);
        check(concurrent.IsUsable&&!ReferenceEquals(leased.proxy,concurrent.proxy),"Concurrent consumers have separate usable sampling hierarchies");
        var concurrentProxy=concurrent.proxy;MmdRigCache.Return(concurrent);
        check(concurrentProxy==null&&leased.IsUsable,"Returning a temporary consumer destroys only its own sampling hierarchy");
        MmdRigCache.Return(leased);builds=MmdRigCache.BuildCount;
        using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),StandardMotion(),MmdReferencePose.T))pose.Apply(.5f);
        actor.avatar.transform.position=new(4,-.035f,2);actor.avatar.transform.rotation=Quaternion.AngleAxis(137,Vector3.up);
        MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
        check(MmdRigCache.BuildCount==builds,"World movement and heading reuse existing static geometry");
        check(actor.renderer.sharedMesh.BindReads==bindReads,"Warm preparation reuses bind poses");
        using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),StandardMotion(),MmdReferencePose.T))pose.Apply(.5f);
        var warmRig=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);
        check(ReferenceEquals(firstProxy,warmRig.proxy),"A warm action reuses the original sampling hierarchy");MmdRigCache.Return(warmRig);
        actor.avatar.transform.localScale=Vector3.one*1.2f;MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
        check(MmdRigCache.BuildCount==builds+1,"Changing height invalidates geometry instead of silently reusing another scale");
        builds=MmdRigCache.BuildCount;CustomizeLegs(actor.bones,1.2f,.85f);MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
        check(MmdRigCache.BuildCount==builds+1,"Changing thigh/shin proportions invalidates solver geometry");
        using(var active=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),StandardMotion(),MmdReferencePose.T))
        {
            active.Apply(.5f);builds=MmdRigCache.BuildCount;MmdRigCache.Invalidate();MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
            check(MmdRigCache.BuildCount==builds&&active.Apply(.5f),"A body-change notification without shape changes retains the active usable solver");
            actor.avatar.transform.localScale=Vector3.one*1.35f;MmdRigCache.Invalidate();MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
            check(MmdRigCache.BuildCount==builds+1&&!active.Apply(.5f),"A changed active shape stops old geometry before it can write another pose");
        }
        using(var updated=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),StandardMotion(),MmdReferencePose.T))
            check(updated.Apply(.5f),"The replacement shape can immediately create a usable new playback");
        UnityEngine.Object.Destroy(actor.avatar);
    }

    static void CheckFailedConstruction(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,
        (GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar)
    {
        MmdRigCache.Clear();var actor=avatar(positions,null);
        var animator=actor.avatar.AddComponent<Animator>();animator.cullingMode=AnimatorCullingMode.CullCompletely;
        var native=actor.bones.ToDictionary(p=>p.Key,p=>new State(p.Value));
        try
        {
            MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
            var failedRig=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);var failedProxy=failedRig.proxy;MmdRigCache.Return(failedRig);
            // An invalid public in-memory input fails in FromMotion after the
            // body lease and upstream initial-pose setup have begun. No test
            // hook or altered production branch is needed to exercise cleanup.
            var invalid=new MmdMotion{lastFrame=30};invalid.tracks["上半身"]=null;
            Exception failure=null;
            try {using var unexpected=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),invalid,MmdReferencePose.T);}
            catch(Exception error){failure=error;}
            check(failure is ArgumentNullException,"A null bone-key list exercises the real solver-construction failure path");
            check(failedProxy==null&&!failedRig.HelpersValid,"Failed construction destroys its hidden hierarchy instead of returning it to the cache");
            check(animator.cullingMode==AnimatorCullingMode.CullCompletely,"Failed construction returns native Animator culling");
            check(native.All(p=>p.Value.Matches(actor.bones[p.Key])),"Failed construction preserves the native body's local TRS");
            check(!MmdRigCache.IsPrepared(actor.avatar,MmdReferencePose.T),"A destroyed failed-construction hierarchy is unavailable for playback");
            MmdRigCache.Prepare(actor.avatar,MmdReferencePose.T);
            check(MmdRigCache.IsPrepared(actor.avatar,MmdReferencePose.T),"Normal preparation can recover after constructor failure");
            var replacement=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);
            check(replacement.proxy!=null&&!ReferenceEquals(replacement.proxy,failedProxy),"Recovery builds a new usable hierarchy");MmdRigCache.Return(replacement);
            using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),StandardMotion(),MmdReferencePose.T))
                check(pose.Apply(.5f),"The first normal playback after failed construction applies successfully");
            check(animator.cullingMode==AnimatorCullingMode.CullCompletely&&native.All(p=>p.Value.Matches(actor.bones[p.Key])),
                "Recovered playback stops with native Animator culling and body TRS restored");
        }
        finally {UnityEngine.Object.Destroy(actor.avatar);}
    }
}
