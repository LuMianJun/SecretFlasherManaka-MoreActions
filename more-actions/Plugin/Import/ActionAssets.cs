using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace SFM.Actions;

internal static class ActionAssets
{
    sealed class Entry
    {
        internal ActionDefinition Definition;
        internal LoadedAction Assets;
        internal WaveAudio.Loader Wave;
        internal NativeAudioLoad NativeAudio;
        internal Exception Error;
        internal bool Ready;
        internal long MaxUploadMs;
        internal long MaxWaveStepTicks;
        internal string WaveBackend;
        internal readonly Stopwatch Timer = Stopwatch.StartNew();
        internal void Dispose()
        {
            Wave?.Dispose(); Wave = null;
            NativeAudio?.Dispose(); NativeAudio = null;
            if (Assets?.Audio != null) UnityEngine.Object.Destroy(Assets.Audio);
            if (Assets?.Clip != null) UnityEngine.Object.Destroy(Assets.Clip);
            if (Assets?.IkuClip != null) UnityEngine.Object.Destroy(Assets.IkuClip);
            Assets = null;
        }
    }
    static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    static int revision = -1;
    internal static int ReadyRevision { get; private set; }
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
                if (NativeAudioLoad.IsMp3(definition.AudioPath))
                {
                    entry.NativeAudio ??= new NativeAudioLoad(definition.AudioPath, AudioType.MPEG);
                    if (!entry.NativeAudio.Step()) return;
                    entry.Assets.Audio = entry.NativeAudio.TakeClip();
                    entry.NativeAudio.Dispose(); entry.NativeAudio = null;
                }
                else
                {
                    long started = Stopwatch.GetTimestamp();
                    entry.Wave ??= new WaveAudio.Loader(definition.AudioPath);
                    bool ready = entry.Wave.Step();
                    entry.MaxWaveStepTicks = Math.Max(entry.MaxWaveStepTicks, Stopwatch.GetTimestamp() - started);
                    if (!ready) return;
                    entry.Assets.Audio = entry.Wave.TakeClip();
                    entry.WaveBackend = entry.Wave.Backend;
                    entry.MaxUploadMs = entry.Wave.MaxUploadMs;
                    if (entry.Wave.FallbackReason != null)
                        Plugin.LogSource.LogWarning("ACTION_WAV_NATIVE_FALLBACK id=" + definition.Id + " reason=" + entry.Wave.FallbackReason);
                    entry.Wave.Dispose(); entry.Wave = null;
                }
            }
            if (entry.Assets.Audio != null && entry.Assets.Audio.loadState == AudioDataLoadState.Failed) throw new Exception("Audio data failed to load");
            if (entry.Assets.Audio != null && entry.Assets.Audio.loadState != AudioDataLoadState.Loaded) return;
            entry.Ready = true; ReadyRevision++;
            if (requested == definition.Id) requested = null;
            var audio = entry.Assets.Audio;
            var format = audio == null ? "none" : string.IsNullOrEmpty(definition.AudioPath) ? "bundle" : NativeAudioLoad.IsMp3(definition.AudioPath) ? "mp3" : "wav";
            string waveTiming = entry.WaveBackend == null ? "" : FormattableString.Invariant(
                $" audioLoader={entry.WaveBackend} audioMainStepMaxMs={1000d * entry.MaxWaveStepTicks / Stopwatch.Frequency:F3} audioSamples={audio.samples} audioRate={audio.frequency} audioChannels={audio.channels}");
            Plugin.LogSource.LogInfo("ACTION_ASSETS_READY id=" + definition.Id + " motion=" + entry.Assets.Duration + " audio=" + (audio == null ? 0 : audio.length) + " prepareMs=" + entry.Timer.ElapsedMilliseconds + " uploadStepMaxMs=" + entry.MaxUploadMs + " audioFormat=" + format + waveTiming);
        }
        catch (Exception e)
        {
            entry.Dispose(); entry.Error = e;
            Plugin.LogSource.LogWarning("ACTION_ASSETS_FAILED id=" + entry.Definition.Id + " " + e);
        }
    }

    static LoadedAction LoadBundleAssets(ActionDefinition definition)
    {
        AssetBundle bundle = null; AnimationClip clip = null; AnimationClip iku = null; AudioClip audio = null;
        try
        {
            if (definition.RuntimeSource == null)
            {
                bundle = AssetBundle.LoadFromFile(definition.BundlePath);
                if (bundle == null) throw new Exception("Cannot load action bundle");
                clip = bundle.LoadAsset<AnimationClip>(definition.ClipAsset);
                if (clip == null || clip.length <= 0) throw new Exception("Animation clip missing or empty");
                if (definition.ResolvedIkuClipAsset != null)
                {
                    iku = ActionIkuAssets.Load(bundle, new[] { definition.ResolvedIkuClipAsset },
                        definition.ResolvedIkuClipAsset, definition.Id, out _);
                    if (iku != null) iku.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            if (!string.IsNullOrEmpty(definition.AudioAsset))
            {
                audio = bundle.LoadAsset<AudioClip>(definition.AudioAsset);
                if (audio == null || audio.length <= 0) throw new Exception("Audio clip missing or empty");
                if (audio.loadState != AudioDataLoadState.Loaded && !audio.LoadAudioData()) throw new Exception("Audio data failed to load");
            }
            if (clip != null) clip.hideFlags = HideFlags.HideAndDontSave;
            if (audio != null) audio.hideFlags = HideFlags.HideAndDontSave;
            return new LoadedAction { Definition = definition, Clip = clip, IkuClip = iku, Audio = audio };
        }
        catch { if (audio != null) UnityEngine.Object.Destroy(audio); if (clip != null) UnityEngine.Object.Destroy(clip); if (iku != null) UnityEngine.Object.Destroy(iku); throw; }
        finally { if (bundle != null) bundle.Unload(false); }
    }

    internal static void Clear()
    {
        foreach (var entry in entries.Values) entry.Dispose(); entries.Clear(); revision = -1; requested = null;
    }
}
