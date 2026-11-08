using System;
using BepInEx.Configuration;
using Seleka2.ModSettings;

namespace Seleka2.Actions;

// Optional providers register into the same native page. Config ownership
// stays with the provider; moving UI never renames existing configuration keys.
public static class ActionSettingsApi
{
    static bool mmdHeader;
    internal static void Initialize()
    {
        ModOptions.Dedicated=true;
        ModOptions.PageTitle=()=>ActionNames.Text("追加アクション","More Actions","更多动作","更多動作","추가 동작");
        ModOptions.Header("actions.section","00",()=>ActionNames.Text("再生設定","Playback","播放设置","播放設定","재생 설정"));
    }
    public static void Toggle(string id,string chinese,string english,ConfigEntry<bool> entry,string helpChinese,string helpEnglish)
    {
        bool mmd=id.StartsWith("mmd.",StringComparison.Ordinal);
        if(mmd&&!mmdHeader){mmdHeader=true;ModOptions.Header("mmd.section","10",()=>"MMD");}
        ModOptions.DynamicToggle(id,mmd?"10":"00",id,
            ()=>Name(id,chinese,english),()=>entry.Value,v=>{entry.Value=v;ModOptions.Refresh();},()=>Help(id,helpChinese,helpEnglish));
    }
    public static void Number(string id,string chinese,string english,ConfigEntry<float> entry,int min,int max,float unit,string suffix,string helpChinese,string helpEnglish)
        => ModOptions.DynamicNumber(id,"00",()=>Name(id,chinese,english),entry,min,max,unit,suffix,()=>Help(id,helpChinese,helpEnglish));
    static string Name(string id,string chinese,string english) => id switch {
        "actions.gameexpressions"=>ActionNames.Text("ゲームの表情を重ねる","Overlay game expressions","叠加原游戏表情","疊加原遊戲表情","게임 표정 겹치기"),
        "actions.facecamera"=>ActionNames.Text("顔カメラ追従","Face camera tracking","面部镜头跟随","面部鏡頭跟隨","얼굴 카메라 추적"),
        "actions.volume"=>ActionNames.Text("アクション音楽の音量","Action music volume","动作音乐音量","動作音樂音量","동작 음악 음량"),
        "mmd.camera"=>ActionNames.Text("配布カメラを使用","Use supplied camera","使用配套镜头","使用配套鏡頭","제공 카메라 사용"),
        _=>ActionNames.Language is 2 or 3?chinese:english
    };
    static string Help(string id,string chinese,string english) => id switch {
        "actions.gameexpressions"=>ActionNames.Text("ゲームの表情に動作の表情を重ねます。動作JSONのkeepGameExpressions=falseが優先されます。","Overlay motion expressions on the game. A motion's keepGameExpressions=false takes priority.","在原游戏表情上叠加动作表情；动作 JSON 的 keepGameExpressions=false 优先。","在原遊戲表情上疊加動作表情；動作 JSON 的 keepGameExpressions=false 優先。","게임 표정에 동작 표정을 겹칩니다. 동작 JSON의 keepGameExpressions=false가 우선합니다."),
        "actions.facecamera"=>ActionNames.Text("動作中にゲームの顔カメラが頭部を追従します。表示はゲームの設定に従います。","Track the head during playback. Camera visibility follows the game's setting.","动作播放时让原版面部摄像机跟随头部，是否显示仍由原版设置控制。","動作播放時讓原版面部攝影機跟隨頭部，是否顯示仍由原版設定控制。","재생 중 원래 얼굴 카메라가 머리를 추적합니다. 표시 여부는 게임 설정을 따릅니다."),
        "actions.volume"=>ActionNames.Text("ゲームの主音量・BGM音量と動作ごとの音量も適用されます。","Also affected by game master/BGM volume and the motion's volume.","同时受游戏主音量、背景音乐音量及单个动作音量影响。","同時受遊戲主音量、背景音樂音量及單個動作音量影響。","게임 전체/BGM 음량과 개별 동작 음량도 적용됩니다."),
        "mmd.camera"=>ActionNames.Text("camera.vmdがある場合に使用します。オフでも動作と音楽は継続します。","Use camera.vmd when provided. Disabling it keeps motion and music playing.","动作包含 camera.vmd 时使用配套镜头，关闭后动作和音乐继续播放。","動作包含 camera.vmd 時使用配套鏡頭，關閉後動作和音樂繼續播放。","camera.vmd가 있으면 사용합니다. 꺼도 동작과 음악은 계속 재생됩니다."),
        _=>ActionNames.Language is 2 or 3?chinese:english
    };
}
