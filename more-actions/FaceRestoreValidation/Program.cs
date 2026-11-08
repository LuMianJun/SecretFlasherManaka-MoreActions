using Seleka2.Actions;
using UnityEngine;

const string hipPath="Armature/Hips";
const string neckPath="Armature/Hips/Spine/Chest/Neck";
const string headPath=neckPath+"/Head";
int count=0;
void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
bool Rotation(Transform bone,float degrees)=>Quaternion.Angle(bone.localRotation,Quaternion.AngleAxis(degrees,Vector3.up))<.01f;
GameObject Avatar()
{
    var root=new GameObject("Avatar");root.AddComponent<Animator>();
    foreach(var path in MotionRig.BonePaths())
    {
        var parent=root.transform;
        foreach(var part in path.Split('/'))
        {
            var child=parent.Find(part);if(child==null){child=new GameObject(part).transform;child.SetParent(parent,false);}parent=child;
        }
    }
    root.transform.Find(hipPath).localPosition=new(0,1,0);
    root.transform.Find(neckPath).localRotation=Quaternion.AngleAxis(5,Vector3.up);
    root.transform.Find(headPath).localRotation=Quaternion.AngleAxis(10,Vector3.up);
    return root;
}
LoadedAction Motion(ActionDefinition definition)=>new(){Definition=definition,Clip=new(){Evaluate=(proxy,time)=>{
    proxy.transform.Find(headPath).localRotation=Quaternion.AngleAxis(80,Vector3.up);
    proxy.transform.Find(neckPath).localRotation=Quaternion.AngleAxis(55,Vector3.up);
    proxy.transform.Find(hipPath).localPosition+=new Vector3(0,time*.1f,0);
}}};

foreach(bool native in new[]{false,true})
{
    var root=Avatar();var head=root.transform.Find(headPath);var neck=root.transform.Find(neckPath);var hip=root.transform.Find(hipPath);
    var animator=root.GetComponent<Animator>();
    var pose=new ActionPose(root,Motion(new(){WriteFaceMorphs=false}),native);
    pose.BeginNativeBody();
    head.localRotation=Quaternion.AngleAxis(25,Vector3.up);neck.localRotation=Quaternion.AngleAxis(18,Vector3.up);
    hip.localPosition=new(0,1.2f,0);head.localPosition=new(0,.16f,.02f);head.localScale=new(1.1f,1.05f,1);
    pose.EndNativeBody(true);pose.Apply(1);
    Check(Rotation(head,80)&&Rotation(neck,55),"Imported pose writes the controlled head and neck");
    Check(Math.Abs(hip.localPosition.y-1.3f)<.0001f,"Hip motion is relative to the latest native/customized baseline");
    pose.Dispose();pose.Dispose();
    Check(Rotation(head,25)&&Rotation(neck,18),"Stop must restore latest native head/neck, not the start-of-play snapshot");
    Check(Vector3.Distance(hip.localPosition,new(0,1.2f,0))<.0001f,"Stop preserves updated native hip height");
    Check(Vector3.Distance(head.localPosition,new(0,.16f,.02f))<.0001f&&Vector3.Distance(head.localScale,new(1.1f,1.05f,1))<.0001f,"Restore preserves native head position and customization scale");
    Check(animator.cullingMode==AnimatorCullingMode.CullUpdateTransforms,"Dispose returns the original Animator culling policy");
}

foreach(bool native in new[]{false,true})
{
    var waiting=Avatar();var head=waiting.transform.Find(headPath);
    var pose=new ActionPose(waiting,Motion(new(){WriteFaceMorphs=false}),native);
    head.localRotation=Quaternion.AngleAxis(32,Vector3.up);pose.Dispose();
    Check(Rotation(head,32),"Cancel before the first applied frame must not overwrite a newer native head pose");
}
var withdrawn=Avatar();var withdrawnHead=withdrawn.transform.Find(headPath);
var withdrawnLease=new ActionBodyPoseLease(withdrawn,new[]{withdrawnHead});
withdrawnLease.BeginNativeBody();withdrawnHead.localRotation=Quaternion.AngleAxis(21,Vector3.up);withdrawnLease.EndNativeBody(true);
withdrawnLease.ApplyBaseline();withdrawnHead.localRotation=Quaternion.AngleAxis(80,Vector3.up);withdrawnLease.RestoreNativeBody();
withdrawnHead.localRotation=Quaternion.AngleAxis(37,Vector3.up);withdrawnLease.Dispose();
Check(Rotation(withdrawnHead,37),"After withdrawing the mod output, stop must leave later native writes intact");
var interrupted=Avatar();var interruptedHead=interrupted.transform.Find(headPath);
var interruptedLease=new ActionBodyPoseLease(interrupted,new[]{interruptedHead});interruptedLease.BeginNativeBody();
var lateCorrection=interruptedLease.PreserveNativeCorrection(ActionBoneCorrection.LookAt);
interruptedHead.localRotation=Quaternion.AngleAxis(48,Vector3.up);interruptedLease.Dispose();lateCorrection.Dispose();
Check(Rotation(interruptedHead,48),"A correction scope finishing after ownership ends cannot restore a stale native head pose");

var partial=Avatar();var partialHead=partial.transform.Find(headPath);
using(var pose=new ActionPose(partial,Motion(new(){WriteFaceMorphs=false,BonePaths=new[]{hipPath}})))
{
    partialHead.localRotation=Quaternion.AngleAxis(34,Vector3.up);pose.BeginNativeBody();pose.EndNativeBody(true);pose.Apply(1);
    Check(Rotation(partialHead,34),"Unowned head/face bones remain native while another body region animates");
}
Check(Rotation(partialHead,34),"Stopping a partial action cannot restore an unowned head");

var failing=Avatar();var failHead=failing.transform.Find(headPath);var failAnimator=failing.GetComponent<Animator>();
bool failed=false;
try { _=new ActionPose(failing,new LoadedAction{Definition=new(){WriteFaceMorphs=false},Clip=new(){Evaluate=(_,_)=>throw new Exception("fixture sample failure")}}); }
catch(Exception){failed=true;}
Check(failed&&Rotation(failHead,10)&&failAnimator.cullingMode==AnimatorCullingMode.CullUpdateTransforms,"Failed construction releases body ownership and restores native culling without leaving a partial head pose");

var faceRoot=Avatar();var node=new GameObject("Body.001");node.transform.SetParent(faceRoot.transform,false);
var face=node.AddComponent<SkinnedMeshRenderer>();face.sharedMesh=new Mesh{ShapeNames=Enumerable.Range(0,473).Select(i=>"shape"+i).ToArray()};
face.sharedMesh.ShapeNames[0]="vrc.v_aa";face.sharedMesh.ShapeNames[19]="eye_close";
foreach(var slot in FixedModelFace.MmdMorphs)face.sharedMesh.ShapeNames[slot.Index]=slot.Name;
face.SetBlendShapeWeight(19,30);face.SetBlendShapeWeight(0,5);face.SetBlendShapeWeight(145,75);
var overlay=new ActionFaceMorphLayer(faceRoot,new());
Check(!overlay.BindMorph(face,145),"Imported channels cannot bind protected facial-customization slots");
overlay.Begin();overlay.Write(face,19,25);overlay.Write(face,0,40);
Check(face.GetBlendShapeWeight(19)==55&&face.GetBlendShapeWeight(0)==45,"Overlay adds motion only to native expression weights");
overlay.RestoreNativeFace();overlay.RestoreNativeFace();
Check(face.GetBlendShapeWeight(19)==30&&face.GetBlendShapeWeight(0)==5&&face.GetBlendShapeWeight(145)==75,"Stopping overlay restores native expressions without resetting appearance");
ActionFaceSettings.KeepGameExpressions=false;
var suppression=new ActionFaceMorphLayer(faceRoot,new());suppression.Begin();suppression.Write(face,19,70);
Check(face.GetBlendShapeWeight(19)==70&&face.GetBlendShapeWeight(0)==0&&face.GetBlendShapeWeight(145)==75,"Suppression clears expressions while preserving customized face shape");
face.SetBlendShapeWeight(19,12);suppression.RestoreNativeFace();
Check(face.GetBlendShapeWeight(19)==12&&face.GetBlendShapeWeight(0)==5,"Stop keeps newer native writes and restores only its own remaining output");
ActionFaceSettings.KeepGameExpressions=true;
var veto=new ActionFaceMorphLayer(faceRoot,new(){KeepGameExpressions=false});veto.Begin();veto.Write(face,19,40);veto.RestoreNativeFace();
Check(face.GetBlendShapeWeight(19)==12&&face.GetBlendShapeWeight(145)==75,"Per-action no-overlay veto remains reversible");
var optout=new ActionFaceMorphLayer(faceRoot,new(){WriteFaceMorphs=false});optout.Begin();optout.Write(face,19,100);optout.RestoreNativeFace();
Check(face.GetBlendShapeWeight(19)==12,"Face opt-out cannot write or reset expression channels");
Console.WriteLine($"PASS {count} assertions: production head/neck/hip pose restoration, latest native baseline, customization, partial ownership and expression cleanup; offline boundaries, not proof of the unreported face symptom.");
