using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Seleka2.Actions;
using Seleka2.Mmd;
using HarmonyLib;
using BepInEx.Configuration;
using Seleka2.ModSettings;

[BepInPlugin(Guid, "Seleka2 MMD Actions", "0.9.9")]
[BepInDependency(Seleka2.Actions.Plugin.Guid, "0.5.3")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "local.seleka2.vmdprobe";
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<bool> UseCamera;
    public override void Load()
    {
        LogSource = Log;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var directory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        var packs = Config.Bind("Actions", "Directory", ".", "VMD packs relative to this plugin. Packs use motion.vmd, optional camera.vmd, BGM.wav and source.pmd; optional action.json overrides individual defaults. Unity animation bundles belong in More Actions.");
        UseCamera = Config.Bind("Camera", "EnableDistribution", true, "Use the optional Camera VMD in an MMD action pack. Change in game settings.");
        ModOptions.Log = text => Log.LogWarning(text);
        ModOptions.Toggle("mmd.camera", "[MMD] 使用配布镜头", "[MMD] Use distributed camera", UseCamera, "动作包含镜头 VMD 时，使用配布镜头。关闭后使用原版游戏镜头，动作与音乐照常播放。", "Use the camera VMD supplied with the motion. Turn off to use the game's camera while motion and music continue.");
        int count = AddComponent<MmdPackPreparation>().Configure(Path.GetFullPath(Path.Combine(directory, packs.Value)));
        AddComponent<MmdRigPreparation>();
        ActionPlaybackApi.PlaybackChanged += ForwardCompatibilityEvent;
        var cameraDriver = AddComponent<MmdCameraDriver>(); cameraDriver.Bind();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("MMD_ACTIONS_LOADING version=0.9.9 queued=" + count + " directVmd=true cameraDistribution=true settings=true base=" + Seleka2.Actions.Plugin.Guid);
    }
    static void ForwardCompatibilityEvent(string id, bool active)
    {
        if (active && ActionPlaybackApi.CurrentProviderId == Guid) Seleka2.Motion.MotionPlaybackApi.Publish(id, true);
        else if (!active && Seleka2.Motion.MotionPlaybackApi.IsPlaying && Seleka2.Motion.MotionPlaybackApi.CurrentMotionId == id) Seleka2.Motion.MotionPlaybackApi.Publish(id, false);
    }
}
