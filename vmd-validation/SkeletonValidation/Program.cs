using System.Text;
using UnityEngine;
using Seleka2.Actions;
using Seleka2.Mmd;

try
{
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
int checks=0;
void Check(bool ok,string label) { checks++; if(!ok) throw new Exception(label); }
void Reject(Action action,string label) { bool failed=false; try { action(); } catch { failed=true; } Check(failed,label); }
bool Finite(Quaternion q)=>float.IsFinite(q.x)&&float.IsFinite(q.y)&&float.IsFinite(q.z)&&float.IsFinite(q.w)&&Math.Abs(q.sqrMagnitude()-1)<.002;
var positions=new Dictionary<string,Vector3>();
const string hips="Armature/Hips", chest=hips+"/Spine/Chest";
positions[hips]=new(0,1,0); positions[hips+"/Spine"]=new(0,1.1f,0); positions[chest]=new(0,1.35f,0);
positions[chest+"/Neck"]=new(0,1.5f,0); positions[chest+"/Neck/Head"]=new(0,1.65f,0);
foreach(var suffix in new[]{"_L","_R"})
{
    float side=suffix=="_L"?1:-1;
    string shoulder=chest+"/Shoulder"+suffix,arm=shoulder+"/UpperArm"+suffix,elbow=arm+"/LowerArm"+suffix,hand=elbow+"/Hand"+suffix;
    positions[shoulder]=new(side*.08f,1.4f,0); positions[arm]=new(side*.17f,1.4f,0); positions[elbow]=new(side*.4f,1.24f,0); positions[hand]=new(side*.6f,1.1f,0);
    int n=0;
    foreach(var finger in new[]{"Thumb","Index","Middle","Ring","Little"})
    {
        string p=hand; for(int i=0;i<3;i++) { p+="/"+finger+new[]{"Proximal","Intermediate","Distal"}[i]+suffix; positions[p]=new(side*(.62f+i*.025f),1.1f,n*.015f); } n++;
    }
    string leg=hips+"/UpperLeg"+suffix,knee=leg+"/LowerLeg"+suffix,foot=knee+"/Foot"+suffix;
    positions[leg]=new(side*.1f,.95f,0); positions[knee]=new(side*.1f,.55f,.02f); positions[foot]=new(side*.1f,.15f,0); positions[foot+"/Toe"+suffix]=new(side*.1f,.08f,.15f);
}
var bind=positions.ToDictionary(x=>x.Key,x=>Matrix4x4.TRS(x.Value,Quaternion.AngleAxis(x.Key.Length%7*10,Vector3.up),Vector3.one));
using(var data=GameMmdSkeleton.Create(bind,false))
{
    Check(data.root==null,"Data construction does not allocate Unity objects");
    Check(data.byName.Keys.ToHashSet().SetEquals(GameMmdSkeleton.SupportedNames()),"Declared supported tracks exactly match generated topology");
    Check(data.iks.Count==4,"Both feet and toes have IK chains");
    foreach(var b in data.bones)
    {
        Check(b.parent<data.bones.IndexOf(b),"Synthetic hierarchy is parent-first and acyclic");
        Check(b.rest.sqrMagnitude<float.PositiveInfinity,"All reference positions are finite");
    }
    float sourceLeg=(data.byName["左足"].rest-data.byName["左ひざ"].rest).magnitude+(data.byName["左ひざ"].rest-data.byName["左足首"].rest).magnitude;
    float targetLeg=(positions[hips+"/UpperLeg_L"]-positions[hips+"/UpperLeg_L/LowerLeg_L"]).magnitude+(positions[hips+"/UpperLeg_L/LowerLeg_L"]-positions[hips+"/UpperLeg_L/LowerLeg_L/Foot_L"]).magnitude;
    Check(Math.Abs(targetLeg/sourceLeg-GameMmdSkeleton.MetersPerMmdUnit)<1e-6,"Rest lengths and VMD displacement use the same unit");
}
var broken=new Dictionary<string,Matrix4x4>(bind); broken.Remove(hips); Reject(()=>GameMmdSkeleton.Create(broken,false),"Missing required bind pose is rejected");
broken=new(bind); broken[hips]=Matrix4x4.TRS(new(float.NaN,0,0),Quaternion.identity,Vector3.one); Reject(()=>GameMmdSkeleton.Create(broken,false),"Nonfinite bind pose is rejected");
broken=new(bind); broken[hips+"/UpperLeg_L/LowerLeg_L"]=bind[hips+"/UpperLeg_L"]; Reject(()=>GameMmdSkeleton.Create(broken,false),"Zero-length leg is rejected");
var noToes=new Dictionary<string,Matrix4x4>(bind); foreach(var key in noToes.Keys.Where(p=>p.Contains("/Toe")).ToArray()) noToes.Remove(key);
using(var data=GameMmdSkeleton.Create(noToes,false)) Check(data.iks.Count==2,"Missing optional toe bind poses do not invent toe geometry");

(GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer) Avatar(IReadOnlyDictionary<string,Vector3> geometry=null,IReadOnlyDictionary<string,Matrix4x4> restMatrices=null)
{
    geometry??=positions;restMatrices??=bind;
    var avatar=new GameObject("Avatar"); var armature=new GameObject("Armature").transform; armature.SetParent(avatar.transform,false); armature.localRotation=Quaternion.AngleAxis(90,new(1,0,0));
    var bones=new Dictionary<string,Transform>();
    foreach(var path in geometry.Keys.OrderBy(p=>p.Count(c=>c=='/')))
    {
        int slash=path.LastIndexOf('/'); var node=new GameObject(path[(slash+1)..]).transform;
        node.SetParent(avatar.transform.Find(path[..slash]),false); node.position=geometry[path]; node.rotation=restMatrices[path].rotation; bones[path]=node;
    }
    var body=new GameObject("Body.001"); body.transform.SetParent(avatar.transform,false);
    var renderer=body.AddComponent<SkinnedMeshRenderer>(); renderer.bones=bones.Values.ToArray(); renderer.sharedMesh=new Mesh{bindposes=bones.Keys.Select(p=>restMatrices[p].inverse).ToArray()};
    return (avatar,bones,renderer);
}
MmdMotion Motion()
{
    var motion=new MmdMotion{lastFrame=30}; motion.tracks["上半身"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64]},new(){frame=30,q=Quaternion.identity,interpolation=new byte[64]}];
    return motion;
}
var fixture=Avatar(); var definition=new ActionDefinition();
// Idle pose changes must not alter the reference axes used for retargeting.
foreach(var node in fixture.bones.Values) node.localRotation=Quaternion.AngleAxis(17,new(1,1,0));
fixture.renderer.SetBlendShapeWeight(0,37);
var saved=fixture.bones.ToDictionary(x=>x.Key,x=>x.Value.localRotation); var savedHip=fixture.bones[hips].localPosition;
using(var pose=new RuntimeVmdPose(fixture.avatar,definition,Motion(),"missing.pmd",false))
{
    Check(pose.BoneCount==49 && pose.Apply(0),"Default game-derived path drives the original 49 body bones without a PMD");
    foreach(var path in MotionRig.BonePaths()) Check(Quaternion.Angle(fixture.bones[path].rotation,bind[path].rotation)<.1,"Identity source yields mesh bind rotation despite a different current idle pose");
}
foreach(var (path,q) in saved) Check(Quaternion.Angle(fixture.bones[path].localRotation,q)<.1,"Stopping restores this playback's original rotations");
Check(Vector3.Distance(savedHip,fixture.bones[hips].localPosition)<1e-5 && fixture.renderer.GetBlendShapeWeight(0)==37,"Stopping restores hip and face state");
var translated=Motion(); translated.tracks["センター"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64]},new(){frame=30,q=Quaternion.identity,interpolation=new byte[64],p=new(0,1,0)}];
using(var pose=new RuntimeVmdPose(fixture.avatar,definition,translated,null,false))
{
    pose.Apply(1); var movement=fixture.bones[hips].position-(fixture.bones[hips].parent.localToWorldMatrix.Point(savedHip));
    Check(Math.Abs(movement.y-GameMmdSkeleton.MetersPerMmdUnit)<1e-5,"One VMD displacement unit maps to the established game distance");
    Check(Math.Abs(movement.x)+Math.Abs(movement.z)<1e-5,"Vertical motion stays on the hips while X/Z is delegated to the controller");
}
var fullMove=Motion();fullMove.tracks["センター"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64]},new(){frame=30,q=Quaternion.identity,interpolation=new byte[64],p=new(1,2,3)}];
using(var pose=new RuntimeVmdPose(fixture.avatar,definition,fullMove,null,false))
{
    pose.Apply(1);Check(pose.HasRootMotion&&Math.Abs(pose.RootDisplacement.x-.08f)<1e-5&&Math.Abs(pose.RootDisplacement.z-.24f)<1e-5&&pose.RootDisplacement.y==0,"Default VMD exposes complete X/Z displacement at the same 0.08 unit as the camera");
    var boneDelta=fixture.bones[hips].position-fixture.bones[hips].parent.localToWorldMatrix.Point(savedHip);Check(Math.Abs(boneDelta.x)+Math.Abs(boneDelta.z)<1e-5,"Horizontal root movement is not also applied to the live hips");
}
using(var pose=new RuntimeVmdPose(fixture.avatar,new ActionDefinition{Loop=true},fullMove,null,false))
    Check(Math.Abs(pose.LoopRootDisplacement.x-.08f)<1e-5&&Math.Abs(pose.LoopRootDisplacement.z-.24f)<1e-5,"Provider publishes per-cycle root displacement for continuous looping");
using(var pose=new RuntimeVmdPose(fixture.avatar,new ActionDefinition{WriteHipTranslation=false},fullMove,null,false))
{pose.Apply(1);Check(!pose.HasRootMotion&&pose.RootDisplacement.sqrMagnitude==0,"Explicit translation-off keeps its existing opt-out semantics");}

// World-metric source calibration must include even a default prefab's root
// scale, while root movement/heading remain absent from the shape signature.
MmdRigCache.Clear();var metricAvatar=Avatar();var metricBefore=MmdTargetSnapshot.Read(metricAvatar.avatar);
metricAvatar.avatar.transform.localScale=Vector3.one*1.2f;
var metricScaled=MmdTargetSnapshot.Read(metricAvatar.avatar);
Check(!metricScaled.Matches(metricBefore.Shape,metricBefore.ShapePaths),"Actual root scale changes invalidate generated reference dimensions");
using(var metricSource=GameMmdSkeleton.CreateData(metricScaled.Positions))
{
    float expected=Vector3.Distance(metricAvatar.bones[hips+"/UpperLeg_L"].position,metricAvatar.bones[hips+"/UpperLeg_L/LowerLeg_L"].position);
    float sourceLength=Vector3.Distance(metricSource.byName["左足"].rest,metricSource.byName["左ひざ"].rest)*.08f;
    Check(Math.Abs(expected-sourceLength)<1e-5,"Generated reference leg matches the actual 1.2-scaled world segment");
}
using(var pose=new RuntimeVmdPose(metricAvatar.avatar,new ActionDefinition(),fullMove,null,false))
{
    var before=metricAvatar.bones[hips].position;pose.Apply(1);
    Check(Math.Abs(metricAvatar.bones[hips].position.y-before.y-.16f)<1e-5,"Two MMD Y units remain .16 world metres on a 1.2 avatar");
    Check(Math.Abs(pose.RootDisplacement.x-.08f)<1e-5&&Math.Abs(pose.RootDisplacement.z-.24f)<1e-5,"Authored root movement is not scaled twice with avatar size");
}
metricAvatar.avatar.transform.position=new(100000,12345,-100000);metricAvatar.avatar.transform.rotation=Quaternion.AngleAxis(73,Vector3.up);
var metricMoved=MmdTargetSnapshot.Read(metricAvatar.avatar);
Check(metricMoved.Matches(metricScaled.Shape,metricScaled.ShapePaths),"World-metric shape is independent of actor translation and heading");
var metricParent=new GameObject("ScaledPlayerParent");metricAvatar.avatar.transform.SetParent(metricParent.transform,false);metricParent.transform.localScale=Vector3.one*1.1f;
var metricAncestor=MmdTargetSnapshot.Read(metricAvatar.avatar);
Check(!metricAncestor.Matches(metricScaled.Shape,metricScaled.ShapePaths)&&Math.Abs(metricAncestor.MetricX.magnitude-1.32f)<1e-5,"External ancestor scale is part of the effective reference shape");
MmdRigCache.Clear();

var masked=new ActionDefinition{BodyMask=ActionBodyMask.LowerBody,WriteFaceMorphs=false};
using(var pose=new RuntimeVmdPose(fixture.avatar,masked,translated,null,false))
{
    pose.Apply(1); foreach(var path in saved.Keys.Where(p=>!p.Contains("/UpperLeg"))) Check(Quaternion.Angle(saved[path],fixture.bones[path].localRotation)<.1,"Leg-only mask preserves unrelated live rotations");
}
using(var rig=GameMmdSkeleton.Create(bind))
{
    var motion=Motion(); motion.tracks["左足ＩＫ"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64],p=new(0,1,-1)}];
    rig.useIk=false; rig.Evaluate(motion,0); float before=Vector3.Distance(rig.byName["左足首"].node.position,rig.byName["左足ＩＫ"].node.position);
    rig.useIk=true; rig.Evaluate(motion,0); float after=Vector3.Distance(rig.byName["左足首"].node.position,rig.byName["左足ＩＫ"].node.position);
    Check(after<before*.1f,"Synthetic leg IK converges toward a reachable animated goal");
}

// Regression from the actual runtime trace: a 157.70-degree knee stepped into
// 192.08 degrees, inverting the physical chain despite fixed segment lengths.
float RuntimeHinge(Quaternion q)=>(float)(2*Math.Atan2(q.x,q.w)*180/Math.PI);
var tracePrevious=new Quaternion(.9888f,0,0,.1492f).normalized;
var traceCandidate=new Quaternion(.9887f,0,0,-.1497f).normalized;
float traceMaximum=179.9f-5.1387f;
var traceFixed=MmdSkeleton.LimitKnee(traceCandidate,tracePrevious,traceMaximum);
Check(RuntimeHinge(traceFixed)<=traceMaximum+.001f&&RuntimeHinge(traceFixed)>150,"Runtime 192-degree inverse-branch candidate stays on the deeply folded allowed side");
var negativeTrace=new Quaternion(-traceCandidate.x,-traceCandidate.y,-traceCandidate.z,-traceCandidate.w);
Check(Quaternion.Angle(traceFixed,MmdSkeleton.LimitKnee(negativeTrace,tracePrevious,traceMaximum))<.1,"Equivalent q and -q cannot select opposite knee branches");
Check(Quaternion.Angle(MmdSkeleton.LimitKnee(new Quaternion(1,0,0,0),Quaternion.identity,traceMaximum),MmdSkeleton.LimitKnee(new Quaternion(-1,0,0,0),Quaternion.identity,traceMaximum))<.1,"Half-turn quaternion sign tie consistently selects the allowed branch");
Check(Quaternion.Angle(MmdSkeleton.LimitKnee(new Quaternion(0,1,0,0),tracePrevious,traceMaximum),tracePrevious)<.1,"Degenerate hinge projection retains previous valid bend");
using(var kneeProfile=GameMmdSkeleton.Create(bind,false))
using(var kneeCopy=kneeProfile.CopyData())
{
    Check(kneeProfile.iks.Count(i=>i.limitKnee)==2,"Generated leg chains carry explicit one-sided knee limits");
    Check(kneeCopy.iks.Zip(kneeProfile.iks).All(p=>p.First.limitKnee==p.Second.limitKnee&&p.First.kneeMax==p.Second.kneeMax&&p.First.kneeMin==p.Second.kneeMin),"Worker/cache skeleton copies preserve knee limits");
}

// Cached helpers are shared by skeleton mode, while every lease captures a
// new restoration snapshot and supplies its own keys/mask.
MmdRigCache.Clear();
int builds=MmdRigCache.BuildCount;
Check(!MmdRigCache.IsPrepared(fixture.avatar,null,false),"Cold playback reports pending without constructing a rig");
MmdRigCache.Prepare(fixture.avatar,null,false);
Check(MmdRigCache.BuildCount==builds+1,"Prewarm builds one reusable game rig");
Check(MmdRigCache.IsPrepared(fixture.avatar,null,false),"Prewarm publishes readiness");
for(int cycle=0;cycle<20;cycle++)
{
    foreach(var node in fixture.bones.Values) node.localRotation=Quaternion.AngleAxis(cycle+3,new(1,1,0));
    var before=fixture.bones.ToDictionary(p=>p.Key,p=>p.Value.localRotation);
    using(var pose=new RuntimeVmdPose(fixture.avatar,cycle%2==0?definition:masked,cycle%2==0?translated:Motion(),null,false)) { Check(pose.Apply(.5f),"Reused rig accepts changing motion and masks"); }
    foreach(var (path,q) in before) Check(Quaternion.Angle(q,fixture.bones[path].localRotation)<.1,"Every reuse restores the current lease rather than an old snapshot");
}
Check(MmdRigCache.BuildCount==builds+1,"Repeated switches allocate no new sampling rigs");
using(var first=new RuntimeVmdPose(fixture.avatar,definition,Motion(),null,false))
{
    using(var second=new RuntimeVmdPose(fixture.avatar,definition,translated,null,false)) Check(second.Apply(.5f),"Simultaneous consumer owns a separate solver");
    Check(first.Apply(.5f),"Disposing a second consumer does not destroy the first solver");
}
Check(MmdRigCache.BuildCount==builds+2,"Only concurrent use allocates an independent temporary rig");
MmdRigCache.Invalidate(); MmdRigCache.Prepare(fixture.avatar,null,false);
Check(MmdRigCache.BuildCount==builds+2,"ReAdjust without a real shape change validates and reuses the helpers");
var other=Avatar(); MmdRigCache.Prepare(other.avatar,null,false);
Check(MmdRigCache.BuildCount==builds+3,"Another live avatar receives an independent cache entry");
UnityEngine.Object.Destroy(other.avatar); MmdRigCache.Prune();
var reload=Avatar();int beforeReload=MmdRigCache.BuildCount;
MmdRigCache.DetachTarget(fixture.avatar.GetInstanceID());
MmdRigCache.Prepare(reload.avatar,null,false);
Check(MmdRigCache.BuildCount==beforeReload,"Reloading the same shape rebinds to persistent helpers without rebuilding");
using(var pose=new RuntimeVmdPose(reload.avatar,definition,Motion(),null,false)) Check(pose.Apply(0),"Rebound rig writes the replacement actor's bones");
var originalMesh=reload.renderer.sharedMesh;
var changed=originalMesh.bindposes.ToArray();int kneeBindIndex=reload.bones.Keys.ToList().IndexOf(hips+"/UpperLeg_L/LowerLeg_L");
changed[kneeBindIndex]=Matrix4x4.TRS(positions[hips+"/UpperLeg_L/LowerLeg_L"]+new Vector3(0,.03f,0),bind[hips+"/UpperLeg_L/LowerLeg_L"].rotation,Vector3.one).inverse;
reload.renderer.sharedMesh=new Mesh{bindposes=changed};MmdRigCache.Invalidate();MmdRigCache.Prepare(reload.avatar,null,false);
Check(MmdRigCache.BuildCount==beforeReload+1,"A changed bind shape builds a new solver exactly once");
reload.renderer.sharedMesh=originalMesh;MmdRigCache.Invalidate();MmdRigCache.Prepare(reload.avatar,null,false);
Check(MmdRigCache.BuildCount==beforeReload+1,"Returning to a cached shape reuses its previous helpers");
var snapshot=MmdTargetSnapshot.Read(reload.avatar);
reload.avatar.transform.position=new Vector3(100000,10000,-100000);reload.avatar.transform.rotation=Quaternion.AngleAxis(73,Vector3.up);
var movedSnapshot=MmdTargetSnapshot.Read(reload.avatar);
Check(movedSnapshot.Matches(snapshot.Shape,snapshot.ShapePaths),"World movement and heading cannot invalidate a shape through matrix cancellation");
reload.avatar.transform.position=Vector3.zero;reload.avatar.transform.rotation=Quaternion.identity;

var template=Task.Run(()=>GameMmdSkeleton.CreateData(snapshot.Positions)).GetAwaiter().GetResult();
var draft=new RuntimeVmdRig();int steps=0,maxObjects=0;
using(var work=draft.BuildSteps(snapshot,template).GetEnumerator())
{
    while(true){int before=GameObject.CreatedCount;bool more=work.MoveNext();maxObjects=Math.Max(maxObjects,GameObject.CreatedCount-before);if(!more)break;steps++;}
}
Check(steps>100&&maxObjects<=2,"A cold build yields between small native node batches");draft.Destroy();


if(args.Length>0)
{
    string resources=Path.GetFullPath(args[0]);
    foreach(var folder in Directory.GetDirectories(resources).Order())
    {
        string vmd=Path.Combine(folder,"motion.vmd"); if(!File.Exists(vmd)) continue;
        string pmd=Path.Combine(folder,"source.pmd"); bool paired=File.Exists(pmd);
        var source=Task.Run(()=>new RuntimeVmdSource(vmd,paired?pmd:"does-not-exist.pmd",paired)).GetAwaiter().GetResult();
        using(var pose=source.CreatePose(fixture.avatar,definition))
        {
            foreach(float t in new[]{0f,source.Duration*.25f,source.Duration*.5f,source.Duration*.75f,source.Duration})
            {
                Check(pose.Apply(t),"Actual VMD applies"); foreach(var node in fixture.bones.Values) Check(Finite(node.localRotation),"Actual VMD produces finite unit rotations");
            }
        }
        if(Path.GetFileName(folder)=="CH4NGE")
        {
            using var cue=source.CreatePose(fixture.avatar,definition);cue.Apply(75/30f);var first=((IActionRootMotionPose)cue).RootDisplacement;cue.Apply(91/30f);var last=((IActionRootMotionPose)cue).RootDisplacement;
            Check(first.x<-.04f&&last.x<-.13f&&first.y==0&&last.y==0,"Actual CH4NGE hand cue retains the previously omitted horizontal root trajectory");
            Console.WriteLine($"CH4NGE root trajectory: frame75=({first.x:F5},{first.z:F5})m frame91=({last.x:F5},{last.z:F5})m");
        }

        if(Path.GetFileName(folder)=="Samsa")
        {
            // Reference geometry reconstructed from the user's runtime trace:
            // local leg lengths .3031/.3215, rest bend 5.1387 degrees. The
            // former tall mock avatar did not reproduce this inverse branch.
            var runtimePositions=new Dictionary<string,Vector3>(positions);
            foreach(var suffix in new[]{"_L","_R"})
            {
                string leg=hips+"/UpperLeg"+suffix,knee=leg+"/LowerLeg"+suffix,foot=knee+"/Foot"+suffix;
                float x=suffix=="_L"?-.0625f:.0625f;
                runtimePositions[leg]=runtimePositions[hips]+new Vector3(x,-.0881f,-.0117f);
                runtimePositions[knee]=runtimePositions[leg]+new Vector3(0,-.3031f,0);
                float bend=5.1387f/Mathf.Rad2Deg;
                runtimePositions[foot]=runtimePositions[knee]+new Vector3(0,-.3215f*MathF.Cos(bend),-.3215f*MathF.Sin(bend));
                runtimePositions[foot+"/Toe"+suffix]=runtimePositions[foot]+new Vector3(0,-.02f,.12f);
            }
            var runtimeBind=runtimePositions.ToDictionary(p=>p.Key,p=>Matrix4x4.TRS(p.Value,Quaternion.identity,Vector3.one));
            var samsa=MmdMotion.Read(vmd);
            using var limited=GameMmdSkeleton.Create(runtimeBind);limited.useIk=true;
            using var old=GameMmdSkeleton.Create(runtimeBind);old.useIk=true;
            foreach(var ik in old.iks)ik.limitKnee=false;
            bool reproduced=false;float maxFixed=0,maxFootError=0,maxOldFootError=0,maxKneelFootError=0,maxAddedError=0;
            for(int frame=690;frame<=840;frame++)
            {
                old.Evaluate(samsa,frame);limited.Evaluate(samsa,frame);
                if(RuntimeHinge(old.byName["左ひざ"].node.localRotation)>180)reproduced=true;
                foreach(string side in new[]{"左","右"})
                {
                    var ik=limited.iks.Single(i=>limited.bones[i.target].name==side+"足ＩＫ");
                    var thigh=limited.byName[side+"足"].node;var knee=limited.byName[side+"ひざ"].node;var foot=limited.byName[side+"足首"].node;
                    float hinge=RuntimeHinge(knee.localRotation);maxFixed=Math.Max(maxFixed,hinge);
                    Check(hinge>=ik.kneeMin-.001f&&hinge<=ik.kneeMax+.001f,"Actual Samsa kneel cannot cross the anatomical knee limit");
                    Check(Vector3.Dot(Vector3.Cross(knee.position-thigh.position,foot.position-knee.position),thigh.rotation*new Vector3(1,0,0))>=-.0001f,"Actual Samsa knee keeps its forward bending direction");
                    Check(Math.Abs(Vector3.Distance(thigh.position,knee.position)*.08f-.3031f)<.0001f&&Math.Abs(Vector3.Distance(knee.position,foot.position)*.08f-.3215f)<.0001f,"Actual kneel regression preserves both bone segment lengths");
                    float error=Vector3.Distance(foot.position,limited.byName[side+"足ＩＫ"].node.position)*.08f;maxFootError=Math.Max(maxFootError,error);
                    float oldError=Vector3.Distance(old.byName[side+"足首"].node.position,old.byName[side+"足ＩＫ"].node.position)*.08f;maxOldFootError=Math.Max(maxOldFootError,oldError);
                    // Some approach/stand-up goals are already unreachable on
                    // this shorter game rig. Bound local changes to 1cm and
                    // also compare the whole window's worst error below.
                    maxAddedError=Math.Max(maxAddedError,error-oldError);
                    Check(error<=oldError+.01f,$"Knee limit foot-goal regression frame={frame} side={side} error={error:F6} old={oldError:F6}");
                    if(frame>=750&&frame<=795){maxKneelFootError=Math.Max(maxKneelFootError,error);Check(error<.001f,"Corrected kneeling branch still reaches the authored foot goal");}
                }
            }
            Check(reproduced,"Runtime-derived geometry reproduces the previous 25-second knee inversion before applying the new limit");

            // Full provider regression using bind positions/neutral rotations
            // captured in the real 0.9.5 diagnostic, including default 1.2 scale.
            var tracePositions=new Dictionary<string,Vector3>(positions);
            tracePositions[hips]=new Vector3(0,10.0136f,.4073f)*.08f;
            foreach(string suffix in new[]{"_L","_R"})
            {
                float x=suffix=="_L"?-.7815f:.7815f;
                string leg=hips+"/UpperLeg"+suffix,knee=leg+"/LowerLeg"+suffix,foot=knee+"/Foot"+suffix;
                tracePositions[leg]=new Vector3(x,8.9125f,.2610f)*.08f;
                tracePositions[knee]=new Vector3(x,5.1245f,.2239f)*.08f;
                tracePositions[foot]=new Vector3(x,1.1261f,-.1752f)*.08f;
                tracePositions[foot+"/Toe"+suffix]=tracePositions[foot]+new Vector3(0,-.04f,.1f);
            }
            var traceBind=tracePositions.ToDictionary(p=>p.Key,p=>Matrix4x4.TRS(p.Value,Quaternion.identity,Vector3.one));
            foreach(string suffix in new[]{"_L","_R"})
            {
                string leg=hips+"/UpperLeg"+suffix,knee=leg+"/LowerLeg"+suffix,foot=knee+"/Foot"+suffix;
                traceBind[leg]=Matrix4x4.TRS(tracePositions[leg],new Quaternion(1,0,0,-.0049f).normalized,Vector3.one);
                traceBind[knee]=Matrix4x4.TRS(tracePositions[knee],new Quaternion(.9988f,0,0,-.0497f).normalized,Vector3.one);
                traceBind[foot]=Matrix4x4.TRS(tracePositions[foot],new Quaternion(.8450f,0,0,.5348f).normalized,Vector3.one);
            }
            var traceActor=Avatar(tracePositions,traceBind);traceActor.avatar.transform.localScale=Vector3.one*1.2f;traceActor.avatar.transform.position=new(0,-.0187f,0);
            traceActor.bones[hips].position+=Vector3.up*(.9206f-traceActor.bones[hips].position.y);
            using var oldScaleSolver=GameMmdSkeleton.Create(traceBind);oldScaleSolver.useIk=true;
            oldScaleSolver.Evaluate(samsa,0);float oldOriginY=oldScaleSolver.byName["下半身"].node.position.y;
            oldScaleSolver.Evaluate(samsa,25.0733f*30);
            float expectedPelvis=.9206f+(oldScaleSolver.byName["下半身"].node.position.y-oldOriginY)*.08f;
            float oldRenderedKnee=expectedPelvis+(oldScaleSolver.byName["左ひざ"].node.position.y-oldScaleSolver.byName["下半身"].node.position.y)*.08f*1.2f;
            var traceMetric=MmdTargetSnapshot.Read(traceActor.avatar);
            using var worldScaleSolver=GameMmdSkeleton.CreateData(traceMetric.Positions);worldScaleSolver.CreateTransforms();worldScaleSolver.useIk=true;
            worldScaleSolver.Evaluate(samsa,25.0733f*30);
            float expectedWorldKnee=expectedPelvis+(worldScaleSolver.byName["左ひざ"].node.position.y-worldScaleSolver.byName["下半身"].node.position.y)*.08f;
            using(var tracePose=new RuntimeVmdPose(traceActor.avatar,new ActionDefinition(),samsa,null,false))
            {
                tracePose.Apply(25.0733f);
                float actualPelvis=traceActor.bones[hips].position.y;
                float actualKnee=traceActor.bones[hips+"/UpperLeg_L/LowerLeg_L"].position.y;
                float actualFoot=traceActor.bones[hips+"/UpperLeg_L/LowerLeg_L/Foot_L"].position.y;
                Check(oldRenderedKnee<-.04f,"Captured runtime reference reproduces old under-floor knee placement");
                Check(Math.Abs(actualPelvis-expectedPelvis)<.0001f,"World-metric fix preserves authored pelvis descent without height compensation");
                Check(Math.Abs(actualKnee-expectedWorldKnee)<.0001f&&actualKnee-oldRenderedKnee>.04f,"Rendered knee follows the world-sized source pose rather than the old undersized solve");
                Check(actualFoot>.15f&&actualFoot<.21f,"World-metric foot lands near its .18m authored ankle goal instead of the old .31m mapped goal");
                Console.WriteLine($"Samsa runtime matrix regression: pelvis={actualPelvis:F6} oldKnee={oldRenderedKnee:F6} newKnee={actualKnee:F6} newAnkle={actualFoot:F6}m");
            }

            Check(maxFootError<=maxOldFootError+.001f,"Knee limit does not increase the window's worst pre-existing foot-goal error");
            Console.WriteLine($"Samsa 23-28s runtime-geometry regression: oldFlip={reproduced} maxFixedHinge={maxFixed:F3} kneelFootError={maxKneelFootError:F6}m windowFootError={maxFootError:F6}/{maxOldFootError:F6}m addedError={maxAddedError:F6}m");
        }

        if(paired)
        {
            var old=new LegacyRuntimeVmdSource(vmd,pmd,true);
            foreach(float t in new[]{0f,1f,5f,20f,45f,80f,source.Duration})
            {
                Dictionary<string,Quaternion> golden;
                using(var pose=old.CreatePose(fixture.avatar,definition)) { pose.Apply(t); golden=fixture.bones.ToDictionary(x=>x.Key,x=>x.Value.localRotation); }
                using(var pose=source.CreatePose(fixture.avatar,definition)) { pose.Apply(t); foreach(var (path,q) in golden) Check(Quaternion.Angle(q,fixture.bones[path].localRotation)<.1,"Paired PMD output agrees with previous implementation"); }
            }
        }
        Console.WriteLine($"PASS {Path.GetFileName(folder)}: mode={(paired?"paired-PMD":"game-bind-pose")} duration={source.Duration} retained={source.RetainedTracks} ignored={source.IgnoredTracks}");
    }
}
string asyncRoot=Path.Combine(Path.GetTempPath(),"mmd-async-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(asyncRoot);
try
{
    foreach(string name in new[]{"a-good","b-invalid","c-camera-invalid"})
    {
        string folder=Path.Combine(asyncRoot,name); Directory.CreateDirectory(folder);
        if(name=="b-invalid") { File.WriteAllText(Path.Combine(folder,"motion.vmd"),"invalid");continue; }
        using(var w=new BinaryWriter(File.Create(Path.Combine(folder,"motion.vmd"))))
        {
            void Text(string text,int length) {var bytes=new byte[length];Encoding.GetEncoding(932).GetBytes(text).CopyTo(bytes,0);w.Write(bytes);}
            Text("Vocaloid Motion Data 0002",30);Text("fixture",20);w.Write(2u);
            foreach(uint frame in new[]{0u,30u}) {Text("上半身",15);w.Write(frame);w.Write(0f);w.Write(0f);w.Write(0f);w.Write(0f);w.Write(0f);w.Write(0f);w.Write(1f);w.Write(new byte[64]);}
            w.Write(0u);
        }
        if(name=="c-camera-invalid")File.WriteAllText(Path.Combine(folder,"camera.vmd"),"invalid");
    }
    var prep=new MmdPackPreparation(IntPtr.Zero); int queued=prep.Configure(asyncRoot);
    Check(queued==3 && ActionRegistry.Registered.Count==0,"Configure queues parsing without waiting or registering on workers");
    var timeout=System.Diagnostics.Stopwatch.StartNew();
    while(!global::Plugin.LogSource.Messages.Any(m=>m.Contains("MMD_ACTIONS_READY")))
    {
        int before=ActionRegistry.Registered.Count;prep.Update();Check(ActionRegistry.Registered.Count-before<=1,"At most one pack is registered per frame");
        if(timeout.Elapsed.TotalSeconds>10)throw new Exception("Async pack pump timed out");Thread.Sleep(1);
    }
    Check(ActionRegistry.Registered.Count==2,"A malformed VMD does not prevent other packs from finishing");
    Check(ActionRegistry.Registered.ContainsKey("c-camera-invalid"),"A malformed camera does not reject its valid motion");
    MmdRigCache.Clear();var pumpAvatar=Avatar();var pump=new MmdRigPreparation(IntPtr.Zero);
    ActionTargetApi.Target=pumpAvatar.avatar;ActionTargetApi.IsReady=true;
    var provider=(RuntimeVmdSource)ActionRegistry.Registered.Values.First().RuntimeSource;
    int coldBuilds=MmdRigCache.BuildCount,frames=0,maxCreated=0;
    timeout.Restart();
    while(!provider.IsPrepared(pumpAvatar.avatar))
    {
        int before=GameObject.CreatedCount;pump.Update();maxCreated=Math.Max(maxCreated,GameObject.CreatedCount-before);frames++;
        if(timeout.Elapsed.TotalSeconds>10)throw new Exception("Rig preparation pump timed out");Thread.Sleep(1);
    }
    Check(frames>10&&maxCreated<=16&&MmdRigCache.BuildCount==coldBuilds+1,"Actual production pump spreads a cold build over bounded frames");
    var replacement=Avatar();UnityEngine.Object.Destroy(pumpAvatar.avatar);ActionTargetApi.Target=replacement.avatar;
    timeout.Restart();
    while(!provider.IsPrepared(replacement.avatar))
    {
        pump.Update();if(timeout.Elapsed.TotalSeconds>10)throw new Exception("Rebind pump timed out");Thread.Sleep(1);
    }
    Check(MmdRigCache.BuildCount==coldBuilds+1&&global::Plugin.LogSource.Messages.Last().Contains("mode=rebind"),"Production actor replacement rebinds persistent helpers without a new build");
    MmdRigCache.Invalidate();timeout.Restart();
    while(!provider.IsPrepared(replacement.avatar)) {pump.Update();if(timeout.Elapsed.TotalSeconds>10)throw new Exception("Shape validation timed out");Thread.Sleep(1);}
    Check(MmdRigCache.BuildCount==coldBuilds+1,"ReAdjust marker alone does not force a rebuild in the production pump");
    pump.OnDestroy();
}
finally {Directory.Delete(asyncRoot,true);MmdRigCache.Clear();}

// Production face mixer and VMD integration: the native evaluator supplies a
// fresh baseline each frame; overlays never feed into the next native update.
MmdRigCache.Clear();
var faceFixture=Avatar();var faceRenderer=faceFixture.renderer;
var faceAction=new ActionDefinition();var faceMotion=Motion();
faceMotion.morphs["まばたき"]=[new(0,25),new(1,25)];
faceRenderer.SetBlendShapeWeight(0,40);faceRenderer.SetBlendShapeWeight(6,65);faceRenderer.SetBlendShapeWeight(7,81);
ActionFaceSettings.KeepGameExpressions=true;
using(var pose=new RuntimeVmdPose(faceFixture.avatar,faceAction,faceMotion,"missing.pmd",false))
{
    pose.Apply(0);
    Check(faceRenderer.GetBlendShapeWeight(0)==65,"Default VMD overlays native 40 and motion 25");
    Check(faceRenderer.GetBlendShapeWeight(6)==65,"Overlay preserves native non-mapped mouth expression");
    for(int i=0;i<30;i++)pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(0)==65,"Repeated samples do not accumulate overlay");
    pose.RestoreNativeFace();Check(faceRenderer.GetBlendShapeWeight(0)==40,"Pre-native restore removes motion contribution");
    faceRenderer.SetBlendShapeWeight(0,85);pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(0)==100,"Fresh native baseline used with bounded same-channel sum");
    ActionFaceSettings.KeepGameExpressions=false;pose.RestoreNativeFace();pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(0)==25&&faceRenderer.GetBlendShapeWeight(6)==0,"UI off selects action alone, including clearing unmapped native expressions");
    Check(faceRenderer.GetBlendShapeWeight(7)==81,"Unrelated customization shape is unchanged");
    ActionFaceSettings.KeepGameExpressions=true;pose.RestoreNativeFace();pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(0)==100&&faceRenderer.GetBlendShapeWeight(6)==65,"UI re-enable takes effect in the same pose");
    faceRenderer.SetBlendShapeWeight(0,12);pose.RestoreNativeFace();
    Check(faceRenderer.GetBlendShapeWeight(0)==12,"Restoration preserves a newer native write");
    pose.Apply(.5f);
}
Check(faceRenderer.GetBlendShapeWeight(0)==12&&faceRenderer.GetBlendShapeWeight(6)==65,"Dispose returns latest native values, not stale start snapshot");
faceAction.KeepGameExpressions=false;
using(var pose=new RuntimeVmdPose(faceFixture.avatar,faceAction,faceMotion,"missing.pmd",false))
{
    pose.Apply(0);Check(faceRenderer.GetBlendShapeWeight(0)==25&&faceRenderer.GetBlendShapeWeight(6)==0,"Pack false vetoes UI true");
    ActionFaceSettings.KeepGameExpressions=false;pose.RestoreNativeFace();pose.Apply(0);
    ActionFaceSettings.KeepGameExpressions=true;pose.RestoreNativeFace();pose.Apply(0);
    Check(faceRenderer.GetBlendShapeWeight(0)==25,"Changing UI cannot bypass pack false");
}
faceAction.WriteFaceMorphs=false;
using(var pose=new RuntimeVmdPose(faceFixture.avatar,faceAction,faceMotion,"missing.pmd",false))
{
    ActionFaceSettings.KeepGameExpressions=false;pose.Apply(0);
    Check(faceRenderer.GetBlendShapeWeight(0)==12&&faceRenderer.GetBlendShapeWeight(6)==65,"writeFaceMorphs=false opts out of all face writes");
}
// Full-size native mesh boundary: expression shape hashes + native script
// channels are suppressed, while original customization indices are retained.
var full=Avatar();full.renderer.sharedMesh.ShapeNames=Enumerable.Range(0,420).Select(i=>"shape"+i).ToArray();
full.renderer.sharedMesh.ShapeNames[15]="eye_close";full.renderer.sharedMesh.ShapeNames[55]="mouth_a";full.renderer.sharedMesh.ShapeNames[60]="mouth_a";
full.renderer.SetBlendShapeWeight(55,73);full.renderer.SetBlendShapeWeight(60,37);full.renderer.SetBlendShapeWeight(290,88);
var layer=new ActionFaceMorphLayer(full.avatar,new ActionDefinition{KeepGameExpressions=false});layer.Begin();
Check(full.renderer.GetBlendShapeWeight(55)==73,"Native OnChangeBody customization channel is protected even when also bound by face clips");
Check(full.renderer.GetBlendShapeWeight(60)==0&&full.renderer.GetBlendShapeWeight(290)==0,"Native clip and script-only dynamic expressions are cleared");
layer.RestoreNativeFace();Check(full.renderer.GetBlendShapeWeight(60)==37&&full.renderer.GetBlendShapeWeight(290)==88,"Native-only suppression is reversible");
ActionFaceSettings.KeepGameExpressions=true;MmdRigCache.Clear();


// Reproduce the native additive corrections without relying on Animator to
// rewrite unkeyed translations. Length/spacing must stay stable across frames.
var ownedFixture=Avatar();var nativeAnimator=ownedFixture.avatar.AddComponent<Animator>();
var ownedKnee=ownedFixture.bones[hips+"/UpperLeg_L/LowerLeg_L"];
var ownedFoot=ownedFixture.bones[hips+"/UpperLeg_L/LowerLeg_L/Foot_L"];
var ownedHead=ownedFixture.bones[chest+"/Neck/Head"];
var ownedShoulder=ownedFixture.bones[chest+"/Shoulder_L"];
var nativeKnee=ownedKnee.localPosition;var nativeFoot=ownedFoot.localPosition;var nativeShoulder=ownedShoulder.localPosition;
var nativeScale=new Vector3(1.2f,1.3f,.9f);ownedKnee.localScale=nativeScale;
var accessory=new GameObject("HairPhysics");accessory.transform.SetParent(ownedHead,false);accessory.transform.localPosition=new(.02f,.03f,.04f);
var accessoryPosition=accessory.transform.localPosition;
using(var pose=new RuntimeVmdPose(ownedFixture.avatar,new ActionDefinition(),Motion(),null,false))
{
    Check(nativeAnimator.cullingMode==AnimatorCullingMode.AlwaysAnimate,"Body lease maintains fresh native evaluation while MMD camera leaves native bounds");
    for(int frame=0;frame<120;frame++)
    {
        pose.RestoreNativeBody();pose.BeginNativeBody();
        using(pose.PreserveNativeCorrection(ActionBoneCorrection.Knee))ownedKnee.localPosition+=new Vector3(.05f,.02f,0);
        Check(Vector3.Distance(ownedKnee.localPosition,nativeKnee)<1e-5,"Native knee slide writes are isolated");
        using(pose.PreserveNativeCorrection(ActionBoneCorrection.LookAt))ownedHead.localRotation=Quaternion.AngleAxis(70,Vector3.up);
        Check(pose.SuppressKneeOffset,"Owned knee does not contribute native knee-height correction");
        // Native customization still applies once: no fake deformation reset to
        // default-sized bind positions and no cumulative multiply/add drift.
        ownedKnee.localPosition*=1.25f;ownedFoot.localPosition*=.85f;
        ownedShoulder.localPosition+=new Vector3(.01f,0,0);
        pose.EndNativeBody(true);
        Check(!pose.SuppressKneeOffset&&pose.PreserveNativeCorrection(ActionBoneCorrection.LookAt)==null,"NPC/out-of-pass updates retain normal corrections");
        pose.Apply(.5f);pose.Apply(.5f);
        Check(Vector3.Distance(ownedKnee.localPosition,nativeKnee*1.25f)<1e-5&&Vector3.Distance(ownedFoot.localPosition,nativeFoot*.85f)<1e-5,"Repeated native size correction and MMD sampling never stretch or shrink bone segments");
        Check(Vector3.Distance(ownedShoulder.localPosition,nativeShoulder+new Vector3(.01f,0,0))<1e-5,"Shoulder spacing stays stable across native and MMD ownership");
        Check(Vector3.Distance(ownedKnee.localScale,nativeScale)<1e-5,"User body scale is retained");
        Check(Vector3.Distance(accessory.transform.localPosition,accessoryPosition)<1e-5,"Accessory physics transform is outside body ownership");
        Check(Quaternion.Angle(ownedHead.rotation,bind[chest+"/Neck/Head"].rotation)<.1,"MMD head pose has no native look-at contribution");
    }
}
Check(nativeAnimator.cullingMode==AnimatorCullingMode.CullUpdateTransforms,"Stopping restores the original animator culling mode");
Check(Vector3.Distance(ownedKnee.localPosition,nativeKnee*1.25f)<1e-5,"Stopping after output returns latest corrected native body pose");
// A stop during the next native Update must return the raw basis, so the
// pending original late-pass customization is not applied twice.
using(var pose=new RuntimeVmdPose(ownedFixture.avatar,new ActionDefinition(),Motion(),null,false))
{
    pose.BeginNativeBody();ownedKnee.localPosition*=1.1f;pose.EndNativeBody(true);pose.Apply(0);pose.RestoreNativeBody();
}
Check(Vector3.Distance(ownedKnee.localPosition,nativeKnee*1.25f)<1e-5,"Cancel before native evaluation leaves uncorrected basis for the pending native pass");
// Toe correction remains bookkeeping-only for a full body owner; unowned head
// stays with the native game for an explicitly configured lower-body action.
var subset=Avatar();var subsetHead=subset.bones[chest+"/Neck/Head"];var subsetToe=subset.bones[hips+"/UpperLeg_L/LowerLeg_L/Foot_L/Toe_L"];
var toePosition=subsetToe.localPosition;var toeRotation=subsetToe.localRotation;
using(var pose=new RuntimeVmdPose(subset.avatar,new ActionDefinition{BodyMask=ActionBodyMask.LowerBody},Motion(),null,false))
{
    pose.BeginNativeBody();
    Check(pose.PreserveNativeCorrection(ActionBoneCorrection.LookAt)==null,"Unowned head/native look-at is not suppressed");
    subsetHead.localRotation=Quaternion.AngleAxis(42,Vector3.up);
    using(pose.PreserveNativeCorrection(ActionBoneCorrection.Toe))
    {subsetToe.localPosition+=Vector3.up*.03f;subsetToe.localRotation=Quaternion.AngleAxis(80,Vector3.up);}
    Check(Vector3.Distance(subsetToe.localPosition,toePosition)<1e-5&&Quaternion.Angle(subsetToe.localRotation,toeRotation)<.1,"Owned toe correction is rolled back without disabling shoe bookkeeping");
    pose.EndNativeBody(true);pose.Apply(.5f);
    Check(Quaternion.Angle(subsetHead.localRotation,Quaternion.AngleAxis(42,Vector3.up))<.1,"Partial pose leaves unrelated native head pose intact");
}
// Failed native evaluation closes its scope without publishing partial offsets.
using(var pose=new RuntimeVmdPose(subset.avatar,new ActionDefinition(),Motion(),null,false))
{
    var before=subsetToe.localPosition;pose.BeginNativeBody();subsetToe.localPosition+=Vector3.up;pose.EndNativeBody(false);
    Check(Vector3.Distance(subsetToe.localPosition,before)<1e-5&&!pose.SuppressKneeOffset,"Native failure restores pre-correction transforms and closes scope");
}
MmdRigCache.Clear();


Console.WriteLine($"PASS {checks} assertions: production topology/parser/retargeter/cache/async pump; synthetic scene math boundary, not rendered game verification");

}
catch(Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode=1;
}

namespace Seleka2.Actions
{
    public enum ActionBodyMask { FullBody,UpperBody,LowerBody }
    public sealed class ActionDefinition
    {
        public string Id="test",ProviderId="test"; public string DisplayName {get;set;}=""; public string AudioFile {get;set;}=""; public ActionBodyMask BodyMask; public string[] BonePaths=[]; public IActionSource RuntimeSource;
        public bool KeepGameExpressions {get;set;}=true; public bool WriteHipTranslation=true,WriteFaceMorphs=true,RequiresHands=true,RequiresStanding=true;public bool Loop;
    }
    public static class ActionEventSchema {public static void ValidateJson(string json) {}}
    public static class ActionRegistry
    {
        public static Dictionary<string,ActionDefinition> Registered=new();
        public static int Revision;
        public static ActionDefinition[] GetAll()=>Registered.Values.ToArray();
        public static ActionDefinition Get(string id)=>Registered.GetValueOrDefault(id);
        public static void RegisterRuntime(ActionDefinition definition,IActionSource source,string directory,string provider)
        {
            if(Environment.CurrentManagedThreadId!=UnityEngine.Object.EngineThread)throw new Exception("Worker registered a Unity-facing action");
            definition.RuntimeSource=source;Registered.Add(definition.Id,definition);Revision++;
        }
    }
    public sealed class ActionDriver { public static ActionDriver Current; public void Request(string id) {} public void Stop(string reason) {} }
    public sealed class Logger {public readonly List<string> Messages=new();public void LogInfo(string message)=>Messages.Add(message); public void LogWarning(string message)=>Messages.Add(message); }
    public static class Plugin { public static Logger LogSource=new(); }
}
public static class Plugin {public const string Guid="test";public static Seleka2.Actions.Logger LogSource=new();}
namespace HarmonyLib { [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch:Attribute {public HarmonyPatch(Type owner,string method){}} }
namespace ExposureUnnoticed2.Object3D.Player.Scripts {public sealed class PlayerBoneScaleAdjustManager {} public sealed class PlayerBoneScaleAdjuster {}}
namespace ExposureUnnoticed2.Object3D.IngameManager {public sealed class InGameUiManager {public static InGameUiManager Instance;public bool IsOpenSomeUi;}}
