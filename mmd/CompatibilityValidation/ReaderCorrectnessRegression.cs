using System.Text;
using SFM.Mmd;
using SFM.Mmd.Upstream.Reader;
using UnityEngine;
using static SFM.Mmd.Upstream.Reader.VMDReader.BoneKeyFrameGroup;

// Pinned input compatibility. The actual unchanged-reader/player differential
// is exercised by UpstreamValidation; these checks cover its memory boundary.
internal static class ReaderCorrectnessRegression
{
    const BoneNames Bone = BoneNames.左足ＩＫ;

    internal static void Run(Action<bool, string> check)
    {
        var first = Key(0, new(3, -1, 2), 15);
        var duplicate = Key(0, new(9, 8, 7), 35);
        var zero = Key(10, Vector3.zero, 55);
        var tiny = Key(12, new(.000001f, -.000002f, .000003f), -20);
        var last = Key(20, new(-2, 4, -1), 80);
        var reader = VMDReader.FromMotion(Motion(first, duplicate, zero, tiny, last));
        var group = reader.GetBoneKeyFrameGroup(Bone);
        check(group.BoneKeyFrames.Count == 5,
            "Pinned reader keeps original key order and duplicate frames in its complete key list");
        check(group.BonePositionKeyFrames.Count == 3,
            "Pinned position brackets exclude zero and Unity-equal-to-zero positions");
        check(reader.GetBoneKeyFrame(Bone, 0).Position == first.p,
            "Pinned Find-first behavior selects the first duplicate key");
        check(reader.FrameCount == 20 && reader.RawVMD.FrameCount == 20,
            "Memory input exposes the last supported bone frame without extending it");

        var rawIk = new MmdMotion();
        rawIk.RawIkFrames.Add((0, false, new()));
        rawIk.RawIkFrames.Add((0, true, new() { new("左足ＩＫ", false), new("左足ＩＫ", true) }));
        var ikReader = VMDReader.FromMotion(rawIk);
        check(ikReader.RawVMD.IKFrames.Count == 2 && !ikReader.RawVMD.IKFrames[0].Display
            && ikReader.RawVMD.IKFrames[0].IKEnable.Count == 0
            && ikReader.RawVMD.IKFrames[1].IKEnable.Select(entry => entry.Enable).SequenceEqual(new[] { false, true }),
            "Memory input preserves empty, duplicate and ordered IK frame records");

        CheckCursorContract(check);
        CheckBinaryInput(check, first);
        CheckCurveChannels(check, first);
        var empty = VMDReader.FromMotion(new MmdMotion());
        empty.GetBoneKeyFrame(Bone, 0);
        check(VMDReader.InterporatePosition(empty, Bone, 0).sqrMagnitude == 0
            && Quaternion.Angle(VMDReader.InterporateRotation(empty, Bone, 0), Quaternion.identity) < .01f,
            "An absent pinned track keeps default position and rotation");
    }

    static void CheckCursorContract(Action<bool, string> check)
    {
        var motion = Motion(Key(0, new(3, 2, -1), 20), Key(20, new(7, -2, 4), 70), Key(40, new(-2, 4, 1), 10));
        var reader = VMDReader.FromMotion(motion);
        check(Sample(reader, 1).p.sqrMagnitude == 0,
            "Pinned first-frame-1 request retains the original uninitialized cursor behavior");
        reader.ResetCursors();
        check(Sample(reader, 1).p.sqrMagnitude == 0,
            "Reset returns the cursor to the same state as a newly constructed pinned reader");
        reader.ResetCursors();
        Sample(reader, 0);
        check(Sample(reader, 1).p.magnitude > 1,
            "Priming frame 0 preserves the pinned player's original sequential sampling contract");

        reader.GetBoneKeyFrame(Bone, 0);
        var group = reader.GetBoneKeyFrameGroup(Bone);
        var last = group.LastPositionKeyFrame;
        var next = group.NextPositionKeyFrame;
        VMDReader.InterporatePosition(reader, Bone, 35);
        VMDReader.InterporateRotation(reader, Bone, 35);
        check(ReferenceEquals(last, group.LastPositionKeyFrame) && ReferenceEquals(next, group.NextPositionKeyFrame),
            "Pinned interpolation reads the existing cursor; it does not silently select another frame");
        reader.GetBoneKeyFrame(Bone, 35);
        check(group.LastPositionKeyFrame.FrameNumber == 20 && group.NextPositionKeyFrame.FrameNumber == 40,
            "The player-selected key request advances interpolation brackets explicitly");
    }

    static void CheckBinaryInput(Action<bool, string> check, MmdMotion.Key key)
    {
        using var bytes = new MemoryStream();
        using(var writer = new BinaryWriter(bytes, Encoding.GetEncoding(932), true))
        {
            var name = Encoding.GetEncoding(932).GetBytes(Bone.ToString());
            writer.Write(name);writer.Write(new byte[15-name.Length]);
            writer.Write(key.frame);
            writer.Write(-key.p.x);writer.Write(key.p.y);writer.Write(-key.p.z);
            writer.Write(-key.q.x);writer.Write(key.q.y);writer.Write(-key.q.z);writer.Write(key.q.w);
            writer.Write(key.interpolation);
        }
        bytes.Position = 0;
        using var binary = new BinaryReader(bytes);
        var parsed = new VMD.BoneKeyFrame(binary);
        var memory = VMDReader.FromMotion(Motion(key)).GetBoneKeyFrame(Bone, (int)key.frame);
        check(parsed.Name == memory.Name && parsed.FrameNumber == memory.FrameNumber
            && parsed.Position == memory.Position && Quaternion.Angle(parsed.Rotation, memory.Rotation) < .01f,
            "Already converted memory keys match the pinned binary parser without a second coordinate conversion");
        foreach(var channel in Enum.GetValues<VMD.BoneKeyFrame.Interpolation.BezierCurveNames>())
            check(parsed.BoneInterpolation.GetInterpolationValue(channel, 5, 0, 20)
                == memory.BoneInterpolation.GetInterpolationValue(channel, 5, 0, 20),
                "Memory input preserves each pinned 16-byte interpolation channel: " + channel);
    }

    static void CheckCurveChannels(Action<bool, string> check, MmdMotion.Key key)
    {
        var reader = VMDReader.FromMotion(Motion(key, Key(20, new(5, 4, 3), 50)));
        var curve = reader.GetBoneKeyFrame(Bone, 0).BoneInterpolation;
        var rates = Enum.GetValues<VMD.BoneKeyFrame.Interpolation.BezierCurveNames>()
            .Select(channel => curve.GetInterpolationValue(channel, 5, 0, 10)).ToArray();
        check(rates.Distinct().Count() == 4,
            "The pinned reader retains distinct XYZ and rotation curves");
        check(curve.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, 0, 0, 10) > 0
            && curve.GetInterpolationValue(VMD.BoneKeyFrame.Interpolation.BezierCurveNames.X, 10, 0, 10) < 1,
            "Pinned finite-iteration endpoint approximation is preserved instead of a separate clamped curve policy");
    }

    static (Vector3 p, Quaternion q) Sample(VMDReader reader, int frame)
    {
        reader.GetBoneKeyFrame(Bone, frame);
        return (VMDReader.InterporatePosition(reader, Bone, frame), VMDReader.InterporateRotation(reader, Bone, frame));
    }

    static MmdMotion Motion(params MmdMotion.Key[] keys)
    {
        var motion = new MmdMotion { lastFrame = keys[^1].frame };
        motion.tracks[Bone.ToString()] = keys.ToList();
        return motion;
    }

    static MmdMotion.Key Key(uint frame, Vector3 position, float degrees)
    {
        byte[] controls = new byte[64];
        (byte x1, byte y1, byte x2, byte y2)[] channels =
            [(10, 100, 100, 120), (40, 5, 100, 90), (70, 15, 120, 100), (15, 110, 100, 120)];
        for(int channel = 0; channel < channels.Length; channel++)
        {
            var value = channels[channel]; int at = channel * 16;
            controls[at] = value.x1; controls[at + 4] = value.y1;
            controls[at + 8] = value.x2; controls[at + 12] = value.y2;
        }
        return new() { frame = frame, p = position, q = Quaternion.AngleAxis(degrees, Vector3.up), interpolation = controls };
    }
}
