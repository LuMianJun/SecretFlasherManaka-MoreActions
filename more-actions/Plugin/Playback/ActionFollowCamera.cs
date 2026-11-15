using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;

namespace SFM.Actions;

// Native TP/FP/Base ticks calculate the camera from the player before the
// action output phase moves it. Replay the accepted ticks once after output.
internal sealed class ActionFollowCamera
{
    readonly List<MonoBehaviour> pending = new(3);
    PlayerController owner;
    int frame = -1;
    bool flushing;

    internal bool Defer(PlayerController player, MonoBehaviour controller)
    {
        if (flushing || player == null || player != PlayerController.Instance) return false;
        if (!IsNativeFollowCamera(player, controller)) return false;
        if (frame != Time.frameCount || owner != player)
        {
            pending.Clear();
            owner = player;
            frame = Time.frameCount;
        }
        if (!pending.Contains(controller)) pending.Add(controller);
        return true;
    }

    static bool IsNativeFollowCamera(PlayerController player, MonoBehaviour controller)
    {
        var locomotion = player.Pca?.PlayerLocomotion;
        var main = locomotion?.baseCameraController;
        if (controller == null || main == null || main.IsLockCamera || main.Camera == null || !main.Camera.isActiveAndEnabled)
            return false;
        return controller == locomotion.tpCameraController || controller == locomotion.fpCameraController || controller == main;
    }

    internal void Flush()
    {
        if (flushing || pending.Count == 0) return;
        try
        {
            if (frame != Time.frameCount || owner == null || owner != PlayerController.Instance) return;
            flushing = true;
            foreach (var controller in pending)
            {
                if (owner != PlayerController.Instance) break;
                // Call the native entry again, including other owners'
                // prefixes. An authored camera can have taken over meanwhile.
                try
                {
                    if (!IsNativeFollowCamera(owner, controller)) continue;
                    switch (controller)
                    {
                        case TPCameraController third: third.OnLateUpdate(); break;
                        case FPCameraController first: first.OnLateUpdate(); break;
                        case BaseCameraController main: main.OnLateUpdate(); break;
                    }
                }
                catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_NATIVE_CAMERA_UPDATE_FAILED " + e.Message); }
            }
        }
        finally
        {
            pending.Clear();
            owner = null;
            frame = -1;
            flushing = false;
        }
    }
}
