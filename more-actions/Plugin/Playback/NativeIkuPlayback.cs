using UnityEngine;

namespace SFM.Actions;

// Only the Idle routes of the vanilla Action layer belong to standalone
// actions. Match the states, so a global clip override cannot change the clock.
internal sealed class NativeIkuPlayback
{
    internal enum Status { Sampling, Finished, Interrupted }

    readonly Animator animator;
    readonly int layer;
    readonly float startedAt;
    bool entered, returning;
    float progress, idleSince = -1;

    NativeIkuPlayback(Animator animator, int layer)
    {
        this.animator = animator; this.layer = layer;
        startedAt = Time.time;
    }

    internal static NativeIkuPlayback TryStart(Animator animator)
    {
        if (animator == null || !animator.enabled || !animator.isInitialized) return null;
        int layer = animator.GetLayerIndex("Action");
        return layer < 0 ? null : new NativeIkuPlayback(animator, layer);
    }

    static bool IsIdle(AnimatorStateInfo state) => state.IsName("Action.NoMotion");
    static bool IsIku(AnimatorStateInfo state)
        => state.IsName("Action.IkuStand") || state.IsName("Action.DogWalkIku");

    internal Status Read(Animator avatar, bool nativeActive, out float normalized, out float weight)
    {
        normalized = progress; weight = 0;
        if (avatar == null || avatar != animator || !animator.enabled || !animator.isInitialized
            || layer >= animator.layerCount) return Status.Interrupted;

        var current = animator.GetCurrentAnimatorStateInfo(layer);
        if (animator.IsInTransition(layer))
        {
            var next = animator.GetNextAnimatorStateInfo(layer);
            float mix = animator.GetAnimatorTransitionInfo(layer).normalizedTime;
            if (!float.IsFinite(mix)) return Status.Interrupted;
            mix = Mathf.Clamp01(mix);
            if (IsIdle(current) && IsIku(next))
            {
                current = next; weight = mix;
            }
            else if (IsIku(current) && IsIdle(next))
            {
                returning = true; weight = 1f - mix;
            }
            else return Status.Interrupted;
        }
        else if (IsIku(current)) weight = 1;
        else if (IsIdle(current))
        {
            if (!entered) return nativeActive && Time.time - startedAt < 1f ? Status.Sampling : Status.Interrupted;
            // Transition polling can skip the last blend on a long frame.
            // Require an observed normal exit or late progress in the clip.
            if (!returning && progress < .85f) return Status.Interrupted;
            if (!nativeActive) return Status.Finished;
            if (idleSince < 0) idleSince = Time.time;
            return Time.time - idleSince < 1f ? Status.Sampling : Status.Interrupted;
        }
        else return Status.Interrupted;

        if (!float.IsFinite(current.normalizedTime)) return Status.Interrupted;
        entered = true;
        normalized = progress = Mathf.Clamp01(current.normalizedTime);
        return Status.Sampling;
    }
}
