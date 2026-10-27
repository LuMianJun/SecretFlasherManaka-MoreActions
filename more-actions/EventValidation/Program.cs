using System.Text.Json;
using Seleka2.Actions;
using Seleka2.NpcSpectators;
using UnityEngine;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Player.ScriptableObject;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.Sound;

int count = 0, ids = 0;
var calls = new List<string>(); var errors = new List<string>(); var activeLeases = new HashSet<string>();
void Check(bool ok, string name) { count++; if (!ok) throw new Exception(name + ": " + string.Join(",", calls) + "\n" + string.Join("\n", Plugin.LogSource?.Messages.TakeLast(8) ?? Enumerable.Empty<string>())); }
void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);
ActionEventDefinition E(double? time, string tag, string repeat = "EachLoop", bool lease = false) => new() { Time = time, Event = "test.record", Args = Args(new { tag, lease }), Repeat = repeat };
ActionDefinition Definition(params ActionEventDefinition[] entries) => new() { Id = "event-" + ++ids, DisplayName = "Test", Events = entries };
using var recordHandler = ActionEventRegistry.Register("test.record", json =>
{
    using var doc = JsonDocument.Parse(json); if (doc.RootElement.GetProperty("tag").ValueKind != JsonValueKind.String) throw new ArgumentException("tag");
}, (source, json) =>
{
    using var doc = JsonDocument.Parse(json); string tag = doc.RootElement.GetProperty("tag").GetString()!; calls.Add(tag);
    if (tag == "throw") throw new Exception("fixture event failure");
    if (!doc.RootElement.GetProperty("lease").GetBoolean()) return null;
    activeLeases.Add(source);
    return new Lease(() => { activeLeases.Remove(source); calls.Add("release:" + tag); if (tag == "bad-cleanup") throw new Exception("fixture cleanup failure"); });
});
ActionEventRunner Runner(ActionDefinition def) => new(def, Array.Empty<ActionEventDefinition>(), 1, () => true, errors.Add);

calls.Clear();
var basic = Definition(E(.35,"a"), E(.85,"b"), E(1,"last")); basic.OnStart = new[] { E(null,"start") }; basic.OnFinish = new[] { E(null,"finish") }; basic.OnCancel = new[] { E(null,"cancel") };
using (var runner = Runner(basic))
{
    Check(calls.Count == 0, "Validation executes no effects");
    runner.Advance(0); Check(calls.SequenceEqual(new[] {"start"}), "Start before timed events");
    runner.Advance(.34); Check(calls.Count == 1, "No early event");
    runner.Advance(.35); runner.Advance(.35); Check(calls.SequenceEqual(new[] {"start","a"}), "Exact boundary fires once");
    runner.Advance(1.2); runner.Close(true); runner.Close(false);
    Check(calls.SequenceEqual(new[] {"start","a","b","last","finish"}), "Dropped frame drains final events before finish; close idempotent");
}
calls.Clear(); using (var runner = Runner(basic)) { runner.Advance(.4); runner.Close(false); runner.Advance(10); }
Check(calls.SequenceEqual(new[] {"start","a","cancel"}), "Cancellation skips future events and finish");
calls.Clear(); using (var runner = Runner(basic)) { runner.Close(false); }
Check(calls.Count == 0, "Cancelled scheduling lead has no start/cancel effects");
var loop = Definition(E(0,"zero"),E(1,"end"),E(.5,"once","Once")); loop.Loop = true;
calls.Clear(); using (var runner = Runner(loop)) { runner.Advance(0); runner.Advance(2.1); runner.Advance(2.1); }
Check(calls.SequenceEqual(new[] {"zero","once","end","zero","end","zero"}), "Loop catch-up preserves end-before-next-zero and Once behavior");
var tied = Definition(E(.2,"first"), E(.2,"second")); calls.Clear(); using (var runner = Runner(tied)) runner.Advance(.3);
Check(calls.SequenceEqual(new[] {"first","second"}), "Same-time JSON declaration order");
calls.Clear(); using (var runner = new ActionEventRunner(tied, new[] { E(.2,"clip") },1,()=>true,errors.Add)) runner.Advance(.3);
Check(calls.SequenceEqual(new[] {"first","second","clip"}), "JSON then embedded events at identical time");
var leaseLoop = Definition(E(0,"scope",lease:true)); leaseLoop.Loop = true;
calls.Clear(); using (var runner = Runner(leaseLoop))
{
    runner.Advance(0); Check(activeLeases.Count == 1,"Scope acquired");
    runner.Advance(3.1); Check(activeLeases.Count == 1 && calls.Count(t=>t=="release:scope") == 3,"Loop retrigger replaces prior lease without growth");
}
Check(activeLeases.Count == 0,"Loop scope released on stop");
var cleanup = Definition(E(0,"good",lease:true),E(.1,"bad-cleanup",lease:true)); cleanup.OnCancel = new[] { E(null,"throw"),E(null,"after-cancel-error") };
calls.Clear(); errors.Clear(); using (var runner = Runner(cleanup)) { runner.Advance(.2); runner.Close(false); }
Check(activeLeases.Count == 0 && calls.Contains("release:good") && calls.Contains("after-cancel-error") && errors.Count == 2,"Lifecycle and lease failures do not block remaining cleanup");
calls.Clear(); using (var runner = Runner(Definition(E(0,"throw"), E(.1,"unreached")))) { Reject(()=>runner.Advance(.2),"Event failure is reported"); }
Check(!calls.Contains("unreached"),"Failure aborts remaining dispatch");
calls.Clear(); var unavailable = Definition(E(0,"valid"),new ActionEventDefinition {Time=.5,Event="not.installed"});
Reject(()=>Runner(unavailable),"Every handler binds before playback"); Check(calls.Count==0,"Missing later dependency produces no partial effects");
using(var registered=ActionEventRegistry.Register("late.provider",_=>{},(_,_)=>null)) { using var runner=Runner(Definition(new ActionEventDefinition{Time=0,Event="late.provider"})); runner.Advance(0); }
Reject(()=>Runner(Definition(new ActionEventDefinition{Time=0,Event="late.provider"})),"Provider removal prevents later starts");
using(var registered=ActionEventRegistry.Register("removed.active",_=>{},(_,_)=>null))
{
    using var runner=Runner(Definition(new ActionEventDefinition{Time=.5,Event="removed.active"})); registered.Dispose(); Reject(()=>runner.Advance(1),"Removed provider cannot be invoked by active plan");
}
using(var runner=Runner(Definition())) { Reject(()=>runner.Advance(double.NaN),"NaN clock rejected"); runner.Advance(.5); Reject(()=>runner.Advance(.4),"Backward clock rejected"); }
foreach(string json in new[]{"{\"events\":null}","{\"events\":[{\"event\":\"x\",\"tiem\":1}]}","{\"events\":[{\"event\":\"x\",\"event\":\"y\"}]}","{\"onStart\":[{\"event\":\"x\",\"time\":0}]}","{\"events\":[{\"event\":\"x\",\"args\":null}]}"}) Reject(()=>ActionEventSchema.ValidateJson(json),"Strict event JSON: "+json);
foreach(var def in new[]{Definition(new ActionEventDefinition{Event="x"}),Definition(E(-1,"negative")),Definition(E(2,"beyond")),Definition(E(0,"repeat","Forever")),Definition(new ActionEventDefinition{Time=0,Event="x",Args=Args("bad")})}) Reject(()=>Runner(def),"Invalid event rules rejected");
ActionEventRunner reentrant = null;
using(var reg=ActionEventRegistry.Register("stop.inside",_=>{},(source,_)=> { reentrant.Close(false); activeLeases.Add(source); return new Lease(()=>activeLeases.Remove(source)); }))
{
    calls.Clear(); var def=Definition(new ActionEventDefinition{Time=0,Event="stop.inside"},E(0,"unreached"));
    reentrant=Runner(def); reentrant.Advance(0); reentrant.Dispose();
    Check(activeLeases.Count==0 && calls.Count==0,"Reentrant stop releases callback's late-returned lease and aborts remaining events");
}

// Integrate the actual production driver and adapters using controlled native boundaries.
Plugin.LogSource = new(); Plugin.Volume = new(1); Plugin.FollowCamera = new(true); NativeActionEvents.Register();
ActionEventBridge.Refresh();
string root = Path.Combine(Path.GetTempPath(),"action-event-driver-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
ActionDriver driver = null; PlayerController player = null; LoadedAction loaded = null;
void Frame(double time)
{
    AudioSettings.dspTime = time; Time.frameCount++; driver.BeforeNativeUpdate(player); driver.Tick(player); driver.Apply();
}
void Start(ActionDefinition def, AnimationClip clip = null, float audioLength=0, IActionSource runtimeSource=null, IActionPose playbackPose=null)
{
    driver?.OnDestroy(); InputManager.Down.Clear(); InputManager.Move=default; GameStateFacade.Handcuffed=false;
    player=new PlayerController(); PlayerController.Instance=player; PlayerFacade.Instance.pca=player.Pca;
    ActionRegistry.RegisterRuntime(def,runtimeSource ?? new Source(),root,"validation");
    loaded=new LoadedAction{NextPose=playbackPose,Definition=ActionRegistry.Get(def.Id),Clip=clip, Audio=audioLength>0?new AudioClip{length=audioLength}:null}; ActionAssets.Items[def.Id]=loaded;
    driver=new ActionDriver(IntPtr.Zero); ActionDriver.Current=driver; Time.frameCount++; driver.Tick(player); driver.Request(def.Id); Frame(0);
}
try
{
    var bodyPose=new BodyPose();Start(Definition(),playbackPose:bodyPose);
    AudioSettings.dspTime=0;Check(driver.BeginNativeBody(player)==null,"DSP lead does not prematurely own native body correction");
    AudioSettings.dspTime=.2;
    object Hook(Type type,string method,object[] parameters)=>type.GetMethod(method,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,parameters);
    object[] lateArgs={player,null};Hook(typeof(NativeBodyPassPatch),"Prefix",lateArgs);
    Check(ReferenceEquals(lateArgs[1],bodyPose)&&bodyPose.InNative,"Actual player late-pass hook opens provider body ownership");
    object[] kneeArgs={null};Hook(typeof(NativeKneePosePatch),"Prefix",kneeArgs);Hook(typeof(NativeKneePosePatch),"Finalizer",new object[]{null,kneeArgs[0]});
    object[] toeArgs={null};Hook(typeof(NativeToePosePatch),"Prefix",toeArgs);Hook(typeof(NativeToePosePatch),"Finalizer",new object[]{null,toeArgs[0]});
    object[] headArgs={null};Hook(typeof(NativeHeadPosePatch),"Prefix",headArgs);Hook(typeof(NativeHeadPosePatch),"Finalizer",new object[]{null,headArgs[0]});
    Check(bodyPose.RestoredCorrections==3,"Actual native knee/toe/look hooks close all temporary transform scopes");
    object[] offsetArgs={.2f};bool nativeOffset=(bool)Hook(typeof(NativeKneeOffsetPatch),"Prefix",offsetArgs);
    Check(!nativeOffset&&(float)offsetArgs[0]==0,"Actual knee offset hook omits owned knee contribution only in the player pass");
    Hook(typeof(NativeBodyPassPatch),"Finalizer",new object[]{null,lateArgs[1]});
    offsetArgs=new object[]{.2f};Check((bool)Hook(typeof(NativeKneeOffsetPatch),"Prefix",offsetArgs)&&!bodyPose.InNative,"Late-pass finalizer closes ownership before NPC dispatch");
    driver.BeforeNativeUpdate(player);Check(bodyPose.RestoreUpdates>0,"Driver withdraws prior body output before native animation");
    Hook(typeof(NativeBodyPassPatch),"Prefix",lateArgs);var nativeFailure=new Exception("native boundary");
    Check(ReferenceEquals(Hook(typeof(NativeBodyPassPatch),"Finalizer",new object[]{nativeFailure,lateArgs[1]}),nativeFailure)&&!bodyPose.InNative&&!bodyPose.LastSuccess,"Native failure is propagated unchanged while ending body scope");
    driver.Stop("cancel");Check(bodyPose.Disposed,"Cancellation disposes body pose ownership");

    var walk=new RootPose();Start(Definition(),playbackPose:walk);Frame(.6);
    Check(walk.FaceRestores==1,"Driver restores transient face overlay before native update");
    Check(driver.Playing&&Math.Abs(player.transform.position.x-.5f)<1e-5,"Root motion moves the actual player without canceling itself");
    Check(player.Pca.CharacterController.Moves.All(v=>v.sqrMagnitude<=.01001f),"Controller movement is swept in small steps");
    int moved=player.Pca.CharacterController.Moves.Count;Frame(.6);
    Check(player.Pca.CharacterController.Moves.Count==moved,"Same absolute sample never applies displacement twice");
    player.transform.position=new Vector3(player.transform.position.x+.1f,0,0);Frame(.7);
    Check(!driver.Playing&&walk.Disposed,"Unrelated position changes still cancel root-motion playback");
    walk=new RootPose();Start(Definition(),playbackPose:walk);Frame(.4);float reached=player.transform.position.x;
    InputManager.Move=new Vector3(1,0,0);Frame(.5);
    Check(!driver.Playing&&walk.Disposed&&player.transform.position.x==reached,"User movement cancels and leaves player at the reached position");
    walk=new RootPose();Start(Definition(),playbackPose:walk);Frame(.3);reached=player.transform.position.x;
    InputManager.Down.Add(InputManager.InputType.Cancel);Frame(.4);
    Check(!driver.Playing&&walk.Disposed&&player.transform.position.x==reached,"Cancel restores the pose without teleporting the player back");
    walk=new RootPose();Start(Definition(),playbackPose:walk);
    player.Pca.CharacterController.Resolve=d=>new Vector3(Math.Min(d.x,.25f-player.transform.position.x),0,d.z);Frame(.6);
    Check(!driver.Playing&&walk.Disposed&&Math.Abs(player.transform.position.x-.25f)<1e-5,"A wall stops playback at a reachable position without disabling collision");
    walk=new RootPose();Start(Definition(),playbackPose:walk);player.Pca.CharacterController.Resolve=d=>{driver.Stop("controller callback");return d;};Frame(.6);
    Check(!driver.Playing&&walk.Disposed&&player.Pca.CharacterController.Moves.Count==1,"Reentrant native/controller cancellation prevents all remaining sweep steps");
    walk=new RootPose();Start(Definition(),playbackPose:walk);player.Pca.CharacterController.Resolve=_=>new Vector3(float.NaN,0,0);Frame(.3);
    Check(!driver.Playing&&walk.Disposed,"Nonfinite controller output is reported rather than treated as successful motion");
    var loopWalk=Definition();loopWalk.Loop=true;walk=new RootPose();Start(loopWalk,playbackPose:walk);Frame(1.4);
    Check(driver.Playing&&Math.Abs(player.transform.position.x-1.3f)<1e-5,"Loop root motion accumulates complete cycles");Frame(2.2);
    Check(driver.Playing&&Math.Abs(player.transform.position.x-2.1f)<1e-5,"Loop boundary does not snap back to the first-frame position");driver.Stop("test cleanup");
    Check(ActionPlaybackApi.RootCycleOffset.sqrMagnitude==0,"Stop clears the loop translation for the next camera/action");
    walk=new RootPose{Sample=_=>new Vector3(float.NaN,0,0)};Start(Definition(),playbackPose:walk);Frame(.2);
    Check(!driver.Playing&&player.Pca.CharacterController.Moves.Count==0,"Invalid trajectories are rejected before touching the controller");
    walk=new RootPose{Sample=_=>new Vector3(1000,0,0)};Start(Definition(),playbackPose:walk);Frame(.2);
    Check(!driver.Playing&&player.Pca.CharacterController.Moves.Count==0,"Discontinuous trajectory cannot turn into a warp");
    calls.Clear(); var waiting=Definition(E(0,"async-start")); ActionAssets.Pending.Add(waiting.Id);
    Start(waiting); Frame(.2); Check(!driver.Playing && calls.Count==0,"Unprepared action waits without firing lifecycle effects");
    InputManager.Down.Add(InputManager.InputType.Cancel); Frame(.3); ActionAssets.Pending.Remove(waiting.Id); InputManager.Down.Clear(); Frame(.4);
    Check(!driver.Playing && calls.Count==0,"Cancel removes pending playback; later readiness never auto-starts it");
    waiting=Definition(E(0,"ready-start")); ActionAssets.Pending.Add(waiting.Id); Start(waiting); Frame(.2);
    ActionAssets.Pending.Remove(waiting.Id); Frame(.3); Check(driver.Playing,"A still-requested action starts once assets become ready");
    driver.Stop("test cleanup");
    var preparation=new PreparedSource(); waiting=Definition(E(0,"prepared-start")); Start(waiting,runtimeSource:preparation); Frame(.2);
    Check(!driver.Playing,"Ready music never forces synchronous skeleton construction at selection");
    preparation.Ready=true;Frame(.3);Check(driver.Playing,"Playback begins after provider prewarm finishes");driver.Stop("test cleanup");
    calls.Clear(); var def=Definition(E(0,"zero"),E(.5,"half"),E(1,"last")); def.OnStart=new[]{E(null,"start")}; def.OnFinish=new[]{E(null,"finish")}; def.OnCancel=new[]{E(null,"cancel")};
    Start(def); Frame(.099); Check(calls.Count==0&&driver.Playing,"Scheduled lead does not fire start or zero event");
    Frame(.1); Check(calls.SequenceEqual(new[]{"start","zero"}),"Actual DSP origin triggers lifecycle and time zero");
    Frame(1.09999999); Check(driver.Playing&&!calls.Contains("last"),"Float pose rounding cannot finish before double event clock reaches last event");
    Frame(1.2); Check(calls.SequenceEqual(new[]{"start","zero","half","last","finish"})&&!driver.Playing&&loaded.LastPose.Disposed&&!ActionPlaybackApi.IsPlaying,"Driver drains terminal interval, finishes, restores pose and publishes stop");
    calls.Clear(); def=Definition(E(1,"last")); def.OnFinish=new[]{E(null,"finish")}; def.AudioDelaySeconds=.5; Start(def,audioLength:2);
    Frame(1.2); Check(driver.Playing&&calls.SequenceEqual(new[]{"last"}),"Motion events finish while long/delayed music continues"); Frame(2.7); Check(!driver.Playing&&calls.Last()=="finish","Finish waits for complete audio timeline");
    calls.Clear(); def=Definition(E(.8,"future")); def.OnStart=new[]{E(null,"scope",lease:true)}; def.OnCancel=new[]{E(null,"cancel")}; Start(def); Frame(.2); InputManager.Down.Add(InputManager.InputType.Cancel); Frame(.3);
    Check(!driver.Playing&&activeLeases.Count==0&&calls.Contains("cancel")&&!calls.Contains("future")&&loaded.LastPose.Disposed,"Native cancellation releases scopes without future events");
    def=Definition(new ActionEventDefinition{Time=.1,Event="native.playSe",Args=Args(new{type="FootStepSneaker"})}); Start(def); Frame(.3);
    Check(player.Pca.PlayerSoundManager.Calls.SequenceEqual(new[]{InGameSoundManager.SeType.FootStepSneaker}),"Native sound adapter calls current player's original sound manager");
    def=Definition(new ActionEventDefinition{Time=0,Event="native.footstep",Args=Args(new{foot="Right",direction="Back",isMoveAction=true})}); Start(def); Frame(.2);
    var foot=player.Pca.PlayerAnimationReceiver.Feet.Single(); Check(foot.side=="Right"&&foot.record.SeType==InGameSoundManager.SeType.FootStepHeel&&foot.record.Direct==Constant.Direct.Back&&foot.record.IsMoveAction&&foot.record.Destroyed,"Foot adapter supplies typed native record and releases transient object");
    Reject(()=>NativeActionEvents.Sound("{\"type\":\"999\"}"),"Sound IDs require actual enum names"); Reject(()=>NativeActionEvents.Foot("{\"foot\":\"Other\"}"),"Foot side validated"); Reject(()=>NativeActionEvents.Foot("{\"direction\":\"Front\"}"),"Foot direction validated"); Reject(()=>NativeActionEvents.Sound("{\"type\":\"FootStepHeel\",\"volume\":1}"),"Unknown native arguments rejected");
    var embeddedRecord=new MFootAnimationEventRecord{SeType=InGameSoundManager.SeType.FootStepHeel,Direct=Constant.Direct.Left,IsMoveAction=true};
    var clip=new AnimationClip{events=new[]{new AnimationEvent{time=.2f,functionName="OnPlaySe",intParameter=(int)InGameSoundManager.SeType.Shiofuki},new AnimationEvent{time=.4f,functionName="FootL",objectReferenceParameter=embeddedRecord}}};
    var converted=ClipActionEvents.Read(clip); Check(converted[0].Event=="native.animationSound"&&converted[1].Event=="native.footstep","Embedded callbacks converted with original OnPlaySe semantics");
    def=Definition(); Start(def,clip); Frame(.6); Check(player.Pca.PlayerAnimationReceiver.Sounds.Single()==InGameSoundManager.SeType.Shiofuki&&player.Pca.PlayerAnimationReceiver.Feet.Single().side=="Left"&&!embeddedRecord.Destroyed,"Embedded callbacks reach actual receiver; original bundle record is retained");
    Reject(()=>ClipActionEvents.Read(new AnimationClip{events=new[]{new AnimationEvent{functionName="Unknown"}}}),"Unknown embedded callback rejected clearly");
    Reject(()=>ClipActionEvents.Read(new AnimationClip{events=new[]{new AnimationEvent{functionName="FootL"}}}),"Embedded foot requires original typed record");
    def=Definition(); def.UseClipEvents=false; Start(def,new AnimationClip{events=new[]{new AnimationEvent{functionName="Unknown"}}}); Check(driver.Playing,"useClipEvents=false bypasses unsupported clip events");
    calls.Clear(); def=Definition(new ActionEventDefinition{Time=0,Event="native.playSe",Args=Args(new{type="FootStepHeel"})},E(0,"unreached")); def.OnCancel=new[]{E(null,"state-cancel")}; Start(def);
    player.Pca.PlayerSoundManager.Callback=()=>player.Pca.PlayerState.CurrentState=PlayerStateModel.PlayerState.Busy; Frame(.2);
    Check(!driver.Playing&&calls.SequenceEqual(new[]{"state-cancel"})&&loaded.LastPose.Disposed,"Native state change aborts subsequent same-frame events and restores player");
    calls.Clear(); def=Definition(new ActionEventDefinition{Time=0,Event="native.playSe",Args=Args(new{type="FootStepHeel"})},E(0,"unreached")); Start(def); player.Pca.PlayerSoundManager.Callback=()=>driver.Stop("native fixture"); Frame(.2);
    Check(!driver.Playing&&calls.Count==0,"Reentrant native stop aborts dispatch");
    calls.Clear(); def=Definition(E(0,"throw"),E(.1,"unreached")); def.OnCancel=new[]{E(null,"cancel")}; Start(def);
    try { Frame(.3); } catch { driver.Stop("pose error"); }
    Check(!driver.Playing&&loaded.LastPose.Disposed&&calls.SequenceEqual(new[]{"throw","cancel"}),"Driver output failure restores state and runs cancellation cleanup");
    def=Definition(); def.OnStart=new[]{new ActionEventDefinition{Event="npc.spectators"}}; CrowdApi.SetSource("independent",true); Start(def); Frame(.2); Check(CrowdApi.Requested,"Optional NPC event starts gathering");
    driver.Stop("cancel"); Check(CrowdApi.Requested,"Action cleanup preserves independent crowd source"); CrowdApi.SetSource("independent",false); Check(!CrowdApi.Requested,"Action-owned crowd source is released");
    def=Definition(); def.OnStart=new[]{new ActionEventDefinition{Event="npc.spectators"}}; Start(def); Frame(.2); loaded.LastPose.ThrowOnDispose=true; driver.OnDisable(); Check(!CrowdApi.Requested&&!driver.Playing&&!ActionPlaybackApi.IsPlaying,"Pose restoration failure still releases crowd, clears playback and publishes stop");
    calls.Clear(); def=Definition(); def.OnStart=new[]{E(null,"scope",lease:true)}; def.OnCancel=new[]{E(null,"wrong-player-cancel")}; Start(def); Frame(.2); PlayerController.Instance=new PlayerController(); driver.Update();
    Check(!driver.Playing&&activeLeases.Count==0&&!calls.Contains("wrong-player-cancel"),"Scene replacement releases old leases without invoking native callbacks on replacement player");
    calls.Clear(); def=Definition(); def.OnStart=new[]{E(null,"inactive-scope",lease:true)}; def.OnCancel=new[]{E(null,"inactive-cancel")}; Start(def); Frame(.2); player.gameObject.activeInHierarchy=false; Frame(.3);
    Check(!driver.Playing&&activeLeases.Count==0&&!calls.Contains("inactive-cancel"),"Inactive player skips native cancellation callbacks while releasing scopes");
    Action<string,bool> stopAtPublish=(_,active)=>{if(active)ActionPlaybackApi.Stop();}; ActionPlaybackApi.PlaybackChanged+=stopAtPublish;
    int notices=ExposureUnnoticed2.ObjectUI.InGame.MessagePanel.MessagePanelView.Messages.Count; Start(Definition()); ActionPlaybackApi.PlaybackChanged-=stopAtPublish;
    Check(!driver.Playing&&loaded.LastPose.Disposed&&notices==ExposureUnnoticed2.ObjectUI.InGame.MessagePanel.MessagePanelView.Messages.Count,"Playback listener can stop at startup without false failure or leaked pose");
    ActionEventBridge.Dispose(); def=Definition(); def.OnStart=new[]{new ActionEventDefinition{Event="npc.spectators"}}; Start(def); Check(!driver.Playing&&loaded.LastPose==null,"Missing optional event dependency is rejected before pose creation"); ActionEventBridge.Refresh();
    Check(!Plugin.LogSource.Messages.Any(m=>m.Contains("NullReferenceException")),"Driver integration produces no null-reference failures");
    var jsonOptions=new JsonSerializerOptions{PropertyNameCaseInsensitive=true}; jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    foreach(string guide in args)
        foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(guide),"```json\\s*\\n(.*?)```",System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            string json=match.Groups[1].Value; ActionEventSchema.ValidateJson(json);
            var example=JsonSerializer.Deserialize<ActionDefinition>(json,jsonOptions);
            using var prepared=new ActionEventRunner(example,Array.Empty<ActionEventDefinition>(),1000,()=>true,errors.Add);
            Check(true,"Player guide JSON parses and all event handlers/arguments validate");
        }
}
finally { driver?.OnDestroy(); ActionEventBridge.Dispose(); ActionRegistry.UnregisterProvider("validation"); Directory.Delete(root); }
Console.WriteLine($"PASS {count} assertions: production event scheduler, original player driver, final-frame/lifecycle dispatch, loops, scopes, reentrancy, native adapters, embedded clips and optional NPC integration");

sealed class Lease(Action release) : IDisposable { bool disposed; public void Dispose(){if(disposed)return;disposed=true;release();} }
sealed class Source : IActionSource { public float Duration=>1; public IActionPose CreatePose(GameObject target,ActionDefinition def)=>throw new NotSupportedException(); }

sealed class PreparedSource : IActionSource, IPreparedActionSource
{
    public bool Ready;public float Duration=>1;public bool IsPrepared(GameObject target)=>Ready;
    public IActionPose CreatePose(GameObject target,ActionDefinition definition)=>throw new NotSupportedException();
}

sealed class RootPose : IActionPose,IActionRootMotionPose,IActionFacePose
{
    public int FaceRestores; public void RestoreNativeFace()=>FaceRestores++;
    public bool Disposed;public Func<float,Vector3> Sample=t=>new Vector3(t,0,0);
    public int BoneCount=>1;public bool HasRootMotion=>true;public Vector3 RootDisplacement{get;private set;}
    public Vector3 LoopRootDisplacement=>new(1,0,0);
    public bool Apply(float t){RootDisplacement=Sample(t);return true;}public void Dispose(){Disposed=true;}
}

sealed class BodyPose : IActionPose,IActionBodyPose
{
    public bool InNative,Disposed,LastSuccess;public int RestoreUpdates,RestoredCorrections;
    public int BoneCount=>49;public bool Apply(float time)=>true;
    public void RestoreNativeBody()=>RestoreUpdates++;
    public void BeginNativeBody()=>InNative=true;
    public void EndNativeBody(bool success){LastSuccess=success;InNative=false;}
    public IDisposable PreserveNativeCorrection(ActionBoneCorrection correction)=>InNative?new Lease(()=>RestoredCorrections++):null;
    public bool SuppressKneeOffset=>InNative;
    public void Dispose()=>Disposed=true;
}
