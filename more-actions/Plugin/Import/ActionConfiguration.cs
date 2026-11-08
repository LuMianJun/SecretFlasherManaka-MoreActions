using System;
using System.Text.Json;

namespace SFM.Actions;

// Public because both Unity and MMD providers share this configuration reader.
public static class ActionConfiguration
{
    static readonly JsonSerializerOptions options = CreateOptions();
    static JsonSerializerOptions CreateOptions()
    {
        var value=new JsonSerializerOptions { PropertyNameCaseInsensitive=true };
        value.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return value;
    }

    public static ActionDefinition Parse(string json)
    {
        using var document=JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    internal static ActionDefinition Parse(JsonElement root)
    {
        ValidateJson(root);
        string json=root.GetRawText();
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
    internal static void ValidateJson(string json)
    {
        using var document=JsonDocument.Parse(json);
        ValidateJson(document.RootElement);
    }

    internal static void ValidateJson(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Action configuration must be an object");
        ActionStatsSchema.ValidateJson(root);
        ActionEventSchema.ValidateEvents(root);
    }

}
