using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Seleka2.ModSettings;

namespace Seleka2.NpcSpectators;

[BepInPlugin("local.seleka2.npcspectators", "Seleka2 NPC Spectators", "0.3.1")]
[BepInDependency("local.seleka2.moreactions", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal static ConfigEntry<bool> Enabled, FollowMmd, FollowNative, FollowAllMmd;
    internal static ConfigEntry<string> NativeActions, MmdActions;
    internal static ConfigEntry<float> Radius, Spacing;
    public override void Load()
    {
        Logger = Log;
        Enabled = Config.Bind("General", "Enabled", true, "Enable spectator behavior.");
        FollowNative = Config.Bind("Triggers", "FollowNativeActions", true, "Gather for original actions listed in NativeActions.");
        NativeActions = Config.Bind("Triggers", "NativeActions", "HipShake", "Comma-separated original ActionType names or numeric IDs. Empty disables native action triggers.");
        FollowMmd = Config.Bind("Integration", "FollowMmd", true, "Listen to the optional MMD playback API. The MMD plugin is not required.");
        FollowAllMmd = Config.Bind("Integration", "FollowAllMmd", false, "Gather for every MMD action instead of only IDs in MmdActions.");
        MmdActions = Config.Bind("Integration", "MmdActions", "wavefile", "Comma-separated MMD motion IDs that gather spectators.");
        Radius = Config.Bind("Gathering", "Radius", 2.8f, "Inner ring radius in meters; clamped to 1.8-8.");
        Spacing = Config.Bind("Gathering", "Spacing", 1.1f, "Minimum separation between spectator destinations; clamped to 0.8-3.");
        ActionEventBridge.Refresh();
        CrowdDriver.Current = AddComponent<CrowdDriver>();
        ModOptions.Log = text => Log.LogWarning(text);
        ModOptions.Toggle("crowd.enabled", "[NPC围观] 启用", "[Spectators] Enable", Enabled, "允许符合条件的普通行人靠近围观。关闭后释放当前围观行人。", "Allow eligible ordinary NPCs to gather. Disabling releases the current spectators.");
        ModOptions.Toggle("crowd.manual", "[NPC围观] 手动召集", "[Spectators] Manual gathering", () => CrowdDriver.Current?.ManualRequested ?? false, value => { if (CrowdDriver.Current != null) CrowdDriver.Current.ManualRequested = value; }, "关闭设置后召集当前场景中的普通行人。移动或取消会结束手动召集；此开关只影响手动来源。", "Gather eligible NPCs after closing settings. Move or cancel to end manual gathering. Automatic triggers remain independent.");
        ModOptions.Toggle("crowd.native", "[NPC围观] 原版动作触发", "[Spectators] Native actions", FollowNative, "执行配置的原版动作时自动围观，默认原版摇臀动作。", "Gather during configured original actions; defaults to HipShake.");
        ModOptions.Toggle("crowd.mmd", "[NPC围观] MMD动作触发", "[Spectators] MMD actions", FollowMmd, "MMD模组播放指定动作时自动围观。未安装MMD时仍可使用其他围观方式。", "Gather during configured MMD actions. Other triggers work without the MMD plugin.");
        ModOptions.Toggle("crowd.allmmd", "[NPC围观] 所有MMD动作", "[Spectators] All MMD actions", FollowAllMmd, "启用后所有MMD动作（包括Shake和新增动作）均可触发；关闭时只使用MmdActions配置名单。需要启用MMD动作触发。", "Gather for every MMD action, including Shake and new imports. Otherwise use the MmdActions list. Requires MMD action triggers.");
        ModOptions.Number("crowd.radius", "[NPC围观] 围观半径", "[Spectators] Radius", Radius, 18, 80, 0.1f, "m", "玩家与第一圈围观行人的距离。", "Distance from the player to the inner spectator ring.");
        ModOptions.Number("crowd.spacing", "[NPC围观] 行人间距", "[Spectators] Spacing", Spacing, 8, 30, 0.1f, "m", "围观目标位置之间的最小间距。", "Minimum separation between spectator destinations.");
        new Harmony("local.seleka2.npcspectators").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("CROWD_READY version=0.3.1 standalone=settings native=HipShake optionalMmd=true");
    }
}
