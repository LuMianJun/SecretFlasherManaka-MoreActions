// Offline adapters only. The plugin itself is compiled against the original
// game's IL2CPP interop assemblies, not against these test types.
using System.Numerics;

public static class Plugin
{
    public const string Guid = "local.seleka2.vmdprobe";
    public static Setting<bool> UseCamera = new() { Value = true };
    public static TestLog LogSource = new();
}
public sealed class Setting<T> { public T Value; }
public sealed class TestLog
{
    public List<string> Messages = new();
    public void LogInfo(object s) => Messages.Add(s.ToString());
    public void LogWarning(object s) => Messages.Add(s.ToString());
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
}
namespace UnityEngine
{
    public class MonoBehaviour { public MonoBehaviour(IntPtr ptr) { } public bool isActiveAndEnabled = true; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion Euler(float x, float y, float z)
        {
            if (x != 0 || z != 0) throw new Exception("Camera anchor must be yaw-only");
            var q = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, y * MathF.PI / 180);
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }
    public sealed class Transform
    {
        public Vector3 position, eulerAngles;
        public Quaternion rotation = new(0, 0, 0, 1);
        public bool ThrowOnWrite;
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { if (ThrowOnWrite) throw new Exception("Destroyed transform fixture"); position = p; rotation = q; }
    }
    public sealed class Camera
    {
        public Transform transform = new(); public bool isActiveAndEnabled = true;
        public float fieldOfView = 85, orthographicSize = 3;
        public bool orthographic; public int cullingMask = 123;
    }
}
namespace ExposureUnnoticed2.Object3D.Camera
{
    public sealed class BaseCameraController
    {
        public UnityEngine.Camera Camera = new(); public bool IsLockCamera;
        public void SetTpsCullingMask() => Camera.cullingMask = 999;
    }
    public sealed class TPCameraController { }
    public sealed class FPCameraController { }
}
namespace ExposureUnnoticed2.Object3D.Player.Scripts
{
    public sealed class PlayerController
    {
        public static PlayerController Instance;
        public UnityEngine.Transform transform = new();
        public Accessor Pca = new();
    }
    public sealed class Accessor { public Locomotion PlayerLocomotion = new(); }
    public sealed class Locomotion
    {
        public ExposureUnnoticed2.Object3D.Camera.BaseCameraController baseCameraController = new();
        public ExposureUnnoticed2.Object3D.Camera.TPCameraController tpCameraController = new();
        public ExposureUnnoticed2.Object3D.Camera.FPCameraController fpCameraController = new();
    }
}
namespace Seleka2.Actions
{
    public static class Plugin { public static TestLog LogSource = new(); }
    public enum ActionBodyMask { FullBody, UpperBody, LowerBody }
    public sealed class ActionDefinition
    {
        public string Id { get; set; } = "";
        public string ProviderId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string AudioFile { get; set; } = "";
        public bool Loop { get; set; }
        public ActionEventDefinition[] Events { get; set; } = Array.Empty<ActionEventDefinition>();
        public ActionEventDefinition[] OnStart { get; set; } = Array.Empty<ActionEventDefinition>();
        public ActionEventDefinition[] OnFinish { get; set; } = Array.Empty<ActionEventDefinition>();
        public ActionEventDefinition[] OnCancel { get; set; } = Array.Empty<ActionEventDefinition>();
        public bool RequiresHands = true, RequiresStanding = true;
        public ActionBodyMask BodyMask;
        public string[] BonePaths = Array.Empty<string>();
    }
    public static class ActionRegistry
    {
        public static Dictionary<string, ActionDefinition> Entries = new();
        public static ActionDefinition Get(string id) => id != null && Entries.TryGetValue(id, out var def) ? def : null;
    }
    public sealed class ActionDriver { public static ActionDriver Current; public void Request(string id) { } public void Stop(string why) { } }
}
