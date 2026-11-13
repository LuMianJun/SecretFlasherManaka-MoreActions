using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using SFM.Actions;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;

namespace SFM.Mmd;

public sealed class MmdRigPreparation : MonoBehaviour
{
    public MmdRigPreparation(IntPtr pointer) : base(pointer) { }
    GameObject target;
    int targetId, revision = -1, registryRevision = -1, stableFrames, index;
    RuntimeVmdSource[] sources = Array.Empty<RuntimeVmdSource>();
    MmdTargetSnapshot snapshot;
    IEnumerator<int> capture, build;
    Task<MmdSkeleton> data;
    RuntimeVmdRig building;

    bool Advance(IEnumerator<int> work,int maxSteps)
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        for (int steps = 0; steps < maxSteps; steps++)
        {
            bool more = work.MoveNext();
            if (!more) return true;
            if ((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency >= 2) break;
        }
        return false;
    }
    void CancelBuild()
    {
        build?.Dispose();build=null;
        if (data != null) data.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        building?.Destroy(); building = null; data = null;
    }
    void CancelWork()
    {
        capture?.Dispose();capture=null;CancelBuild();snapshot=null;
    }
    public void Update()
    {
        MmdRigCache.Prune();
        var current = ActionTargetApi.Target;
        int currentId = current == null ? 0 : current.GetInstanceID();
        bool targetChanged=currentId!=targetId||revision!=MmdRigCache.Revision;
        bool requiredPosesChanged=false;
        if(registryRevision!=ActionRegistry.Revision)
        {
            registryRevision=ActionRegistry.Revision;
            var required=ActionRegistry.GetAll().Select(d=>d.RuntimeSource).OfType<RuntimeVmdSource>()
                .GroupBy(s=>s.SkeletonKey).Select(g=>g.First()).OrderBy(s=>s.SkeletonKey,StringComparer.Ordinal).ToArray();
            requiredPosesChanged=!sources.Select(s=>s.SkeletonKey).SequenceEqual(required.Select(s=>s.SkeletonKey));
            sources=required;
        }
        if (targetChanged)
        {
            CancelWork();
            if (currentId != targetId && targetId != 0) MmdRigCache.DetachTarget(targetId);
            target = current; targetId = currentId; revision = MmdRigCache.Revision;
            stableFrames = 0; index = 0;
        }
        else if(requiredPosesChanged)
        {
            // Availability/labels/aliases do not change the actor's geometry.
            // Only a different A/T requirement reschedules builds; the same
            // actor snapshot and any ongoing reference capture remain valid.
            CancelBuild();index=0;
        }
        if (target == null || !ActionTargetApi.IsReady) { stableFrames = 0; return; }
        if (++stableFrames <= 4 || index >= sources.Length) return;
        var source = sources[index];
        try
        {
            if (snapshot == null)
            {
                snapshot = new MmdTargetSnapshot(target); capture = snapshot.Capture().GetEnumerator();
            }
            if (capture != null)
            {
                if (!Advance(capture,256)) return;
                capture.Dispose(); capture = null; return;
            }
            if (data == null && build == null)
            {
                if (MmdRigCache.TryRebind(snapshot, source.ReferencePose))
                {
                    global::Plugin.LogSource.LogInfo("MMD_RIG_READY skeleton=" + source.SkeletonKey + " mode=rebind");
                    index++;return;
                }
                var positions = snapshot.Positions; var referencePose = source.ReferencePose;
                data = Task.Run(() => GameMmdSkeleton.CreateData(positions, referencePose));
                return;
            }
            if (data != null)
            {
                if (!data.IsCompleted) return;
                var template = data.GetAwaiter().GetResult(); data = null;
                building = new RuntimeVmdRig(); build = building.BuildSteps(snapshot, template, source.ReferencePose).GetEnumerator();
            }
            if (!Advance(build,8)) return;
            build.Dispose(); build = null;
            MmdRigCache.Publish(snapshot, building, source.ReferencePose); building = null;
            global::Plugin.LogSource.LogInfo("MMD_RIG_READY skeleton=" + source.SkeletonKey + " mode=build");
            index++;
        }
        catch (Exception e)
        {
            global::Plugin.LogSource.LogWarning("MMD_RIG_PREPARE_FAILED skeleton=" + source.SkeletonKey + " " + e);
            if (capture != null || snapshot?.Shape == null)
            {
                foreach (var item in sources) MmdRigCache.Fail(target, e, item.ReferencePose);
                CancelWork(); index = sources.Length;
            }
            else
            {
                build?.Dispose(); build = null; building?.Destroy(); building = null; data = null;
                MmdRigCache.Fail(target, e, source.ReferencePose); index++;
            }
        }
    }
    public void OnDestroy()
    {
        if (ActionPlaybackApi.CurrentProviderId == global::Plugin.Guid) ActionPlaybackApi.Stop();
        CancelWork(); MmdRigCache.Clear();
    }
}

[HarmonyPatch(typeof(PlayerBoneScaleAdjustManager), "ReAdjust")]
static class MmdBodyChangedPatch { static void Postfix() => MmdRigCache.Invalidate(); }

[HarmonyPatch(typeof(PlayerBoneScaleAdjuster), "OnChangeBody")]
static class MmdBodyDataChangedPatch { static void Postfix() => MmdRigCache.Invalidate(); }
