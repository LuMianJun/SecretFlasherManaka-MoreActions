using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;

namespace SFM.Actions;

internal static class NativeAnimationReplacements
{
    static readonly Dictionary<string,AnimationClip> slots=new(StringComparer.Ordinal);
    static readonly Dictionary<IntPtr,AnimationClip> cameraSource=new();
    static readonly Dictionary<string,AnimationClip> cameraSourceNames=new(StringComparer.Ordinal);
    static readonly HashSet<string> originalNames=new(StringComparer.Ordinal);
    static readonly HashSet<string> controlledSlots=new(StringComparer.Ordinal);
    static Animator animator;
    static RuntimeAnimatorController baseline;
    static AnimatorOverrideController owned;
    static int applied=-1,catalog=-1,ready=-1;
    static bool appliedEnabled;
    static float nextCheck,faultUntil;
    internal static bool EnabledForDiagnostics=>ReplacementService.Enabled;

    static bool Same(UnityEngine.Object a,UnityEngine.Object b)=>a==b;
    static bool Contains(RuntimeAnimatorController controller,AnimatorOverrideController target)
    {
        var seen=new HashSet<IntPtr>();
        while(controller!=null&&seen.Add(controller.Pointer))
        {if(Same(controller,target))return true;var wrapper=controller.TryCast<AnimatorOverrideController>();if(wrapper==null)return false;controller=wrapper.runtimeAnimatorController;}
        return false;
    }
    internal static void Tick(Animator current)
    {
        try
        {
            if(!ReplacementService.IsConfigured||Time.unscaledTime<faultUntil)return;
            if(current==null){if(animator!=null)Release();return;}
            if(!current.enabled||!current.isInitialized)return;
            if(current!=animator){Release();animator=current;catalog=-1;applied=-1;}
            if(owned==null)Bind(current.runtimeAnimatorController);
            if(owned==null)return;
            if(catalog!=ActionRegistry.Revision||ready!=ActionAssets.ReadyRevision){ReplacementService.RefreshCatalog(slots.Keys);catalog=ActionRegistry.Revision;ready=ActionAssets.ReadyRevision;applied=-1;}
            if(Time.unscaledTime>=nextCheck)
            {
                nextCheck=Time.unscaledTime+.5f;
                var controller=current.runtimeAnimatorController;
                if(!Same(controller,owned)&&!Same(controller,baseline))
                {
                    // Another owner may have wrapped us. Never reclaim their
                    // controller or destroy a node still referenced by it.
                    if(Contains(controller,owned))return;
                    if(ActionPlaybackApi.IsPlaying||InGameUiManager.Instance?.IsOpenSomeUi==true)return;
                    Release();animator=current;Bind(controller);catalog=-1;return;
                }
            }
            if(applied==ReplacementService.SelectionRevision&&appliedEnabled==ReplacementService.Enabled)return;
            if(ActionPlaybackApi.IsPlaying||InGameUiManager.Instance?.IsOpenSomeUi==true)return;
            var actual=current.runtimeAnimatorController;
            if(!Same(actual,owned)&&!Same(actual,baseline))return;
            // Controller assignment captures defaults. Wait for the same native
            // idle boundary used by independent actions, including chair and
            // pose exit animations whose IsCrouch flag is already false.
            var state=PlayerController.Instance?.Pca?.PlayerState;
            if(state==null||state.IsCrouch||state.IsSit||state.IsEcstasyMotion
                ||(int)state.CurrentState!=0||state.CurrentAction==null||(int)state.CurrentAction.Type!=-1)return;
            int actionLayer=current.GetLayerIndex("Action");
            if(actionLayer<0||!current.GetCurrentAnimatorStateInfo(actionLayer).IsName("Action.NoMotion"))return;
            for(int i=0;i<current.layerCount;i++)if(current.IsInTransition(i))return;
            int count=0;
            bool diagnose=ReplacementService.Enabled||appliedEnabled||Same(actual,owned);
            if(diagnose)CameraTransitionDiagnostics.ReplacementChange(current,"before-replacements");
            using(var refresh=ReplacementService.Enabled||Same(actual,owned)?new NativeAnimatorRefresh(current):null)
            {
                foreach(var pair in slots)
                {
                    AnimationClip replacement=null;
                    if(ReplacementService.Enabled)
                    {
                        string id=ReplacementService.Selected(pair.Key);
                        replacement=ReplacementService.ResolveClip(id,pair.Key);
                    }
                    if(replacement!=null)controlledSlots.Add(pair.Key);
                    var effective=replacement??(controlledSlots.Contains(pair.Key)?BaselineClip(pair.Value):null);
                    owned.SetClip(pair.Value,effective,false);
                    if(replacement!=null){count++;RegisterCamera(pair.Value,replacement);}
                    else UnregisterCamera(pair.Value);
                }
                owned.SendNotification();
                // Once attached, retain this binding owner even when every
                // override is disabled. Returning the original clips does not
                // require detaching the native animation/default-value graph.
                var target=count>0||Same(actual,owned)?owned:baseline;
                if(refresh!=null&&(Same(actual,owned)||!Same(current.runtimeAnimatorController,target)))refresh.Evaluate(target);
            }
            if(diagnose)CameraTransitionDiagnostics.ReplacementChange(current,"after-replacements");
            applied=ReplacementService.SelectionRevision;appliedEnabled=ReplacementService.Enabled;
            Plugin.LogSource.LogInfo("ACTION_REPLACEMENTS_APPLIED enabled="+ReplacementService.Enabled+" clips="+count);
        }
        catch(Exception e)
        {
            Plugin.LogSource.LogError("ACTION_REPLACEMENTS_FAILED "+e);
            try{Release();}catch(Exception cleanup){Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_RELEASE_FAILED "+cleanup.Message);}
            faultUntil=Time.unscaledTime+5;
        }
    }
    // Il2CppInterop hands out a fresh wrapper per native object, so identity
    // through Dictionary<AnimationClip,...> is not reliable; key by pointer and
    // fall back to the authored clip name.
    static void RegisterCamera(AnimationClip original,AnimationClip replacement)
    {
        UnregisterCamera(original);
        cameraSource[replacement.Pointer]=original;
        cameraSourceNames[replacement.name]=original;
    }
    static void UnregisterCamera(AnimationClip original)
    {
        IntPtr stale=IntPtr.Zero;string staleName=null;
        foreach(var pair in cameraSource)
            if(Same(pair.Value,original)){stale=pair.Key;break;}
        if(stale!=IntPtr.Zero)cameraSource.Remove(stale);
        foreach(var pair in cameraSourceNames)
            if(Same(pair.Value,original)){staleName=pair.Key;break;}
        if(staleName!=null)cameraSourceNames.Remove(staleName);
    }
    // The replacement clip carries skeleton bones only. NativeCameraTrack asks
    // which original clip a playing replacement came from so the authored camera
    // markers can keep running while the slot is replaced.
    internal static bool IsBoundTo(Animator animator)
    {
        if(animator==null||owned==null)return false;
        var controller=animator.runtimeAnimatorController;
        return controller!=null&&(Same(controller,owned)||Contains(controller,owned));
    }
    internal static int CameraSourceCount=>cameraSource.Count;
    internal static bool TryGetCameraSource(AnimationClip replacement,out AnimationClip original)
    {
        original=null;
        if(replacement==null)return false;
        if(cameraSource.TryGetValue(replacement.Pointer,out original))return true;
        return replacement.name!=null&&cameraSourceNames.TryGetValue(replacement.name,out original);
    }
    // IL2CPP interop exposes only the pointer constructor; the managed
    // (RuntimeAnimatorController) constructor is a native Internal_Create call.
    static AnimatorOverrideController NewOverrideController(RuntimeAnimatorController controller)
    {
        try
        {
            RuntimeHelpers.RunClassConstructor(typeof(AnimatorOverrideController).TypeHandle);
            var klass=Il2CppInterop.Runtime.Il2CppClassPointerStore<AnimatorOverrideController>.NativeClassPtr;
            if(klass==IntPtr.Zero){Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_NO_CLASS");return null;}
            var pointer=Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(klass);
            var created=new AnimatorOverrideController(pointer);
            AnimatorOverrideController.Internal_Create(created,controller);
            return created;
        }
        catch(Exception e){Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_CREATE_FAILED "+e.Message);return null;}
    }
    // Enumerate originals from the unwrapped controller: an override layer
    // reports effective clips, whose names belong to replacements.
    static RuntimeAnimatorController SlotsSource(RuntimeAnimatorController controller)
    {
        var wrapper=controller==null?null:controller.TryCast<AnimatorOverrideController>();
        return wrapper!=null&&wrapper.runtimeAnimatorController!=null?wrapper.runtimeAnimatorController:controller;
    }
    static void Bind(RuntimeAnimatorController controller)
    {
        if(controller==null)return;
        baseline=controller;
        // Slots come from the live controller: names are stable inside a game
        // version and no pack needs to ship references to native assets.
        owned=NewOverrideController(controller);
        if(owned==null){baseline=null;return;}
        owned.hideFlags=HideFlags.HideAndDontSave;owned.runtimeAnimatorController=baseline;
        slots.Clear();originalNames.Clear();controlledSlots.Clear();var ambiguous=new HashSet<string>();
        cameraSource.Clear();cameraSourceNames.Clear();NativeCameraTrack.Release();
        var nativeClips=SlotsSource(controller).animationClips;
        for(int i=0;i<nativeClips.Length;i++)
        {
            var clip=nativeClips[i];if(clip==null)continue;
            originalNames.Add(clip.name);
            if(slots.TryGetValue(clip.name,out var previous)&&!Same(previous,clip)){slots.Remove(clip.name);ambiguous.Add(clip.name);}
            else if(!ambiguous.Contains(clip.name))slots[clip.name]=clip;
        }
        catalog=-1;applied=-1;
        Plugin.LogSource.LogInfo("ACTION_REPLACEMENTS_BOUND originals="+slots.Count+" ambiguous="+ambiguous.Count+" unmappedNames="+slots.Keys.Except(GameAnimationNames.Names,StringComparer.Ordinal).Count());
    }
    static AnimationClip BaselineClip(AnimationClip original)
    {
        var inherited=baseline==null?null:baseline.TryCast<AnimatorOverrideController>();
        return inherited==null?original:inherited.GetClip(original,true)??original;
    }
    internal static void Release()
    {
        if(owned!=null)
        {
            bool live=animator!=null&&Contains(animator.runtimeAnimatorController,owned);
            bool detached=!live;
            using(var refresh=live?new NativeAnimatorRefresh(animator):null)
            {
                foreach(var original in slots.Values)
                    if(original!=null)owned.SetClip(original,controlledSlots.Contains(original.name)?BaselineClip(original):null,false);
                owned.SendNotification();
                if(live)
                {
                    refresh.Evaluate(animator.runtimeAnimatorController);
                }
            }
            if(detached)UnityEngine.Object.Destroy(owned);
            else owned.hideFlags&=~HideFlags.DontUnloadUnusedAsset;
        }
        owned=null;baseline=null;animator=null;slots.Clear();originalNames.Clear();controlledSlots.Clear();cameraSource.Clear();cameraSourceNames.Clear();NativeCameraTrack.Release();applied=-1;catalog=-1;ready=-1;
    }
}
