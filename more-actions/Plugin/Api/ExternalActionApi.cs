using System;
using System.IO;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace SFM.Actions;

/// <summary>Arbitrates player access and camera follow for actions driven by another mod.</summary>
public static class ExternalActionApi
{
    /// <summary>
    /// Acquire an exclusive player session on the game thread. Reads optional action.json from
    /// &lt;provider DLL directory&gt;/Actions/&lt;actionResourceName&gt;.
    /// The registered provider owns animation, props and input. Report camera targets after
    /// writing the pose, before the final camera update. Dispose after restoring pose and props,
    /// or restore them in the interruption callback. Returns null with a reason on failure.
    /// A null isExercising predicate means the whole session. The optional IKU reader supplies
    /// native progress and blend weight. All callbacks run on the game thread.
    /// </summary>
    public static IDisposable TryBeginAction(string providerId, bool requiresHands, bool requiresStanding,
        string actionResourceName, Func<bool> isExercising, bool hasIku, Action<string> interrupted,
        out Action<Vector3, Vector3> followCamera, out Func<(float Progress, float Weight)?> readIku, out string reason)
    {
        if (interrupted == null) throw new ArgumentNullException(nameof(interrupted));
        followCamera = null;
        readIku = null;
        if (string.IsNullOrWhiteSpace(providerId) || IL2CPPChainloader.Instance?.Plugins.TryGetValue(providerId, out var provider) != true ||
            string.IsNullOrEmpty(provider.Location))
        {
            reason = "action provider unavailable";
            return null;
        }
        if (string.IsNullOrWhiteSpace(actionResourceName) || actionResourceName.EndsWith('.') ||
            actionResourceName.EndsWith(' ') || actionResourceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            actionResourceName.Contains('/') || actionResourceName.Contains('\\') || actionResourceName.Contains(':'))
        {
            reason = "invalid action resource name";
            return null;
        }
        string directory = Path.GetDirectoryName(provider.Location);
        if (string.IsNullOrEmpty(directory))
        {
            reason = "action provider directory unavailable";
            return null;
        }
        var driver = ActionDriver.Current;
        if (driver == null || !driver.isActiveAndEnabled)
        {
            reason = "action driver unavailable";
            return null;
        }
        string path = Path.Combine(directory, "Actions", actionResourceName, "action.json");
        var session = driver.TryBeginExternal(providerId, requiresHands, requiresStanding, path,
            isExercising, hasIku, interrupted, out reason);
        if (session == null) return null;
        followCamera = session.Track;
        if (hasIku) readIku = session.ReadIku;
        return session;
    }

    internal static float ReadEcstasyRate(string path)
    {
        try
        {
            var config = ActionConfiguration.Parse(File.ReadAllText(path));
            return NativeActionStats.Resolve(config).EcstasyPerSecond;
        }
        catch (FileNotFoundException) { return 0f; }
        catch (DirectoryNotFoundException) { return 0f; }
    }
}
