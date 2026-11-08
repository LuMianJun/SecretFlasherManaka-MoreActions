using System;

namespace SFM.Actions;

internal sealed class ActionRequestQueue
{
    string pending;
    internal string PendingId => pending;
    internal void Request(string id) { if (ActionRegistry.Get(id) == null) throw new ArgumentException("Unknown action: " + id); pending = id; }
    internal void Clear() => pending = null;
    internal string Take(bool ring, bool ui) { if (pending == null || ring || ui) return null; var id = pending; pending = null; return id; }
}
