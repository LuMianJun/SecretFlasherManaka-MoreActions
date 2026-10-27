using System;

namespace Seleka2.Motion;

// Optional managed API. Consumers discover it without referencing this DLL.
public static class MotionPlaybackApi
{
    public static bool IsPlaying { get; private set; }
    public static string CurrentMotionId { get; private set; } = string.Empty;
    public static event Action<string, bool> PlaybackChanged;

    internal static void Publish(string id, bool playing)
    {
        string previous = CurrentMotionId;
        CurrentMotionId = playing ? id : string.Empty;
        IsPlaying = playing;
        var listeners = PlaybackChanged;
        if (listeners == null) return;
        foreach (Action<string, bool> listener in listeners.GetInvocationList())
        {
            try { listener(playing ? id : previous, playing); }
            catch (Exception e) { Plugin.LogSource?.LogWarning("MOTION_LISTENER_FAILED " + e.Message); }
        }
    }
}
