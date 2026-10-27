using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Seleka2.NpcSpectators;

// Optional registration through a CLR-only delegate API. No base/MMD assembly
// reference is introduced, so NPC Spectators still loads on its own.
internal static class ActionEventBridge
{
    static IDisposable registration;
    internal static void Refresh()
    {
        if (registration != null) return;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Seleka2.MoreActions");
        var registry = assembly?.GetType("Seleka2.Actions.ActionEventRegistry");
        if (registry == null) return;
        var method = registry.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(string), typeof(Action<string>), typeof(Func<string, string, IDisposable>) }, null);
        if (method == null) return;
        registration = (IDisposable)method.Invoke(null, new object[] { "npc.spectators", (Action<string>)Validate, (Func<string, string, IDisposable>)Begin });
    }
    static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Any())
            throw new ArgumentException("npc.spectators takes no arguments; configure gathering in game settings");
    }
    static IDisposable Begin(string source, string json)
    {
        Validate(json); CrowdApi.SetSource(source, true); return new Request(source);
    }
    sealed class Request : IDisposable
    {
        readonly string source;
        internal Request(string source) { this.source = source; }
        bool released;
        public void Dispose() { if (released) return; released = true; CrowdApi.SetSource(source, false); }
    }
    internal static void Dispose() { registration?.Dispose(); registration = null; }
}
