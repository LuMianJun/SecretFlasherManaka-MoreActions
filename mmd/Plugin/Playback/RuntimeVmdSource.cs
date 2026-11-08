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
    public float Duration => motion.lastFrame / 30f;
    public int RetainedTracks => motion.tracks.Count;
    public int IgnoredTracks { get; }
    internal string SkeletonKey => MmdRigCache.SkeletonKey(ReferencePose);
    internal MmdReferencePose ReferencePose { get; }
    internal void Prepare(GameObject target) => MmdRigCache.Prepare(target, ReferencePose);
    public bool IsPrepared(GameObject target) => MmdRigCache.IsPrepared(target, ReferencePose);
    public RuntimeVmdSource(string vmdPath, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        if (!Enum.IsDefined(typeof(MmdReferencePose), referencePose)) throw new ArgumentOutOfRangeException(nameof(referencePose));
        ReferencePose = referencePose;
        motion = MmdMotion.Read(vmdPath);
        if (motion.tracks.Count == 0 || Duration <= 0) throw new InvalidDataException("VMD contains no playable bone motion");
        {
            var names = new HashSet<string>(GameMmdSkeleton.SupportedNames());
            var ignored = motion.tracks.Keys.Where(n => !names.Contains(n)).ToArray();
            IgnoredTracks = ignored.Length;
            foreach (var name in ignored) motion.tracks.Remove(name);
            if (!motion.tracks.Keys.Any(n => n == "上半身" || n == "下半身" || n == "左腕" || n == "右腕")) throw new InvalidDataException("VMD has no supported standard body tracks");
        }
    }
    public IActionPose CreatePose(GameObject target, ActionDefinition definition) => new RuntimeVmdPose(target, definition, motion, ReferencePose);
}
