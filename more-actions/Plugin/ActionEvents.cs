using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seleka2.Actions;

public sealed class ActionEventDefinition
{
    public double? Time { get; set; }
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    public JsonElement Args { get; set; } = JsonSerializer.SerializeToElement(new Dictionary<string, string>());
    public string Repeat { get; set; } = "EachLoop";
}

// A small CLR-only boundary also allows standalone mods to register via reflection.
// Invoke runs on the game's main thread. Its first argument is a unique effect
// source ID; its second is the validated args JSON. Returned leases are always
// disposed on stop, including when stop occurs inside Invoke itself.
public static class ActionEventRegistry
{
    internal sealed class Handler : IDisposable
    {
        internal string Name;
        internal Action<string> Validate;
        internal Func<string, string, IDisposable> Invoke;
        internal bool Active = true;
        public void Dispose()
        {
            if (!Active) return;
            Active = false;
            if (handlers.TryGetValue(Name, out var current) && ReferenceEquals(this, current)) handlers.Remove(Name);
        }
    }
    static readonly Dictionary<string, Handler> handlers = new(StringComparer.Ordinal);
    public static IDisposable Register(string name, Action<string> validate, Func<string, string, IDisposable> invoke)
    {
        if (string.IsNullOrWhiteSpace(name) || validate == null || invoke == null) throw new ArgumentException("An event needs a name, validator and callback");
        if (handlers.ContainsKey(name)) throw new ArgumentException("Duplicate action event: " + name);
        var handler = new Handler { Name = name, Validate = validate, Invoke = invoke };
        handlers.Add(name, handler);
        return handler;
    }
    internal static Handler Resolve(string name) => handlers.TryGetValue(name, out var value) ? value
        : throw new ArgumentException("Action event unavailable: " + name + " (check the providing plugin)");
}

public static class ActionEventSchema
{
    public static void ValidateJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Action configuration must be an object");
        foreach (var field in doc.RootElement.EnumerateObject())
        {
            bool timed = field.Name.Equals("events", StringComparison.OrdinalIgnoreCase);
            if (!timed && !new[] { "onStart", "onFinish", "onCancel" }.Contains(field.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (field.Value.ValueKind != JsonValueKind.Array) throw new ArgumentException(field.Name + " must be an array");
            foreach (var item in field.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new ArgumentException(field.Name + " entries must be objects");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                {
                    if (!(timed ? new[] { "time", "event", "args", "repeat" } : new[] { "event", "args" }).Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                        || !seen.Add(property.Name) || property.Value.ValueKind == JsonValueKind.Null)
                        throw new ArgumentException("Invalid or duplicate event field: " + property.Name);
                }
            }
        }
    }

    internal static void Validate(ActionDefinition definition, double? duration = null)
    {
        ValidateList(definition.Events, true, duration);
        ValidateList(definition.OnStart, false, duration);
        ValidateList(definition.OnFinish, false, duration);
        ValidateList(definition.OnCancel, false, duration);
    }
    internal static void ValidateList(ActionEventDefinition[] items, bool timed, double? duration)
    {
        if (items == null || items.Length > 4096) throw new ArgumentException("Event lists must be non-null arrays with at most 4096 entries");
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Event) || item.Args.ValueKind != JsonValueKind.Object
                || (item.Repeat != "EachLoop" && item.Repeat != "Once")) throw new ArgumentException("Invalid event name, args or repeat");
            if (timed ? !item.Time.HasValue || !double.IsFinite(item.Time.Value) || item.Time < 0
                || (duration.HasValue && item.Time > duration + 0.00001) : item.Time.HasValue)
                throw new ArgumentException("Event time must be within the motion duration; lifecycle entries have no time");
        }
    }
}

internal sealed class ActionEventRunner : IDisposable
{
    sealed class Bound
    {
        internal ActionEventRegistry.Handler Handler;
        internal string Args, Location, Source;
        internal double Time, Next;
        internal long Cycle;
        internal bool Once;
        internal int Index;
    }
    readonly List<Bound> timeline;
    readonly List<Bound> onStart, onFinish, onCancel;
    readonly Dictionary<Bound, IDisposable> leases = new();
    readonly Func<bool> current;
    readonly Action<string> log;
    readonly bool loop;
    readonly double duration;
    bool started, closed;
    double previous = -1;
    readonly string session = Guid.NewGuid().ToString("N");

    internal ActionEventRunner(ActionDefinition definition, ActionEventDefinition[] clipEvents, double duration, Func<bool> current, Action<string> log)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentException("Event runner needs a positive duration");
        ActionEventSchema.Validate(definition, duration);
        ActionEventSchema.ValidateList(clipEvents, true, duration);
        this.duration = duration; this.current = current; this.log = log; loop = definition.Loop;
        // Bind every handler and validate every argument before any effects execute.
        timeline = Bind(definition.Events.Concat(clipEvents), "events", true);
        onStart = Bind(definition.OnStart, "onStart", false);
        onFinish = Bind(definition.OnFinish, "onFinish", false);
        onCancel = Bind(definition.OnCancel, "onCancel", false);
    }

    List<Bound> Bind(IEnumerable<ActionEventDefinition> entries, string phase, bool timed)
    {
        var result = new List<Bound>();
        foreach (var entry in entries)
        {
            string location = phase + "[" + result.Count + "] " + entry.Event + (timed ? " time=" + entry.Time.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            try
            {
                var handler = ActionEventRegistry.Resolve(entry.Event);
                string args = entry.Args.GetRawText(); handler.Validate(args);
                double time = timed ? Math.Min(entry.Time.Value, duration) : 0;
                result.Add(new Bound { Handler = handler, Args = args, Location = location, Time = time, Next = time,
                    Once = entry.Repeat == "Once", Index = result.Count, Source = "moreactions/" + session + "/" + phase + "/" + result.Count });
            }
            catch (Exception e) { throw new ArgumentException(location + ": " + e.Message, e); }
        }
        return result;
    }

    internal void Advance(double seconds)
    {
        if (closed || !current()) return;
        if (!double.IsFinite(seconds) || seconds < 0 || seconds < previous) throw new ArgumentException("Event clock must be finite and monotonic");
        previous = seconds;
        if (!started)
        {
            started = true;
            foreach (var entry in onStart) { if (closed || !current()) return; Execute(entry, 0); }
        }
        int dispatched = 0;
        while (!closed && current())
        {
            Bound next = null;
            foreach (var entry in timeline)
                if (entry.Next <= seconds && (next == null || entry.Next < next.Next
                    || (entry.Next == next.Next && (entry.Cycle < next.Cycle || (entry.Cycle == next.Cycle && entry.Index < next.Index))))) next = entry;
            if (next == null) break;
            if (++dispatched > 4096) throw new InvalidOperationException("Too many overdue action events in one frame");
            long cycle = next.Cycle;
            // Advance before invoking user code, which may stop this runner.
            next.Next = loop && !next.Once ? (++next.Cycle * duration + next.Time) : double.PositiveInfinity;
            Execute(next, cycle);
        }
    }

    void Execute(Bound entry, long cycle)
    {
        IDisposable lease = null;
        try
        {
            if (leases.TryGetValue(entry, out var previousLease))
            {
                leases.Remove(entry); previousLease.Dispose();
                if (closed || !current()) return;
            }
            if (!entry.Handler.Active) throw new InvalidOperationException("Event provider was removed");
            lease = entry.Handler.Invoke(entry.Source + "/" + cycle, entry.Args);
            if (lease == null) return;
            if (closed) lease.Dispose(); else leases.Add(entry, lease);
        }
        catch (Exception e) { throw new InvalidOperationException(entry.Location + ": " + e.Message, e); }
    }

    internal void Close(bool completed)
    {
        if (closed) return;
        closed = true;
        // Lifecycle failures must not prevent subsequent cleanup. Closed also
        // ensures leases returned by reentrant callbacks are released immediately.
        if (started)
            foreach (var entry in completed ? onFinish : onCancel)
            {
                if (!current()) break;
                try { Execute(entry, 0); } catch (Exception e) { log?.Invoke("ACTION_EVENT_END_FAILED " + e.Message); }
            }
        foreach (var lease in leases.Reverse().ToArray())
            try { lease.Value.Dispose(); } catch (Exception e) { log?.Invoke("ACTION_EVENT_CLEANUP_FAILED " + lease.Key.Location + " " + e.Message); }
        leases.Clear();
    }
    public void Dispose() => Close(false);
}
