#pragma warning disable CS9113 // Adapter constructor parameters mirror production signatures.
// Offline boundaries only: production driver, scheduler, native adapters and
// optional NPC registration are linked unchanged by EventValidation.csproj.
using Seleka2.Actions;
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin(string id, string name, string version) : Attribute {}
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public event EventHandler SettingChanged { add {} remove {} } public T Value; public ConfigEntry(T value) { Value = value; } }
    public sealed class ConfigFile { public ConfigEntry<T> Bind<T>(string section, string key, T value, string description) => new(value); }
}
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public List<string> Messages = new(); public void LogInfo(object text) => Messages.Add(text.ToString());
        public void LogWarning(object text) => Messages.Add(text.ToString()); public void LogError(object text) => Messages.Add(text.ToString());
    }
}
namespace BepInEx.Unity.IL2CPP
{
    public abstract class BasePlugin
    {
        public BepInEx.Configuration.ConfigFile Config = new(); public BepInEx.Logging.ManualLogSource Log = new();
        public abstract void Load(); public T AddComponent<T>() => (T)Activator.CreateInstance(typeof(T), IntPtr.Zero);
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch(Type owner, string method) : Attribute {}
    public sealed class Harmony(string id) { public void PatchAll(System.Reflection.Assembly assembly) {} }
}
namespace UnityEngine
{
    public class Object { public bool Destroyed; public static void Destroy(Object value) { value.Destroyed = true; } public static T FindObjectOfType<T>() => default; public T TryCast<T>() where T : class => this as T; }
    public class ScriptableObject : Object { public HideFlags hideFlags; public static T CreateInstance<T>() where T : ScriptableObject, new() => new(); }
    public enum HideFlags { HideAndDontSave }
    public sealed class GameObject : Object { public bool activeInHierarchy = true; public T AddComponent<T>() where T : new() => new(); }
    public class MonoBehaviour(IntPtr pointer) : Object { public GameObject gameObject = new(); }
    public sealed class Animator { public GameObject gameObject = new(); public bool enabled = true, isInitialized = true; }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z; public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public sealed class Transform { public Vector3 position; }
    public enum CollisionFlags { None, Sides }
    public sealed class CharacterController : Object
    {
        public bool enabled=true; public Transform transform;
        public Func<Vector3,Vector3> Resolve;public List<Vector3> Moves=new();
        public CollisionFlags Move(Vector3 delta)
        {
            Moves.Add(delta);var actual=Resolve==null?delta:Resolve(delta);var p=transform.position;
            transform.position=new Vector3(p.x+actual.x,p.y+actual.y,p.z+actual.z);return CollisionFlags.None;
        }
    }
    public static class Time { public static int frameCount; }
    public static class Mathf { public static float Min(float a, float b) => Math.Min(a,b); public static float Clamp01(float value) => Math.Clamp(value,0,1); }
    public static class AudioSettings { public static double dspTime; }
    public enum AudioDataLoadState { Loaded }
    public sealed class AudioClip : Object { public float length; public AudioDataLoadState loadState = AudioDataLoadState.Loaded; }
    public sealed class AudioMixerGroup {}
    public sealed class AudioMixer { public AudioMixerGroup[] FindMatchingGroups(string name) => Array.Empty<AudioMixerGroup>(); }
    public sealed class AudioSource : Object
    {
        public bool playOnAwake, loop; public float spatialBlend, pitch, volume; public AudioClip clip; public AudioMixerGroup outputAudioMixerGroup;
        public void Stop() {} public void PlayScheduled(double time) {}
    }
    public sealed class AnimationEvent { public Object objectReferenceParameter; public string functionName; public int intParameter; public float time; }
    public sealed class AnimationClip : Object { public AnimationEvent[] events = Array.Empty<AnimationEvent>(); public float length = 1; }
}
namespace ExposureUnnoticed2.Scripts.Base
{
    public static class Constant { public enum Direct { Forward, Back, Left, Right, ForwardLeft, ForwardRight, BackLeft, BackRight } }
    public static class GameState
    {
        public static object GameStateData = new(); public static Options OptionData = new();
        public sealed class Options { public int Language = 2; public float SoundMaster100 = 100, SoundBgm100 = 100; }
    }
    public static class GameStateFacade { public static bool Handcuffed; public static bool IsAttachSomeHandcuffs() => Handcuffed; }
    public static class InputManager
    {
        public enum InputType { Cancel, OpenClothesA, OpenClothesB, CloseClothesA, Crouch, Interact, DrinkWater, EyeMask, Move }
        public static HashSet<InputType> Down = new(); public static UnityEngine.Vector3 Move;
        public static bool IsDownFrame(InputType input) => Down.Contains(input); public static UnityEngine.Vector3 GetVector3(InputType input) => Move;
    }
    public static class InputResolver { public static ExposureUnnoticed2.ObjectUI.InGame.RingMenu.RingMenuParentView.RingItemType RingShortcutInput; }
}
namespace ExposureUnnoticed2.Scripts.InGame
{
    public sealed class EventMethodManager { public void LateUpdate() {} }
    public sealed class UiOptionApplyer { public UnityEngine.AudioMixer audioMixer; }
    public sealed class SexManager { public static SexManager Instance; public bool IsSexMode; }
}
namespace ExposureUnnoticed2.Scripts.Sound { public static class InGameSoundManager { public enum SeType { FootStepHeel=1, FootStepSneaker=31, Shiofuki=8 } } }
namespace ExposureUnnoticed2.Object3D.IngameManager
{
    public sealed class InGameManager { public static InGameManager Instance = new(); public Temp TempInGameState = new(); public sealed class Temp { public bool IsGameOver, IsPlayingSomePerformance, IsTimeStop; } }
    public sealed class InGameUiManager { public static InGameUiManager Instance = new(); public bool IsOpenSomeUi; }
}
namespace ExposureUnnoticed2.Object3D.Camera { public sealed class FaceCameraController {} }
namespace ExposureUnnoticed2.ObjectUI.InGame.RingMenu
{
    public sealed class RingMenuParentView { public enum RingItemType { None } public static RingMenuParentView Instance = new(); public bool IsOpenRing; }
}
namespace ExposureUnnoticed2.ObjectUI.InGame.MessagePanel
{
    public static class MessagePanelView { public enum MessageType { Log } public static List<string> Messages = new(); public static void AddMessage(string message, MessageType type) => Messages.Add(message); }
}
namespace ExposureUnnoticed2.Object3D.Player.ScriptableObject
{
    public sealed class MFootAnimationEventRecord : UnityEngine.ScriptableObject
    {
        public ExposureUnnoticed2.Scripts.Sound.InGameSoundManager.SeType SeType; public ExposureUnnoticed2.Scripts.Base.Constant.Direct Direct; public bool IsMoveAction;
    }
}
namespace ExposureUnnoticed2.Object3D.Player.Scripts
{
    public sealed class PlayerController
    {
        public static PlayerController Instance; public UnityEngine.GameObject gameObject = new(); public UnityEngine.Transform transform = new(); public PlayerClassAccessor Pca = new();
        public PlayerController() { Pca.CharacterController.transform=transform; }
    }
    public sealed class PlayerClassAccessor
    {
        public PlayerStateModel PlayerState = new(); public UnityEngine.Animator Animator = new(); public PlayerAction PlayerAction = new(); public UnityEngine.CharacterController CharacterController=new();
        public PlayerSoundManager PlayerSoundManager = new(); public PlayerAnimationReceiver PlayerAnimationReceiver = new();
    }
    public sealed class PlayerStateModel
    {
        public enum PlayerState { Idle, Busy } public IntPtr Pointer = (IntPtr)1; public PlayerState CurrentState;
        public bool IsEcstasyMotion, IsCrouch, IsSit; public ActionRecord CurrentAction = new();
        public sealed class ActionRecord { public int Type = -1; }
    }
    public sealed class PlayerAction { public IntPtr Pointer; }
    public sealed class PlayerFacade { public static PlayerFacade Instance = new(); public PlayerClassAccessor pca; public bool CanUseHand(bool a, bool b, bool c, bool d, bool e) => true; }
    public sealed class PlayerSoundManager
    {
        public List<ExposureUnnoticed2.Scripts.Sound.InGameSoundManager.SeType> Calls = new(); public Action Callback;
        public void PlaySe(ExposureUnnoticed2.Scripts.Sound.InGameSoundManager.SeType value) { Calls.Add(value); Callback?.Invoke(); }
    }
    public sealed class PlayerAnimationReceiver
    {
        public List<ExposureUnnoticed2.Scripts.Sound.InGameSoundManager.SeType> Sounds = new();
        public List<(string side, ExposureUnnoticed2.Object3D.Player.ScriptableObject.MFootAnimationEventRecord record)> Feet = new();
        public void OnPlaySe(ExposureUnnoticed2.Scripts.Sound.InGameSoundManager.SeType value) => Sounds.Add(value);
        public void FootL(UnityEngine.AnimationEvent value) => Feet.Add(("Left",(ExposureUnnoticed2.Object3D.Player.ScriptableObject.MFootAnimationEventRecord)value.objectReferenceParameter));
        public void FootR(UnityEngine.AnimationEvent value) => Feet.Add(("Right",(ExposureUnnoticed2.Object3D.Player.ScriptableObject.MFootAnimationEventRecord)value.objectReferenceParameter));
    }
}
namespace Seleka2.ModSettings
{
    public static class ModOptions
    {
        public static Action<string> Log; public static void Toggle(string a,string b,string c,BepInEx.Configuration.ConfigEntry<bool> d,string e,string f) {}
        public static void Number(string a,string b,string c,BepInEx.Configuration.ConfigEntry<float> d,int e,int f,float g,string h,string i,string j) {}
    }
}
namespace Seleka2.Actions
{
    public static class ActionFaceSettings { public static bool KeepGameExpressions {get;set;}=true; }
    public static class RingActionMenu { public static BepInEx.Configuration.ConfigEntry<string> LastActions; }
    internal sealed class MotionFaceCamera { public void Begin(ExposureUnnoticed2.Object3D.Player.Scripts.PlayerController owner) {} public void RefreshAfterPose() {} public void End() {} public bool ShouldDefer(ExposureUnnoticed2.Object3D.Camera.FaceCameraController camera) => true; }
    internal sealed class LoadedAction
    {
        public ActionDefinition Definition; public UnityEngine.AudioClip Audio; public UnityEngine.AnimationClip Clip; public float Duration = 1;
        public TestPose LastPose;public IActionPose NextPose;public IActionPose CreatePose(UnityEngine.GameObject target) => NextPose ?? (LastPose = new TestPose());
    }
    internal sealed class TestPose : IActionPose
    {
        public int BoneCount => 1; public List<float> Samples = new(); public bool Disposed, ThrowOnDispose;
        public bool Apply(float seconds) { Samples.Add(seconds); return true; }
        public void Dispose() { Disposed = true; if (ThrowOnDispose) throw new Exception("fixture pose cleanup failure"); }
    }
    internal static class ActionAssets
    {
        public static Dictionary<string, LoadedAction> Items = new(); public static HashSet<string> Pending = new(); public static bool TryGet(ActionDefinition definition, out LoadedAction loaded) { loaded = Pending.Contains(definition.Id) ? null : Items[definition.Id]; return loaded != null; } public static void Tick() {} public static void Clear() {}
    }
}
