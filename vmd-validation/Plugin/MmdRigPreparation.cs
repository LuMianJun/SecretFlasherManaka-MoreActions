using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Seleka2.Actions;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.IngameManager;
using UnityEngine;

namespace Seleka2.Mmd;

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
    readonly System.Diagnostics.Stopwatch total = new();
    long maxStepTicks;

    bool Advance(IEnumerator<int> work)
    {
        var frame = System.Diagnostics.Stopwatch.StartNew();
        for (int steps = 0; steps < 8; steps++)
        {
            var operation = System.Diagnostics.Stopwatch.StartNew();
            bool more = work.MoveNext();
            maxStepTicks = Math.Max(maxStepTicks, operation.ElapsedTicks);
            if (!more) return true;
            if (frame.Elapsed.TotalMilliseconds >= 2) break;
        }
        return false;
    }
    void CancelWork()
    {
        capture?.Dispose(); build?.Dispose(); capture = null; build = null;
        if (data != null) data.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        building?.Destroy(); building = null; data = null; snapshot = null;
    }
    public void Update()
    {
        MmdRigCache.Prune();
        var current = ActionTargetApi.Target;
        int currentId = current == null ? 0 : current.GetInstanceID();
        if (currentId != targetId || revision != MmdRigCache.Revision || registryRevision != ActionRegistry.Revision)
        {
            CancelWork();
            if (currentId != targetId && targetId != 0) MmdRigCache.DetachTarget(targetId);
            target = current; targetId = currentId; revision = MmdRigCache.Revision; registryRevision = ActionRegistry.Revision;
            stableFrames = 0; index = 0; maxStepTicks = 0;
            sources = ActionRegistry.GetAll().Select(d => d.RuntimeSource).OfType<RuntimeVmdSource>().GroupBy(s => s.SkeletonKey).Select(g => g.First()).ToArray();
        }
        if (target == null || !ActionTargetApi.IsReady || InGameUiManager.Instance?.IsOpenSomeUi == true) { stableFrames = 0; return; }
        if (++stableFrames <= 4 || index >= sources.Length) return;
        var source = sources[index];
        try
        {
            if (snapshot == null)
            {
                total.Restart(); snapshot = new MmdTargetSnapshot(target); capture = snapshot.Capture().GetEnumerator();
            }
            if (capture != null)
            {
                if (!Advance(capture)) return;
                capture.Dispose(); capture = null; return;
            }
            if (data == null && build == null)
            {
                if (MmdRigCache.TryRebind(snapshot, source.ModelPath, source.Paired))
                {
                    global::Plugin.LogSource.LogInfo("MMD_RIG_READY skeleton=" + source.SkeletonKey + " mode=rebind totalMs=" + total.ElapsedMilliseconds + " maxOperationMs=" + (maxStepTicks * 1000d / System.Diagnostics.Stopwatch.Frequency).ToString("F3"));
                    index++; return;
                }
                var positions = snapshot.Positions; string path = source.ModelPath; bool paired = source.Paired;
                data = Task.Run(() => paired ? MmdRigCache.Profile(path).CopyData() : GameMmdSkeleton.CreateData(positions));
                return;
            }
            if (data != null)
            {
                if (!data.IsCompleted) return;
                var template = data.GetAwaiter().GetResult(); data = null;
                building = new RuntimeVmdRig(); build = building.BuildSteps(snapshot, template).GetEnumerator();
            }
            if (!Advance(build)) return;
            build.Dispose(); build = null;
            MmdRigCache.Publish(snapshot, source.ModelPath, source.Paired, building); building = null;
            global::Plugin.LogSource.LogInfo("MMD_RIG_READY skeleton=" + source.SkeletonKey + " mode=build totalMs=" + total.ElapsedMilliseconds + " maxOperationMs=" + (maxStepTicks * 1000d / System.Diagnostics.Stopwatch.Frequency).ToString("F3"));
            index++;
        }
        catch (Exception e)
        {
            global::Plugin.LogSource.LogWarning("MMD_RIG_PREPARE_FAILED skeleton=" + source.SkeletonKey + " " + e);
            if (capture != null || snapshot?.Shape == null)
            {
                foreach (var item in sources) MmdRigCache.Fail(target, item.ModelPath, item.Paired, e);
                CancelWork(); index = sources.Length;
            }
            else
            {
                build?.Dispose(); build = null; building?.Destroy(); building = null; data = null;
                MmdRigCache.Fail(target, source.ModelPath, source.Paired, e); index++;
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
