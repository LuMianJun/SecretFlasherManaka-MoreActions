using System;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Scripts.Sound;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SFM.Actions;

// Standard uGUI only. Game resources supply fonts and optional checkbox art,
// never a settings/graphics controller or its serialized layout constraints.
internal sealed class ReplacementUi
{
    internal static readonly Color Background=new(.105f,.115f,.135f,.98f);
    internal static readonly Color Row=new(.165f,.18f,.21f,1);
    internal static readonly Color Selected=new(.22f,.31f,.41f,1);
    internal static readonly Color Accent=new(.58f,.77f,.94f,1);
    internal static readonly Color Secondary=new(.70f,.74f,.80f,1);
    readonly TMP_FontAsset fallback;
    readonly Sprite checkSprite;
    readonly Color checkColor;
    internal ReplacementUi(TMP_FontAsset fallback,Sprite checkSprite,Color checkColor)
    {this.fallback=fallback;this.checkSprite=checkSprite;this.checkColor=checkColor;}
    internal TMP_FontAsset Font
    {
        get
        {
            var assets=AssetReferencer.Instance;
            var font=assets==null?null:ActionNames.Language switch {2=>assets.fontSc,3=>assets.fontTc,4=>assets.fontKr,_=>assets.defaultFont};
            return font??fallback;
        }
    }
    internal GameObject Node(string name,Transform parent)
    {
        var node=new GameObject(name);node.layer=parent.gameObject.layer;
        node.AddComponent<RectTransform>().SetParent(parent,false);return node;
    }
    internal static RectTransform Rect(GameObject node)=>node.GetComponent<RectTransform>();
    internal static void Box(RectTransform rect,float x0,float y0,float x1,float y1,float left=0,float bottom=0,float right=0,float top=0)
    {
        rect.anchorMin=new Vector2(x0,y0);rect.anchorMax=new Vector2(x1,y1);
        rect.offsetMin=new Vector2(left,bottom);rect.offsetMax=new Vector2(right,top);rect.localScale=new Vector3(1,1,1);
    }
    internal static LayoutElement Size(GameObject node,float height=-1,float width=-1)
    {
        var item=node.GetComponent<LayoutElement>()??node.AddComponent<LayoutElement>();
        item.minHeight=height<0?0:height;item.preferredHeight=height;item.flexibleHeight=height<0?1:0;
        item.minWidth=width<0?0:width;item.preferredWidth=width;item.flexibleWidth=width<0?1:0;return item;
    }
    internal static void Vertical(GameObject node,int padding=0,float spacing=8)
    {
        var group=node.AddComponent<VerticalLayoutGroup>();var inset=group.padding;
        inset.left=inset.right=inset.top=inset.bottom=padding;group.padding=inset;
        group.spacing=spacing;group.childControlWidth=true;group.childForceExpandWidth=true;
        group.childControlHeight=true;group.childForceExpandHeight=false;
    }
    internal static void Horizontal(GameObject node,float spacing=10)
    {
        var group=node.AddComponent<HorizontalLayoutGroup>();group.spacing=spacing;
        group.childControlWidth=true;group.childForceExpandWidth=false;
        group.childControlHeight=true;group.childForceExpandHeight=true;
    }
    internal static Image Paint(GameObject node,Color color,bool raycast=false)
    {var image=node.AddComponent<Image>();image.color=color;image.raycastTarget=raycast;return image;}
    internal TextMeshProUGUI Label(Transform parent,string name,float size=22,bool wrap=false)
    {
        var node=Node(name,parent);var text=node.AddComponent<TextMeshProUGUI>();
        text.font=Font;text.enableAutoSizing=false;text.fontSize=size;text.fontSizeMin=size;text.fontSizeMax=size;
        text.enableWordWrapping=wrap;text.overflowMode=TextOverflowModes.Ellipsis;text.richText=false;
        text.color=Color.white;text.alignment=TextAlignmentOptions.MidlineLeft;text.raycastTarget=false;return text;
    }
    internal static void ColorButton(Button button,bool selected=false)
    {
        var colors=button.colors;colors.normalColor=selected?Selected:Row;
        colors.highlightedColor=new Color(.29f,.34f,.42f,1);colors.selectedColor=new Color(.27f,.34f,.43f,1);
        colors.pressedColor=new Color(.18f,.25f,.33f,1);colors.disabledColor=new Color(.12f,.13f,.15f,.7f);
        colors.colorMultiplier=1;colors.fadeDuration=.08f;button.colors=colors;
    }
    internal static void Sound(bool back=false)=>UiSoundPlayer.Instance?.PlayOneShotAudioData((InGameSoundManager.SeType)(back?50:49),-1f,false);
    internal Button Button(Transform parent,string name,Action click,float size=22,bool back=false)
    {
        var node=Node(name,parent);var image=Paint(node,Color.white,true);var button=node.AddComponent<Button>();
        button.targetGraphic=image;ColorButton(button);
        var text=Label(node.transform,"Label",size);text.alignment=TextAlignmentOptions.Center;
        Box(Rect(text.gameObject),0,0,1,1,12,5,-12,-5);
        Bind(button,click,back);return button;
    }
    internal static void Bind(Button button,Action click,bool back=false)
    {
        button.onClick=new Button.ButtonClickedEvent();
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(()=>{Sound(back);click();})));
    }
    internal static void Bind(Toggle toggle,Action<bool> change)
    {
        toggle.onValueChanged=new Toggle.ToggleEvent();
        toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(change)));
    }
    internal static void Caption(Button button,string value)=>button.transform.Find("Label").GetComponent<TextMeshProUGUI>().text=value;
    internal Toggle Toggle(Transform parent,string name,Action<bool> change)
    {
        var node=Node(name,parent);var image=Paint(node,Color.white,true);var toggle=node.AddComponent<Toggle>();toggle.targetGraphic=image;
        var colors=toggle.colors;colors.normalColor=Row;colors.highlightedColor=new Color(.29f,.34f,.42f,1);
        colors.selectedColor=new Color(.27f,.34f,.43f,1);colors.pressedColor=Selected;colors.colorMultiplier=1;toggle.colors=colors;
        var label=Label(node.transform,"Label",22);Box(Rect(label.gameObject),0,0,1,1,14,8,-54,-8);
        var box=Node("Box",node.transform);Paint(box,new Color(.90f,.92f,.95f,1));
        var rect=Rect(box);rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);
        rect.sizeDelta=new Vector2(28,28);rect.anchoredPosition=new Vector2(-14,0);
        var mark=Node("Check",box.transform);Box(Rect(mark),0,0,1,1,4,4,-4,-4);
        var graphic=Paint(mark,checkSprite==null?Selected:checkColor);graphic.sprite=checkSprite;toggle.graphic=graphic;
        Bind(toggle,change);return toggle;
    }
    internal static void Caption(Toggle toggle,string value)=>toggle.transform.Find("Label").GetComponent<TextMeshProUGUI>().text=value;
    internal ScrollRect Scroll(Transform parent,string name)
    {
        var node=Node(name,parent);Paint(node,new Color(.075f,.085f,.105f,.9f),true);var scroll=node.AddComponent<ScrollRect>();
        var viewport=Node("Viewport",node.transform);Box(Rect(viewport),0,0,1,1,0,0,-14,0);Paint(viewport,Color.clear,true);viewport.AddComponent<RectMask2D>();
        var content=Node("Content",viewport.transform);var rect=Rect(content);rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);
        rect.pivot=new Vector2(.5f,1);rect.sizeDelta=Vector2.zero;Vertical(content,4,6);
        content.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport=Rect(viewport);scroll.content=rect;scroll.horizontal=false;scroll.vertical=true;scroll.inertia=true;
        scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
        var bar=Node("Scrollbar",node.transform);Box(Rect(bar),1,0,1,1,-10,3,0,-3);Paint(bar,new Color(.12f,.14f,.17f,1));
        var handle=Node("Handle",bar.transform);Box(Rect(handle),0,0,1,1,1,0,-1,0);var handleImage=Paint(handle,new Color(.52f,.57f,.65f,1),true);
        var scrollbar=bar.AddComponent<Scrollbar>();scrollbar.handleRect=Rect(handle);scrollbar.targetGraphic=handleImage;
        scrollbar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=scrollbar;
        return scroll;
    }
    internal void Fonts(GameObject root)
    {var font=Font;if(font!=null)foreach(var text in root.GetComponentsInChildren<TextMeshProUGUI>(true))text.font=font;}
    internal static void Focus(GameObject node){if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(node);}
}
