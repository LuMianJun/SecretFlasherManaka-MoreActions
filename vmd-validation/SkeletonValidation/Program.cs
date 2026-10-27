using System.Text;
using UnityEngine;
using Seleka2.Actions;
using Seleka2.Mmd;

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

(GameObject avatar,Dictionary<string,Transform> bones,SkinnedMeshRenderer renderer) Avatar()
{
    var avatar=new GameObject("Avatar"); var armature=new GameObject("Armature").transform; armature.SetParent(avatar.transform,false); armature.localRotation=Quaternion.AngleAxis(90,new(1,0,0));
    var bones=new Dictionary<string,Transform>();
    foreach(var path in positions.Keys.OrderBy(p=>p.Count(c=>c=='/')))
    {
        int slash=path.LastIndexOf('/'); var node=new GameObject(path[(slash+1)..]).transform;
        node.SetParent(avatar.transform.Find(path[..slash]),false); node.position=positions[path]; node.rotation=bind[path].rotation; bones[path]=node;
    }
    var body=new GameObject("Body.001"); body.transform.SetParent(avatar.transform,false);
    var renderer=body.AddComponent<SkinnedMeshRenderer>(); renderer.bones=bones.Values.ToArray(); renderer.sharedMesh=new Mesh{bindposes=bones.Keys.Select(p=>bind[p].inverse).ToArray()};
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

Console.WriteLine($"PASS {checks} assertions: production topology/parser/retargeter/cache/async pump; synthetic scene math boundary, not rendered game verification");

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
