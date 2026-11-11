using System;

namespace SFM.Actions;

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
    // Null outside the scheduled music interval. Like the pose clock, this
    // position is published before PoseApplied and holds while paused.
    public static double? AudioPositionSeconds { get; private set; }
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
        AudioPositionSeconds = null;
        RootCycleOffset = default;
        if (PlaybackChanged == null) return;
        foreach (Action<string, bool> listener in PlaybackChanged.GetInvocationList())
            try { listener(active ? action.Id : previous, active); } catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_LISTENER_FAILED " + e.Message); }
    }
    internal static void PublishPose(string id, double seconds, double? audioPositionSeconds = null)
    {
        if (!IsPlaying || CurrentActionId != id) return;
        PlaybackPositionSeconds = seconds;
        AudioPositionSeconds = audioPositionSeconds;
        if (PoseApplied == null) return;
        foreach (Action<string, double> listener in PoseApplied.GetInvocationList())
            try { listener(id, seconds); } catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_POSE_LISTENER_FAILED " + e.Message); }
    }
}
