using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Seleka2.Mmd;

internal static class MmdRigCache
{
    static readonly ConcurrentDictionary<string, Lazy<MmdSkeleton>> profiles = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<(int Target, string Model), RuntimeVmdRig> rigs = new();
    static readonly HashSet<(int Target, string Model)> ready = new();
    static readonly Dictionary<(int Target, string Model), Exception> failures = new();
    static readonly List<RuntimeVmdRig> idle = new();
    internal static int Revision { get; private set; }
    internal static int BuildCount { get; private set; }
    internal static string ModelKey(string path, bool paired, MmdReferencePose referencePose = MmdReferencePose.A)
        => paired ? "pmd:" + Path.GetFullPath(path).ToUpperInvariant() : "game-bind-pose:" + referencePose;
    internal static MmdSkeleton Profile(string path)
    {
        path = Path.GetFullPath(path);
        return profiles.GetOrAdd(path, p => new Lazy<MmdSkeleton>(() => MmdSkeleton.ReadPmd(p, false))).Value;
    }
    static (int Target, string Model) Key(GameObject target, string path, bool paired, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        if (target == null) throw new ArgumentException("VMD target missing");
        return (target.GetInstanceID(), ModelKey(path, paired, referencePose));
    }
    internal static bool TryRebind(MmdTargetSnapshot snapshot, string path, bool paired, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(snapshot.Target, path, paired, referencePose);
        if (rigs.TryGetValue(key, out var rig))
        {
            if (!rig.Leased && rig.HelpersValid && snapshot.Matches(rig.Shape, rig.ShapePaths))
            { rig.Rebind(snapshot); ready.Add(key); failures.Remove(key); return true; }
            if (rig.Leased) return false;
            rigs.Remove(key); ready.Remove(key); Park(rig);
        }
        rig = idle.FirstOrDefault(r => r.ModelKey == key.Model && r.HelpersValid && snapshot.Matches(r.Shape, r.ShapePaths));
        if (rig == null) return false;
        idle.Remove(rig); rig.Rebind(snapshot); rigs.Add(key, rig); ready.Add(key); failures.Remove(key); return true;
    }
    internal static void Publish(MmdTargetSnapshot snapshot, string path, bool paired, RuntimeVmdRig rig, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(snapshot.Target, path, paired, referencePose); rig.ModelKey = key.Model;
        if (rigs.TryGetValue(key, out var old)) Park(old);
        rigs[key] = rig; ready.Add(key); failures.Remove(key); BuildCount++;
    }
    internal static void Fail(GameObject target, string path, bool paired, Exception error, MmdReferencePose referencePose = MmdReferencePose.A) => failures[Key(target, path, paired, referencePose)] = error;
    internal static void Prepare(GameObject target, string path, bool paired, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        // Synchronous adapter for direct API/offline callers. Gameplay uses the
        // bounded preparation pump and IsPrepared gate, never this cold path.
        try
        {
            var snapshot = MmdTargetSnapshot.Read(target);
            if (TryRebind(snapshot, path, paired, referencePose)) return;
            var rig = new RuntimeVmdRig();
            try
            {
                var data = paired ? Profile(path).CopyData() : GameMmdSkeleton.CreateData(snapshot.Positions, referencePose);
                foreach (var _ in rig.BuildSteps(snapshot, data, !paired, referencePose)) { }
                Publish(snapshot, path, paired, rig, referencePose);
            }
            catch { rig.Destroy(); throw; }
        }
        catch (Exception e) { Fail(target, path, paired, e, referencePose); throw; }
    }
    internal static bool IsPrepared(GameObject target, string path, bool paired, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(target, path, paired, referencePose);
        if (failures.TryGetValue(key, out var error)) throw new InvalidOperationException("MMD skeleton preparation failed; see BepInEx log", error);
        if (!ready.Contains(key) || !rigs.TryGetValue(key, out var rig)) return false;
        if (!rig.IsUsable || rig.target != target) { ready.Remove(key); Revision++; return false; }
        return !rig.Leased;
    }
    internal static RuntimeVmdRig Rent(GameObject target, string path, bool paired, MmdReferencePose referencePose = MmdReferencePose.A)
    {
        var key = Key(target, path, paired, referencePose);
        if (!ready.Contains(key) || !rigs.TryGetValue(key, out var rig) || !rig.IsUsable)
        { Prepare(target, path, paired, referencePose); rig = rigs[key]; }
        if (rig.Leased)
        {
            var snapshot = MmdTargetSnapshot.Read(target);
            var temporary = new RuntimeVmdRig { Retired = true, ModelKey = key.Model };
            try
            {
                var data = paired ? Profile(path).CopyData() : GameMmdSkeleton.CreateData(snapshot.Positions, referencePose);
                foreach (var _ in temporary.BuildSteps(snapshot, data, !paired, referencePose)) { }
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
        // Keep at most two previous shapes per model, in addition to live rigs.
        var matching = idle.Where(r => r.ModelKey == rig.ModelKey).ToArray();
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
        rigs.Clear(); idle.Clear(); ready.Clear(); failures.Clear(); profiles.Clear(); Revision++;
    }
}
