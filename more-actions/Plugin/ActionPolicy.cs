using System;

namespace Seleka2.Actions;

public readonly record struct ActionAvailability(bool Initialized, bool Idle, bool NativeAction, bool Ecstasy, bool Crouching, bool Sitting, bool Handcuffed, bool SexMode, bool Moving, bool UiOpen, bool GameUnavailable);

public static class ActionPolicy
{
    public static string BlockReason(ActionDefinition action, ActionAvailability state)
    {
        if (!state.Initialized || state.GameUnavailable) return "player unavailable";
        if (!state.Idle || state.NativeAction || state.Ecstasy || state.SexMode) return "native action/state active";
        if (action.RequiresHands && state.Handcuffed) return "handcuffed";
        if (action.RequiresStanding && (state.Crouching || state.Sitting)) return "stand first";
        if (state.Moving) return "moving";
        if (state.UiOpen) return "menu open";
        return null;
    }

    public static bool InMask(string path, ActionBodyMask mask)
    {
        bool leg = path.Contains("/UpperLeg_L", StringComparison.Ordinal) || path.Contains("/UpperLeg_R", StringComparison.Ordinal);
        return mask == ActionBodyMask.FullBody || (mask == ActionBodyMask.LowerBody ? leg : !leg && path != "Armature/Hips");
    }

    public static bool WritesBone(ActionDefinition action, string path)
        => InMask(path, action.BodyMask) && (action.BonePaths.Length == 0 || Array.IndexOf(action.BonePaths, path) >= 0);
}

public static class ActionPlaybackApi
{
    public static bool IsPlaying { get; private set; }
    public static string CurrentActionId { get; private set; } = "";
    public static string CurrentProviderId { get; private set; } = "";
    public static event Action<string, bool> PlaybackChanged;
    // Emitted after the final gameplay pose, never for the Tab preview. Time is
    // taken from the same DSP clock used to schedule the action's audio.
    public static event Action<string, double> PoseApplied;
    public static double PlaybackPositionSeconds { get; private set; }
    // Only the whole-cycle translation for a looping root-motion clip. This
    // unrolls the authored loop; it is not a camera correction for omitted motion.
    public static UnityEngine.Vector3 RootCycleOffset { get; private set; }
    internal static void SetRootCycleOffset(UnityEngine.Vector3 offset) => RootCycleOffset = offset;
    public static void Request(string id) => ActionDriver.Current?.Request(id);
    public static void Stop() => ActionDriver.Current?.Stop("API");
    internal static void Publish(ActionDefinition action, bool active)
    {
        string previous = CurrentActionId;
        IsPlaying = active; CurrentActionId = active ? action.Id : ""; CurrentProviderId = active ? action.ProviderId : "";
        PlaybackPositionSeconds = 0;
        RootCycleOffset = default;
        if (PlaybackChanged == null) return;
        foreach (Action<string, bool> listener in PlaybackChanged.GetInvocationList())
            try { listener(active ? action.Id : previous, active); } catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_LISTENER_FAILED " + e.Message); }
    }
    internal static void PublishPose(string id, double seconds)
    {
        if (!IsPlaying || CurrentActionId != id) return;
        PlaybackPositionSeconds = seconds;
        if (PoseApplied == null) return;
        foreach (Action<string, double> listener in PoseApplied.GetInvocationList())
            try { listener(id, seconds); } catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_POSE_LISTENER_FAILED " + e.Message); }
    }
}

internal sealed class ActionRequestQueue
{
    string pending;
    internal string PendingId => pending;
    internal void Request(string id) { if (ActionRegistry.Get(id) == null) throw new ArgumentException("Unknown action: " + id); pending = id; }
    internal void Clear() => pending = null;
    internal string Take(bool ring, bool ui) { if (pending == null || ring || ui) return null; var id = pending; pending = null; return id; }
}
