using System;
using HarmonyLib;
using UnityEngine;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Master.Mission;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame.MissionArea;
using ExposureUnnoticed2.Scripts.Mission;

namespace SFM.Actions;

internal static class NativeActionStats
{
    internal static ActionStatValues Resolve(ActionDefinition action) => ActionStatsSchema.Resolve(action, name =>
    {
        var record=MAction.Get((ActionType)Enum.Parse(typeof(ActionType),name));
        if(record==null)throw new InvalidOperationException("Native stat preset unavailable: "+name);
        return new(record.AddEcstasy,record.AddDokidoki100*0.01f,0,0,record.Strange,record.StrangenessScale,record.Discomfort,false);
    });

    internal static bool Active(PlayerClassAccessor accessor,out ActionStatValues stats)
    {
        stats=default;
        return accessor!=null&&ActionDriver.Current?.TryGetStats(accessor.PlayerState,out stats)==true;
    }

    // The game already combines an additive transient input with its action
    // rate, then applies tension/reinforcement and handles decay/thresholds.
    // Its existing accumulator uses units converted by 0.15 inside OnUpdate.
    // Preserve existing equipment input and remove ours if the native call
    // returned early or threw before consuming it. Never call OnUpdate twice.
    internal sealed class EcstasyInput
    {
        readonly PlayerStateModel state;
        readonly float before, injected;
        internal EcstasyInput(PlayerStateModel state,float addition)
        {
            this.state=state;before=state.AddEcstasyByPiston;
            injected=before+addition;state.AddEcstasyByPiston=injected;
        }
        internal void Release()
        {
            if(state.AddEcstasyByPiston==injected)state.AddEcstasyByPiston=before;
        }
    }

    internal static EcstasyInput BeforeEcstasy(PlayerEcstasyController controller)
    {
        if(!Active(controller.pca,out var stats)||stats.EcstasyPerSecond<=0
            ||!ActionDriver.Current.TakeStatFrame(ActionStatChannel.Ecstasy))return null;
        float rate=stats.EcstasyPerSecond*(1+GameState.GameStateData.MotionSpeedScaleIndex*0.1f);
        if(rate<=0)return null;
        return new EcstasyInput(controller.pca.PlayerState,rate/0.15f);
    }

    internal static void AfterDokidoki(PlayerMiscProcess process)
    {
        if(!Active(process.pca,out var stats)||stats.DokidokiAdd<=0)return;
        var state=process.playerState;
        state.Dokidoki=Mathf.Clamp01(state.Dokidoki+stats.DokidokiAdd*state.CurrentRank.DokidokiBoostScale);
    }

    internal static void BeforeMoisture(PlayerMoistureManager manager)
    {
        if(!Active(manager.pca,out var stats)||stats.MoisturePerSecond<=0||GameState.OptionData.PeeDisable
            ||!ActionDriver.Current.TakeStatFrame(ActionStatChannel.Moisture))return;
        PlayerFacade.Instance.AddAddMoisture(stats.MoisturePerSecond*Time.deltaTime);
    }

    internal static void AfterStamina(StaminaManager stamina)
    {
        var driver=ActionDriver.Current;
        var accessor=driver?.Owner?.Pca;
        if(accessor?.Stamina?.Pointer!=stamina.Pointer||!Active(accessor,out var stats)||stats.StaminaPerSecond<=0
            ||!driver.TakeStatFrame(ActionStatChannel.Stamina))return;
        // ConsumePerSecond owns deltaTime, cooldown and the native empty flash.
        stamina.ConsumePerSecond(stats.StaminaPerSecond);
        if(stamina.Current<=0)driver.StopWithDiagnostics("action stats","stamina depleted");
    }

    internal static void BeforePee(PlayerPeeController controller)
    {
        if(!Active(controller.pca,out var stats)||!stats.CanStartPee||GameState.OptionData.PeeDisable
            ||controller.pca.PlayerState.IsPeeing||GameState.GameStateData.PlayerMoisture<=0)return;
        controller.StartPee();
    }

    internal static void AfterExpose(PlayerStrangenessController controller,ref float value)
    {
        if(!Active(controller.pca,out var stats))return;
        // Called before native lighting/sight scaling and invisibility checks.
        value=value*stats.StrangenessScale+stats.Strange;
    }

    internal static void AfterDiscomfort(PlayerStrangenessController controller,ref float value)
    {
        if(!Active(controller.pca,out var stats))return;
        // Original directional caches combine contributions using maximum.
        // Apply at the read, leaving no inflated cache after playback stops.
        value=Math.Max(value,stats.Discomfort);
    }

    internal static void AfterPoseCondition(ref bool result)
    {
        var driver=ActionDriver.Current;
        if(result||!Active(driver?.Owner?.Pca,out _)||!driver.Definition.PoseMission)return;
        if(PlayerFacade.Instance.GetExposedBodyPartsByClothesAndCostume()==ExposedBodyParts.None)return;
        var areas=NpcRouteAreaManager.Instance;
        result=areas==null||areas.IsInArea;
    }
}

[HarmonyPatch(typeof(PlayerEcstasyController),"OnUpdate")]
static class ActionEcstasyStatsPatch
{
    static void Prefix(PlayerEcstasyController __instance,out NativeActionStats.EcstasyInput __state)
        =>__state=NativeActionStats.BeforeEcstasy(__instance);
    static Exception Finalizer(Exception __exception,NativeActionStats.EcstasyInput __state)
    {__state?.Release();return __exception;}
}
[HarmonyPatch(typeof(PlayerMiscProcess),"CalcDokidoki")]
static class ActionDokidokiStatsPatch
{static void Postfix(PlayerMiscProcess __instance)=>NativeActionStats.AfterDokidoki(__instance);}
[HarmonyPatch(typeof(PlayerMoistureManager),"OnUpdate")]
static class ActionMoistureStatsPatch
{static void Prefix(PlayerMoistureManager __instance)=>NativeActionStats.BeforeMoisture(__instance);}
[HarmonyPatch(typeof(StaminaManager),"OnUpdate")]
static class ActionStaminaStatsPatch
{static void Postfix(StaminaManager __instance)=>NativeActionStats.AfterStamina(__instance);}
[HarmonyPatch(typeof(PlayerPeeController),"OnUpdate")]
static class ActionPeeStatsPatch
{static void Prefix(PlayerPeeController __instance)=>NativeActionStats.BeforePee(__instance);}
[HarmonyPatch(typeof(PlayerStrangenessController),"CalcExposeStrange")]
static class ActionExposeStatsPatch
{static void Postfix(PlayerStrangenessController __instance,ref float __result)=>NativeActionStats.AfterExpose(__instance,ref __result);}
[HarmonyPatch(typeof(PlayerStrangenessController),"CalcMaxDiscomfort")]
static class ActionDiscomfortStatsPatch
{static void Postfix(PlayerStrangenessController __instance,ref float __result)=>NativeActionStats.AfterDiscomfort(__instance,ref __result);}
[HarmonyPatch(typeof(MissionPoseExpose),"IsInCondition")]
static class ActionPoseMissionPatch
{static void Postfix(ref bool __result)=>NativeActionStats.AfterPoseCondition(ref __result);}
