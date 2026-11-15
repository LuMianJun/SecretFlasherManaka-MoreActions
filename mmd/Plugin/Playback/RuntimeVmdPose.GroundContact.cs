using System;
using SFM.Actions;
using UnityEngine;

namespace SFM.Mmd;

public sealed partial class RuntimeVmdPose
{
    const float MeasuredScale = 1.125f;
    readonly record struct ContactChain(Transform Root, Transform Middle, Transform End);
    MmdGroundContactPlan contactPlan;
    CharacterController contactController;
    ContactChain[] contactChains;
    string contactAction;
    int contactFrame = -1;
    int floorMask;
    int correctedFrames, missedFloorFrames;
    float contactMotionFrame;

    internal void AttachGroundContact(GameObject target, ActionDefinition action, MmdGroundContactPlan plan)
    {
        if (plan == null || target != ActionTargetApi.Target || !writeHip
            || action.BodyMask != ActionBodyMask.FullBody || action.BonePaths.Length != 0) return;
        var controller = target.GetComponentInParent<CharacterController>();
        if (controller == null) return;
        var chains = new[]
        {
            Chain(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
            Chain(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
            Chain(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
            Chain(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand)
        };
        if (Array.Exists(chains, chain => chain.Root == null || chain.Middle == null || chain.End == null)) return;

        int mask = ResolveFloorMask();
        if (mask == 0) return;

        contactPlan = plan;
        contactController = controller;
        contactChains = chains;
        contactAction = action.Id;
        floorMask = mask;
        ActionPlaybackApi.PoseApplied += ApplyGroundContact;
        global::Plugin.LogSource?.LogInfo("MMD_GROUND_IK_BOUND id=" + contactAction);
    }

    static int ResolveFloorMask()
    {
        // This game's GetMask wrapper throws because its managed body was
        // stripped. NameToLayer still calls the game's native implementation.
        int mask = 0;
        foreach (string name in new[] { "Default", "Level", "CollideOnlyPlayer" })
        {
            int layer = LayerMask.NameToLayer(name);
            if ((uint)layer < 32) mask |= 1 << layer;
        }
        return mask;
    }

    ContactChain Chain(HumanBodyBones root, HumanBodyBones middle, HumanBodyBones end) =>
        new(OwnedBone(root), OwnedBone(middle), OwnedBone(end));

    Transform OwnedBone(HumanBodyBones human)
    {
        for (int i = 0; i < write.Length; i++)
            if (write[i] && rig.bindings[i].human == human) return rig.bindings[i].live;
        return null;
    }

    partial void MarkGroundContactFrame(float seconds)
    {
        if (contactPlan == null) return;
        contactFrame = Time.frameCount;
        contactMotionFrame = seconds * 30;
    }

    void ApplyGroundContact(string id, double _)
    {
        if (disposed || id != contactAction || ActionPlaybackApi.CurrentProviderId != global::Plugin.Guid
            || contactFrame != Time.frameCount) return;
        contactFrame = -1;
        if (global::Plugin.UseGroundContact?.Value != true || contactController == null || !contactController.enabled) return;

        try { ApplyGroundContactFrame(); }
        catch (Exception e)
        {
            ReleaseGroundContact();
            global::Plugin.LogSource?.LogWarning($"MMD_GROUND_IK_DISABLED id={id} phase=apply reason={e}");
        }
    }

    void ApplyGroundContactFrame()
    {
        Span<MmdGroundContactPlan.Sample> samples = stackalloc MmdGroundContactPlan.Sample[4];
        bool active = false;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = contactPlan.At(contactMotionFrame, i);
            active |= samples[i].Gain > .0001f;
        }
        if (!active) return;

        // More Actions emits PoseApplied after its CharacterController move.
        // Use that placement for both the supporting floor and each limb.
        var player = contactController.transform;
        if (!GroundAt(player.position + Vector3.up * .35f, .95f, out float belowPlayer))
        {
            missedFloorFrames++;
            return;
        }
        float scale = Mathf.Abs(rig.liveRoot.lossyScale.y) / MeasuredScale;
        if (!(scale > 0) || !float.IsFinite(scale)) return;
        Span<float> lifts = stackalloc float[4];
        lifts.Clear();
        for (int i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (sample.Gain <= .0001f) continue;
            var chain = contactChains[i];
            if (chain.Root == null || chain.Middle == null || chain.End == null) continue;
            var p = chain.End.position;
            if (!GroundAt(new Vector3(p.x, belowPlayer + .18f, p.z), .36f, out float floor)) continue;
            lifts[i] = MmdGroundContactPlan.LiftForFloor(sample, floor, p.y, scale);
        }
        bool corrected = false;
        for (int i = 0; i < lifts.Length; i++)
        {
            if (lifts[i] <= .0001f) continue;
            var chain = contactChains[i];
            corrected |= MmdContactSolver.Lift(chain.Root, chain.Middle, chain.End, lifts[i]) > .0001f;
        }
        if (corrected && ++correctedFrames == 1)
            global::Plugin.LogSource?.LogInfo($"MMD_GROUND_IK_ACTIVE id={contactAction} floorY={belowPlayer:F3}");
    }

    internal void DetachGroundContact() => ReleaseGroundContact();

    bool GroundAt(Vector3 origin, float distance, out float height)
    {
        height = 0;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, floorMask, QueryTriggerInteraction.Ignore)
            || hit.collider == null || hit.normal.y < .70f
            || hit.collider.transform.IsChildOf(contactController.transform)) return false;
        height = hit.point.y;
        return float.IsFinite(height);
    }

    partial void ReleaseGroundContact()
    {
        if (contactPlan == null) return;
        ActionPlaybackApi.PoseApplied -= ApplyGroundContact;
        contactPlan = null;
        global::Plugin.LogSource?.LogInfo($"MMD_GROUND_IK_STOP id={contactAction} correctedFrames={correctedFrames} missedFloorFrames={missedFloorFrames}");
    }
}
