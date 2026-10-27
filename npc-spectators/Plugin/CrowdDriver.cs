using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using ExposureUnnoticed2.Object3D.NPC.Script;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Scripts.Base;

namespace Seleka2.NpcSpectators;

public sealed class CrowdDriver : MonoBehaviour
{
    public CrowdDriver(IntPtr pointer) : base(pointer) { }
    internal static CrowdDriver Current;
    readonly Dictionary<IntPtr, Spectator> spectators = new();
    readonly Dictionary<IntPtr, float> retryAfter = new();
    readonly HashSet<int> occupied = new();
    MmdBridge bridge;
    PlayerController player;
    NpcManager manager;
    Vector3 center;
    List<CrowdSlot> slots;
    bool manual, active;
    internal bool ManualRequested
    {
        get => manual;
        set
        {
            if (value && player == null) { player = PlayerController.Instance; manager = NpcManager.Instance; }
            manual = value;
        }
    }
    int tickFrame = -1;
    float nextBridge, nextScan, faultUntil;

    public void Update()
    {
        try { Tick(); }
        catch (Exception e)
        {
            Plugin.Logger.LogError("CROWD_TICK_FAILED " + e);
            manual = false; faultUntil = Time.unscaledTime + 5; End("error");
        }
    }

    internal void Tick()
    {
        if (tickFrame == Time.frameCount) return;
        tickFrame = Time.frameCount;
        bridge ??= new MmdBridge(s => Plugin.Logger.LogInfo(s));
        if (Time.unscaledTime >= nextBridge)
        {
            nextBridge = Time.unscaledTime + 2;
            ActionEventBridge.Refresh();
            bridge.Refresh();
        }
        var currentPlayer = PlayerController.Instance;
        var currentManager = NpcManager.Instance;
        var state = currentPlayer == null ? null : currentPlayer.Pca?.PlayerState;
        var game = InGameManager.Instance;
        var temp = game == null ? null : game.TempInGameState;
        var ui = InGameUiManager.Instance;
        bool readyContext = Plugin.Enabled.Value && Time.unscaledTime >= faultUntil && currentPlayer != null
            && currentPlayer.gameObject.activeInHierarchy && currentManager != null && currentManager.initialized
            && currentManager.ExistNpcList != null && state != null && (int)state.CurrentState == 0
            && temp != null && !temp.IsTimeStop && !temp.IsGameOver && !temp.IsPlayingSomePerformance;
        bool ready = readyContext && ui != null && !ui.IsOpenFade && !ui.IsOpenSomeUi;
        if (!ready)
        {
            // Opening settings pauses gathering, but must not erase a manual
            // request made by the settings checkbox before the panel closes.
            if (!readyContext || ui == null || ui.IsOpenFade) manual = false;
            End("player/UI unavailable"); return;
        }
        if (player != currentPlayer || manager != currentManager)
        {
            manual = false; End("scene/player changed"); player = currentPlayer; manager = currentManager;
        }
        if (InputManager.GetVector3(InputManager.InputType.Move).sqrMagnitude > 0.0001f
            || InputManager.IsDownFrame(InputManager.InputType.Cancel)) manual = false;
        string nativeId = state.CurrentAction == null ? string.Empty : state.CurrentAction.Type.ToString();
        string nativeNumber = state.CurrentAction == null ? string.Empty : ((int)state.CurrentAction.Type).ToString();
        bool native = Plugin.FollowNative.Value && (CrowdPolicy.Matches(Plugin.NativeActions.Value, nativeId) || CrowdPolicy.Matches(Plugin.NativeActions.Value, nativeNumber));
        bool mmd = Plugin.FollowMmd.Value && !string.IsNullOrEmpty(bridge.MotionId) && (Plugin.FollowAllMmd.Value || CrowdPolicy.Matches(Plugin.MmdActions.Value, bridge.MotionId));
        bool requested = manual || native || mmd || CrowdApi.Requested;
        if (!requested) { End("triggers ended"); return; }
        if (active && (player.transform.position - center).sqrMagnitude > 1f) End("player moved; rebuild gathering");
        if (!active) Begin("manual=" + manual + " native=" + nativeId + " mmd=" + bridge.MotionId);
        if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.25f; Scan(); }

    }

    void Begin(string reason)
    {
        center = player.transform.position;
        float radius = FiniteClamp(Plugin.Radius.Value, 2.8f, 1.8f, 8);
        float spacing = FiniteClamp(Plugin.Spacing.Value, 1.1f, 0.8f, 3);
        slots = CrowdSlots.Create(Math.Max(128, manager.ExistNpcList.Count * 6), radius, spacing);
        active = true; nextScan = 0;
        Plugin.Logger.LogInfo("CROWD_START " + reason + " radius=" + radius + " spacing=" + spacing);
    }

    static float FiniteClamp(float value, float fallback, float min, float max)
        => float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;

    internal static bool Eligible(NpcController npc)
    {
        if (npc == null) return false;
        var nca = npc.Nca;
        var model = nca?.StateModel;
        var agent = nca?.NavAgent;
        if (model == null || npc.GameObject == null || npc.Transform == null || nca.NpcLocomotion == null || nca.NpcAnimationManager?.speed == null) return false;
        return CrowdPolicy.IsEligible(npc.IsAlive, npc.GameObject.activeInHierarchy, model.IsPolice, model.IsDestroy,
            (int)model.CurrentFixedType, (int)model.CurrentState, model.IsSit, model.HasInteractToken,
            model.RouteData != null, agent != null && agent.enabled && agent.isOnNavMesh);
    }

    void Scan()
    {
        var list = manager.ExistNpcList;
        var alive = new HashSet<IntPtr>();
        for (int i = 0; i < list.Count; i++) if (list[i] != null) alive.Add(list[i].Pointer);
        foreach (var pair in spectators.ToArray())
            if (!alive.Contains(pair.Key) || !Eligible(pair.Value.Npc)) Release(pair.Key, "NPC removed/busy");
        if (slots.Count < list.Count * 6)
            slots = CrowdSlots.Create(list.Count * 6, FiniteClamp(Plugin.Radius.Value, 2.8f, 1.8f, 8), FiniteClamp(Plugin.Spacing.Value, 1.1f, 0.8f, 3));
        int attempts = 0;
        for (int i = 0; i < list.Count && attempts < 4; i++)
        {
            var npc = list[i];
            if (npc == null || spectators.ContainsKey(npc.Pointer) || !Eligible(npc)) continue;
            if (retryAfter.TryGetValue(npc.Pointer, out float retry) && Time.unscaledTime < retry) continue;
            attempts++;
            if (!Recruit(npc)) retryAfter[npc.Pointer] = Time.unscaledTime + 5;
        }
        foreach (var key in retryAfter.Keys.Where(k => !alive.Contains(k)).ToArray()) retryAfter.Remove(key);
    }

    bool Recruit(NpcController npc)
    {
        var agent = npc.Nca.NavAgent;
        float separation = FiniteClamp(Plugin.Spacing.Value, 1.1f, 0.8f, 3);
        float radius = FiniteClamp(Plugin.Radius.Value, 2.8f, 1.8f, 8);
        Vector3 npcPosition = npc.Transform.position;
        // Prefer the inner ring and the side nearest this NPC. Every reservation uses
        // the projected point, so wall-clamped samples cannot collapse onto one spot.
        foreach (var slot in slots.Where(s => !occupied.Contains(s.Index)).OrderBy(s => s.Ring)
            .ThenBy(s => HorizontalSquare(center + new Vector3(s.X, 0, s.Z) - npcPosition)))
        {
            Vector3 desired = center + new Vector3(slot.X, 0, slot.Z);
            // This game's stripped interop omits NavMeshQueryFilter. Sample by the
            // agent's area mask, then CalculatePath on that agent verifies its type.
            if (!NavMesh.SamplePosition(desired, out var hit, 0.9f, agent.areaMask) || Math.Abs(hit.position.y - center.y) > 1f
                || HorizontalSquare(hit.position - center) < radius * radius * 0.8f
                || spectators.Values.Any(s => HorizontalSquare(s.Destination - hit.position) < separation * separation)) continue;
            var path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            var spectator = new Spectator(npc, slot.Index, hit.position);
            spectators.Add(npc.Pointer, spectator); occupied.Add(slot.Index);
            try
            {
                spectator.Acquire(path);
                Plugin.Logger.LogInfo("CROWD_RECRUIT npc=" + npc.id + " slot=" + slot.Index + " destination=" + hit.position);
                return true;
            }
            catch { Release(npc.Pointer, "recruit failed"); throw; }
        }
        return false;
    }

    internal bool Drive(NpcLocomotion locomotion)
    {
        var npc = locomotion.nca?.NpcController;
        if (!active || npc == null || !spectators.TryGetValue(npc.Pointer, out var spectator)) return false;
        try
        {
            if (!Eligible(npc)) { Release(npc.Pointer, "NPC busy"); return false; }
            spectator.Drive(center);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("CROWD_NPC_FAILED npc=" + npc.id + " " + e.Message);
            Release(npc.Pointer, "navigation failure"); retryAfter[npc.Pointer] = Time.unscaledTime + 5;
            return false;
        }
    }

    internal void Release(IntPtr pointer, string reason)
    {
        if (!spectators.TryGetValue(pointer, out var spectator)) return;
        spectators.Remove(pointer); occupied.Remove(spectator.Slot);
        try { spectator.Restore(); }
        catch (Exception e) { Plugin.Logger.LogWarning("CROWD_RESTORE_FAILED " + e.Message); }
        Plugin.Logger.LogInfo("CROWD_RELEASE npc=" + spectator.Id + " reason=" + reason);
    }

    internal void PrepareFixedStep(NpcLocomotion locomotion)
    {
        var npc = locomotion.nca?.NpcController;
        if (!active || npc == null || !spectators.TryGetValue(npc.Pointer, out var spectator)) return;
        try
        {
            if (!Eligible(npc)) { Release(npc.Pointer, "NPC busy before movement"); return; }
            spectator.PrepareFixedStep();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("CROWD_STEP_FAILED npc=" + npc.id + " " + e.Message);
            Release(npc.Pointer, "navigation step failure"); retryAfter[npc.Pointer] = Time.unscaledTime + 5;
        }
    }

    void End(string reason)
    {
        if (!active && spectators.Count == 0) return;
        active = false;
        foreach (var pointer in spectators.Keys.ToArray()) Release(pointer, reason);
        occupied.Clear(); retryAfter.Clear(); slots = null;
        Plugin.Logger.LogInfo("CROWD_END reason=" + reason);
    }
    internal static float HorizontalSquare(Vector3 v) => v.x * v.x + v.z * v.z;
    public void OnDisable() { manual = false; End("plugin disabled"); ActionEventBridge.Dispose(); }
    public void OnDestroy() { manual = false; End("plugin destroyed"); bridge?.Dispose(); bridge = null; ActionEventBridge.Dispose(); Current = null; }
}

internal sealed class Spectator
{
    internal readonly NpcController Npc;
    internal readonly int Slot, Id;
    internal readonly Vector3 Destination;
    internal bool Arrived { get; private set; }
    readonly NavMeshAgent agent;
    readonly NpcLocomotion locomotion;
    readonly bool positionBefore, rotationBefore, stoppedBefore, pathBefore;
    readonly Vector3 destinationBefore, cachedDestinationBefore;
    readonly Quaternion rotationTargetBefore;
    readonly float speedBefore;

    internal Spectator(NpcController npc, int slot, Vector3 destination)
    {
        Npc = npc; Slot = slot; Destination = destination; Id = npc.id;
        agent = npc.Nca.NavAgent; locomotion = npc.Nca.NpcLocomotion;
        positionBefore = agent.updatePosition; rotationBefore = agent.updateRotation; stoppedBefore = agent.isStopped;
        pathBefore = agent.hasPath; destinationBefore = agent.destination;
        cachedDestinationBefore = locomotion.lastSetDestinationPoint; rotationTargetBefore = locomotion.targetRotation;
        speedBefore = npc.Nca.NpcAnimationManager.speed.Target;
    }
    internal void Acquire(NavMeshPath path)
    {
        // Native FixedUpdate already moves the NPC root and resolves nearby collisions.
        // Disable automatic agent transform writes to avoid two movement owners.
        agent.updatePosition = false; agent.updateRotation = false;
        agent.nextPosition = Npc.Transform.position; agent.isStopped = false;
        locomotion.SetMoveZero();
        if (!agent.SetPath(path)) throw new InvalidOperationException("Crowd path was rejected.");
        locomotion.lastSetDestinationPoint = Destination;
    }
    internal void Drive(Vector3 center)
    {
        var root = Npc.Transform;
        var animation = Npc.Nca.NpcAnimationManager;
        agent.nextPosition = root.position;
        Arrived = CrowdDriver.HorizontalSquare(root.position - Destination) <= 0.35f * 0.35f;
        if (Arrived)
        {
            locomotion.SetMoveZero(); animation.SetSpeed(0); agent.isStopped = true;
            Vector3 facing = center - root.position; facing.y = 0;
            if (facing.sqrMagnitude > 0.001f) locomotion.targetRotation = Quaternion.LookRotation(facing);
        }
        else
        {
            agent.isStopped = false;
            if (agent.pathPending) { locomotion.SetMoveZero(); animation.SetSpeed(0); return; }
            if (!agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("Spectator path is no longer complete.");
            Vector3 direction = agent.steeringTarget - root.position; direction.y = 0;
            if (direction.sqrMagnitude < 0.0001f) { locomotion.SetMoveZero(); animation.SetSpeed(0); return; }
            direction = direction.normalized;
            locomotion.targetRotation = Quaternion.LookRotation(direction);
            animation.SetSpeed(1);
            // Reuse the native walk scale and animation smoothing. Turning reduces
            // speed without moving sideways; cap the final step at the destination.
            float speed = ExposureUnnoticed2.Master.Common.MCommon.PlayerWalkSpeed * 0.9f
                * Npc.Nca.StateModel.MoveSpeedScale * animation.GetCurrentMoveSpeed();
            float alignment = Mathf.Clamp01(Vector3.Dot(root.forward, direction));
            float remaining = Mathf.Sqrt(CrowdDriver.HorizontalSquare(root.position - Destination));
            speed = Mathf.Min(speed * alignment, remaining / Mathf.Max(Time.fixedDeltaTime * 3, 0.02f));
            locomotion.moveV = direction * speed;
            if (Time.unscaledTime - locomotion.lastCalcNearNpcTime >= 0.5f)
            {
                Npc.Nca.StateModel.NearNpc = NpcManager.Instance.GetNearOtherNpc(Npc);
                locomotion.lastCalcNearNpcTime = Time.unscaledTime;
            }
        }
        root.rotation = Quaternion.RotateTowards(root.rotation, locomotion.targetRotation, Time.deltaTime * 180);
    }
    internal void Restore()
    {
        if (Npc.GameObject == null || Npc.Transform == null) return;
        // A changed native state owns its own movement settings. Never restore the
        // patrol snapshot over a police/discovery/interaction transition.
        if (Npc.Nca.StateModel.IsDestroy || Npc.Nca.StateModel.CurrentState != NpcStateModel.State.Idle) return;
        locomotion.SetMoveZero(); locomotion.lastSetDestinationPoint = cachedDestinationBefore;
        locomotion.targetRotation = rotationTargetBefore;
        Npc.Nca.NpcAnimationManager.SetSpeed(speedBefore);
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.nextPosition = Npc.Transform.position;
        if (pathBefore && float.IsFinite(destinationBefore.x) && float.IsFinite(destinationBefore.y) && float.IsFinite(destinationBefore.z)) agent.SetDestination(destinationBefore);
        else agent.ResetPath();
        agent.updatePosition = positionBefore; agent.updateRotation = rotationBefore; agent.isStopped = stoppedBefore;
    }

    internal void PrepareFixedStep()
    {
        if (locomotion.moveV.sqrMagnitude < 0.000001f) return;
        Vector3 position = Npc.Transform.position;
        // Shopping-mall native movement accumulates up to three fixed frames for
        // distant NPCs. Project precisely the step that that method will consume.
        float dt = Time.fixedDeltaTime * (locomotion.skipFixedUpdateFrame + 1);
        if (dt <= 0) { locomotion.SetMoveZero(); return; }
        Vector3 next = position + locomotion.moveV * dt;
        agent.nextPosition = position;
        if (agent.Raycast(next, out var edge)) next = edge.position;
        Vector3 floorQuery = next; floorQuery.y -= agent.baseOffset;
        if (!NavMesh.SamplePosition(floorQuery, out var floor, 0.3f, agent.areaMask)
            || Math.Abs(floor.position.y + agent.baseOffset - position.y) > 0.5f)
            throw new InvalidOperationException("Navigation step is outside the current floor.");
        next = floor.position; next.y += agent.baseOffset;
        locomotion.moveV = (next - position) / dt;
    }
}

[HarmonyPatch(typeof(NpcLocomotion), "OnUpdate")]
static class CrowdLocomotionPatch
{
    static bool Prefix(NpcLocomotion __instance) => !(CrowdDriver.Current?.Drive(__instance) ?? false);
}

[HarmonyPatch(typeof(NpcController), "ChangeState")]
static class CrowdInterruptPatch
{
    static void Prefix(NpcController __instance, NpcStateModel.State __0)
    {
        // Release BEFORE original OnEnter writes the next state's movement settings.
        if (__0 != NpcStateModel.State.Idle) CrowdDriver.Current?.Release(__instance.Pointer, "native state transition " + __0);
    }
}

[HarmonyPatch(typeof(NpcManager), "OnUpdate")]
static class CrowdTickPatch
{
    static void Prefix() { CrowdDriver.Current?.Update(); }
}

[HarmonyPatch(typeof(NpcLocomotion), "OnFixedUpdate")]
static class CrowdFixedStepPatch
{
    static void Prefix(NpcLocomotion __instance) { CrowdDriver.Current?.PrepareFixedStep(__instance); }
}
