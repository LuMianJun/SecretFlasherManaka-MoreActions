using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Seleka2.Mmd;

internal sealed class CameraOptions
{
    public string VmdFile { get; set; } = "";
    public bool Enabled { get; set; } = true;
    // Positive offset delays the camera relative to the motion; audio delay is
    // independent. Scale converts raw MMD units into game units.
    public double TimeOffsetSeconds { get; set; }
    public float Scale { get; set; } = 0.08f;
    public float HeightOffset { get; set; }
}

internal sealed record CameraPack(CameraOptions Options, VmdCameraMotion Motion);
internal static class MmdCameraPacks
{
    static readonly Dictionary<string, CameraPack> packs = new(StringComparer.Ordinal);
    internal static CameraPack Get(string id) => packs.TryGetValue(id, out var pack) ? pack : null;
    internal static void Register(string id, string directory, string json)
        => RegisterLoaded(id, Read(directory, json));
    internal static void RegisterLoaded(string id, CameraPack pack) { if (pack != null) packs.Add(id, pack); }
    internal static CameraPack Read(string directory, string json)
    {
        using var doc = JsonDocument.Parse(json);
        bool supplied = MmdActionPacks.TryField(doc.RootElement, "camera", out var camera);
        if (supplied) MmdActionPacks.CheckFields(camera, "vmdFile", "enabled", "timeOffsetSeconds", "scale", "heightOffset");
        var options = supplied ? JsonSerializer.Deserialize<CameraOptions>(camera.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : new CameraOptions();
        if (!options.Enabled) return null;
        bool specifiedFile = supplied && MmdActionPacks.TryField(camera, "vmdFile", out _);
        if (!specifiedFile)
        {
            if (!File.Exists(Path.Combine(directory, "camera.vmd"))) return null;
            options.VmdFile = "camera.vmd";
        }
        if (!double.IsFinite(options.TimeOffsetSeconds) || !float.IsFinite(options.Scale) || options.Scale <= 0 || options.Scale > 100
            || !float.IsFinite(options.HeightOffset)) throw new ArgumentException("Invalid camera timing, scale or height");
        string path = MmdActionPacks.PackFile(directory, options.VmdFile);
        return new CameraPack(options, VmdCameraMotion.Read(path));
    }
}
