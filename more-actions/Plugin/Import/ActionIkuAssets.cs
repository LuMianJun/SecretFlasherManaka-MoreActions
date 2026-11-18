using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace SFM.Actions;

internal static class ActionIkuAssets
{
    internal static bool Matches(string path, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;
        reference = reference.Replace('\\', '/');
        if (reference.Contains('/'))
            return string.Equals(path, reference, StringComparison.OrdinalIgnoreCase);
        return string.Equals(Path.GetFileName(path), reference, StringComparison.OrdinalIgnoreCase)
            || string.Equals(UnityActionIdentity.ClipKey(path), reference, StringComparison.OrdinalIgnoreCase);
    }

    internal static AnimationClip Load(AssetBundle bundle, string[] assets, string reference, string owner, out string path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(reference)) return null;
        try
        {
            var matches = assets.Where(asset => Matches(asset, reference)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(matches.Length == 0 ? "clip missing from this bundle" : "ambiguous clip; use its full asset path");
            var clip = bundle.LoadAsset<AnimationClip>(matches[0]);
            if (clip == null || !float.IsFinite(clip.length) || clip.length <= 0)
                throw new InvalidDataException("invalid or empty animation clip");
            path = matches[0];
            return clip;
        }
        catch (Exception error)
        {
            Warn(owner, reference, error.Message);
            return null;
        }
    }

    internal static string BundleSetting(string json, string owner)
    {
        if (json == null) return null;
        using var doc = JsonDocument.Parse(json);
        foreach (var property in doc.RootElement.EnumerateObject())
            if (property.Name.Equals("ikuClipAsset", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
                if (property.Value.ValueKind != JsonValueKind.Null) Warn(owner, "", "ikuClipAsset must be a string");
                return null;
            }
        return null;
    }

    internal static void Warn(string owner, string reference, string reason) =>
        Plugin.LogSource?.LogWarning($"ACTION_IKU_ASSET_UNAVAILABLE owner={owner} asset={reference} reason={reason}");
}
