using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace SFM.Actions;

// Imports AnimReplacer-style bundles from the Actions collection. Every clip
// becomes an action; clips named after a native motion double as replacement
// resources (hidden from the ring, matched by name in the replacement page).
internal static class ActionBundleImport
{
    static string root, provider;
    static Task<string[]> discovery;
    static Queue<string> pending;
    static string file;
    static AssetBundleCreateRequest opening;
    static AssetBundle bundle;
    static Queue<string> assets;
    static string asset;
    static AssetBundleRequest loading;
    static readonly List<AnimationClip> clips = new();
    static int registered;
    static bool announced;
    internal static int Revision { get; private set; }

    internal static void Begin(string directory, string providerId)
    {
        root = Path.GetFullPath(directory); provider = providerId;
        discovery = Task.Run(() => Discover(root));
    }

    static string[] Discover(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var used = UsedBundles(root);
        var found = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(path);
            if (used.Contains(full)) continue;
            try
            {
                using var stream = File.OpenRead(full);
                var magic = new byte[8];
                if (stream.Read(magic, 0, 8) != 8) continue;
                if (magic[0] != (byte)'U' || magic[1] != (byte)'n' || magic[2] != (byte)'i' || magic[3] != (byte)'t' ||
                    magic[4] != (byte)'y' || magic[5] != (byte)'F' || magic[6] != (byte)'S') continue;
                found.Add(full);
            }
            catch { }
        }
        return found.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    // A bundle referenced by a JSON action pack belongs to that pack; importing
    // it again would duplicate every clip.
    static HashSet<string> UsedBundles(string root)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in Directory.GetFiles(root, "*.action.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
                string relative = null;
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals("bundle", StringComparison.OrdinalIgnoreCase))
                        relative = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(relative)) continue;
                used.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(json), relative)));
            }
            catch { }
        }
        return used;
    }

    static string Id(string relative, string asset)
        => ("bundle:" + relative.Replace('\\', '/') + ":" + asset).Replace(',', '_');

    internal static void Tick()
    {
        try
        {
            if (discovery != null)
            {
                if (!discovery.IsCompleted) return;
                pending = new Queue<string>(discovery.GetAwaiter().GetResult()); discovery = null;
                Plugin.LogSource.LogInfo("ACTION_BUNDLES_DISCOVERED bundles=" + pending.Count);
            }
            if (pending == null) return;
            if (opening != null)
            {
                if (!opening.isDone) return;
                bundle = opening.assetBundle; opening = null;
                if (bundle == null) throw new InvalidDataException("Bundle could not load; check duplicate internal bundle names");
                assets = new Queue<string>(bundle.GetAllAssetNames().Where(n => n.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal));
            }
            if (loading != null)
            {
                if (!loading.isDone) return;
                var value = loading.asset; loading = null;
                var clip = value == null ? null : value.TryCast<AnimationClip>();
                if (clip == null || !float.IsFinite(clip.length) || clip.length < 0) throw new InvalidDataException("Invalid animation clip");
                clip.hideFlags = HideFlags.HideAndDontSave;
                string relative = Path.GetRelativePath(root, file);
                var source = new AnimatorActionSource(clip, relative);
                var definition = new ActionDefinition
                {
                    Id = UnityActionIdentity.ForClip(root,Path.GetDirectoryName(file),clip.name),
                    DisplayName = GameAnimationNames.Label(clip.name),
                    Description = "Action bundle / " + relative + " / " + clip.name,
                    ShowInRing = !GameAnimationNames.IsOriginal(clip.name),
                    Loop = true
                };
                try { ActionRegistry.RegisterRuntime(definition, source, Path.GetDirectoryName(file), provider); }
                catch
                {
                    var owner=(ActionRegistry.Get(definition.Id)?.RuntimeSource as IActionClipSource)?.Clip;
                    if(owner!=clip)UnityEngine.Object.Destroy(clip);
                    throw;
                }
                ActionRegistry.RegisterAlias(Id(relative,asset),definition.Id);
                clips.Add(clip); registered++; Revision++;
                Plugin.LogSource.LogInfo("ACTION_BUNDLE_CLIP_READY id=" + definition.Id + " clip=" + clip.name + " seconds=" + source.Duration + " humanoid=" + clip.isHumanMotion);
            }
            if (bundle != null)
            {
                if (assets.Count > 0)
                {
                    asset = assets.Dequeue();
                    loading = bundle.LoadAssetAsync<AnimationClip>(asset);
                    return;
                }
                bundle.Unload(false); bundle = null; assets = null;
            }
            if (pending.Count > 0) { file = pending.Dequeue(); opening = AssetBundle.LoadFromFileAsync(file); return; }
            if (!announced)
            {
                announced = true;
                int extra = ActionRegistry.GetAll().Count(d => d.ProviderId == provider && d.ShowInRing);
                Plugin.LogSource.LogInfo("ACTION_BUNDLES_READY actions=" + registered + " ring=" + extra + " replacements=" + (registered - extra) + " playback=avatar-playable");
            }
        }
        catch (Exception e)
        {
            Plugin.LogSource.LogWarning("ACTION_BUNDLE_IMPORT_FAILED file=" + file + " asset=" + asset + " " + e);
            try { bundle?.Unload(false); } catch { }
            bundle = null; assets = null; loading = null; opening = null;
            if (discovery != null) { discovery = null; pending = new Queue<string>(); }
        }
    }

    internal static void Clear()
    {
        discovery = null; pending = null;
        if (provider != null) ActionRegistry.UnregisterProvider(provider);
        try { bundle?.Unload(false); }
        finally
        {
            bundle = null; assets = null; loading = null;
            if (opening != null && opening.isDone && opening.assetBundle != null) opening.assetBundle.Unload(true);
            opening = null;
            foreach (var clip in clips) if (clip != null) UnityEngine.Object.Destroy(clip);
            clips.Clear(); registered = 0; announced = false; Revision++;
        }
    }
}
