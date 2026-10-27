using System;
using System.IO;
using UnityEngine;
namespace Seleka2.Actions;
internal static class WaveAudio
{
    internal sealed record Decoded(string Name, int Channels, int Rate, float[] Samples);

    // Only managed data is touched here; callers may decode on a worker.
    internal static Decoded Decode(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        string Tag() => new string(reader.ReadChars(4));
        if (Tag() != "RIFF") throw new ArgumentException("AudioFile supports PCM WAV (RIFF)");
        reader.ReadUInt32(); if (Tag() != "WAVE") throw new ArgumentException("Not a WAV file");
        ushort format = 0, channels = 0, bits = 0; int rate = 0; byte[] samples = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string tag = Tag(); uint count = reader.ReadUInt32(); long end = reader.BaseStream.Position + count;
            if (end > reader.BaseStream.Length || count > 256 * 1024 * 1024) throw new ArgumentException("Invalid WAV chunk size");
            if (tag == "fmt ") { if (count < 16) throw new ArgumentException("Invalid WAV format"); format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32(); reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16(); }
            if (tag == "data") samples = reader.ReadBytes((int)count);
            reader.BaseStream.Position = end + (count & 1);
        }
        if (channels < 1 || channels > 2 || rate < 8000 || rate > 192000 || samples == null || samples.Length == 0 || !((format == 1 && bits == 16) || (format == 3 && bits == 32))) throw new ArgumentException("Use mono/stereo PCM16 or float32 WAV, 8-192 kHz");
        int bytes = bits / 8; if (samples.Length % (bytes * channels) != 0) throw new ArgumentException("Truncated WAV samples");
        var data = new float[samples.Length / bytes];
        for (int i = 0; i < data.Length; i++) { float value = format == 1 ? BitConverter.ToInt16(samples, i * 2) / 32768f : BitConverter.ToSingle(samples, i * 4); if (!float.IsFinite(value)) throw new ArgumentException("Invalid WAV sample"); data[i] = Math.Clamp(value, -1f, 1f); }
        return new Decoded(Path.GetFileNameWithoutExtension(path), channels, rate, data);
    }

    // AudioClip operations remain on the Unity thread, with bounded transfers.
    internal sealed class Upload : IDisposable
    {
        const int BlockSamples = 262144;
        Decoded data;
        float[] transfer;
        int offset;
        internal AudioClip Clip { get; private set; }
        internal Upload(Decoded decoded) { data = decoded; }
        internal bool Step()
        {
            if (data == null) return true;
            if (Clip == null) { Clip = AudioClip.Create(data.Name, data.Samples.Length / data.Channels, data.Channels, data.Rate, false); Clip.hideFlags = HideFlags.HideAndDontSave; }
            int count = Math.Min(BlockSamples, data.Samples.Length - offset);
            if (transfer == null || transfer.Length != count) transfer = new float[count];
            Array.Copy(data.Samples, offset, transfer, 0, count);
            if (!Clip.SetData(transfer, offset / data.Channels)) throw new Exception("WAV sample upload failed");
            offset += count;
            if (offset < data.Samples.Length) return false;
            data = null; transfer = null; return true;
        }
        public void Dispose() { if (Clip != null) UnityEngine.Object.Destroy(Clip); Clip = null; data = null; transfer = null; }
    }
}
