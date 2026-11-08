using System;
using System.Collections.Generic;
using System.Linq;
using ExposureUnnoticed2.ObjectUI.OptionMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SFM.Actions;

// The native menu owns entry, pause and close. Every control inside this page
// is constructed from standard uGUI/TMP, with explicit dimensions and fonts.
internal sealed class ReplacementBrowserView : IDisposable
{
    readonly ReplacementUi ui;
    readonly ReplacementBrowserModel model=new();
    readonly ScrollRect original,packs,items;
    readonly GameObject root,sidebar,packHeader,slotHeader,overlay;
    readonly RectTransform dialog;
    readonly ScrollRect choices;
    readonly TextMeshProUGUI title,status,packTitle,empty,choiceTitle;
    readonly Button backButton,packMode,slotMode,apply,clear,choiceBack;
    readonly Toggle enabledToggle,onlyToggle;
    readonly List<Button> packRows=new(),slotRows=new(),choiceRows=new();
    readonly List<Toggle> variantRows=new();
    long version=long.MinValue;
    int stateVersion=-1,language=-1;
    bool enabled,refreshing,disposed,resetScroll=true;
    string popupSlot;
    Button popupOwner;
    static string T(params string[] words)=>ActionNames.Text(words);

    internal ReplacementBrowserView(OptionMenuView menu,GameSystemMenuView view,Action back)
    {
        original=NativeSettingsUi.Parent<ScrollRect>(view.futanariDisable);
        if(original==null)throw new InvalidOperationException("Native settings content missing");
        var sample=view.futanariDisable.transform.Find("CommonText").GetComponent<TextMeshProUGUI>();
        var oldToggle=view.futanariDisable.GetComponentInChildren<Toggle>(true);
        var mark=oldToggle==null?null:oldToggle.graphic?.TryCast<Image>();
        ui=new ReplacementUi(sample.font,mark?.sprite,mark==null?Color.white:mark.color);
        root=ui.Node("MoreActionsReplacementBrowser",original.transform.parent);
        var rect=ReplacementUi.Rect(root);var source=ReplacementUi.Rect(original.gameObject);
        rect.anchorMin=source.anchorMin;rect.anchorMax=source.anchorMax;rect.pivot=source.pivot;
        rect.sizeDelta=source.sizeDelta;rect.anchoredPosition=source.anchoredPosition;
        ReplacementUi.Paint(root,ReplacementUi.Background,true);ReplacementUi.Vertical(root,14,10);
        try
        {
            var header=ui.Node("Header",rect);ReplacementUi.Size(header,52);ReplacementUi.Horizontal(header);
            backButton=ui.Button(header.transform,"BrowserBack",back,22,true);ReplacementUi.Size(backButton.gameObject,52,92);
            title=ui.Label(header.transform,"BrowserTitle",26);ReplacementUi.Size(title.gameObject,52);
            enabledToggle=ui.Toggle(header.transform,"BrowserEnabled",value=>{
                if(refreshing)return;ReplacementUi.Sound();ReplacementService.SetEnabled(value);
            });ReplacementUi.Size(enabledToggle.gameObject,52,260);
            var tools=ui.Node("Tools",rect);ReplacementUi.Size(tools,52);ReplacementUi.Horizontal(tools);
            packMode=ui.Button(tools.transform,"ByPack",()=>Switch(false));ReplacementUi.Size(packMode.gameObject,52,154);
            slotMode=ui.Button(tools.transform,"BySlot",()=>Switch(true));ReplacementUi.Size(slotMode.gameObject,52,154);
            var spacer=ui.Node("Spacer",tools.transform);ReplacementUi.Size(spacer,52);
            onlyToggle=ui.Toggle(tools.transform,"SelectedOnly",value=>{
                if(refreshing)return;ReplacementUi.Sound();model.Filter(value);resetScroll=true;
            });ReplacementUi.Size(onlyToggle.gameObject,52,205);
            status=ui.Label(rect,"Status",18);status.color=ReplacementUi.Secondary;ReplacementUi.Size(status.gameObject,32);
            var body=ui.Node("Body",rect);ReplacementUi.Size(body);ReplacementUi.Horizontal(body,14);
            sidebar=ui.Node("PackSidebar",body.transform);ReplacementUi.Size(sidebar,-1,280);ReplacementUi.Vertical(sidebar,0,8);
            var packLabel=ui.Label(sidebar.transform,"PackLabel",20);ReplacementUi.Size(packLabel.gameObject,36);
            packs=ui.Scroll(sidebar.transform,"Packs");ReplacementUi.Size(packs.gameObject);
            var main=ui.Node("Main",body.transform);ReplacementUi.Size(main);ReplacementUi.Vertical(main,0,8);
            packHeader=ui.Node("PackActions",main.transform);ReplacementUi.Size(packHeader,48);ReplacementUi.Horizontal(packHeader,8);
            packTitle=ui.Label(packHeader.transform,"PackCount",20);ReplacementUi.Size(packTitle.gameObject,48);
            apply=ui.Button(packHeader.transform,"Apply",()=>{if(model.SelectedPack!=null)ReplacementService.ApplyPack(model.SelectedPack);},20);
            clear=ui.Button(packHeader.transform,"Clear",()=>{if(model.SelectedPack!=null)ReplacementService.ClearPack(model.SelectedPack);},20);
            ReplacementUi.Size(apply.gameObject,48,116);ReplacementUi.Size(clear.gameObject,48,116);
            slotHeader=ui.Node("SlotColumns",main.transform);ReplacementUi.Size(slotHeader,32);
            var actionColumn=ui.Label(slotHeader.transform,"ActionColumn",19);actionColumn.color=ReplacementUi.Secondary;
            ReplacementUi.Box(ReplacementUi.Rect(actionColumn.gameObject),0,0,.42f,1,14,0,-12,0);
            var sourceColumn=ui.Label(slotHeader.transform,"SourceColumn",19);sourceColumn.color=ReplacementUi.Secondary;
            ReplacementUi.Box(ReplacementUi.Rect(sourceColumn.gameObject),.42f,0,1,1,6,0,-14,0);
            items=ui.Scroll(main.transform,"Items");ReplacementUi.Size(items.gameObject);
            empty=ui.Label(items.content,"Empty",22,true);ReplacementUi.Size(empty.gameObject,76);
            overlay=ui.Node("SourceOverlay",rect);overlay.AddComponent<LayoutElement>().ignoreLayout=true;
            ReplacementUi.Box(ReplacementUi.Rect(overlay),0,0,1,1);
            var backdrop=ui.Node("Backdrop",overlay.transform);ReplacementUi.Box(ReplacementUi.Rect(backdrop),0,0,1,1);
            var shade=ReplacementUi.Paint(backdrop,new Color(0,0,0,.65f),true);
            var dismiss=backdrop.AddComponent<Button>();dismiss.targetGraphic=shade;dismiss.transition=Selectable.Transition.None;
            ReplacementUi.Bind(dismiss,CloseSource,true);
            var panel=ui.Node("SourceDialog",overlay.transform);dialog=ReplacementUi.Rect(panel);
            dialog.anchorMin=dialog.anchorMax=new Vector2(.5f,.5f);dialog.pivot=new Vector2(.5f,.5f);
            ReplacementUi.Paint(panel,ReplacementUi.Background,true);ReplacementUi.Vertical(panel,16,12);
            var popupHeader=ui.Node("Header",dialog);ReplacementUi.Size(popupHeader,84);ReplacementUi.Horizontal(popupHeader);
            choiceTitle=ui.Label(popupHeader.transform,"SourceTitle",24,true);ReplacementUi.Size(choiceTitle.gameObject,84);
            choiceBack=ui.Button(popupHeader.transform,"CloseSource",CloseSource,22,true);ReplacementUi.Size(choiceBack.gameObject,48,90);
            choices=ui.Scroll(dialog,"Sources");ReplacementUi.Size(choices.gameObject);
            overlay.SetActive(false);original.gameObject.SetActive(false);
            menu.descriptionParent.SetActive(false);
        }
        catch{Dispose();throw;}
    }
    void Switch(bool bySlot){model.Mode(bySlot);CloseSource();resetScroll=true;}
    internal bool HandleBack(){if(popupSlot==null)return false;CloseSource();return true;}
    void CloseSource()
    {
        popupSlot=null;if(overlay!=null)overlay.SetActive(false);
        if(popupOwner!=null&&popupOwner.gameObject.activeInHierarchy)ReplacementUi.Focus(popupOwner.gameObject);
        popupOwner=null;
    }
    string VariantName(ReplacementOption option)
    {
        bool duplicate=ReplacementService.Catalog.Variants(option.Slot).Count(o=>o.Pack==option.Pack)>1;
        return option.Pack+(duplicate?" · "+option.Source:"");
    }
    void OpenSource(string slot,Button owner)
    {
        popupSlot=slot;popupOwner=owner;
        var size=ReplacementUi.Rect(root).rect;
        dialog.sizeDelta=new Vector2(Mathf.Min(800,Mathf.Max(320,size.width-36)),Mathf.Min(650,Mathf.Max(260,size.height-36)));
        BuildChoices();overlay.transform.SetAsLastSibling();overlay.SetActive(true);
        int selected=Array.FindIndex(ReplacementService.Catalog.Variants(slot),o=>o.Id==ReplacementService.Catalog.Selected(slot))+1;
        if(selected<choiceRows.Count)ReplacementUi.Focus(choiceRows[selected].gameObject);
    }
    Button ListButton(List<Button> pool,int index,Transform parent,string name,float height)
    {
        if(index==pool.Count){var added=ui.Button(parent,name+index,()=>{});ReplacementUi.Size(added.gameObject,height);pool.Add(added);}
        var button=pool[index];button.gameObject.SetActive(true);button.transform.SetSiblingIndex(index);return button;
    }
    void BuildChoices()
    {
        foreach(var row in choiceRows)row.gameObject.SetActive(false);
        var catalog=ReplacementService.Catalog;var variants=catalog.Variants(popupSlot);
        choiceTitle.text=T("置換元を選択","Choose animation source","选择替换来源","選擇替換來源","교체 소스 선택")+"\n"+ActionNames.Clip(popupSlot);
        ReplacementUi.Caption(choiceBack,T("戻る","Back","返回","返回","뒤로"));
        var original=ListButton(choiceRows,0,choices.content,"Source",60);
        ReplacementUi.Caption(original,T("元の動作","Original","原版","原版","원본"));
        ReplacementUi.ColorButton(original,!variants.Any(o=>o.Id==catalog.Selected(popupSlot)));
        string slot=popupSlot;
        ReplacementUi.Bind(original,()=>{ReplacementService.SelectOriginal(slot);CloseSource();});
        for(int index=0;index<variants.Length;index++)
        {
            var option=variants[index];var button=ListButton(choiceRows,index+1,choices.content,"Source",84);
            var text=button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            text.enableWordWrapping=true;text.maxVisibleLines=2;text.alignment=TextAlignmentOptions.MidlineLeft;
            ReplacementUi.Caption(button,VariantName(option));ReplacementUi.ColorButton(button,catalog.Selected(slot)==option.Id);
            ReplacementUi.Bind(button,()=>{ReplacementService.SelectVariant(option.Id,true);CloseSource();});
        }
        LayoutRebuilder.MarkLayoutForRebuild(choices.content);choices.verticalNormalizedPosition=1;
    }
    void PackRow(int index,string pack)
    {
        var catalog=ReplacementService.Catalog;var button=ListButton(packRows,index,packs.content,"Pack",108);
        var name=button.transform.Find("Label").GetComponent<TextMeshProUGUI>();name.alignment=TextAlignmentOptions.TopLeft;
        name.fontSize=20;name.fontSizeMin=20;name.fontSizeMax=20;name.enableWordWrapping=true;name.maxVisibleLines=2;
        ReplacementUi.Box(ReplacementUi.Rect(name.gameObject),0,0,1,1,14,34,-14,-8);name.text=pack;
        var count=button.transform.Find("Count")?.GetComponent<TextMeshProUGUI>();
        if(count==null){count=ui.Label(button.transform,"Count",16);count.color=ReplacementUi.Secondary;ReplacementUi.Box(ReplacementUi.Rect(count.gameObject),0,0,1,0,14,4,-14,32);}
        count.text=string.Format(T("{0}/{1} 選択","{0}/{1} selected","已选择 {0}/{1}","已選取 {0}/{1}","{0}/{1} 선택"),catalog.EnabledInPack(pack),catalog.Slots(pack).Length);
        ReplacementUi.ColorButton(button,model.SelectedPack==pack);
        ReplacementUi.Bind(button,()=>{model.Pack(pack);resetScroll=true;});
    }
    void VariantRow(int index,ReplacementOption option)
    {
        if(index==variantRows.Count)
        {
            var added=ui.Toggle(items.content,"Variant"+index,_=>{});ReplacementUi.Size(added.gameObject,56);variantRows.Add(added);
        }
        var toggle=variantRows[index];toggle.gameObject.SetActive(true);toggle.transform.SetSiblingIndex(index);
        toggle.isOn=ReplacementService.Catalog.Selected(option.Slot)==option.Id;
        bool duplicate=ReplacementService.Catalog.Options.Count(o=>o.Pack==option.Pack&&o.Slot==option.Slot)>1;
        ReplacementUi.Caption(toggle,ActionNames.Clip(option.Slot)+(duplicate?" · "+option.Source:""));
        ReplacementUi.Bind(toggle,value=>{if(refreshing)return;ReplacementUi.Sound();ReplacementService.SelectVariant(option.Id,value);});
    }
    void SlotRow(int index,string slot)
    {
        var row=ListButton(slotRows,index,items.content,"Slot",60);
        // The row's own text is an explicit action label, not a borrowed
        // SettingSelectItemView title with a null serialized reference.
        var label=row.transform.Find("Label").GetComponent<TextMeshProUGUI>();label.text=ActionNames.Clip(slot);
        label.alignment=TextAlignmentOptions.MidlineLeft;ReplacementUi.Box(ReplacementUi.Rect(label.gameObject),0,0,.42f,1,14,8,-12,-8);
        var source=row.transform.Find("Source")?.GetComponent<Button>();
        if(source==null)
        {
            source=ui.Button(row.transform,"Source",()=>{},20);
            ReplacementUi.Box(ReplacementUi.Rect(source.gameObject),.42f,0,1,1,6,7,-8,-7);
        }
        var selected=ReplacementService.Catalog.Variants(slot).FirstOrDefault(o=>o.Id==ReplacementService.Catalog.Selected(slot));
        ReplacementUi.Caption(source,(selected==null?T("元の動作","Original","原版","原版","원본"):VariantName(selected))+"  >");
        var captured=source;ReplacementUi.Bind(source,()=>OpenSource(slot,captured));
        ReplacementUi.Bind(row,()=>OpenSource(slot,captured));
    }
    internal void Refresh()
    {
        if(disposed)return;
        model.Synchronize(ReplacementService.Catalog);
        bool changed=version!=ReplacementService.SettingsVersion||stateVersion!=model.Revision
            ||language!=ActionNames.Language||enabled!=ReplacementService.Enabled;
        if(!changed)return;
        refreshing=true;
        try
        {
            var catalog=ReplacementService.Catalog;
            float packPosition=packs.verticalNormalizedPosition,itemPosition=items.verticalNormalizedPosition;
            foreach(var node in packRows)node.gameObject.SetActive(false);
            foreach(var node in slotRows)node.gameObject.SetActive(false);
            foreach(var node in variantRows)node.gameObject.SetActive(false);
            sidebar.SetActive(!model.BySlot);packHeader.SetActive(!model.BySlot);slotHeader.SetActive(model.BySlot);
            int index=0;
            if(model.BySlot)
                foreach(var slot in model.VisibleSlots(catalog).OrderBy(ActionNames.Clip,StringComparer.CurrentCulture))SlotRow(index++,slot);
            else
            {
                var names=catalog.Packs;for(int i=0;i<names.Length;i++)PackRow(i,names[i]);
                foreach(var option in model.VisibleOptions(catalog))VariantRow(index++,option);
            }
            empty.gameObject.SetActive(index==0);
            empty.text=catalog.Options.Count==0
                ?T("シーンに入ると候補が表示されます。","Enter a scene to discover replacement candidates.","进入场景后会显示可替换动作。","進入場景後會顯示可替換動作。","장면에 들어가면 교체 후보가 표시됩니다.")
                :T("条件に一致する動作がありません。","No animations match this filter.","没有符合筛选条件的动作。","沒有符合篩選條件的動作。","필터에 맞는 동작이 없습니다.");
            title.text=T("動作の置換","Animation replacements","动作替换","動作替換","동작 교체");
            ReplacementUi.Caption(backButton,T("戻る","Back","返回","返回","뒤로"));
            ReplacementUi.Caption(packMode,T("パック別","By pack","按资源包","按資源包","팩별"));
            ReplacementUi.Caption(slotMode,T("元の動作別","By animation","按原动作","按原動作","동작별"));
            ReplacementUi.ColorButton(packMode,!model.BySlot);ReplacementUi.ColorButton(slotMode,model.BySlot);
            onlyToggle.isOn=model.OnlySelected;enabledToggle.isOn=ReplacementService.Enabled;
            ReplacementUi.Caption(onlyToggle,T("選択済みのみ","Selected only","仅已选择","僅已選取","선택 항목만"));
            ReplacementUi.Caption(enabledToggle,T("置換を有効化","Enable replacements","启用动作替换","啟用動作替換","동작 교체 활성화"));
            int selectedCount=catalog.Slots().Count(slot=>catalog.Variants(slot).Any(o=>o.Id==catalog.Selected(slot)));
            status.text=(ReplacementService.Enabled
                ?T("設定を閉じ、直立待機に戻ると適用","Applied after closing settings and returning to idle","关闭设置并恢复站立待机后应用","關閉設定並恢復站立待機後套用","설정을 닫고 직립 대기로 돌아오면 적용")
                :T("置換は無効・選択は保存されます","Replacements off; selections are saved","总开关关闭，选择仍会保存","總開關關閉，選擇仍會儲存","교체 꺼짐; 선택은 저장됩니다"))
                +" · "+string.Format(T("{0}/{1} 選択","{0}/{1} selected","已选择 {0}/{1} 项","已選取 {0}/{1} 項","{0}/{1} 선택"),selectedCount,catalog.Slots().Length);
            sidebar.transform.Find("PackLabel").GetComponent<TextMeshProUGUI>().text=T("アニメーションパック","Animation packs","资源包","資源包","애니메이션 팩");
            packTitle.text=string.Format(T("動作 {0} 件","{0} animations","包内动作 {0} 项","包內動作 {0} 項","동작 {0}개"),catalog.Slots(model.SelectedPack).Length);
            ReplacementUi.Caption(apply,T("適用","Apply pack","应用本包","套用本包","팩 적용"));
            ReplacementUi.Caption(clear,T("解除","Clear pack","清除此包","清除此包","팩 해제"));
            apply.interactable=clear.interactable=model.SelectedPack!=null;
            slotHeader.transform.Find("ActionColumn").GetComponent<TextMeshProUGUI>().text=T("元の動作","Native animation","原动作","原動作","기본 동작");
            slotHeader.transform.Find("SourceColumn").GetComponent<TextMeshProUGUI>().text=T("現在のソース","Current source","当前来源","目前來源","현재 소스");
            if(popupSlot!=null)
            {
                if(catalog.Variants(popupSlot).Length==0)CloseSource();else BuildChoices();
            }
            if(language!=ActionNames.Language)ui.Fonts(root);
            LayoutRebuilder.ForceRebuildLayoutImmediate(ReplacementUi.Rect(root));
            packs.verticalNormalizedPosition=resetScroll?1:packPosition;items.verticalNormalizedPosition=resetScroll?1:itemPosition;
            resetScroll=false;version=ReplacementService.SettingsVersion;stateVersion=model.Revision;
            language=ActionNames.Language;enabled=ReplacementService.Enabled;
            var focused=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if(focused==null||!focused.activeInHierarchy)ReplacementUi.Focus((model.BySlot?slotMode:packMode).gameObject);
        }
        finally{refreshing=false;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(root!=null){root.SetActive(false);UnityEngine.Object.Destroy(root);}
        if(original!=null)original.gameObject.SetActive(true);
    }
}
