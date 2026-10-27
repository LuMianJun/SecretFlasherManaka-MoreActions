using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ExposureUnnoticed2.Object3D.Player.Scripts;

namespace Seleka2.Actions;

// Bracket only the active player's native late pass. NPC LookAt updates occur
// outside this scope and retain their original behavior.
[HarmonyPatch(typeof(PlayerController), "OnLateUpdate")]
static class NativeBodyPassPatch
{
    static void Prefix(PlayerController __instance, out IActionBodyPose __state)
        => __state=ActionDriver.Current?.BeginNativeBody(__instance);
    static Exception Finalizer(Exception __exception, IActionBodyPose __state)
    { __state?.EndNativeBody(__exception==null); return __exception; }
}

// Keep native bookkeeping, size/shoe offsets and side effects. Revert just the
// transform writes which conflict with the currently owned MMD body pose.
[HarmonyPatch(typeof(PlayerKneeAdjuster), "OnLateUpdate")]
static class NativeKneePosePatch
{
    static void Prefix(out IDisposable __state) => __state=ActionDriver.Current?.PreserveNativeCorrection(ActionBoneCorrection.Knee);
    static Exception Finalizer(Exception __exception, IDisposable __state) { __state?.Dispose(); return __exception; }
}
[HarmonyPatch(typeof(PlayerToeHeelController), "OnLateUpdate")]
static class NativeToePosePatch
{
    static void Prefix(out IDisposable __state) => __state=ActionDriver.Current?.PreserveNativeCorrection(ActionBoneCorrection.Toe);
    static Exception Finalizer(Exception __exception, IDisposable __state) { __state?.Dispose(); return __exception; }
}
[HarmonyPatch(typeof(LookAtController), "OnLateUpdate")]
static class NativeHeadPosePatch
{
    static void Prefix(out IDisposable __state) => __state=ActionDriver.Current?.PreserveNativeCorrection(ActionBoneCorrection.LookAt);
    static Exception Finalizer(Exception __exception, IDisposable __state) { __state?.Dispose(); return __exception; }
}
[HarmonyPatch(typeof(PlayerKneeAdjuster), "get_KneeOffsetY")]
static class NativeKneeOffsetPatch
{
    static bool Prefix(ref float __result)
    {
        if(!(ActionDriver.Current?.SuppressKneeOffset??false))return true;
        __result=0;return false;
    }
}
