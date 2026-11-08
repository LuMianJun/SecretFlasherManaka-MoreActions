using System;
using Common.CommonUI;
using ExposureUnnoticed2.Object3D.IngameManager;
using HadakaCoat.ObjectsUi.Common.Button;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SFM.Actions;

// This adapter is only for the entry in the native settings list. The browser
// itself builds standard uGUI controls and has no native prefab dependencies.
internal static class NativeSettingsUi
{
    internal static T Parent<T>(Component component) where T:Component
    {
        for(var node=component.transform;node!=null;node=node.parent)
        {var value=node.GetComponent<T>();if(value!=null)return value;}
        return null;
    }
    internal static void Labels(GameObject node)
    {
        var assets=AssetReferencer.Instance;int language=ActionNames.Language;
        var font=assets==null?null:language switch {2=>assets.fontSc,3=>assets.fontTc,4=>assets.fontKr,_=>assets.defaultFont};
        foreach(var common in node.GetComponentsInChildren<CommonTextView>(true))
        {
            common.key="";common.isListenChangeEvent=false;
            var text=common.GetComponent<TextMeshProUGUI>();if(text==null)continue;
            if(!common.isNoChangeFont&&font!=null)text.font=font;
            text.enableAutoSizing=false;text.fontSize=22;
            if(common.fontMaterialId!=0&&assets?.fontMaterials!=null&&language<assets.fontMaterials.Length)
                text.fontMaterial=assets.fontMaterials[language];
        }
    }
    internal static ButtonView Entry(GameObject template,Transform parent,string name,string label,Action click)
    {
        var node=UnityEngine.Object.Instantiate(template,parent);node.name=name;Labels(node);
        var button=node.GetComponent<ButtonView>();node.GetComponent<Button>().onClick=new Button.ButtonClickedEvent();
        button.isInitialized=false;button.onClick=null;button.sendOnClickNotify=null;button.Initialize();button.SetText(label);
        button.RegisterAction(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(click));node.SetActive(true);return button;
    }
}
