using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SFM.Actions;

public static class ActionRegistry
{
    static readonly Dictionary<string, ActionDefinition> entries = new(StringComparer.Ordinal);
    static readonly Dictionary<string,string> aliases = new(StringComparer.Ordinal);
    public static int Revision { get; private set; }
    public static IReadOnlyList<ActionDefinition> GetAll() => entries.Values.ToArray();
    public static ActionDefinition Get(string id)
    {
        if(id==null)return null;
        if(entries.TryGetValue(id,out var value))return value;
        return aliases.TryGetValue(id,out var target)&&entries.TryGetValue(target,out value)?value:null;
    }
    internal static void RegisterAlias(string alias,string id)
    {
        if(alias==id||entries.ContainsKey(alias)||!entries.ContainsKey(id))return;
        aliases[alias]=id;Revision++;Notify();
    }
    public static event Action Changed;
    internal static void SetRingVisibility(string id,bool visible)
    {
        if(!entries.TryGetValue(id,out var definition)||definition.ShowInRing==visible)return;
        definition.ShowInRing=visible;Revision++;Notify();
    }

    public static void Register(ActionDefinition definition, string packDirectory, string providerId)
        => RegisterCore(definition, packDirectory, providerId, null);

    public static void RegisterRuntime(ActionDefinition definition, IActionSource source, string packDirectory, string providerId)
    {
        if (source == null || !float.IsFinite(source.Duration) || source.Duration <= 0) throw new ArgumentException("Runtime source requires a positive finite duration");
        RegisterCore(definition, packDirectory, providerId, source);
    }

    static void RegisterCore(ActionDefinition definition, string packDirectory, string providerId, IActionSource source)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        var files=ActionDefinitionValidation.Validate(definition,packDirectory,source);
        if (entries.ContainsKey(definition.Id)) throw new ArgumentException("Duplicate action ID: " + definition.Id);
        // Own a snapshot so a provider cannot mutate a running action's rules.
        var owned = JsonSerializer.Deserialize<ActionDefinition>(JsonSerializer.Serialize(definition));
        owned.BundlePath = files.Bundle; owned.AudioPath = files.Audio; owned.ProviderId = providerId ?? ""; owned.RuntimeSource = source;
        entries.Add(owned.Id, owned); Revision++; Notify();
    }

    public static int LoadPacks(string directory, string providerId)
        => ActionPackLoader.Load(directory, providerId);

    public static void UnregisterProvider(string providerId)
    {
        var ids = entries.Values.Where(d => d.ProviderId == providerId).Select(d => d.Id).ToArray();
        foreach (var id in ids) entries.Remove(id);
        foreach(var alias in aliases.Where(p=>!entries.ContainsKey(p.Value)).Select(p=>p.Key).ToArray())aliases.Remove(alias);
        if (ids.Length > 0) { Revision++; Notify(); }
    }

    static void Notify()
    {
        if (Changed == null) return;
        foreach (Action listener in Changed.GetInvocationList()) try { listener(); } catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_REGISTRY_LISTENER_FAILED " + e.Message); }
    }
}
