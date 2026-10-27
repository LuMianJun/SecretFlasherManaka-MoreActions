using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Player.ScriptableObject;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.Sound;
using UnityEngine;

namespace Seleka2.Actions;

internal static class NativeActionEvents
{
    internal static void Register()
    {
        ActionEventRegistry.Register("native.playSe", json => Sound(json), (_, json) =>
        {
            Owner().Pca.PlayerSoundManager.PlaySe(Sound(json)); return null;
        });
        // Preserve the original OnPlaySe special-effect branch for embedded clips.
        ActionEventRegistry.Register("native.animationSound", json => Sound(json), (_, json) =>
        {
            Owner().Pca.PlayerAnimationReceiver.OnPlaySe(Sound(json)); return null;
        });
        ActionEventRegistry.Register("native.footstep", json => Foot(json), (_, json) =>
        {
            var args = Foot(json);
            var owner = Owner();
            var record = ScriptableObject.CreateInstance<MFootAnimationEventRecord>();
            try
            {
                record.hideFlags = HideFlags.HideAndDontSave;
                record.SeType = args.SeType; record.Direct = args.Direction; record.IsMoveAction = args.IsMoveAction;
                var animationEvent = new AnimationEvent { objectReferenceParameter = record };
                if (args.Foot == "Left") owner.Pca.PlayerAnimationReceiver.FootL(animationEvent);
                else owner.Pca.PlayerAnimationReceiver.FootR(animationEvent);
            }
            finally { UnityEngine.Object.Destroy(record); }
            return null;
        });
    }

    static PlayerController Owner()
    {
        var owner = ActionDriver.Current?.Owner;
        if (owner == null || owner != PlayerController.Instance || owner.Pca == null) throw new InvalidOperationException("Original player context unavailable");
        return owner;
    }
    internal static InGameSoundManager.SeType Sound(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement; Fields(root, "type");
        return EnumValue<InGameSoundManager.SeType>(root, "type", null);
    }
    internal sealed record FootArgs(string Foot, InGameSoundManager.SeType SeType, Constant.Direct Direction, bool IsMoveAction);
    internal static FootArgs Foot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement; Fields(root, "foot", "seType", "direction", "isMoveAction");
        string foot = root.TryGetProperty("foot", out var side) ? side.GetString() : "Left";
        if (foot != "Left" && foot != "Right") throw new ArgumentException("foot must be Left or Right");
        return new FootArgs(foot, EnumValue<InGameSoundManager.SeType>(root, "seType", "FootStepHeel"),
            EnumValue<Constant.Direct>(root, "direction", "Forward"), root.TryGetProperty("isMoveAction", out var move) && move.GetBoolean());
    }
    internal static void Fields(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Event args must be an object");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
            if (!names.Contains(field.Name) || !seen.Add(field.Name) || field.Value.ValueKind == JsonValueKind.Null)
                throw new ArgumentException("Unknown, duplicate or null event argument: " + field.Name);
    }
    static T EnumValue<T>(JsonElement root, string field, string fallback) where T : struct, Enum
    {
        string name = root.TryGetProperty(field, out var value) ? value.GetString() : fallback;
        if (name == null || !Enum.GetNames(typeof(T)).Contains(name) || !Enum.TryParse<T>(name, out var result))
            throw new ArgumentException("Invalid " + field + ": use an original " + typeof(T).Name + " enum name");
        return result;
    }
}

internal static class ClipActionEvents
{
    internal static ActionEventDefinition[] Read(AnimationClip clip)
    {
        var result = new List<ActionEventDefinition>();
        foreach (var entry in clip.events)
        {
            ActionEventDefinition converted;
            switch (entry.functionName)
            {
                case "OnPlaySe":
                    var type = (InGameSoundManager.SeType)entry.intParameter;
                    if (!Enum.IsDefined(typeof(InGameSoundManager.SeType), type)) throw new ArgumentException("Invalid embedded OnPlaySe sound ID");
                    converted = new ActionEventDefinition { Event = "native.animationSound", Args = JsonSerializer.SerializeToElement(new { type = type.ToString() }) };
                    break;
                case "FootL":
                case "FootR":
                    var record = entry.objectReferenceParameter?.TryCast<MFootAnimationEventRecord>();
                    if (record == null) throw new ArgumentException("Embedded FootL/FootR requires an original MFootAnimationEventRecord; use JSON native.footstep or useClipEvents=false");
                    converted = new ActionEventDefinition { Event = "native.footstep", Args = JsonSerializer.SerializeToElement(new {
                        foot = entry.functionName == "FootL" ? "Left" : "Right", seType = record.SeType.ToString(), direction = record.Direct.ToString(), isMoveAction = record.IsMoveAction }) };
                    break;
                default:
                    throw new ArgumentException("Unsupported embedded animation event: " + entry.functionName + "; use JSON events or useClipEvents=false");
            }
            converted.Time = entry.time; result.Add(converted);
        }
        return result.ToArray();
    }
}
