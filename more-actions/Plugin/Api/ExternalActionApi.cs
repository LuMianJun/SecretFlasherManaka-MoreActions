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
    /// All callbacks run on the game thread.
    /// </summary>
    /// <param name="providerId">The caller's registered BepInEx plugin GUID.</param>
    /// <param name="requiresHands">Reject the action while the player is handcuffed.</param>
    /// <param name="requiresStanding">Reject the action while the player is crouching or sitting.</param>
    /// <param name="actionResourceName">One directory name under the caller's <c>Actions</c> directory.</param>
    /// <param name="isExercising">Return true while the action should contribute attributes. Null enables contributions during the whole eligible session.</param>
    /// <param name="hasIku">Whether the caller already has a dedicated clip it can sample during native IKU.</param>
    /// <param name="interrupted">Required callback that restores the caller's pose, inputs and props when More Actions ends the session.</param>
    /// <param name="followCamera">Optional use of world-space third-person target and first-person eye reporting. Call after writing the final pose.</param>
    /// <param name="readIku">Supplies native IKU progress and blend weight when <paramref name="hasIku"/> is true. The callback returns null when there is no IKU to sample.</param>
    /// <param name="reason">Failure reason when acquisition is declined.</param>
    /// <returns>The acquired session, or null if acquisition is declined. Dispose it on normal completion.</returns>
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
        if (!ValidResourceName(actionResourceName))
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

    /// <summary>Acquire one player lease for independently gated action profiles.</summary>
    /// <param name="options">Player requirements, stamina-depletion behavior and the required interruption callback.</param>
    /// <param name="profiles">One or more resource profiles. Attribute channels must be unique across profiles.</param>
    /// <param name="reason">Failure reason when acquisition is declined.</param>
    /// <returns>The acquired session, or null if acquisition is declined. Dispose it on normal completion.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or its interruption callback is null.</exception>
    public static IExternalActionSession TryBeginAction(ExternalActionOptions options,
        ExternalActionProfile[] profiles, out string reason)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (options.Interrupted == null) throw new ArgumentNullException(nameof(options.Interrupted));
        if (string.IsNullOrWhiteSpace(options.ProviderId) ||
            IL2CPPChainloader.Instance?.Plugins.TryGetValue(options.ProviderId, out var provider) != true ||
            string.IsNullOrEmpty(provider.Location))
        {
            reason = "action provider unavailable";
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
        if (options.StaminaDepletion is not (ExternalStaminaDepletion.EndSession or ExternalStaminaDepletion.KeepSession))
        {
            reason = "invalid stamina depletion policy";
            return null;
        }
        return driver.TryBeginExternal(options, directory, profiles, out reason);
    }

    internal static bool TryGetConfigPath(string directory, string resourceName, out string path)
    {
        path = ValidResourceName(resourceName) ? Path.Combine(directory, "Actions", resourceName, "action.json") : null;
        return path != null;
    }

    private static bool ValidResourceName(string name) =>
        !string.IsNullOrWhiteSpace(name) && !name.EndsWith('.') && !name.EndsWith(' ') &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') &&
        !name.Contains('\\') && !name.Contains(':');

    internal static ActionDefinition ReadConfiguration(string path)
    {
        try { return ActionConfiguration.Parse(File.ReadAllText(path)); }
        catch (FileNotFoundException) { return new ActionDefinition(); }
        catch (DirectoryNotFoundException) { return new ActionDefinition(); }
    }
}
