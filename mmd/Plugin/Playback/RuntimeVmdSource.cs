using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SFM.Actions;
using SFM.Mmd.Upstream;

namespace SFM.Mmd;

// This source shares immutable sparse keys; each playback owns a solver.
public sealed class RuntimeVmdSource : IActionSource, IPreparedActionSource
{
    static readonly Lazy<HashSet<string>> supportedNames=new(BuildSupportedNames);
    static HashSet<string> BuildSupportedNames()
    {
        var paths=MotionRig.BonePaths().ToHashSet(StringComparer.Ordinal);
        var names=paths.SelectMany(path=>new[]{path.Substring(path.LastIndexOf('/')+1),MotionRig.MmdSource(path,paths)})
            .Where(name=>name!=null).Concat(new[]{"腰","上半身3","両目"}).ToList();
        foreach(string side in new[]{"左","右"})
            foreach(string control in new[]{"肩P","肩C","腕捩","手捩","足D","ひざD","足首D","足先EX","つま先","親指０"})names.Add(side+control);
        names.AddRange(new[]{"腰キャンセル左","腰キャンセル右"});
        var encoding=System.Text.Encoding.GetEncoding(932);
        return names.SelectMany(name=>
        {
            var bytes=encoding.GetBytes(name);
            return bytes.Length<=15?new[]{name}:new[]{name,encoding.GetString(bytes,0,15)};
        }).Select(MmdBoneMap.Normalize).ToHashSet(StringComparer.Ordinal);
    }
    readonly MmdMotion motion;
    readonly MmdMotion faceMotion;
    readonly MmdGroundContactPlan groundContact;
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
        motion = UpstreamInputCompatibility.Apply(MmdMotion.Read(vmdPath));
        motion.PreparedForPlayback=true;
        this.faceMotion = MmdMorphBindings.Compose(motion, faceMotion, lipMotion);
        if (motion.tracks.Count == 0 || Duration <= 0) throw new InvalidDataException("VMD contains no playable bone motion");
        try { groundContact = MmdGroundContactPlan.Load(vmdPath, motion.lastFrame); }
        catch (Exception e) { global::Plugin.LogSource?.LogWarning($"MMD_GROUND_IK_PLAN_REJECTED file={vmdPath} reason={e.Message}"); }
        {
            var tracks=UpstreamMotionSupport.Tracks(motion).ToDictionary(track=>track.Key,track=>track.Value,StringComparer.Ordinal);
            var ignored = tracks.Keys.Where(n => !UpstreamMotionSupport.Contains(n)).ToArray();
            ModelSpecificTracks = ignored.Length;
            var unsupported=ignored.Where(n=>!supportedNames.Value.Contains(MmdBoneMap.Normalize(n))).ToArray();
            UnsupportedAnimatedTracks=unsupported.Count(n=>MmdBoneMap.Animated(tracks[n]));
            // Keep native/model-specific tracks until the actual body mesh is
            // known. A full binding report follows at CreatePose.
            global::Plugin.LogSource?.LogInfo("MMD_TRACK_AUDIT file="+vmdPath+" standard="+(tracks.Count-ignored.Length)+" additional="+(ignored.Length-unsupported.Length)+" unknown="+unsupported.Length+" unsupportedAnimated="+string.Join(",",unsupported.Where(n=>MmdBoneMap.Animated(tracks[n])).OrderBy(n=>n,StringComparer.Ordinal)));
        }
    }
    public IActionPose CreatePose(GameObject target, ActionDefinition definition)
    {
        var pose = new RuntimeVmdPose(target, definition, motion, ReferencePose, faceMotion);
        try
        {
            pose.AttachGroundContact(target, definition, groundContact);
            return pose;
        }
        catch (Exception e)
        {
            pose.DetachGroundContact();
            global::Plugin.LogSource?.LogWarning($"MMD_GROUND_IK_DISABLED id={definition.Id} phase=bind reason={e}");
            return pose;
        }
    }
}
