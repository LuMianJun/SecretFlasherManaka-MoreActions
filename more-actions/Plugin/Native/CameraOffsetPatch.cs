using ExposureUnnoticed2.Object3D.Player.Scripts;
using HarmonyLib;

namespace SFM.Actions;

[HarmonyPatch(typeof(PlayerAvatarOffsetManager), "OnLateUpdate")]
internal static class CameraOffsetPatch
{
    static void Prefix(PlayerAvatarOffsetManager __instance, out NativeAnimatorRefresh.CameraHeight __state)
        => __state = NativeAnimatorRefresh.BeforeCameraOffset(__instance);

    static void Postfix(NativeAnimatorRefresh.CameraHeight __state)
        => NativeAnimatorRefresh.AfterCameraOffset(__state);
}
