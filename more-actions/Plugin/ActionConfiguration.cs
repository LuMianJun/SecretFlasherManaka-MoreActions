using System;
using System.Text.Json;

namespace SFM.Actions;

// Public because both Unity and MMD providers share this configuration reader.
public static class ActionConfiguration
{
    public static ActionDefinition Parse(string json)
    {
        ActionEventSchema.ValidateJson(json);
        var options=new JsonSerializerOptions { PropertyNameCaseInsensitive=true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var action=JsonSerializer.Deserialize<ActionDefinition>(json,options);
        var flat=JsonSerializer.Deserialize<ActionStatsDefinition>(json,options);
        var stats=action.Stats??throw new ArgumentException("stats must be an object");
        stats.EcstasyPerSecond=flat.EcstasyPerSecond??stats.EcstasyPerSecond;
        stats.DokidokiAdd=flat.DokidokiAdd??stats.DokidokiAdd;
        stats.MoisturePerSecond=flat.MoisturePerSecond??stats.MoisturePerSecond;
        stats.StaminaPerSecond=flat.StaminaPerSecond??stats.StaminaPerSecond;
        stats.Strange=flat.Strange??stats.Strange;
        stats.StrangenessScale=flat.StrangenessScale??stats.StrangenessScale;
        stats.Discomfort=flat.Discomfort??stats.Discomfort;
        stats.CanStartPee=flat.CanStartPee??stats.CanStartPee;
        ActionStatsSchema.Validate(action);
        return action;
    }
}
