using System;
using UnityEngine;

namespace Seleka2.Actions;

// The common runtime owns locomotion; providers never warp a live character.
internal sealed class ActionRootMotion
{
    readonly CharacterController controller;
    readonly Transform player;
    readonly Vector3 anchor;
    readonly Func<bool> canContinue;
    const float StepLength = .1f;
    const int MaxSteps = 128;
    const float PositionTolerance = .02f;
    internal float Distance { get; private set; }

    internal ActionRootMotion(CharacterController controller, Transform player, Func<bool> canContinue)
    {
        if (controller == null || player == null || controller.transform != player || !controller.enabled)
            throw new InvalidOperationException("Root motion requires the initialized player CharacterController");
        this.controller = controller; this.player = player; this.canContinue = canContinue; anchor = player.position;
    }
    static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    static float HorizontalDistance(Vector3 a, Vector3 b)
    { double x = (double)a.x - b.x, z = (double)a.z - b.z; return (float)Math.Sqrt(x * x + z * z); }

    internal bool Apply(Vector3 displacement, Vector3 cycle, double loops, out string reason)
    {
        reason = null;
        if (controller == null || player == null || !controller.enabled) { reason = "root controller unavailable"; return false; }
        if (!Finite(displacement) || !Finite(cycle) || !double.IsFinite(loops) || loops < 0) { reason = "invalid root displacement"; return false; }
        double x = anchor.x + (double)displacement.x + loops * cycle.x;
        double z = anchor.z + (double)displacement.z + loops * cycle.z;
        var current = player.position;
        if (!Finite(current)) { reason = "invalid controller position"; return false; }
        var goal = new Vector3((float)x, current.y, (float)z);
        if (!Finite(goal)) { reason = "invalid root displacement"; return false; }
        float distance = HorizontalDistance(goal, current);
        if (!float.IsFinite(distance) || distance > StepLength * MaxSteps) { reason = "root trajectory discontinuity"; return false; }
        int steps = Math.Max(1, (int)Math.Ceiling(distance / StepLength));
        if (distance < .000001f) return true;
        var step = new Vector3((goal.x - current.x) / steps, 0, (goal.z - current.z) / steps);
        for (int i = 0; i < steps; i++)
        {
            if (!canContinue()) { reason = "root motion canceled"; return false; }
            var before = player.position;
            controller.Move(step);
            if (!canContinue()) { reason = "root motion canceled"; return false; }
            var after = player.position;
            if (!Finite(after)) { reason = "invalid controller position"; return false; }
            Distance += HorizontalDistance(before, after);
            var expected = new Vector3(before.x + step.x, after.y, before.z + step.z);
            if (HorizontalDistance(expected, after) > PositionTolerance) { reason = "root motion blocked"; return false; }
        }
        if (HorizontalDistance(goal, player.position) > PositionTolerance) { reason = "root motion blocked"; return false; }
        return true;
    }
}
