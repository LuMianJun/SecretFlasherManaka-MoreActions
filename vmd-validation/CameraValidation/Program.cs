using System.Numerics;
using System.Text;
using System.Text.Json;
using Seleka2.Mmd;
using Seleka2.Actions;
using ExposureUnnoticed2.Object3D.Player.Scripts;

if(args.Length==2&&args[0]=="--injection-check")
{
    try { InstalledCameraContract.VerifyDriverInjectionBoundary(args[1]); }
    catch(Exception e) { Console.Error.WriteLine(e.Message);Environment.ExitCode=1; }
    return;
}
bool packageOnly = args.Length > 1 && args[0] == "--package";
int assertions = 0;
void Check(bool ok, string name) { assertions++; if (!ok) throw new Exception(name); }
bool Close(float a, float b) => Math.Abs(a - b) < 0.0001;
void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
string directory = Path.Combine(Path.GetTempPath(), "seleka-camera-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    byte[] Curves() => Enumerable.Range(0, 6).SelectMany(_ => new byte[] { 20, 107, 20, 107 }).ToArray();
    string Fixture(string name, (uint frame, float distance, Vector3 center, Vector3 euler, uint fov, byte projection)[] keys, bool old = false, bool withBones = false)
    {
        string file = Path.Combine(directory, name);
        using var w = new BinaryWriter(File.Create(file));
        void Text(string text, int size) { byte[] b = new byte[size]; Encoding.ASCII.GetBytes(text).CopyTo(b, 0); w.Write(b); }
        Text(old ? "Vocaloid Motion Data file" : "Vocaloid Motion Data 0002", 30); w.Write(new byte[old ? 10 : 20]);
        w.Write(withBones ? 1u : 0u); if (withBones) w.Write(new byte[111]);
        w.Write(withBones ? 1u : 0u); if (withBones) w.Write(new byte[23]);
        w.Write((uint)keys.Length);
        foreach (var k in keys)
        {
            w.Write(k.frame); w.Write(k.distance);
            w.Write(k.center.X); w.Write(k.center.Y); w.Write(k.center.Z);
            w.Write(k.euler.X); w.Write(k.euler.Y); w.Write(k.euler.Z);
            w.Write(Curves()); w.Write(k.fov); w.Write(k.projection);
        }
        return file;
    }
    var vmd = Fixture("linear.vmd", new[] { (0u, -30f, new Vector3(0, 10, 0), Vector3.Zero, 30u, (byte)0), (30u, -10f, new Vector3(10, 20, 30), Vector3.Zero, 60u, (byte)1) }, withBones: true);
    var motion = VmdCameraMotion.Read(vmd);
    Check(Close(new CameraOptions().Scale, .08f), "Camera defaults to 0.08 game units per MMD unit without a model");
    Check(motion.KeyCount == 2 && motion.Duration == 1, "Skip bone/morph sections and parse camera");
    var mid = motion.Sample(.5);
    Check(Vector3.Distance(mid.Center, new Vector3(5, 15, 15)) < .0001 && Close(mid.Distance, -20) && Close(mid.Fov, 45), "Six channel interpolation");
    Check(mid.Perspective && !motion.Sample(1).Perspective, "Projection switches at key, not mid-interval");
    Check(motion.Sample(-1).Center == motion.Sample(0).Center && motion.Sample(100).Center == motion.Sample(1).Center, "Clamp before/after track");
    var front = VmdCameraMotion.ToGame(motion.Sample(0), Vector3.Zero, Quaternion.Identity, .1f, 0);
    Check(Vector3.Distance(front.Position, new Vector3(0, 1, 3)) < .0001, "MMD zero-angle camera is in front of game character");
    Check(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, front.Rotation), -Vector3.UnitZ) < .0001, "Camera looks toward game character");
    var sideTrack = VmdCameraMotion.Read(Fixture("yaw.vmd", new[] { (0u, -30f, new Vector3(0, 10, 0), new Vector3(0, MathF.PI / 2, 0), 30u, (byte)0) }));
    var side = VmdCameraMotion.ToGame(sideTrack.Sample(0), Vector3.Zero, Quaternion.Identity, .1f, 0);
    Check(Vector3.Distance(side.Position, new Vector3(-3, 1, 0)) < .0001 && Vector3.Distance(Vector3.Transform(Vector3.UnitZ, side.Rotation), Vector3.UnitX) < .0001, "Known 90-degree camera yaw position/forward");
    var rollTrack = VmdCameraMotion.Read(Fixture("roll.vmd", new[] { (0u, -30f, new Vector3(0, 10, 0), new Vector3(0, 0, MathF.PI / 2), 30u, (byte)0) }));
    var roll = VmdCameraMotion.ToGame(rollTrack.Sample(0), Vector3.Zero, Quaternion.Identity, .1f, 0);
    Check(Vector3.Distance(Vector3.Transform(Vector3.UnitY, roll.Rotation), -Vector3.UnitX) < .0001, "Camera roll preserves rotated up direction");
    var anchored = VmdCameraMotion.ToGame(motion.Sample(0), new Vector3(100, 3, 20), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), .1f, .2f);
    Check(Vector3.Distance(anchored.Position, new Vector3(103, 4.2f, 20)) < .0001, "Player anchor/yaw/height are applied once");
    var cut = VmdCameraMotion.Read(Fixture("cut.vmd", new[] { (0u, -30f, Vector3.Zero, Vector3.Zero, 30u, (byte)0), (1u, -10f, Vector3.One, Vector3.Zero, 60u, (byte)0) }));
    Check(cut.Sample(.5 / 30).Center == Vector3.Zero && cut.Sample(1d / 30).Center == Vector3.One, "Adjacent-frame cut");
    var old = VmdCameraMotion.Read(Fixture("old.vmd", new[] { (0u, -30f, Vector3.Zero, Vector3.Zero, 30u, (byte)0) }, old: true));
    Check(old.KeyCount == 1, "VMD 1 header");
    var padded = Path.Combine(directory, "padded.vmd"); var paddedBytes = File.ReadAllBytes(vmd); paddedBytes[26] = 74; paddedBytes[27] = 75; File.WriteAllBytes(padded, paddedBytes);
    Check(VmdCameraMotion.Read(padded).KeyCount == 2, "Ignore header padding after first NUL");
    var duplicate = VmdCameraMotion.Read(Fixture("duplicate.vmd", new[] { (30u, -20f, Vector3.One, Vector3.Zero, 40u, (byte)0), (0u, -30f, Vector3.Zero, Vector3.Zero, 30u, (byte)0), (30u, -10f, Vector3.One, Vector3.Zero, 50u, (byte)0) }));
    Check(duplicate.KeyCount == 2 && duplicate.Sample(1).Fov == 50, "Unsorted duplicate frames resolve deterministically");
    Reject(() => VmdCameraMotion.Read(Fixture("empty.vmd", Array.Empty<(uint, float, Vector3, Vector3, uint, byte)>())), "Empty camera rejected");
    Reject(() => VmdCameraMotion.Read(Fixture("nan.vmd", new[] { (0u, float.NaN, Vector3.Zero, Vector3.Zero, 30u, (byte)0) })), "NaN rejected");
    Reject(() => VmdCameraMotion.Read(Fixture("badfov.vmd", new[] { (0u, -30f, Vector3.Zero, Vector3.Zero, 180u, (byte)0) })), "Bad FOV rejected");
    var truncated = Path.Combine(directory, "truncated.vmd"); File.WriteAllBytes(truncated, File.ReadAllBytes(vmd)[..^1]);
    Reject(() => VmdCameraMotion.Read(truncated), "Truncated camera rejected");
    byte[] nonlinear = Curves(); nonlinear[2] = 0; nonlinear[3] = 0;
    Check(VmdCameraMotion.Bezier(nonlinear, 0, .5f) < .5f && Close(VmdCameraMotion.Bezier(nonlinear, 1, .5f), .5f), "Independent camera Bezier controls");

    string Pack(string file, float scale = .1f, double offset = 0) => JsonSerializer.Serialize(new { Camera = new { VmdFile = file, Scale = scale, TimeOffsetSeconds = offset } });
    MmdCameraPacks.Register("fixture", directory, Pack("linear.vmd"));
    Check(Close(MmdCameraPacks.Get("fixture").Options.Scale, .1f), "Explicit per-pack scale overrides the new default");
    Reject(() => MmdCameraPacks.Register("escape", directory, Pack("../outside.vmd")), "Relative camera path cannot escape pack");
    Reject(() => MmdCameraPacks.Register("absolute", directory, Pack(vmd)), "Absolute camera path rejected");
    Reject(() => MmdCameraPacks.Register("scale", directory, Pack("linear.vmd", -1)), "Negative scale rejected");
    MmdCameraPacks.Register("absent", directory, "{}"); Check(MmdCameraPacks.Get("absent") == null, "No Camera property is optional");
    MmdCameraPacks.Register("disabled", directory, "{\"camera\":{\"enabled\":false,\"vmdFile\":\"missing.vmd\"}}"); Check(MmdCameraPacks.Get("disabled") == null, "Disabled camera does not load file");
    MmdCameraPacks.Register("delayed", directory, Pack("linear.vmd", offset: 2));

    var owner = new PlayerController(); owner.transform.position = new UnityEngine.Vector3(10, 2, 30);
    PlayerController.Instance = owner;
    var native = owner.Pca.PlayerLocomotion.baseCameraController; var camera = native.Camera;
    camera.transform.position = new UnityEngine.Vector3(99, 98, 97); camera.fieldOfView = 77; camera.orthographic = true; camera.orthographicSize = 4; camera.cullingMask = 321;
    var driver = new MmdCameraDriver(IntPtr.Zero); driver.Bind();
    var definition = new ActionDefinition { Id = "fixture", ProviderId = global::Plugin.Guid }; ActionRegistry.Entries[definition.Id] = definition;
    void Start(ActionDefinition def) => ActionPlaybackApi.Publish(def, true);
    void Stop() => ActionPlaybackApi.Publish(definition, false);
    Start(definition);
    Check(driver.Defer(native) && driver.Defer(owner.Pca.PlayerLocomotion.tpCameraController) && driver.Defer(owner.Pca.PlayerLocomotion.fpCameraController), "Own only main/TP/FP cameras during playback");
    Check(!driver.Defer(new ExposureUnnoticed2.Object3D.Camera.BaseCameraController()), "Other cameras remain independent");
    Check(native.IsLockCamera && !camera.orthographic && camera.cullingMask == 999, "Native input lock and full-player mask");
    ActionPlaybackApi.PublishPose("fixture", .5);
    Check(Close(camera.fieldOfView, 45) && ActionPlaybackApi.PlaybackPositionSeconds == .5, "Pose event applies exact shared clock");
    Stop();
    Check(!driver.Defer(native) && !native.IsLockCamera && Close(camera.fieldOfView, 77) && camera.orthographic && camera.orthographicSize == 4 && camera.cullingMask == 321 && camera.transform.position.x == 99, "Stop restores all captured camera state");
    Start(definition);owner.transform.position=new UnityEngine.Vector3(11,2.5f,32);ActionPlaybackApi.PublishPose("fixture",.5);
    var anchoredDuringWalk=VmdCameraMotion.ToGame(motion.Sample(.5),new Vector3(10,2,30),Quaternion.Identity,.1f,0);
    Check(Close(camera.transform.position.x,anchoredDuringWalk.Position.X)&&Close(camera.transform.position.z,anchoredDuringWalk.Position.Z),"Walking does not add player displacement a second time to the VMD camera");
    ActionPlaybackApi.SetRootCycleOffset(new UnityEngine.Vector3(2,0,3));ActionPlaybackApi.PublishPose("fixture",.5);
    Check(Close(camera.transform.position.x,anchoredDuringWalk.Position.X+2)&&Close(camera.transform.position.z,anchoredDuringWalk.Position.Z+3),"Authored loop translation is unrolled once for the camera, without live-player compensation");
    Stop();Check(Close(camera.transform.position.x,100)&&Close(camera.transform.position.y,98.5f)&&Close(camera.transform.position.z,99),"Returning to native view preserves its relative offset at the reached position");
    owner.transform.position=new UnityEngine.Vector3(10,2,30);camera.transform.position=new UnityEngine.Vector3(99,98,97);
    Start(definition); global::Plugin.UseCamera.Value = false; driver.Update();
    Check(!driver.Defer(native) && Close(camera.fieldOfView, 77), "Disable distribution returns native camera during continuing action");
    global::Plugin.UseCamera.Value = true; ActionPlaybackApi.PublishPose("fixture", .75);
    Check(driver.Defer(native) && Close(camera.fieldOfView, 52.5f), "Re-enable rejoins current action time");
    PlayerController.Instance = new PlayerController(); driver.Update();
    Check(!driver.Defer(native) && !native.IsLockCamera && Close(camera.fieldOfView, 77), "Scene/player replacement releases old camera");
    PlayerController.Instance = owner; Start(definition); driver.OnDisable();
    Check(!driver.Defer(native) && Close(camera.fieldOfView, 77), "Component disable restores camera");
    var delayed = new ActionDefinition { Id = "delayed", ProviderId = global::Plugin.Guid }; ActionRegistry.Entries[delayed.Id] = delayed;
    Start(delayed); Check(!driver.Defer(native), "Positive camera offset waits in native view");
    ActionPlaybackApi.PublishPose("delayed", 2.5); Check(driver.Defer(native) && Close(camera.fieldOfView, 45), "Camera offset relative to motion, not audio start");
    Stop();
    var ordinary = new ActionDefinition { Id = "fixture", ProviderId = "another.provider" }; Start(ordinary);
    Check(!driver.Defer(native), "Other providers never activate MMD camera");
    Start(definition); camera.isActiveAndEnabled = false;
    Check(!driver.Defer(native) && !native.IsLockCamera, "Inactive main camera releases control"); camera.isActiveAndEnabled = true; Stop();
    Start(definition); camera.transform.ThrowOnWrite = true;
    Reject(() => driver.OnDisable(), "Restoration failure fixture");
    Check(!native.IsLockCamera, "Restoration exception still releases native input lock");
    camera.transform.ThrowOnWrite = false;
    Start(definition); camera.transform.ThrowOnWrite = true; global::Plugin.UseCamera.Value = false;
    Check(!driver.Defer(native) && !native.IsLockCamera, "Failed restoration never blocks native Harmony tick");
    camera.transform.ThrowOnWrite = false; global::Plugin.UseCamera.Value = true;
    Start(definition); camera.transform.ThrowOnWrite = true;
    Reject(() => driver.OnDestroy(), "Destroy restoration failure fixture");
    camera.transform.ThrowOnWrite = false;
    Check(MmdCameraDriver.Current == null && !driver.Defer(native) && !native.IsLockCamera, "Destroy always unsubscribes and clears owner after restoration failure");

    var clock = new MotionAudioClock(); clock.Start(100, 160 / 30d);
    Check(Math.Abs(clock.AudioStartDsp - clock.MotionStartDsp - 160 / 30d) < .00001 && Close(clock.Position(101.1), 1), "Existing WAVEFILE audio delay and camera clock share motion origin");
    Action<string, double> bad = (_, _) => throw new Exception("isolated"); int calls = 0;
    Action<string, double> good = (_, _) => calls++;
    ActionPlaybackApi.PoseApplied += bad; ActionPlaybackApi.PoseApplied += good;
    Start(definition); ActionPlaybackApi.PublishPose("fixture", 1);
    Check(calls == 1, "Broken pose consumer cannot break other listeners"); Stop(); ActionPlaybackApi.PublishPose("fixture", 2);
    Check(calls == 1 && ActionPlaybackApi.PlaybackPositionSeconds == 0, "No pose events after stop");
    ActionPlaybackApi.PoseApplied -= bad; ActionPlaybackApi.PoseApplied -= good;

    if (args.Length > 0 && !packageOnly)
    {
        var wave = VmdCameraMotion.Read(args[0]);
        Check(wave.KeyCount > 10 && wave.Duration > 80, "Real WAVEFILE camera parses");
        for (double t = 0; t <= wave.Duration; t += 1d / 60)
        {
            var sample = wave.Sample(t); var converted = VmdCameraMotion.ToGame(sample, Vector3.Zero, Quaternion.Identity, .08f, 0);
            if (!float.IsFinite(converted.Position.X) || !float.IsFinite(converted.Position.Y) || !float.IsFinite(converted.Position.Z) || !float.IsFinite(converted.Rotation.W)) throw new Exception("Non-finite WAVEFILE output");
        }
        Console.WriteLine($"WAVEFILE camera keys={wave.KeyCount} duration={wave.Duration:F6}s sampledAt=60Hz");
    }
    if (args.Length > 3 && !packageOnly)
    {
        var pack = MmdCameraPacks.Read(Path.GetDirectoryName(Path.GetFullPath(args[3]))!, "{}");
        foreach (var fixture in new[] { (Frame:33, Height:16.1f), (Frame:75, Height:17.45f), (Frame:91, Height:21.3f) })
        {
            var converted = VmdCameraMotion.ToGame(pack.Motion.Sample(fixture.Frame / 30d), Vector3.Zero, Quaternion.Identity, pack.Options.Scale, pack.Options.HeightOffset);
            Check(Close(converted.Position.Y, fixture.Height * .08f), "Actual CH4NGE opening uses default scale at a ground origin without model data");
        }
        Console.WriteLine("PASS CH4NGE opening: face=1.288m; hand=1.396-1.704m; zero added height");
    }
    Console.WriteLine($"PASS {assertions} assertions: VMD parser, cuts, coordinates, synchronization, optional packs, ownership, restoration and failure isolation");
    if (args.Length > 1) InstalledCameraContract.Verify(args[1], !packageOnly);
    if (args.Length > 2 && !packageOnly)
    {
        var shake = VmdCameraMotion.Read(args[2]);
        for (double t = 0; t <= shake.Duration; t += 1d / 60)
        {
            var converted = VmdCameraMotion.ToGame(shake.Sample(t), Vector3.Zero, Quaternion.Identity, .08f, 0);
            if (!float.IsFinite(converted.Position.X) || !float.IsFinite(converted.Position.Y) || !float.IsFinite(converted.Position.Z) || !float.IsFinite(converted.Rotation.W)) throw new Exception("Non-finite Shake camera output");
        }
        Console.WriteLine($"PASS SHAKE camera keys={shake.KeyCount} duration={shake.Duration:F6}s sampledAt=60Hz");
    }
}
finally { Directory.Delete(directory, true); }
