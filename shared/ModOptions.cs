using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Common.CommonUI;
using HadakaCoat.ObjectsUi.Common.Button;
using ExposureUnnoticed2.ObjectUI.OptionMenu;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Seleka2.ModSettings;

// Linked privately into each plugin: installing NPC Spectators alone still
// provides its settings, without depending on another plugin or UI assembly.
internal static class ModOptions
{
    sealed class Item
    {
        internal string Id, Chinese, English, HelpChinese, HelpEnglish;
        internal Func<bool> ReadBool; internal Action<bool> WriteBool;
        internal Func<float> ReadNumber; internal Action<float> WriteNumber;
        internal int Min, Max; internal float Unit; internal string Suffix;
    }
    static readonly List<Item> items = new();
    internal static Action<string> Log;
    static bool Chinese => GameState.OptionData != null && ((int)GameState.OptionData.Language == 2 || (int)GameState.OptionData.Language == 3);
    static string Key(Item item) => "mod_setting_" + item.Id;
    internal static void Toggle(string id, string chinese, string english, ConfigEntry<bool> entry, string helpChinese, string helpEnglish)
        => Toggle(id, chinese, english, () => entry.Value, value => entry.Value = value, helpChinese, helpEnglish);
    internal static void Toggle(string id, string chinese, string english, Func<bool> read, Action<bool> write, string helpChinese, string helpEnglish)
        => items.Add(new Item { Id = id, Chinese = chinese, English = english, ReadBool = read, WriteBool = write, HelpChinese = helpChinese, HelpEnglish = helpEnglish });
    internal static void Number(string id, string chinese, string english, ConfigEntry<float> entry, int min, int max, float unit, string suffix, string helpChinese, string helpEnglish)
        => items.Add(new Item { Id = id, Chinese = chinese, English = english, ReadNumber = () => entry.Value, WriteNumber = value => entry.Value = value, Min = min, Max = max, Unit = unit, Suffix = suffix, HelpChinese = helpChinese, HelpEnglish = helpEnglish });
    internal static bool Translate(string key, out string text)
    {
        var item = items.FirstOrDefault(i => Key(i) == key);
        text = item == null ? null : Chinese ? item.Chinese : item.English;
        return item != null;
    }
    static T Parent<T>(Component component) where T : Component
    {
        // Explicit traversal also works while native settings panels initialize inactive.
        for (var node = component.transform; node != null; node = node.parent)
        {
            var found = node.GetComponent<T>();
            if (found != null) return found;
        }
        return null;
    }
    static void Describe(GameSystemMenuView view, Item item)
    {
        var menu = Parent<OptionMenuView>(view);
        if (menu == null) return;
        menu.descriptionTitle.text = Chinese ? item.Chinese : item.English;
        menu.description.text = Chinese ? item.HelpChinese : item.HelpEnglish;
        menu.descriptionParent.SetActive(true);
    }
    static GameObject Row(GameSystemMenuView view, Item item, GameObject template)
    {
        string name = "ModSetting_" + item.Id;
        var existing = template.transform.parent.Find(name);
        var row = existing != null ? existing.gameObject : UnityEngine.Object.Instantiate(template, template.transform.parent);
        row.name = name; row.SetActive(true);
        var label = row.transform.Find("CommonText");
        if (label == null) throw new Exception("Native option label missing");
        var common = label.GetComponent<CommonTextView>();
        if (common != null) { common.key = Key(item); common.isListenChangeEvent = true; }
        label.GetComponent<TextMeshProUGUI>().text = Chinese ? item.Chinese : item.English;
        return row;
    }
    internal static void Add(GameSystemMenuView view)
    {
        foreach (var item in items)
        {
            GameObject row = null;
            try
            {
                bool initializing = true;
                var onClick = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<OptionItemType>>(new Action<OptionItemType>(_ => { if (!initializing) Describe(view, item); }));
                if (item.ReadBool != null)
                {
                    row = Row(view, item, view.futanariDisable.gameObject);
                    var toggle = row.GetComponent<OptionItemCheckView>();
                    toggle.Initialize(item.ReadBool(), DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(new Action<bool>(value => { if (!initializing) item.WriteBool(value); })), onClick);
                }
                else
                {
                    row = Row(view, item, view.maleRate.gameObject);
                    var bar = row.GetComponent<OptionItemBarView>();
                    bar.GetComponentInChildren<Slider>(true).minValue = item.Min;
                    float value = item.ReadNumber(); if (!float.IsFinite(value)) value = item.Min * item.Unit;
                    int initial = Mathf.Clamp(Mathf.RoundToInt(value / item.Unit), item.Min, item.Max);
                    bar.Initialize(initial, item.Max, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(new Action<int>(current =>
                    {
                        bar.SetValueText((current * item.Unit).ToString(item.Unit < 0.1f ? "0.00" : item.Unit < 1 ? "0.0" : "0") + item.Suffix);
                        if (!initializing) item.WriteNumber(current * item.Unit);
                    })), onClick);
                }
                initializing = false;
            }
            catch (Exception e) { if (row != null) row.SetActive(false); Log?.Invoke("MOD_SETTING_ROW_FAILED id=" + item.Id + " " + e.Message); }
        }
        // Native scroll content owns layout and controller/keyboard navigation.
        var group = Parent<ButtonGroupManager>(view.futanariDisable);
        if (group != null) group.ReSearchButton(false, false);
        LayoutRebuilder.MarkLayoutForRebuild(view.futanariDisable.transform.parent.GetComponent<RectTransform>());
    }
}

[HarmonyPatch(typeof(GameSystemMenuView), "Initialize")]
internal static class ModOptionsPagePatch
{
    static void Postfix(GameSystemMenuView __instance)
    {
        try { ModOptions.Add(__instance); }
        catch (Exception e) { ModOptions.Log?.Invoke("MOD_SETTINGS_PAGE_FAILED " + e.Message); }
    }
}
[HarmonyPatch(typeof(Localizer), "Trans", new Type[] { typeof(string) })]
internal static class ModOptionLabelsPatch
{
    static bool Prefix(string __0, ref string __result)
    {
        if (!ModOptions.Translate(__0, out var text)) return true;
        __result = text; return false;
    }
}
