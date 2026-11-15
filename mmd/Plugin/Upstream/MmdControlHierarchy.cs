using UnityEngine;
using SFM.Mmd.Upstream.Reader;
using static SFM.Mmd.Upstream.Reader.VMDReader.BoneKeyFrameGroup;

namespace SFM.Mmd.Upstream;

// The game skin has one Hips, but its MMD controls have separate parents
// and pivots. Evaluate them on the private ghost tree before copying poses.
internal sealed class MmdControlHierarchy
{
    const float MetersPerUnit = UpstreamBodySolver.DefaultBoneAmplifier;
    readonly Transform center, groove, waist, leftCancel, rightCancel;
    readonly Vector3 centerRest, waistRest, lowerBodyRest, leftCancelRest, rightCancelRest;

    internal Transform LowerBody { get; }
    internal Quaternion CenterAndGrooveRotation => center.localRotation * groove.localRotation;

    internal MmdControlHierarchy(Transform center, Transform upperBody, Transform leftLeg,
        Transform rightLeg, Vector3 waistPivot)
    {
        this.center = center;
        centerRest = center.localPosition;
        groove = Create("GrooveControl", center, center.position);
        waist = Create("WaistControl", groove, waistPivot);
        waistRest = waist.localPosition;
        LowerBody = Create("LowerBodyControl", waist, center.position);
        lowerBodyRest = LowerBody.localPosition;
        upperBody.SetParent(waist, true);
        leftCancel = Create("LeftWaistCancel", LowerBody, leftLeg.position);
        rightCancel = Create("RightWaistCancel", LowerBody, rightLeg.position);
        leftCancelRest = leftCancel.localPosition;
        rightCancelRest = rightCancel.localPosition;
        leftLeg.SetParent(leftCancel, true);
        rightLeg.SetParent(rightCancel, true);
    }

    static Transform Create(string name, Transform parent, Vector3 pivot)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, false);
        node.position = pivot;
        return node;
    }

    internal void Evaluate(VMDReader reader, int frame)
    {
        ApplyTrack(center, centerRest, reader, BoneNames.センター, frame);
        ApplyTrack(groove, Vector3.zero, reader, BoneNames.グルーブ, frame);
        ApplyTrack(LowerBody, lowerBodyRest, reader, BoneNames.下半身, frame);
    }

    static void ApplyTrack(Transform target, Vector3 rest, VMDReader reader, BoneNames name, int frame)
    {
        var key = reader.GetBoneKeyFrame(name, frame);
        var position = key != null && key.Position != Vector3.zero
            ? key.Position : VMDReader.InterporatePosition(reader, name, frame);
        var q = key?.Rotation ?? default;
        target.localPosition = rest + position * MetersPerUnit;
        target.localRotation = q.x != 0 || q.y != 0 || q.z != 0 || q.w != 0
            ? q.normalized : name == BoneNames.下半身 ? InterpolateLowerRotation(reader, frame)
            : VMDReader.InterporateRotation(reader, name, frame);
    }

    static Quaternion InterpolateLowerRotation(VMDReader reader, int frame)
    {
        // Keep the pinned lower-body rotation sampler's Lerp and key range.
        var group = reader.GetBoneKeyFrameGroup(BoneNames.下半身);
        var previous = group.LastRotationKeyFrame;
        var next = group.NextRotationKeyFrame;
        var start = previous?.Rotation ?? Quaternion.identity;
        if (next == null) return start.normalized;
        var t = group.Interpolation.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.Rotation,
            frame, previous?.FrameNumber ?? 0, next.FrameNumber);
        return Quaternion.Lerp(start, next.Rotation, t);
    }

    internal void SetWaist(Vector3 position, Quaternion rotation)
    {
        waist.localPosition = waistRest + position * MetersPerUnit;
        waist.localRotation = rotation;
    }

    internal void SetWaistCancel(bool left, Vector3 position, Quaternion rotation)
    {
        var target = left ? leftCancel : rightCancel;
        var rest = left ? leftCancelRest : rightCancelRest;
        target.localPosition = rest + position * MetersPerUnit;
        target.localRotation = rotation * Quaternion.Inverse(waist.localRotation);
    }
}
