using SFM.Actions;
using UnityEngine;
using UnityEngine.Playables;

if(args.Length==3&&args[0]=="--stats-interop")
{
    try { StatsInteropContract.Verify(args[1],args[2]); }
    catch(Exception error) { Console.Error.WriteLine(error.Message);Environment.ExitCode=1; }
    return;
}

if(args.Length==4&&args[0]=="--settings-interop")
{
    try { SettingsInteropContract.Verify(args[1],args[2],args[3]); }
    catch(Exception error) { Console.Error.WriteLine(error.Message);Environment.ExitCode=1; }
    return;
}

if(args.Length==3&&args[0]=="--interop")
{
    try { InteropContract.Verify(args[1],args[2]); }
    catch(Exception error) { Console.Error.WriteLine(error.Message);Environment.ExitCode=1; }
    return;
}

int assertions=0;
void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message);}
var root=Path.Combine(Path.GetTempPath(),"actions-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    string Pack(string relative,params (string Name,AnimationClip Clip)[] clips)
    {
        var path=Path.GetFullPath(Path.Combine(root,relative));Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,"UnityFS\0payload"u8.ToArray());
        var value=new AssetBundle();foreach(var item in clips)value.Clips.Add(item.Name,item.Clip);AssetBundle.Files.Add(path,value);return path;
    }
    var first=new AnimationClip{name="Female_Idle_Base",isHumanMotion=true};var second=new AnimationClip{name="Female_Idle_Base"};
    Pack("A/pack.bundle",("a.anim",first));Pack("B/pack.bundle",("b.anim",second));
    ActionRegistry.Register(new ActionDefinition{Id="test.a",DisplayName="A",Bundle="A/pack.bundle",ClipAsset="Female_Idle_Base",ShowInRing=false},root,"test");
    ActionRegistry.Register(new ActionDefinition{Id="test.b",DisplayName="B",Bundle="B/pack.bundle",ClipAsset="Female_Idle_Base",ShowInRing=false},root,"test");
    var external=new AnimationClip{name="V-sign",length=2};
    ActionRegistry.RegisterRuntime(new ActionDefinition{Id="test.v",DisplayName="V-sign"},new AnimatorActionSource(external,"V"),root,"test");
    ActionAssets.Loaded["test.a"]=new LoadedAction{Definition=ActionRegistry.Get("test.a"),Clip=first};
    ActionAssets.Loaded["test.b"]=new LoadedAction{Definition=ActionRegistry.Get("test.b"),Clip=second};
    Check(ActionRegistry.Entries.Count==3,"Actions collection registers every action");
    Check(ActionRegistry.Entries.Values.Where(e=>e.Definition.ClipAsset=="Female_Idle_Base").All(e=>!e.Definition.ShowInRing),"Replacement resources stay out of the ring");
    Check(ActionRegistry.Entries.Values.Single(e=>e.Definition.Id=="test.v").Definition.ShowInRing,"New poses remain available in the ring");
    var source=(AnimatorActionSource)ActionRegistry.Entries["test.v"].Source;
    Check(((ActionPose)source.CreatePose(new(),new())).Native,"Imported clips select the native Animator sampler");
    Check(((ActionPose)new LoadedAction{Definition=new(),Clip=first,Audio=null}.CreatePose(new())).Native,"JSON Humanoid clips use the same Avatar backend as imported bundles");
    Check(!((ActionPose)new LoadedAction{Definition=new(),Clip=second}.CreatePose(new())).Native,"JSON Generic clips preserve their canonical transform backend");
    foreach(var spelling in new[]{"bundle","Bundle","bUnDlE"})
    {
        var folder=Path.Combine(root,"owner-"+spelling);Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"pose.bundle"),"UnityFS\0payload"u8.ToArray());
        File.WriteAllText(Path.Combine(folder,"pose.action.json"),"{\""+spelling+"\":\"pose.bundle\"}");
        var discover=typeof(ActionBundleImport).GetMethod("Discover",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Check(((string[])discover.Invoke(null,new object[]{folder})).Length==0,"JSON owns its bundle independently of field casing: "+spelling);
    }
    var original=new Animator{avatar=new(),runtimeAnimatorController=new(),applyRootMotion=false};var controller=original.runtimeAnimatorController;
    var proxy=new GameObject();var sampler=new AnimatorClipSampler(proxy,original,first);var graph=PlayableGraph.Last;
    Check(graph.Manual&&graph.Playing&&proxy.active,"Sampling graph is active but advances only manually");
    Check(graph.Target==proxy.animator&&proxy.animator.avatar==original.avatar,"Retargeting uses the player's Avatar on a separate Animator");
    Check(!proxy.animator.fireEvents,"Only the shared event runner may dispatch embedded sounds");
    sampler.Sample(0);sampler.Sample(1.5f);sampler.Sample(1.5f);
    Check(graph.Time==1.5,"Repeated paused sampling never advances graph time");
    sampler.Sample(.25f);Check(graph.Time==.25,"Loop rewind resets graph time before evaluating the next cycle");
    Check(original.runtimeAnimatorController==controller&&!original.applyRootMotion,"Player controller and root-motion ownership are untouched");
    sampler.Dispose();sampler.Dispose();Check(!graph.Valid,"Stopping destroys the graph idempotently");
    Reject(()=>sampler.Sample(0),"Disposed samplers cannot evaluate live output");
    Reject(()=>new AnimatorClipSampler(new(),new Animator(),first),"Missing Avatar fails before taking any player ownership");
    UnityEngine.Animations.AnimationClipPlayable.FailCreation=true;
    Reject(()=>new AnimatorClipSampler(new(),original,first),"Native factory failure is surfaced without invoking the stripped constructor");
    Check(!PlayableGraph.Last.Valid,"Partially constructed native graphs are destroyed on factory failure");
    UnityEngine.Animations.AnimationClipPlayable.FailCreation=false;
    UnityEngine.Animations.AnimationPlayableGraphExtensions.FailCreation=true;
    Reject(()=>new AnimatorClipSampler(new(),original,first),"Native output factory failure is surfaced without output wrapper helpers");
    Check(!PlayableGraph.Last.Valid,"Output creation failure releases the partially built graph");
    UnityEngine.Animations.AnimationPlayableGraphExtensions.FailCreation=false;
    // Production replacement manager over native controller/config boundaries.
    var config=new BepInEx.Configuration.ConfigFile();
    ReplacementService.Configure(config);ReplacementSettings.Register();
    var enable=(BepInEx.Configuration.ConfigEntry<bool>)config.Values["Replacements.Enabled"];
    var saved=(BepInEx.Configuration.ConfigEntry<string>)config.Values["Replacements.Selections"];
    var nativeClip=new AnimationClip{name="Female_Idle_Base"};var otherClip=new AnimationClip{name="Unchanged"};
    var baseController=new RuntimeAnimatorController{Originals=new[]{nativeClip,otherClip}};
    var actor=new Animator{avatar=new(),runtimeAnimatorController=baseController};
    actor.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
    actor.States[1]=new(){fullPathHash=20,normalizedTime=float.NaN};
    var faceNode=new GameObject{renderer=new(){sharedMesh=new(){blendShapeCount=2},Values=new[]{62f,17f}}};
    var bodyNode=new GameObject{renderer=new(){sharedMesh=new(){blendShapeCount=1},Values=new[]{43f}}};
    actor.transform.Children["Body.001"]=faceNode.transform;actor.transform.Children["Selestia_body"]=bodyNode.transform;
    float markerHeight=1.1661667f;bool evaluatedAlways=false;
    actor.OnEvaluate=()=>{
        evaluatedAlways|=actor.cullingMode==AnimatorCullingMode.AlwaysAnimate;
        markerHeight=1.137f;actor.Weights[1]=0;
        Array.Fill(faceNode.renderer.Values,0);Array.Fill(bodyNode.renderer.Values,0);
    };
    actor.isInitialized=false;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==baseController&&!SFM.ModSettings.ModOptions.Rows.Keys.Any(k=>k.StartsWith("replacement.variant.")),"Player initialization cannot cache an incomplete clip catalog");
    actor.isInitialized=true;
    NativeAnimationReplacements.Tick(actor);
    var aId="test.a";var bId="test.b";
    var aRow=(Read:(Func<bool>)(()=>ReplacementService.Catalog.Selected("Female_Idle_Base")==aId),Write:(Action<bool>)(v=>ReplacementService.SelectVariant(aId,v)));
    var bRow=(Read:(Func<bool>)(()=>ReplacementService.Catalog.Selected("Female_Idle_Base")==bId),Write:(Action<bool>)(v=>ReplacementService.SelectVariant(bId,v)));
    Check(actor.runtimeAnimatorController==baseController&&!enable.Value,"Replacement defaults never change the actor controller");
    Check(actor.UpdateCalls==0,"Disabled detached replacements do not force native evaluation");
    Check(ReplacementService.Catalog.Options.Count==2&&!SFM.ModSettings.ModOptions.Rows.Keys.Any(k=>k.StartsWith("replacement.variant.")),"Replacement candidates belong to the browser instead of expanding the settings page");
    aRow.Write(true);enable.Value=true;
    var playerState=ExposureUnnoticed2.Object3D.Player.Scripts.PlayerController.Instance.Pca.PlayerState;
    playerState.IsSit=true;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==baseController,"Chair sitting defers controller attachment even with IsCrouch=false");
    playerState.IsSit=false;playerState.IsCrouch=true;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==baseController,"Crouching defers controller attachment");
    playerState.IsCrouch=false;playerState.CurrentAction.Type=50001;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==baseController,"An active native pose defers controller attachment");
    playerState.CurrentAction.Type=-1;actor.States[1].NotIdle=true;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==baseController,"Native pose exits must reach Action.NoMotion before attachment");
    actor.States[1].NotIdle=false;NativeAnimationReplacements.Tick(actor);
    var layer=(AnimatorOverrideController)actor.runtimeAnimatorController;
    int bindingAssignments=actor.ControllerAssignments;
    Check(layer.runtimeAnimatorController==baseController&&layer.Overrides[nativeClip]==first,"Selected native slot uses a separate override around the existing controller");
    Check(layer.Overrides[otherClip]==null,"Unselected slots inherit the existing controller");
    Check(actor.States[0].fullPathHash==10&&actor.States[0].normalizedTime==.4f&&actor.Weights[1]==.5f,"Attaching an override preserves state time and layer weights");
    Check(actor.PlayCalls==0&&float.IsNaN(actor.States[1].normalizedTime),"Native time-driven layers are not replayed, including a legitimate NaN normalized time");
    Check(actor.UpdateCalls==1&&markerHeight==1.137f,"Controller attachment forces one zero-time native pose evaluation before additive correction");
    Check(evaluatedAlways&&actor.cullingMode==AnimatorCullingMode.CullUpdateTransforms,"One-shot native evaluation is not culled and restores the prior culling policy");
    Check(faceNode.renderer.Values.SequenceEqual(new[]{62f,17f})&&bodyNode.renderer.Values[0]==43f,"Native refresh preserves customization on both fixed base-model meshes");
    ActionRegistry.RegisterAlias("legacy-a",aId);saved.Value="{\"Female_Idle_Base\":\"legacy-a\"}";NativeAnimationReplacements.Tick(actor);
    Check(ReplacementService.Catalog.Selected("Female_Idle_Base")==aId&&saved.Value.Contains(aId),"Saved legacy replacement IDs migrate when the alias becomes available");
    int notifications=layer.Notifications;
    int evaluations=actor.UpdateCalls;
    bRow.Write(true);NativeAnimationReplacements.Tick(actor);
    Check(!aRow.Read()&&bRow.Read()&&layer.Overrides[nativeClip]==second,"Enabling another source for the same slot unchecks the former source");
    Check(layer.Notifications==notifications+1,"Changing a selection batches clip writes into one notification");
    Check(actor.UpdateCalls==evaluations+1,"Editing clips on the already attached override also evaluates once");
    ActionPlaybackApi.IsPlaying=true;aRow.Write(true);NativeAnimationReplacements.Tick(actor);
    Check(layer.Overrides[nativeClip]==second,"Independent playback defers native replacement changes");
    ActionPlaybackApi.IsPlaying=false;NativeAnimationReplacements.Tick(actor);
    Check(layer.Overrides[nativeClip]==first,"Pending selection applies after independent playback ends");
    ExposureUnnoticed2.Object3D.IngameManager.InGameUiManager.Instance.IsOpenSomeUi=true;bRow.Write(true);NativeAnimationReplacements.Tick(actor);
    Check(layer.Overrides[nativeClip]==first,"Settings changes wait for the menu to close");
    ExposureUnnoticed2.Object3D.IngameManager.InGameUiManager.Instance.IsOpenSomeUi=false;NativeAnimationReplacements.Tick(actor);
    Check(layer.Overrides[nativeClip]==second,"Closing settings applies the pending native replacement");
    actor.Transition=true;enable.Value=false;evaluations=actor.UpdateCalls;markerHeight+=.0291667f;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==layer,"Controller restoration waits for native transitions");
    actor.Transition=false;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==layer&&layer.Overrides[nativeClip]==nativeClip&&layer.runtimeAnimatorController==baseController&&bRow.Read(),"Disabling restores original clips without detaching the binding owner or erasing choices");
    Check(actor.UpdateCalls==evaluations+1&&markerHeight==1.137f,"Restoring original clips evaluates once while retaining native marker/default bindings");
    Check(actor.PlayCalls==0&&actor.ControllerAssignments==bindingAssignments,"Disabling never replays states or changes the live controller identity");
    enable.Value=true;NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==layer&&layer.Overrides[nativeClip]==second,"Re-enabling reuses the selection and owned override");
    for(int cycle=0;cycle<3;cycle++)
    {
        enable.Value=false;NativeAnimationReplacements.Tick(actor);
        Check(actor.runtimeAnimatorController==layer&&layer.Overrides[nativeClip]==nativeClip,"Repeated disable preserves one native binding owner with explicit original clips");
        enable.Value=true;NativeAnimationReplacements.Tick(actor);
    }
    Check(actor.ControllerAssignments==bindingAssignments&&actor.PlayCalls==0,"Repeated enable/disable never recreates the native controller binding");
    var temporary=new AnimatorOverrideController{runtimeAnimatorController=layer};actor.runtimeAnimatorController=temporary;Time.unscaledTime+=1;
    aRow.Write(true);NativeAnimationReplacements.Tick(actor);
    Check(actor.runtimeAnimatorController==temporary,"Another controller wrapping ours is never forcibly reclaimed");
    actor.runtimeAnimatorController=layer;NativeAnimationReplacements.Tick(actor);
    Check(layer.Overrides[nativeClip]==first,"Returning native ownership applies deferred changes");
    var nextClip=new AnimationClip{name="Female_Idle_Base"};var nextBase=new RuntimeAnimatorController{Originals=new[]{nextClip}};
    var nextActor=new Animator{avatar=new(),runtimeAnimatorController=nextBase};NativeAnimationReplacements.Tick(nextActor);
    Check(actor.runtimeAnimatorController==layer&&layer.GetClip(nativeClip,true)==nativeClip&&layer.GetClip(otherClip,true)==otherClip&&nextActor.runtimeAnimatorController is AnimatorOverrideController,"Scene/actor replacement neutralizes the old layer and binds the new actor");
    Check(!layer.destroyed&&(layer.hideFlags&HideFlags.DontUnloadUnusedAsset)==0,"A released layer still referenced by a live animator is not destroyed or permanently pinned");
    Check(((AnimatorOverrideController)nextActor.runtimeAnimatorController).Overrides[nextClip]==first,"Scene rebinding uses new original clip objects with persisted resource IDs");
    saved.Value="{\"Female_Idle_Base\":\"missing-resource\"}";NativeAnimationReplacements.Tick(nextActor);
    var nextLayer=(AnimatorOverrideController)nextActor.runtimeAnimatorController;
    Check(nextLayer.runtimeAnimatorController==nextBase&&nextLayer.Overrides[nextClip]==nextClip,"Missing selected assets restore the original clip without selecting another pack or detaching bindings");
    NativeAnimationReplacements.Release();NativeAnimationReplacements.Release();
    Check(nextActor.runtimeAnimatorController==nextLayer&&nextLayer.GetClip(nextClip,true)==nextClip&&!nextLayer.destroyed&&(nextLayer.hideFlags&HideFlags.DontUnloadUnusedAsset)==0,"Cleanup is idempotent and leaves only an unpinned neutral layer owned by the native animator");
    var extraNative=new AnimationClip{name="AnotherNativeSlot"};var extraSource=new AnimationClip{name="AnotherNativeSlot"};
    ActionRegistry.Register(new ActionDefinition{Id="other-native-id",DisplayName="Other",ClipAsset="AnotherNativeSlot",ShowInRing=false},root,"test");
    ActionAssets.Loaded["other-native-id"]=new LoadedAction{Definition=ActionRegistry.Get("other-native-id"),Clip=extraSource};
    var allActor=new Animator{avatar=new(),runtimeAnimatorController=new RuntimeAnimatorController{Originals=new[]{extraNative}}};
    saved.Value="{\"AnotherNativeSlot\":\"other-native-id\"}";NativeAnimationReplacements.Tick(allActor);
    Check(allActor.runtimeAnimatorController is AnimatorOverrideController all&&all.Overrides[extraNative]==extraSource,"Replacement capability is not restricted to a list of 44 imported motions or named animation kinds");
    var inner=(AnimatorOverrideController)allActor.runtimeAnimatorController;
    var externalClip=new AnimationClip{name="External owner"};
    var outer=new AnimatorOverrideController{runtimeAnimatorController=inner};outer.Overrides[extraNative]=externalClip;
    allActor.runtimeAnimatorController=outer;
    NativeAnimationReplacements.Release();
    Check(allActor.runtimeAnimatorController==outer&&outer.Overrides[extraNative]==externalClip&&inner.Overrides[extraNative]==extraNative,"Releasing a layer wrapped by another owner only neutralizes its own overrides");
    Check(!inner.destroyed&&(inner.hideFlags&HideFlags.DontUnloadUnusedAsset)==0,"External-wrapper references retain a valid neutral layer without a permanent pin");
    var failedActor=new Animator{avatar=new(),runtimeAnimatorController=new(),cullingMode=AnimatorCullingMode.CullUpdateTransforms,ThrowEvaluation=true};
    var failedShape=new GameObject{renderer=new(){sharedMesh=new(){blendShapeCount=1},Values=new[]{71f}}};
    failedActor.transform.Children["Body.001"]=failedShape.transform;
    failedActor.OnEvaluate=()=>{failedShape.renderer.Values[0]=0;failedActor.Weights[1]=0;};
    Reject(()=>{using var refresh=new NativeAnimatorRefresh(failedActor);refresh.Evaluate(failedActor.runtimeAnimatorController);},"Native evaluation errors remain visible");
    Check(failedActor.cullingMode==AnimatorCullingMode.CullUpdateTransforms&&failedActor.Weights[1]==.5f&&failedShape.renderer.Values[0]==71f,"Evaluation failure still restores appearance, layer weights and culling");
    var transferred=new Animator{avatar=new(),runtimeAnimatorController=new(),cullingMode=AnimatorCullingMode.CullUpdateTransforms};
    var transferredShape=new GameObject{renderer=new(){sharedMesh=new(){blendShapeCount=1},Values=new[]{19f}}};transferred.transform.Children["Body.001"]=transferredShape.transform;
    var foreign=new RuntimeAnimatorController();
    transferred.OnEvaluate=()=>{transferred.runtimeAnimatorController=foreign;transferred.Weights[1]=.8f;transferredShape.renderer.Values[0]=91f;transferred.cullingMode=AnimatorCullingMode.CullCompletely;};
    using(var refresh=new NativeAnimatorRefresh(transferred))refresh.Evaluate(transferred.runtimeAnimatorController);
    Check(transferred.runtimeAnimatorController==foreign&&transferred.Weights[1]==.8f&&transferredShape.renderer.Values[0]==91f&&transferred.cullingMode==AnimatorCullingMode.CullCompletely,"A different controller taking ownership during evaluation keeps its own appearance and policy");
    var ambiguousBase=new RuntimeAnimatorController{Originals=new[]{new AnimationClip{name="Female_Idle_Base"},new AnimationClip{name="Female_Idle_Base"}}};
    var ambiguousActor=new Animator{avatar=new(),runtimeAnimatorController=ambiguousBase};saved.Value="{\"Female_Idle_Base\":\""+aId+"\"}";NativeAnimationReplacements.Tick(ambiguousActor);
    Check(ambiguousActor.runtimeAnimatorController==ambiguousBase&&!SFM.ModSettings.ModOptions.Rows.Keys.Any(k=>k.StartsWith("replacement.variant.")),"Distinct original clips sharing a name are excluded instead of choosing an arbitrary target");
    var unreferencedLayer=(AnimatorOverrideController)UnityEngine.Object.LastInstantiated;
    NativeAnimationReplacements.Release();
    Check(unreferencedLayer.destroyed,"A detached layer with no native owner is destroyed on release");
    var inheritedOriginal=new AnimationClip{name="Female_Idle_Base"};var inheritedOther=new AnimationClip{name="Unchanged"};
    var inheritedBase=new RuntimeAnimatorController{Originals=new[]{inheritedOriginal,inheritedOther}};
    var parentOverride=new AnimatorOverrideController{runtimeAnimatorController=inheritedBase};
    var parentIdle=new AnimationClip{name="Parent idle"};var parentOther=new AnimationClip{name="Parent other"};
    parentOverride.Overrides[inheritedOriginal]=parentIdle;parentOverride.Overrides[inheritedOther]=parentOther;
    var inheritedActor=new Animator{avatar=new(),runtimeAnimatorController=parentOverride};
    saved.Value="{\"Female_Idle_Base\":\""+aId+"\"}";NativeAnimationReplacements.Tick(inheritedActor);
    var inheritedOwned=(AnimatorOverrideController)inheritedActor.runtimeAnimatorController;
    Check(inheritedOwned.GetClip(inheritedOriginal,true)==first&&inheritedOwned.Overrides[inheritedOther]==null,"Only selected slots override an existing parent layer");
    var changedParentOther=new AnimationClip{name="Changed parent other"};parentOverride.Overrides[inheritedOther]=changedParentOther;
    Check(inheritedOwned.GetClip(inheritedOther,true)==changedParentOther,"Never-held slots continue inheriting changes from an existing parent override");
    enable.Value=false;NativeAnimationReplacements.Tick(inheritedActor);
    Check(inheritedActor.runtimeAnimatorController==inheritedOwned&&inheritedOwned.Overrides[inheritedOriginal]==parentIdle,"Disabling explicitly restores the effective baseline clip, including an existing parent's override");
    Check(inheritedOwned.Overrides[inheritedOther]==null&&inheritedOwned.GetClip(inheritedOther,true)==changedParentOther,"Restoring held slots does not freeze unrelated parent-controlled slots");
    NativeAnimationReplacements.Release();
    Check(inheritedActor.runtimeAnimatorController==inheritedOwned&&inheritedOwned.Overrides[inheritedOriginal]==parentIdle&&!inheritedOwned.destroyed,"Release keeps valid inherited native bindings with effective baseline clips");
    var persisted=new ReplacementSelection();persisted.Set("slot","A",true);persisted.Set("slot","B",true);persisted.Set("slot","A",false);
    var restored=new ReplacementSelection();restored.Load(persisted.Serialize());
    Check(restored.Get("slot")=="B","Selection persistence stores one resource ID per slot");
    Reject(()=>restored.Load("broken json"),"Malformed replacement config is rejected");
    Check(restored.Get("slot")=="B","Malformed config cannot erase a previously valid selection");
    Check(GameAnimationNames.Names.Count()==175,"All 175 original player-controller animation identifiers have labels");
    for(int language=0;language<5;language++)
    {
        ExposureUnnoticed2.Scripts.InGame.GameState.OptionData.Language=language;
        foreach(var name in GameAnimationNames.Names)
            Check(!string.IsNullOrWhiteSpace(ActionNames.Clip(name))&&ActionNames.Clip(name)!=name,"Native animation label coverage: "+language+" / "+name);
        Check(ActionNames.Clip("CatPose")=="CatPose"&&ActionNames.Clip("V-sign")=="V-sign"&&ActionNames.Clip("Author_Custom")=="Author_Custom","External motion names remain unchanged in language "+language);
    }
    ActionSettingsApi.Initialize();
    Check(SFM.ModSettings.ModOptions.Dedicated&&!string.IsNullOrWhiteSpace(SFM.ModSettings.ModOptions.PageTitle()),"Shared settings API registers a dedicated localized page");
    var autoClip=new AnimationClip{name="Pose",isHumanMotion=true};
    Pack("Auto/MyPack/pose.bundle",("clip.anim",autoClip));
    File.WriteAllText(Path.Combine(root,"Auto/MyPack/pose.bundle.action.json"),"{\"displayName\":\"Short label\",\"loop\":false}");
    ActionBundleImport.Begin(Path.Combine(root,"Auto"),"auto-test");
    for(int step=0;step<500&&ActionRegistry.Get("MyPack/Pose")==null;step++){ActionBundleImport.Tick();Thread.Sleep(1);}
    var autoAction=ActionRegistry.Get("MyPack/Pose");
    Check(autoAction?.DisplayName=="Short label"&&!autoAction.Loop,"Sparse sidecar overrides only explicit values after loading real clip metadata");
    Check(autoAction?.RuntimeSource is AnimatorActionSource,"No-JSON bundles use folder/clip identities");
    Check(ReferenceEquals(ActionRegistry.Get("bundle:MyPack/pose.bundle:clip.anim"),autoAction),"Legacy bundle paths resolve to the new action identity");
    ActionBundleImport.Clear();
    Check(ActionRegistry.Get("MyPack/Pose")==null&&autoClip.destroyed,"Imported clip cleanup follows the new identity");
    ActionRegistry.Entries.Clear();Check(ActionRegistry.Entries.Count==0,"Provider cleanup unregisters actions");
    Console.WriteLine($"PASS {assertions} assertions: action collection, replacements, sampling, multilingual labels and cleanup; offline boundaries, not rendered game verification");
}
finally {Directory.Delete(root,true);}
