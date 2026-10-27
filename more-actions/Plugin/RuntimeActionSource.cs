using System;
using UnityEngine;

namespace Seleka2.Actions;

// Optional providers evaluate imported motion without baking an AnimationClip.
// Every player/preview receives its own pose and restoration state.
public interface IActionPose : IDisposable
{
    int BoneCount { get; }
    bool Apply(float seconds);
}

// Restore the previous face overlay before native animation/face evaluation.
public interface IActionFacePose
{
    void RestoreNativeFace();
}

// Sampled by the provider after Apply. Both vectors use world axes relative to
// the immutable playback heading, and contain horizontal displacement only.
public interface IActionRootMotionPose
{
    bool HasRootMotion { get; }
    Vector3 RootDisplacement { get; }
    Vector3 LoopRootDisplacement { get; }
}

public interface IActionSource
{
    float Duration { get; }
    IActionPose CreatePose(GameObject target, ActionDefinition definition);
}

public interface IPreparedActionSource
{
    bool IsPrepared(GameObject target);
}

// Published by the common runtime after native avatar initialization. Providers
// can prepare against it without taking ownership of the native Animator.
public static class ActionTargetApi
{
    public static GameObject Target { get; internal set; }
    public static bool IsReady { get; internal set; }
}
