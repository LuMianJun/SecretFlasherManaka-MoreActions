using SFM.Actions;
using UnityEngine;

int count = 0;
void Check(bool ok, string name) { count++; if (!ok) throw new Exception(name); }
void Reject(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
string root = Path.Combine(Path.GetTempPath(), "more-actions-runtime-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "BGM.wav"), new byte[] { 1 });
try
{
    var source = new Source(22.3f);
    var definition = new ActionDefinition { Id = "shake", DisplayName = "Shake", AudioFile = "BGM.wav", Stats=new(){EcstasyPerSecond=.03f} };
    ActionRegistry.RegisterRuntime(definition, source, root, "mmd");
    var registered = ActionRegistry.Get("shake");
    Check(registered.RuntimeSource == source && registered.BundlePath == "" && registered.ProviderId == "mmd", "Source registration does not require a bundle");
    Check(registered.AudioPath == Path.Combine(root, "BGM.wav"), "Runtime imports retain shared audio loading");
    definition.RequiresHands = false; definition.DisplayName = "changed";definition.Stats.EcstasyPerSecond=.9f;
    Check(registered.RequiresHands && registered.DisplayName == "Shake", "Registry snapshots provider rules");
    Check(registered.Stats.EcstasyPerSecond==.03f,"Registry owns an independent snapshot of nested stat configuration");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "shake", DisplayName = "duplicate" }, source, root, "mmd"), "Duplicate runtime IDs rejected");
    Reject(() => ActionRegistry.Register(new ActionDefinition { Id = "missing", DisplayName = "Missing" }, root, "base"), "Clip packs still require clip assets");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "escape", DisplayName = "Escape", AudioFile = "../outside.wav" }, source, root, "mmd"), "Runtime audio cannot escape package");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "invalid", DisplayName = "Invalid" }, new Source(float.NaN), root, "mmd"), "Invalid source duration rejected");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "mixed", DisplayName = "Mixed", Bundle = "bundle" }, source, root, "mmd"), "Ambiguous clip/runtime sources rejected");
    var idle = new ActionAvailability(true, true, false, false, false, false, false, false, false, false, false);
    Check(ActionPolicy.BlockReason(registered, idle) == null && ActionPolicy.BlockReason(registered, idle with { Handcuffed = true }) == "handcuffed", "Runtime actions retain native handcuff restrictions");
    Check(ActionPolicy.BlockReason(registered, idle with { Sitting = true }) == "stand first" && ActionPolicy.BlockReason(registered, idle with { Moving = true }) == "moving", "Runtime actions retain posture/movement restrictions");
    ActionRegistry.UnregisterProvider("mmd"); Check(ActionRegistry.Get("shake") == null, "Runtime provider cleanup");
    var pack=Path.Combine(root,"MyPack");Directory.CreateDirectory(pack);
    File.WriteAllBytes(Path.Combine(pack,"pose.bundle"),new byte[]{1});
    File.WriteAllText(Path.Combine(pack,"pose.action.json"),"{\"bundle\":\"pose.bundle\",\"clipAsset\":\"Pose\"}");
    Check(ActionRegistry.LoadPacks(root,"unity")==1,"Unity JSON can omit id and displayName");
    var imported=ActionRegistry.Get("MyPack/Pose");
    Check(imported!=null&&imported.DisplayName=="Pose","Default identity uses relative folder plus clip name");
    ActionRegistry.RegisterAlias("legacy-unity-id",imported.Id);
    Check(ReferenceEquals(ActionRegistry.Get("legacy-unity-id"),imported),"Legacy IDs resolve to the registered action snapshot");
    Check(UnityActionIdentity.ForClip(root,root,"Pose")==new DirectoryInfo(root).Name+"/Pose","Root clips use the root folder name");
    Check(UnityActionIdentity.ClipKey("assets/animations/Pose.anim")=="Pose","Full resource paths resolve the clip key");
    Check(UnityActionIdentity.ForClip(root,pack,"Pose.v2")=="MyPack/Pose.v2","Clip suffixes other than .anim remain part of the identity");
    Check(!UnityActionIdentity.ForClip(root,pack,"Pose,variant").Contains(','),"Generated IDs cannot corrupt comma-delimited shortcuts");
    Reject(()=>UnityActionIdentity.ForClip(root,Path.GetDirectoryName(root),"Pose"),"Default IDs cannot escape the action root");
    File.Delete(Path.Combine(pack,"pose.action.json"));
    File.WriteAllText(Path.Combine(pack,"explicit.action.json"),"{\"id\":\"stable-id\",\"displayName\":\"Custom name\",\"bundle\":\"pose.bundle\",\"clipAsset\":\"Pose\"}");
    Check(ActionRegistry.LoadPacks(root,"unity")==1&&ActionRegistry.Get("stable-id").DisplayName=="Custom name","Explicit identity and display labels take priority");
    ActionRegistry.UnregisterProvider("unity");
    Check(ActionRegistry.Get("legacy-unity-id")==null,"Provider removal also removes legacy aliases");
    File.Delete(Path.Combine(pack,"explicit.action.json"));File.Delete(Path.Combine(pack,"pose.bundle"));Directory.Delete(pack);
    var samples=Path.Combine(root,"examples");Directory.CreateDirectory(samples);
    foreach(var name in new[]{"catpose","v-sign"})
    {
        File.WriteAllText(Path.Combine(samples,name),"UnityFS sample boundary");
        File.Copy(Path.Combine(AppContext.BaseDirectory,"examples",name+".action.json"),Path.Combine(samples,name+".action.json"));
    }
    Check(ActionRegistry.LoadPacks(samples,"samples")==0,"Sparse sidecars wait for asynchronous bundle metadata instead of guessing resource names");
    foreach(var pair in new[]{("catpose","CatPose"),("v-sign","V-sign")})
    {
        var sparse=UnityActionOverrides.Apply(new ActionDefinition{Id="Actions/"+pair.Item2,DisplayName=pair.Item2,Loop=true},UnityActionOverrides.Read(Path.Combine(samples,pair.Item1)));
        ActionRegistry.RegisterRuntime(sparse,source,samples,"samples");
    }
    var cat=ActionRegistry.Get("Actions/CatPose");var sign=ActionRegistry.Get("Actions/V-sign");
    Check(cat.Loop&&cat.PoseMission&&cat.Stats.EcstasyPerSecond==.1f,"Minimal CatPose config inherits looping, identity and native clip display name");
    Check(sign.Loop&&sign.PoseMission&&sign.Stats.CanStartPee==true,"Minimal V-sign config retains independent pee and pose flags");
    Check(ActionConfiguration.Parse("{\"stats\":{\"ecstasyPerSecond\":0.03},\"ecstasyPerSecond\":0.1}").Stats.EcstasyPerSecond==.1f,"Flat stat parameters override their legacy nested equivalent");
    ActionRegistry.UnregisterProvider("samples");
    foreach(var file in Directory.GetFiles(samples))File.Delete(file);Directory.Delete(samples);
    Console.WriteLine("PASS " + count + " assertions: runtime registration, audio paths, snapshots, invalid packs, native restrictions, provider cleanup");
}
finally { File.Delete(Path.Combine(root, "BGM.wav")); Directory.Delete(root); }

sealed class Source(float duration) : IActionSource
{
    public float Duration => duration;
    public IActionPose CreatePose(GameObject target, ActionDefinition action) => throw new NotSupportedException();
}
namespace UnityEngine { public sealed class GameObject {} public struct Vector3 { public float x,y,z; } }
namespace SFM.Actions
{
    public sealed class Logger { public void LogWarning(string message) {} }
    public static class Plugin { public static Logger LogSource = new(); }
    public sealed class ActionDriver { public static ActionDriver Current; public void Request(string id) {} public void Stop(string reason) {} }
}
