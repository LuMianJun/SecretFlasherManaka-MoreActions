using SFM.Actions;
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch(Type type,string name):Attribute {public Type Type=type;public string Name=name;}
}
namespace UnityEngine
{
    public static class Time {public static float deltaTime=.1f;public static int frameCount;}
    public static class Mathf {public static float Clamp01(float value)=>Math.Clamp(value,0,1);}
}
namespace ExposureUnnoticed2.Master.Action
{
    public enum ActionType {None=-1,HipShake,OnaniNormal,UseDildoWallPussy1,UseDildoWallFella1,UseDildoWallWaitPussy,SitDildo,SexStandBack,PeeNormal}
    public sealed class MActionRecord {public float AddEcstasy,AddDokidoki100,Strange,StrangenessScale=1,Discomfort;}
    public static class MAction
    {
        public static Dictionary<ActionType,MActionRecord> Records=new();
        public static MActionRecord Get(ActionType type)=>Records.GetValueOrDefault(type);
    }
}
namespace ExposureUnnoticed2.Master.Mission {public enum ExposedBodyParts {None=1,Front=2}}
namespace ExposureUnnoticed2.Scripts.Base {}
namespace ExposureUnnoticed2.Scripts.InGame
{
    public static class GameState
    {
        public static Data GameStateData=new();public static Options OptionData=new();
        public sealed class Data {public int MotionSpeedScaleIndex;public float PlayerMoisture,PlayerAddMoisture;}
        public sealed class Options {public bool PeeDisable;}
    }
}
namespace ExposureUnnoticed2.Scripts.InGame.MissionArea
{
    public sealed class NpcRouteAreaManager {public static NpcRouteAreaManager Instance;public bool IsInArea;}
}
namespace ExposureUnnoticed2.Scripts.Mission {public sealed class MissionPoseExpose {}}
namespace ExposureUnnoticed2.Object3D.Player.Scripts
{
    public sealed class PlayerStateModel {public float AddEcstasyByPiston,Dokidoki;public Rank CurrentRank=new();public bool IsPeeing;public sealed class Rank {public float DokidokiBoostScale=1;}}
    public sealed class PlayerClassAccessor {public PlayerStateModel PlayerState=new();public StaminaManager Stamina=new();}
    public sealed class PlayerController {public PlayerClassAccessor Pca=new();}
    public sealed class PlayerMiscProcess {public PlayerClassAccessor pca;public PlayerStateModel playerState=>pca.PlayerState;}
    public sealed class PlayerEcstasyController {public PlayerClassAccessor pca;}
    public sealed class PlayerMoistureManager {public PlayerClassAccessor pca;}
    public sealed class PlayerPeeController {public PlayerClassAccessor pca;public int Starts;public void StartPee(){Starts++;pca.PlayerState.IsPeeing=true;}}
    public sealed class PlayerStrangenessController {public PlayerClassAccessor pca;}
    public sealed class StaminaManager
    {
        static int next;public int Pointer=++next;public float Current=1;public int Calls;public float Rate;
        public void ConsumePerSecond(float rate){Calls++;Rate=rate;Current=Math.Max(0,Current-rate*UnityEngine.Time.deltaTime);}
    }
    public sealed class PlayerFacade
    {
        public static PlayerFacade Instance=new();public ExposureUnnoticed2.Master.Mission.ExposedBodyParts Exposed=ExposureUnnoticed2.Master.Mission.ExposedBodyParts.None;
        public void AddAddMoisture(float amount)=>ExposureUnnoticed2.Scripts.InGame.GameState.GameStateData.PlayerAddMoisture+=amount;
        public ExposureUnnoticed2.Master.Mission.ExposedBodyParts GetExposedBodyPartsByClothesAndCostume()=>Exposed;
    }
}
namespace SFM.Actions
{
    public sealed class ActionDefinition
    {
        public string NativeKind {get;set;}="None";public ActionStatsDefinition Stats {get;set;}=new();public bool PoseMission {get;set;}
    }
    // Native adapter boundary only. The real driver's clock, ownership and
    // cancellation guards are covered by EventValidation.
    public sealed class ActionDriver
    {
        public static ActionDriver Current;public ExposureUnnoticed2.Object3D.Player.Scripts.PlayerController Owner=new();
        public ActionDefinition Definition=new();internal ActionStatValues Values;public bool Enabled=true;public string StopReason;
        readonly ActionStatFrames frames=new();
        internal bool TryGetStats(ExposureUnnoticed2.Object3D.Player.Scripts.PlayerStateModel state,out ActionStatValues values){values=Values;return Enabled&&state==Owner.Pca.PlayerState;}
        internal bool TakeStatFrame(ActionStatChannel channel)=>frames.Take(channel,UnityEngine.Time.frameCount);
        internal void StopWithDiagnostics(string stage,string reason){StopReason=reason;Enabled=false;}
    }
}
