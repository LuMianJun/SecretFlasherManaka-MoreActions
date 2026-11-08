using UnityEngine;
using SFM.Actions;
using ExposureUnnoticed2.Object3D.Player.Scripts;
int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
bool Near(float a,float b)=>MathF.Abs(a-b)<.0001f;
AnimationClip Y(string name,Func<float,float> value,bool loop=false)=>new(){name=name,isLooping=loop,Evaluate=(go,t)=>{
 var node=go.transform.Find("CameraTarget");var p=node.localPosition;p.y=value(t);node.localPosition=p;
}};
(PlayerController Player,Transform Pivot,Transform Fp,Animator Animator,AnimationClip Replacement) Scene(AnimationClip original)
{
 NativeCameraTrack.Release();NativeAnimationReplacements.Sources.Clear();NativeAnimationReplacements.Bound=true;
 var root=new GameObject("Avatar");var pivot=new GameObject("CameraTarget");pivot.transform.SetParent(root.transform,false);pivot.transform.localPosition=new(3,1.137f,4);pivot.transform.localRotation=Quaternion.AngleAxis(19,Vector3.up);
 var child=new GameObject("CameraTarget2");child.transform.SetParent(pivot.transform,false);
 var parent=new GameObject("FirstPersonCameraParent");parent.transform.SetParent(root.transform,false);
 var fp=new GameObject("FirstPersonCamera");fp.transform.SetParent(parent.transform,false);
 var replacement=new AnimationClip{name="Replacement"};
 var animator=new Animator{transform=root.transform,Current=new[]{new[]{new AnimatorClipInfo{clip=replacement,weight=1}}}};
 var player=new PlayerController{Pca=new(){Animator=animator,PlayerAvatarObjectReferencer=new(){CameraTarget=child.transform},PlayerLocomotion=new(){fpCameraController=new(){transform=parent.transform,Child=fp.transform}}}};
 NativeAnimationReplacements.Sources[replacement]=original;return(player,pivot.transform,fp.transform,animator,replacement);
}
var position=Scene(Y("Position",t=>1+t));position.Animator.Times[0]=.5f;NativeCameraTrack.Apply(position.Player);
Check(Near(position.Pivot.localPosition.y,1.5f),"Position track is sampled");
Check(Quaternion.Angle(position.Pivot.localRotation,Quaternion.AngleAxis(19,Vector3.up))<.01f,"A position-only track must never write its sentinel rotation");
Check(Near(position.Pivot.localPosition.x,3)&&Near(position.Pivot.localPosition.z,4),"Unkeyed position components are preserved");
position.Pivot.localPosition=new(8,1.5f,9);NativeCameraTrack.Apply(position.Player);
Check(Near(position.Pivot.localPosition.x,8)&&Near(position.Pivot.localPosition.z,9),"Unkeyed components keep newer native writes");
var rotation=Scene(new(){name="Rotation",Evaluate=(go,t)=>go.transform.Find("CameraTarget").localRotation=Quaternion.identity});NativeCameraTrack.Apply(rotation.Player);
Check(Near(rotation.Pivot.localPosition.y,1.137f)&&Near(rotation.Pivot.localPosition.x,3),"Rotation-only track does not write sentinel positions");
Check(Quaternion.Angle(rotation.Pivot.localRotation,Quaternion.identity)<.01f,"Authored rotation is sampled");
var blend=Scene(Y("A",t=>1+t));blend.Animator.Times[0]=.5f;blend.Animator.Transition[0]=true;blend.Animator.Current[0][0].weight=.5f;
blend.Animator.Next[0]=new[]{new AnimatorClipInfo{clip=Y("B",t=>4),weight=.5f}};blend.Animator.NextTimes[0]=.25f;NativeCameraTrack.Apply(blend.Player);
Check(Near(blend.Pivot.localPosition.y,2.75f),"Crossfades include the unreplaced next state with its real weight");
var next=blend.Animator.Next[0][0].clip;NativeAnimationReplacements.Sources[next]=Y("B original",t=>4+t);NativeCameraTrack.Apply(blend.Player);
Check(Near(blend.Pivot.localPosition.y,2.875f),"Two replaced transition states use their separate normalized times");
var end=Scene(Y("One shot",t=>1+t));end.Animator.Times[0]=1;NativeCameraTrack.Apply(end.Player);
Check(Near(end.Pivot.localPosition.y,2),"One-shot camera holds the final key at normalized time one");
end.Animator.Times[0]=4;NativeCameraTrack.Apply(end.Player);
Check(Near(end.Pivot.localPosition.y,2),"One-shot time beyond the end stays clamped");
var loop=Scene(Y("Loop",t=>1+t,true));loop.Animator.Times[0]=1.25f;NativeCameraTrack.Apply(loop.Player);
Check(Near(loop.Pivot.localPosition.y,1.25f),"Looping originals retain periodic camera playback");
var fractional=Scene(Y("Fraction",t=>t));NativeAnimationReplacements.Sources[fractional.Replacement].length=1.05f;fractional.Animator.Times[0]=1;NativeCameraTrack.Apply(fractional.Player);
Check(Near(fractional.Pivot.localPosition.y,1.05f),"Final cache bucket never samples beyond a fractional clip duration");
var fpScene=Scene(new(){name="First person",Evaluate=(go,t)=>go.transform.Find("FirstPersonCameraParent/FirstPersonCamera").localPosition=new(0,-.4166f,-.0041f)});
NativeCameraTrack.Apply(fpScene.Player);
Check(Near(fpScene.Fp.localPosition.y,-.4166f)&&Near(fpScene.Fp.localPosition.z,-.0041f),"First-person marker sampling targets the animated child");
Check(Near(fpScene.Fp.parent.localPosition.y,0),"First-person controller parent remains native");
var supplied=Scene(new(){name="No original camera"});supplied.Replacement.Evaluate=(go,t)=>go.transform.Find("CameraTarget").localPosition=new(77,88,99);NativeCameraTrack.Apply(supplied.Player);
Check(Near(supplied.Pivot.localPosition.y,1.137f),"Replacement-only camera tracks are discarded in favour of original defaults");
var overlay=Scene(Y("Lower",t=>2));overlay.Animator.Current=new[]{overlay.Animator.Current[0],new[]{new AnimatorClipInfo{clip=Y("Upper native",t=>4),weight=1}}};
overlay.Animator.Next=new[]{Array.Empty<AnimatorClipInfo>(),Array.Empty<AnimatorClipInfo>()};overlay.Animator.Weights=new[]{1f,.5f};overlay.Animator.Times=new[]{0f,0f};overlay.Animator.NextTimes=new[]{0f,0f};overlay.Animator.Transition=new[]{false,false};NativeCameraTrack.Apply(overlay.Player);
Check(Near(overlay.Pivot.localPosition.y,3),"Higher native override layers retain their camera contribution");
NativeAnimationReplacements.Bound=false;overlay.Pivot.localPosition=new(3,7,4);NativeCameraTrack.Apply(overlay.Player);
Check(Near(overlay.Pivot.localPosition.y,7),"Unbound controllers never receive marker writes");
NativeCameraTrack.Release();NativeCameraTrack.Release();
Console.WriteLine($"PASS {checks} assertions: production camera tracks, per-property ownership, first-person child, weighted current/next states, layer composition and loop/end sampling; Unity rendering is verified separately.");
