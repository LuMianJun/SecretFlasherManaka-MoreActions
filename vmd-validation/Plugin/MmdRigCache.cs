using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seleka2.Mmd;

internal static class MmdRigCache
{
    static readonly Dictionary<(int Target, MmdReferencePose Pose), RuntimeVmdRig> rigs = new();
    static readonly HashSet<(int Target, MmdReferencePose Pose)> ready = new();
    static readonly Dictionary<(int Target, MmdReferencePose Pose), Exception> failures = new();
    static readonly List<RuntimeVmdRig> idle = new();
    internal static int Revision { get; private set; }
    internal static int BuildCount { get; private set; }
    internal static string SkeletonKey(MmdReferencePose referencePose) => "game-bind-pose:" + referencePose;
    static (int Target, MmdReferencePose Pose) Key(GameObject target, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        if (target == null) throw new ArgumentException("VMD target missing");
        return (target.GetInstanceID(), referencePose);
    }
    internal static bool TryRebind(MmdTargetSnapshot snapshot, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(snapshot.Target, referencePose);
        if (rigs.TryGetValue(key, out var rig))
        {
            if (!rig.Leased && rig.HelpersValid && snapshot.Matches(rig.Shape, rig.ShapePaths))
            { rig.Rebind(snapshot); ready.Add(key); failures.Remove(key); return true; }
            if (rig.Leased) return false;
            rigs.Remove(key); ready.Remove(key); Park(rig);
        }
        rig = idle.FirstOrDefault(r => r.ReferencePose == key.Pose && r.HelpersValid && snapshot.Matches(r.Shape, r.ShapePaths));
        if (rig == null) return false;
        idle.Remove(rig); rig.Rebind(snapshot); rigs.Add(key, rig); ready.Add(key); failures.Remove(key); return true;
    }
    internal static void Publish(MmdTargetSnapshot snapshot, RuntimeVmdRig rig, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(snapshot.Target, referencePose);
        if (rigs.TryGetValue(key, out var old)) Park(old);
        rigs[key] = rig; ready.Add(key); failures.Remove(key); BuildCount++;
    }
    internal static void Fail(GameObject target, Exception error, MmdReferencePose referencePose = MmdReferencePose.A) => failures[Key(target, referencePose)] = error;
    internal static void Prepare(GameObject target, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        // Synchronous adapter for direct API/offline callers. Gameplay uses the
        // bounded preparation pump and IsPrepared gate, never this cold path.
        try
        {
            var snapshot = MmdTargetSnapshot.Read(target);
            if (TryRebind(snapshot, referencePose)) return;
            var rig = new RuntimeVmdRig();
            try
            {
                var data = GameMmdSkeleton.CreateData(snapshot.Positions, referencePose);
                foreach (var _ in rig.BuildSteps(snapshot, data, referencePose)) { }
                Publish(snapshot, rig, referencePose);
            }
            catch { rig.Destroy(); throw; }
        }
        catch (Exception e) { Fail(target, e, referencePose); throw; }
    }
    internal static bool IsPrepared(GameObject target, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(target, referencePose);
        if (failures.TryGetValue(key, out var error)) throw new InvalidOperationException("MMD skeleton preparation failed; see BepInEx log", error);
        if (!ready.Contains(key) || !rigs.TryGetValue(key, out var rig)) return false;
        if (!rig.IsUsable || rig.target != target) { ready.Remove(key); Revision++; return false; }
        return !rig.Leased;
    }
    internal static RuntimeVmdRig Rent(GameObject target, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(target, referencePose);
        if (!ready.Contains(key) || !rigs.TryGetValue(key, out var rig) || !rig.IsUsable)
        { Prepare(target, referencePose); rig = rigs[key]; }
        if (rig.Leased)
        {
            var snapshot = MmdTargetSnapshot.Read(target);
            var temporary = new RuntimeVmdRig { Retired = true };
            try
            {
                var data = GameMmdSkeleton.CreateData(snapshot.Positions, referencePose);
                foreach (var _ in temporary.BuildSteps(snapshot, data, referencePose)) { }
            }
            catch { temporary.Destroy(); throw; }
            rig = temporary; BuildCount++;
        }
        rig.Leased = true; return rig;
    }
    internal static void Return(RuntimeVmdRig rig)
    {
        rig.Leased = false;
        if (rig.Retired) rig.Destroy();
        else if (rig.DetachedOnReturn || rig.target == null) Park(rig);
    }
    static void Park(RuntimeVmdRig rig)
    {
        if (rig.Leased) { rig.DetachedOnReturn = true; return; }
        if (!rig.HelpersValid) { rig.Destroy(); return; }
        rig.Detach(); if (!idle.Contains(rig)) idle.Add(rig);
        // Keep at most two previous shapes per reference pose, besides live rigs.
        var matching = idle.Where(r => r.ReferencePose == rig.ReferencePose).ToArray();
        foreach (var old in matching.Take(Math.Max(0, matching.Length - 2))) { idle.Remove(old); old.Destroy(); }
    }
    internal static void DetachTarget(int targetId)
    {
        foreach (var key in rigs.Keys.Where(k => k.Target == targetId).ToArray())
        { var rig = rigs[key]; rigs.Remove(key); ready.Remove(key); failures.Remove(key); Park(rig); }
    }
    internal static void Prune()
    {
        foreach (var id in rigs.Where(p => p.Value.target == null).Select(p => p.Key.Target).Distinct().ToArray()) DetachTarget(id);
        foreach (var rig in idle.Where(r => !r.HelpersValid).ToArray()) { idle.Remove(rig); rig.Destroy(); }
    }
    // ReAdjust only requests shape validation. It is not proof of a shape change.
    internal static void Invalidate() { ready.Clear(); failures.Clear(); Revision++; }
    internal static void Clear()
    {
        foreach (var rig in rigs.Values.Concat(idle).Distinct()) { rig.Retired = true; if (!rig.Leased) rig.Destroy(); }
        rigs.Clear(); idle.Clear(); ready.Clear(); failures.Clear(); Revision++;
    }
}
