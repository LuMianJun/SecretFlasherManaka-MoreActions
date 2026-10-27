using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Seleka2.Mmd;

internal readonly record struct CameraSample(Vector3 Center, Quaternion Rotation, float Distance, float Fov, bool Perspective);

// Independent of Unity and game assemblies so the binary format and coordinate
// conversion can be checked against known camera fixtures offline.
internal sealed class VmdCameraMotion
{
    internal sealed record Key(uint Frame, Vector3 Center, Quaternion Rotation, float Distance, float Fov, bool Perspective, byte[] Curves);
    readonly Key[] keys;
    internal int KeyCount => keys.Length;
    internal double Duration => keys[^1].Frame / 30d;
    VmdCameraMotion(Key[] value) { keys = value; }

    internal static VmdCameraMotion Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        // Some MMD exports leave arbitrary bytes after the first NUL in this
        // fixed-width field (the bundled WAVEFILE camera does exactly that).
        string header = Encoding.ASCII.GetString(Exact(reader, 30)).Split('\0')[0];
        if (header == "Vocaloid Motion Data 0002") Exact(reader, 20);
        else if (header == "Vocaloid Motion Data file") Exact(reader, 10);
        else throw new InvalidDataException("Unsupported VMD header");
        SkipRecords(reader, 111); // Bone name + frame + position + quaternion + curves.
        SkipRecords(reader, 23);  // Morph name + frame + weight.
        uint count = reader.ReadUInt32();
        if (count == 0 || count > 200000 || count * 61L > stream.Length - stream.Position)
            throw new InvalidDataException("Missing, excessive or truncated VMD camera tracks");
        var list = new Key[count];
        for (int i = 0; i < count; i++)
        {
            uint frame = reader.ReadUInt32();
            float distance = Finite(reader.ReadSingle());
            var center = new Vector3(Finite(reader.ReadSingle()), Finite(reader.ReadSingle()), Finite(reader.ReadSingle()));
            var euler = new Vector3(Finite(reader.ReadSingle()), Finite(reader.ReadSingle()), Finite(reader.ReadSingle()));
            byte[] curves = Exact(reader, 24);
            uint fov = reader.ReadUInt32(); byte projection = reader.ReadByte();
            if (fov < 1 || fov >= 180 || projection > 1 || curves.Any(v => v > 127)) throw new InvalidDataException("Invalid VMD camera projection or interpolation");
            // MMD Euler order XYZ, radians, with negated camera angles. Do not
            // feed these values into Unity's Euler(), which uses a different order.
            var rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -euler.X)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -euler.Y)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -euler.Z));
            list[i] = new Key(frame, center, rotation, distance, fov, projection == 0, curves);
        }
        // Last record wins at duplicate frames; stable ordering is deliberate.
        return new VmdCameraMotion(list.GroupBy(k => k.Frame).Select(g => g.Last()).OrderBy(k => k.Frame).ToArray());
    }

    static float Finite(float value) => float.IsFinite(value) ? value : throw new InvalidDataException("Non-finite camera value");
    static byte[] Exact(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);
        return bytes.Length == count ? bytes : throw new EndOfStreamException("Truncated VMD");
    }
    static void SkipRecords(BinaryReader reader, int stride)
    {
        uint count = reader.ReadUInt32(); long bytes = count * (long)stride;
        if (count > 2000000 || bytes > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Truncated or excessive VMD records");
        reader.BaseStream.Seek(bytes, SeekOrigin.Current);
    }

    internal CameraSample Sample(double seconds)
    {
        if (!double.IsFinite(seconds)) throw new ArgumentException("Invalid camera time");
        double frame = Math.Max(0, seconds) * 30;
        if (frame <= keys[0].Frame) return Value(keys[0]);
        if (frame >= keys[^1].Frame) return Value(keys[^1]);
        int lo = 0, hi = keys.Length - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (keys[mid].Frame <= frame) lo = mid; else hi = mid; }
        var a = keys[lo]; var b = keys[hi];
        // Adjacent-frame keys encode a cut: keep the old camera until the new key.
        float t = b.Frame - a.Frame == 1 ? 0 : (float)((frame - a.Frame) / (b.Frame - a.Frame));
        float C(int channel) => Bezier(b.Curves, channel, t);
        static float L(float x, float y, float t) => x + (y - x) * t;
        return new CameraSample(new Vector3(L(a.Center.X, b.Center.X, C(0)), L(a.Center.Y, b.Center.Y, C(1)), L(a.Center.Z, b.Center.Z, C(2))),
            Quaternion.Normalize(Quaternion.Slerp(a.Rotation, b.Rotation, C(3))), L(a.Distance, b.Distance, C(4)), L(a.Fov, b.Fov, C(5)), a.Perspective);
    }
    static CameraSample Value(Key key) => new(key.Center, key.Rotation, key.Distance, key.Fov, key.Perspective);

    internal static float Bezier(byte[] curves, int channel, float x)
    {
        if (x <= 0 || x >= 1) return Math.Clamp(x, 0, 1);
        int at = channel * 4;
        float x1 = curves[at] / 127f, x2 = curves[at + 1] / 127f, y1 = curves[at + 2] / 127f, y2 = curves[at + 3] / 127f;
        float lo = 0, hi = 1, t = 0;
        for (int i = 0; i < 24; i++) { t = (lo + hi) / 2; float bx = Cubic(t, x1, x2); if (bx < x) lo = t; else hi = t; }
        return Cubic(t, y1, y2);
    }
    static float Cubic(float t, float a, float b) => 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;

    internal static (Vector3 Position, Quaternion Rotation, float OrthographicSize) ToGame(CameraSample sample, Vector3 origin, Quaternion yaw, float scale, float height)
    {
        // Same 180-degree Y basis as the existing character retargeter. Signed
        // distance is an offset along raw MMD camera Z, not an absolute radius.
        var basis = new Quaternion(0, 1, 0, 0);
        var rawPosition = sample.Center + Vector3.Transform(new Vector3(0, 0, sample.Distance), sample.Rotation);
        var position = origin + Vector3.Transform(Vector3.Transform(rawPosition, basis) * scale, yaw) + Vector3.UnitY * height;
        var rotation = Quaternion.Normalize(yaw * basis * sample.Rotation);
        // Preserve apparent framing when VMD switches off perspective.
        float size = Math.Max(0.001f, Math.Abs(sample.Distance) * scale * MathF.Tan(sample.Fov * MathF.PI / 360));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) || !float.IsFinite(size))
            throw new InvalidDataException("Camera transform exceeds the supported numeric range");
        return (position, rotation, size);
    }
}
