using System.Text;
using Seleka2.Actions;
using Seleka2.Mmd;

int count = 0;
void Check(bool ok, string name) { count++; if (!ok) throw new Exception(name); }
void Reject(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
string root = Path.Combine(Path.GetTempPath(), "mmd-import-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string folder = Path.Combine(root, "中文动作 Shake"); Directory.CreateDirectory(folder);
string manifest = Path.Combine(folder, "action.json");
void Json(string json) => File.WriteAllText(manifest, json);
MmdActionPack Read() => MmdActionPacks.Read(folder);
void Register(MmdActionPack pack) => ActionRegistry.RegisterRuntime(pack.Action, new Source(), pack.Directory, "test");
try
{
    File.WriteAllText(Path.Combine(folder, "motion.vmd"), "motion");
    Check(Read().ReferencePose==MmdReferencePose.A,"Omitted reference pose defaults to A");
    Json("{\"referencePose\":\"T\"}"); Check(Read().ReferencePose==MmdReferencePose.T,"Action JSON can explicitly select T");
    Json("{\"REFERENCEPOSE\":\"a\"}"); Check(Read().ReferencePose==MmdReferencePose.A,"Reference pose field and value are case insensitive");
    foreach(var json in new[]{"{\"referencePose\":null}","{\"referencePose\":0}","{\"referencePose\":true}","{\"referencePose\":\"\"}","{\"referencePose\":\"B\"}","{\"referencePose\":\"1\"}","{\"referencePose\":\"A\",\"ReferencePose\":\"T\"}"})
    {Json(json);Reject(()=>Read(),"Invalid reference pose is rejected rather than silently falling back: "+json);}
    File.Delete(manifest);
    Check(Read().Action.KeepGameExpressions,"Omitted face policy defaults to overlay");
    Json("{\"keepGameExpressions\":false}"); var faceOff=Read();
    Check(!faceOff.Action.KeepGameExpressions,"JSON false disables overlay");
    Register(faceOff); Check(!ActionRegistry.Get(faceOff.Action.Id).KeepGameExpressions,"Registry snapshot preserves hard veto"); ActionRegistry.UnregisterProvider("test");
    Json("{\"KEEPGAMEEXPRESSIONS\":true}"); Check(Read().Action.KeepGameExpressions,"Face field is case insensitive");
    Json("{\"keepGameExpressions\":null}"); Reject(()=>Read(),"Null face policy rejected");
    Json("{\"keepGameExpressions\":\"false\"}"); Reject(()=>Read(),"String face policy rejected");
    Json("{\"keepGameExpressions\":false,\"KeepGameExpressions\":true}"); Reject(()=>Read(),"Duplicate face policy rejected");
    File.Delete(manifest);
    var silent = Read();
    Check(silent.Action.Id == "中文动作 Shake" && silent.Action.DisplayName == "中文动作 Shake", "Folder names are preserved verbatim");
    Check(silent.Action.AudioFile == "" && !silent.MatchedModel && silent.ModelPath == "", "Optional music and paired model may be absent");
    Check(silent.Action.Volume == 1 && silent.Action.RequiresHands && silent.Action.RequiresStanding && silent.Action.ShowInRing, "Native action defaults are retained");
    Register(silent);
    Check(ActionRegistry.Get("中文动作 Shake")?.RuntimeSource != null, "Unicode ID works in production registry");
    Reject(() => Register(silent), "Duplicate IDs rejected without renaming");
    ActionRegistry.UnregisterProvider("test");
    File.WriteAllText(Path.Combine(folder, "BGM.wav"), "audio");
    File.WriteAllText(Path.Combine(folder, "source.pmd"), "paired");
    using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "camera.vmd"))))
    {
        var header = new byte[30]; Encoding.ASCII.GetBytes("Vocaloid Motion Data 0002").CopyTo(header, 0);
        writer.Write(header); writer.Write(new byte[20]); writer.Write(0u); writer.Write(0u); writer.Write(1u);
        writer.Write(0u); writer.Write(-30f); writer.Write(0f); writer.Write(10f); writer.Write(0f);
        writer.Write(0f); writer.Write(0f); writer.Write(0f);
        writer.Write(Enumerable.Range(0, 6).SelectMany(_ => new byte[] { 20, 107, 20, 107 }).ToArray()); writer.Write(30u); writer.Write((byte)0);
    }
    var discovered = Read();
    Json("{\"referencePose\":\"T\"}");Check(Read().MatchedModel&&Read().ReferencePose==MmdReferencePose.T,"Paired model discovery remains independent of the generated-rig pose option");File.Delete(manifest);
    Check(discovered.Action.AudioFile == "BGM.wav" && discovered.MatchedModel && discovered.ModelPath == Path.Combine(folder, "source.pmd"), "Default WAV and paired PMD are discovered");
    Json("{}"); var empty = Read();
    Check(empty.Action.Id == discovered.Action.Id && empty.Action.AudioFile == discovered.Action.AudioFile && empty.ModelPath == discovered.ModelPath && empty.MatchedModel == discovered.MatchedModel, "Empty manifest and no manifest have identical defaults");
    MmdCameraPacks.Register("default", folder, discovered.Json);
    Check(MmdCameraPacks.Get("default")?.Options.VmdFile == "camera.vmd", "Camera is discovered without JSON");
    Json("{\"displayName\":\"特别版\",\"camera\":{\"heightOffset\":0.1}}");
    var partial = Read(); MmdCameraPacks.Register("partial", folder, partial.Json);
    var camera = MmdCameraPacks.Get("partial");
    Check(partial.Action.DisplayName == "特别版" && partial.Action.Id == discovered.Action.Id && partial.Action.AudioFile == "BGM.wav" && partial.MatchedModel, "Partial JSON keeps folder, audio and model defaults");
    Check(camera.Options.VmdFile == "camera.vmd" && camera.Options.Scale == .08f && camera.Options.HeightOffset == .1f, "Nested camera overrides retain filename and scale");
    Json("{\"audioFile\":\"\",\"volume\":0,\"requiresStanding\":false,\"showInRing\":false,\"camera\":{\"enabled\":false,\"vmdFile\":\"missing.vmd\"}}");
    var disabled = Read(); Register(disabled); MmdCameraPacks.Register("disabled", folder, disabled.Json);
    Check(disabled.Action.AudioFile == "" && disabled.Action.Volume == 0 && !disabled.Action.RequiresStanding && !disabled.Action.ShowInRing && MmdCameraPacks.Get("disabled") == null, "Explicit empty, zero and false override defaults");
    ActionRegistry.UnregisterProvider("test");
    Json("{\"id\":\"FixedID\"}"); Check(Read().Action.DisplayName == "中文动作 Shake" && Read().Action.Id == "FixedID", "ID override does not change default display name");
    foreach (var json in new[] { "{\"vmdFile\":\"missing.vmd\"}", "{\"audioFile\":\"missing.wav\"}", "{\"modelFile\":\"missing.pmd\"}", "{\"vmdFile\":\"\"}", "{\"modelFile\":\"\"}", "{\"audioFile\":null}", "{\"volume\":null}", "{\"vmdFile\":\"../outside.vmd\"}", "{\"modelFile\":\"../reference-skeleton.pmd\"}", "{\"schemaVersion\":1}", "{\"bundle\":\"old.bundle\"}", "{\"volum\":0.5}", "{\"volume\":1,\"Volume\":0}", "[]", "null", "{" })
    { Json(json); Reject(() => Read(), "Invalid manifest does not fall back: " + json); }
    Json("{\"vmdFile\":" + System.Text.Json.JsonSerializer.Serialize(Path.Combine(folder, "motion.vmd")) + "}");
    Reject(() => Read(), "Absolute motion path rejected");
    foreach (var json in new[] { "{\"volume\":2}", "{\"audioDelaySeconds\":-1}", "{\"id\":\"comma,id\"}", "{\"id\":\"\"}", "{\"requiresHands\":false}", "{\"loop\":true}" })
    { Json(json); Reject(() => Register(Read()), "Production registry rejects invalid action rules: " + json); }
    Json("{\"audioFile\":\"\",\"loop\":true,\"requiresHands\":false,\"writeFaceMorphs\":false,\"bodyMask\":\"LowerBody\"}");
    Register(Read()); Check(ActionRegistry.Get("中文动作 Shake").Loop, "Documented silent lower-body example is accepted"); ActionRegistry.UnregisterProvider("test");
    Json("{\"bodyMask\":\"UpperBody\",\"writeFaceMorphs\":false,\"bonePaths\":[\"Armature/Hips/Spine/Chest/Neck/Head\"]}");
    Register(Read()); Check(ActionRegistry.Get("中文动作 Shake").BonePaths.Length == 1, "Documented bone-path example is accepted"); ActionRegistry.UnregisterProvider("test");
    foreach (var json in new[] { "{\"camera\":{\"vmdFile\":\"missing.vmd\"}}", "{\"camera\":{\"vmdFile\":\"\"}}", "{\"camera\":{\"scale\":0}}", "{\"camera\":null}", "{\"camera\":{\"heigthOffset\":1}}" })
    {
        Json(json); var pack = Read(); Register(pack);
        Reject(() => MmdCameraPacks.Register("invalid-camera-" + count, folder, pack.Json), "Camera error is isolated: " + json);
        Check(ActionRegistry.Get(pack.Action.Id) != null, "Bad camera leaves motion and audio registered"); ActionRegistry.UnregisterProvider("test");
    }
    string custom = Path.Combine(root, "自定义文件名"); Directory.CreateDirectory(custom);
    File.WriteAllText(Path.Combine(custom, "舞蹈.vmd"), "motion"); File.WriteAllText(Path.Combine(custom, "action.json"), "{\"vmdFile\":\"舞蹈.vmd\"}");
    string duplicate = Path.Combine(root, "nested", "中文动作 Shake"); Directory.CreateDirectory(duplicate); File.WriteAllText(Path.Combine(duplicate, "motion.vmd"), "motion");
    var folders = MmdActionPacks.FindFolders(root).ToArray();
    Check(folders.Length == 3 && folders.Contains(custom) && folders.Contains(folder), "Discovery includes custom filenames, deduplicates manifest plus motion");
    Json("{}"); Register(Read()); Reject(() => Register(MmdActionPacks.Read(duplicate)), "Duplicate folder names cause a real registry collision"); ActionRegistry.UnregisterProvider("test");
    string optional = Path.Combine(root, "optional"); Directory.CreateDirectory(optional); File.WriteAllText(Path.Combine(optional, "motion.vmd"), "motion");
    MmdCameraPacks.Register("no-camera", optional, "{\"camera\":{\"heightOffset\":0.1}}"); Check(MmdCameraPacks.Get("no-camera") == null, "Missing default camera is optional even with other camera parameters");
    if (args.Length > 0)
    {
        string resources = Path.GetFullPath(args[0]);
        foreach (string name in new[] { "WAVEFILE", "Shake" })
        {
            var pack = MmdActionPacks.Read(Path.Combine(resources, name)); Register(pack);
            MmdCameraPacks.Register(name, pack.Directory, pack.Json);
            Check(pack.Action.Id == name && pack.Action.DisplayName == name && pack.Action.Volume == 1 && pack.Action.AudioFile == "BGM.wav", name + " effective defaults");
            Check(MmdCameraPacks.Get(name).Motion.KeyCount > 10, name + " actual camera resolves");
            Check(name == "WAVEFILE" ? pack.MatchedModel && Math.Abs(pack.Action.AudioDelaySeconds - 160d / 30) < 1e-10 : !pack.MatchedModel && pack.Action.AudioDelaySeconds == 0, name + " skeleton policy and audio timing");
            Console.WriteLine($"PASS {name}: id={pack.Action.Id} volume={pack.Action.Volume} delay={pack.Action.AudioDelaySeconds} pairedPMD={pack.MatchedModel} cameraKeys={MmdCameraPacks.Get(name).Motion.KeyCount}");
        }
    }
    Console.WriteLine("PASS " + count + " assertions: actual pack resolver/registry, Unicode, sparse JSON, nested defaults, overrides, duplicate IDs, path restrictions, camera isolation and real resources");
}
finally
{
    // Only remove the unique fixture directory created above.
    ActionRegistry.UnregisterProvider("test"); Directory.Delete(root, true);
}

sealed class Source : IActionSource
{
    public float Duration => 1;
    public IActionPose CreatePose(UnityEngine.GameObject target, ActionDefinition action) => throw new NotSupportedException();
}
namespace UnityEngine { public sealed class GameObject {} public struct Vector3 { public float x,y,z; } }
namespace Seleka2.Actions
{
    public sealed class Logger { public void LogWarning(string message) {} }
    public static class Plugin { public static Logger LogSource = new(); }
    public sealed class ActionDriver { public static ActionDriver Current; public void Request(string id) {} public void Stop(string reason) {} }
}
