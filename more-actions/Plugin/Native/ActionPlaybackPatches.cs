using System;
using HarmonyLib;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.Scripts.InGame;
using UnityEngine;

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
        var driver = ActionDriver.Current;
        string id = driver?.Definition?.Id;
        try { driver?.Apply(); }
        catch (Exception e) { Plugin.LogSource.LogError("ACTION_OUTPUT_FAILED id=" + id + " " + e); driver?.Stop("pose/event error"); }
        finally
        {
            try { driver?.FlushBreathFollow(); }
            finally { driver?.FlushNativeFollowCamera(); }
        }
    }
}
[HarmonyPatch(typeof(FaceCameraController), "OnLateUpdate")]
static class FaceCameraOrderPatch { static bool Prefix(FaceCameraController __instance) => !(ActionDriver.Current?.DeferFaceCamera(__instance) ?? false); }

// The VMD camera retains first refusal. Do not queue an original that has
// already been suppressed by a camera owner.
static class NativeFollowCameraOrder
{
    internal static bool RunOriginal(MonoBehaviour controller, bool runOriginal)
    {
        if (!runOriginal) return false;
        try { return !(ActionDriver.Current?.DeferNativeFollowCamera(controller) ?? false); }
        catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_NATIVE_CAMERA_DEFER_FAILED " + e.Message); return true; }
    }
}

[HarmonyPatch(typeof(TPCameraController), "OnLateUpdate")]
[HarmonyAfter("local.sfm.vmdprobe")]
[HarmonyPriority(Priority.Last)]
static class ThirdPersonFollowOrderPatch
{
    static bool Prefix(TPCameraController __instance, bool __runOriginal) =>
        NativeFollowCameraOrder.RunOriginal(__instance, __runOriginal);
}

[HarmonyPatch(typeof(FPCameraController), "OnLateUpdate")]
[HarmonyAfter("local.sfm.vmdprobe")]
[HarmonyPriority(Priority.Last)]
static class FirstPersonFollowOrderPatch
{
    static bool Prefix(FPCameraController __instance, bool __runOriginal) =>
        NativeFollowCameraOrder.RunOriginal(__instance, __runOriginal);

    static void Postfix(FPCameraController __instance, bool __runOriginal)
    {
        if (!__runOriginal) return;
        var driver = ActionDriver.Current;
        try { driver?.TrackExternalFirstPerson(__instance); }
        catch (Exception e) { Plugin.LogSource?.LogWarning("EXTERNAL_ACTION_CAMERA_FAILED " + e); }
    }
}

[HarmonyPatch(typeof(BaseCameraController), "OnLateUpdate")]
[HarmonyAfter("local.sfm.vmdprobe")]
[HarmonyPriority(Priority.Last)]
static class MainCameraFollowOrderPatch
{
    static bool Prefix(BaseCameraController __instance, bool __runOriginal) =>
        NativeFollowCameraOrder.RunOriginal(__instance, __runOriginal);
}
