using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SFM.Actions;

/// <summary>Overrides for action attributes. Null inherits the preset or default; zero is a supplied value.</summary>
public sealed class ActionStatsDefinition
{
    /// <summary>Base arousal added per second. A full gauge is 1. Native multipliers still apply.</summary>
    public float? EcstasyPerSecond { get; set; }
    /// <summary>Native tension contribution from 0 to 1, before native multipliers.</summary>
    public float? DokidokiAdd { get; set; }
    /// <summary>Bladder refill queued per second in normalized units. Native absorption controls when it takes effect.</summary>
    public float? MoisturePerSecond { get; set; }
    /// <summary>Native stamina units consumed per second.</summary>
    public float? StaminaPerSecond { get; set; }
    /// <summary>Base suspicion added after scaling the native visual exposure contribution.</summary>
    public float? Strange { get; set; }
    /// <summary>Multiplier for the native visual exposure contribution, before adding <see cref="Strange"/>.</summary>
    public float? StrangenessScale { get; set; }
    /// <summary>Base discomfort contribution, compared with the native discomfort value.</summary>
    public float? Discomfort { get; set; }
    /// <summary>Allow the native urination flow when the regular game requirements are met.</summary>
    public bool? CanStartPee { get; set; }
}

internal readonly record struct ActionStatValues(float EcstasyPerSecond = 0, float DokidokiAdd = 0,
    float MoisturePerSecond = 0, float StaminaPerSecond = 0, float Strange = 0,
    float StrangenessScale = 1, float Discomfort = 0, bool CanStartPee = false)
{
    internal static ActionStatValues Empty => new(0,0,0,0,0,1,0,false);
}

internal enum ActionStatChannel { Ecstasy, Moisture, Stamina }
internal sealed class ActionStatFrames
{
    readonly int[] frames = { -1, -1, -1 };
    internal bool Take(ActionStatChannel channel, int frame)
    {
        int index=(int)channel;
        if(frames[index]==frame)return false;
        frames[index]=frame;return true;
    }
}

internal static class ActionStatsSchema
{
    // Presets read these original records; no record or CurrentAction is changed.
    internal static readonly IReadOnlyDictionary<string,string> Presets = new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["None"]="", ["Interaction"]="", ["Pose"]="HipShake", ["Onani"]="OnaniNormal",
        ["Dildo"]="UseDildoWallPussy1", ["DildoFella"]="UseDildoWallFella1",
        ["DildoWait"]="UseDildoWallWaitPussy", ["SitDildo"]="SitDildo",
        ["Sex"]="SexStandBack", ["Pee"]="PeeNormal"
    };
    static readonly string[] fields = { "ecstasyPerSecond", "dokidokiAdd", "moisturePerSecond",
        "staminaPerSecond", "strange", "strangenessScale", "discomfort", "canStartPee" };

    internal static void Validate(ActionDefinition action)
    {
        if(action.NativeKind==null||!Presets.ContainsKey(action.NativeKind))throw new ArgumentException("Unknown nativeKind");
        var stats=action.Stats??throw new ArgumentException("stats must be an object");
        var values=new[]{stats.EcstasyPerSecond,stats.DokidokiAdd,stats.MoisturePerSecond,stats.StaminaPerSecond,
            stats.Strange,stats.StrangenessScale,stats.Discomfort};
        if(values.Any(x=>x.HasValue&&(!float.IsFinite(x.Value)||x.Value<0)))
            throw new ArgumentException("Stats must be finite nonnegative numbers");
        if(stats.DokidokiAdd>1)throw new ArgumentException("dokidokiAdd must be between 0 and 1");
    }

    internal static void ValidateJson(JsonElement root)
    {
        var top=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var property in root.EnumerateObject())
        {
            bool stats=property.Name.Equals("stats",StringComparison.OrdinalIgnoreCase);
            bool kind=property.Name.Equals("nativeKind",StringComparison.OrdinalIgnoreCase);
            bool mission=property.Name.Equals("poseMission",StringComparison.OrdinalIgnoreCase);
            bool flat=fields.Contains(property.Name,StringComparer.OrdinalIgnoreCase);
            if(!stats&&!kind&&!mission&&!flat)continue;
            if(!top.Add(property.Name)||property.Value.ValueKind==JsonValueKind.Null)
                throw new ArgumentException("Duplicate or null action property: "+property.Name);
            if(kind&&property.Value.ValueKind!=JsonValueKind.String)throw new ArgumentException("nativeKind must be a string");
            if(mission&&property.Value.ValueKind!=JsonValueKind.True&&property.Value.ValueKind!=JsonValueKind.False)
                throw new ArgumentException("poseMission must be a boolean");
            if(!stats)continue;
            if(property.Value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("stats must be an object");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var field in property.Value.EnumerateObject())
                if(!fields.Contains(field.Name,StringComparer.OrdinalIgnoreCase)||!seen.Add(field.Name)||field.Value.ValueKind==JsonValueKind.Null)
                    throw new ArgumentException("Unknown, duplicate or null stat: "+field.Name);
        }
    }

    internal static ActionStatValues Resolve(ActionDefinition definition, Func<string,ActionStatValues> read)
    {
        Validate(definition);
        string source=Presets[definition.NativeKind];
        var defaults=source.Length==0?ActionStatValues.Empty:read(source);
        var s=definition.Stats;
        return new(s.EcstasyPerSecond??defaults.EcstasyPerSecond,s.DokidokiAdd??defaults.DokidokiAdd,
            s.MoisturePerSecond??0,s.StaminaPerSecond??0,s.Strange??defaults.Strange,
            s.StrangenessScale??defaults.StrangenessScale,s.Discomfort??defaults.Discomfort,s.CanStartPee??false);
    }
}
