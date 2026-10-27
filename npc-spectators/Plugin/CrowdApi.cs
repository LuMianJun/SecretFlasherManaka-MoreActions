using System;
using System.Collections.Generic;

namespace Seleka2.NpcSpectators;

// Optional API for other action mods. Each caller owns a distinct source ID.
// Requests are thread-safe; all Unity work stays in the driver's main-thread tick.
public static class CrowdApi
{
    static readonly HashSet<string> sources = new(StringComparer.Ordinal);
    public static void SetSource(string sourceId, bool active)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("A stable source ID is required.", nameof(sourceId));
        lock (sources) { if (active) sources.Add(sourceId); else sources.Remove(sourceId); }
    }
    internal static bool Requested { get { lock (sources) return sources.Count > 0; } }
}
