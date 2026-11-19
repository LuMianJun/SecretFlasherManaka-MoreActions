using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Player.Scripts.Other;
using ExposureUnnoticed2.Object3D.SpermBullet;
using HarmonyLib;
using UnityEngine;

namespace SFM.Actions;

internal sealed class NativePlayerParticles
{
    readonly Queue<Action> pending = new();
    readonly ParticleEmitterFollow shiofukiFollow = new();
    readonly ParticleEmitterFollow spermFollow = new();
    readonly ParticleEmitterFollow peeFollow = new();
    int frame = -1;
    bool ready, peeQueued;
    PlayerBreathController breath;
    bool refreshingBreath;
    internal bool PoseReady => ready && frame == Time.frameCount;

    internal void CaptureShiofuki(PlayerController player) =>
        shiofukiFollow.Capture(player.Pca?.PlayerAvatarObjectReferencer?.ShiofukiParticle, player.transform);

    internal void CaptureSpermAfterEmission(PlayerController player)
    {
        var emitter = player.Pca?.PlayerAvatarObjectReferencer?.SpermParticle;
        // EmitShiofuki can also skip this effect. Keep the original gates
        // and only take ownership once the native call has started it.
        if (emitter && emitter.isPlaying)
            spermFollow.Capture(emitter, player.transform);
    }

    internal void UpdateEmitters(PlayerController player)
    {
        var avatar = player?.Pca?.PlayerAvatarObjectReferencer;
        shiofukiFollow.ReleaseIfStopped(avatar?.ShiofukiParticle);
        spermFollow.ReleaseIfStopped(avatar?.SpermParticle);
        peeFollow.ReleaseIfStopped(avatar?.PeeParticle);
    }

    internal void ReleaseEmitters()
    {
        try { shiofukiFollow.Release(); }
        finally
        {
            try { spermFollow.Release(); }
            finally { peeFollow.Release(); }
        }
    }

    internal bool DeferBreath(PlayerBreathController controller)
    {
        BeginFrame();
        if (ready || refreshingBreath) return false;
        breath = controller;
        return true;
    }

    internal bool Defer(Action emit, bool startsPee)
    {
        BeginFrame();
        if (ready) return false;
        // Both the action-stat hook and the native input path may request a start
        // before the deferred call has set IsPeeing.
        if (!startsPee || !peeQueued) pending.Enqueue(emit);
        peeQueued |= startsPee;
        return true;
    }

    internal void AfterPose(PlayerController player)
    {
        BeginFrame();
        ready = true;
        shiofukiFollow.AfterPose();
        spermFollow.AfterPose();
        FlushBreath();
        // A native call may synchronously enter another effect, such as FireSperm
        // inside EmitShiofuki. With the pose ready the nested call runs directly.
        while (pending.TryDequeue(out var emit))
        {
            try { emit(); }
            catch (Exception error) { Plugin.LogSource.LogWarning("ACTION_PLAYER_PARTICLE_FAILED " + error); }
        }
        // The native controller owns start/stop and may switch the emitter's
        // parent. Capture its current binding only after those changes.
        if (!ready || player == null || player != PlayerController.Instance) return;
        try
        {
            var emitter = player.Pca?.PlayerAvatarObjectReferencer?.PeeParticle;
            if (emitter && emitter.isPlaying)
            {
                peeFollow.Capture(emitter, player.transform);
                peeFollow.AfterPose();
            }
        }
        catch (Exception error) { Plugin.LogSource.LogWarning("ACTION_PEE_FOLLOW_FAILED " + error); }
    }

    internal void Clear()
    {
        pending.Clear();
        ready = peeQueued = false;
    }

    internal void FlushBreath()
    {
        BeginFrame();
        var controller = breath;
        breath = null;
        if (controller == null || PlayerController.Instance?.Pca?.PlayerBreathController?.Pointer != controller.Pointer)
            return;
        try
        {
            refreshingBreath = true;
            controller.OnLateUpdate();
        }
        catch (Exception error) { Plugin.LogSource.LogWarning("ACTION_BREATH_FOLLOW_FAILED " + error); }
        finally { refreshingBreath = false; }
    }

    void BeginFrame()
    {
        if (frame == Time.frameCount) return;
        Clear();
        breath = null;
        frame = Time.frameCount;
    }
}

[HarmonyPatch(typeof(PlayerShiofukiController), nameof(PlayerShiofukiController.EmitShiofuki))]
static class ActionShiofukiOrderPatch
{
    static bool replaying;

    static bool Prefix(PlayerShiofukiController __instance)
    {
        if (replaying) return true;
        var driver = ActionDriver.Current;
        var player = driver?.ParticlePlayer;
        if (player?.Pca?.PlayerShiofukiController?.Pointer != __instance.Pointer) return true;
        bool queued = driver.DeferParticle(() =>
        {
            replaying = true;
            try
            {
                driver.CaptureShiofuki(player);
                __instance.EmitShiofuki();
                driver.CaptureSpermAfterEmission(player);
            }
            finally { replaying = false; }
        });
        if (!queued) driver.CaptureShiofuki(player);
        return !queued;
    }

    static void Postfix(PlayerShiofukiController __instance, bool __runOriginal)
    {
        if (replaying || !__runOriginal) return;
        var driver = ActionDriver.Current;
        var player = driver?.ParticlePlayer;
        if (player?.Pca?.PlayerShiofukiController?.Pointer != __instance.Pointer) return;
        driver.CaptureSpermAfterEmission(player);
    }
}

[HarmonyPatch(typeof(PlayerPeeController), nameof(PlayerPeeController.StartPee))]
static class ActionPeeOrderPatch
{
    static bool Prefix(PlayerPeeController __instance)
    {
        var driver = ActionDriver.Current;
        return driver == null || driver.ParticlePlayer?.Pca?.PlayerPeeController?.Pointer != __instance.Pointer ||
            !driver.DeferParticle(__instance.StartPee, startsPee: true);
    }
}

// IL2CPP interop exposes the original <PlayBreath>b__16_0 under this C# name.
[HarmonyPatch(typeof(PlayerBreathController), nameof(PlayerBreathController._PlayBreath_b__16_0))]
static class ActionBreathOrderPatch
{
    static bool Prefix(PlayerBreathController __instance, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        var driver = ActionDriver.Current;
        var player = driver?.ParticlePlayer;
        if (player?.Pca?.PlayerBreathController?.Pointer != __instance.Pointer) return true;
        return !driver.DeferParticle(__instance._PlayBreath_b__16_0);
    }
}

[HarmonyPatch(typeof(PlayerBreathController), nameof(PlayerBreathController.OnLateUpdate))]
[HarmonyPriority(Priority.Last)]
static class ActionBreathFollowOrderPatch
{
    static bool Prefix(PlayerBreathController __instance, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        var driver = ActionDriver.Current;
        return driver == null || driver.ParticlePlayer?.Pca?.PlayerBreathController?.Pointer != __instance.Pointer ||
            !driver.DeferBreathFollow(__instance);
    }
}

[HarmonyPatch(typeof(FutanariController), nameof(FutanariController.FireSperm))]
static class ActionSpermOrderPatch
{
    internal static PlayerController SpawningFor { get; private set; }

    static bool Prefix(FutanariController __instance, bool __runOriginal, out bool __state)
    {
        __state = false;
        if (!__runOriginal) return false;
        if (SpawningFor != null) return true;
        var driver = ActionDriver.Current;
        var player = driver?.ParticlePlayer;
        if (player?.Pca?.FutanariController?.Pointer != __instance.Pointer) return true;
        if (driver.DeferParticle(__instance.FireSperm)) return false;
        __state = true;
        SpawningFor = player;
        return true;
    }

    static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state) SpawningFor = null;
        return __exception;
    }
}

[HarmonyPatch(typeof(SpermBullet), nameof(SpermBullet.Awake))]
static class ActionProjectileSpawnPatch
{
    static void Postfix(SpermBullet __instance, bool __runOriginal)
    {
        var player = ActionSpermOrderPatch.SpawningFor;
        if (!__runOriginal || player == null || player != PlayerController.Instance) return;
        ActionProjectileUpdatePatch.Track(__instance, player);
    }
}

[HarmonyPatch(typeof(SpermBullet), nameof(SpermBullet.Update))]
[HarmonyPriority(Priority.Last)]
static class ActionProjectileUpdatePatch
{
    sealed class TrackedProjectile
    {
        internal readonly SpermBullet Bullet;
        internal readonly PlayerController Player;
        internal int DeferredFrame = -1;

        internal TrackedProjectile(SpermBullet bullet, PlayerController player)
        {
            Bullet = bullet;
            Player = player;
        }
    }

    static readonly List<TrackedProjectile> tracked = new();

    internal static void Track(SpermBullet bullet, PlayerController player) => tracked.Add(new(bullet, player));
    internal static void Clear() => tracked.Clear();
    internal static void Prune() => tracked.RemoveAll(item => !item.Bullet);

    static bool Prefix(SpermBullet __instance, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (tracked.Count == 0) return true;
        var item = tracked.Find(entry => entry.Bullet == __instance);
        if (item == null) return true;
        var driver = ActionDriver.Current;
        if (__instance.isFirstFrame && item.Player != null && driver?.ParticlePlayer == item.Player &&
            !driver.ParticlePoseWritten)
        {
            if (item.DeferredFrame == Time.frameCount) return false;
            if (driver.DeferParticle(() => { if (__instance) __instance.Update(); }))
            {
                item.DeferredFrame = Time.frameCount;
                return false;
            }
        }
        return true;
    }

    static void Postfix(SpermBullet __instance, bool __runOriginal)
    {
        if (__runOriginal && tracked.Count != 0 && __instance && !__instance.isFirstFrame)
            tracked.RemoveAll(item => item.Bullet == __instance);
    }
}
