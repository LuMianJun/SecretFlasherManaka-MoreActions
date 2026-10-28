using System.Reflection;
using Seleka2.NpcSpectators;
using Seleka2.Motion;
using ExposureUnnoticed2.Object3D.NPC.Script;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Base;
using UnityEngine;
using UnityEngine.AI;

if(args.Length>0&&args[0]=="--installed-api")
{
    InstalledContract.Verify(args[1],args[2],args[3]);return;
}

static void Check(bool condition, string message) { if(!condition)throw new Exception(message); }
static bool Same(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-6f;
static void Pass(string message)=>Console.WriteLine("PASS: "+message);

// All current ordinary eligibility inputs and each individual exclusion.
Check(CrowdPolicy.IsEligible(true,true,false,false,0,0,false,false,true,true),"ordinary NPC excluded");
Check(!CrowdPolicy.IsEligible(true,true,true,false,0,0,false,false,true,true),"police accepted");
Check(!CrowdPolicy.IsEligible(true,true,false,true,0,0,false,false,true,true),"destroyed accepted");
for(int state=1;state<=7;state++)Check(!CrowdPolicy.IsEligible(true,true,false,false,0,state,false,false,true,true),"busy accepted");
for(int type=1;type<=10;type++)Check(!CrowdPolicy.IsEligible(true,true,false,false,type,0,false,false,true,true),"fixed accepted");
Check(!CrowdPolicy.IsEligible(false,true,false,false,0,0,false,false,true,true),"dead accepted");
Check(!CrowdPolicy.IsEligible(true,false,false,false,0,0,false,false,true,true),"inactive accepted");
Check(!CrowdPolicy.IsEligible(true,true,false,false,0,0,true,false,true,true),"sitting accepted");
Check(!CrowdPolicy.IsEligible(true,true,false,false,0,0,false,true,true,true),"token accepted");
Check(!CrowdPolicy.IsEligible(true,true,false,false,0,0,false,false,false,true),"no route accepted");
Check(!CrowdPolicy.IsEligible(true,true,false,false,0,0,false,false,true,false),"no nav accepted");
Check(CrowdPolicy.Matches(" Dogeza, HIPshake ","HipShake"),"native name matching");
Check(CrowdPolicy.Matches("50002","50002"),"numeric action matching");
Check(!CrowdPolicy.Matches("None,-1","None")&&!CrowdPolicy.Matches("None,-1","-1"),"idle must not trigger");
Pass("ordinary NPC exclusions and explicit name/ID triggers");

foreach(float radius in new[]{1.8f,2.8f,8f})foreach(float spacing in new[]{0.8f,1.1f,3f})
{
    var slots=CrowdSlots.Create(240,radius,spacing);
    Check(slots.Count==240&&slots.Select(s=>s.Index).Distinct().Count()==240,"slot IDs");
    for(int i=0;i<slots.Count;i++)for(int j=i+1;j<slots.Count;j++)
    {
        float dx=slots[i].X-slots[j].X,dz=slots[i].Z-slots[j].Z;
        Check(dx*dx+dz*dz>=spacing*spacing-0.0001f,"overlapping theoretical destinations");
    }
}
Pass("240 unique destinations with minimum spacing across radius/spacing extremes");

var missing=new MmdBridge(null,()=>null);missing.Refresh();Check(!missing.Connected&&missing.MotionId=="","MMD required");missing.Dispose();
MotionPlaybackApi.Publish("wavefile",true);
using(var bridge=new MmdBridge(null))
{
    bridge.Refresh();Check(bridge.Connected&&bridge.MotionId=="wavefile","late subscriber missed active playback");
    MotionPlaybackApi.Publish("wavefile",false);Check(bridge.MotionId=="","stop event missed");
    MotionPlaybackApi.Publish("wavefile",true);Check(bridge.MotionId=="wavefile","start event missed");
    bridge.Dispose();MotionPlaybackApi.Publish("wavefile",false);Check(bridge.MotionId=="","disposed bridge subscribed");
}
Action<string,bool> bad=(_,_)=>throw new Exception("consumer error");
bool goodCalled=false;Action<string,bool> good=(_,_)=>goodCalled=true;
MotionPlaybackApi.PlaybackChanged+=bad;MotionPlaybackApi.PlaybackChanged+=good;
MotionPlaybackApi.Publish("wavefile",true);Check(goodCalled&&MotionPlaybackApi.IsPlaying,"bad listener broke playback/other listener");
MotionPlaybackApi.PlaybackChanged-=bad;MotionPlaybackApi.PlaybackChanged-=good;MotionPlaybackApi.Publish("wavefile",false);
Pass("absent MMD, late subscription, start/stop, unsubscribe and listener failure isolation");

CrowdApi.SetSource("a",true);CrowdApi.SetSource("b",true);CrowdApi.SetSource("a",false);
Check(CrowdApi.Requested,"one consumer stopped another");CrowdApi.SetSource("b",false);Check(!CrowdApi.Requested,"source leaked");
Pass("independent external source ownership");

var npc=new NpcController(1);npc.Transform.position=new(0,0,-3);
var agent=npc.Nca.NavAgent;var locomotion=npc.Nca.NpcLocomotion;
var oldDestination=agent.destination;locomotion.lastSetDestinationPoint=oldDestination;
var s=new Spectator(npc,0,new(0,0,2.8f));var path=new NavMeshPath();agent.CalculatePath(s.Destination,path);s.Acquire();
Check(agent.updatePosition&&agent.updateRotation&&!agent.isStopped&&agent.SetPathCalls==0&&Same(agent.destination,s.Destination),"acquisition must only assign a target and retain native movement flags");
locomotion.moveV=new(.125f,0,.25f);var nativeVelocity=locomotion.moveV;
s.Drive(Vector3.zero);Check(locomotion.SimpleUpdateCalls==1&&Same(locomotion.SimpleTarget,s.Destination)&&locomotion.SimpleStopDistance==.35f&&!s.Arrived&&npc.Nca.NpcAnimationManager.speed.Target==1,"native target approach was not called");
Check(Same(locomotion.moveV,nativeVelocity),"plugin overwrote native movement velocity");
npc.Transform.position=s.Destination;s.Drive(Vector3.zero);
Check(locomotion.moveV.sqrMagnitude==0&&s.Arrived&&agent.isStopped&&npc.Nca.NpcAnimationManager.speed.Target==0,"arrival retained walking velocity");
Check(npc.Transform.forward.z<0,"arrival did not face player");s.Restore();
Check(agent.updatePosition&&agent.updateRotation&&!agent.isStopped&&Same(agent.destination,oldDestination),"original nav settings not restored");
Check(Same(locomotion.lastSetDestinationPoint,oldDestination)&&npc.Nca.NpcAnimationManager.speed.Target==1,"patrol cache/speed not restored");
Check(locomotion.moveV.sqrMagnitude==0,"stale pre-crowd velocity restored");
Pass("walk/arrival/facing/stop restores original agent flags, patrol destination/cache and speed");

var busy=new NpcController(2);var lease=new Spectator(busy,0,new(0,0,3));lease.Acquire();
busy.Nca.StateModel.CurrentState=NpcStateModel.State.Interact;
busy.Nca.NavAgent.updatePosition=false;busy.Nca.NavAgent.isStopped=true;busy.Nca.NpcAnimationManager.speed.Target=0;
lease.Restore();Check(!busy.Nca.NavAgent.updatePosition&&busy.Nca.NavAgent.isStopped&&busy.Nca.NpcAnimationManager.speed.Target==0,"overwrote changed native state");
var noPath=new NpcController(3);noPath.Nca.NavAgent.hasPath=false;
var noPathLease=new Spectator(noPath,0,new(0,0,3));noPathLease.Acquire();noPathLease.Restore();
Check(!noPath.Nca.NavAgent.hasPath,"restoration invented original path");
Pass("changed native state retains ownership; no-path snapshot remains no-path");

PlayerController.Instance=new();NpcManager.Instance=new();InGameManager.Instance=new();InGameUiManager.Instance=new();
var normal=new NpcController(10);normal.Transform.position=new(0,0,8);
var police=new NpcController(11);police.Nca.StateModel.IsPolice=true;
var clerk=new NpcController(12);clerk.Nca.StateModel.CurrentFixedType=NpcStateModel.FixedType.Conbini;
var unreachable=new NpcController(13);unreachable.Nca.NavAgent.CalculateStatus=NavMeshPathStatus.PathPartial;
NpcManager.Instance.ExistNpcList.AddRange(new[]{normal,police,clerk,unreachable});
var driver=new CrowdDriver(IntPtr.Zero);CrowdDriver.Current=driver;
void Tick(float delta=0.3f) { Time.frameCount++;Time.unscaledTime+=delta;driver.Update(); }
Tick();driver.ManualRequested = !driver.ManualRequested;Tick();
Check(driver.Drive(normal.Nca.NpcLocomotion)&&!driver.Drive(police.Nca.NpcLocomotion)&&!driver.Drive(clerk.Nca.NpcLocomotion)&&!driver.Drive(unreachable.Nca.NpcLocomotion),"manual recruitment filters");
InGameUiManager.Instance.IsOpenSomeUi = true; Tick();
Check(normal.Nca.NavAgent.updatePosition && driver.ManualRequested, "settings pause must preserve manual checkbox request");
driver.ManualRequested = false; Tick(); driver.ManualRequested = true; Tick();
InGameUiManager.Instance.IsOpenSomeUi = false; Tick();
Check(driver.Drive(normal.Nca.NpcLocomotion), "manual setting must gather after closing settings");
Check(driver.Drive(normal.Nca.NpcLocomotion),"locomotion takeover absent");
Check(!driver.Drive(police.Nca.NpcLocomotion),"police intercepted");
var beforeNormalDestination=normal.Nca.NavAgent.destination;
var interrupt=typeof(CrowdInterruptPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
interrupt.Invoke(null,new object[]{normal,NpcStateModel.State.FoundStranger});
Check(normal.Nca.NavAgent.updatePosition,"state prefix must release before native OnEnter");
normal.Nca.StateModel.CurrentState=NpcStateModel.State.FoundStranger;
normal.Nca.NavAgent.isStopped=true;normal.Nca.NpcAnimationManager.speed.Target=0;
Check(!driver.Drive(normal.Nca.NpcLocomotion),"found state retained by crowd");
Tick();driver.ManualRequested = !driver.ManualRequested;Tick();Check(!driver.Drive(normal.Nca.NpcLocomotion),"manual stop retains participant");
normal.Nca.StateModel.CurrentState=NpcStateModel.State.Idle;normal.Nca.NavAgent.isStopped=false;normal.Nca.NpcAnimationManager.speed.Target=1;
PlayerController.Instance.Pca.PlayerState.CurrentAction.Type=ActionType.HipShake;Tick();
Check(driver.Drive(normal.Nca.NpcLocomotion),"standalone native trigger requires MMD");
PlayerController.Instance.Pca.PlayerState.CurrentAction.Type=ActionType.None;Tick();Check(!driver.Drive(normal.Nca.NpcLocomotion),"native end did not release");
MotionPlaybackApi.Publish("wavefile",true);Tick();Check(driver.Drive(normal.Nca.NpcLocomotion),"MMD trigger failed");
MotionPlaybackApi.Publish("wavefile",false);Tick();Check(!driver.Drive(normal.Nca.NpcLocomotion),"MMD cancellation failed");
MotionPlaybackApi.Publish("shake",true); Tick(); Check(!driver.Drive(normal.Nca.NpcLocomotion), "configured list remains default");
Seleka2.NpcSpectators.Plugin.FollowAllMmd.Value = true; Tick(); Check(driver.Drive(normal.Nca.NpcLocomotion), "all-MMD checkbox includes new Shake imports");
Seleka2.NpcSpectators.Plugin.FollowMmd.Value = false; Tick(); Check(!driver.Drive(normal.Nca.NpcLocomotion), "MMD trigger switch releases gathering");
Seleka2.NpcSpectators.Plugin.FollowMmd.Value = true; Seleka2.NpcSpectators.Plugin.FollowAllMmd.Value = false; MotionPlaybackApi.Publish("shake",false); Tick();
driver.ManualRequested = !driver.ManualRequested;Tick();InGameManager.Instance.TempInGameState.IsTimeStop=true;Tick();
Check(normal.Nca.NavAgent.updatePosition&&!driver.Drive(normal.Nca.NpcLocomotion),"time-stop not respected");
InGameManager.Instance.TempInGameState.IsTimeStop=false;driver.ManualRequested = !driver.ManualRequested;Tick();
Check(driver.Drive(normal.Nca.NpcLocomotion),"manual restart failed");
NpcManager.Instance=new();Tick();Check(normal.Nca.NavAgent.updatePosition,"scene switch not restored");driver.OnDestroy();
Check(!Seleka2.NpcSpectators.Plugin.Logger.Lines.Any(s=>s.Contains("FAILED")),"driver emitted unexpected failure");
Pass("production driver: standalone settings/native trigger, MMD link, partial path exclusion, interruption, time-stop, scene cleanup");
// Standpoint visibility and native-navigation ownership. Synthetic ray hits
// exercise selection; they are not a rendering/physics or native movement emulator.
var playerRoot=new Transform();var actorRoot=new Transform();
var actorRoots=new[]{playerRoot,actorRoot};
var wall=new Collider();var playerCollider=new Collider{transform=new Transform{parent=playerRoot}};
var npcCollider=new Collider{transform=new Transform{parent=actorRoot}};
var trigger=new Collider{isTrigger=true};var npcLayer=new Collider();npcLayer.gameObject.layer=12;
Physics.Cast=(_,_,_)=>new[]{new RaycastHit{collider=playerCollider},new RaycastHit{collider=npcCollider},new RaycastHit{collider=trigger},new RaycastHit{collider=npcLayer}};
Check(CrowdVisibility.IsClear(Vector3.zero,new(3,0,0),actorRoots),"Actors and triggers must not occlude gathering");
Physics.Cast=(_,_,_)=>new[]{new RaycastHit{collider=playerCollider},new RaycastHit{collider=wall}};
Check(!CrowdVisibility.IsClear(Vector3.zero,new(3,0,0),actorRoots),"A wall behind a player hit must still be tested");
RaycastHit[] WallAtX(Vector3 origin,Vector3 direction,float distance,float halfWidth=float.PositiveInfinity)
{
    if(Math.Abs(direction.x)<1e-6f)return [];
    float t=(1-origin.x)/direction.x;
    return t>0&&t<=distance&&Math.Abs(origin.z+direction.z*t)<=halfWidth?new[]{new RaycastHit{collider=wall}}:[];
}
Physics.Cast=(o,d,t)=>WallAtX(o,d,t);
Check(!CrowdVisibility.IsClear(Vector3.zero,new(3,0,0),actorRoots)&&!CrowdVisibility.IsClear(Vector3.zero,new(6,0,0),actorRoots),"A wall within the minimum ring rejects both inner and outer positions behind it");
Check(CrowdVisibility.IsClear(Vector3.zero,new(-3,0,0),actorRoots),"An occluded direction must not erase the opposite side");
Physics.Cast=(o,d,t)=>WallAtX(o,d,t,.2f);
Check(!CrowdVisibility.IsClear(Vector3.zero,new(3,0,0),actorRoots)&&CrowdVisibility.IsClear(Vector3.zero,new(3,0,3),actorRoots),"A narrow pillar rejects only intersecting rays");
Physics.Cast=(o,_,_)=>o.y<1?new[]{new RaycastHit{collider=wall}}:[];
Check(!CrowdVisibility.IsClear(Vector3.zero,new(3,0,0),actorRoots),"Waist-height obstruction cannot be missed by a clear chest ray");
Physics.Cast=(o,_,_)=>o.y>1?new[]{new RaycastHit{collider=wall}}:[];
Check(!CrowdVisibility.IsClear(Vector3.zero,new(3,0,0),actorRoots),"Chest-height obstruction cannot be missed by a clear waist ray");
Physics.Cast=null;

CrowdDriver SelectionScene(int id)
{
    MotionPlaybackApi.Publish("",false);
    PlayerController.Instance=new();NpcManager.Instance=new();InGameManager.Instance=new();InGameUiManager.Instance=new();
    var candidate=new NpcController(id);candidate.Transform.position=new(0,0,8);NpcManager.Instance.ExistNpcList.Add(candidate);
    var gather=new CrowdDriver(IntPtr.Zero);CrowdDriver.Current=gather;gather.ManualRequested=true;
    return gather;
}
Physics.Cast=(_,_,_)=>new[]{new RaycastHit{collider=wall}};
var hidden=SelectionScene(50);var hiddenNpc=NpcManager.Instance.ExistNpcList[0];int sampledBefore=NavMesh.Samples;
Time.frameCount++;Time.unscaledTime+=.3f;hidden.Update();
Check(!hidden.Drive(hiddenNpc.Nca.NpcLocomotion)&&NavMesh.Samples==sampledBefore&&hiddenNpc.Nca.NavAgent.PathCalculations==0,"Occluded raw directions are rejected before NavMesh sampling and path calculation");hidden.OnDestroy();
Physics.Cast=(o,d,t)=>WallAtX(o,d,t);
NavMesh.Project=_=>new(3,0,0);
var projected=SelectionScene(51);var projectedNpc=NpcManager.Instance.ExistNpcList[0];
Time.frameCount++;Time.unscaledTime+=.3f;projected.Update();
Check(!projected.Drive(projectedNpc.Nca.NpcLocomotion)&&projectedNpc.Nca.NavAgent.PathCalculations==0,"A raw visible slot projected behind the wall is rejected before path calculation");projected.OnDestroy();
NavMesh.Project=null;
var visible=SelectionScene(52);var visibleNpc=NpcManager.Instance.ExistNpcList[0];
Time.frameCount++;Time.unscaledTime+=.3f;visible.Update();
Check(visible.Drive(visibleNpc.Nca.NpcLocomotion)&&visibleNpc.Nca.NavAgent.destination.x<1&&visibleNpc.Nca.NpcLocomotion.SimpleUpdateCalls>0,"Visible side remains recruitable using the native approach routine");visible.OnDestroy();
Physics.Cast=null;

var flagsNpc=new NpcController(53);flagsNpc.Nca.NavAgent.updatePosition=false;flagsNpc.Nca.NavAgent.updateRotation=false;
var flags=new Spectator(flagsNpc,0,new(0,0,3));flags.Acquire();
Check(!flagsNpc.Nca.NavAgent.updatePosition&&!flagsNpc.Nca.NavAgent.updateRotation,"Acquisition leaves nonstandard native update flags intact");
flagsNpc.Nca.NavAgent.updatePosition=true;flagsNpc.Nca.NavAgent.updateRotation=true;flags.Restore();
Check(flagsNpc.Nca.NavAgent.updatePosition&&flagsNpc.Nca.NavAgent.updateRotation,"Restoration must not overwrite update flags the plugin never owned");
Pass("radial occlusion, actor/trigger exclusions, projected point recheck and native movement ownership");

Console.WriteLine("Offline boundary validation complete. Real native navigation and Harmony dispatch require user gameplay testing.");
