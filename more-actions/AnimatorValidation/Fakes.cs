// Offline boundaries, not a Unity retargeting/physics simulator.
namespace UnityEngine
{
    public class Object
    {
        static int next;public IntPtr Pointer=new(++next);public HideFlags hideFlags;public bool destroyed;
        public static Object LastInstantiated;
        public static void Destroy(Object value)=>value.destroyed=true; public T TryCast<T>() where T:class=>this as T;
        public static T Instantiate<T>(T value) where T:Object=>value is AnimatorOverrideController?(T)(LastInstantiated=new AnimatorOverrideController()):throw new NotSupportedException();
    }
    [Flags] public enum HideFlags { None=0, DontUnloadUnusedAsset=32, HideAndDontSave=61 }
    public struct Vector3 { public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 zero=>default; }
    public struct Quaternion { public static Quaternion identity=>default; }
    public sealed class Transform
    {
        public Vector3 position;public Quaternion rotation;public GameObject gameObject;
        public Dictionary<string,Transform> Children=new();public Transform Find(string path)=>Children.GetValueOrDefault(path);
        public T GetComponent<T>() where T:class=>gameObject.GetComponent<T>();
    }
    public sealed class GameObject
    {
        public Transform transform;public bool active=true;public bool activeInHierarchy=>active;public Animator animator;public SkinnedMeshRenderer renderer;
        public GameObject(){transform=new(){gameObject=this};}
        public void SetActive(bool value)=>active=value;public T GetComponent<T>() where T:class=>typeof(T)==typeof(SkinnedMeshRenderer)?renderer as T:animator as T;
        public T AddComponent<T>() where T:new(){var value=new T();if(value is Animator a)animator=a;return value;}
    }
    public sealed class Avatar { public bool isValid=true; }
    public enum AnimatorCullingMode { AlwaysAnimate,CullUpdateTransforms,CullCompletely }
    public sealed class Animator
    {
        public Avatar avatar;RuntimeAnimatorController controller;public int ControllerAssignments;
        public RuntimeAnimatorController runtimeAnimatorController {get=>controller;set{if(!ReferenceEquals(controller,value))ControllerAssignments++;controller=value;}}
        public bool enabled=true,isInitialized=true,applyRootMotion,fireEvents=true;public AnimatorCullingMode cullingMode;
        public GameObject gameObject=new();public Transform transform=>gameObject.transform;public int UpdateCalls,PlayCalls;public Action OnEvaluate;public bool ThrowEvaluation;
        public int layerCount=2;public bool Transition;public AnimatorStateInfo[] States={new(){fullPathHash=10,normalizedTime=.4f},new(){fullPathHash=20,normalizedTime=.8f}};
        public int GetLayerIndex(string name)=>name=="Action"?1:-1;
        public float[] Weights={1,.5f};public bool IsInTransition(int index)=>Transition;public AnimatorStateInfo GetCurrentAnimatorStateInfo(int index)=>States[index];
        public float GetLayerWeight(int index)=>Weights[index];public void SetLayerWeight(int index,float value)=>Weights[index]=value;
        public void Play(int hash,int index,float time){PlayCalls++;if(!float.IsFinite(time))throw new Exception("Cannot replay nonfinite state time");States[index]=new(){fullPathHash=hash,normalizedTime=time};}
        public void Update(float delta){if(delta!=0)throw new Exception("Refresh must not advance game time");UpdateCalls++;OnEvaluate?.Invoke();if(ThrowEvaluation)throw new Exception("Native evaluation failed");}
    }
    public struct AnimatorStateInfo { public int fullPathHash;public float normalizedTime;public bool NotIdle;public bool IsName(string name)=>name=="Action.NoMotion"&&!NotIdle; }
    public static class Time { public static float unscaledTime; }
    public sealed class AnimationClip:Object { public string name;float seconds=2;public float length {get {if(destroyed)throw new InvalidOperationException("Native clip was unloaded");return seconds;}set=>seconds=value;}public bool isHumanMotion; }
    public sealed class AudioClip : Object { }
    public sealed class Mesh:Object {public int blendShapeCount;}
    public sealed class SkinnedMeshRenderer:Object
    {
        public Mesh sharedMesh;public float[] Values;public float GetBlendShapeWeight(int index)=>Values[index];public void SetBlendShapeWeight(int index,float value)=>Values[index]=value;
    }
    public class RuntimeAnimatorController:Object {public AnimationClip[] Originals=Array.Empty<AnimationClip>();public virtual AnimationClip[] animationClips=>Originals;}
    public sealed class AnimatorOverrideController:RuntimeAnimatorController
    {
        public AnimatorOverrideController(){}
        public AnimatorOverrideController(System.IntPtr pointer){Pointer=pointer;LastInstantiated=this;}
        public AnimatorOverrideController(RuntimeAnimatorController controller){runtimeAnimatorController=controller;}
        public static void Internal_Create(AnimatorOverrideController self,RuntimeAnimatorController controller){self.runtimeAnimatorController=controller;}
        public RuntimeAnimatorController runtimeAnimatorController;public Dictionary<AnimationClip,AnimationClip> Overrides=new();public int Notifications;
        AnimationClip[] BaseClips=>runtimeAnimatorController is AnimatorOverrideController wrapper?wrapper.BaseClips:runtimeAnimatorController?.Originals??Originals;
        public override AnimationClip[] animationClips=>BaseClips;public int overridesCount=>BaseClips.Length;public AnimationClip GetOriginalClip(int i)=>BaseClips[i];
        public void SetClip(AnimationClip original,AnimationClip clip,bool notify){Overrides[original]=clip;if(notify)Notifications++;}
        public AnimationClip GetClip(AnimationClip original,bool effective)
        {
            if(Overrides.TryGetValue(original,out var clip)&&clip!=null)return clip;
            return runtimeAnimatorController is AnimatorOverrideController parent?parent.GetClip(original,effective):original;
        }
        public void SendNotification()=>Notifications++;
    }
    public sealed class AssetBundleCreateRequest { public bool isDone=true;public AssetBundle assetBundle; }
    public sealed class AssetBundleRequest { public bool isDone=true;public Object asset; }
    public sealed class AssetBundle
    {
        public static Dictionary<string,AssetBundle> Files=new();public Dictionary<string,AnimationClip> Clips=new();public bool Unloaded;
        public static AssetBundleCreateRequest LoadFromFileAsync(string path)=>new(){assetBundle=Files.GetValueOrDefault(path)};
        public string[] GetAllAssetNames()=>Clips.Keys.Append("template.overridecontroller").ToArray();
        public AssetBundleRequest LoadAssetAsync<T>(string name)=>new(){asset=name.EndsWith(".overridecontroller")?new AnimatorOverrideController():Clips.GetValueOrDefault(name)};
        public void Unload(bool all)=>Unloaded=true;
        public static void SceneCleanup(){foreach(var clip in Files.Values.SelectMany(b=>b.Clips.Values))if((clip.hideFlags&HideFlags.DontUnloadUnusedAsset)==0)Object.Destroy(clip);}
    }
}
namespace UnityEngine.Playables
{
    public enum DirectorUpdateMode { Manual }
    public sealed class GraphState { public bool Valid=true,Manual,Playing;public int Evaluations;public double Time;public UnityEngine.Animator Target; }
    public struct PlayableHandle { public GraphState State;public static void SetDuration_Injected(ref PlayableHandle handle,double value){}public static void SetTime_Injected(ref PlayableHandle handle,double value)=>handle.State.Time=value;public static void SetDone_Injected(ref PlayableHandle handle,bool value){} }
    public struct PlayableOutputHandle { public GraphState State;public static void SetSourcePlayable_Injected(ref PlayableOutputHandle handle,ref PlayableHandle target,int port){}public static void SetWeight_Injected(ref PlayableOutputHandle handle,float value){} }
    public struct PlayableGraph
    {
        public GraphState State;public static GraphState Last;public static bool FailEvaluation;
        public static void Create_Injected(string name,out PlayableGraph graph)=>graph=new(){State=Last=new()};
        public static void SetTimeUpdateMode_Injected(ref PlayableGraph graph,DirectorUpdateMode mode)=>graph.State.Manual=mode==DirectorUpdateMode.Manual;
        public static void Play_Injected(ref PlayableGraph graph)=>graph.State.Playing=true;
        public static bool IsValid_Injected(ref PlayableGraph graph)=>graph.State?.Valid==true;
        public static void Destroy_Injected(ref PlayableGraph graph)=>graph.State.Valid=false;
        public static void Evaluate_Injected(ref PlayableGraph graph,float delta){if(FailEvaluation)throw new Exception("native evaluation failed");graph.State.Evaluations++;graph.State.Time+=delta;}
    }
}
namespace UnityEngine.Animations
{
    public struct AnimationClipPlayable
    {
        public UnityEngine.Playables.PlayableHandle m_Handle;
        public static AnimationClipPlayable Create(UnityEngine.Playables.PlayableGraph graph,UnityEngine.AnimationClip clip)=>throw new MissingMethodException("Stripped AnimationClipPlayable constructor");
        public static bool FailCreation;
        public static bool CreateHandleInternal_Injected(ref UnityEngine.Playables.PlayableGraph graph,UnityEngine.AnimationClip clip,ref UnityEngine.Playables.PlayableHandle handle){if(FailCreation)return false;handle=new(){State=graph.State};return true;}
        public static void SetApplyFootIKInternal(ref UnityEngine.Playables.PlayableHandle handle,bool value){}
        public static void SetApplyPlayableIKInternal(ref UnityEngine.Playables.PlayableHandle handle,bool value){}
        public static void SetOverrideLoopTimeInternal(ref UnityEngine.Playables.PlayableHandle handle,bool value){}
        public static void SetLoopTimeInternal(ref UnityEngine.Playables.PlayableHandle handle,bool value){}
        public void SetApplyFootIK(bool value){}public void SetApplyPlayableIK(bool value){}public void SetOverrideLoopTime(bool value){}public void SetLoopTime(bool value){}
    }
    public struct AnimationPlayableOutput
    {
        public UnityEngine.Playables.PlayableOutputHandle m_Handle;
        public static AnimationPlayableOutput Create(UnityEngine.Playables.PlayableGraph graph,string name,UnityEngine.Animator target)=>throw new MissingMethodException("Do not use restored output helpers");
        public static void InternalSetTarget(ref UnityEngine.Playables.PlayableOutputHandle output,UnityEngine.Animator target)=>output.State.Target=target;
    }
    public static class AnimationPlayableGraphExtensions
    {
        public static bool FailCreation;
        public static bool InternalCreateAnimationOutput(ref UnityEngine.Playables.PlayableGraph graph,string name,out UnityEngine.Playables.PlayableOutputHandle output){output=new(){State=graph.State};return !FailCreation;}
    }
}
namespace SFM.Actions
{
    internal static class CameraTransitionDiagnostics {internal static void ReplacementChange(UnityEngine.Animator animator,string phase){} }
    public sealed class ActionDefinition { public string Id,DisplayName,Description,ProviderId;public bool Loop,ShowInRing=true;public string Bundle,BundlePath,ClipAsset;public IActionSource RuntimeSource; }
    public static class ActionRegistry
    {
        public static int Revision;
        public static ActionDefinition Get(string id)=>Entries.TryGetValue(id,out var entry)?entry.Definition:null;
        public static IEnumerable<ActionDefinition> GetAll()=>Entries.Values.Select(e=>e.Definition);
        public static void SetRingVisibility(string id,bool visible){if(Entries.TryGetValue(id,out var value))value.Definition.ShowInRing=visible;}
        public static Dictionary<string,(ActionDefinition Definition,IActionSource Source,string Provider)> Entries=new();
        public static void RegisterRuntime(ActionDefinition definition,IActionSource source,string directory,string provider){definition.RuntimeSource=source;definition.ProviderId=provider;Entries.Add(definition.Id,(definition,source,provider));Revision++;}
        public static void Register(ActionDefinition definition,string directory,string provider){Entries.Add(definition.Id,(definition,null,provider));Revision++;}
        public static void UnregisterProvider(string provider){foreach(var id in Entries.Where(e=>e.Value.Provider==provider).Select(e=>e.Key).ToArray())Entries.Remove(id);Revision++;}
    }
    internal static class ActionAssets
    {
        public static int ReadyRevision=0;
        internal static Dictionary<string,LoadedAction> Loaded=new();
        public static bool TryGet(ActionDefinition definition,out LoadedAction loaded)=>Loaded.TryGetValue(definition.Id,out loaded);
    }
    internal sealed class ActionPose: IActionPose { internal bool Native;internal ActionPose(UnityEngine.GameObject target,LoadedAction action,bool native=false){Native=native;}public int BoneCount=>1;public bool Apply(float seconds)=>true;public void Dispose(){} }
    public static class Plugin { public static Logger LogSource=new(); }
    public sealed class Logger { public List<string> Lines=new();public void LogInfo(string text)=>Lines.Add(text);public void LogWarning(string text)=>Lines.Add(text);public void LogError(string text)=>Lines.Add(text); }
    public static class ActionPlaybackApi {public static bool IsPlaying;}
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        T value;public ConfigEntry(T value)=>this.value=value;public event EventHandler SettingChanged;
        public T Value{get=>value;set{if(EqualityComparer<T>.Default.Equals(this.value,value))return;this.value=value;SettingChanged?.Invoke(this,EventArgs.Empty);}}
    }
    public sealed class ConfigFile {public Dictionary<string,object> Values=new();public ConfigEntry<T> Bind<T>(string section,string key,T value,string description){string id=section+"."+key;if(!Values.TryGetValue(id,out var entry))Values[id]=entry=new ConfigEntry<T>(value);return (ConfigEntry<T>)entry;}}
}
namespace ExposureUnnoticed2.Scripts.Base
{
    public static class Localizer {public static string Trans(string key)=>"L"+(int)ExposureUnnoticed2.Scripts.InGame.GameState.OptionData.Language+":"+key;}
}
namespace ExposureUnnoticed2.Scripts.InGame
{
    public static class GameState {public static Options OptionData=new();}
    public sealed class Options {public int Language;}
}
namespace ExposureUnnoticed2.Object3D.IngameManager
{
    public sealed class InGameUiManager {public static InGameUiManager Instance=new();public bool IsOpenSomeUi;}
}
namespace ExposureUnnoticed2.Object3D.Player.Scripts
{
    public sealed class PlayerController { public static PlayerController Instance=new(); public PlayerClassAccessor Pca=new(); }
    public sealed class PlayerClassAccessor { public PlayerStateModel PlayerState=new(); }
    public sealed class PlayerStateModel { public bool IsCrouch,IsSit,IsEcstasyMotion;public int CurrentState;public NativeAction CurrentAction=new(); }
    public sealed class NativeAction {public int Type=-1;}
}
namespace SFM.ModSettings
{
    public static class ModOptions
    {
        public sealed record Row(Func<string> Title,Func<bool> Read,Action<bool> Write,Func<string> Help);
        public static Dictionary<string,Row> Rows=new();public static int Revision;public static bool Dedicated;public static Func<string> PageTitle;
        public static void Refresh()=>Revision++;
        public static void Header(string id,string group,Func<string> title){Rows[id]=new(title,null,null,null);Revision++;}
        public static void Remove(string prefix){foreach(var key in Rows.Keys.Where(k=>k.StartsWith(prefix,StringComparison.Ordinal)).ToArray())Rows.Remove(key);Revision++;}
        public static void DynamicToggle(string id,string group,string order,Func<string> title,Func<bool> read,Action<bool> write,Func<string> help){Rows[id]=new(title,read,write,help);Revision++;}
        public static void DynamicNumber(string id,string group,Func<string> title,BepInEx.Configuration.ConfigEntry<float> value,int min,int max,float unit,string suffix,Func<string> help){}
    }
}

namespace SFM.Actions
{
    // Camera marker sampling is exercised in-game; the offline boundary only
    // needs the release hook used by the replacement lifecycle.
    internal static class NativeCameraTrack { internal static void Release(){} internal static string State=>""; }
}
namespace Il2CppInterop.Runtime
{
    public static class IL2CPP { static int next; public static IntPtr il2cpp_object_new(IntPtr klass)=>new(900000+ ++next); }
    public static class Il2CppClassPointerStore<T> { public static IntPtr NativeClassPtr=new(1); }
}
