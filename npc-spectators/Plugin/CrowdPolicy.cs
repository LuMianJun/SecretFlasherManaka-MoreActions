using System;
using System.Collections.Generic;

namespace Seleka2.NpcSpectators;

internal static class CrowdPolicy
{
    internal static bool IsEligible(bool alive, bool active, bool police, bool destroyed,
        int fixedType, int state, bool sitting, bool token, bool hasRoute, bool canNavigate)
        => alive && active && !police && !destroyed && fixedType == 0 && state == 0
            && !sitting && !token && hasRoute && canNavigate;

    internal static bool Matches(string configured, string id)
    {
        if (string.IsNullOrEmpty(id) || id == "None" || id == "-1") return false;
        foreach (var token in (configured ?? "").Split(','))
            if (string.Equals(token.Trim(), id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

internal readonly struct CrowdSlot
{
    internal readonly int Index, Ring;
    internal readonly float X, Z;
    internal CrowdSlot(int index, int ring, double angle, float radius)
    { Index = index; Ring = ring; X = (float)Math.Cos(angle) * radius; Z = (float)Math.Sin(angle) * radius; }
}

internal static class CrowdSlots
{
    internal static List<CrowdSlot> Create(int count, float radius, float spacing)
    {
        if (count < 0 || !float.IsFinite(radius) || !float.IsFinite(spacing) || radius < 1 || spacing < 0.5f || spacing > radius * 1.7f)
            throw new ArgumentOutOfRangeException();
        var result = new List<CrowdSlot>(count);
        for (int ring = 0; result.Count < count; ring++)
        {
            float r = radius + ring * spacing * 1.1f;
            // Chord length, rather than arc length, guarantees the requested spacing.
            int capacity = Math.Max(3, (int)Math.Floor(Math.PI / Math.Asin(Math.Min(0.99, spacing / (2 * r)))));
            for (int slot = 0; slot < capacity && result.Count < count; slot++)
                result.Add(new CrowdSlot(result.Count, ring, (slot + (ring % 2) * 0.5) * Math.PI * 2 / capacity, r));
        }
        return result;
    }
}
