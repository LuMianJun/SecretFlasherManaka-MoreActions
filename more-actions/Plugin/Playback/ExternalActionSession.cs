using System;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SFM.Actions;

internal sealed class ExternalActionSession : IDisposable
{
    readonly ActionDriver driver;
    readonly Action<string> interrupted;
    readonly Animator animator;
    readonly int scene;
    readonly MotionFaceCamera faceCamera = new();
    TPCameraController thirdPerson;
    FPCameraController writtenFirstPerson;
    Transform originalTarget;
    Transform followTarget;
    Vector3 eye, priorEyeLocal, writtenEyeLocal;
    bool faceCameraActive, disposed;
    int poseFrame = -1;

    internal PlayerController Player { get; }
    internal string ProviderId { get; }
    internal ActionDefinition Rules { get; }
    internal bool HasPoseThisFrame => poseFrame == Time.frameCount;
    internal bool IsCurrentPlayer => Player != null && Player == PlayerController.Instance && animator != null &&
        animator == Player.Pca?.Animator && scene == SceneManager.GetActiveScene().handle;

    internal ExternalActionSession(ActionDriver driver, PlayerController player, string providerId, ActionDefinition rules,
        Action<string> interrupted)
    {
        this.driver = driver;
        this.interrupted = interrupted;
        Player = player;
        ProviderId = providerId;
        Rules = rules;
        animator = player.Pca.Animator;
        scene = SceneManager.GetActiveScene().handle;
    }

    internal void Track(Vector3 thirdPersonTarget, Vector3 firstPersonEye)
    {
        if (disposed || !IsCurrentPlayer) return;
        EnsureFollowTarget();
        if (followTarget != null) followTarget.position = thirdPersonTarget;
        eye = firstPersonEye;
        poseFrame = Time.frameCount;
    }

    void EnsureFollowTarget()
    {
        if (followTarget != null) return;
        var controller = Player.Pca?.PlayerLocomotion?.tpCameraController;
        if (controller == null || controller.cameraTarget == null) return;
        thirdPerson = controller;
        originalTarget = controller.cameraTarget;
        followTarget = new GameObject("External action follow target").transform;
        followTarget.SetParent(Player.transform, false);
        thirdPerson.cameraTarget = followTarget;
    }

    internal void TrackFirstPerson(FPCameraController controller)
    {
        if (disposed || poseFrame != Time.frameCount || controller == null ||
            controller != Player.Pca?.PlayerAvatarObjectReferencer?.FPCameraController ||
            Player.Pca?.PlayerLocomotion?.baseCameraController?.IsLockCamera == true) return;
        // Native first-person follow has just calculated its own position.
        // The base camera that consumes it runs after this callback.
        var target = controller.transform;
        if (writtenFirstPerson != controller || (target.localPosition - writtenEyeLocal).sqrMagnitude > .0000001f)
            priorEyeLocal = target.localPosition;
        target.position = eye;
        writtenEyeLocal = target.localPosition;
        writtenFirstPerson = controller;
    }

    internal bool DeferFaceCamera(FaceCameraController camera) =>
        Plugin.FollowCamera.Value && faceCameraActive && faceCamera.ShouldDefer(camera);

    internal void RefreshFaceCamera()
    {
        if (!Plugin.FollowCamera.Value)
        {
            if (faceCameraActive) faceCamera.End();
            faceCameraActive = false;
            return;
        }
        if (poseFrame != Time.frameCount && !faceCameraActive) return;
        if (!faceCameraActive) faceCameraActive = faceCamera.Begin(Player);
        if (faceCameraActive) faceCamera.RefreshAfterPose();
    }

    internal void End(string reason, bool notify)
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (notify) interrupted(reason);
        }
        finally
        {
            try
            {
                if (writtenFirstPerson != null &&
                    (writtenFirstPerson.transform.localPosition - writtenEyeLocal).sqrMagnitude <= .0000001f)
                    writtenFirstPerson.transform.localPosition = priorEyeLocal;
                if (thirdPerson != null && followTarget != null && thirdPerson.cameraTarget == followTarget)
                    thirdPerson.cameraTarget = originalTarget;
                if (followTarget != null) Object.Destroy(followTarget.gameObject);
            }
            finally { faceCamera.End(); }
        }
    }

    public void Dispose() => driver.EndExternal(this, "released by provider", false);
}
