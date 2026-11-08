using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using BepInEx.Configuration;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using SFM.ModSettings;
using UnityEngine;

namespace SFM.Actions;

internal static class NativeAnimationReplacements
{
    static ConfigEntry<bool> enabled;
    static ConfigEntry<string> saved;
    static readonly ReplacementSelection selection=new();
    internal static readonly ReplacementCatalog Catalog=new(selection);
    internal static long SettingsVersion=>((long)Catalog.Revision<<32)|(uint)selection.Revision;
    internal static bool Enabled=>enabled?.Value==true;
    internal static void SetEnabled(bool value){if(enabled!=null)enabled.Value=value;}
    static void Persist(bool changed){if(changed)saved.Value=selection.Serialize();}
    internal static void SelectVariant(string id,bool value)=>Persist(Catalog.Choose(id,value));
    internal static void SelectOriginal(string slot)=>Persist(Catalog.Original(slot));
    internal static void ApplyPack(string pack)=>Persist(Catalog.ApplyPack(pack)>0);
    internal static void ClearPack(string pack)=>Persist(Catalog.ClearPack(pack)>0);
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
    internal static bool EnabledForDiagnostics=>enabled?.Value==true;

    internal static void Configure(ConfigFile config)
    {
        enabled=config.Bind("Replacements","Enabled",false,"Enable selected native animation replacements. Disabling preserves selections.");
        saved=config.Bind("Replacements","Selections","{}","Original clip name to action ID; independent of game saves.");
        LoadSelection();
        saved.SettingChanged+=(_,_)=>LoadSelection();
        enabled.SettingChanged+=(_,_)=>{applied=-1;ModOptions.Refresh();};
        ModOptions.Header("replacement.header","20",()=>ActionNames.Text("元の動作の置換","Native animation replacements","原动作替换","原動作替換","기본 동작 교체"));
        ModOptions.DynamicToggle("replacement.enabled","20","",()=>ActionNames.Text("選択した置換を有効化","Enable selected replacements","启用选中的动作替换","啟用選中的動作替換","선택한 교체 활성화"),()=>enabled.Value,v=>enabled.Value=v,
            ()=>ActionNames.Text("オフにすると元の動作に戻ります。選択は保持されます。置換は待機・直立時のみ適用され、しゃがみ・着座中は立ち上がるまで保留されます。","Turn off to restore native animations without clearing selections. Replacements apply only while idle and standing; crouching or sitting defers them until the player stands up.","关闭后恢复原动作，保留各项选择。替换只在闲置站立状态应用；蹲下、坐下时会延后到站起后再应用。","關閉後恢復原動作，保留各項選擇。替換只在閒置站立狀態套用；蹲下、坐下時會延後到站起後再套用。","끄면 선택을 유지하고 원래 동작으로 돌아갑니다. 교체는 대기·직립 상태에서만 적용되며, 웅크리기·앉기 중에는 일어날 때까지 보류됩니다."));
    }
    static void LoadSelection()
    {try{selection.Load(saved.Value);}catch(Exception e){Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_CONFIG_REJECTED "+e.Message);}applied=-1;ModOptions.Refresh();}
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
            if(enabled==null||Time.unscaledTime<faultUntil)return;
            if(current==null){if(animator!=null)Release();return;}
            if(!current.enabled||!current.isInitialized)return;
            if(current!=animator){Release();animator=current;catalog=-1;applied=-1;}
            if(owned==null)Bind(current.runtimeAnimatorController);
            if(owned==null)return;
            if(catalog!=ActionRegistry.Revision||ready!=ActionAssets.ReadyRevision){BuildSettings();catalog=ActionRegistry.Revision;ready=ActionAssets.ReadyRevision;applied=-1;}
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
            if(applied==selection.Revision&&appliedEnabled==enabled.Value)return;
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
            bool diagnose=enabled.Value||appliedEnabled||Same(actual,owned);
            if(diagnose)CameraTransitionDiagnostics.ReplacementChange(current,"before-replacements");
            using(var refresh=enabled.Value||Same(actual,owned)?new NativeAnimatorRefresh(current):null)
            {
                foreach(var pair in slots)
                {
                    AnimationClip replacement=null;
                    if(enabled.Value)
                    {
                        string id=selection.Get(pair.Key);
                        replacement=ReplacementClip(id,pair.Key);
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
            applied=selection.Revision;appliedEnabled=enabled.Value;
            Plugin.LogSource.LogInfo("ACTION_REPLACEMENTS_APPLIED enabled="+enabled.Value+" clips="+count);
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
    static void BuildSettings()
    {
        ModOptions.Remove("replacement.variant.");ModOptions.Remove("replacement.group.");
        var candidates=new List<(ActionDefinition Definition,string Slot,string Group)>();
        foreach(var definition in ActionRegistry.GetAll())
        {
            var name=ReplacementName(definition);
            if(name==null)continue;
            if(slots.ContainsKey(name)){candidates.Add((definition,name,SourceGroup(definition)));continue;}
            // A case-only variant usually means the pack renamed the clip; the
            // slot name and the bundle file name are unrelated.
            var near=slots.Keys.FirstOrDefault(k=>string.Equals(k,name,StringComparison.OrdinalIgnoreCase));
            if(near!=null)Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_UNMATCHED id="+definition.Id+" clip="+name+" expected="+near);
        }
        candidates.Sort((a,b)=>string.Compare(a.Group,b.Group,StringComparison.Ordinal)!=0?string.Compare(a.Group,b.Group,StringComparison.Ordinal):string.Compare(a.Slot,b.Slot,StringComparison.Ordinal));
        foreach(var candidate in candidates)
            if(GameAnimationNames.IsOriginal(candidate.Slot))ActionRegistry.SetRingVisibility(candidate.Definition.Id,false);
        Catalog.Update(candidates.Select(c=>new ReplacementOption(c.Definition.Id,c.Slot,c.Group,SourceLabel(c.Definition,c.Slot))));
        ModOptions.Refresh();
    }

    static string SourceGroup(ActionDefinition definition)
    {
        if(definition.RuntimeSource is AnimatorActionSource imported)return string.IsNullOrEmpty(imported.Group)?"Actions":imported.Group;
        var directory=Path.GetDirectoryName(definition.BundlePath??"");
        return string.IsNullOrEmpty(directory)?"Actions":Path.GetFileName(directory);
    }
    static string SourceLabel(ActionDefinition definition,string slot)
    {
        if(definition.RuntimeSource is AnimatorActionSource imported)
        {
            var path=imported.RelativeBundle.Replace('\\','/');
            int slash=path.IndexOf('/');
            return slash<0?path:path.Substring(slash+1);
        }
        return string.IsNullOrEmpty(definition.Bundle)?definition.DisplayName:definition.Bundle;
    }
    static string ReplacementName(ActionDefinition definition)
    {
        if(definition.RuntimeSource is AnimatorActionSource imported)return imported.ClipName;
        try{if(ActionAssets.TryGet(definition,out var loaded)&&loaded?.Clip!=null)return loaded.Clip.name;}catch{}
        var asset=Path.GetFileNameWithoutExtension(definition.ClipAsset);
        return string.IsNullOrEmpty(asset)?null:asset;
    }
    static AnimationClip ReplacementClip(string id,string slot)
    {
        var definition=id==null?null:ActionRegistry.Get(id);
        if(definition==null)return null;
        if(definition.RuntimeSource is AnimatorActionSource imported)
            return string.Equals(imported.ClipName,slot,StringComparison.Ordinal)?imported.Clip:null;
        try
        {
            if(!ActionAssets.TryGet(definition,out var loaded)||loaded?.Clip==null)return null;
            return string.Equals(loaded.Clip.name,slot,StringComparison.Ordinal)?loaded.Clip:null;
        }
        catch(Exception e){Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_ASSET "+definition.Id+" "+e.Message);return null;}
    }    static AnimationClip BaselineClip(AnimationClip original)
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
