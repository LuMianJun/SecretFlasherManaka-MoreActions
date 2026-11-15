using System.Text.Json;
using SFM.Actions;
using SFM.Mmd;
using SFM.Mmd.Upstream;
using UnityEngine;

// Functional preservation checks use the original 80-path native hierarchy.
// Standard-only equivalence remains in UpstreamCompatibilityRegression; these
// checks require the restored extension controls to produce observable output.
internal static class AdditionalTracksRegression
{
    const string Hips="Armature/Hips",Chest=Hips+"/Spine/Chest",Head=Chest+"/Neck/Head";
    readonly record struct Local(Vector3 P,Quaternion Q,Vector3 S)
    {
        internal Local(Transform node):this(node.localPosition,node.localRotation,node.localScale){}
        internal bool Matches(Transform node)=>Vector3.Distance(P,node.localPosition)<.00005f
            &&Quaternion.Angle(Q,node.localRotation)<.1f&&Vector3.Distance(S,node.localScale)<.00001f;
    }
    readonly record struct World(Vector3 P,Quaternion Q)
    {
        internal World(Transform node):this(node.position,node.rotation){}
        internal bool Matches(Transform node)=>Vector3.Distance(P,node.position)<.00005f&&Quaternion.Angle(Q,node.rotation)<.1f;
    }

    internal static void Run(Action<bool,string> check)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","native-model-bind.json")));
        var fixture=document.RootElement;
        CheckNeutralTracks(check,fixture);
        CheckActivityClassification(check,fixture);
        CheckNeutralNativeOwnership(check,fixture);
        CheckNativeOptionalSources(check,fixture);
        CheckEyes(check,fixture);
        CheckSemanticTwists(check,fixture);
        CheckIndependentTorsoControls(check,fixture);
        CheckHeadWritebackOrder(check,fixture);
        CheckTurnedLegTargets(check,fixture);
        CheckHipAnchor(check,fixture);
        CheckWaistBeforeIk(check,fixture);
        CheckPureVirtualTracks(check,fixture);
        CheckUpperBodyGrant(check,fixture);
        CheckDeformGrants(check,fixture);
        CheckThumbZeroMode(check,fixture);
        CheckTwoJointThumbCore(check,fixture);
        CheckThumbNativeDelta(check,fixture);
        CheckOptionalParentSelection(check,fixture);
        CheckUnkeyedOwnership(check,fixture);
        CheckMasksAndRestore(check,fixture);
        CheckNativeParentScale(check,fixture);
        MmdRigCache.Clear();
    }

    static MmdMotion Motion()
    {
        var result=new MmdMotion{lastFrame=30};
        result.tracks["センター"]=[Key(0),Key(30)];
        result.RawIkFrames.Add((0,true,new(){new("左足ＩＫ",false),new("右足ＩＫ",false),
            new("左つま先ＩＫ",false),new("右つま先ＩＫ",false)}));
        return result;
    }
    static MmdMotion.Key Key(uint frame,Quaternion? q=null,Vector3 p=default)
    {
        var interpolation=new byte[64];
        for(int channel=0;channel<4;channel++)
        {interpolation[channel]=20;interpolation[channel+4]=20;interpolation[channel+8]=107;interpolation[channel+12]=107;}
        return new(){frame=frame,p=p,q=q??Quaternion.identity,interpolation=interpolation};
    }
    static void Track(MmdMotion motion,string name,Quaternion q,Vector3 p=default)
        =>motion.tracks[name]=[Key(0),Key(30,q,p)];
    static string Leaf(string path)=>path[(path.LastIndexOf('/')+1)..];
    static Dictionary<string,Local> Locals(NativeBundleRegression.NativeActor actor)
        =>actor.Bones.ToDictionary(pair=>pair.Key,pair=>new Local(pair.Value));
    static Dictionary<string,World> Worlds(NativeBundleRegression.NativeActor actor)
        =>actor.Bones.ToDictionary(pair=>pair.Key,pair=>new World(pair.Value));

    static void CheckNeutralTracks(Action<bool,string> check,JsonElement fixture)
    {
        string[][] groups=[
            ["腰","腰キャンセル左","腰キャンセル右","上半身3","左肩P","右肩P","左肩C","右肩C"],
            ["左腕捩","右腕捩","左手捩","右手捩","左足D","右足D","左ひざD","右ひざD","左足首D","右足首D","左足先EX","右足先EX"],
            ["左親指０","右親指０","左親指１","右親指１","左親指２","右親指２"],
            ["両目","左目","右目","Tang","Tang.001","Butt_L","Butt_R","Breast_L","Breast_R"]
        ];
        foreach(var reference in new[]{MmdReferencePose.A,MmdReferencePose.T})
        foreach(bool ik in new[]{false,true})
        foreach(var names in groups.Append(groups.SelectMany(group=>group).ToArray()))
        {
            MmdRigCache.Clear();
            using var baselineActor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            using var neutralActor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            MmdMotion CoreMotion()
            {
                var motion=Motion();motion.lastFrame=180;
                motion.tracks["センター"]=[Key(0,Quaternion.AngleAxis(17,Vector3.up),new(.2f,.1f,.3f)),Key(30,Quaternion.AngleAxis(29,Vector3.up),new(.8f,-.2f,.5f))];
                Track(motion,"下半身",Quaternion.AngleAxis(27,Vector3.forward));
                Track(motion,"上半身",Quaternion.AngleAxis(19,Vector3.right));
                Track(motion,"左腕",Quaternion.AngleAxis(31,Vector3.up));
                Track(motion,"右足",Quaternion.AngleAxis(-23,Vector3.forward));
                if(ik)motion.RawIkFrames.Clear();
                return motion;
            }
            var baseline=CoreMotion();var neutral=CoreMotion();
            foreach(string name in names)
            {
                neutral.tracks[name]=[Key(0),Key(180,new Quaternion(0,0,0,-1))];
                // Reader-recognized eyes/thumb 1/2 extend the pinned player's
                // own frame count even without extension output. Keep those
                // same inputs in the baseline and isolate extension effects.
                if(SFM.Mmd.Upstream.Reader.VMDReader.BoneKeyFrameGroup.StringBoneNames.Contains(name))
                    baseline.tracks[name]=neutral.tracks[name];
            }
            using var original=new RuntimeVmdPose(baselineActor.Root,new ActionDefinition{WriteFaceMorphs=false},baseline,reference);
            using var augmented=new RuntimeVmdPose(neutralActor.Root,new ActionDefinition{WriteFaceMorphs=false},neutral,reference);
            float maxP=0,maxQComponent=0;int differing=0;
            foreach(float frame in new[]{0f,1f,15f,29.5f,45f,179f,7f,21f,29.5f})
            {
                original.Apply(frame/30);augmented.Apply(frame/30);
                foreach(var pair in baselineActor.Bones)
                {
                    var p=pair.Value.position;var q=pair.Value.rotation;var actual=neutralActor.Bones[pair.Key];
                    maxP=MathF.Max(maxP,Vector3.Distance(p,actual.position));
                    var aq=actual.rotation;
                    float direct=MathF.Max(MathF.Max(MathF.Abs(q.x-aq.x),MathF.Abs(q.y-aq.y)),MathF.Max(MathF.Abs(q.z-aq.z),MathF.Abs(q.w-aq.w)));
                    float opposite=MathF.Max(MathF.Max(MathF.Abs(q.x+aq.x),MathF.Abs(q.y+aq.y)),MathF.Max(MathF.Abs(q.z+aq.z),MathF.Abs(q.w+aq.w)));
                    float rotation=MathF.Min(direct,opposite);maxQComponent=MathF.Max(maxQComponent,rotation);
                    if(p.x!=actual.position.x||p.y!=actual.position.y||p.z!=actual.position.z||rotation!=0)differing++;
                }
            }
            Console.WriteLine("NEUTRAL_EXTENSION reference="+reference+" ik="+ik+" first="+names[0]+" tracks="+names.Length
                +" differing="+differing+" maxP="+maxP+" maxQComponent="+maxQComponent);
            check(differing==0,"Missing or all-neutral extension controls preserve the exact standard output, including the pinned reader's frame limit: "+reference+" ik="+ik+" "+names[0]);
        }
    }

    static void CheckActivityClassification(Action<bool,string> check,JsonElement fixture)
    {
        foreach(bool tiny in new[]{false,true})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            var motion=Motion();var shift=tiny?new Vector3(0,.000001f,0):Vector3.zero;
            var rotation=tiny?new Quaternion(0,.0000001f,0,1):Quaternion.identity;
            Hold(motion,"腰",rotation,shift);Hold(motion,"Tang",rotation,shift);
            var rig=MmdRigCache.Rent(actor.Root,MmdReferencePose.T);
            try
            {
                using var solver=new UpstreamBodySolver(rig.avatar,rig.BindMotion(motion),false);
                var additional=new MmdAdditionalTracks(rig,motion,new ActionDefinition());
                int tongue=rig.bindings.FindIndex(binding=>binding.sample.name=="Tang");
                check(additional.HasBeforeIk==tiny,
                    "Only exact neutral pre-IK controls skip the callback; tiny authored deltas remain active: "+tiny);
                check(tongue>=0&&additional.Owns(tongue)==tiny,
                    "Only exact neutral optional sources skip native ownership; tiny authored deltas remain active: "+tiny);
                check(additional.ContainsTrack("腰")&&additional.ContainsTrack("Tang"),
                    "Neutral supported tracks remain classified as consumed even when no output work is needed");
            }
            finally {MmdRigCache.Return(rig);}
        }
    }

    static void CheckNeutralNativeOwnership(Action<bool,string> check,JsonElement fixture)
    {
        MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var optional=actor.Bones.Where(pair=>!MotionRig.IsRequiredPath(pair.Key)&&!pair.Key.Contains("/Toe_",StringComparison.Ordinal)).ToArray();
        var motion=Motion();foreach(var pair in optional)Hold(motion,Leaf(pair.Key),Quaternion.identity);
        using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T);
        foreach(var pair in optional)
        {
            pair.Value.localRotation=Quaternion.AngleAxis(21,new(1,2,3))*pair.Value.localRotation;
            pair.Value.localPosition+=new Vector3(.003f,-.002f,.001f);
        }
        var current=optional.ToDictionary(pair=>pair.Key,pair=>new Local(pair.Value));
        pose.Apply(.3f);pose.Apply(.7f);
        check(current.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),
            "Neutral optional tracks retain fresh native animation exactly as missing tracks do");
        pose.Dispose();
        check(current.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),
            "Stopping neutral optional tracks does not restore older native locals");
    }

    static void CheckNativeOptionalSources(Action<bool,string> check,JsonElement fixture)
    {
        int count=0;
        foreach(string path in MotionRig.BonePaths().Where(path=>!MotionRig.IsRequiredPath(path)))
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            var native=Locals(actor);var initial=new World(actor.Bones[path]);
            var authored=Quaternion.AngleAxis(37,new(1,2,3));var translation=new Vector3(.14f,.07f,-.09f);
            var motion=Motion();Track(motion,Leaf(path),authored,translation);
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
            {
                check(pose.Apply(.5f),"Native optional source can be sampled: "+path);
                check(Quaternion.Angle(initial.Q,actor.Bones[path].rotation)>1,
                    "Every existing optional native bone receives its authored rotation: "+path);
                check(Vector3.Distance(initial.P,actor.Bones[path].position)>.0001f,
                    "Every existing optional native bone receives its authored translation: "+path);
                var heading=actor.Root.transform.rotation;
                check(Quaternion.Angle(heading*Quaternion.Slerp(Quaternion.identity,authored,.5f)*Quaternion.Inverse(heading)*initial.Q,actor.Bones[path].rotation)<.1f,
                    "A native leaf keeps the full authored rotation in the source frame: "+path);
                check(Vector3.Distance(initial.P+heading*(translation*.04f),actor.Bones[path].position)<.00005f,
                    "A native leaf adds authored translation once in world metres: "+path);
                var half=Worlds(actor);
                pose.Apply(.5f);
                check(half.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Repeated native extension output does not accumulate: "+path);
                pose.Apply(.8f);pose.Apply(.1f);pose.Apply(.5f);
                check(half.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Seeking restores the same native extension pose: "+path);
                check(native.Where(pair=>!MotionRig.IsRequiredPath(pair.Key)&&pair.Key!=path).All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),
                    "A native optional track leaves other unkeyed optional locals owned by native animation: "+path);
            }
            check(native.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Stopping restores all owned native extension locals: "+path);
            count++;
        }
        check(count==31,"Functional native-source checks cover all 31 optional preset paths");
    }

    static void CheckUnkeyedOwnership(Action<bool,string> check,JsonElement fixture)
    {
        MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var optional=actor.Bones.Where(pair=>!MotionRig.IsRequiredPath(pair.Key)&&!pair.Key.Contains("/Toe_",StringComparison.Ordinal)).ToArray();
        using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),Motion(),MmdReferencePose.T);
        // A native update arrives after action construction. No source track
        // means no ownership, including no restore of the constructor's pose.
        foreach(var pair in optional)
        {
            pair.Value.localRotation=Quaternion.AngleAxis(21,new(1,2,3))*pair.Value.localRotation;
            pair.Value.localPosition+=new Vector3(.003f,-.002f,.001f);
        }
        var current=optional.ToDictionary(pair=>pair.Key,pair=>new Local(pair.Value));
        pose.Apply(.3f);pose.Apply(.7f);
        check(current.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Unkeyed optional bones retain fresh native animation during playback");
        pose.Dispose();
        check(current.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Stopping never restores an older constructor state over unkeyed native bones");
    }

    static void Hold(MmdMotion motion,string name,Quaternion q,Vector3 p=default)
        =>motion.tracks[name]=[Key(0,q,p),Key(30,q,p)];

    static void CheckEyes(Action<bool,string> check,JsonElement fixture)
    {
        MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        string left=Head+"/LeftEye",right=Head+"/RightEye";
        var initial=Worlds(actor);var heading=actor.Root.transform.rotation;
        var combined=Quaternion.AngleAxis(17,Vector3.up);
        var leftQ=Quaternion.AngleAxis(23,Vector3.right);var rightQ=Quaternion.AngleAxis(-11,Vector3.forward);
        var motion=Motion();Hold(motion,"両目",combined);Hold(motion,"左目",leftQ);Hold(motion,"右目",rightQ);
        Quaternion solvedLeft;
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
        {
            pose.Apply(.5f);
            foreach(var pair in new[]{(path:left,q:leftQ),(path:right,q:rightQ)})
            {
                var expected=heading*(combined*pair.q)*Quaternion.Inverse(heading)*initial[pair.path].Q;
                check(Quaternion.Angle(expected,actor.Bones[pair.path].rotation)<.1f,
                    "Combined eye rotation precedes each independently authored eye rotation: "+pair.path);
                var center=(initial[left].P+initial[right].P)*.5f;
                var expectedP=center+heading*combined*Quaternion.Inverse(heading)*(initial[pair.path].P-center);
                check(Vector3.Distance(expectedP,actor.Bones[pair.path].position)<.00005f,
                    "The combined eye parent rotates original eye offsets around their native midpoint once");
            }
            solvedLeft=actor.Bones[left].rotation;
        }
        var alias=Motion();Hold(alias,"両目",combined);Hold(alias,"LeftEye",leftQ);
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),alias,MmdReferencePose.T))
        {
            pose.Apply(.5f);
            check(Quaternion.Angle(solvedLeft,actor.Bones[left].rotation)<.1f,"The original LeftEye native leaf aliases its canonical single-eye source");
        }
        var collision=Motion();Hold(collision,"LeftEye",Quaternion.AngleAxis(61,Vector3.forward));Hold(collision,"左目",leftQ);
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),collision,MmdReferencePose.T))
        {
            pose.Apply(.5f);var expected=heading*leftQ*Quaternion.Inverse(heading)*initial[left].Q;
            check(Quaternion.Angle(expected,actor.Bones[left].rotation)<.1f,"An authored canonical eye track wins over a simultaneous native leaf alias");
        }
    }

    static void CheckMasksAndRestore(Action<bool,string> check,JsonElement fixture)
    {
        foreach(var mask in new[]{ActionBodyMask.UpperBody,ActionBodyMask.LowerBody})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            var native=Locals(actor);var motion=Motion();
            foreach(string path in MotionRig.BonePaths().Where(path=>!MotionRig.IsRequiredPath(path)))
                Track(motion,Leaf(path),Quaternion.AngleAxis(31,new(1,2,3)),new(.08f,.03f,-.04f));
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition{BodyMask=mask},motion,MmdReferencePose.T))
            {
                pose.Apply(.5f);
                foreach(var pair in actor.Bones.Where(pair=>!ActionPolicy.InMask(pair.Key,mask)))
                    check(native[pair.Key].Matches(pair.Value),"A body mask excludes native extension ownership: "+mask+" "+pair.Key);
                check(actor.Bones.Where(pair=>!MotionRig.IsRequiredPath(pair.Key)&&ActionPolicy.InMask(pair.Key,mask)).Any(pair=>!native[pair.Key].Matches(pair.Value)),
                    "A body mask permits authored native extension output in its selected region: "+mask);
            }
            check(native.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Stopping a masked extension action restores owned locals: "+mask);
        }
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            string selected=Head+"/Tang";var native=Locals(actor);var motion=Motion();
            Track(motion,"Tang",Quaternion.AngleAxis(33,Vector3.right));
            Track(motion,"Butt_L",Quaternion.AngleAxis(41,Vector3.up));
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition{BonePaths=[selected]},motion,MmdReferencePose.T))
            {
                pose.Apply(.5f);
                check(!native[selected].Matches(actor.Bones[selected]),"Explicit native extension selection permits its authored source");
                check(native.Where(pair=>pair.Key!=selected).All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),
                    "Explicit native extension selection leaves every other native local untouched");
            }
            check(native.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Explicit native selection restores its original local TRS");
        }
    }

    static void CheckSemanticTwists(Action<bool,string> check,JsonElement fixture)
    {
        foreach(var side in new[]{(suffix:"_L",mmd:"左"),(suffix:"_R",mmd:"右")})
        foreach(bool forearm in new[]{false,true})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            string arm=Chest+"/Shoulder"+side.suffix+"/UpperArm"+side.suffix,elbow=arm+"/LowerArm"+side.suffix;
            string segment=forearm?elbow:arm,next=forearm?elbow+"/Hand"+side.suffix:elbow;
            string target=segment+"/"+(forearm?"LowerArm_twist":"UpperArm_twist")+side.suffix;
            var initial=Worlds(actor);var native=Locals(actor);var heading=actor.Root.transform.rotation;
            var axis=(Quaternion.Inverse(heading)*(actor.Bones[next].position-actor.Bones[segment].position)).normalized;
            var twist=Quaternion.AngleAxis(31,axis);
            var motion=Motion();Hold(motion,side.mmd+(forearm?"手捩":"腕捩"),twist);
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
            {
                pose.Apply(.5f);
                var expected=heading*twist*Quaternion.Inverse(heading)*initial[target].Q;
                check(Quaternion.Angle(expected,actor.Bones[target].rotation)<.1f,
                    "A semantic twist rotates its actual native helper around the measured segment axis: "+target);
                check(Vector3.Distance(initial[target].P,actor.Bones[target].position)<.00005f,
                    "A semantic twist keeps its native helper joint on the measured arm axis: "+target);
                check(initial[segment].Matches(actor.Bones[segment]),"A twist does not feed its inherited grant back into the upstream source segment: "+target);
                check(Vector3.Distance(initial[next].P,actor.Bones[next].position)<.00005f,
                    "The next joint stays on the measured twist axis: "+target);
                check(Quaternion.Angle(heading*twist*Quaternion.Inverse(heading)*initial[next].Q,actor.Bones[next].rotation)<.1f,
                    "The downstream arm chain inherits the semantic twist exactly once: "+target);
            }
            check(native.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Stopping semantic twist restores native helper locals: "+target);
            var perpendicular=Vector3.Cross(axis,Vector3.up).normalized;
            var swing=Motion();Hold(swing,side.mmd+(forearm?"手捩":"腕捩"),Quaternion.AngleAxis(29,perpendicular));
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),swing,MmdReferencePose.T))
            {
                pose.Apply(.5f);
                check(initial[target].Matches(actor.Bones[target]),"Semantic twist projection rejects swing around a perpendicular axis: "+target);
            }
        }
    }

    static void CheckNativeParentScale(Action<bool,string> check,JsonElement fixture)
    {
        MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        string control=Chest+"/BreastRotateL",target=control+"/Breast_L";
        var parent=actor.Bones[control];
        parent.localRotation=Quaternion.AngleAxis(27,Vector3.up)*parent.localRotation;
        parent.localScale=new(.83f,1.17f,1.09f);
        var native=Locals(actor);var pivot=actor.Bones[target].position;
        var motion=Motion();Track(motion,"Breast_L",Quaternion.AngleAxis(23,Vector3.right));
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition{BonePaths=[target]},motion,MmdReferencePose.T))
        {
            pose.Apply(.5f);
            check(native[control].Matches(parent),"A keyed secondary child preserves its unkeyed native parent's rotation and nonuniform scale");
            check(Vector3.Distance(pivot,actor.Bones[target].position)<.00005f,"Secondary rotation preserves the customized native parent pivot");
            check(!native[target].Matches(actor.Bones[target]),"A secondary track still rotates under the customized native parent");
            var result=Worlds(actor);pose.Apply(.5f);pose.Apply(.8f);pose.Apply(.5f);
            check(result.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Native parent customization does not accumulate during repeat or seek");
        }
        check(native.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Secondary output restores native customized parent and child locals");
    }

    static void CheckIndependentTorsoControls(Action<bool,string> check,JsonElement fixture)
    {
        MmdRigCache.Clear();
        using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var initial=Worlds(actor);
        var heading=actor.Root.transform.rotation;
        string spine=Hips+"/Spine", leftLeg=Hips+"/UpperLeg_L", rightLeg=Hips+"/UpperLeg_R";
        var pivot=ReferenceWaistPivot(actor,initial);
        var centerQ=Quaternion.AngleAxis(60,Vector3.right);
        var grooveQ=Quaternion.AngleAxis(-25,Vector3.up);
        var waistQ=Quaternion.AngleAxis(21,Vector3.forward);
        var lowerQ=Quaternion.AngleAxis(35,Vector3.right);
        var upperQ=Quaternion.AngleAxis(-19,Vector3.up);
        var centerP=new Vector3(.5f,-.2f,.3f);
        var grooveP=Vector3.up;
        var waistP=new Vector3(.1f,-.2f,.3f);
        var lowerP=new Vector3(-.2f,.4f,.3f);
        var leftQ=Quaternion.AngleAxis(12,Vector3.forward);
        var leftP=new Vector3(.1f,.2f,.1f);

        var center=new GameObject("CenterOracle").transform;
        try
        {
            center.position=initial[Hips].P;center.rotation=heading;
            Transform Child(string name,Transform parent,Vector3 worldP)
            {
                var child=new GameObject(name).transform;
                child.SetParent(parent,false);child.position=worldP;return child;
            }
            var groove=Child("GrooveOracle",center,initial[Hips].P);
            var waist=Child("WaistOracle",groove,pivot);
            var lower=Child("LowerOracle",waist,initial[Hips].P);
            var upper=Child("UpperOracle",waist,initial[spine].P);
            var left=Child("LeftCancelOracle",lower,initial[leftLeg].P);
            var right=Child("RightCancelOracle",lower,initial[rightLeg].P);
            center.localPosition+=heading*(centerP*.08f);center.rotation=heading*centerQ;
            groove.localPosition+=grooveP*.08f;groove.localRotation=grooveQ;
            waist.localPosition+=waistP*.08f;waist.localRotation=waistQ;
            lower.localPosition+=lowerP*.08f;lower.localRotation=lowerQ;
            upper.localRotation=upperQ;
            left.localPosition+=leftP*.08f;left.localRotation=leftQ*Quaternion.Inverse(waistQ);
            right.localRotation=Quaternion.Inverse(waistQ);

            var motion=Motion();
            Hold(motion,"センター",centerQ,centerP);Hold(motion,"グルーブ",grooveQ,grooveP);
            Hold(motion,"腰",waistQ,waistP);Hold(motion,"下半身",lowerQ,lowerP);Hold(motion,"上半身",upperQ);
            Hold(motion,"腰キャンセル左",leftQ,leftP);
            using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T);
            var displacement=initial[Hips].P-lower.position;displacement.y=0;
            foreach(float time in new[]{0f,.5f,.25f,.517f})
            {
                check(pose.Apply(time),"Combined independent torso controls can be sampled repeatedly");
                foreach(var (path,node) in new[]{(Hips,lower),(spine,upper),(leftLeg,left),(rightLeg,right)})
                {
                    var expectedP=node.position+displacement;
                    var expectedQ=node.rotation*Quaternion.Inverse(heading)*initial[path].Q;
                    check(Vector3.Distance(expectedP,actor.Bones[path].position)<.0001f,
                        "Independent torso hierarchy positions at "+path);
                    check(Quaternion.Angle(expectedQ,actor.Bones[path].rotation)<.1f,
                        "Independent torso hierarchy rotations at "+path);
                }
            }
        }
        finally {UnityEngine.Object.Destroy(center.gameObject);}
    }

    static Vector3 ReferenceWaistPivot(NativeBundleRegression.NativeActor actor,Dictionary<string,World> initial)
    {
        var heading=actor.Root.transform.rotation;
        var origin=actor.Root.transform.position;
        var hips=Quaternion.Inverse(heading)*(initial[Hips].P-origin);
        var thighs=Quaternion.Inverse(heading)*((initial[Hips+"/UpperLeg_L"].P+initial[Hips+"/UpperLeg_R"].P)*.5f-origin);
        return origin+heading*new Vector3(thighs.x,.4f*hips.y+.6f*thighs.y,-hips.y*.02f);
    }

    static void CheckHeadWritebackOrder(Action<bool,string> check,JsonElement fixture)
    {
        using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var neck=actor.Bones[Chest+"/Neck"];
        var head=actor.Bones[Head];
        var bindHead=new World(head);
        var heading=actor.Root.transform.rotation;
        var neckKey=Quaternion.AngleAxis(25,Vector3.right);
        var headKey=Quaternion.AngleAxis(-12,Vector3.up);
        var neckDelta=heading*neckKey*Quaternion.Inverse(heading);
        var expectedP=neck.position+neckDelta*(bindHead.P-neck.position);
        var expectedQ=heading*neckKey*headKey*Quaternion.Inverse(heading)*bindHead.Q;
        var motion=Motion();
        Hold(motion,"首",neckKey);
        Hold(motion,"頭",headKey);

        using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T);
        foreach(float second in new[]{0f,1f/60f,1f/30f,.25f,0f})
        {
            check(pose.Apply(second),"Constant head and neck tracks play");
            check(Vector3.Distance(expectedP,head.position)<.0001f,
                "Changing the neck writes the expected head position on the first frame");
            check(Quaternion.Angle(expectedQ,head.rotation)<.1f,
                "Changing the neck applies its rotation to the head exactly once on the first frame");
        }
    }

    static void CheckTurnedLegTargets(Action<bool,string> check,JsonElement fixture)
    {
        var turn=Quaternion.AngleAxis(180,Vector3.up);
        string[] sides=["左","右"];
        float[] frames=[0,.375f,1,1.375f,5,5.375f,12,12.375f,29];
        var feet=new[]{HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot};
        var toes=new[]{HumanBodyBones.LeftToes,HumanBodyBones.RightToes};

        List<(World Ankle,Vector3 Toe)> Sample(Quaternion yaw)
        {
            using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            var rig=MmdRigCache.Rent(actor.Root,MmdReferencePose.A);
            try
            {
                var motion=new MmdMotion{lastFrame=30};
                Hold(motion,"全ての親",yaw);
                Hold(motion,"センター",Quaternion.identity);
                foreach(var side in sides)
                {
                    Hold(motion,side+"足ＩＫ",Quaternion.Euler(15,20,5),new Vector3(0,-.1f,.12f));
                    Hold(motion,side+"つま先ＩＫ",Quaternion.identity);
                }
                using var solver=new UpstreamBodySolver(rig.avatar,rig.BindMotion(motion),
                    controlWaistPivot:rig.ControlWaistPivot);
                var result=new List<(World Ankle,Vector3 Toe)>();
                foreach(var frame in frames)
                {
                    solver.Evaluate(frame);
                    for(int side=0;side<feet.Length;side++)
                        result.Add((new World(rig.avatar.GetBoneTransform(feet[side])),
                            rig.avatar.GetBoneTransform(toes[side]).position));
                }
                return result;
            }
            finally {MmdRigCache.Return(rig);}
        }

        var original=Sample(Quaternion.identity);
        var rotated=Sample(turn);
        for(int i=0;i<original.Count;i++)
        {
            var a=original[i];var b=rotated[i];
            check(Vector3.Distance(turn*a.Ankle.P,b.Ankle.P)<.0001f,"Turning the root carries the solved ankle");
            check(Vector3.Distance(turn*a.Toe,b.Toe)<.0001f,"Turning the root carries the toe target direction");
            check(Quaternion.Angle(turn*a.Ankle.Q,b.Ankle.Q)<.1f,"Turning the root carries the entire foot orientation");
        }
    }

    static void CheckHipAnchor(Action<bool,string> check,JsonElement fixture)
    {
        using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        using var offsetActor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var offsetHip=offsetActor.Bones[Hips];
        offsetHip.position-=Vector3.up*.0206f;
        var nativeHip=offsetHip.position;
        var motion=Motion();
        Track(motion,"センター",Quaternion.identity,new Vector3(.2f,.5f,.3f));
        var sway=new Vector3(.003f,-.004f,-.002f);

        using(var original=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
        using(var shifted=new RuntimeVmdPose(offsetActor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
        {
            foreach(float second in new[]{0f,.25f,.517f,.5f})
            {
                shifted.RestoreNativeBody();
                offsetHip.position=nativeHip+sway;
                shifted.BeginNativeBody();
                shifted.EndNativeBody(true);
                sway=-sway;
                check(original.Apply(second)&&shifted.Apply(second),"Both native idle heights can play the motion");
                float hipDifference=Vector3.Distance(actor.Bones[Hips].position,offsetHip.position);
                check(actor.Bones.All(pair=>new World(pair.Value).Matches(offsetActor.Bones[pair.Key])),
                    "Native hip sway does not shift the posed body against the avatar root (hip difference "+hipDifference.ToString("R")+" m)");
                check(Vector3.Distance(original.RootDisplacement,shifted.RootDisplacement)<.00001f,
                    "Native hip sway does not shift horizontal root travel");
            }

            var before=offsetHip.position;
            var rootStep=new Vector3(.1f,.03f,-.2f);
            offsetActor.Root.transform.position+=rootStep;
            check(shifted.Apply(.5f)&&Vector3.Distance(offsetHip.position,before+rootStep)<.00001f,
                "The posed body follows the avatar root when the controller moves");
        }
        var restoredHip=offsetHip.position;
        using var rotationOnly=new RuntimeVmdPose(offsetActor.Root,new ActionDefinition{WriteHipTranslation=false},
            motion,MmdReferencePose.T);
        check(rotationOnly.Apply(.5f)&&Vector3.Distance(restoredHip,offsetHip.position)<.00001f,
            "Disabling hip translation keeps the native hip position");
    }

    static void CheckWaistBeforeIk(Action<bool,string> check,JsonElement fixture)
    {
        MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var initial=Worlds(actor);var native=Locals(actor);var heading=actor.Root.transform.rotation;
        var waist=Quaternion.AngleAxis(27,Vector3.up);var authoredCancel=Quaternion.AngleAxis(11,Vector3.right);
        var hipPivot=ReferenceWaistPivot(actor,initial);
        var waistWorld=heading*waist*Quaternion.Inverse(heading);
        var hipShift=hipPivot+waistWorld*(initial[Hips].P-hipPivot)-initial[Hips].P;
        var horizontalShift=new Vector3(hipShift.x,0,hipShift.z);
        var motion=Motion();Hold(motion,"腰",waist);Hold(motion,"腰キャンセル左",authoredCancel);
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
        {
            pose.Apply(.5f);
            foreach(string path in new[]{Hips,Hips+"/Spine",Chest,Head})
            {
                var expectedQ=heading*waist*Quaternion.Inverse(heading)*initial[path].Q;
                var expectedP=hipPivot+waistWorld*(initial[path].P-hipPivot)-horizontalShift;
                check(Quaternion.Angle(expectedQ,actor.Bones[path].rotation)<.1f,"Waist rotates the upper branch once before the leg solve: "+path);
                check(Vector3.Distance(expectedP,actor.Bones[path].position)<.0001f,"Waist preserves the upper-branch FK pivot relationship: "+path);
            }
            foreach(var side in new[]{(suffix:"_L",cancel:authoredCancel),(suffix:"_R",cancel:Quaternion.identity)})
            {
                string upper=Hips+"/UpperLeg"+side.suffix;
                var expectedP=hipPivot+waistWorld*(initial[upper].P-hipPivot)-horizontalShift;
                var expectedQ=heading*(waist*side.cancel*Quaternion.Inverse(waist))*Quaternion.Inverse(heading)*initial[upper].Q;
                check(Vector3.Distance(expectedP,actor.Bones[upper].position)<.0001f,"Waist moves the leg origin around the hip pivot: "+upper);
                check(Quaternion.Angle(expectedQ,actor.Bones[upper].rotation)<.1f,"Leg cancellation appends authored control before inverse waist: "+upper);
            }
            var expected=Worlds(actor);pose.Apply(.5f);pose.Apply(.2f);pose.Apply(.8f);pose.Apply(.5f);
            check(expected.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Pre-IK waist application is stable across repeat and seek");
        }
        check(native.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Stopping a waist action restores all native locals");

        var ikMotion=Motion();ikMotion.RawIkFrames.Clear();Hold(ikMotion,"腰",waist);
        var offset=new Vector3(.1f,.25f,-.1f);
        foreach(string side in new[]{"左","右"})Hold(ikMotion,side+"足ＩＫ",Quaternion.identity,offset);
        var originalError=new Dictionary<string,float>();
        var originalMotion=Motion();originalMotion.RawIkFrames.Clear();
        foreach(string side in new[]{"左","右"})Hold(originalMotion,side+"足ＩＫ",Quaternion.identity,offset);
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),originalMotion,MmdReferencePose.T))
        {
            pose.Apply(.5f);
            foreach(string suffix in new[]{"_L","_R"})
            {
                string foot=Hips+"/UpperLeg"+suffix+"/LowerLeg"+suffix+"/Foot"+suffix;
                originalError[foot]=Vector3.Distance(initial[foot].P+heading*(offset*.08f),actor.Bones[foot].position);
            }
        }
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),ikMotion,MmdReferencePose.T))
        {
            pose.Apply(.5f);
            foreach(string suffix in new[]{"_L","_R"})
            {
                string foot=Hips+"/UpperLeg"+suffix+"/LowerLeg"+suffix+"/Foot"+suffix;
                var target=initial[foot].P+heading*(offset*.08f)-horizontalShift;
                float error=Vector3.Distance(target,actor.Bones[foot].position);
                // The pinned analytic solver itself has a nonzero native-axis
                // residual. Retain that independently measured precision and
                // also require correction of the waist-only FK displacement.
                string upper=Hips+"/UpperLeg"+suffix;
                var movedUpper=hipPivot+waistWorld*(initial[upper].P-hipPivot)-horizontalShift;
                var withoutIk=initial[foot].P+movedUpper-initial[upper].P;
                check(error<originalError[foot]+.003f&&error<Vector3.Distance(target,withoutIk)*.5f,
                    "Leg IK corrects the waist-only FK displacement at the original solver's measured precision, without post-IK waist rotation: "+foot+" error="+error+" original="+originalError[foot]);
            }
        }
    }

    static void CheckPureVirtualTracks(Action<bool,string> check,JsonElement fixture)
    {
        foreach(var control in new[]{(source:"腰",target:Hips),(source:"上半身3",target:Chest),
            (source:"左肩P",target:Chest+"/Shoulder_L"),(source:"左親指０",target:Chest+"/Shoulder_L/UpperArm_L/LowerArm_L/Hand_L/ThumbProximal_L")})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            var initial=new World(actor.Bones[control.target]);
            var motion=new MmdMotion{lastFrame=30};
            Track(motion,control.source,Quaternion.AngleAxis(31,Vector3.up));
            using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T);
            pose.Apply(0);pose.Apply(.5f);
            check(Quaternion.Angle(initial.Q,actor.Bones[control.target].rotation)>1,
                "A virtual extension without any standard center or FK keys is evaluated: "+control.source);
            var result=Worlds(actor);pose.Apply(.8f);pose.Apply(.1f);pose.Apply(.5f);
            check(result.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Pure virtual extension seek is stable: "+control.source);
        }
    }

    static void CheckUpperBodyGrant(Action<bool,string> check,JsonElement fixture)
    {
        MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
        var initial=Worlds(actor);var heading=actor.Root.transform.rotation;
        var upper=Quaternion.AngleAxis(23,Vector3.right);var third=Quaternion.AngleAxis(29,Vector3.up);
        var original=Motion();Hold(original,"上半身2",upper);
        Dictionary<string,World> baseline;
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),original,MmdReferencePose.T))
        {pose.Apply(.5f);baseline=Worlds(actor);}
            var motion=Motion();Hold(motion,"上半身2",upper);Track(motion,"上半身3",third);
        using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
        {
            pose.Apply(.5f);
            var frame=baseline[Chest].Q*Quaternion.Inverse(initial[Chest].Q);
            var delta=frame*heading*Quaternion.Slerp(Quaternion.identity,third,.5f)*Quaternion.Inverse(heading)*Quaternion.Inverse(frame);
            foreach(string path in new[]{Chest,Head,Chest+"/Shoulder_L",Chest+"/Shoulder_R"})
            {
                check(Quaternion.Angle(delta*baseline[path].Q,actor.Bones[path].rotation)<.1f,
                    "UpperBody3 appends in the inherited upper-body source frame: "+path);
                var expected=baseline[Chest].P+delta*(baseline[path].P-baseline[Chest].P);
                check(Vector3.Distance(expected,actor.Bones[path].position)<.0001f,
                    "UpperBody3 rotates the actual upper branch about its native Chest pivot: "+path);
            }
            foreach(string path in new[]{Hips,Hips+"/Spine",Hips+"/UpperLeg_L",Hips+"/UpperLeg_R"})
                check(baseline[path].Matches(actor.Bones[path]),"UpperBody3 leaves upstream lower controls untouched: "+path);
        }
    }

    static void CheckDeformGrants(Action<bool,string> check,JsonElement fixture)
    {
        foreach(var side in new[]{(suffix:"_L",mmd:"左"),(suffix:"_R",mmd:"右")})
        foreach(int segment in new[]{0,1,2,3})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            string upper=Hips+"/UpperLeg"+side.suffix,knee=upper+"/LowerLeg"+side.suffix,foot=knee+"/Foot"+side.suffix,toe=foot+"/Toe"+side.suffix;
            string[] paths=[upper,knee,foot,toe],parents=[Hips,upper,knee,foot],names=["足D","ひざD","足首D","足先EX"];
            string target=paths[segment],parent=parents[segment];
            var initial=Worlds(actor);var heading=actor.Root.transform.rotation;
            MmdMotion CoreMotion()
            {
                var result=Motion();Hold(result,"下半身",Quaternion.AngleAxis(11,Vector3.forward));
                Hold(result,side.mmd+"足",Quaternion.AngleAxis(13,Vector3.up));
                Hold(result,side.mmd+"ひざ",Quaternion.AngleAxis(21,Vector3.right));
                Hold(result,side.mmd+"足首",Quaternion.AngleAxis(17,Vector3.forward));return result;
            }
            Dictionary<string,World> baseline;
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),CoreMotion(),MmdReferencePose.T))
            {pose.Apply(.5f);baseline=Worlds(actor);}
            var authored=Quaternion.AngleAxis(28,new(1,2,3));var shift=new Vector3(.06f,.05f,-.03f);
            var motion=CoreMotion();Track(motion,side.mmd+names[segment],authored,shift);
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
            {
                pose.Apply(.5f);
                var parentFrame=baseline[parent].Q*Quaternion.Inverse(initial[parent].Q);
                var delta=parentFrame*heading*Quaternion.Slerp(Quaternion.identity,authored,.5f)*Quaternion.Inverse(heading)*Quaternion.Inverse(parentFrame);
                var offset=parentFrame*heading*(shift*.04f);
                foreach(string path in paths.Skip(segment))
                {
                    var expectedP=baseline[target].P+delta*(baseline[path].P-baseline[target].P)+offset;
                    check(Vector3.Distance(expectedP,actor.Bones[path].position)<.0001f,
                        "Post-IK D/EX preserves the inherited native FK branch and applies authored translation once: "+side.mmd+names[segment]+" "+path);
                    check(Quaternion.Angle(delta*baseline[path].Q,actor.Bones[path].rotation)<.1f,
                        "Post-IK D/EX appends author rotation before inherited rotation in the source parent frame: "+side.mmd+names[segment]+" "+path);
                }
                foreach(string path in paths.Take(segment))
                    check(baseline[path].Matches(actor.Bones[path]),"D/EX cannot feed back into a source ancestor: "+path);
                var expected=Worlds(actor);pose.Apply(.5f);pose.Apply(.1f);pose.Apply(.8f);pose.Apply(.5f);
                check(expected.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"D/EX post-solve grants do not accumulate on repeat and seek: "+side.mmd+names[segment]);
            }
        }
    }

    static void CheckThumbZeroMode(Action<bool,string> check,JsonElement fixture)
    {
        foreach(var side in new[]{(suffix:"_L",mmd:"左"),(suffix:"_R",mmd:"右")})
        foreach(bool neutralZero in new[]{false,true})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            string hand=Chest+"/Shoulder"+side.suffix+"/UpperArm"+side.suffix+"/LowerArm"+side.suffix+"/Hand"+side.suffix;
            string proximal=hand+"/ThumbProximal"+side.suffix,intermediate=proximal+"/ThumbIntermediate"+side.suffix,distal=intermediate+"/ThumbDistal"+side.suffix;
            string[] paths=[proximal,intermediate,distal];
            Quaternion[] authored=[neutralZero?Quaternion.identity:Quaternion.AngleAxis(17,Vector3.up),Quaternion.AngleAxis(23,Vector3.right),Quaternion.AngleAxis(-19,Vector3.forward)];
            var initial=Worlds(actor);var heading=actor.Root.transform.rotation;var motion=Motion();
            for(int i=0;i<3;i++)Track(motion,side.mmd+"親指"+(char)('０'+i),authored[i]);
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
            {
                pose.Apply(.5f);Quaternion accumulated=Quaternion.identity;string parent=hand;
                foreach(var (path,index) in paths.Select((path,index)=>(path,index)))
                {
                    var parentDelta=heading*accumulated*Quaternion.Inverse(heading);
                    accumulated=accumulated*Quaternion.Slerp(Quaternion.identity,authored[index],.5f);
                    check(Quaternion.Angle(heading*accumulated*Quaternion.Inverse(heading)*initial[path].Q,actor.Bones[path].rotation)<.1f,
                        "An explicit thumb-zero track maps 0/1/2 to native proximal/intermediate/distal once: "+path);
                    var expectedPoint=actor.Bones[parent].position+parentDelta*(initial[path].P-initial[parent].P);
                    check(Vector3.Distance(expectedPoint,actor.Bones[path].position)<.0001f,
                        "Explicit thumb-zero mode retains each real native thumb segment and parent pivot: "+path);
                    parent=path;
                }
                var expected=Worlds(actor);pose.Apply(.5f);pose.Apply(.8f);pose.Apply(.1f);pose.Apply(.5f);
                check(expected.All(pair=>pair.Value.Matches(actor.Bones[pair.Key])),"Explicit thumb-zero remapping does not accumulate over core thumb output: "+side.mmd);
            }
        }
    }

    static void CheckOptionalParentSelection(Action<bool,string> check,JsonElement fixture)
    {
        foreach(bool selectParent in new[]{false,true})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            string parent=Head+"/Tang",child=parent+"/Tang.001";
            var initial=Worlds(actor);var native=Locals(actor);var heading=actor.Root.transform.rotation;
            var parentQ=Quaternion.AngleAxis(31,Vector3.up);var childQ=Quaternion.AngleAxis(19,Vector3.right);
            var shift=new Vector3(.1f,.2f,-.1f);var motion=Motion();
            Hold(motion,"Tang",parentQ);Hold(motion,"Tang.001",childQ,shift);
            using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition{BonePaths=selectParent?[parent,child]:[child]},motion,MmdReferencePose.T);
            pose.Apply(.5f);
            var parentOutput=selectParent?parentQ:Quaternion.identity;
            var expectedQ=heading*(parentQ*childQ*Quaternion.Inverse(parentQ)*parentOutput)*Quaternion.Inverse(heading)*initial[child].Q;
            var expectedP=initial[parent].P+heading*parentOutput*Quaternion.Inverse(heading)*(initial[child].P-initial[parent].P)
                +heading*parentQ*(shift*.08f);
            check(Quaternion.Angle(expectedQ,actor.Bones[child].rotation)<.1f,
                "A native child retains its authored parent source axes while the mask controls live output only: selectParent="+selectParent);
            check(Vector3.Distance(expectedP,actor.Bones[child].position)<.0001f,
                "Native child translation uses the source parent frame and only selected parent pivot output: selectParent="+selectParent);
            if(!selectParent)check(native[parent].Matches(actor.Bones[parent]),"An excluded optional parent retains its native local pose");
        }
    }

    static void CheckTwoJointThumbCore(Action<bool,string> check,JsonElement fixture)
    {
        foreach(var side in new[]{(suffix:"_L",mmd:"左"),(suffix:"_R",mmd:"右")})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            var motion=Motion();Hold(motion,side.mmd+"親指１",Quaternion.AngleAxis(19,Vector3.up));
            Hold(motion,side.mmd+"親指２",Quaternion.AngleAxis(29,Vector3.right));
            var expected=new Dictionary<string,World>();var heading=actor.Root.transform.rotation;
            var hip=actor.Bones[Hips].position;var rig=MmdRigCache.Rent(actor.Root,MmdReferencePose.T);
            try
            {
                using var original=new UpstreamBodySolver(rig.avatar,motion,false);original.Evaluate(0);original.Evaluate(15);
                foreach(var binding in rig.bindings.Where(binding=>binding.human.HasValue))
                {
                    var point=hip+heading*(binding.sample.position-rig.sampleHip.position);
                    expected[binding.path]=new World(point,heading*binding.sample.rotation);
                }
            }
            finally {MmdRigCache.Return(rig);}
            using var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T);pose.Apply(.5f);
            foreach(var pair in expected)
                check(pair.Value.Matches(actor.Bones[pair.Key]),"Without thumb zero, semantic 1/2 preserve the pinned core mapping and never double-apply source 2 to distal: "+pair.Key);
        }
    }

    static void CheckThumbNativeDelta(Action<bool,string> check,JsonElement fixture)
    {
        foreach(var side in new[]{(suffix:"_L",mmd:"左"),(suffix:"_R",mmd:"右")})
        {
            MmdRigCache.Clear();using var actor=new NativeBundleRegression.NativeActor(fixture,1.125f);
            string hand=Chest+"/Shoulder"+side.suffix+"/UpperArm"+side.suffix+"/LowerArm"+side.suffix+"/Hand"+side.suffix;
            string proximal=hand+"/ThumbProximal"+side.suffix,intermediate=proximal+"/ThumbIntermediate"+side.suffix,distal=intermediate+"/ThumbDistal"+side.suffix;
            var initial=Worlds(actor);var heading=actor.Root.transform.rotation;
            MmdMotion ThumbMotion()
            {
                var result=Motion();Hold(result,side.mmd+"親指０",Quaternion.AngleAxis(17,Vector3.up));
                Hold(result,side.mmd+"親指１",Quaternion.AngleAxis(23,Vector3.right));
                Hold(result,side.mmd+"親指２",Quaternion.AngleAxis(-19,Vector3.forward));return result;
            }
            Dictionary<string,World> baseline;
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),ThumbMotion(),MmdReferencePose.T))
            {pose.Apply(.5f);baseline=Worlds(actor);}
            var native=Quaternion.AngleAxis(37,new(1,2,3));var shift=new Vector3(.1f,.07f,-.04f);
            var motion=ThumbMotion();Hold(motion,Leaf(distal),native,shift);
            using(var pose=new RuntimeVmdPose(actor.Root,new ActionDefinition(),motion,MmdReferencePose.T))
            {
                pose.Apply(.5f);
                var frame=baseline[intermediate].Q*Quaternion.Inverse(initial[intermediate].Q)*heading;
                check(Quaternion.Angle(frame*native*Quaternion.Inverse(frame)*baseline[distal].Q,actor.Bones[distal].rotation)<.1f,
                    "Native thumb-distal rotation adds after the explicit semantic source 2 output: "+side.mmd);
                check(Vector3.Distance(baseline[distal].P+frame*(shift*.08f),actor.Bones[distal].position)<.0001f,
                    "Native thumb-distal translation preserves the semantic 0/1/2 FK endpoint: "+side.mmd);
                foreach(string path in new[]{proximal,intermediate})
                    check(baseline[path].Matches(actor.Bones[path]),"Native distal delta cannot change thumb-zero ancestors: "+path);
            }
        }
    }
}
