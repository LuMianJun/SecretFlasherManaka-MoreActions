using Seleka2.Actions;
using UnityEngine;
using UnityEngine.Playables;

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
    Console.WriteLine($"PASS {assertions} assertions: bundle source selection, JSON ownership, native sampling and cleanup");
}
finally {Directory.Delete(root,true);}
