using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Seleka2.Actions;

public enum ActionBodyMask { FullBody, UpperBody, LowerBody }

// A pack contains a Generic clip already retargeted to the game's canonical rig.
// Registry entries never become original SkillType, ActionType or save records.
public sealed class ActionDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Bundle { get; set; } = "";
    public string ClipAsset { get; set; } = "";
    public string AudioAsset { get; set; } = "";
    public string AudioFile { get; set; } = "";
    public double AudioDelaySeconds { get; set; }
    public float Volume { get; set; } = 1;
    public bool RequiresHands { get; set; } = true;
    public bool RequiresStanding { get; set; } = true;
    public bool FollowFaceCamera { get; set; } = true;
    public bool WriteHipTranslation { get; set; } = true;
    public bool Loop { get; set; }
    public ActionBodyMask BodyMask { get; set; } = ActionBodyMask.FullBody;
    public string[] BonePaths { get; set; } = Array.Empty<string>();
    public bool WriteFaceMorphs { get; set; } = true;
    public bool KeepGameExpressions { get; set; } = true;
    public bool ShowInRing { get; set; } = true;
    public bool UseClipEvents { get; set; } = true;
    public ActionEventDefinition[] Events { get; set; } = Array.Empty<ActionEventDefinition>();
    public ActionEventDefinition[] OnStart { get; set; } = Array.Empty<ActionEventDefinition>();
    public ActionEventDefinition[] OnFinish { get; set; } = Array.Empty<ActionEventDefinition>();
    public ActionEventDefinition[] OnCancel { get; set; } = Array.Empty<ActionEventDefinition>();
    public string ProviderId { get; internal set; } = "";
    public string BundlePath { get; internal set; } = "";
    public string AudioPath { get; internal set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public IActionSource RuntimeSource { get; internal set; }
}

public static class ActionRegistry
{
    static readonly Dictionary<string, ActionDefinition> entries = new(StringComparer.Ordinal);
    public static int Revision { get; private set; }
    public static IReadOnlyList<ActionDefinition> GetAll() => entries.Values.ToArray();
    public static ActionDefinition Get(string id) => id != null && entries.TryGetValue(id, out var value) ? value : null;
    public static event Action Changed;

    public static void Register(ActionDefinition definition, string packDirectory, string providerId)
        => RegisterCore(definition, packDirectory, providerId, null);

    public static void RegisterRuntime(ActionDefinition definition, IActionSource source, string packDirectory, string providerId)
    {
        if (source == null || !float.IsFinite(source.Duration) || source.Duration <= 0) throw new ArgumentException("Runtime source requires a positive finite duration");
        RegisterCore(definition, packDirectory, providerId, source);
    }

    static void RegisterCore(ActionDefinition definition, string packDirectory, string providerId, IActionSource source)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        ActionEventSchema.Validate(definition, source?.Duration);
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Any(c => char.IsControl(c) || c == ',')) throw new ArgumentException("Action ID cannot be empty or contain control characters or commas");
        if (string.IsNullOrWhiteSpace(definition.DisplayName) || (source == null && (string.IsNullOrWhiteSpace(definition.Bundle) || string.IsNullOrWhiteSpace(definition.ClipAsset)))) throw new ArgumentException("Action requires a label and a clip or runtime source");
        if (source != null && (!string.IsNullOrEmpty(definition.Bundle) || !string.IsNullOrEmpty(definition.ClipAsset) || !string.IsNullOrEmpty(definition.AudioAsset))) throw new ArgumentException("Runtime sources use AudioFile; bundle assets must be empty");
        if (!string.IsNullOrEmpty(definition.AudioAsset) && !string.IsNullOrEmpty(definition.AudioFile)) throw new ArgumentException("Choose AudioAsset or AudioFile");
        if (!Enum.IsDefined(typeof(ActionBodyMask), definition.BodyMask) || !double.IsFinite(definition.AudioDelaySeconds) || definition.AudioDelaySeconds < 0 || !float.IsFinite(definition.Volume) || definition.Volume < 0 || definition.Volume > 1) throw new ArgumentException("Invalid mask or audio settings");
        if (definition.Loop && (!string.IsNullOrEmpty(definition.AudioAsset) || !string.IsNullOrEmpty(definition.AudioFile))) throw new ArgumentException("Audio requires one-shot playback; silent motion loops are supported");
        if (!definition.RequiresHands && (definition.BodyMask != ActionBodyMask.LowerBody || definition.WriteFaceMorphs)) throw new ArgumentException("Hand-independent actions must use LowerBody and leave face morphs to the game");
        if (definition.BonePaths == null || definition.BonePaths.Distinct().Count() != definition.BonePaths.Length || definition.BonePaths.Any(p => !MotionRig.BonePaths().Contains(p) || !ActionPolicy.InMask(p, definition.BodyMask))) throw new ArgumentException("Unknown, duplicate or out-of-mask bone path");
        var root = Path.GetFullPath(packDirectory);
        var path = source == null ? ResolveFile(root, definition.Bundle) : "";
        var audioPath = string.IsNullOrEmpty(definition.AudioFile) ? "" : ResolveFile(root, definition.AudioFile);
        if (entries.ContainsKey(definition.Id)) throw new ArgumentException("Duplicate action ID: " + definition.Id);
        // Own a snapshot so a provider cannot mutate a running action's rules.
        var owned = JsonSerializer.Deserialize<ActionDefinition>(JsonSerializer.Serialize(definition));
        owned.BundlePath = path; owned.AudioPath = audioPath; owned.ProviderId = providerId ?? ""; owned.RuntimeSource = source;
        entries.Add(owned.Id, owned); Revision++; Notify();
    }

    static string ResolveFile(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new ArgumentException("Pack files must use relative paths");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("File escapes its pack directory");
        if (!File.Exists(path)) throw new FileNotFoundException("Pack file missing", path);
        return path;
    }

    public static int LoadPacks(string directory, string providerId)
    {
        if (!Directory.Exists(directory)) return 0;
        int count = 0;
        foreach (var file in Directory.GetFiles(directory, "*.action.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                string json = File.ReadAllText(file);
                ActionEventSchema.ValidateJson(json);
                Register(JsonSerializer.Deserialize<ActionDefinition>(json, options), Path.GetDirectoryName(file), providerId); count++;
            }
            catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_PACK_REJECTED file=" + Path.GetFileName(file) + " reason=" + e.Message); }
        }
        return count;
    }

    public static void UnregisterProvider(string providerId)
    {
        var ids = entries.Values.Where(d => d.ProviderId == providerId).Select(d => d.Id).ToArray();
        foreach (var id in ids) entries.Remove(id);
        if (ids.Length > 0) { Revision++; Notify(); }
    }

    static void Notify()
    {
        if (Changed == null) return;
        foreach (Action listener in Changed.GetInvocationList()) try { listener(); } catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_REGISTRY_LISTENER_FAILED " + e.Message); }
    }
}
