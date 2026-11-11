using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using SFM.Actions;
using SFM.Mmd;
using HarmonyLib;
using BepInEx.Configuration;
using SFM.ModSettings;

[BepInPlugin(Guid, "SFM MMD Actions", Version)]
[BepInDependency(SFM.Actions.Plugin.Guid)]
public sealed class Plugin : BasePlugin
{
    internal const string Version = "1.3.0";
    public const string Guid = "local.sfm.vmdprobe";
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<bool> UseCamera;
    public override void Load()
    {
        LogSource = Log;
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var directory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        var packs = Config.Bind("Actions", "Directory", ".", "VMD packs relative to this plugin. Packs use motion.vmd, optional camera.vmd and BGM.wav/BGM.mp3; optional action.json overrides individual defaults. Unity animation bundles belong in More Actions.");
        UseCamera = Config.Bind("Camera", "EnableDistribution", true, "Use the optional Camera VMD in an MMD action pack. Change in game settings.");
        ModOptions.Log = text => Log.LogWarning(text);
        ActionSettingsApi.Toggle("mmd.camera", "[MMD] 使用配布镜头", "[MMD] Use distributed camera", UseCamera, "动作包含镜头 VMD 时，使用配布镜头。关闭后使用原版游戏镜头，动作与音乐照常播放。", "Use the camera VMD supplied with the motion. Turn off to use the game's camera while motion and music continue.");
        int count = AddComponent<MmdPackPreparation>().Configure(Path.GetFullPath(Path.Combine(directory, packs.Value)));
        AddComponent<MmdRigPreparation>();
        ActionPlaybackApi.PlaybackChanged += ForwardCompatibilityEvent;
        var cameraDriver = AddComponent<MmdCameraDriver>(); cameraDriver.Bind();
        AddComponent<MmdLyricsView>().Bind();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("MMD_ACTIONS_LOADING version=" + Version + " queued=" + count + " directVmd=true cameraDistribution=true settings=true base=" + SFM.Actions.Plugin.Guid);
    }
    static void ForwardCompatibilityEvent(string id, bool active)
    {
        if (active && ActionPlaybackApi.CurrentProviderId == Guid) SFM.Motion.MotionPlaybackApi.Publish(id, true);
        else if (!active && SFM.Motion.MotionPlaybackApi.IsPlaying && SFM.Motion.MotionPlaybackApi.CurrentMotionId == id) SFM.Motion.MotionPlaybackApi.Publish(id, false);
    }
}
