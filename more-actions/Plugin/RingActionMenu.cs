using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Object3D.IngameManager;

namespace Seleka2.Actions;

internal static class RingActionMenu
{
    internal static ConfigEntry<string> LastActions;
    static RingMenuParentView owner;
    static RingMenuItemModel[][] models;
    static int revision = -1;
    internal sealed class OpenState { internal RingMenuParentView Parent; internal Il2CppReferenceArray<RingMenuItemModel> Original; }
    internal static int PageStart(int nativeCount) => checked((nativeCount + 7) / 8 * 8);
    internal static string Key(string id) => "mod_moreactions_" + id;
    internal static string FromKey(string key) => key != null && key.StartsWith("mod_moreactions_", StringComparison.Ordinal) && ActionRegistry.Get(key.Substring(16)) != null ? key.Substring(16) : null;
    static string[] Choices()
    {
        var result = new string[8]; Array.Fill(result, "");
        var saved = (LastActions.Value ?? "").Split(','); Array.Copy(saved, result, Math.Min(saved.Length, 8)); return result;
    }
    static void Save(int slot, string id) { var saved = Choices(); saved[slot] = id ?? ""; LastActions.Value = string.Join(",", saved); }

    static void Ensure(RingMenuParentView parent)
    {
        if (owner == parent && revision == ActionRegistry.Revision && models != null) return;
        owner = parent; revision = ActionRegistry.Revision;
        var items = ActionRegistry.GetAll().Where(d => d.ShowInRing).ToArray(); models = new RingMenuItemModel[8][];
        int first = PageStart(parent.commonRingMenuModel.Length);
        for (int slot = 0; slot < 8; slot++)
        {
            int capturedSlot = slot; models[slot] = new RingMenuItemModel[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var id = items[i].Id;
                var model = new RingMenuItemModel { Index = first + i, NameKey = Key(id), RingItemType = RingMenuParentView.RingItemType.None };
                model.OnSelect = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Select(parent, capturedSlot, model, id)));
                models[slot][i] = model;
            }
        }
    }

    static RingMenuItemModel Last(RingMenuParentView p, int slot) => slot switch { 0 => p.lastInvokeModelCommon, 1 => p.lastInvokeModelSkill, 2 => p.lastInvokeModelSkill3, 3 => p.lastInvokeModelSkill4, 4 => p.lastInvokeModelSkill5, 5 => p.lastInvokeModelSkill6, 6 => p.lastInvokeModelSkill7, _ => p.lastInvokeModelSkill8 };
    static void SetLast(RingMenuParentView p, int slot, RingMenuItemModel model)
    {
        switch (slot) { case 0: p.lastInvokeModelCommon = model; break; case 1: p.lastInvokeModelSkill = model; break; case 2: p.lastInvokeModelSkill3 = model; break; case 3: p.lastInvokeModelSkill4 = model; break; case 4: p.lastInvokeModelSkill5 = model; break; case 5: p.lastInvokeModelSkill6 = model; break; case 6: p.lastInvokeModelSkill7 = model; break; case 7: p.lastInvokeModelSkill8 = model; break; }
        p.shortcutItemViews[slot].SetName(model);
    }
    static void Page(RingMenuParentView p, int slot, int page)
    {
        switch (slot) { case 0: p.CurrentRingIndexCommon = page; break; case 1: p.CurrentRingIndexSkill = page; break; case 2: p.CurrentRingIndexSkill3 = page; break; case 3: p.CurrentRingIndexSkill4 = page; break; case 4: p.CurrentRingIndexSkill5 = page; break; case 5: p.CurrentRingIndexSkill6 = page; break; case 6: p.CurrentRingIndexSkill7 = page; break; case 7: p.CurrentRingIndexSkill8 = page; break; }
    }
    static void Select(RingMenuParentView parent, int slot, RingMenuItemModel model, string id)
    {
        Save(slot, id); SetLast(parent, slot, model); ActionDriver.Current?.Request(id);
    }

    internal static void Initialized(RingMenuParentView parent) { revision = -1; Ensure(parent); RestoreChoices(parent); }
    internal static void RestoreChoices(RingMenuParentView parent)
    {
        Ensure(parent); var saved = Choices();
        for (int slot = 0; slot < 8; slot++)
        {
            if (slot == 0 && parent.currentOverrideType != RingMenuParentView.RingMenuOverrideType.None) continue;
            var selected = models[slot].FirstOrDefault(m => FromKey(m.NameKey) == saved[slot]);
            if (selected != null) SetLast(parent, slot, selected);
        }
    }

    internal static OpenState BeginOpen(RingMenuParentView parent, RingMenuParentView.RingMenuType type)
    {
        if (type == RingMenuParentView.RingMenuType.Common && parent.currentOverrideType != RingMenuParentView.RingMenuOverrideType.None) return null;
        Ensure(parent); int slot = (int)type; if (slot < 0 || slot >= 8) return null;
        // Native Common recomputes its page count on every open. Skill slots
        // reuse an initialization-time count, including when creating views.
        // Keep the expanded count until the open menu finishes using it.
        if (type != RingMenuParentView.RingMenuType.Common)
            parent.maxRingIndexSkill = (PageStart(parent.commonRingMenuModel.Length) + models[slot].Length + 7) / 8 - 1;
        if (models[slot].Length == 0) return null;
        RestoreChoices(parent);
        var original = parent.commonRingMenuModel;
        int first = PageStart(original.Length), count = PageStart(first + models[slot].Length);
        var combined = new Il2CppReferenceArray<RingMenuItemModel>(count);
        for (int i = 0; i < original.Length; i++) combined[i] = original[i];
        for (int i = 0; i < models[slot].Length; i++) { models[slot][i].Index = first + i; combined[first + i] = models[slot][i]; }
        parent.commonRingMenuModel = combined; return new OpenState { Parent = parent, Original = original };
    }
    internal static void RestoreArray(OpenState state) { if (state?.Parent != null) state.Parent.commonRingMenuModel = state.Original; }
    internal static void Decorate(RingMenuParentView parent, Il2CppReferenceArray<RingMenuItemModel> items, ref Il2CppSystem.Action<RingMenuItemModel> callback)
    {
        if (!items.Any(m => m != null && FromKey(m.NameKey) != null)) return;
        int slot = (int)parent.currentOpenType; var selected = models[slot].FirstOrDefault(m => FromKey(m.NameKey) == Choices()[slot]);
        if (selected != null) Page(parent, slot, selected.Index / 8);
        var native = callback;
        callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<RingMenuItemModel>>(new Action<RingMenuItemModel>(model =>
        {
            var id = model == null ? null : FromKey(model.NameKey);
            if (id != null) Select(parent, slot, model, id);
            else { Save(slot, ""); native.Invoke(model); }
        }));
    }
    internal static bool ShortcutIndex(RingMenuParentView parent, out int index)
    {
        index = 0; int slot = (int)parent.currentOpenType;
        if (parent != owner || models == null || slot < 0 || slot >= 8 || (slot == 0 && parent.currentOverrideType != RingMenuParentView.RingMenuOverrideType.None)) return false;
        var selected = models[slot].FirstOrDefault(m => FromKey(m.NameKey) == Choices()[slot]);
        if (selected == null) return false; index = selected.Index; return true;
    }
    internal static void TickShortcuts(RingMenuParentView parent)
    {
        if (parent.IsOpenRing || parent.lockOpen || parent.IsClickToAction) return;
        var ui = InGameUiManager.Instance;
        if (ui == null || ui.IsOpenSomeUi || SexManager.Instance?.IsSexMode == true) return;
        // Providers may register after player initialization while parsing in
        // the background. Restore saved labels/shortcuts when they arrive.
        if (parent != owner || revision != ActionRegistry.Revision || models == null) RestoreChoices(parent);
        var keys = new[] { InputManager.InputType.SkillRingMenu, InputManager.InputType.SkillRingMenu3, InputManager.InputType.SkillRingMenu4, InputManager.InputType.SkillRingMenu5, InputManager.InputType.SkillRingMenu6, InputManager.InputType.SkillRingMenu7, InputManager.InputType.SkillRingMenu8 };
        // Common already invokes non-null OnSelect in native LateUpdate. The
        // seven skill slots need this branch because native dispatch skips it.
        for (int slot = 1; slot < 8; slot++)
        {
            var model = Last(parent, slot);
            if (model != null && FromKey(model.NameKey) != null && parent.GetShortUp(keys[slot - 1])) model.OnSelect.Invoke();
        }
    }
}

[HarmonyPatch(typeof(RingMenuParentView), "Initialize")]
static class RingInitializePatch { static void Postfix(RingMenuParentView __instance) => RingActionMenu.Initialized(__instance); }
[HarmonyPatch(typeof(RingMenuParentView), "SetOverride")]
static class RingOverridePatch { static void Postfix(RingMenuParentView __instance) => RingActionMenu.RestoreChoices(__instance); }
[HarmonyPatch(typeof(RingMenuParentView), "OpenRingMenu")]
static class RingOpenPatch
{
    static void Prefix(RingMenuParentView __instance, RingMenuParentView.RingMenuType __0, out RingActionMenu.OpenState __state) => __state = RingActionMenu.BeginOpen(__instance, __0);
    static void Postfix(RingActionMenu.OpenState __state) => RingActionMenu.RestoreArray(__state);
    static Exception Finalizer(Exception __exception, RingActionMenu.OpenState __state) { RingActionMenu.RestoreArray(__state); return __exception; }
}
[HarmonyPatch(typeof(RingMenuView), "Initialize")]
static class RingViewPatch { static void Prefix(RingMenuParentView __0, Il2CppReferenceArray<RingMenuItemModel> __1, ref Il2CppSystem.Action<RingMenuItemModel> __2) => RingActionMenu.Decorate(__0, __1, ref __2); }
[HarmonyPatch(typeof(RingMenuParentView), "CurrentShortcutIndex")]
static class RingShortcutPatch { static bool Prefix(RingMenuParentView __instance, ref int __result) { if (!RingActionMenu.ShortcutIndex(__instance, out int index)) return true; __result = index; return false; } }
[HarmonyPatch(typeof(RingMenuParentView), "LateUpdate")]
static class RingShortPressPatch { static void Postfix(RingMenuParentView __instance) { try { RingActionMenu.TickShortcuts(__instance); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_SHORTCUT_FAILED " + e.Message); } } }
[HarmonyPatch(typeof(Localizer), "Trans", new Type[] { typeof(string) })]
static class ActionLabelPatch { static bool Prefix(string __0, ref string __result) { var id = RingActionMenu.FromKey(__0); if (id == null) return true; __result = ActionRegistry.Get(id).DisplayName; return false; } }
