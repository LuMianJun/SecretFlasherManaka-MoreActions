using System;
using System.Collections.Generic;
using Common.CommonUI;
using ExposureUnnoticed2.ObjectUI.OptionMenu;
using ExposureUnnoticed2.Scripts.Base;
using HadakaCoat.ObjectsUi.Common.Button;
using HarmonyLib;
using Il2CppInterop.Runtime;
using SFM.ModSettings;
using UnityEngine;
using UnityEngine.UI;

namespace SFM.Actions;

internal static class MoreActionsSettingsPage
{
    const string PanelName="MoreActionsSettingsPanel";
    sealed class State
    {
        internal int Revision,Language;
        internal bool Closing;
        internal OptionMenuView Menu;
        internal GameSystemMenuView View;
        internal ReplacementBrowserView Browser;
        internal ButtonView ManageButton;
    }
    static readonly Dictionary<IntPtr,State> states=new();
    internal static bool Owns(GameSystemMenuView view)=>view!=null&&view.gameObject.name==PanelName;
    internal static void AddCategory(OptionMenuView menu)
    {
        var stale=new List<IntPtr>();
        foreach(var pair in states)
            if(pair.Value.Menu==null||pair.Value.View==null){pair.Value.Browser?.Dispose();stale.Add(pair.Key);}
        foreach(var key in stale)states.Remove(key);
        var parent=menu.keyconfigButton.transform.parent;
        if(parent.Find("MoreActionsCategory")!=null)return;
        var node=UnityEngine.Object.Instantiate(menu.keyconfigButton.gameObject,parent);
        node.name="MoreActionsCategory";
        var button=node.GetComponent<ButtonView>();
        node.GetComponent<Button>().onClick=new Button.ButtonClickedEvent();
        button.isInitialized=false;button.onClick=null;button.sendOnClickNotify=null;button.Initialize();
        foreach(var text in node.GetComponentsInChildren<CommonTextView>(true)){text.key="mod_setting_more_actions_page";text.isListenChangeEvent=true;}
        button.SetText(ModOptions.PageTitle());
        button.RegisterAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(()=>Open(menu))));
        node.SetActive(true);menu.categoryGroup.ReSearchButton(false,false);
        var horizontal=parent.GetComponent<HorizontalLayoutGroup>();
        if(horizontal!=null){horizontal.childControlWidth=true;horizontal.childForceExpandWidth=true;}
        LayoutRebuilder.MarkLayoutForRebuild(parent.GetComponent<RectTransform>());
    }
    static void Open(OptionMenuView menu)
    {
        menu.CommonOnClickProcess(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(()=>{
            var node=UnityEngine.Object.Instantiate(menu.gameSystemPanel,menu.middleParent);
            node.name=PanelName;
            var view=node.GetComponent<GameSystemMenuView>();
            view.animator=node.GetComponent<Animator>();
            view.onClose=DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(()=>menu.OnCloseChild()));
            menu.currentOpen=(OptionMenuView.MiddleType)6;
            menu.currentOpenView=view.Cast<IOptionMiddlePanel>();
            // Never call native GameSystem.Initialize/Close here: that panel
            // reads and writes unrelated OptionData fields and saves them.
            foreach(var nativeButton in node.GetComponentsInChildren<Button>(true))
            {
                var calls=nativeButton.onClick.m_PersistentCalls?.m_Calls;
                if(calls==null)continue;
                for(int i=0;i<calls.Count;i++)
                    if(calls[i].m_MethodName=="AllReset")nativeButton.gameObject.SetActive(false);
            }
            states[view.Pointer]=new State{Revision=-1,Language=-1,Menu=menu,View=view};
            Refresh(view);
        })),(OptionMenuView.MiddleType)6);
    }
    internal static void Refresh(GameSystemMenuView view)
    {
        if(!Owns(view)||!states.TryGetValue(view.Pointer,out var state)||state.Closing)return;
        if(state.Browser!=null){state.Browser.Refresh();return;}
        if(state.Revision==ModOptions.Revision&&state.Language==ActionNames.Language)return;
        ModOptions.Add(view);
        if(state.ManageButton==null)
        {
            state.ManageButton=NativeSettingsUi.Entry(state.Menu.keyconfigButton.gameObject,view.futanariDisable.transform.parent,
                "MoreActionsManageReplacements","",()=>OpenBrowser(view));
            var layout=state.ManageButton.gameObject.GetComponent<LayoutElement>()??state.ManageButton.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight=42;layout.minHeight=42;
        }
        state.ManageButton.gameObject.SetActive(true);state.ManageButton.transform.SetAsLastSibling();
        NativeSettingsUi.Labels(state.ManageButton.gameObject);
        state.ManageButton.SetText(ActionNames.Text("動作の置換を管理","Manage animation replacements","管理动作替换","管理動作替換","동작 교체 관리"));
        var group=NativeSettingsUi.Parent<ButtonGroupManager>(view.futanariDisable);
        if(group!=null)group.ReSearchButton(false,false);
        LayoutRebuilder.MarkLayoutForRebuild(view.futanariDisable.transform.parent.GetComponent<RectTransform>());
        state.Revision=ModOptions.Revision;state.Language=ActionNames.Language;
    }
    static void OpenBrowser(GameSystemMenuView view)
    {
        if(!states.TryGetValue(view.Pointer,out var state)||state.Closing||state.Browser!=null)return;
        try
        {
            state.Browser=new ReplacementBrowserView(state.Menu,view,()=>Back(view));
            state.Browser.Refresh();
            Plugin.LogSource.LogInfo("ACTION_REPLACEMENT_BROWSER_OPEN candidates="+ReplacementService.Catalog.Options.Count);
        }
        catch(Exception e)
        {
            state.Browser?.Dispose();state.Browser=null;
            Plugin.LogSource.LogError("ACTION_REPLACEMENT_BROWSER_FAILED "+e);
        }
    }
    internal static bool Back(GameSystemMenuView view)
    {
        if(!Owns(view)||!states.TryGetValue(view.Pointer,out var state)||state.Browser==null)return false;
        state.Browser.Dispose();state.Browser=null;state.Revision=-1;Refresh(view);
        if(state.ManageButton!=null)ReplacementUi.Focus(state.ManageButton.gameObject);
        return true;
    }
    internal static bool BeforeUpdate(GameSystemMenuView view)
    {
        if(Owns(view)&&states.TryGetValue(view.Pointer,out var state)&&state.Browser!=null
            &&InputManager.IsDownFrame(InputManager.InputType.Cancel))
        {ReplacementUi.Sound(true);if(!state.Browser.HandleBack())Back(view);return false;}
        return true;
    }
    internal static bool Close(GameSystemMenuView view)
    {
        if(!Owns(view))return true;
        if(states.TryGetValue(view.Pointer,out var state)&&!state.Closing)
        {state.Closing=true;state.Browser?.Dispose();state.Browser=null;view.animator.SetTrigger("Close");view.onClose?.Invoke();}
        return false;
    }
    internal static void Removed(GameSystemMenuView view)
    {if(view!=null&&states.TryGetValue(view.Pointer,out var state)){state.Browser?.Dispose();states.Remove(view.Pointer);}}
}
[HarmonyPatch(typeof(OptionMenuView),"Initialize")]
internal static class MoreActionsCategoryPatch
{
    static void Postfix(OptionMenuView __instance){try{MoreActionsSettingsPage.AddCategory(__instance);}catch(Exception e){Plugin.LogSource.LogError("ACTION_SETTINGS_CATEGORY_FAILED "+e);}}
}
[HarmonyPatch(typeof(GameSystemMenuView),"Update")]
internal static class MoreActionsSettingsRefreshPatch
{
    static bool Prefix(GameSystemMenuView __instance)=>MoreActionsSettingsPage.BeforeUpdate(__instance);
    static void Postfix(GameSystemMenuView __instance){try{MoreActionsSettingsPage.Refresh(__instance);}catch(Exception e){Plugin.LogSource.LogWarning("ACTION_SETTINGS_REFRESH_FAILED "+e);}}
}
[HarmonyPatch(typeof(GameSystemMenuView),"Close")]
internal static class MoreActionsSettingsClosePatch {static bool Prefix(GameSystemMenuView __instance)=>MoreActionsSettingsPage.Close(__instance);}
[HarmonyPatch(typeof(GameSystemMenuView),"AllReset")]
internal static class MoreActionsSettingsResetPatch {static bool Prefix(GameSystemMenuView __instance)=>!MoreActionsSettingsPage.Owns(__instance);}
[HarmonyPatch(typeof(GameSystemMenuView),"OnAnimCloseEnd")]
internal static class MoreActionsSettingsRemovedPatch {static void Prefix(GameSystemMenuView __instance)=>MoreActionsSettingsPage.Removed(__instance);}
