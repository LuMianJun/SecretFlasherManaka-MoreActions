using UnityEngine;

// Offline prototype. Contact samples are in scene coordinates on a flat floor.
// The detector uses only limb measurements and retains the authored X/Z path.
internal static class GroundContact
{
    internal const int SampleRate = 30;
    internal const float FloorY = 0;

    internal readonly record struct Sample(Vector3 Surface, Vector3 Joint, float Facing, float Lowest);

    internal static float[] PlanLift(Sample[] samples, bool hand, float heightFactor, float limbLength)
    {
        var nearFloor = new bool[samples.Length];
        var steady = new bool[samples.Length];
        var weights = new float[samples.Length];
        // Evaluate the wrist/palm or ankle/sole over a short interval so the
        // choice is based on the trajectory and independent of seek direction.
        const int velocityRadius = 2;
        for (int frame = 0; frame < samples.Length; ++frame)
        {
            var sample = samples[frame];
            int first = Math.Max(0, frame - velocityRadius);
            int last = Math.Min(samples.Length - 1, frame + velocityRadius);
            float duration = (last - first) / (float)SampleRate;
            if (duration <= 0 || sample.Surface.y - FloorY > .015f * heightFactor) continue;
            // A low, nearly vertical foot can bear weight on the toe or
            // heel. Allow that stance to establish contact at a low ankle.
            bool facesFloor = sample.Facing >= (hand ? .25f : .20f);
            bool edgeOfFoot = !hand && sample.Facing >= -.30f && sample.Joint.y - FloorY <= .025f * heightFactor;
            if (!facesFloor && !edgeOfFoot) continue;
            nearFloor[frame] = true;
            var jointVelocity = (samples[last].Joint - samples[first].Joint) / duration;
            float surfaceVerticalSpeed = MathF.Abs(samples[last].Surface.y - samples[first].Surface.y) / duration;
            float horizontalSpeed = MathF.Sqrt(jointVelocity.x * jointVelocity.x + jointVelocity.z * jointVelocity.z);
            steady[frame] = MathF.Abs(jointVelocity.y) <= (hand ? .45f : .35f) * heightFactor
                && surfaceVerticalSpeed <= (hand ? .55f : .45f) * heightFactor
                && horizontalSpeed <= (hand ? .95f : .80f) * heightFactor;
        }

        int start = 0;
        while (start < nearFloor.Length)
        {
            if (!nearFloor[start]) { ++start; continue; }
            int end = start + 1;
            while (end < nearFloor.Length && nearFloor[end]) ++end;
            int candidate = start;
            while (candidate < end)
            {
                if (!steady[candidate]) { ++candidate; continue; }
                int next = candidate + 1;
                while (next < end && steady[next]) ++next;
                if (next - candidate >= (hand ? 5 : 4))
                {
                    // Keep the seed and a short approach/release where the
                    // surface is still low. The union tolerates brief motion
                    // between the steady portions of the same contact.
                    AddContact(samples, weights, Math.Max(start, candidate - 3), Math.Min(end, next + 3), heightFactor);
                }
                candidate = next;
            }
            start = end;
        }
        var heights = new float[samples.Length];
        for (int frame = 0; frame < heights.Length; ++frame)
        {
            float depth = MathF.Max(0, FloorY - samples[frame].Lowest);
            // A palm well below the plane is ambiguous when the original
            // model or an alternate support surface is unknown.
            float confidence = hand ? 1 - SmoothStep((depth / limbLength - .32f) / .13f) : 1;
            heights[frame] = depth * weights[frame] * confidence;
        }

        // Limit extra vertical steps while allowing a lift to cancel a fast
        // source motion across the plane during normal approach and release.
        float extraStep = .020f * heightFactor;
        for (int frame = 1; frame < heights.Length; ++frame)
        {
            float step = samples[frame].Joint.y - samples[frame - 1].Joint.y;
            heights[frame] = MathF.Min(heights[frame], heights[frame - 1] + MathF.Abs(step) + extraStep - step);
        }
        for (int frame = heights.Length - 2; frame >= 0; --frame)
        {
            float step = samples[frame + 1].Joint.y - samples[frame].Joint.y;
            heights[frame] = MathF.Min(heights[frame], heights[frame + 1] + MathF.Abs(step) + extraStep + step);
        }
        return heights;
    }

    static void AddContact(Sample[] samples, float[] weights, int start, int end, float heightFactor)
    {
        const float fadeFrames = 5;
        bool easeIn = start > 0 && samples[start - 1].Surface.y - FloorY < -.02f * heightFactor;
        bool easeOut = end < samples.Length && samples[end].Surface.y - FloorY < -.02f * heightFactor;
        for (int frame = start; frame < end; ++frame)
        {
            float entering = easeIn ? (frame - start + 1) / fadeFrames : 1;
            float leaving = easeOut ? (end - frame) / fadeFrames : 1;
            weights[frame] = MathF.Max(weights[frame], SmoothStep(MathF.Min(entering, leaving)));
        }
    }

    static float SmoothStep(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
