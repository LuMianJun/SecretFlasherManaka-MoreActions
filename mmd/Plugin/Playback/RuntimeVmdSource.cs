using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SFM.Actions;

namespace SFM.Mmd;

// This source shares immutable sparse keys; each playback owns a solver.
public sealed class RuntimeVmdSource : IActionSource, IPreparedActionSource
{
    readonly MmdMotion motion;
    readonly MmdMotion faceMotion;
    public float Duration => motion.lastFrame / 30f;
    public int RetainedTracks => motion.tracks.Count;
    public int IgnoredTracks => 0;
    public int ModelSpecificTracks { get; }
    public int UnsupportedAnimatedTracks { get; }
    internal string SkeletonKey => MmdRigCache.SkeletonKey(ReferencePose);
    internal MmdReferencePose ReferencePose { get; }
    internal void Prepare(GameObject target) => MmdRigCache.Prepare(target, ReferencePose);
    public bool IsPrepared(GameObject target) => MmdRigCache.IsPrepared(target, ReferencePose);
    public RuntimeVmdSource(string vmdPath, MmdReferencePose referencePose = MmdReferencePose.A)
        : this(vmdPath, referencePose, null) { }
    internal RuntimeVmdSource(string vmdPath, MmdReferencePose referencePose, MmdMotion faceMotion, MmdMotion lipMotion = null)
    {
        if (!Enum.IsDefined(typeof(MmdReferencePose), referencePose)) throw new ArgumentOutOfRangeException(nameof(referencePose));
        ReferencePose = referencePose;
        motion = MmdMotion.Read(vmdPath);
        motion.PreparedForPlayback=true;
        this.faceMotion = MmdMorphBindings.Compose(motion, faceMotion, lipMotion);
        if (motion.tracks.Count == 0 || Duration <= 0) throw new InvalidDataException("VMD contains no playable bone motion");
        {
            var names = new HashSet<string>(GameMmdSkeleton.SupportedNames());
            var ignored = motion.tracks.Keys.Where(n => !names.Contains(n)).ToArray();
            ModelSpecificTracks = ignored.Length;
            UnsupportedAnimatedTracks=ignored.Count(n=>MmdBoneMap.Animated(motion.tracks[n]));
            // Keep native/model-specific tracks until the actual body mesh is
            // known. A full binding report follows at CreatePose.
            global::Plugin.LogSource?.LogInfo("MMD_TRACK_AUDIT file="+vmdPath+" standard="+(motion.tracks.Count-ignored.Length)+" modelSpecific="+ignored.Length+" animatedModelSpecific="+string.Join(",",ignored.Where(n=>MmdBoneMap.Animated(motion.tracks[n])).OrderBy(n=>n,StringComparer.Ordinal)));
        }
    }
    public IActionPose CreatePose(GameObject target, ActionDefinition definition) => new RuntimeVmdPose(target, definition, motion, ReferencePose, faceMotion);
}
