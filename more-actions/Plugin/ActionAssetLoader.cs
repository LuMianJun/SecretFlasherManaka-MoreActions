using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Seleka2.Actions;

internal static class ActionAssets
{
    sealed class Entry
    {
        internal ActionDefinition Definition;
        internal LoadedAction Assets;
        internal Task<WaveAudio.Decoded> Decode;
        internal WaveAudio.Upload Upload;
        internal Exception Error;
        internal bool Ready;
        internal long MaxUploadMs;
        internal readonly System.Diagnostics.Stopwatch Timer = System.Diagnostics.Stopwatch.StartNew();
        internal void Dispose()
        {
            Upload?.Dispose(); Upload = null; Decode = null;
            if (Assets?.Audio != null) UnityEngine.Object.Destroy(Assets.Audio);
            if (Assets?.Clip != null) UnityEngine.Object.Destroy(Assets.Clip);
            Assets = null;
        }
    }
    static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    static readonly SemaphoreSlim decodeGate = new(1);
    static int revision = -1;
    static string requested;

    static void Refresh()
    {
        if (revision == ActionRegistry.Revision) return;
        revision = ActionRegistry.Revision;
        foreach (var id in entries.Keys.ToArray())
            if (!ReferenceEquals(entries[id].Definition, ActionRegistry.Get(id))) { entries[id].Dispose(); entries.Remove(id); }
        foreach (var definition in ActionRegistry.GetAll())
            if (!entries.ContainsKey(definition.Id)) entries.Add(definition.Id, new Entry { Definition = definition });
    }

    internal static bool TryGet(ActionDefinition definition, out LoadedAction loaded)
    {
        Refresh();
        if (!entries.TryGetValue(definition.Id, out var entry) || !ReferenceEquals(entry.Definition, definition)) throw new ArgumentException("Action is no longer registered");
        if (entry.Error != null) throw new InvalidOperationException("Action assets could not be prepared: " + definition.Id, entry.Error);
        loaded = entry.Ready ? entry.Assets : null;
        if (!entry.Ready) requested = definition.Id;
        return entry.Ready;
    }

    // Runs during title/loading too. At most one decode/upload is advanced per
    // frame; a requested action takes priority over speculative preparation.
    internal static void Tick()
    {
        Refresh();
        Entry entry = requested != null && entries.TryGetValue(requested, out var priority) && !priority.Ready && priority.Error == null ? priority : null;
        entry ??= entries.Values.FirstOrDefault(e => !e.Ready && e.Error == null);
        if (entry == null) return;
        try
        {
            var definition = entry.Definition;
            if (entry.Assets == null) entry.Assets = LoadBundleAssets(definition);
            if (!string.IsNullOrEmpty(definition.AudioPath) && entry.Assets.Audio == null)
            {
                if (entry.Decode == null && entry.Upload == null)
                {
                    string path = definition.AudioPath;
                    entry.Decode = Task.Run(async () =>
                    {
                        await decodeGate.WaitAsync().ConfigureAwait(false);
                        try { return WaveAudio.Decode(path); }
                        finally { decodeGate.Release(); }
                    });
                    return;
                }
                if (entry.Upload == null)
                {
                    if (!entry.Decode.IsCompleted) return;
                    entry.Upload = new WaveAudio.Upload(entry.Decode.GetAwaiter().GetResult());
                    entry.Decode = null;
                }
                var uploadTimer = System.Diagnostics.Stopwatch.StartNew();
                bool uploaded = entry.Upload.Step();
                entry.MaxUploadMs = Math.Max(entry.MaxUploadMs, uploadTimer.ElapsedMilliseconds);
                if (!uploaded) return;
                entry.Assets.Audio = entry.Upload.Clip;
                entry.Upload = null; // Clip ownership passes to the ready entry.
            }
            if (entry.Assets.Audio != null && entry.Assets.Audio.loadState == AudioDataLoadState.Failed) throw new Exception("Audio data failed to load");
            if (entry.Assets.Audio != null && entry.Assets.Audio.loadState != AudioDataLoadState.Loaded) return;
            entry.Ready = true;
            if (requested == definition.Id) requested = null;
            Plugin.LogSource.LogInfo("ACTION_ASSETS_READY id=" + definition.Id + " motion=" + entry.Assets.Duration + " audio=" + (entry.Assets.Audio == null ? 0 : entry.Assets.Audio.length) + " prepareMs=" + entry.Timer.ElapsedMilliseconds + " uploadStepMaxMs=" + entry.MaxUploadMs);
        }
        catch (Exception e)
        {
            entry.Dispose(); entry.Error = e;
            Plugin.LogSource.LogWarning("ACTION_ASSETS_FAILED id=" + entry.Definition.Id + " " + e);
        }
    }

    static LoadedAction LoadBundleAssets(ActionDefinition definition)
    {
        AssetBundle bundle = null; AnimationClip clip = null; AudioClip audio = null;
        try
        {
            if (definition.RuntimeSource == null)
            {
                bundle = AssetBundle.LoadFromFile(definition.BundlePath);
                if (bundle == null) throw new Exception("Cannot load action bundle");
                clip = bundle.LoadAsset<AnimationClip>(definition.ClipAsset);
                if (clip == null || clip.length <= 0) throw new Exception("Animation clip missing or empty");
            }
            if (!string.IsNullOrEmpty(definition.AudioAsset))
            {
                audio = bundle.LoadAsset<AudioClip>(definition.AudioAsset);
                if (audio == null || audio.length <= 0) throw new Exception("Audio clip missing or empty");
                if (audio.loadState != AudioDataLoadState.Loaded && !audio.LoadAudioData()) throw new Exception("Audio data failed to load");
            }
            if (clip != null) clip.hideFlags = HideFlags.HideAndDontSave;
            if (audio != null) audio.hideFlags = HideFlags.HideAndDontSave;
            return new LoadedAction { Definition = definition, Clip = clip, Audio = audio };
        }
        catch { if (audio != null) UnityEngine.Object.Destroy(audio); if (clip != null) UnityEngine.Object.Destroy(clip); throw; }
        finally { if (bundle != null) bundle.Unload(false); }
    }

    internal static void Clear()
    {
        foreach (var entry in entries.Values) entry.Dispose(); entries.Clear(); revision = -1; requested = null;
    }
}
