// Boundary doubles for offline lifecycle tests. These do not emulate native Unity
// navigation, IL2CPP patch dispatch, animations, or visual gameplay correctness.
using ExposureUnnoticed2.Object3D.NPC.Script;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using UnityEngine.AI;

public static class Plugin { public static TestLog LogSource = new(); }
public sealed class TestLog
{
    public readonly List<string> Lines = new();
    public void LogInfo(object value) => Lines.Add(value.ToString());
    public void LogWarning(object value) => Lines.Add(value.ToString());
    public void LogError(object value) => Lines.Add(value.ToString());
}
namespace Seleka2.NpcSpectators
{
    internal sealed class Entry<T>(T value) { internal T Value = value; }
    internal static class Plugin
    {
        internal static TestLog Logger = new();
        internal static Entry<bool> Enabled = new(true), FollowMmd = new(true), FollowNative = new(true), FollowAllMmd = new(false);
        internal static Entry<string> NativeActions = new("HipShake"), MmdActions = new("wavefile");
        internal static Entry<float> Radius = new(2.8f), Spacing = new(1.1f);
    }
}
namespace HarmonyLib { [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { _=type;_=method; } } }
namespace UnityEngine
{
    public class MonoBehaviour { public MonoBehaviour(IntPtr pointer) { _=pointer; } }
    public class GameObject { public bool activeInHierarchy = true; }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public float sqrMagnitude => x*x+y*y+z*z;
        public Vector3 normalized => this / MathF.Sqrt(sqrMagnitude);
        public static Vector3 zero => new(0,0,0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a, float b) => a*(1/b);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public struct Quaternion { public Vector3 Facing; public static Quaternion LookRotation(Vector3 v)=>new(){Facing=v.normalized}; public static Quaternion RotateTowards(Quaternion a,Quaternion b,float angle)=>b; }
    public sealed class Transform { public Vector3 position; public Quaternion rotation=Quaternion.LookRotation(new(0,0,1)); public Vector3 forward=>rotation.Facing; }
    public static class Mathf { public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b); public static float Clamp01(float x)=>Clamp(x,0,1); public static float Min(float a,float b)=>MathF.Min(a,b); public static float Max(float a,float b)=>MathF.Max(a,b); public static float Sqrt(float v)=>MathF.Sqrt(v); }
    public static class Time { public static int frameCount; public static float unscaledTime, fixedDeltaTime=0.02f, deltaTime=0.02f; }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial, PathInvalid }
    public sealed class NavMeshPath { public NavMeshPathStatus status; public Vector3 Destination; }
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh
    {
        public static bool Reachable = true;
        public static float FloorOffset;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float distance,int areaMask) { p.y+=FloorOffset;hit=new(){position=p}; return Reachable; }
    }
    public sealed class NavMeshAgent
    {
        public bool updatePosition=true, updateRotation=true, isStopped, enabled=true, isOnNavMesh=true, hasPath=true, pathPending;
        public int areaMask=-1, DestinationWrites;
        public float baseOffset;
        public Vector3 destination=new(0,0,20), nextPosition, steeringTarget;
        public NavMeshPathStatus pathStatus=NavMeshPathStatus.PathComplete, CalculateStatus=NavMeshPathStatus.PathComplete;
        public bool CalculatePath(Vector3 point, NavMeshPath path) { path.status=CalculateStatus;path.Destination=point;return true; }
        public bool SetPath(NavMeshPath path) { destination=path.Destination;steeringTarget=destination;hasPath=true;pathStatus=path.status;return true; }
        public bool SetDestination(Vector3 point) { destination=point;hasPath=true;DestinationWrites++;return true; }
        public void ResetPath() { hasPath=false; }
        public bool Raycast(Vector3 next,out NavMeshHit hit) { hit=new(){position=next};return false; }
    }
}
namespace ExposureUnnoticed2.Object3D.NPC.Script
{
    public sealed class NpcStateModel
    {
        public enum State { Idle, FoundStranger, TurnAround, FadeOut, Stagger, Interact, WaitToken, TraceSexPlayer }
        public enum FixedType { None, Car, Talk, SitWork, SitTalk, SitSleep, SitBook, SitSumaho, StandSumaho, Pinpon, Conbini }
        public State CurrentState; public FixedType CurrentFixedType;
        public bool IsPolice, IsDestroy, IsSit, HasInteractToken;
        public object RouteData=new(); public float MoveSpeedScale=1; public NpcController NearNpc;
    }
    public sealed class Smooth { public float Target=1, Current=1; }
    public sealed class NpcAnimationManager { public Smooth speed=new(); public void SetSpeed(float v)=>speed.Target=v;public float GetCurrentMoveSpeed()=>speed.Current; }
    public sealed class NpcComponentAccessor { public NpcController NpcController;public NpcStateModel StateModel=new();public NavMeshAgent NavAgent=new();public NpcLocomotion NpcLocomotion;public NpcAnimationManager NpcAnimationManager=new(); }
    public sealed class NpcLocomotion { public NpcComponentAccessor nca; public Vector3 moveV,lastSetDestinationPoint;public Quaternion targetRotation;public float lastCalcNearNpcTime;public int skipFixedUpdateFrame;public void SetMoveZero()=>moveV=Vector3.zero; }
    public sealed class NpcController
    {
        public IntPtr Pointer; public int id; public bool IsAlive=true;public GameObject GameObject=new();public Transform Transform=new(); public NpcComponentAccessor Nca=new();
        public NpcController(int key) { id=key;Pointer=new(key);Nca.NpcController=this;Nca.NpcLocomotion=new(){nca=Nca}; }
    }
    public sealed class NpcManager { public static NpcManager Instance;public bool initialized=true;public List<NpcController> ExistNpcList=new();public NpcController GetNearOtherNpc(NpcController n)=>null; }
}
namespace ExposureUnnoticed2.Master.Common { public static class MCommon { public static float PlayerWalkSpeed=2; } }
namespace ExposureUnnoticed2.Object3D.Player.Scripts
{
    public enum ActionType { None=-1, HipShake=50002, Dogeza=50005 }
    public sealed class ActionRecord { public ActionType Type=ActionType.None; }
    public sealed class PlayerState { public int CurrentState;public ActionRecord CurrentAction=new(); }
    public sealed class PlayerAccessor { public PlayerState PlayerState=new(); }
    public sealed class PlayerController { public static PlayerController Instance;public GameObject gameObject=new();public Transform transform=new();public PlayerAccessor Pca=new(); }
}
namespace ExposureUnnoticed2.Scripts.InGame
{
    public sealed class TempState { public bool IsTimeStop,IsGameOver,IsPlayingSomePerformance; }
    public sealed class InGameManager { public static InGameManager Instance;public TempState TempInGameState=new(); }
}
namespace ExposureUnnoticed2.Object3D.IngameManager { public sealed class InGameUiManager { public static InGameUiManager Instance;public bool IsOpenFade,IsOpenSomeUi; } }
namespace ExposureUnnoticed2.Scripts.Base
{
    public static class InputManager { public enum InputType { Move,Cancel } public static Vector3 Move;public static bool Cancel;public static Vector3 GetVector3(InputType t)=>Move;public static bool IsDownFrame(InputType t)=>Cancel; }
}
