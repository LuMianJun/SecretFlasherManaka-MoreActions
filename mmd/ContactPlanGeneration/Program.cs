using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SFM.Actions;
using SFM.Mmd;
using UnityEngine;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
if (args.Length != 3)
    throw new ArgumentException("Expected a motion VMD or directory, a skin JSON file, and a plan output directory.");
var input = Path.GetFullPath(args[0]);
var files = File.Exists(input) ? new[] { input }
    : Directory.GetFiles(input, "motion.vmd", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
if (files.Length == 0) throw new InvalidDataException("No motion.vmd files were found.");
using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/native-model-bind.json")));
using var sourceSkin = JsonDocument.Parse(File.ReadAllText(args[1]));

var skin = new Skin(sourceSkin.RootElement);
var fixtureHash = fixture.RootElement.GetProperty("source").GetProperty("sha256").GetString();
if (sourceSkin.RootElement.GetProperty("bundleSha256").GetString() != fixtureHash)
    throw new InvalidDataException("Skin and reference binding must come from the same original bundle.");
Directory.CreateDirectory(args[2]);
const float scale = 1.125f;

foreach (string file in files)
{
    string name = Path.GetFileName(Path.GetDirectoryName(file));
    using var stream = File.OpenRead(file);
    using var sha = SHA256.Create();
    string hash = Convert.ToHexString(sha.ComputeHash(stream));
    var motion = MmdMotion.Read(file);
    using var actor = new NativeBundleRegression.NativeActor(fixture.RootElement, scale);
    var bones = actor.Bones.Values.ToDictionary(b => b.name);
    var transforms = actor.Bones.Values.ToArray();
    var skinBones = skin.Paths.Select(path => actor.Bones[path]).ToArray();
    var limbs = new[]
    {
        new Limb(bones, false, "_L"), new Limb(bones, false, "_R"),
        new Limb(bones, true, "_L"), new Limb(bones, true, "_R")
    };
    skin.UpdateMatrices(skinBones);
    if (skin.ValidateBind(actor.Root.transform.Find(skin.Name).localToWorldMatrix) > .00002f)
        throw new InvalidDataException("Skin vertices do not match the original binding.");
    actor.Bones["Armature/Hips"].position -= Vector3.up * .0206f;
    var action = new ActionDefinition { Id = name, WriteFaceMorphs = false, WriteHipTranslation = true };
    using var pose = new RuntimeVmdPose(actor.Root, action, motion, MmdReferencePose.A);
    int frames = checked((int)motion.lastFrame + 1);
    var baseline = new Local[frames * transforms.Length];
    for (int frame = 0; frame < frames; ++frame)
    {
        if (!pose.Apply(frame / (float)GroundContact.SampleRate))
            throw new InvalidOperationException("Playback failed on frame " + frame);
        int offset = frame * transforms.Length;
        for (int i = 0; i < transforms.Length; ++i)
            baseline[offset + i] = new Local(transforms[i].localPosition, transforms[i].localRotation);
        skin.UpdateMatrices(skinBones);
        foreach (var limb in limbs) limb.Capture(skin, pose.RootDisplacement);
    }

    var plans = limbs.Select(l => GroundContact.PlanLift(l.Samples.ToArray(), l.Hand, scale / 1.125f, l.ReferenceLength)).ToArray();
    using var data = new MemoryStream(checked(8 + frames * limbs.Length * 12));
    using var output = new BinaryWriter(data);
    output.Write(MmdGroundContactPlan.Magic);
    output.Write(frames);
    var endBefore = new Vector3[limbs.Length];
    var offsets = new float[limbs.Length];
    int active = 0;
    for (int frame = 0; frame < frames; ++frame)
    {
        for (int i = 0; i < limbs.Length; ++i) offsets[i] = plans[i][frame];
        bool adjust = offsets.Any(rise => rise > .00001f);
        if (adjust)
        {
            int offset = frame * transforms.Length;
            for (int i = 0; i < transforms.Length; ++i)
            {
                transforms[i].localPosition = baseline[offset + i].Position;
                transforms[i].localRotation = baseline[offset + i].Rotation;
            }
            for (int i = 0; i < limbs.Length; ++i)
            {
                endBefore[i] = limbs[i].End.position;
                limbs[i].Lift(offsets[i]);
            }
            skin.UpdateMatrices(skinBones);
            // Contact vertices around joints may blend adjacent bones. Check
            // the final surface and compensate the remaining vertical residual.
            for (int i = 0; i < limbs.Length; ++i)
            {
                if (offsets[i] <= .00001f) continue;
                float target = limbs[i].Samples[frame].Lowest + offsets[i];
                var residual = target - skin.LowestPoint(limbs[i].Area).y;
                if (residual > .0001f) limbs[i].Lift(residual);
            }
        }
        for (int i = 0; i < limbs.Length; ++i)
        {
            var limb = limbs[i];
            var source = limb.Samples[frame];
            float offset = source.Lowest - source.Joint.y;
            float lift = adjust ? limb.End.position.y - endBefore[i].y : 0;
            float gain = 0;
            if (offsets[i] > 0 && lift > .0001f && source.Lowest < -.0001f)
            {
                gain = Math.Min(MmdGroundContactPlan.MaxGain, lift / -source.Lowest);
                active++;
            }
            else lift = 0;
            if (!float.IsFinite(offset) || !float.IsFinite(lift) || lift > MmdGroundContactPlan.MaxLift)
                throw new InvalidDataException($"Ground contact sample outside the plan format at frame {frame}.");
            output.Write(offset);
            output.Write(gain);
            output.Write(lift);
        }
    }
    var planPath = Path.Combine(args[2], hash + ".bin");
    File.WriteAllBytes(planPath, data.ToArray());
    Console.WriteLine($"{name}: {frames} frames, {active} contact samples — {hash}.bin");
}
MmdRigCache.Clear();

readonly record struct Local(Vector3 Position, Quaternion Rotation);

sealed class Limb
{
    readonly Transform root;
    readonly Transform middle;
    readonly Vector3 referenceNormal;
    public Transform End { get; }
    public string Area { get; }
    public bool Hand { get; }
    public float ReferenceLength { get; }
    public List<GroundContact.Sample> Samples { get; } = new();

    internal Limb(Dictionary<string, Transform> bones, bool hand, string side)
    {
        Hand = hand;
        root = bones[(hand ? "UpperArm" : "UpperLeg") + side];
        middle = bones[(hand ? "LowerArm" : "LowerLeg") + side];
        End = bones[(hand ? "Hand" : "Foot") + side];
        var length = Lengths();
        ReferenceLength = length.upper + length.lower;
        Area = (hand ? "hand" : "foot") + side;
        Vector3 normal = Vector3.up;
        if (hand)
        {
            var tip = bones["MiddleProximal" + side].position - End.position;
            var across = bones["IndexProximal" + side].position - bones["LittleProximal" + side].position;
            normal = Vector3.Cross(tip, across).normalized;
            if (normal.y > 0) normal = -normal;
        }
        referenceNormal = Quaternion.Inverse(End.rotation) * normal;
    }

    public Vector3 Support(Skin skin) => skin.LowestPoint(Hand ? Area + "/palm" : Area);

    public void Capture(Skin skin, Vector3 horizontalTravel)
    {
        float facing = Vector3.Dot(End.rotation * referenceNormal, Hand ? -Vector3.up : Vector3.up);
        Samples.Add(new GroundContact.Sample(Support(skin) + horizontalTravel, End.position + horizontalTravel,
            facing, skin.LowestPoint(Area).y));
    }

    public (float upper, float lower) Lengths() => (Vector3.Distance(root.position, middle.position), Vector3.Distance(middle.position, End.position));
    public float Lift(float rise) => MmdContactSolver.Lift(root, middle, End, rise);
}
