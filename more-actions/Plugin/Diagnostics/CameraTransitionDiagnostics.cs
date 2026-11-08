using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using BepInEx.Configuration;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using HarmonyLib;
using UnityEngine;

namespace SFM.Actions;

// Read-only snapshots: four selected frames per crouch/action/controller
// transition, not a continuous camera trace and never an offset correction.
internal static class CameraTransitionDiagnostics
{
    static readonly JsonSerializerOptions json=new(){NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
    static readonly float[] times={0,.15f,.45f,1};
    static ConfigEntry<bool> enabled;
    static PlayerController owner;
    static bool crouching;
    static string motion="",lastMotion="",reason="";
    static float start,stopped;
    static int step=4,selectedFrame=-1,emittedFrame=-1;
    static object beforeOffset,afterOffset;
    static bool faulted;

    internal static void Configure(ConfigFile config)
        =>enabled=config.Bind("Diagnostics","CameraTransitions",true,"Temporary read-only camera snapshots on crouch/stand, custom playback and replacement changes. At most four frames per transition; no camera correction.");
    static bool Ready=>enabled?.Value==true&&!faulted;
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    static float[] Q(Quaternion q)=>new[]{q.x,q.y,q.z,q.w};
    static object T(Transform t)=>t==null?null:new{id=t.GetInstanceID(),name=t.name,world=V(t.position),local=V(t.localPosition),scale=V(t.localScale),rotation=Q(t.rotation),localRotation=Q(t.localRotation)};
    static object FaceShapes(GameObject target)
    {
        var face=FixedModelFace.Find(target);if(face==null)return null;
        int[] indices={19,0,425,426,443,145,146};var values=new float[indices.Length];
        for(int i=0;i<values.Length;i++)values[i]=face.GetBlendShapeWeight(indices[i]);
        return new{indices,values};
    }
    static void Guard(Action work)
    {
        if(!Ready)return;
        try{work();}catch(Exception e){Failed(e);}
    }
    static void Failed(Exception e){faulted=true;Plugin.LogSource?.LogWarning("ACTION_CAMERA_DIAGNOSTIC_FAILED "+e.Message);}
    static void Schedule(string value)
    {
        if(step<4&&Time.unscaledTime-start<.05f){if(!reason.Contains(value,StringComparison.Ordinal))reason+="+"+value;return;}
        reason=value;start=Time.unscaledTime;step=0;
    }
    static bool Observe(PlayerController player)
    {
        if(player==null||player.Pca?.PlayerState==null)return false;
        var state=player.Pca.PlayerState;
        string active=ActionPlaybackApi.IsPlaying?ActionPlaybackApi.CurrentActionId:"";
        if(owner!=player){owner=player;crouching=state.IsCrouch;motion=active;step=4;selectedFrame=-1;return true;}
        if(crouching!=state.IsCrouch){crouching=state.IsCrouch;Schedule(crouching?"crouch":"stand");}
        if(active!=motion)
        {
            if(motion.Length>0){lastMotion=motion;stopped=Time.unscaledTime;}
            motion=active;Schedule(active.Length==0?"extra-stop":"extra-start");
        }
        return true;
    }
    static void SelectFrame()
    {
        if(step>=times.Length||Time.unscaledTime<start+times[step])return;
        if(selectedFrame==Time.frameCount)return;
        selectedFrame=Time.frameCount;beforeOffset=null;afterOffset=null;
    }
    internal static void BeginFrame(PlayerController player)
    {
        if(!Ready)return;
        try{if(Observe(player))SelectFrame();}catch(Exception e){Failed(e);}
    }
    static object OffsetState()
    {
        var p=owner.Pca;var target=p.PlayerAvatarObjectReferencer.CameraTarget;
        return new{avatar=T(p.AvatorTransform),targetParent=T(target==null?null:target.parent),offset=p.PlayerAvatarOffsetManager.OffsetY};
    }
    internal static void BeforeOffset(PlayerAvatarOffsetManager manager)
    {
        if(!Ready||selectedFrame!=Time.frameCount||owner==null)return;
        Guard(()=>{if(manager.pca?.Pointer==owner.Pca?.Pointer)beforeOffset=OffsetState();});
    }
    internal static void AfterOffset(PlayerAvatarOffsetManager manager)
    {
        if(!Ready||selectedFrame!=Time.frameCount||owner==null)return;
        Guard(()=>{if(manager.pca?.Pointer==owner.Pca?.Pointer)afterOffset=OffsetState();});
    }
    internal static void EndFrame()=>Guard(()=>{
        if(!Observe(PlayerController.Instance))return;
        SelectFrame();
        if(selectedFrame!=Time.frameCount||emittedFrame==Time.frameCount)return;
        Write("after-camera-and-pose");emittedFrame=Time.frameCount;step++;
    });
    internal static void ReplacementChange(Animator animator,string phase)=>Guard(()=>{
        var player=PlayerController.Instance;
        if(player?.Pca?.Animator!=animator||!Observe(player))return;
        Schedule("replacement-change");beforeOffset=null;afterOffset=null;Write(phase);
    });

    static void Write(string phase)
    {
        var p=owner.Pca;var state=p.PlayerState;var avatar=p.PlayerAvatarObjectReferencer;
        var movement=p.PlayerLocomotion;var fp=movement.fpCameraController;var tp=movement.tpCameraController;var main=movement.baseCameraController;
        var animator=p.Animator;var body=p.PlayerBodyCustomizeAdjuster;
        var controller=animator.runtimeAnimatorController;var wrapper=controller==null?null:controller.TryCast<AnimatorOverrideController>();
        var face=owner.faceCameraController;
        var layers=new object[animator.layerCount];
        for(int i=0;i<layers.Length;i++){var s=animator.GetCurrentAnimatorStateInfo(i);layers[i]=new{index=i,state=s.fullPathHash,time=s.normalizedTime,length=s.length,transition=animator.IsInTransition(i)};}
        Plugin.LogSource.LogInfo("ACTION_CAMERA_TRANSITION "+JsonSerializer.Serialize(new{
            phase,reason,frame=Time.frameCount,seconds=Time.unscaledTime,sample=step,
            crouching=state.IsCrouch,firstPerson=state.IsStrafe,nativeAction=state.CurrentAction==null?-1:(int)state.CurrentAction.Type,
            extra=motion,provider=ActionPlaybackApi.CurrentProviderId,lastExtra=lastMotion,secondsSinceExtraStop=lastMotion.Length==0?(float?)null:Time.unscaledTime-stopped,
            replacements=NativeAnimationReplacements.EnabledForDiagnostics,
            controller=controller==null?null:new{id=controller.GetInstanceID(),name=controller.name,isOverride=wrapper!=null,baseId=wrapper?.runtimeAnimatorController==null?(int?)null:wrapper.runtimeAnimatorController.GetInstanceID()},
            root=T(owner.transform),avatar=T(p.AvatorTransform),hip=T(avatar.Hip),neck=T(avatar.Neck),target=T(avatar.CameraTarget),
            head=T(avatar.Neck==null?null:avatar.Neck.Find("Head")),
            targetParent=T(avatar.CameraTarget==null?null:avatar.CameraTarget.parent),
            main=T(main?.Camera?.transform),locked=main?.IsLockCamera,
            fp=T(fp?.transform),fpChild=T(fp?.Child),fpTargetOffset=fp==null?null:V(fp.targetOffset),fpNeckOffset=fp==null?null:V(fp.neckOffset),
            tp=T(tp?.transform),tpTarget=T(tp?.cameraTarget),
            avatarOffset=p.PlayerAvatarOffsetManager.OffsetY,bodyOffset=body.AvatarYOffset,crouchOffset=body.AvatarCrouchYOffset,crouchOffsetB=body.AvatarCrouchYOffsetB,
            knee=p.PlayerKneeAdjuster.KneeOffsetY,culling=animator.cullingMode.ToString(),animator.enabled,animator.isInitialized,animator.speed,layers,
            faceCamera=T(face?.camera?.transform),facePosition=T(avatar.FaceCameraPosiiton),faceTarget=T(avatar.FaceCameraTarget),
            facePositionParent=T(avatar.FaceCameraPosiiton==null?null:avatar.FaceCameraPosiiton.parent),
            faceTargetParent=T(avatar.FaceCameraTarget==null?null:avatar.FaceCameraTarget.parent),
            faceCurrentOffset=face==null?null:V(face.currentOffset),faceBaseOffset=face==null?null:V(face.offset),faceOffsetY=face?.offsetY,
            faceTrackingOwned=face!=null&&(ActionDriver.Current?.DeferFaceCamera(face)??false),
            cameraTrack=NativeCameraTrack.State,
            faceActive=face?.isActiveAndEnabled,faceCameraActive=face?.camera?.isActiveAndEnabled,faceShapes=FaceShapes(animator.gameObject),
            visible=avatar.bodyMeshRenderer==null?(bool?)null:avatar.bodyMeshRenderer.isVisible,
            beforeOffset,afterOffset
        },json));
    }
}
[HarmonyPatch(typeof(PlayerAvatarOffsetManager),"OnLateUpdate")]
internal static class CameraOffsetDiagnosticsPatch
{
    static void Prefix(PlayerAvatarOffsetManager __instance)=>CameraTransitionDiagnostics.BeforeOffset(__instance);
    static void Postfix(PlayerAvatarOffsetManager __instance)=>CameraTransitionDiagnostics.AfterOffset(__instance);
}
