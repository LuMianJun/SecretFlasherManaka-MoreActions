using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace SFM.Actions;
internal static class WaveAudio
{
    internal sealed record Decoded(string Name, int Channels, int Rate, float[] Samples);
    sealed record Format(int Channels, int Rate, ushort Encoding, int BytesPerSample,
        long DataOffset, int DataBytes, bool UseNative)
    {
        internal int Frames => DataBytes / (Channels * BytesPerSample);
        internal bool Matches(AudioClip clip)
        {
            if (clip.samples != Frames || clip.channels != Channels || clip.frequency != Rate)
                return false;
            double seconds = (double)Frames / Rate;
            // Unity can report clip length rounded down to milliseconds.
            return Math.Abs(clip.length - seconds) <= 0.001d + Math.Abs((float)seconds - seconds);
        }
    }

    static Format ReadFormat(BinaryReader reader)
    {
        string Tag() => new string(reader.ReadChars(4));
        if (Tag() != "RIFF") throw new ArgumentException("AudioFile supports PCM WAV (RIFF)");
        reader.ReadUInt32(); if (Tag() != "WAVE") throw new ArgumentException("Not a WAV file");
        ushort format = 0, channels = 0, bits = 0;
        int rate = 0, dataBytes = 0, formatChunks = 0, dataChunks = 0;
        long dataOffset = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string tag = Tag(); uint count = reader.ReadUInt32(); long end = reader.BaseStream.Position + count;
            if (end > reader.BaseStream.Length || count > 256 * 1024 * 1024) throw new ArgumentException("Invalid WAV chunk size");
            if (tag == "fmt ") { if (count < 16) throw new ArgumentException("Invalid WAV format"); format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32(); reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16(); formatChunks++; }
            if (tag == "data") { dataOffset = reader.BaseStream.Position; dataBytes = (int)count; dataChunks++; }
            reader.BaseStream.Position = end + (count & 1);
        }
        if (channels < 1 || channels > 2 || rate < 8000 || rate > 192000 || dataBytes == 0 || !((format == 1 && bits == 16) || (format == 3 && bits == 32))) throw new ArgumentException("Use mono/stereo PCM16 or float32 WAV, 8-192 kHz");
        int bytes = bits / 8;
        if (dataBytes % (bytes * channels) != 0) throw new ArgumentException("Truncated WAV samples");
        // The PCM reader preserves handling of nonfinite/over-range floats and repeated chunks.
        bool useNative = format == 1 && formatChunks == 1 && dataChunks == 1;
        return new Format(channels, rate, format, bytes, dataOffset, dataBytes, useNative);
    }

    // Only managed data is touched here; callers may decode on a worker.
    internal static Decoded Decode(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        var format = ReadFormat(reader);
        reader.BaseStream.Position = format.DataOffset;
        var samples = reader.ReadBytes(format.DataBytes);
        if (samples.Length != format.DataBytes) throw new EndOfStreamException("Truncated WAV samples");
        var data = new float[samples.Length / format.BytesPerSample];
        for (int i = 0; i < data.Length; i++)
        {
            float value = format.Encoding == 1 ? BitConverter.ToInt16(samples, i * 2) / 32768f : BitConverter.ToSingle(samples, i * 4);
            if (!float.IsFinite(value)) throw new ArgumentException("Invalid WAV sample");
            data[i] = Math.Clamp(value, -1f, 1f);
        }
        return new Decoded(Path.GetFileNameWithoutExtension(path), format.Channels, format.Rate, data);
    }

    internal sealed class Loader : IDisposable
    {
        static readonly SemaphoreSlim decodeGate = new(1);
        readonly string path;
        readonly Task<Format> header;
        NativeAudioLoad native;
        Task<Decoded> decode;
        Upload upload;
        AudioClip clip;
        bool usePcm, completed;
        internal string Backend => usePcm ? "wav-pcm" : "wav-native";
        internal string FallbackReason { get; private set; }
        internal long MaxUploadMs { get; private set; }

        internal Loader(string path)
        {
            this.path = path;
            header = Task.Run(() =>
            {
                using var reader = new BinaryReader(File.OpenRead(path));
                return ReadFormat(reader);
            });
        }

        internal bool Step()
        {
            if (completed) return true;
            if (usePcm) return StepPcm();
            if (!header.IsCompleted) return false;
            Format format = header.GetAwaiter().GetResult();
            if (!format.UseNative)
            {
                usePcm = true;
                return StepPcm();
            }

            try
            {
                native ??= new NativeAudioLoad(path, AudioType.WAV);
                if (!native.Step()) return false;
                if (!format.Matches(native.Clip))
                    throw new InvalidDataException($"Native WAV clip differs from its header (expected {format.Frames} frames, {format.Rate} Hz, {format.Channels} channels; got {native.Clip.samples}, {native.Clip.frequency}, {native.Clip.channels}, {native.Clip.length} s)");
                clip = native.TakeClip();
                native.Dispose();
                native = null;
                return completed = true;
            }
            catch (Exception error)
            {
                native?.Dispose();
                native = null;
                FallbackReason = error.Message;
                usePcm = true;
                return StepPcm();
            }
        }

        bool StepPcm()
        {
            if (decode == null && upload == null)
            {
                decode = Task.Run(async () =>
                {
                    await decodeGate.WaitAsync().ConfigureAwait(false);
                    try { return Decode(path); }
                    finally { decodeGate.Release(); }
                });
                return false;
            }
            if (upload == null)
            {
                if (!decode.IsCompleted) return false;
                upload = new Upload(decode.GetAwaiter().GetResult());
                decode = null;
            }
            var timer = Stopwatch.StartNew();
            bool ready = upload.Step();
            MaxUploadMs = Math.Max(MaxUploadMs, timer.ElapsedMilliseconds);
            if (!ready) return false;
            clip = upload.Clip;
            upload = null;
            return completed = true;
        }

        internal AudioClip TakeClip()
        {
            if (!completed || clip == null) throw new InvalidOperationException("WAV clip is not ready");
            var result = clip;
            clip = null;
            return result;
        }

        public void Dispose()
        {
            native?.Dispose(); native = null;
            upload?.Dispose(); upload = null;
            if (clip != null) UnityEngine.Object.Destroy(clip);
            clip = null;
        }
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
