namespace Seleka2.Actions;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;

// Reuse the original marker tracking and body-customization corrections. Only
// move its update to after the final mod pose while this playback owns it.
internal sealed class MotionFaceCamera
{
    PlayerController owner;
    FaceCameraController camera;
    Vector3 offsetBefore, motionOffset;
    bool active, refreshing;

    internal bool Begin(PlayerController player)
    {
        End();
        var native = player == null ? null : player.faceCameraController;
        if (native == null || native.player == null || native.camera == null
            || player.Pca?.PlayerAvatarObjectReferencer?.FaceCameraPosiiton == null
            || player.Pca.PlayerAvatarObjectReferencer.FaceCameraTarget == null) return false;
        owner = player; camera = native;
        offsetBefore = native.currentOffset; motionOffset = offsetBefore;
        active = true;
        return true;
    }

    bool ValidOwner() => active && owner != null && camera != null
        && owner == PlayerController.Instance && owner.faceCameraController == camera
        && owner.Pca?.PlayerAvatarObjectReferencer?.FaceCameraPosiiton != null
        && owner.Pca.PlayerAvatarObjectReferencer.FaceCameraTarget != null;

    internal bool ShouldDefer(FaceCameraController native)
        => !refreshing && camera == native && ValidOwner() && camera.isActiveAndEnabled;

    internal bool RefreshAfterPose()
    {
        if (!ValidOwner() || !camera.isActiveAndEnabled) return false;
        // Keep a separate smoothing history. Even if an inlined native caller
        // already ran with the old Animator pose, that update cannot bias this one.
        camera.currentOffset = motionOffset;
        refreshing = true;
        try
        {
            camera.OnLateUpdate();
            motionOffset = camera.currentOffset;
            return true;
        }
        finally { refreshing = false; }
    }

    internal void End()
    {
        var previous = camera;
        bool restore = ValidOwner();
        active = false; owner = null; camera = null; refreshing = false;
        if (!restore) return;
        // Called after the saved player pose is restored. Release ownership before
        // the native update so normal camera dispatch is immediately available.
        previous.currentOffset = offsetBefore;
        if (previous.isActiveAndEnabled) previous.OnLateUpdate();
    }
}
