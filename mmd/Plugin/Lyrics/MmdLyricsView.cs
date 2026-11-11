using System;
using System.Runtime.CompilerServices;
using ExposureUnnoticed2.Object3D.IngameManager;
using Il2CppInterop.Runtime.Attributes;
using SFM.Actions;
using TMPro;
using UnityEngine;

namespace SFM.Mmd;

public sealed class MmdLyricsView : MonoBehaviour
{
    public MmdLyricsView(IntPtr pointer) : base(pointer) { }

    MmdLyricTrack track;
    string actionId;
    TextMeshProUGUI label;
    string displayed;
    bool shown;

    [HideFromIl2Cpp]
    internal void Bind()
    {
        ActionPlaybackApi.PlaybackChanged += Playback;
        ActionPlaybackApi.PoseApplied += Apply;
    }

    [HideFromIl2Cpp]
    void Playback(string id, bool active)
    {
        ResetTrack();
        if (!active || !isActiveAndEnabled || ActionPlaybackApi.CurrentProviderId != global::Plugin.Guid) return;
        track = MmdPackPreparation.GetLyrics(id);
        actionId = id;
    }

    [HideFromIl2Cpp]
    void Apply(string id, double _)
    {
        if (track == null || id != actionId) return;
        try { Render(); }
        catch (Exception e)
        {
            ResetTrack();
            if (label != null) Destroy(label.gameObject);
            label = null;
            global::Plugin.LogSource.LogWarning("MMD_LYRICS_DISPLAY_FAILED id=" + id + " reason=" + e.Message);
        }
    }

    // Keep the optional audio API call inside the catch boundary when the
    // player has an older More Actions DLL.
    [HideFromIl2Cpp, MethodImpl(MethodImplOptions.NoInlining)]
    void Render()
    {
        var ui = InGameUiManager.Instance;
        var position = ActionPlaybackApi.AudioPositionSeconds;
        string line = position.HasValue ? track.LineAt(position.Value) : null;
        if (string.IsNullOrEmpty(line) || ui == null || ui.BackLayer == null || ui.IsOpenFade)
        {
            Hide();
            return;
        }
        if (label == null || label.transform.parent != ui.BackLayer)
        {
            if (label != null) Destroy(label.gameObject);
            CreateLabel(ui.BackLayer);
        }
        if (displayed != line)
        {
            label.text = line;
            displayed = line;
        }
        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        if (!shown && label.gameObject.activeInHierarchy)
        {
            shown = true;
            global::Plugin.LogSource.LogInfo(FormattableString.Invariant(
                $"MMD_LYRICS_SHOWN id={actionId} audioSeconds={position.Value:F3}"));
        }
    }

    [HideFromIl2Cpp]
    void CreateLabel(Transform parent)
    {
        var node = new GameObject("SFM MMD Lyrics");
        node.layer = parent.gameObject.layer;
        var rect = node.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(.15f, 0);
        rect.anchorMax = new Vector2(.85f, 0);
        rect.offsetMin = new Vector2(0, 64);
        rect.offsetMax = new Vector2(0, 164);

        label = node.AddComponent<TextMeshProUGUI>();
        var fonts = AssetReferencer.Instance;
        if (fonts != null) label.font = fonts.fontSc ?? fonts.defaultFont;
        label.fontSize = 36;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false;
        label.raycastTarget = false;
        label.color = Color.white;
        displayed = null;
    }

    [HideFromIl2Cpp]
    void Hide()
    {
        if (label != null && label.gameObject.activeSelf) label.gameObject.SetActive(false);
    }

    [HideFromIl2Cpp]
    void ResetTrack()
    {
        track = null;
        actionId = null;
        shown = false;
        Hide();
    }

    public void OnDisable() => ResetTrack();

    public void OnDestroy()
    {
        ActionPlaybackApi.PlaybackChanged -= Playback;
        ActionPlaybackApi.PoseApplied -= Apply;
        if (label != null) Destroy(label.gameObject);
    }
}
