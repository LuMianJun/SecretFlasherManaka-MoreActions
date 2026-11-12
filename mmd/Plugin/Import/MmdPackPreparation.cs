using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SFM.Actions;
using UnityEngine;

namespace SFM.Mmd;

public sealed class MmdPackPreparation : MonoBehaviour
{
    public MmdPackPreparation(IntPtr pointer) : base(pointer) { }
    sealed record Result(RuntimeVmdSource Source, Exception FaceError, Exception LipError, CameraPack Camera, Exception CameraError,
        string CameraWarning, MmdLyricTrack Lyrics, Exception LyricsError, long ParseMs);
    sealed record Job(MmdActionPack Pack, Task<Result> Task);
    readonly List<Job> jobs = new();
    static readonly Dictionary<string, MmdLyricTrack> lyrics = new(StringComparer.Ordinal);
    static readonly SemaphoreSlim gate = new(1);
    int completed, registered;
    bool announced;
    internal static MmdLyricTrack GetLyrics(string id) => lyrics.TryGetValue(id, out var track) ? track : null;
    internal static MmdMotion ReadFace(string directory, string json)
        => ReadMorphFile(directory, json, "faceVmdFile", "face.vmd", false);
    internal static MmdMotion ReadLip(string directory, string json)
        => ReadMorphFile(directory, json, "lipVmdFile", "lip.vmd", true);
    static MmdMotion ReadMorphFile(string directory, string json, string field, string defaultName, bool mouthOnly)
    {
        using var document = JsonDocument.Parse(json);
        string path = MmdActionPacks.OptionalFile(directory, document.RootElement, field, defaultName);
        if (path == null) return null;
        var motion = MmdMotion.Read(path);
        if (mouthOnly)
            foreach (string name in motion.morphs.Keys.Where(name => !MmdMorphBindings.IsMouthMorph(name)).ToArray())
                motion.morphs.Remove(name);
        if (motion.morphs.Count == 0) throw new InvalidDataException(mouthOnly
            ? "Lip VMD contains no supported mouth tracks" : "Face VMD contains no expression tracks");
        // Only morphs are retained. Bone/IK/camera data never drives the actor.
        motion.tracks.Clear();
        return motion;
    }
    internal int Configure(string directory)
    {
        foreach (var folder in MmdActionPacks.FindFolders(directory))
        {
            try
            {
                var pack = MmdActionPacks.Read(folder);
                var task = Task.Run(async () =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        MmdMotion face = null; Exception faceError = null;
                        try { face = ReadFace(pack.Directory, pack.Json); } catch (Exception e) { faceError = e; }
                        MmdMotion lip = null; Exception lipError = null;
                        try { lip = ReadLip(pack.Directory, pack.Json); } catch (Exception e) { lipError = e; }
                        var source = new RuntimeVmdSource(pack.MotionPath, pack.ReferencePose, face, lip);
                        CameraPack camera = null; Exception cameraError = null; string cameraWarning = null;
                        try { camera = MmdCameraPacks.Read(pack.Directory, pack.Json, message => cameraWarning = message); } catch (Exception e) { cameraError = e; }
                        MmdLyricTrack lyrics = null; Exception lyricsError = null;
                        if (!string.IsNullOrEmpty(pack.Action.AudioFile))
                        {
                            try { lyrics = MmdLyricTrack.Read(pack.Directory, pack.Json); } catch (Exception e) { lyricsError = e; }
                        }
                        return new Result(source, faceError, lipError, camera, cameraError, cameraWarning, lyrics, lyricsError, timer.ElapsedMilliseconds);
                    }
                    finally { gate.Release(); }
                });
                jobs.Add(new Job(pack, task));
            }
            catch (Exception e) { global::Plugin.LogSource.LogWarning("MMD_PACK_REJECTED folder=" + folder + " reason=" + e.Message); }
        }
        return jobs.Count;
    }
    public void Update()
    {
        // Never wait for decoding here; publish at most one pack per frame.
        if (completed < jobs.Count)
        {
            var job = jobs[completed];
            if (!job.Task.IsCompleted) return;
            completed++;
            try
            {
                var result = job.Task.GetAwaiter().GetResult();
                ActionRegistry.RegisterRuntime(job.Pack.Action, result.Source, job.Pack.Directory, global::Plugin.Guid); registered++;
                MmdCameraPacks.RegisterLoaded(job.Pack.Action.Id, result.Camera);
                if (result.Lyrics != null)
                {
                    lyrics[job.Pack.Action.Id] = result.Lyrics;
                    global::Plugin.LogSource.LogInfo("MMD_LYRICS_READY id=" + job.Pack.Action.Id + " cues=" + result.Lyrics.Count);
                }
                else lyrics.Remove(job.Pack.Action.Id);
                global::Plugin.LogSource.LogInfo("MMD_VMD_READY id=" + job.Pack.Action.Id + " seconds=" + result.Source.Duration + " tracks=" + result.Source.RetainedTracks + " ignoredTracks=" + result.Source.IgnoredTracks + " parseMs=" + result.ParseMs + " skeleton=" + result.Source.SkeletonKey);
                if (result.FaceError != null) global::Plugin.LogSource.LogWarning("MMD_FACE_REJECTED id=" + job.Pack.Action.Id + " fallback=motion reason=" + result.FaceError.Message);
                if (result.LipError != null) global::Plugin.LogSource.LogWarning("MMD_LIP_REJECTED id=" + job.Pack.Action.Id + " fallback=previous reason=" + result.LipError.Message);
                if (result.CameraWarning != null) global::Plugin.LogSource.LogWarning("MMD_CAMERA_FILE_OVERRIDE id=" + job.Pack.Action.Id + " reason=" + result.CameraWarning);
                if (result.CameraError != null) global::Plugin.LogSource.LogWarning("MMD_CAMERA_PACK_REJECTED id=" + job.Pack.Action.Id + " reason=" + result.CameraError.Message);
                if (result.LyricsError != null) global::Plugin.LogSource.LogWarning("MMD_LYRICS_REJECTED id=" + job.Pack.Action.Id + " reason=" + result.LyricsError.Message);
            }
            catch (Exception e) { global::Plugin.LogSource.LogWarning("MMD_PACK_REJECTED folder=" + job.Pack.Directory + " " + e); }
        }
        if (!announced && completed == jobs.Count)
        {
            announced = true;
            global::Plugin.LogSource.LogInfo("MMD_ACTIONS_READY version=" + global::Plugin.Version + " packs=" + registered + " directVmd=true asyncPreparation=true");
        }
    }
}
