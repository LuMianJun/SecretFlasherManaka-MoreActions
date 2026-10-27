using System;
using System.Reflection;

namespace Seleka2.NpcSpectators;

// No assembly reference, dependency attribute, or bundle requirement on the MMD mod.
internal sealed class MmdBridge : IDisposable
{
    readonly Action<string> log;
    readonly Func<Type> discover;
    Type api;
    EventInfo changed;
    Action<string, bool> listener;
    PropertyInfo playing, motion;
    internal string MotionId { get; private set; } = string.Empty;
    internal bool Connected => api != null;

    internal MmdBridge(Action<string> logger, Func<Type> discoverApi = null)
    {
        log = logger;
        discover = discoverApi ?? (() =>
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var found = assembly.GetType("Seleka2.Motion.MotionPlaybackApi", false);
                if (found != null) return found;
            }
            return null;
        });
    }

    internal void Refresh()
    {
        if (api == null)
        {
            var found = discover();
            if (found != null)
            {
                var p = found.GetProperty("IsPlaying", BindingFlags.Public | BindingFlags.Static);
                var m = found.GetProperty("CurrentMotionId", BindingFlags.Public | BindingFlags.Static);
                if (p?.PropertyType != typeof(bool) || m?.PropertyType != typeof(string)) return;
                api = found; playing = p; motion = m;
                changed = found.GetEvent("PlaybackChanged", BindingFlags.Public | BindingFlags.Static);
                if (changed?.EventHandlerType == typeof(Action<string, bool>))
                {
                    listener = (id, active) => MotionId = active ? id : string.Empty;
                    changed.AddEventHandler(null, listener);
                }
                log?.Invoke("CROWD_MMD_CONNECTED assembly=" + found.Assembly.GetName().Name);
            }
        }
        // Snapshot also covers late installation/subscription and a missed notification.
        MotionId = api != null && (bool)playing.GetValue(null) ? (string)motion.GetValue(null) : string.Empty;
    }

    public void Dispose()
    {
        if (listener != null) changed.RemoveEventHandler(null, listener);
        listener = null; api = null; MotionId = string.Empty;
    }
}
