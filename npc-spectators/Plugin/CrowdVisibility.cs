using System.Collections.Generic;
using UnityEngine;

namespace Seleka2.NpcSpectators;

// Standpoint selection only. Cast before navigation projection, then recheck
// the actual projected point so a nearby navmesh cannot put a spectator behind a wall.
internal static class CrowdVisibility
{
    internal static bool IsClear(Vector3 center, Vector3 destination, IReadOnlyList<Transform> actors)
    {
        int mask = ~LayerMask.GetMask("Ignore Raycast", "Player", "PlayerFace", "Npc", "NpcSightArea");
        foreach (float height in new[] { 0.8f, 1.3f })
        {
            var origin = center + new Vector3(0, height, 0);
            var end = destination + new Vector3(0, height, 0);
            var delta = end - origin;
            if (delta.sqrMagnitude < 0.000001f) continue;
            foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore))
            {
                var collider = hit.collider;
                if (collider == null || collider.isTrigger) continue;
                bool actor = false;
                foreach (var root in actors)
                    if (root != null && collider.transform.IsChildOf(root)) { actor = true; break; }
                if (!actor) return false;
            }
        }
        return true;
    }
}
