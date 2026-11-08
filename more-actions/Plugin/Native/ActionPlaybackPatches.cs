using System;
using HarmonyLib;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.Scripts.InGame;

namespace SFM.Actions;

[HarmonyPatch(typeof(PlayerController), "OnUpdate")]
static class ActionTickPatch
{
    static void Prefix(PlayerController __instance) { try { ActionDriver.Current?.BeforeNativeUpdate(__instance); } catch (Exception e) { Plugin.LogSource.LogError(e); ActionDriver.Current?.Stop("pre-update error"); } }
    static void Postfix(PlayerController __instance) { try { ActionDriver.Current?.Tick(__instance); } catch (Exception e) { Plugin.LogSource.LogError(e); ActionDriver.Current?.Stop("update error"); } }
}
[HarmonyPatch(typeof(EventMethodManager), "LateUpdate")]
static class ActionOutputPatch
{
    static void Postfix()
    {
        string id = ActionDriver.Current?.Definition?.Id;
        try { ActionDriver.Current?.Apply(); }
        catch (Exception e) { Plugin.LogSource.LogError("ACTION_OUTPUT_FAILED id=" + id + " " + e); ActionDriver.Current?.Stop("pose/event error"); }
        finally { CameraTransitionDiagnostics.EndFrame(); }
    }
}
[HarmonyPatch(typeof(FaceCameraController), "OnLateUpdate")]
static class FaceCameraOrderPatch { static bool Prefix(FaceCameraController __instance) => !(ActionDriver.Current?.DeferFaceCamera(__instance) ?? false); }
