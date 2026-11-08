using SFM.ModSettings;

namespace SFM.Actions;

internal static class ReplacementSettings
{
    static bool registered;
    internal static void Register()
    {
        if(registered)return;
        registered=true;
        ReplacementService.Changed+=ModOptions.Refresh;
        ReplacementService.CatalogChanged+=RefreshCatalog;

        ModOptions.Header("replacement.header","20",()=>ActionNames.Text("元の動作の置換","Native animation replacements","原动作替换","原動作替換","기본 동작 교체"));
        ModOptions.DynamicToggle("replacement.enabled","20","",()=>ActionNames.Text("選択した置換を有効化","Enable selected replacements","启用选中的动作替换","啟用選中的動作替換","선택한 교체 활성화"),()=>ReplacementService.Enabled,v=>ReplacementService.SetEnabled(v),
            ()=>ActionNames.Text("オフにすると元の動作に戻ります。選択は保持されます。置換は待機・直立時のみ適用され、しゃがみ・着座中は立ち上がるまで保留されます。","Turn off to restore native animations without clearing selections. Replacements apply only while idle and standing; crouching or sitting defers them until the player stands up.","关闭后恢复原动作，保留各项选择。替换只在闲置站立状态应用；蹲下、坐下时会延后到站起后再应用。","關閉後恢復原動作，保留各項選擇。替換只在閒置站立狀態套用；蹲下、坐下時會延後到站起後再套用。","끄면 선택을 유지하고 원래 동작으로 돌아갑니다. 교체는 대기·직립 상태에서만 적용되며, 웅크리기·앉기 중에는 일어날 때까지 보류됩니다."));
    }
    static void RefreshCatalog()
    {
        ModOptions.Remove("replacement.variant.");
        ModOptions.Remove("replacement.group.");
        ModOptions.Refresh();
    }

}
