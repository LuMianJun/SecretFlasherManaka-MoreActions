using System;
using System.Collections.Generic;
using UnityEngine;

namespace SFM.Actions;

// Samples the child off-screen. The main pose supplies the only live body lease.
internal sealed class ActionIkuPose : IDisposable
{
    readonly AnimationClip clip;
    readonly ActionSamplingRigPool.Entry rig;
    readonly Transform[] live, sample;
    readonly Quaternion[] heldRotations;
    readonly Vector3[] heldPositions;
    readonly int hipIndex;
    readonly int leftFootIndex, rightFootIndex;
    AnimatorClipSampler sampler;
    bool loggedFullWeight, loggedMidpoint;
    internal bool HasHeldPose { get; private set; }

    internal ActionIkuPose(GameObject target, AnimationClip clip, List<(string path, Transform live, Transform sample)> bones)
    {
        this.clip = clip;
        bool humanoid = clip.isHumanMotion;
        rig = ActionSamplingRigPool.Acquire(target, humanoid);
        try
        {
            live = new Transform[bones.Count]; sample = new Transform[bones.Count];
            heldRotations = new Quaternion[bones.Count];
            heldPositions = new Vector3[bones.Count];
            hipIndex = bones.FindIndex(bone => bone.path == "Armature/Hips");
            leftFootIndex = bones.FindIndex(bone => bone.live.name == "Foot_L");
            rightFootIndex = bones.FindIndex(bone => bone.live.name == "Foot_R");
            for (int i = 0; i < bones.Count; i++)
            {
                live[i] = bones[i].live;
                sample[i] = rig.Sample[bones[i].path];
            }
            sampler = new AnimatorClipSampler(rig.Proxy, target.GetComponent<Animator>(), clip, genericWithoutAvatar: !humanoid);
            Sample(0);
        }
        catch
        {
            rig.Retired = true;
            Dispose();
            throw;
        }
    }

    void Sample(float seconds)
    {
        for (int i = 0; i < rig.Nodes.Length; i++)
        {
            rig.Nodes[i].localRotation = rig.Rotations[i];
            rig.Nodes[i].localPosition = rig.Positions[i];
        }
        sampler.Sample(seconds);
    }

    internal void HoldMainPose()
    {
        for (int i = 0; i < live.Length; i++)
        {
            heldRotations[i] = live[i].localRotation;
            heldPositions[i] = live[i].localPosition;
        }
        HasHeldPose = true;
        loggedFullWeight = loggedMidpoint = false;
    }

    internal bool Apply(float progress, float weight, ActionBodyPoseLease body)
    {
        if (!HasHeldPose || clip == null || rig.Proxy == null) return false;
        if (weight > 0) Sample(progress * clip.length);
        for (int i = 0; i < live.Length; i++)
            if (live[i] == null || sample[i] == null) return false;
        body.ApplyBaseline();
        var clipRoot = rig.Proxy.transform;
        var playerAvatar = rig.Root.transform;
        for (int i = 0; i < live.Length; i++)
        {
            if (weight <= 0)
            {
                live[i].localRotation = heldRotations[i];
                live[i].localPosition = heldPositions[i];
                continue;
            }

            Vector3 position = sample[i].localPosition;
            Quaternion rotation = sample[i].localRotation;
            if (i == hipIndex)
            {
                // Armature is outside the mask. Its authored basis is
                // already included in the sampled hip transform.
                var relativePosition = clipRoot.InverseTransformPoint(sample[i].position);
                var relativeRotation = Quaternion.Inverse(clipRoot.rotation) * sample[i].rotation;
                var liveParent = live[i].parent;
                position = liveParent.InverseTransformPoint(playerAvatar.TransformPoint(relativePosition));
                rotation = Quaternion.Inverse(liveParent.rotation) * playerAvatar.rotation * relativeRotation;
            }
            live[i].localPosition = Vector3.Lerp(heldPositions[i], position, weight);
            live[i].localRotation = Quaternion.Slerp(heldRotations[i], rotation, weight);
        }
        if (hipIndex >= 0 && weight >= .98f)
        {
            bool midpoint = progress >= .5f;
            if ((!midpoint && !loggedFullWeight) || (midpoint && !loggedMidpoint))
            {
                if (midpoint) loggedMidpoint = true;
                else loggedFullWeight = true;
                var hip = live[hipIndex];
                var sampledHip = clipRoot.InverseTransformPoint(sample[hipIndex].position);
                float leftHeight = leftFootIndex >= 0 ? live[leftFootIndex].position.y : float.NaN;
                float rightHeight = rightFootIndex >= 0 ? live[rightFootIndex].position.y : float.NaN;
                Plugin.LogSource?.LogInfo(FormattableString.Invariant(
                    $"ACTION_IKU_POSE asset={clip.name} progress={progress:F4} weight={weight:F4} sampledHipY={sampledHip.y:F5} liveHipY={hip.position.y:F5} avatarY={playerAvatar.position.y:F5} leftFootY={leftHeight:F5} rightFootY={rightHeight:F5}"));
            }
        }
        return true;
    }

    public void Dispose()
    {
        try { sampler?.Dispose(); }
        catch { rig.Retired = true; throw; }
        finally { sampler = null; rig.Dispose(); }
    }
}
