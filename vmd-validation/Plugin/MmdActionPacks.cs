using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Seleka2.Actions;

namespace Seleka2.Mmd;

internal sealed record MmdActionPack(ActionDefinition Action, string Directory, string Json,
    string MotionPath, MmdReferencePose ReferencePose);

// Discovery and defaults are shared by manifest and folder-only imports.
internal static class MmdActionPacks
{
    static readonly JsonSerializerOptions options = CreateOptions();
    static JsonSerializerOptions CreateOptions()
    {
        var value = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        value.Converters.Add(new JsonStringEnumConverter());
        return value;
    }

    internal static IEnumerable<string> FindFolders(string root)
    {
        if (!System.IO.Directory.Exists(root)) return Array.Empty<string>();
        return System.IO.Directory.EnumerateFiles(root, "motion.vmd", SearchOption.AllDirectories)
            .Concat(System.IO.Directory.EnumerateFiles(root, "action.json", SearchOption.AllDirectories))
            .Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    internal static MmdActionPack Read(string directory)
    {
        directory = Path.GetFullPath(directory);
        string manifest = Path.Combine(directory, "action.json");
        string json = File.Exists(manifest) ? File.ReadAllText(manifest) : "{}";
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckFields(root, "id", "displayName", "description", "vmdFile", "audioFile", "referencePose",
            "audioDelaySeconds", "volume", "loop", "requiresHands", "requiresStanding", "bodyMask",
            "writeHipTranslation", "writeFaceMorphs", "keepGameExpressions", "followFaceCamera", "showInRing", "bonePaths", "camera",
            "events", "onStart", "onFinish", "onCancel", "useClipEvents");
        ActionEventSchema.ValidateJson(json);
        var action = JsonSerializer.Deserialize<ActionDefinition>(json, options);
        string label = new DirectoryInfo(directory).Name;
        if (!TryField(root, "id", out _)) action.Id = label;
        if (!TryField(root, "displayName", out _)) action.DisplayName = label;
        if (!TryField(root, "audioFile", out _))
            action.AudioFile = File.Exists(Path.Combine(directory, "BGM.wav")) ? "BGM.wav" : "";
        if (!string.IsNullOrEmpty(action.AudioFile)) PackFile(directory, action.AudioFile);
        string motion = TryField(root, "vmdFile", out var vmd) ? vmd.GetString() : "motion.vmd";
        var referencePose = MmdReferencePose.A;
        if (TryField(root, "referencePose", out var pose))
        {
            if (pose.ValueKind != JsonValueKind.String) throw new ArgumentException("referencePose must be A or T");
            referencePose = pose.GetString().ToUpperInvariant() switch
            {
                "A" => MmdReferencePose.A,
                "T" => MmdReferencePose.T,
                _ => throw new ArgumentException("referencePose must be A or T")
            };
        }
        return new MmdActionPack(action, directory, json, PackFile(directory, motion), referencePose);
    }

    internal static bool TryField(JsonElement root, string name, out JsonElement value)
    {
        foreach (var field in root.EnumerateObject())
            if (field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = field.Value; return true; }
        value = default;
        return false;
    }

    internal static void CheckFields(JsonElement root, params string[] allowed)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("action.json and camera must be JSON objects");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in root.EnumerateObject())
        {
            if (field.Name.Equals("modelFile", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("modelFile has been removed; delete it from action.json. Reference skeletons are generated from the game avatar.");
            if (!allowed.Contains(field.Name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unknown MMD parameter: " + field.Name);
            if (!seen.Add(field.Name)) throw new ArgumentException("Duplicate MMD parameter: " + field.Name);
            // Camera errors are handled separately so motion/music can still play.
            if (field.Value.ValueKind == JsonValueKind.Null && !field.Name.Equals("camera", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("MMD parameter cannot be null: " + field.Name);
        }
    }

    internal static string PackFile(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new ArgumentException("MMD files must use nonempty relative paths");
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("MMD file escapes its pack directory");
        if (!File.Exists(path)) throw new FileNotFoundException("MMD pack file missing", path);
        return path;
    }
}
