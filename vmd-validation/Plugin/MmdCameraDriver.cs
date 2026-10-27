using System;
using HarmonyLib;
using Seleka2.Actions;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Seleka2.Mmd;

public sealed class MmdCameraDriver : MonoBehaviour
{
    public MmdCameraDriver(IntPtr pointer) : base(pointer) { }
    internal static MmdCameraDriver Current;
    PlayerController owner;
    CameraPack pack;
    string actionId;
    NVector origin;
    NQuaternion yaw;
    BaseCameraController native;
    TPCameraController tp;
    FPCameraController fp;
    UnityEngine.Camera camera;
    Vector3 savedPosition;
    Vector3 savedOwnerPosition;
    Quaternion savedRotation;
    float savedFov, savedOrthoSize;
    int savedMask;
    bool savedOrtho, savedLock, bound;

    internal void Bind()
    {
        Current = this; bound = true;
        ActionPlaybackApi.PlaybackChanged += Playback;
        ActionPlaybackApi.PoseApplied += Apply;
    }
    void Playback(string id, bool active)
    {
        Stop();
        if (!active || !isActiveAndEnabled || ActionPlaybackApi.CurrentProviderId != global::Plugin.Guid) return;
        pack = MmdCameraPacks.Get(id);
        owner = PlayerController.Instance;
        if (pack == null || owner == null) { Stop(); return; }
        actionId = id;
        var position = owner.transform.position; origin = new NVector(position.x, position.y, position.z);
        var rotation = Quaternion.Euler(0, owner.transform.eulerAngles.y, 0);
        yaw = new NQuaternion(rotation.x, rotation.y, rotation.z, rotation.w);
        // First frame is applied immediately, before the next native camera tick.
        Apply(id, 0);
    }
    bool ValidAction() => isActiveAndEnabled && owner != null && owner == PlayerController.Instance
        && ActionPlaybackApi.IsPlaying && ActionPlaybackApi.CurrentActionId == actionId
        && ActionPlaybackApi.CurrentProviderId == global::Plugin.Guid && ActionRegistry.Get(actionId) != null;

    bool BeginCamera()
    {
        var movement = owner.Pca?.PlayerLocomotion;
        native = movement?.baseCameraController;
        if (native == null || native.Camera == null || !native.Camera.isActiveAndEnabled) { native = null; return false; }
        tp = movement.tpCameraController; fp = movement.fpCameraController; camera = native.Camera;
        savedPosition = camera.transform.position; savedRotation = camera.transform.rotation;
        savedOwnerPosition = owner.transform.position;
        savedFov = camera.fieldOfView; savedOrtho = camera.orthographic; savedOrthoSize = camera.orthographicSize;
        savedMask = camera.cullingMask; savedLock = native.IsLockCamera;
        native.IsLockCamera = true;
        native.SetTpsCullingMask(); // Include the player's face even when starting in first person.
        global::Plugin.LogSource.LogInfo("MMD_CAMERA_START id=" + actionId + " keys=" + pack.Motion.KeyCount + " scale=" + pack.Options.Scale + " offset=" + pack.Options.TimeOffsetSeconds);
        return true;
    }
    void Apply(string id, double time)
    {
        if (id != actionId || pack == null) return;
        try
        {
            if (!ValidAction()) { Stop(); return; }
            double cameraTime = time - pack.Options.TimeOffsetSeconds;
            if (!global::Plugin.UseCamera.Value || cameraTime < 0) { ReleaseCamera(); return; }
            if (camera == null && !BeginCamera()) return;
            if (!camera.isActiveAndEnabled || owner.Pca?.PlayerLocomotion?.baseCameraController?.Camera != camera) { ReleaseCamera(); return; }
            var sample = pack.Motion.Sample(cameraTime);
            var pose = VmdCameraMotion.ToGame(sample, origin, yaw, pack.Options.Scale, pack.Options.HeightOffset);
            var cycle = ActionPlaybackApi.RootCycleOffset;
            camera.transform.SetPositionAndRotation(new Vector3(pose.Position.X + cycle.x, pose.Position.Y + cycle.y, pose.Position.Z + cycle.z),
                new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W));
            camera.fieldOfView = sample.Fov;
            camera.orthographic = !sample.Perspective;
            if (!sample.Perspective) camera.orthographicSize = pose.OrthographicSize;
        }
        catch (Exception e) { Stop(); global::Plugin.LogSource.LogWarning("MMD_CAMERA_FAILED " + e); }
    }
    internal bool Defer(BaseCameraController value) => ShouldOwn() && native == value;
    internal bool Defer(TPCameraController value) => ShouldOwn() && tp == value;
    internal bool Defer(FPCameraController value) => ShouldOwn() && fp == value;
    bool ShouldOwn()
    {
        try
        {
            if (camera == null) return false;
            if (!global::Plugin.UseCamera.Value || !ValidAction() || !camera.isActiveAndEnabled || owner.Pca?.PlayerLocomotion?.baseCameraController?.Camera != camera) { ReleaseCamera(); return false; }
            return true;
        }
        catch (Exception e)
        {
            try { Stop(); } catch { }
            global::Plugin.LogSource.LogWarning("MMD_CAMERA_RELEASE_FAILED " + e.Message);
            return false; // A broken mod snapshot must never suppress the native tick.
        }
    }
    void ReleaseCamera()
    {
        var previous = camera; var previousNative = native;
        // Clear ownership before restoring: exceptions cannot leave a native tick blocked.
        camera = null; native = null; tp = null; fp = null;
        try
        {
            if (previous != null)
            {
                var restored = savedPosition;
                // This only restores the native follow-camera after playback;
                // the VMD camera above always keeps its original fixed anchor.
                if (owner != null)
                {
                    var p = owner.transform.position;
                    restored = new Vector3(savedPosition.x + p.x - savedOwnerPosition.x, savedPosition.y + p.y - savedOwnerPosition.y, savedPosition.z + p.z - savedOwnerPosition.z);
                }
                previous.transform.SetPositionAndRotation(restored, savedRotation);
                previous.fieldOfView = savedFov; previous.orthographic = savedOrtho;
                previous.orthographicSize = savedOrthoSize; previous.cullingMask = savedMask;
                global::Plugin.LogSource.LogInfo("MMD_CAMERA_RESTORED id=" + actionId);
            }
        }
        finally { if (previousNative != null) previousNative.IsLockCamera = savedLock; }
    }
    void Stop() { try { ReleaseCamera(); } finally { pack = null; owner = null; actionId = null; } }
    public void Update()
    {
        if (pack != null && !ValidAction()) Stop();
        if (!global::Plugin.UseCamera.Value) ReleaseCamera();
    }
    public void OnDisable() => Stop();
    public void OnDestroy()
    {
        try { Stop(); }
        finally
        {
            if (bound) { ActionPlaybackApi.PlaybackChanged -= Playback; ActionPlaybackApi.PoseApplied -= Apply; bound = false; }
            if (Current == this) Current = null;
        }
    }
}

[HarmonyPatch(typeof(BaseCameraController), "OnLateUpdate")]
static class MainCameraOwnership { static bool Prefix(BaseCameraController __instance) => !(MmdCameraDriver.Current?.Defer(__instance) ?? false); }
[HarmonyPatch(typeof(TPCameraController), "OnLateUpdate")]
static class ThirdPersonOwnership { static bool Prefix(TPCameraController __instance) => !(MmdCameraDriver.Current?.Defer(__instance) ?? false); }
[HarmonyPatch(typeof(FPCameraController), "OnLateUpdate")]
static class FirstPersonOwnership { static bool Prefix(FPCameraController __instance) => !(MmdCameraDriver.Current?.Defer(__instance) ?? false); }
