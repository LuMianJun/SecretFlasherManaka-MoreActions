using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace SFM.Actions;

internal static class ReplacementService
{
    internal static event Action Changed;
    internal static event Action CatalogChanged;
    static ConfigEntry<bool> enabled;
    static ConfigEntry<string> saved;
    static readonly ReplacementSelection selection=new();
    internal static readonly ReplacementCatalog Catalog=new(selection);
    internal static long SettingsVersion=>((long)Catalog.Revision<<32)|(uint)selection.Revision;
    internal static bool Enabled=>enabled?.Value==true;
    internal static void SetEnabled(bool value){if(enabled!=null)enabled.Value=value;}
    static void Persist(bool changed){if(changed)saved.Value=selection.Serialize();}
    internal static void SelectVariant(string id,bool value)=>Persist(Catalog.Choose(id,value));
    internal static void SelectOriginal(string slot)=>Persist(Catalog.Original(slot));
    internal static void ApplyPack(string pack)=>Persist(Catalog.ApplyPack(pack)>0);
    internal static void ClearPack(string pack)=>Persist(Catalog.ClearPack(pack)>0);
    internal static bool IsConfigured => enabled != null;
    internal static int SelectionRevision => selection.Revision;
    internal static string Selected(string slot) => selection.Get(slot);

    internal static void Configure(ConfigFile config)
    {
        enabled=config.Bind("Replacements","Enabled",false,"Enable selected native animation replacements. Disabling preserves selections.");
        saved=config.Bind("Replacements","Selections","{}","Original clip name to action ID; independent of game saves.");
        LoadSelection();
        saved.SettingChanged+=(_,_)=>LoadSelection();
        enabled.SettingChanged+=(_,_)=>{Changed?.Invoke();};
    }

    static void LoadSelection()
    {try{selection.Load(saved.Value);}catch(Exception e){Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_CONFIG_REJECTED "+e.Message);}Changed?.Invoke();}
    internal static void RefreshCatalog(IEnumerable<string> nativeSlots)
    {
        var slots=new HashSet<string>(nativeSlots,StringComparer.Ordinal);
        Persist(selection.Remap(id=>ActionRegistry.Get(id)?.Id??id));
        var candidates=new List<(ActionDefinition Definition,string Slot,string Group)>();
        foreach(var definition in ActionRegistry.GetAll())
        {
            var name=ReplacementName(definition);
            if(name==null)continue;
            if(slots.Contains(name)){candidates.Add((definition,name,SourceGroup(definition)));continue;}
            // A case-only variant usually means the pack renamed the clip; the
            // slot name and the bundle file name are unrelated.
            var near=slots.FirstOrDefault(k=>string.Equals(k,name,StringComparison.OrdinalIgnoreCase));
            if(near!=null)Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_UNMATCHED id="+definition.Id+" clip="+name+" expected="+near);
        }
        candidates.Sort((a,b)=>string.Compare(a.Group,b.Group,StringComparison.Ordinal)!=0?string.Compare(a.Group,b.Group,StringComparison.Ordinal):string.Compare(a.Slot,b.Slot,StringComparison.Ordinal));
        foreach(var candidate in candidates)
            if(GameAnimationNames.IsOriginal(candidate.Slot))ActionRegistry.SetRingVisibility(candidate.Definition.Id,false);
        Catalog.Update(candidates.Select(c=>new ReplacementOption(c.Definition.Id,c.Slot,c.Group,SourceLabel(c.Definition,c.Slot))));
        CatalogChanged?.Invoke();
    }

    static string SourceGroup(ActionDefinition definition)
    {
        if(definition.RuntimeSource is AnimatorActionSource imported)return string.IsNullOrEmpty(imported.Group)?"Actions":imported.Group;
        var directory=Path.GetDirectoryName(definition.BundlePath??"");
        return string.IsNullOrEmpty(directory)?"Actions":Path.GetFileName(directory);
    }
    static string SourceLabel(ActionDefinition definition,string slot)
    {
        if(definition.RuntimeSource is AnimatorActionSource imported)
        {
            var path=imported.RelativeBundle.Replace('\\','/');
            int slash=path.IndexOf('/');
            return slash<0?path:path.Substring(slash+1);
        }
        return string.IsNullOrEmpty(definition.Bundle)?definition.DisplayName:definition.Bundle;
    }
    static string ReplacementName(ActionDefinition definition)
    {
        if(definition.RuntimeSource is AnimatorActionSource imported)return imported.ClipName;
        try{if(ActionAssets.TryGet(definition,out var loaded)&&loaded?.Clip!=null)return loaded.Clip.name;}catch{}
        var asset=Path.GetFileNameWithoutExtension(definition.ClipAsset);
        return string.IsNullOrEmpty(asset)?null:asset;
    }
    internal static AnimationClip ResolveClip(string id,string slot)
    {
        var definition=id==null?null:ActionRegistry.Get(id);
        if(definition==null)return null;
        if(definition.RuntimeSource is AnimatorActionSource imported)
            return string.Equals(imported.ClipName,slot,StringComparison.Ordinal)?imported.Clip:null;
        try
        {
            if(!ActionAssets.TryGet(definition,out var loaded)||loaded?.Clip==null)return null;
            return string.Equals(loaded.Clip.name,slot,StringComparison.Ordinal)?loaded.Clip:null;
        }
        catch(Exception e){Plugin.LogSource.LogWarning("ACTION_REPLACEMENTS_ASSET "+definition.Id+" "+e.Message);return null;}
    }
}
