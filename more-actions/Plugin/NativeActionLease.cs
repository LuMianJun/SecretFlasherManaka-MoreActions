using HarmonyLib;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.ObjectUI.InGame.MessagePanel;
using ExposureUnnoticed2.Scripts.Base;

namespace Seleka2.Actions;

// Managed occupation augments existing queries without inventing an MAction
// or changing the original Animator's ActionId/state-machine nodes.
[HarmonyPatch(typeof(PlayerFacade), "IsPlayingAction")]
static class FacadeActionQueryPatch { static void Postfix(PlayerFacade __instance, ref bool __result) => __result |= ActionDriver.Current?.Owns(__instance) ?? false; }
[HarmonyPatch(typeof(PlayerStateModel), "get_IsPlayingAction")]
static class StateActionQueryPatch { static void Postfix(PlayerStateModel __instance, ref bool __result) => __result |= ActionDriver.Current?.Owns(__instance) ?? false; }
[HarmonyPatch(typeof(PlayerStateModel), "get_IsPlayingSomeNonMoveAction")]
static class StateNonMoveQueryPatch { static void Postfix(PlayerStateModel __instance, ref bool __result) => __result |= ActionDriver.Current?.Owns(__instance) ?? false; }
[HarmonyPatch(typeof(PlayerFacade), "CanUseHand")]
static class ActionHandsPatch
{
    static bool Prefix(PlayerFacade __instance, bool __0, ref bool __result)
    {
        var driver = ActionDriver.Current;
        if (driver == null || driver.CheckingAvailability || !driver.Owns(__instance)) return true;
        if (__0) MessagePanelView.AddMessage(Localizer.Trans("log_cant_use_hand_by_other"), MessagePanelView.MessageType.Caution);
        __result = false; return false;
    }
}
[HarmonyPatch(typeof(PlayerAction), "ChangeAction")]
static class NativeActionEntryPatch
{
    static void Prefix(PlayerAction __instance, ActionType __0)
    {
        if (ActionDriver.Current?.Owns(__instance) == true) ActionDriver.Current.StopWithDiagnostics("native action entry", "native action " + __0);
    }
}
[HarmonyPatch(typeof(PlayerController), "ChangeState")]
static class NativeStateEntryPatch
{
    static void Prefix(PlayerController __instance, PlayerStateModel.PlayerState __0)
    {
        if ((int)__0 != 0 && ActionDriver.Current?.Owner == __instance) ActionDriver.Current.StopWithDiagnostics("native state entry", "native state " + __0);
    }
}
