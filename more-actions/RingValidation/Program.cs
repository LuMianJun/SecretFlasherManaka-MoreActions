using Seleka2.Actions;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

int checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
RingActionMenu.LastActions = new("");
// The boundary reproduces native page creation: Common recomputes its count;
// Skill uses the stored count both for construction and subsequent navigation.
(List<Il2CppSystem.Action<RingMenuItemModel>> callbacks, int pages) Open(RingMenuParentView p, int slot)
{
    var original = p.commonRingMenuModel;
    var state = RingActionMenu.BeginOpen(p, (RingMenuParentView.RingMenuType)slot);
    p.currentOpenType = (RingMenuParentView.RingMenuType)slot;
    if (slot == 0) p.maxRingIndexCommon = (p.commonRingMenuModel.Length - 1) / 8;
    int pages = (slot == 0 ? p.maxRingIndexCommon : p.maxRingIndexSkill) + 1;
    var callbacks = new List<Il2CppSystem.Action<RingMenuItemModel>>();
    for (int page = 0; page < pages; page++)
    {
        Il2CppSystem.Action<RingMenuItemModel> callback = new(model => p.SetNative(slot, model));
        RingActionMenu.Decorate(p, p.commonRingMenuModel, ref callback);
        callbacks.Add(callback);
    }
    RingActionMenu.RestoreArray(state); RingActionMenu.RestoreArray(state);
    Check(ReferenceEquals(original, p.commonRingMenuModel), "Temporary model array is restored, including repeated finalization");
    return (callbacks, pages);
}
foreach (int nativeCount in new[] { 8, 9, 16, 23 })
foreach (int customCount in new[] { 1, 8, 9, 17 })
foreach (int slot in Enumerable.Range(0, 8))
{
    RingActionMenu.LastActions.Value = "";
    ActionRegistry.Set(customCount);
    var p = new RingMenuParentView(nativeCount); RingActionMenu.Initialized(p);
    var opened = Open(p, slot);
    int first = RingActionMenu.PageStart(nativeCount);
    Check(opened.pages == (first + customCount + 7) / 8, "Every slot creates all appended pages");
    var selected = new RingMenuItemModel { Index = first + customCount - 1, NameKey = RingActionMenu.Key("custom" + (customCount - 1)) };
    opened.callbacks[^1].Invoke(selected);
    Check(RingActionMenu.LastActions.Value.Split(',')[slot] == "custom" + (customCount - 1), "Custom selection is slot-local");
    Check(RingActionMenu.ShortcutIndex(p, out int selectedIndex) && selectedIndex == selected.Index, "Custom shortcut restores appended index");
    var native = p.commonRingMenuModel[0];
    Open(p, slot).callbacks[0].Invoke(native);
    Check(RingActionMenu.LastActions.Value.Split(',')[slot] == "", "Selecting native clears custom shortcut");
    Check(p.NativeSelections == 1, "Native selection callback still runs exactly once");
    Check(Open(p, slot).pages == opened.pages, "Selecting native then reopening retains every custom page");
    Check(!RingActionMenu.ShortcutIndex(p, out _), "Native selection uses native shortcut lookup");
    ActionRegistry.Set(0);
    Check(Open(p, slot).pages == (nativeCount + 7) / 8, "Removing provider contracts page count");
}
var special = new RingMenuParentView(9) { currentOverrideType = RingMenuParentView.RingMenuOverrideType.Special };
ActionRegistry.Set(9);
Check(RingActionMenu.BeginOpen(special, RingMenuParentView.RingMenuType.Common) == null, "Special native Common menu remains unchanged");
Check(Open(special, 1).pages == 4, "Skill slots remain expandable during a Common override");
ActionRegistry.Set(0); RingActionMenu.LastActions.Value="custom0,custom0";
var delayed=new RingMenuParentView(9);RingActionMenu.Initialized(delayed);
Check(delayed.lastInvokeModelCommon==null,"An unparsed saved action is not invoked during initialization");
ActionRegistry.Set(1);ExposureUnnoticed2.Object3D.IngameManager.InGameUiManager.Instance=new();
RingActionMenu.TickShortcuts(delayed);
Check(delayed.lastInvokeModelCommon?.NameKey==RingActionMenu.Key("custom0") && delayed.lastInvokeModelSkill?.NameKey==RingActionMenu.Key("custom0"),"Async provider arrival restores remembered shortcuts without reopening the ring");
Console.WriteLine($"PASS {checks} assertions: actual ring adapter, slots 1-8, native/custom selection, reopen, multi-page packs, provider removal, override and cleanup");

namespace BepInEx.Configuration { public sealed class ConfigEntry<T>(T value) { public T Value = value; } }
namespace HarmonyLib { [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) {} public HarmonyPatch(Type type, string name, Type[] args) {} } }
namespace Il2CppSystem
{
    public sealed class Action(System.Action callback) { public void Invoke() => callback(); }
    public sealed class Action<T>(System.Action<T> callback) { public void Invoke(T value) => callback(value); }
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T ConvertDelegate<T>(Delegate callback)
        {
            if (typeof(T) == typeof(Il2CppSystem.Action)) return (T)(object)new Il2CppSystem.Action((System.Action)callback);
            return (T)(object)new Il2CppSystem.Action<RingMenuItemModel>((System.Action<RingMenuItemModel>)callback);
        }
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppReferenceArray<T> : IEnumerable<T>
    {
        readonly T[] items; public Il2CppReferenceArray(int count) { items = new T[count]; }
        public int Length => items.Length; public T this[int index] { get => items[index]; set => items[index] = value; }
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace ExposureUnnoticed2.ObjectUI.InGame.RingMenu
{
    public sealed class RingMenuItemModel { public int Index; public string NameKey; public RingMenuParentView.RingItemType RingItemType; public Il2CppSystem.Action OnSelect; }
    public sealed class ShortcutItemView { public void SetName(RingMenuItemModel model) {} }
    public sealed class RingMenuView {}
    public sealed class RingMenuParentView
    {
        public enum RingMenuType { Common, Skill, Skill3, Skill4, Skill5, Skill6, Skill7, Skill8 }
        public enum RingItemType { None }
        public enum RingMenuOverrideType { None, Special }
        public RingMenuOverrideType currentOverrideType; public RingMenuType currentOpenType;
        public Il2CppReferenceArray<RingMenuItemModel> commonRingMenuModel;
        public int maxRingIndexSkill, maxRingIndexCommon, NativeSelections;
        public RingMenuItemModel lastInvokeModelCommon, lastInvokeModelSkill, lastInvokeModelSkill3, lastInvokeModelSkill4, lastInvokeModelSkill5, lastInvokeModelSkill6, lastInvokeModelSkill7, lastInvokeModelSkill8;
        public int CurrentRingIndexCommon, CurrentRingIndexSkill, CurrentRingIndexSkill3, CurrentRingIndexSkill4, CurrentRingIndexSkill5, CurrentRingIndexSkill6, CurrentRingIndexSkill7, CurrentRingIndexSkill8;
        public ShortcutItemView[] shortcutItemViews = Enumerable.Range(0,8).Select(_ => new ShortcutItemView()).ToArray();
        public bool IsOpenRing, lockOpen, IsClickToAction;
        public bool GetShortUp(ExposureUnnoticed2.Scripts.InGame.InputManager.InputType type) => false;
        public RingMenuParentView(int count)
        {
            commonRingMenuModel = new(count); maxRingIndexSkill = (count-1)/8;
            for (int i=0;i<count;i++) commonRingMenuModel[i] = new() { Index=i,NameKey="native"+i };
        }
        public void SetNative(int slot, RingMenuItemModel model) { NativeSelections++; }
    }
}
namespace ExposureUnnoticed2.Scripts.Base { public static class Localizer {} }
namespace ExposureUnnoticed2.Scripts.InGame { public static class InputManager { public enum InputType { SkillRingMenu, SkillRingMenu3, SkillRingMenu4, SkillRingMenu5, SkillRingMenu6, SkillRingMenu7, SkillRingMenu8 } } }
namespace ExposureUnnoticed2.Object3D.IngameManager
{
    public sealed class InGameUiManager { public static InGameUiManager Instance; public bool IsOpenSomeUi; }
    public sealed class SexManager { public static SexManager Instance; public bool IsSexMode; }
}
namespace Seleka2.Actions
{
    public sealed class ActionDefinition { public string Id, DisplayName; public bool ShowInRing = true; }
    public static class ActionRegistry
    {
        static ActionDefinition[] items = []; public static int Revision;
        public static void Set(int count) { items = Enumerable.Range(0,count).Select(i => new ActionDefinition { Id="custom"+i, DisplayName="Custom "+i }).ToArray(); Revision++; }
        public static ActionDefinition[] GetAll() => items; public static ActionDefinition Get(string id) => items.FirstOrDefault(x => x.Id==id);
    }
    public sealed class Logger { public void LogInfo(string text) {} public void LogWarning(string text) {} }
    public static class Plugin { public static Logger LogSource = new(); }
    public sealed class ActionDriver { public static ActionDriver Current; public void Request(string id) {} }
}
