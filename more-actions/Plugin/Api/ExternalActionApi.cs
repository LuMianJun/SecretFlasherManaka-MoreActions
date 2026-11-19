using System;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace SFM.Actions;

/// <summary>Arbitrates player access and camera follow for actions driven by another mod.</summary>
public static class ExternalActionApi
{
    /// <summary>
    /// Acquire an exclusive player session on the game thread.
    /// The registered provider owns animation, props and input. Report camera targets after
    /// writing the pose, before the final camera update. Dispose after restoring pose and props,
    /// or restore them in the interruption callback. Returns null with a reason on failure.
    /// All callbacks run on the game thread.
    /// </summary>
    public static IDisposable TryBeginAction(string providerId, bool requiresHands, bool requiresStanding,
        Action<string> interrupted, out Action<Vector3, Vector3> followCamera, out string reason)
    {
        if (interrupted == null) throw new ArgumentNullException(nameof(interrupted));
        followCamera = null;
        if (string.IsNullOrWhiteSpace(providerId) || IL2CPPChainloader.Instance?.Plugins.ContainsKey(providerId) != true)
        {
            reason = "action provider unavailable";
            return null;
        }
        var driver = ActionDriver.Current;
        if (driver == null || !driver.isActiveAndEnabled)
        {
            reason = "action driver unavailable";
            return null;
        }
        var session = driver.TryBeginExternal(providerId, requiresHands, requiresStanding, interrupted, out reason);
        if (session == null) return null;
        followCamera = session.Track;
        return session;
    }
}
