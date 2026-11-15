using System;
using UnityEngine;

namespace SFM.Mmd;

internal static class MmdContactSolver
{
    const float Epsilon = 0.00001f;

    // Uses the already evaluated pose, including foot D, toe and finger
    // output. Only the three joints of this chain receive new rotations.
    internal static float Lift(Transform root, Transform middle, Transform end, float rise)
    {
        if (rise <= Epsilon) return 0;
        var start = root.position;
        var oldMiddle = middle.position;
        var oldEnd = end.position;
        var endRotation = end.rotation;
        var first = oldMiddle - start;
        var second = oldEnd - oldMiddle;
        var fromRoot = oldEnd - start;
        float upper = first.magnitude;
        float lower = second.magnitude;
        float currentDistance = fromRoot.magnitude;
        if (upper <= Epsilon || lower <= Epsilon || currentDistance <= Epsilon) return 0;

        // Intersect the line above the authored endpoint with the two-bone
        // reach shell. Keep the horizontal path even when full lift is unreachable.
        float minDistance = MathF.Abs(upper - lower) + Epsilon;
        float maxDistance = upper + lower - Epsilon;
        float horizontalSquared = fromRoot.x * fromRoot.x + fromRoot.z * fromRoot.z;
        if (horizontalSquared >= maxDistance * maxDistance) return 0;
        float verticalLimit = MathF.Sqrt(maxDistance * maxDistance - horizontalSquared);
        float targetY = MathF.Min(fromRoot.y + rise, verticalLimit);
        if (horizontalSquared < minDistance * minDistance)
        {
            float inner = MathF.Sqrt(minDistance * minDistance - horizontalSquared);
            if (targetY > -inner && targetY < inner) targetY = -inner;
        }
        if (targetY - fromRoot.y <= Epsilon) return 0;

        var goal = new Vector3(oldEnd.x, start.y + targetY, oldEnd.z);
        var delta = goal - start;
        float distance = delta.magnitude;
        var axis = delta / distance;
        var oldAxis = fromRoot / currentDistance;
        var bend = first - oldAxis * Vector3.Dot(first, oldAxis);
        if (bend.sqrMagnitude < Epsilon * Epsilon) return 0;
        bend = Quaternion.FromToRotation(oldAxis, axis) * bend.normalized;
        float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
        float perpendicular = MathF.Sqrt(MathF.Max(0, upper * upper - along * along));
        var joint = start + axis * along + bend * perpendicular;

        root.rotation = Quaternion.FromToRotation(first, joint - start) * root.rotation;
        middle.rotation = Quaternion.FromToRotation(end.position - middle.position, goal - middle.position) * middle.rotation;
        end.rotation = endRotation;
        return end.position.y - oldEnd.y;
    }
}
