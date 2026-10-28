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
string[] FixedNames()
{
    var names=Enumerable.Range(0,FixedModelFace.ShapeCount).Select(i=>"shape"+i).ToArray();
    names[0]="vrc.v_aa";names[19]="eye_close";
    foreach(var entry in FixedModelFace.MmdMorphs)names[entry.Index]=entry.Name;
    return names;
}
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
    var renderer=body.AddComponent<SkinnedMeshRenderer>(); renderer.bones=bones.Values.ToArray(); renderer.sharedMesh=new Mesh{ShapeNames=FixedNames(),bindposes=bones.Keys.Select(p=>restMatrices[p].inverse).ToArray()};
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
using(var pose=new RuntimeVmdPose(fixture.avatar,definition,Motion(),MmdReferencePose.T))
{
    Check(pose.BoneCount==49 && pose.Apply(0),"Explicit T reference drives the original 49 body bones without a PMD");
    foreach(var path in MotionRig.BonePaths()) Check(Quaternion.Angle(fixture.bones[path].rotation,bind[path].rotation)<.1,"Explicit T preserves the previous bind reference despite a different current idle pose");
}
foreach(var (path,q) in saved) Check(Quaternion.Angle(fixture.bones[path].localRotation,q)<.1,"Stopping restores this playback's original rotations");
Check(Vector3.Distance(savedHip,fixture.bones[hips].localPosition)<1e-5 && fixture.renderer.GetBlendShapeWeight(0)==37,"Stopping restores hip and face state");
var translated=Motion(); translated.tracks["センター"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64]},new(){frame=30,q=Quaternion.identity,interpolation=new byte[64],p=new(0,1,0)}];
using(var pose=new RuntimeVmdPose(fixture.avatar,definition,translated))
{
    pose.Apply(1); var movement=fixture.bones[hips].position-(fixture.bones[hips].parent.localToWorldMatrix.Point(savedHip));
    Check(Math.Abs(movement.y-GameMmdSkeleton.MetersPerMmdUnit)<1e-5,"One VMD displacement unit maps to the established game distance");
    Check(Math.Abs(movement.x)+Math.Abs(movement.z)<1e-5,"Vertical motion stays on the hips while X/Z is delegated to the controller");
}
var fullMove=Motion();fullMove.tracks["センター"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64]},new(){frame=30,q=Quaternion.identity,interpolation=new byte[64],p=new(1,2,3)}];
using(var pose=new RuntimeVmdPose(fixture.avatar,definition,fullMove))
{
    pose.Apply(1);Check(pose.HasRootMotion&&Math.Abs(pose.RootDisplacement.x-.08f)<1e-5&&Math.Abs(pose.RootDisplacement.z-.24f)<1e-5&&pose.RootDisplacement.y==0,"Default VMD exposes complete X/Z displacement at the same 0.08 unit as the camera");
    var boneDelta=fixture.bones[hips].position-fixture.bones[hips].parent.localToWorldMatrix.Point(savedHip);Check(Math.Abs(boneDelta.x)+Math.Abs(boneDelta.z)<1e-5,"Horizontal root movement is not also applied to the live hips");
}
using(var pose=new RuntimeVmdPose(fixture.avatar,new ActionDefinition{Loop=true},fullMove))
    Check(Math.Abs(pose.LoopRootDisplacement.x-.08f)<1e-5&&Math.Abs(pose.LoopRootDisplacement.z-.24f)<1e-5,"Provider publishes per-cycle root displacement for continuous looping");
using(var pose=new RuntimeVmdPose(fixture.avatar,new ActionDefinition{WriteHipTranslation=false},fullMove))
{pose.Apply(1);Check(!pose.HasRootMotion&&pose.RootDisplacement.sqrMagnitude==0,"Explicit translation-off keeps its existing opt-out semantics");}

// A motion can start crouched or airborne: vertical reference is the bind
// skeleton, while horizontal travel is still anchored at the selected start.
var initialHeightActor=Avatar();initialHeightActor.avatar.transform.localScale=Vector3.one*1.2f;
initialHeightActor.avatar.transform.rotation=Quaternion.AngleAxis(73,Vector3.up);
initialHeightActor.avatar.transform.position=new(3,2,-4);
var initialHeightHip=initialHeightActor.bones[hips];var initialHeightBase=initialHeightHip.position;
var initialHeightMotion=Motion();
initialHeightMotion.tracks["センター"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64],p=new(1,-1.25f,2)},new(){frame=30,q=Quaternion.identity,interpolation=new byte[64],p=new(3,2.5f,5)}];
using(var pose=new RuntimeVmdPose(initialHeightActor.avatar,new ActionDefinition{Loop=true},initialHeightMotion))
{
    pose.Apply(0);
    Check(Math.Abs(initialHeightHip.position.y-initialHeightBase.y+.1f)<1e-5,"First-frame crouch preserves its authored 10cm descent on a scaled avatar");
    Check(pose.RootDisplacement.sqrMagnitude<1e-8,"Nonzero first-frame X/Z still anchors to the character's starting position");
    pose.Apply(1);
    Check(Math.Abs(initialHeightHip.position.y-initialHeightBase.y-.2f)<1e-5,"Later authored rise is relative to reference height rather than recentered crouch");
    var cycle=initialHeightActor.avatar.transform.rotation*new Vector3(.16f,0,.24f);
    Check(Vector3.Distance(pose.RootDisplacement,cycle)<1e-5&&Vector3.Distance(pose.LoopRootDisplacement,cycle)<1e-5,"Vertical reference change preserves heading-aware horizontal and loop trajectories");
    for(int i=0;i<3;i++)pose.Apply(0);
    Check(Math.Abs(initialHeightHip.position.y-initialHeightBase.y+.1f)<1e-5,"Repeated loop starts preserve crouch height without accumulating an offset");
}
Check(Vector3.Distance(initialHeightHip.position,initialHeightBase)<1e-5,"Stopping the first-frame crouch restores the native hip baseline");
using(var pose=new RuntimeVmdPose(initialHeightActor.avatar,new ActionDefinition{WriteHipTranslation=false},initialHeightMotion))
{
    pose.Apply(0);Check(Vector3.Distance(initialHeightHip.position,initialHeightBase)<1e-5,"Translation opt-out also suppresses the authored initial vertical offset");
}
var elevatedStart=Motion();elevatedStart.tracks["センター"]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64],p=new(0,1.25f,0)}];
using(var pose=new RuntimeVmdPose(initialHeightActor.avatar,new ActionDefinition(),elevatedStart))
{
    pose.Apply(0);Check(Math.Abs(initialHeightHip.position.y-initialHeightBase.y-.1f)<1e-5,"An airborne first frame remains airborne rather than being locked to the floor");
}
var composedHeight=Motion();
foreach(var (name,height) in new[]{("全ての親",.25f),("センター",-1f),("グルーブ",-.5f)})
    composedHeight.tracks[name]=[new(){frame=0,q=Quaternion.identity,interpolation=new byte[64],p=new(0,height,0)}];
using(var pose=new RuntimeVmdPose(initialHeightActor.avatar,new ActionDefinition(),composedHeight))
{
    pose.Apply(0);Check(Math.Abs(initialHeightHip.position.y-initialHeightBase.y+.1f)<1e-5,"Initial parent/center/groove translations compose before vertical reference conversion");
}

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
using(var pose=new RuntimeVmdPose(metricAvatar.avatar,new ActionDefinition(),fullMove))
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
using(var pose=new RuntimeVmdPose(fixture.avatar,masked,translated))
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
Check(!MmdRigCache.IsPrepared(fixture.avatar),"Cold playback reports pending without constructing a rig");
MmdRigCache.Prepare(fixture.avatar);
Check(MmdRigCache.BuildCount==builds+1,"Prewarm builds one reusable game rig");
Check(MmdRigCache.IsPrepared(fixture.avatar),"Prewarm publishes readiness");
for(int cycle=0;cycle<20;cycle++)
{
    foreach(var node in fixture.bones.Values) node.localRotation=Quaternion.AngleAxis(cycle+3,new(1,1,0));
    var before=fixture.bones.ToDictionary(p=>p.Key,p=>p.Value.localRotation);
    using(var pose=new RuntimeVmdPose(fixture.avatar,cycle%2==0?definition:masked,cycle%2==0?translated:Motion())) { Check(pose.Apply(.5f),"Reused rig accepts changing motion and masks"); }
    foreach(var (path,q) in before) Check(Quaternion.Angle(q,fixture.bones[path].localRotation)<.1,"Every reuse restores the current lease rather than an old snapshot");
}
Check(MmdRigCache.BuildCount==builds+1,"Repeated switches allocate no new sampling rigs");
using(var first=new RuntimeVmdPose(fixture.avatar,definition,Motion()))
{
    using(var second=new RuntimeVmdPose(fixture.avatar,definition,translated)) Check(second.Apply(.5f),"Simultaneous consumer owns a separate solver");
    Check(first.Apply(.5f),"Disposing a second consumer does not destroy the first solver");
}
Check(MmdRigCache.BuildCount==builds+2,"Only concurrent use allocates an independent temporary rig");
MmdRigCache.Invalidate(); MmdRigCache.Prepare(fixture.avatar);
Check(MmdRigCache.BuildCount==builds+2,"ReAdjust without a real shape change validates and reuses the helpers");
var other=Avatar(); MmdRigCache.Prepare(other.avatar);
Check(MmdRigCache.BuildCount==builds+3,"Another live avatar receives an independent cache entry");
UnityEngine.Object.Destroy(other.avatar); MmdRigCache.Prune();
var reload=Avatar();int beforeReload=MmdRigCache.BuildCount;
MmdRigCache.DetachTarget(fixture.avatar.GetInstanceID());
MmdRigCache.Prepare(reload.avatar);
Check(MmdRigCache.BuildCount==beforeReload,"Reloading the same shape rebinds to persistent helpers without rebuilding");
using(var pose=new RuntimeVmdPose(reload.avatar,definition,Motion())) Check(pose.Apply(0),"Rebound rig writes the replacement actor's bones");
var originalMesh=reload.renderer.sharedMesh;
var changed=originalMesh.bindposes.ToArray();int kneeBindIndex=reload.bones.Keys.ToList().IndexOf(hips+"/UpperLeg_L/LowerLeg_L");
changed[kneeBindIndex]=Matrix4x4.TRS(positions[hips+"/UpperLeg_L/LowerLeg_L"]+new Vector3(0,.03f,0),bind[hips+"/UpperLeg_L/LowerLeg_L"].rotation,Vector3.one).inverse;
reload.renderer.sharedMesh=new Mesh{bindposes=changed};MmdRigCache.Invalidate();MmdRigCache.Prepare(reload.avatar);
Check(MmdRigCache.BuildCount==beforeReload+1,"A changed bind shape builds a new solver exactly once");
reload.renderer.sharedMesh=originalMesh;MmdRigCache.Invalidate();MmdRigCache.Prepare(reload.avatar);
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
        var source=Task.Run(()=>new RuntimeVmdSource(vmd)).GetAwaiter().GetResult();
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
            var contactActor=Avatar();contactActor.avatar.transform.localScale=Vector3.one*1.2f;
            var contactHip=contactActor.bones[hips];float contactBase=contactHip.position.y;
            using(var contactPose=source.CreatePose(contactActor.avatar,definition))
            {
                contactPose.Apply(0);float initialDrop=contactHip.position.y-contactBase;
                Check(initialDrop<-.068f&&initialDrop>-.070f,"Actual CH4NGE retains its previously cancelled 6.87cm initial pelvis descent");
                Check(((IActionRootMotionPose)contactPose).RootDisplacement.sqrMagnitude<1e-8,"Actual CH4NGE still starts with no horizontal teleport");
                contactPose.Apply(5);contactPose.Apply(0);
                Check(Math.Abs(contactHip.position.y-contactBase-initialDrop)<1e-5,"Actual CH4NGE reuses the same vertical reference after sampling later frames");
                Console.WriteLine($"CH4NGE initial authored pelvis offset: {initialDrop:F6}m (old first-frame rebasing incorrectly produced 0m)");
            }
            Check(Math.Abs(contactHip.position.y-contactBase)<1e-5,"Stopping actual CH4NGE removes its retained initial vertical offset");
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
            float oldRebasedPelvis=.9206f+(oldScaleSolver.byName["下半身"].node.position.y-oldOriginY)*.08f;
            float initialPelvisOffset=(oldOriginY-oldScaleSolver.byName["下半身"].rest.y)*.08f;
            float expectedPelvis=oldRebasedPelvis+initialPelvisOffset;
            float oldRenderedKnee=oldRebasedPelvis+(oldScaleSolver.byName["左ひざ"].node.position.y-oldScaleSolver.byName["下半身"].node.position.y)*.08f*1.2f;
            float oldKneeAtFixedReference=oldRenderedKnee+initialPelvisOffset;
            var traceMetric=MmdTargetSnapshot.Read(traceActor.avatar);
            using var worldScaleSolver=GameMmdSkeleton.CreateData(traceMetric.Positions);worldScaleSolver.CreateTransforms();worldScaleSolver.useIk=true;
            worldScaleSolver.Evaluate(samsa,25.0733f*30);
            float expectedWorldKnee=expectedPelvis+(worldScaleSolver.byName["左ひざ"].node.position.y-worldScaleSolver.byName["下半身"].node.position.y)*.08f;
            float expectedWorldFoot=expectedPelvis+(worldScaleSolver.byName["左足首"].node.position.y-worldScaleSolver.byName["下半身"].node.position.y)*.08f;
            using(var tracePose=new RuntimeVmdPose(traceActor.avatar,new ActionDefinition(),samsa))
            {
                tracePose.Apply(25.0733f);
                float actualPelvis=traceActor.bones[hips].position.y;
                float actualKnee=traceActor.bones[hips+"/UpperLeg_L/LowerLeg_L"].position.y;
                float actualFoot=traceActor.bones[hips+"/UpperLeg_L/LowerLeg_L/Foot_L"].position.y;
                Check(oldRenderedKnee<-.04f,"Captured runtime reference reproduces old under-floor knee placement");
                Check(Math.Abs(actualPelvis-expectedPelvis)<.0001f&&initialPelvisOffset<-.018f,"Samsa retains initial pelvis descent as well as the world-metric correction");
                Check(Math.Abs(actualKnee-expectedWorldKnee)<.0001f&&actualKnee-oldKneeAtFixedReference>.04f,"Rendered knee follows the world-sized solve at the corrected common height reference");
                Check(Math.Abs(actualFoot-expectedWorldFoot)<.0001f&&actualFoot>.14f&&actualFoot<.19f,"Samsa ankle follows the world-sized solve after preserving the authored initial descent");
                Console.WriteLine($"Samsa runtime matrix regression: pelvis={actualPelvis:F6} oldKnee={oldRenderedKnee:F6} newKnee={actualKnee:F6} newAnkle={actualFoot:F6} initialOffset={initialPelvisOffset:F6}m");
            }

            Check(maxFootError<=maxOldFootError+.001f,"Knee limit does not increase the window's worst pre-existing foot-goal error");
            Console.WriteLine($"Samsa 23-28s runtime-geometry regression: oldFlip={reproduced} maxFixedHinge={maxFixed:F3} kneelFootError={maxKneelFootError:F6}m windowFootError={maxFootError:F6}/{maxOldFootError:F6}m addedError={maxAddedError:F6}m");
        }

        Console.WriteLine($"PASS {Path.GetFileName(folder)}: mode=game-bind-pose duration={source.Duration} retained={source.RetainedTracks} ignored={source.IgnoredTracks}");
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
        if(name=="c-camera-invalid")File.WriteAllText(Path.Combine(folder,"action.json"),"{\"referencePose\":\"T\"}");
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
    var poseProviders=ActionRegistry.GetAll().Select(d=>(RuntimeVmdSource)d.RuntimeSource).ToArray();
    Check(poseProviders.Select(p=>p.ReferencePose).Distinct().Count()==2,"Async pack parsing forwards both default A and JSON T to providers");
    int coldBuilds=MmdRigCache.BuildCount,frames=0,maxCreated=0;
    ExposureUnnoticed2.Object3D.IngameManager.InGameUiManager.Instance=new(){IsOpenSomeUi=true};
    timeout.Restart();
    while(!poseProviders.All(p=>p.IsPrepared(pumpAvatar.avatar)))
    {
        int before=GameObject.CreatedCount;pump.Update();maxCreated=Math.Max(maxCreated,GameObject.CreatedCount-before);frames++;
        if(timeout.Elapsed.TotalSeconds>10)throw new Exception("Rig preparation pump timed out");Thread.Sleep(1);
    }
    ExposureUnnoticed2.Object3D.IngameManager.InGameUiManager.Instance=null;
    Check(frames>10&&maxCreated<=16&&MmdRigCache.BuildCount==coldBuilds+2,"Production pump completes with UI open and preserves bounded A/T object creation");
    var replacement=Avatar();UnityEngine.Object.Destroy(pumpAvatar.avatar);ActionTargetApi.Target=replacement.avatar;
    timeout.Restart();
    while(!poseProviders.All(p=>p.IsPrepared(replacement.avatar)))
    {
        pump.Update();if(timeout.Elapsed.TotalSeconds>10)throw new Exception("Rebind pump timed out");Thread.Sleep(1);
    }
    Check(MmdRigCache.BuildCount==coldBuilds+2&&global::Plugin.LogSource.Messages.Last().Contains("mode=rebind"),"Production actor replacement rebinds both A/T helpers without a new build");
    MmdRigCache.Invalidate();timeout.Restart();
    while(!poseProviders.All(p=>p.IsPrepared(replacement.avatar))) {pump.Update();if(timeout.Elapsed.TotalSeconds>10)throw new Exception("Shape validation timed out");Thread.Sleep(1);}
    Check(MmdRigCache.BuildCount==coldBuilds+2,"ReAdjust marker alone does not force A/T rebuilds in the production pump");
    pump.OnDestroy();
}
finally {Directory.Delete(asyncRoot,true);MmdRigCache.Clear();}

// Production face mixer and VMD integration: the native evaluator supplies a
// fresh baseline each frame; overlays never feed into the next native update.
MmdRigCache.Clear();
var faceFixture=Avatar();var faceRenderer=faceFixture.renderer;
var faceAction=new ActionDefinition();var faceMotion=Motion();
faceMotion.morphs["まばたき"]=[new(0,25),new(1,25)];
faceRenderer.SetBlendShapeWeight(425,40);faceRenderer.SetBlendShapeWeight(25,65);faceRenderer.SetBlendShapeWeight(55,81);
ActionFaceSettings.KeepGameExpressions=true;
using(var pose=new RuntimeVmdPose(faceFixture.avatar,faceAction,faceMotion))
{
    pose.Apply(0);
    Check(faceRenderer.GetBlendShapeWeight(425)==65,"Default VMD overlays native 40 and motion 25");
    Check(faceRenderer.GetBlendShapeWeight(25)==65,"Overlay preserves native non-mapped mouth expression");
    for(int i=0;i<30;i++)pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(425)==65,"Repeated samples do not accumulate overlay");
    pose.RestoreNativeFace();Check(faceRenderer.GetBlendShapeWeight(425)==40,"Pre-native restore removes motion contribution");
    faceRenderer.SetBlendShapeWeight(425,85);pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(425)==100,"Fresh native baseline used with bounded same-channel sum");
    ActionFaceSettings.KeepGameExpressions=false;pose.RestoreNativeFace();pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(425)==25&&faceRenderer.GetBlendShapeWeight(25)==0,"UI off selects action alone, including clearing unmapped native expressions");
    Check(faceRenderer.GetBlendShapeWeight(55)==81,"Unrelated customization shape is unchanged");
    ActionFaceSettings.KeepGameExpressions=true;pose.RestoreNativeFace();pose.Apply(.5f);
    Check(faceRenderer.GetBlendShapeWeight(425)==100&&faceRenderer.GetBlendShapeWeight(25)==65,"UI re-enable takes effect in the same pose");
    faceRenderer.SetBlendShapeWeight(425,12);pose.RestoreNativeFace();
    Check(faceRenderer.GetBlendShapeWeight(425)==12,"Restoration preserves a newer native write");
    pose.Apply(.5f);
}
Check(faceRenderer.GetBlendShapeWeight(425)==12&&faceRenderer.GetBlendShapeWeight(25)==65,"Dispose returns latest native values, not stale start snapshot");
faceAction.KeepGameExpressions=false;
using(var pose=new RuntimeVmdPose(faceFixture.avatar,faceAction,faceMotion))
{
    pose.Apply(0);Check(faceRenderer.GetBlendShapeWeight(425)==25&&faceRenderer.GetBlendShapeWeight(25)==0,"Pack false vetoes UI true");
    ActionFaceSettings.KeepGameExpressions=false;pose.RestoreNativeFace();pose.Apply(0);
    ActionFaceSettings.KeepGameExpressions=true;pose.RestoreNativeFace();pose.Apply(0);
    Check(faceRenderer.GetBlendShapeWeight(425)==25,"Changing UI cannot bypass pack false");
}
faceAction.WriteFaceMorphs=false;
using(var pose=new RuntimeVmdPose(faceFixture.avatar,faceAction,faceMotion))
{
    ActionFaceSettings.KeepGameExpressions=false;pose.Apply(0);
    Check(faceRenderer.GetBlendShapeWeight(425)==12&&faceRenderer.GetBlendShapeWeight(25)==65,"writeFaceMorphs=false opts out of all face writes");
}
// Full-size native mesh boundary: expression shape hashes + native script
// channels are suppressed, while original customization indices are retained.
var full=Avatar();
full.renderer.SetBlendShapeWeight(55,73);full.renderer.SetBlendShapeWeight(25,37);full.renderer.SetBlendShapeWeight(290,88);
var layer=new ActionFaceMorphLayer(full.avatar,new ActionDefinition{KeepGameExpressions=false});layer.Begin();
Check(full.renderer.GetBlendShapeWeight(55)==73,"Native OnChangeBody customization channel remains protected");
Check(full.renderer.GetBlendShapeWeight(25)==0&&full.renderer.GetBlendShapeWeight(290)==0,"Fixed native clip and script-only expression slots are cleared");
layer.RestoreNativeFace();Check(full.renderer.GetBlendShapeWeight(25)==37&&full.renderer.GetBlendShapeWeight(290)==88,"Native-only suppression is reversible");
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
using(var pose=new RuntimeVmdPose(ownedFixture.avatar,new ActionDefinition(),Motion()))
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
using(var pose=new RuntimeVmdPose(ownedFixture.avatar,new ActionDefinition(),Motion()))
{
    pose.BeginNativeBody();ownedKnee.localPosition*=1.1f;pose.EndNativeBody(true);pose.Apply(0);pose.RestoreNativeBody();
}
Check(Vector3.Distance(ownedKnee.localPosition,nativeKnee*1.25f)<1e-5,"Cancel before native evaluation leaves uncorrected basis for the pending native pass");
// Toe correction remains bookkeeping-only for a full body owner; unowned head
// stays with the native game for an explicitly configured lower-body action.
var subset=Avatar();var subsetHead=subset.bones[chest+"/Neck/Head"];var subsetToe=subset.bones[hips+"/UpperLeg_L/LowerLeg_L/Foot_L/Toe_L"];
var toePosition=subsetToe.localPosition;var toeRotation=subsetToe.localRotation;
using(var pose=new RuntimeVmdPose(subset.avatar,new ActionDefinition{BodyMask=ActionBodyMask.LowerBody},Motion()))
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
using(var pose=new RuntimeVmdPose(subset.avatar,new ActionDefinition(),Motion()))
{
    var before=subsetToe.localPosition;pose.BeginNativeBody();subsetToe.localPosition+=Vector3.up;pose.EndNativeBody(false);
    Check(Vector3.Distance(subsetToe.localPosition,before)<1e-5&&!pose.SuppressKneeOffset,"Native failure restores pre-correction transforms and closes scope");
}
MmdRigCache.Clear();


// Reference-pose change is confined to the source arms, then calibrated back
// to the game's original axes. Identity VMD keys must yield the chosen pose.
MmdRigCache.Clear();var referenceActor=Avatar();
var originalReference=referenceActor.bones.ToDictionary(p=>p.Key,p=>(p.Value.localRotation,p.Value.localPosition,p.Value.localScale));
using(var aPose=new RuntimeVmdPose(referenceActor.avatar,new ActionDefinition(),Motion()))
{
    aPose.Apply(0);
    foreach(var (path,bone) in referenceActor.bones)
    {
        Quaternion expected=bind[path].rotation;
        if(path.Contains("/UpperArm_L"))expected=Quaternion.AngleAxis(30,new(0,0,1))*expected;
        if(path.Contains("/UpperArm_R"))expected=Quaternion.AngleAxis(-30,new(0,0,1))*expected;
        Check(Quaternion.Angle(bone.rotation,expected)<.1,"Default A follows the mirrored UnityVMDPlayer 30-degree upper-arm calibration through the fingers");
        Check(Vector3.Distance(bone.localPosition,originalReference[path].localPosition)<1e-5,"A reference does not change live local bone offsets");
    }
}
foreach(var (path,bone) in referenceActor.bones)
    Check(Quaternion.Angle(bone.localRotation,originalReference[path].localRotation)<.1,"Stopping A returns the player's original body rotations");
using(var aData=GameMmdSkeleton.Create(bind,false))
using(var tData=GameMmdSkeleton.Create(bind,false,MmdReferencePose.T))
{
    foreach(var suffix in new[]{("左","_L"),("右","_R")})
    {
        var upper=chest+"/Shoulder"+suffix.Item2+"/UpperArm"+suffix.Item2;
        var lower=upper+"/LowerArm"+suffix.Item2;var hand=lower+"/Hand"+suffix.Item2;
        Check(Math.Abs(Vector3.Distance(aData.byName[suffix.Item1+"腕"].rest,aData.byName[suffix.Item1+"ひじ"].rest)*.08f-Vector3.Distance(positions[upper],positions[lower]))<1e-5,"A source preserves actual upper-arm length");
        Check(Math.Abs(Vector3.Distance(aData.byName[suffix.Item1+"ひじ"].rest,aData.byName[suffix.Item1+"手首"].rest)*.08f-Vector3.Distance(positions[lower],positions[hand]))<1e-5,"A source preserves actual forearm length");
    }
    foreach(var name in new[]{"下半身","上半身","左足","左ひざ","左足首","右足","右ひざ","右足首"})
        Check(Vector3.Distance(aData.byName[name].rest,tData.byName[name].rest)<1e-6,"Reference pose does not change torso or leg reference geometry");
    Check(Vector3.Distance(tData.byName["左手首"].rest*.08f,positions[chest+"/Shoulder_L/UpperArm_L/LowerArm_L/Hand_L"])<1e-5,"T source exactly retains the previous hand reference positions");
}
Reject(()=>GameMmdSkeleton.Create(bind,false,(MmdReferencePose)99),"Invalid programmatic reference pose is rejected");
MmdRigCache.Clear();int referenceBuilds=MmdRigCache.BuildCount;
MmdRigCache.Prepare(referenceActor.avatar,MmdReferencePose.A);
MmdRigCache.Prepare(referenceActor.avatar,MmdReferencePose.T);
Check(MmdRigCache.BuildCount==referenceBuilds+2,"A and T warm independent rigs on one actor");
var aLease=MmdRigCache.Rent(referenceActor.avatar,MmdReferencePose.A);
var tLease=MmdRigCache.Rent(referenceActor.avatar,MmdReferencePose.T);
Check(!ReferenceEquals(aLease,tLease)&&aLease.ReferencePose==MmdReferencePose.A&&tLease.ReferencePose==MmdReferencePose.T,"A/T playback cannot borrow the other mode's cached solver");
Check(aLease.SkeletonKey!=tLease.SkeletonKey,"Reference pose participates in cache identity");
MmdRigCache.Return(aLease);MmdRigCache.Return(tLease);
MmdRigCache.Invalidate();MmdRigCache.Prepare(referenceActor.avatar,MmdReferencePose.T);MmdRigCache.Prepare(referenceActor.avatar,MmdReferencePose.A);
Check(MmdRigCache.BuildCount==referenceBuilds+2,"Revalidation reuses both reference modes without rebuilding");
var referenceReload=Avatar();MmdRigCache.DetachTarget(referenceActor.avatar.GetInstanceID());
MmdRigCache.Prepare(referenceReload.avatar,MmdReferencePose.A);MmdRigCache.Prepare(referenceReload.avatar,MmdReferencePose.T);
Check(MmdRigCache.BuildCount==referenceBuilds+2,"A/T caches both rebind across same-shape reloads");
MmdRigCache.Clear();

// Fixed base-model binding: no hierarchy/morph discovery; independent
// native overlay, immutable channel table and bind-layout reuse on reload.
MmdRigCache.Clear();ActionFaceSettings.KeepGameExpressions=true;
var namedFace=Avatar();var namedRenderer=namedFace.renderer;
var extraObject=new GameObject("ExtraFace");extraObject.transform.SetParent(namedFace.avatar.transform,false);
var extra=extraObject.AddComponent<SkinnedMeshRenderer>();extra.sharedMesh=new Mesh{ThrowOnNameRead=true};
for(int i=0;i<1000;i++){var child=new GameObject("Unused"+i);child.transform.SetParent(extraObject.transform,false);}
int rendererQueries=GameObject.RendererQueries;
var expressive=Motion();expressive.morphs["笑い"]=[new(0,20),new(1,70)];expressive.morphs["まばたき"]=[new(0,25)];expressive.morphs["にこり"]=[new(0,30)];expressive.morphs["ウィンク右"]=[new(0,40)];
namedRenderer.SetBlendShapeWeight(426,10);namedRenderer.SetBlendShapeWeight(425,5);namedRenderer.SetBlendShapeWeight(19,40);namedRenderer.SetBlendShapeWeight(55,81);
using(var pose=new RuntimeVmdPose(namedFace.avatar,new ActionDefinition(),expressive))
{
    int validatedReads=namedRenderer.sharedMesh.NameReads;
    pose.Apply(.5f);
    Check(namedRenderer.GetBlendShapeWeight(426)==55&&namedRenderer.GetBlendShapeWeight(465)==30&&namedRenderer.GetBlendShapeWeight(428)==40,"Fixed smile/brow/wink slots sample interpolated VMD morph tracks");
    Check(namedRenderer.GetBlendShapeWeight(19)==40&&namedRenderer.GetBlendShapeWeight(425)==30,"MMD blink does not also write the native English blink shape");
    for(int i=0;i<5;i++)pose.Apply(.5f);
    Check(namedRenderer.GetBlendShapeWeight(426)==55,"Fixed morph overlay does not accumulate");
    pose.RestoreNativeFace();Check(namedRenderer.GetBlendShapeWeight(426)==10,"Pre-native restore removes the current action contribution");
    namedRenderer.SetBlendShapeWeight(426,13);pose.Apply(.5f);
    Check(namedRenderer.GetBlendShapeWeight(426)==58,"Fixed morph uses the newest native baseline");
    Check(namedRenderer.sharedMesh.NameReads==validatedReads&&extra.sharedMesh.NameReads==0,"Sampling never reads morph names or inspects extra renderers");
}
Check(namedRenderer.GetBlendShapeWeight(426)==13&&namedRenderer.GetBlendShapeWeight(425)==5&&namedRenderer.GetBlendShapeWeight(55)==81,"Disposal restores expression weights without changing customization");
using(var pose=new RuntimeVmdPose(namedFace.avatar,new ActionDefinition{KeepGameExpressions=false},expressive))
{
    pose.Apply(.5f);Check(namedRenderer.GetBlendShapeWeight(426)==45&&namedRenderer.GetBlendShapeWeight(425)==25&&namedRenderer.GetBlendShapeWeight(19)==0,"Pack veto suppresses native expressions while playing fixed MMD channels");
}
var onlySmile=Motion();onlySmile.morphs["笑い"]=[new(0,12)];
int beforeNames=namedRenderer.sharedMesh.NameReads,beforeBind=namedRenderer.sharedMesh.BindReads;
using(var pose=new RuntimeVmdPose(namedFace.avatar,new ActionDefinition(),onlySmile))
{
    pose.Apply(.5f);Check(namedRenderer.GetBlendShapeWeight(426)==25&&namedRenderer.GetBlendShapeWeight(425)==5,"A reused rig binds only this motion's declared fixed channels");
}
using(var pose=new RuntimeVmdPose(namedFace.avatar,new ActionDefinition{WriteFaceMorphs=false},expressive))
{
    pose.Apply(.5f);Check(namedRenderer.GetBlendShapeWeight(426)==13,"Face opt-out skips all fixed morph writes");
}
Check(namedRenderer.sharedMesh.NameReads==beforeNames&&namedRenderer.sharedMesh.BindReads==beforeBind,"Switching actions does not rediscover morphs or reread bind poses");
var reloadFixed=Avatar();reloadFixed.renderer.sharedMesh=namedRenderer.sharedMesh;
int beforeReloadNames=namedRenderer.sharedMesh.NameReads,beforeReloadBind=namedRenderer.sharedMesh.BindReads,buildsBeforeReload=MmdRigCache.BuildCount;
MmdRigCache.DetachTarget(namedFace.avatar.GetInstanceID());MmdRigCache.Prepare(reloadFixed.avatar);
Check(MmdRigCache.BuildCount==buildsBeforeReload,"Same model and body reload rebinds the existing solver");
Check(namedRenderer.sharedMesh.NameReads==beforeReloadNames&&namedRenderer.sharedMesh.BindReads==beforeReloadBind,"Reload only rebinds references: no morph-name reads or bind-pose reconstruction");
using(var pose=new RuntimeVmdPose(reloadFixed.avatar,new ActionDefinition(),expressive))
{
    pose.Apply(.5f);Check(reloadFixed.renderer.GetBlendShapeWeight(426)==45&&namedRenderer.GetBlendShapeWeight(426)==13,"Reloaded action writes the replacement renderer, not the detached actor");
}
Check(GameObject.RendererQueries==rendererQueries,"Preparation/playback never calls generic renderer hierarchy enumeration");
var normalizationMotion=Motion();normalizationMotion.morphs["ｳｨﾝｸ２"]=[new(0,20)];normalizationMotion.morphs["かわいいあ"]=[new(0,50)];
var normalLayer=new ActionFaceMorphLayer(reloadFixed.avatar,new ActionDefinition());
var normalized=MmdMorphBindings.Create(reloadFixed.renderer,normalizationMotion,normalLayer,out int ambiguous);
Check(normalized.Count==1&&normalized[0].Index==429&&ambiguous==0,"Fixed target slots normalize VMD spelling without arbitrary substring matching");
var bad=Avatar();bad.renderer.sharedMesh.ShapeNames[425]="wrong-layout";
using(var pose=new RuntimeVmdPose(bad.avatar,new ActionDefinition(),expressive))
{
    Check(pose.Apply(.5f)&&bad.renderer.GetBlendShapeWeight(425)==0,"Unexpected model layout disables morph writes while keeping body animation available");
}
Check(Seleka2.Actions.Plugin.LogSource.Messages.Any(m=>m.Contains("FIXED_FACE_LAYOUT_MISMATCH")),"Unsupported layout produces a clear one-time diagnostic");
var protectedFace=Avatar();protectedFace.renderer.SetBlendShapeWeight(55,71);protectedFace.renderer.SetBlendShapeWeight(145,72);protectedFace.renderer.SetBlendShapeWeight(373,73);
var protectedLayer=new ActionFaceMorphLayer(protectedFace.avatar,new ActionDefinition{KeepGameExpressions=false});
Check(!protectedLayer.BindMorph(protectedFace.renderer,55)&&!protectedLayer.BindMorph(protectedFace.renderer,145)&&!protectedLayer.BindMorph(protectedFace.renderer,373),"Appearance slots cannot be registered by the fixed morph layer");
protectedLayer.Begin();protectedLayer.Write(protectedFace.renderer,55,100);protectedLayer.Write(protectedFace.renderer,145,100);protectedLayer.Write(protectedFace.renderer,373,100);
Check(protectedFace.renderer.GetBlendShapeWeight(55)==71&&protectedFace.renderer.GetBlendShapeWeight(145)==72&&protectedFace.renderer.GetBlendShapeWeight(373)==73,"Eye/lash customization remains intact");
protectedLayer.RestoreNativeFace();ActionFaceSettings.KeepGameExpressions=true;MmdRigCache.Clear();

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
