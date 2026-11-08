using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SFM.ModSettings;

namespace SFM.Actions;

[BepInPlugin(Guid, "SFM More Actions", Version)]
public sealed class Plugin : BasePlugin
{
    internal const string Version = "1.5.0";
    public const string Guid = "local.sfm.moreactions";
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<float> Volume;
    internal static ConfigEntry<bool> FollowCamera;
    internal static ConfigEntry<bool> KeepGameExpressions;
    public override void Load()
    {
        LogSource = Log;
        var packs = Config.Bind("Actions", "Directory", "Actions", "Action pack directory relative to this plugin.");
        Volume = Config.Bind("Audio", "Volume", 1f, "Master custom-action music volume, 0-1, multiplied by pack volume and native sound settings.");
        FollowCamera = Config.Bind("Camera", "FollowFaceCamera", true, "Track the original face camera after the custom pose; preserves native visibility.");
        KeepGameExpressions = Config.Bind("Face", "KeepGameExpressions", true, "Overlay action expressions on the native game expressions. An action's keepGameExpressions=false always disables this.");
        ActionFaceSettings.KeepGameExpressions = KeepGameExpressions.Value;
        KeepGameExpressions.SettingChanged += (_, _) => ActionFaceSettings.KeepGameExpressions = KeepGameExpressions.Value;
        ModOptions.Log = text => Log.LogWarning(text);
        ActionSettingsApi.Initialize();
        CameraTransitionDiagnostics.Configure(Config);
        ReplacementService.Configure(Config);
        ReplacementSettings.Register();
        ActionSettingsApi.Toggle("actions.gameexpressions", "[更多动作] 叠加原游戏表情", "[Actions] Keep game expressions", KeepGameExpressions, "默认在原游戏表情上叠加动作表情。关闭后只显示动作表情。动作 JSON 的 keepGameExpressions=false 优先，开启此项也不能覆盖。", "Overlay action expressions on the game's expressions. When off, show only action expressions. An action's keepGameExpressions=false takes priority over this setting.");
        ActionSettingsApi.Toggle("actions.facecamera", "[更多动作] 面部镜头跟随", "[Actions] Face camera tracking", FollowCamera, "自定义动作播放时，让原版面部摄像机跟随角色头部。面部摄像机显示仍由原版设置控制。", "Track the character's head during custom actions. Camera visibility follows the original settings.");
        ActionSettingsApi.Number("actions.volume", "[更多动作] 动作音乐音量", "[Actions] Music volume", Volume, 0, 100, 0.01f, "", "0 为静音，1 为完整音量；同时受动作配置与游戏主音量、背景音乐音量影响。", "0 is silent; 1 is full volume. Pack volume and native master/BGM volume also apply.");
        RingActionMenu.LastActions = Config.Bind("Shortcuts", "LastActions", "", "Custom action IDs for the eight ring shortcuts, separated by commas; stored outside the game save.");
        NativeActionEvents.Register();
        ActionDriver.Current = AddComponent<ActionDriver>();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        var pluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
        int count = ActionPackLoader.Load(Path.GetFullPath(Path.Combine(pluginDirectory, packs.Value)), Guid);
        ActionBundleImport.Begin(Path.GetFullPath(Path.Combine(pluginDirectory, packs.Value)), Guid + ".bundles");

        Log.LogInfo("MORE_ACTIONS_READY version=" + Version + " packs=" + count + " ring=true nativeRestrictions=true runtimeSources=true settings=true");
    }
}
