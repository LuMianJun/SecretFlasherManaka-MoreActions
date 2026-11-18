using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace SFM.Actions;

internal static class ActionPackLoader
{
    sealed record Pack(string File, ActionDefinition Definition, string Bundle);

    internal static int Load(string directory, string providerId)
    {
        if (!Directory.Exists(directory)) return 0;
        var packs = new List<Pack>();
        foreach (var file in Directory.GetFiles(directory, "*.action.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                string json = File.ReadAllText(file);
                using var doc=JsonDocument.Parse(json);
                if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Action configuration must be an object");
                if(!doc.RootElement.EnumerateObject().Any(p=>p.Name.Equals("bundle",StringComparison.OrdinalIgnoreCase)||p.Name.Equals("clipAsset",StringComparison.OrdinalIgnoreCase)))
                {
                    ActionConfiguration.ValidateJson(doc.RootElement);
                    continue;
                }
                var definition=ActionConfiguration.Parse(doc.RootElement);
                bool Has(string name)=>doc.RootElement.EnumerateObject().Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
                string clip=UnityActionIdentity.ClipKey(definition.ClipAsset);
                if(!Has("id"))definition.Id=UnityActionIdentity.ForClip(directory,Path.GetDirectoryName(file),clip);
                if(!Has("displayName"))definition.DisplayName=clip;
                var files=ActionDefinitionValidation.Validate(definition,Path.GetDirectoryName(file),null);
                packs.Add(new Pack(file,definition,files.Bundle));
            }
            catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_PACK_REJECTED file=" + Path.GetFileName(file) + " reason=" + e.Message); }
        }

        var ikuOnly = FindIkuOnlyAssets(packs);
        int count = 0;
        foreach (var pack in packs)
        {
            var file = pack.File;
            var definition = pack.Definition;
            if (ikuOnly.TryGetValue(pack.Bundle, out var reserved)
                && reserved.Any(asset => ActionIkuAssets.Matches(asset, definition.ClipAsset)))
            {
                Plugin.LogSource?.LogWarning($"ACTION_IKU_ROLE_CONFLICT id={definition.Id} asset={definition.ClipAsset}; reserved for IKU");
                continue;
            }
            try
            {
                ActionRegistry.Register(definition, Path.GetDirectoryName(file), providerId);
                if(definition.ClipAsset.Contains("/")||definition.ClipAsset.Contains("\\"))
                {
                    var registered=ActionRegistry.Get(definition.Id);
                    ActionRegistry.RegisterAlias(UnityActionIdentity.LegacyBundleId(Path.GetRelativePath(directory,registered.BundlePath),definition.ClipAsset),registered.Id);
                }
                count++;
            }
            catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_PACK_REJECTED file=" + Path.GetFileName(file) + " reason=" + e.Message); }
        }
        return count;
    }

    static Dictionary<string, HashSet<string>> FindIkuOnlyAssets(List<Pack> packs)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in packs.Where(p => !string.IsNullOrWhiteSpace(p.Definition.IkuClipAsset))
                     .GroupBy(p => p.Bundle, StringComparer.OrdinalIgnoreCase))
        {
            AssetBundle bundle = null;
            try
            {
                bundle = AssetBundle.LoadFromFile(group.Key);
                if (bundle == null) throw new InvalidDataException("cannot open bundle");
                var assets = bundle.GetAllAssetNames().ToArray();
                var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pack in group)
                {
                    var definition = pack.Definition;
                    var clip = ActionIkuAssets.Load(bundle, assets, definition.IkuClipAsset, definition.Id, out var path);
                    if (clip == null) continue;
                    definition.ResolvedIkuClipAsset = path;
                    reserved.Add(path);
                }
                result.Add(group.Key, reserved);
            }
            catch (Exception error)
            {
                foreach (var pack in group) ActionIkuAssets.Warn(pack.Definition.Id, pack.Definition.IkuClipAsset, error.Message);
            }
            finally { if (bundle != null) bundle.Unload(true); }
        }
        return result;
    }
}
