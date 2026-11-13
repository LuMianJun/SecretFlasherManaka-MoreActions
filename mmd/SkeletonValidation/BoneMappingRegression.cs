using UnityEngine;
using SFM.Mmd;
using SFM.Actions;

internal static class BoneMappingRegression
{
    internal static void Run(Action<bool,string> check,Dictionary<string,Vector3> positions,
        Func<IReadOnlyDictionary<string,Vector3>,IReadOnlyDictionary<string,Matrix4x4>,(GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer)> avatar,string resources)
    {
        const string hips="Armature/Hips",chest=hips+"/Spine/Chest",head=chest+"/Neck/Head";
        const string breastControl=chest+"/BreastRotateL",breastPath=breastControl+"/Breast_L",unknownPath=head+"/NotInFixedPreset";
        var presetPaths=MotionRig.BonePaths().ToArray();
        var fullMap=MmdBoneMap.Create(presetPaths);
        check(fullMap.Count==presetPaths.Length,"The shared JSON supplies every fixed-preset MMD mapping");
        foreach(string s in new[]{"_L","_R"})
        {
            string side=s=="_L"?"左":"右",arm=chest+"/Shoulder"+s+"/UpperArm"+s,lower=arm+"/LowerArm"+s;
            string upperTwist=arm+"/UpperArm_twist"+s,lowerTwist=lower+"/LowerArm_twist"+s;
            check(fullMap[arm]==side+"腕"&&fullMap[lower]==side+"ひじ","Full models preserve separate arm and twist tracks: "+s);
            var noUpperTwist=MmdBoneMap.Create(presetPaths.Where(p=>p!=upperTwist));
            check(noUpperTwist[arm]==side+"腕捩"&&noUpperTwist[lower]==side+"ひじ","Missing upper twist applies only its JSON fallback: "+s);
            var noLowerTwist=MmdBoneMap.Create(presetPaths.Where(p=>p!=lowerTwist));
            check(noLowerTwist[arm]==side+"腕"&&noLowerTwist[lower]==side+"手捩","Missing lower twist applies only its JSON fallback: "+s);
            var reduced=MmdBoneMap.Create(presetPaths.Where(p=>p!=upperTwist&&p!=lowerTwist));
            check(reduced[arm]==side+"腕捩"&&reduced[lower]==side+"手捩","Reduced models retain both inherited twist rotations: "+s);
            var unrelatedTwist=MmdBoneMap.Create(presetPaths.Where(p=>p!=upperTwist).Append("Offline/UpperArm_twist"+s));
            check(unrelatedTwist[arm]==side+"腕捩","An unrelated matching leaf cannot satisfy a preset fallback dependency: "+s);
        }
        var toolMap=MmdBoneMap.Create(new[]{"Offline/Head","Offline/Unique","Offline/A/Repeated","Offline/B/Repeated"});
        check(toolMap["Offline/Head"]=="Head"&&toolMap["Offline/Unique"]=="Unique","Internal custom topologies retain unique native leaf names without preset guesses");
        check(toolMap["Offline/A/Repeated"]=="Offline/A/Repeated"&&toolMap["Offline/B/Repeated"]=="Offline/B/Repeated","Internal duplicate leaf names retain their full paths");
        var geometry=new Dictionary<string,Vector3>(positions){[head+"/LeftEye"]=new(.03f,1.68f,.05f),[head+"/RightEye"]=new(-.03f,1.68f,.05f),[breastControl]=new(.07f,1.36f,.08f),[breastPath]=new(.07f,1.36f,.08f),[hips+"/Butt_L"]=new(.08f,.95f,-.06f),[head+"/Tang"]=new(0,1.64f,.05f),[unknownPath]=new(0,1.7f,0)};
        foreach(string s in new[]{"_L","_R"})
        {
            string arm=chest+"/Shoulder"+s+"/UpperArm"+s,lower=arm+"/LowerArm"+s;
            geometry[arm+"/UpperArm_twist"+s]=(positions[arm]+positions[lower])*.5f;
            geometry[lower+"/LowerArm_twist"+s]=(positions[lower]+positions[lower+"/Hand"+s])*.5f;
        }
        var matrices=geometry.ToDictionary(p=>p.Key,p=>Matrix4x4.TRS(p.Value,Quaternion.AngleAxis(p.Key.Length%7*10,Vector3.up),Vector3.one));
        try
        {
            var actor=avatar(geometry,matrices);
            var snapshot=MmdTargetSnapshot.Read(actor.avatar);
            check(snapshot.Rest.Keys.ToHashSet().SetEquals(geometry.Keys.Where(MotionRig.IsSupportedPath)),"Only fixed-preset body bones bind, including present eyes and secondary bones");
            check(!snapshot.Bones.ContainsKey(unknownPath),"Renderer bone lists cannot expand the fixed production topology");
            int bindReads=actor.renderer.sharedMesh.BindReads;
            var repeated=MmdTargetSnapshot.Read(actor.avatar);
            check(repeated.Rest.Keys.ToHashSet().SetEquals(snapshot.Rest.Keys)&&actor.renderer.sharedMesh.BindReads==bindReads,"A reduced model reuses static bind data without file caches or rediscovery");
            var fullBones=actor.renderer.bones.ToArray();
            int leftSlot=Array.FindIndex(fullBones,b=>b==actor.bones[head+"/LeftEye"]);
            int rightSlot=Array.FindIndex(fullBones,b=>b==actor.bones[head+"/RightEye"]);
            var partialSlots=fullBones.ToArray();partialSlots[leftSlot]=actor.bones[unknownPath];
            actor.renderer.bones=partialSlots;
            var partialSnapshot=MmdTargetSnapshot.Read(actor.avatar);
            check(!partialSnapshot.Rest.ContainsKey(head+"/LeftEye"),"An optional bone without a supported renderer slot has no invented bind pose");
            actor.renderer.bones=fullBones.ToArray();
            var expandedSnapshot=MmdTargetSnapshot.Read(actor.avatar);
            check(expandedSnapshot.Rest.ContainsKey(head+"/LeftEye"),"Adding a supported bone to a previously unmapped slot invalidates the same-mesh bind layout");
            var reordered=fullBones.ToArray();(reordered[leftSlot],reordered[rightSlot])=(reordered[rightSlot],reordered[leftSlot]);
            actor.renderer.bones=reordered;
            var reorderedSnapshot=MmdTargetSnapshot.Read(actor.avatar);
            check(Vector3.Distance(reorderedSnapshot.ReferencePositions[head+"/LeftEye"],snapshot.ReferencePositions[head+"/RightEye"])<1e-6f,
                "Reordering supported renderer bones invalidates the cached path-to-bind-index mapping");
            actor.renderer.bones=fullBones;
            var restoredSnapshot=MmdTargetSnapshot.Read(actor.avatar);
            check(restoredSnapshot.Matches(snapshot.Shape,snapshot.ShapePaths),"Restoring the original renderer layout restores its original reference geometry");
            var baseline=actor.bones.ToDictionary(p=>p.Key,p=>(p.Value.localPosition,p.Value.localRotation,p.Value.localScale));
            var motion=new MmdMotion{lastFrame=30};
            foreach(var (name,axis,angle) in new[]{("下半身",new Vector3(0,0,1),67f),("上半身2",new Vector3(1,0,0),-53f),("腰",new Vector3(0,1,0),31f),("左肩P",new Vector3(0,0,1),28f),("左腕捩",new Vector3(1,0,0),37f),("左手捩",new Vector3(1,0,0),-25f),("左足首D",new Vector3(1,0,0),24f),("左足先EX",new Vector3(1,0,0),19f),("両目",Vector3.up,12f),("Breast_L",Vector3.up,23f),("Tang",new Vector3(1,0,0),13f)})
                motion.tracks[name]=[new(){q=Quaternion.AngleAxis(angle,axis),interpolation=new byte[64]}];
            motion.tracks["上半身"]=[new(){q=Quaternion.identity,p=new(.15f,-.2f,.3f),interpolation=new byte[64]}];
            motion.tracks["左肩"]=[new(){q=Quaternion.identity,p=new(.1f,.2f,0),interpolation=new byte[64]}];
            var rig=MmdRigCache.Rent(actor.avatar,MmdReferencePose.T);MmdRigCache.Return(rig);
            check(rig.bindings.All(b=>b.sourceBone==rig.source.byName[b.source]&&(b.parent==null||b.parentSourceBone==b.parent.sourceBone)),"Bindings retain direct references to their own source and parent source bones");
            using(var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),motion,MmdReferencePose.T))
            {
                pose.Apply(0);
                foreach(var b in rig.bindings.Where(b=>!b.optional||b.twist||motion.tracks.ContainsKey(b.source)||b.source=="左目"||b.source=="右目"))
                {
                    var expected=rig.hip.position+actor.avatar.transform.rotation*(rig.source.byName[b.source].node.position-rig.source.byName["下半身"].node.position)*rig.scale;
                    check(Vector3.Distance(b.live.position,expected)<.00003f,"Whole-chain pivot mapping: "+b.path+" error="+Vector3.Distance(b.live.position,expected));
                    check(Quaternion.Angle(b.live.rotation,actor.avatar.transform.rotation*rig.source.byName[b.source].node.rotation*b.neutral)<.1f,"Whole-chain orientation mapping: "+b.path);
                }
                check(Quaternion.Angle(actor.bones[head+"/LeftEye"].localRotation,baseline[head+"/LeftEye"].localRotation)>1,"Combined eye track drives actual eye bones");
                check(Quaternion.Angle(actor.bones[hips+"/Butt_L"].localRotation,baseline[hips+"/Butt_L"].localRotation)<.1f,"Unkeyed secondary bones retain native ownership");
                var result=actor.bones.ToDictionary(p=>p.Key,p=>p.Value.position);pose.Apply(0);
                check(result.All(p=>Vector3.Distance(actor.bones[p.Key].position,p.Value)<.00001f),"Whole-chain position corrections do not accumulate on repeated Apply");
            }
            foreach(var p in baseline)
                check(Vector3.Distance(actor.bones[p.Key].localPosition,p.Value.localPosition)<.00001f&&Quaternion.Angle(actor.bones[p.Key].localRotation,p.Value.localRotation)<.1f,"Stopping restores every owned bone position and rotation: "+p.Key);
            using(var source=GameMmdSkeleton.CreateData(snapshot.Positions))
            {
                source.CreateTransforms();source.useIk=true;
                motion.tracks["左足ＩＫ"]=[new(){q=Quaternion.identity,p=new(0,2,1),interpolation=new byte[64]}];
                motion.tracks["左つま先ＩＫ"]=[new(){q=Quaternion.identity,interpolation=new byte[64]}];
                source.Evaluate(motion,0);
                check(Quaternion.Angle(source.byName["左足首D"].node.localRotation,motion.Sample("左足首D",0).q*source.byName["左足首"].node.localRotation)<.1f,"D ankle appends post-toe-IK rotation in user-times-inherited order");
                check(Quaternion.Angle(source.byName["左肩C"].node.localRotation,Quaternion.Inverse(source.byName["左肩P"].node.localRotation))<.1f,"Shoulder cancellation inherits inverse P rotation");
                check(Quaternion.Angle(source.byName["腰キャンセル左"].node.localRotation,Quaternion.Inverse(source.byName["腰"].node.localRotation))<.1f,"Waist cancellation inherits inverse waist rotation");
                var simple=new MmdMotion();simple.tracks["左親指１"]=[new(){q=Quaternion.AngleAxis(25,Vector3.up)}];simple.tracks["左親指２"]=[new(){q=Quaternion.AngleAxis(40,Vector3.up)}];source.Evaluate(simple,0);
                check(Quaternion.Angle(source.byName["左親指０"].node.localRotation,simple.Sample("左親指１",0).q)<.1f&&Quaternion.Angle(source.byName["左親指１"].node.localRotation,simple.Sample("左親指２",0).q)<.1f,"Two-joint thumb motion drives proximal and intermediate rather than skipping proximal");
            }
            foreach(string value in new[]{"左足IK","右足ＩＫ親","左人差指1","上半身２","左腕捩れ"})
                check(GameMmdSkeleton.SupportedNames().Contains(MmdBoneMap.Normalize(value)),"Known bone spelling normalized: "+value);
            {
                var aliasMotion=new MmdMotion{lastFrame=30};aliasMotion.tracks["LeftEye"]=[new(){q=Quaternion.AngleAxis(17,Vector3.up)}];
                using var eyePose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),aliasMotion,MmdReferencePose.T);
                eyePose.Apply(0);
                check(Quaternion.Angle(actor.bones[head+"/LeftEye"].localRotation,baseline[head+"/LeftEye"].localRotation)>1,"Actual native bone names bind to their canonical eye track");
            }
            {
                var aliases=new MmdMotion();aliases.tracks["truncated"]=[new(){q=Quaternion.identity}];
                var collision=aliases.BindAliases(new[]{new KeyValuePair<string,string>("truncated","a"),new("truncated","b")});
                check(!collision.tracks.ContainsKey("a")&&!collision.tracks.ContainsKey("b"),"Ambiguous 15-byte aliases never select a guessed bone");
            }
            if(resources!=null)
            foreach(var referencePose in new[]{MmdReferencePose.A,MmdReferencePose.T})
            foreach(var file in Directory.GetFiles(resources,"motion.vmd",SearchOption.AllDirectories))
            {
                var real=MmdMotion.Read(file);double maximum=0;
                rig=MmdRigCache.Rent(actor.avatar,referencePose);MmdRigCache.Return(rig);
                using var pose=new RuntimeVmdPose(actor.avatar,new ActionDefinition(),real,referencePose);
                for(float t=0;t<=real.lastFrame/30f;t+=.5f)
                {
                    pose.Apply(t);
                    foreach(var b in rig.bindings.Where(b=>!b.optional||b.twist))
                    {
                        var expected=rig.hip.position+actor.avatar.transform.rotation*(rig.source.byName[b.source].node.position-rig.source.byName["下半身"].node.position)*rig.scale;
                        double error=Vector3.Distance(b.live.position,expected);maximum=Math.Max(maximum,error);
                        check(error<.0001,"Real VMD whole body positions within 0.1mm: "+Path.GetFileName(Path.GetDirectoryName(file))+" t="+t+" bone="+b.source+" error="+error);
                    }
                }
                Console.WriteLine("BONE_MAPPING "+referencePose+" "+Path.GetFileName(Path.GetDirectoryName(file))+" maxPositionErrorM="+maximum);
            }
            MmdRigCache.Clear();
            var customized=avatar(geometry,matrices);var breast=customized.bones[breastPath];
            var control=customized.bones[breastControl];
            var originalBones=customized.renderer.bones;var originalBind=customized.renderer.sharedMesh.bindposes;
            var boundIndices=Enumerable.Range(0,originalBones.Length).Where(i=>originalBones[i]!=control).ToArray();
            customized.renderer.bones=boundIndices.Select(i=>originalBones[i]).ToArray();
            customized.renderer.sharedMesh.bindposes=boundIndices.Select(i=>originalBind[i]).ToArray();
            control.localRotation=Quaternion.AngleAxis(27,Vector3.up);
            var customizedSnapshot=MmdTargetSnapshot.Read(customized.avatar);
            check(customizedSnapshot.Bones.ContainsKey(breastControl)&&!customizedSnapshot.Rest.ContainsKey(breastControl),"Known native control parents bind as references without inventing an absent mesh bind pose");
            bool rejected=false;
            try{using var missingControl=new RuntimeVmdPose(customized.avatar,new ActionDefinition{BonePaths=[breastControl]},new MmdMotion{lastFrame=30},MmdReferencePose.T);}
            catch(System.IO.InvalidDataException){rejected=true;}
            check(rejected,"Explicit MMD selection of a preset control without a real bind pose remains rejected");
            var nativePosition=breast.position;var nativeRotation=breast.rotation;
            var customKeys=new MmdMotion{lastFrame=30};var authored=Quaternion.AngleAxis(23,new Vector3(1,0,0));customKeys.tracks["Breast_L"]=[new(){q=authored}];
            using(var pose=new RuntimeVmdPose(customized.avatar,new ActionDefinition(),customKeys,MmdReferencePose.T))
            {
                pose.Apply(0);
                check(Vector3.Distance(breast.position,nativePosition)<.00001f,"Native breast control parent retains customized pivot");
                check(Quaternion.Angle(breast.rotation,authored*nativeRotation)<.1f,"Native breast control parent retains customization rotation while adding authored track");
            }
            check(Vector3.Distance(breast.position,nativePosition)<.00001f&&Quaternion.Angle(breast.rotation,nativeRotation)<.1f,"Secondary bone restoration preserves unmapped customization parent");
            UnityEngine.Object.Destroy(customized.avatar);
            MmdRigCache.Clear();UnityEngine.Object.Destroy(actor.avatar);
        }
        finally{MmdRigCache.Clear();}
    }
}
