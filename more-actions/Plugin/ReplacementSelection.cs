using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Seleka2.Actions;

// One chosen resource ID per original clip. UI checkboxes derive from this
// mapping, so conflicting variants cannot both be enabled by load order.
internal sealed class ReplacementSelection
{
    readonly Dictionary<string,string> selected = new(StringComparer.Ordinal);
    internal int Revision { get; private set; }
    internal string Get(string slot) => selected.TryGetValue(slot, out var id) ? id : null;
    internal void Set(string slot,string id,bool enabled)
    {
        if (enabled) { if(Get(slot)==id)return; selected[slot]=id; }
        else { if(Get(slot)!=id)return; selected.Remove(slot); }
        Revision++;
    }
    internal string Serialize() => JsonSerializer.Serialize(selected);
    internal void Load(string json)
    {
        var data=JsonSerializer.Deserialize<Dictionary<string,string>>(json);
        selected.Clear();
        if(data!=null)foreach(var pair in data)
            if(!string.IsNullOrWhiteSpace(pair.Key)&&!string.IsNullOrWhiteSpace(pair.Value))selected[pair.Key]=pair.Value;
        Revision++;
    }
}
