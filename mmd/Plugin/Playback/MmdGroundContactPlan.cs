using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace SFM.Mmd;

// All trial clips use the same contact detector. A replacement motion cannot
// inherit stale data from a plan generated for a different file.
internal sealed class MmdGroundContactPlan
{
    const string Prefix = "SFM.Mmd.ContactPlans.";
    const int LimbCount = 4;
    internal const uint Magic = 0x31434d53;
    internal const float MaxGain = 1.2f;
    internal const float MaxLift = .25f;
    static readonly Assembly Owner = typeof(MmdGroundContactPlan).Assembly;
    internal static readonly bool HasPlans = Array.Exists(Owner.GetManifestResourceNames(),
        name => name.StartsWith(Prefix, StringComparison.Ordinal));
    readonly Sample[] samples;
    readonly int frames;

    internal readonly record struct Sample(float SurfaceFromJoint, float Gain, float Lift);

    MmdGroundContactPlan(Sample[] samples, int frames)
    {
        this.samples = samples;
        this.frames = frames;
    }

    internal static MmdGroundContactPlan Load(string motionPath, uint lastFrame)
    {
        if (!HasPlans) return null;
        using var vmd = File.OpenRead(motionPath);
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(vmd));
        using var stream = Owner.GetManifestResourceStream(Prefix + hash);
        if (stream == null) return null;
        using var reader = new BinaryReader(stream);
        int count = checked((int)lastFrame + 1);
        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != count
            || stream.Length != 8L + 12L * count * LimbCount)
            throw new InvalidDataException("Invalid MMD ground contact plan.");
        var data = new Sample[count * LimbCount];
        for (int i = 0; i < data.Length; i++)
        {
            float surface = reader.ReadSingle(), gain = reader.ReadSingle(), lift = reader.ReadSingle();
            if (!float.IsFinite(surface) || !float.IsFinite(gain) || !float.IsFinite(lift)
                || gain < 0 || gain > MaxGain || lift < 0 || lift > MaxLift)
                throw new InvalidDataException("Invalid MMD ground contact sample.");
            data[i] = new Sample(surface, gain, lift);
        }
        return new MmdGroundContactPlan(data, count);
    }

    internal Sample At(float frame, int limb)
    {
        frame = Math.Clamp(frame, 0, frames - 1);
        int first = (int)frame;
        var from = samples[first * LimbCount + limb];
        if (first == frames - 1) return from;
        var to = samples[(first + 1) * LimbCount + limb];
        float mix = frame - first;
        return new Sample(from.SurfaceFromJoint + (to.SurfaceFromJoint - from.SurfaceFromJoint) * mix,
            from.Gain + (to.Gain - from.Gain) * mix, from.Lift + (to.Lift - from.Lift) * mix);
    }

    internal static float LiftForFloor(Sample sample, float floorY, float jointY, float scale)
    {
        float depth = floorY - jointY - sample.SurfaceFromJoint * scale;
        // Cached skin clearance approximates live skin. Bound the extension
        // for differences between the measured plane and the scene floor.
        return Math.Clamp(depth * sample.Gain, 0, (sample.Lift + .035f) * scale);
    }
}
