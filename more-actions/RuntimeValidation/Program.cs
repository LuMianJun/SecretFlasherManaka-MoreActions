using Seleka2.Actions;
using UnityEngine;

int count = 0;
void Check(bool ok, string name) { count++; if (!ok) throw new Exception(name); }
void Reject(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
string root = Path.Combine(Path.GetTempPath(), "more-actions-runtime-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); File.WriteAllBytes(Path.Combine(root, "BGM.wav"), new byte[] { 1 });
try
{
    var source = new Source(22.3f);
    var definition = new ActionDefinition { Id = "shake", DisplayName = "Shake", AudioFile = "BGM.wav" };
    ActionRegistry.RegisterRuntime(definition, source, root, "mmd");
    var registered = ActionRegistry.Get("shake");
    Check(registered.RuntimeSource == source && registered.BundlePath == "" && registered.ProviderId == "mmd", "Source registration does not require a bundle");
    Check(registered.AudioPath == Path.Combine(root, "BGM.wav"), "Runtime imports retain shared audio loading");
    definition.RequiresHands = false; definition.DisplayName = "changed";
    Check(registered.RequiresHands && registered.DisplayName == "Shake", "Registry snapshots provider rules");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "shake", DisplayName = "duplicate" }, source, root, "mmd"), "Duplicate runtime IDs rejected");
    Reject(() => ActionRegistry.Register(new ActionDefinition { Id = "missing", DisplayName = "Missing" }, root, "base"), "Clip packs still require clip assets");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "escape", DisplayName = "Escape", AudioFile = "../outside.wav" }, source, root, "mmd"), "Runtime audio cannot escape package");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "invalid", DisplayName = "Invalid" }, new Source(float.NaN), root, "mmd"), "Invalid source duration rejected");
    Reject(() => ActionRegistry.RegisterRuntime(new ActionDefinition { Id = "mixed", DisplayName = "Mixed", Bundle = "bundle" }, source, root, "mmd"), "Ambiguous clip/runtime sources rejected");
    var idle = new ActionAvailability(true, true, false, false, false, false, false, false, false, false, false);
    Check(ActionPolicy.BlockReason(registered, idle) == null && ActionPolicy.BlockReason(registered, idle with { Handcuffed = true }) == "handcuffed", "Runtime actions retain native handcuff restrictions");
    Check(ActionPolicy.BlockReason(registered, idle with { Sitting = true }) == "stand first" && ActionPolicy.BlockReason(registered, idle with { Moving = true }) == "moving", "Runtime actions retain posture/movement restrictions");
    ActionRegistry.UnregisterProvider("mmd"); Check(ActionRegistry.Get("shake") == null, "Runtime provider cleanup");
    Console.WriteLine("PASS " + count + " assertions: runtime registration, audio paths, snapshots, invalid packs, native restrictions, provider cleanup");
}
finally { File.Delete(Path.Combine(root, "BGM.wav")); Directory.Delete(root); }

sealed class Source(float duration) : IActionSource
{
    public float Duration => duration;
    public IActionPose CreatePose(GameObject target, ActionDefinition action) => throw new NotSupportedException();
}
namespace UnityEngine { public sealed class GameObject {} public struct Vector3 { public float x,y,z; } }
namespace Seleka2.Actions
{
    public sealed class Logger { public void LogWarning(string message) {} }
    public static class Plugin { public static Logger LogSource = new(); }
    public sealed class ActionDriver { public static ActionDriver Current; public void Request(string id) {} public void Stop(string reason) {} }
}
