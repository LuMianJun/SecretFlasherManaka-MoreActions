using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SFM.Actions;

internal static class UnityActionOverrides
{
    internal static string Read(string bundle)
    {
        string exact=bundle+".action.json";
        string shortName=Path.Combine(Path.GetDirectoryName(bundle),Path.GetFileNameWithoutExtension(bundle)+".action.json");
        string file=File.Exists(exact)?exact:File.Exists(shortName)?shortName:null;
        if(file==null)return null;
        string json=File.ReadAllText(file);
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Action options must be an object");
        foreach(var field in doc.RootElement.EnumerateObject())
            if(field.Name.Equals("bundle",StringComparison.OrdinalIgnoreCase)||field.Name.Equals("clipAsset",StringComparison.OrdinalIgnoreCase))
                return null; // Explicit resource manifests retain their existing loading path.
        return json;
    }

    internal static ActionDefinition Apply(ActionDefinition defaults,string json)
    {
        using var overlay=JsonDocument.Parse(json);
        var options=new JsonSerializerOptions{DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
        var original=JsonSerializer.SerializeToElement(defaults,options);
        var merged=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach(var field in original.EnumerateObject())merged[field.Name]=field.Value;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var field in overlay.RootElement.EnumerateObject())
        {
            if(!seen.Add(field.Name))throw new ArgumentException("Duplicate action option: "+field.Name);
            merged[field.Name]=field.Value;
        }
        return ActionConfiguration.Parse(JsonSerializer.Serialize(merged));
    }
}
