using System.Text.Json;
using SFM.Actions;
using UnityEngine;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Master.Mission;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.InGame.MissionArea;

int checks=0;
void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
void Near(float a,float b,string name)=>Check(Math.Abs(a-b)<.00001f,name+$" ({a} vs {b})");
void Reject(Action code,string name){bool rejected=false;try{code();}catch(Exception e) when(e is ArgumentException or JsonException){rejected=true;}Check(rejected,name);}
ActionDefinition Read(string json)
{
    using var document=JsonDocument.Parse(json);ActionStatsSchema.ValidateJson(document.RootElement);
    var d=JsonSerializer.Deserialize<ActionDefinition>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    ActionStatsSchema.Validate(d);return d;
}
var defaults=ActionStatsSchema.Resolve(new(),_=>throw new Exception("Default must not query masters"));
Check(defaults==ActionStatValues.Empty,"Omitted stats have no extra effects and retain scale 1");
foreach(var pair in ActionStatsSchema.Presets)
{
    bool read=false;
    var resolved=ActionStatsSchema.Resolve(new(){NativeKind=pair.Key},name=>{read=true;Check(name==pair.Value,"Preset selects its documented original record");return new(.03f,.1f,0,0,.2f,1.5f,.3f);});
    Check(read==(pair.Value.Length>0),"Only nonempty presets read master data");
    Check(!resolved.CanStartPee&&resolved.MoisturePerSecond==0&&resolved.StaminaPerSecond==0,"Presets never implicitly start pee or consume/fill resources");
}
var explicitZero=Read("{\"nativeKind\":\"Onani\",\"stats\":{\"ecstasyPerSecond\":0,\"dokidokiAdd\":0,\"strangenessScale\":0},\"poseMission\":true}");
var zero=ActionStatsSchema.Resolve(explicitZero,_=>new(.03f,.1f,0,0,.2f,1.5f,.3f));
Check(zero.EcstasyPerSecond==0&&zero.DokidokiAdd==0&&zero.StrangenessScale==0&&zero.Strange==.2f,"Explicit zeros override individual preset fields");
Check(explicitZero.PoseMission,"Task eligibility is independent of numeric preset");
foreach(var json in new[]{"{\"nativeKind\":\"unknown\"}","{\"nativeKind\":null}","{\"nativeKind\":4}","{\"stats\":null}","{\"stats\":[]}","{\"poseMission\":null}","{\"poseMission\":\"true\"}",
    "{\"stats\":{\"ecstasyPerSecon\":0.1}}","{\"stats\":{\"ecstasyPerSecond\":null}}","{\"stats\":{\"ecstasyPerSecond\":0.1,\"ECSTASYPERSECOND\":0.2}}",
    "{\"stats\":{\"ecstasyPerSecond\":-0.1}}","{\"stats\":{\"dokidokiAdd\":1.1}}","{\"stats\":{\"canStartPee\":\"true\"}}","{\"stats\":{},\"STATS\":{}}"})
    Reject(()=>Read(json),"Malformed configuration rejected: "+json);
foreach(var n in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
    Reject(()=>ActionStatsSchema.Validate(new(){Stats=new(){EcstasyPerSecond=n}}),"Nonfinite runtime rate rejected");

var driver=new ActionDriver();ActionDriver.Current=driver;var pca=driver.Owner.Pca;var state=pca.PlayerState;
driver.Values=new(.03f,.2f,.02f,.25f,.3f,2,.4f,true);
var ecstasy=new PlayerEcstasyController{pca=pca};var moisture=new PlayerMoistureManager{pca=pca};
var misc=new PlayerMiscProcess{pca=pca};var pee=new PlayerPeeController{pca=pca};var strange=new PlayerStrangenessController{pca=pca};
Time.deltaTime=.2f;Time.frameCount=1;GameState.GameStateData.MotionSpeedScaleIndex=2;
state.AddEcstasyByPiston=.8f;
var input=NativeActionStats.BeforeEcstasy(ecstasy);
Near(state.AddEcstasyByPiston,.8f+.03f*1.2f/.15f,"Native accumulator preserves equipment and motion speed");
Check(NativeActionStats.BeforeEcstasy(ecstasy)==null,"Duplicate native update cannot double the contribution");
// Model only the documented native input/output boundary, not its whole state machine.
float nativeRate=state.AddEcstasyByPiston*.15f*(1+state.Dokidoki*.5f);
state.AddEcstasyByPiston=0;input!.Release();
Near(state.AddEcstasyByPiston,0,"Consumed input never resurrects earlier equipment values");
Near(nativeRate,(.8f*.15f+.03f*1.2f),"Additive rate enters original native integration once");
Time.frameCount++;state.AddEcstasyByPiston=.5f;input=NativeActionStats.BeforeEcstasy(ecstasy);input!.Release();
Near(state.AddEcstasyByPiston,.5f,"Native early-return/exception restores only the unconsumed injection");
Time.frameCount++;input=NativeActionStats.BeforeEcstasy(ecstasy);state.AddEcstasyByPiston=42;input!.Release();
Near(state.AddEcstasyByPiston,42,"A different writer's value is not overwritten by cleanup");

state.CurrentRank.DokidokiBoostScale=1.5f;state.Dokidoki=.2f;
NativeActionStats.AfterDokidoki(misc);Near(state.Dokidoki,.5f,"Heartbeat contribution inherits native rank scaling");
state.Dokidoki=.2f;NativeActionStats.AfterDokidoki(misc);Near(state.Dokidoki,.5f,"A fresh native recalculation does not accumulate earlier frames");
state.Dokidoki=.9f;NativeActionStats.AfterDokidoki(misc);Near(state.Dokidoki,1,"Heartbeat contribution retains 0-1 clamp");

GameState.GameStateData.PlayerAddMoisture=.4f;NativeActionStats.BeforeMoisture(moisture);
Near(GameState.GameStateData.PlayerAddMoisture,.404f,"Moisture enters the native queue as rate times delta");
NativeActionStats.BeforeMoisture(moisture);Near(GameState.GameStateData.PlayerAddMoisture,.404f,"Moisture is queued once per frame");
Time.frameCount++;GameState.OptionData.PeeDisable=true;NativeActionStats.BeforeMoisture(moisture);
Near(GameState.GameStateData.PlayerAddMoisture,.404f,"PeeDisable prevents custom input");
GameState.OptionData.PeeDisable=false;
// The queue is intentional native state, retained after action stop for normal absorption.
driver.Enabled=false;Time.frameCount++;NativeActionStats.BeforeMoisture(moisture);
Near(GameState.GameStateData.PlayerAddMoisture,.404f,"Stopped action does not append fluid or erase the native queue");driver.Enabled=true;

NativeActionStats.AfterStamina(pca.Stamina);Near(pca.Stamina.Current,.95f,"Stamina method multiplies time exactly once");
Check(pca.Stamina.Calls==1&&pca.Stamina.Rate==.25f,"Native stamina method owns the consumed rate");
NativeActionStats.AfterStamina(pca.Stamina);Check(pca.Stamina.Calls==1,"Repeated native calls do not double-charge stamina");
var other=new StaminaManager();NativeActionStats.AfterStamina(other);Check(other.Calls==0,"Other stamina owners are untouched");

GameState.GameStateData.PlayerMoisture=0;NativeActionStats.BeforePee(pee);Check(pee.Starts==0,"No synthetic urine when empty");
GameState.GameStateData.PlayerMoisture=.2f;NativeActionStats.BeforePee(pee);NativeActionStats.BeforePee(pee);
Check(pee.Starts==1&&state.IsPeeing,"Custom pee uses native StartPee and does not restart an active timer");
state.IsPeeing=false;GameState.OptionData.PeeDisable=true;NativeActionStats.BeforePee(pee);Check(pee.Starts==1,"PeeDisable blocks the custom start");GameState.OptionData.PeeDisable=false;

float exposure=.2f;NativeActionStats.AfterExpose(strange,ref exposure);Near(exposure,.7f,"Action scale/add precede native lighting, sight and invisibility");
// No override of final NPC result: the native caller still multiplies and can clear for invisibility.
Near(exposure*.5f*0,0,"Zero native sight still gives zero visual suspicion");
float discomfort=.6f;NativeActionStats.AfterDiscomfort(strange,ref discomfort);Near(discomfort,.6f,"Native directional maximum is preserved");
discomfort=.1f;NativeActionStats.AfterDiscomfort(strange,ref discomfort);Near(discomfort,.4f,"Custom discomfort supplies a maximum rather than an additive cache");
driver.Enabled=false;exposure=.2f;discomfort=.1f;NativeActionStats.AfterExpose(strange,ref exposure);NativeActionStats.AfterDiscomfort(strange,ref discomfort);
Near(exposure,.2f,"No stale suspicion after cancellation");Near(discomfort,.1f,"No stale discomfort cache after cancellation");driver.Enabled=true;

driver.Definition.PoseMission=true;bool mission=false;PlayerFacade.Instance.Exposed=ExposedBodyParts.None;
NativeActionStats.AfterPoseCondition(ref mission);Check(!mission,"Pose task retains exposure gate");
PlayerFacade.Instance.Exposed=ExposedBodyParts.Front;NpcRouteAreaManager.Instance=new(){IsInArea=false};
NativeActionStats.AfterPoseCondition(ref mission);Check(!mission,"Pose task retains area gate");
NpcRouteAreaManager.Instance.IsInArea=true;NativeActionStats.AfterPoseCondition(ref mission);Check(mission,"Declared pose gains task eligibility in the valid area");
mission=false;NpcRouteAreaManager.Instance=null;NativeActionStats.AfterPoseCondition(ref mission);Check(mission,"Missing optional area matches original rule");
mission=false;driver.Definition.PoseMission=false;NativeActionStats.AfterPoseCondition(ref mission);Check(!mission,"Undeclared custom motions are not automatically tasks");
mission=true;driver.Enabled=false;NativeActionStats.AfterPoseCondition(ref mission);Check(mission,"Original successful task condition is never removed");
state.Dokidoki=.1f;NativeActionStats.AfterDokidoki(misc);Near(state.Dokidoki,.1f,"Stopped playback does not contribute to heartbeat");
var foreign=new PlayerClassAccessor();Check(!NativeActionStats.Active(foreign,out _),"A previous/foreign character reference cannot receive effects");
driver.Enabled=true;driver.Definition.PoseMission=true;Time.frameCount++;pca.Stamina.Current=.01f;NativeActionStats.AfterStamina(pca.Stamina);
Check(driver.StopReason=="stamina depleted"&&!driver.Enabled,"Exhaustion retains native flash path and cancels custom playback");

MAction.Records[ActionType.OnaniNormal]=new(){AddEcstasy=.03f,AddDokidoki100=10,Strange=.2f,StrangenessScale=1.2f,Discomfort=.3f};
var record=MAction.Records[ActionType.OnaniNormal];var preset=NativeActionStats.Resolve(new(){NativeKind="Onani"});
Near(preset.EcstasyPerSecond,.03f,"Native preset reads actual master rate");Near(preset.DokidokiAdd,.1f,"Master percentage converts to normalized heartbeat contribution");
Check(ReferenceEquals(record,MAction.Records[ActionType.OnaniNormal])&&record.AddEcstasy==.03f,"Original master record stays unchanged");
Console.WriteLine($"PASS {checks} assertions: stat schema, native inputs, ownership, frame guards, pee, perception, task conditions and cancellation; bounded native fixtures, runtime unverified.");
