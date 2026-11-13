using System;
using System.IO;
using System.Linq;

namespace SFM.Actions;

internal static class ActionDefinitionValidation
{
    internal static (string Bundle, string Audio) Validate(ActionDefinition definition, string packDirectory, IActionSource source)
    {
        ActionStatsSchema.Validate(definition);
        ActionEventSchema.Validate(definition, source?.Duration);
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Any(c => char.IsControl(c) || c == ',')) throw new ArgumentException("Action ID cannot be empty or contain control characters or commas");
        if (string.IsNullOrWhiteSpace(definition.DisplayName) || (source == null && (string.IsNullOrWhiteSpace(definition.Bundle) || string.IsNullOrWhiteSpace(definition.ClipAsset)))) throw new ArgumentException("Action requires a label and a clip or runtime source");
        if (source != null && (!string.IsNullOrEmpty(definition.Bundle) || !string.IsNullOrEmpty(definition.ClipAsset) || !string.IsNullOrEmpty(definition.AudioAsset))) throw new ArgumentException("Runtime sources use AudioFile; bundle assets must be empty");
        if (!string.IsNullOrEmpty(definition.AudioAsset) && !string.IsNullOrEmpty(definition.AudioFile)) throw new ArgumentException("Choose AudioAsset or AudioFile");
        if (!Enum.IsDefined(typeof(ActionBodyMask), definition.BodyMask) || !double.IsFinite(definition.AudioDelaySeconds) || definition.AudioDelaySeconds < 0 || !float.IsFinite(definition.Volume) || definition.Volume < 0 || definition.Volume > 1) throw new ArgumentException("Invalid mask or audio settings");
        if (definition.Loop && (!string.IsNullOrEmpty(definition.AudioAsset) || !string.IsNullOrEmpty(definition.AudioFile))) throw new ArgumentException("Audio requires one-shot playback; silent motion loops are supported");
        if (!definition.RequiresHands && (definition.BodyMask != ActionBodyMask.LowerBody || definition.WriteFaceMorphs)) throw new ArgumentException("Hand-independent actions must use LowerBody and leave face morphs to the game");
        if (definition.BonePaths == null || definition.BonePaths.Distinct().Count() != definition.BonePaths.Length || definition.BonePaths.Any(p => !MotionRig.IsSupportedPath(p) || !ActionPolicy.InMask(p, definition.BodyMask))) throw new ArgumentException("Unknown, duplicate or out-of-mask bone path");
        var root = Path.GetFullPath(packDirectory);
        var path = source == null ? ResolveFile(root, definition.Bundle) : "";
        var audioPath = string.IsNullOrEmpty(definition.AudioFile) ? "" : ResolveFile(root, definition.AudioFile);
        return (path,audioPath);
    }

    static string ResolveFile(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new ArgumentException("Pack files must use relative paths");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("File escapes its pack directory");
        if (!File.Exists(path)) throw new FileNotFoundException("Pack file missing", path);
        return path;
    }

}
