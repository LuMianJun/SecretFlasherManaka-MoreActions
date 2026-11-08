using System;
using System.Linq;

namespace SFM.Actions;

internal sealed class ReplacementBrowserModel
{
    internal bool BySlot { get; private set; }
    internal bool OnlySelected { get; private set; }
    internal string SelectedPack { get; private set; }
    internal int Revision { get; private set; }
    internal void Mode(bool bySlot){if(BySlot==bySlot)return;BySlot=bySlot;Revision++;}
    internal void Filter(bool selected){if(OnlySelected==selected)return;OnlySelected=selected;Revision++;}
    internal void Pack(string pack){if(SelectedPack==pack)return;SelectedPack=pack;Revision++;}
    internal void Synchronize(ReplacementCatalog catalog)
    {
        var packs=catalog.Packs;
        if(SelectedPack!=null&&packs.Contains(SelectedPack))return;
        Pack(packs.FirstOrDefault(p=>catalog.EnabledInPack(p)>0)??packs.FirstOrDefault());
    }
    internal ReplacementOption[] VisibleOptions(ReplacementCatalog catalog)=>catalog.Options
        .Where(o=>o.Pack==SelectedPack&&(!OnlySelected||catalog.Selected(o.Slot)==o.Id)).ToArray();
    // Slot browsing is global. A remembered pack is only used when returning
    // to pack mode; it must never silently filter the full-width slot list.
    internal string[] VisibleSlots(ReplacementCatalog catalog)=>catalog.Slots()
        .Where(slot=>!OnlySelected||catalog.Variants(slot).Any(o=>catalog.Selected(slot)==o.Id)).ToArray();
}
