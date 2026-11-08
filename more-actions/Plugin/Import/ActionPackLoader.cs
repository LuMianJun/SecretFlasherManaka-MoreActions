using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SFM.Actions;

internal static class ActionPackLoader
{
    internal static int Load(string directory, string providerId)
    {
        if (!Directory.Exists(directory)) return 0;
        int count = 0;
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

}
