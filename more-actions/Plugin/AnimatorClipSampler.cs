using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Seleka2.Actions;

// Native Humanoid/Generic evaluation runs on a script-free, renderer-free
// proxy. Never assign a controller, Avatar or root-motion flag to the player.
internal sealed class AnimatorClipSampler : IDisposable
{
    readonly GameObject proxy;
    PlayableGraph graph;
    PlayableHandle playable;
    Vector3 position;
    Quaternion rotation = Quaternion.identity;
    float previous;
    bool disposed;

    internal AnimatorClipSampler(GameObject proxy, Animator original, AnimationClip clip)
    {
        this.proxy = proxy;
        string phase = "avatar";
        try
        {
            if (original == null || original.avatar == null || !original.avatar.isValid)
                throw new InvalidOperationException("Animator playback requires the player's valid Avatar");
            phase = "proxy animator";
            var animator = proxy.AddComponent<Animator>();
            animator.avatar = original.avatar;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = true; // Only the private sampling object moves.
            animator.fireEvents = false; // EventRunner owns live sounds/lifecycle.
            proxy.SetActive(true);
            phase = "graph";
            PlayableGraph.Create_Injected("MoreActionsAnimatorSample", out graph);
            PlayableGraph.SetTimeUpdateMode_Injected(ref graph, DirectorUpdateMode.Manual);
            phase = "clip handle";
            // The generated Create helper calls a stripped value-type ctor.
            // Its native injected factory exists; keep the raw handle instead.
            if (!AnimationClipPlayable.CreateHandleInternal_Injected(ref graph, clip, ref playable))
                throw new InvalidOperationException("Native animation clip handle creation failed");
            AnimationClipPlayable.SetApplyFootIKInternal(ref playable, false);
            AnimationClipPlayable.SetApplyPlayableIKInternal(ref playable, false);
            AnimationClipPlayable.SetOverrideLoopTimeInternal(ref playable, true);
            AnimationClipPlayable.SetLoopTimeInternal(ref playable, false);
            PlayableHandle.SetDuration_Injected(ref playable, double.PositiveInfinity);
            phase = "animation output";
            if (!AnimationPlayableGraphExtensions.InternalCreateAnimationOutput(ref graph, "Pose", out var output))
                throw new InvalidOperationException("Native animation output creation failed");
            AnimationPlayableOutput.InternalSetTarget(ref output, animator);
            PlayableOutputHandle.SetSourcePlayable_Injected(ref output, ref playable, 0);
            PlayableOutputHandle.SetWeight_Injected(ref output, 1);
            PlayableGraph.Play_Injected(ref graph);
        }
        catch (Exception error)
        {
            try { Dispose(); } catch (Exception cleanup) { Plugin.LogSource?.LogWarning("ANIMATOR_SETUP_CLEANUP_FAILED " + cleanup.Message); }
            throw new InvalidOperationException("Animator setup failed at " + phase, error);
        }
    }

    internal void Sample(float seconds)
    {
        if (disposed) throw new ObjectDisposedException(nameof(AnimatorClipSampler));
        proxy.transform.position = position;
        proxy.transform.rotation = rotation;
        if (seconds < previous)
        {
            PlayableHandle.SetTime_Injected(ref playable, 0);
            PlayableHandle.SetDone_Injected(ref playable, false);
            PlayableGraph.Evaluate_Injected(ref graph, 0);
            proxy.transform.position = Vector3.zero;
            proxy.transform.rotation = Quaternion.identity;
            previous = 0;
        }
        PlayableGraph.Evaluate_Injected(ref graph, seconds - previous);
        previous = seconds;
        position = proxy.transform.position;
        rotation = proxy.transform.rotation;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (PlayableGraph.IsValid_Injected(ref graph)) PlayableGraph.Destroy_Injected(ref graph);
    }
}

internal sealed class AnimatorActionSource : IActionClipSource
{
    readonly float duration;
    public AnimationClip Clip { get; }
    internal string ClipName { get; }
    internal string RelativeBundle { get; }
    internal string Group { get; }
    public float Duration => duration;
    internal AnimatorActionSource(AnimationClip clip,string relativeBundle="")
    {
        Clip=clip;ClipName=clip.name;RelativeBundle=relativeBundle;duration=Math.Max(clip.length,1f/30);
        var path=relativeBundle.Replace('\\','/');int slash=path.IndexOf('/');Group=slash<0?"Actions":path.Substring(0,slash);
    }
    public IActionPose CreatePose(GameObject target, ActionDefinition definition)
        => new ActionPose(target, new LoadedAction { Definition = definition, Clip = Clip }, true);
}
