using System;
using System.Collections.Generic;
using System.Linq;

namespace SFM.Actions;

internal sealed record ReplacementOption(string Id,string Slot,string Pack,string Source);

// Both browser views edit the same slot-to-resource selection. Bulk clearing
// only withdraws selections owned by that pack; other packs remain untouched.
internal sealed class ReplacementCatalog
{
    readonly ReplacementSelection selection;
    ReplacementOption[] options=Array.Empty<ReplacementOption>();
    internal IReadOnlyList<ReplacementOption> Options=>options;
    internal int Revision { get; private set; }
    internal ReplacementCatalog(ReplacementSelection selection)=>this.selection=selection;
    internal string Selected(string slot)=>selection.Get(slot);
    internal string[] Packs=>options.Select(o=>o.Pack).Distinct(StringComparer.Ordinal).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
    internal string[] Slots(string pack=null)=>options.Where(o=>pack==null||o.Pack==pack).Select(o=>o.Slot).Distinct(StringComparer.Ordinal).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
    internal ReplacementOption[] Variants(string slot)=>options.Where(o=>o.Slot==slot).ToArray();
    internal int EnabledInPack(string pack)=>options.Count(o=>o.Pack==pack&&Selected(o.Slot)==o.Id);
    internal void Update(IEnumerable<ReplacementOption> values)
    {
        var next=values.OrderBy(o=>o.Pack,StringComparer.Ordinal).ThenBy(o=>o.Slot,StringComparer.Ordinal)
            .ThenBy(o=>o.Source,StringComparer.Ordinal).ThenBy(o=>o.Id,StringComparer.Ordinal).ToArray();
        if(options.SequenceEqual(next))return;
        options=next;Revision++;
    }
    internal bool Choose(string id,bool enabled)
    {
        var option=options.FirstOrDefault(o=>o.Id==id);
        if(option==null)return false;
        int before=selection.Revision;
        selection.Set(option.Slot,id,enabled);
        return before!=selection.Revision;
    }
    internal bool Original(string slot)
    {
        var current=Selected(slot);
        if(current==null)return false;
        selection.Set(slot,current,false);return true;
    }
    internal int ApplyPack(string pack)
    {
        int changed=0;
        foreach(var slot in options.Where(o=>o.Pack==pack).GroupBy(o=>o.Slot,StringComparer.Ordinal))
        {
            // Retain a deliberate within-pack variant; otherwise use the first
            // displayed source, with deterministic source/ID ordering.
            var option=slot.FirstOrDefault(o=>o.Id==Selected(o.Slot))??slot.First();
            if(Choose(option.Id,true))changed++;
        }
        return changed;
    }
    internal int ClearPack(string pack)
    {
        int changed=0;
        foreach(var option in options.Where(o=>o.Pack==pack))if(Selected(option.Slot)==option.Id&&Choose(option.Id,false))changed++;
        return changed;
    }
}
