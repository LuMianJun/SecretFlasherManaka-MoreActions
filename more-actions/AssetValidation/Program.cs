using System.Text;
using SFM.Actions;
using UnityEngine;

Boundary.MainThread=Environment.CurrentManagedThreadId;
int count=0;
void Check(bool ok,string label) { count++; if(!ok) throw new Exception(label); }
void Reject(Action action,string label) { bool failed=false; try{action();}catch{failed=true;} Check(failed,label); }
string root=Path.Combine(Path.GetTempPath(),"action-assets-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
string Wave(string name,bool stereo,bool floating,int samples)
{
    string path=Path.Combine(root,name+".wav");
    using var w=new BinaryWriter(File.Create(path));
    void Tag(string tag)=>w.Write(Encoding.ASCII.GetBytes(tag));
    int bytes=floating?4:2,channels=stereo?2:1;
    Tag("RIFF"); w.Write(36+samples*bytes); Tag("WAVE"); Tag("fmt "); w.Write(16); w.Write((ushort)(floating?3:1)); w.Write((ushort)channels); w.Write(44100); w.Write(44100*channels*bytes); w.Write((ushort)(channels*bytes)); w.Write((ushort)(bytes*8)); Tag("data"); w.Write(samples*bytes);
    for(int i=0;i<samples;i++) { if(floating)w.Write((i%19-9)/9f);else w.Write((short)(i%30000-15000)); } return path;
}
ActionDefinition Definition(string id,string path)=>new(){Id=id,RuntimeSource=new Source(),AudioPath=path,AudioFile=Path.GetFileName(path)};
void Drain(ActionDefinition definition)
{
    var timer=System.Diagnostics.Stopwatch.StartNew();
    while(!ActionAssets.TryGet(definition,out _)) { ActionAssets.Tick(); Thread.Sleep(1); if(timer.Elapsed.TotalSeconds>15)throw new Exception("Asset preparation did not finish"); }
}
try
{
    foreach(bool stereo in new[]{false,true}) foreach(bool floating in new[]{false,true})
    {
        int samples=524296; string path=Wave("samples-"+stereo+floating,stereo,floating,samples);
        var task=Task.Run(()=>WaveAudio.Decode(path)); var decoded=task.GetAwaiter().GetResult();
        Check(decoded.Channels==(stereo?2:1)&&decoded.Samples.Length==samples,"Worker decoding preserves channels and sample count");
        var upload=new WaveAudio.Upload(decoded); int steps=0; while(!upload.Step())steps++;
        Check(steps==2,"WAV upload advances one bounded block per step");
        for(int i=0;i<samples;i++) if(Math.Abs(upload.Clip.Data[i]-decoded.Samples[i])>1e-7)throw new Exception("Audio upload offset/tail corruption at "+i);
        Check(upload.Clip.MaxTransfer<=262144,"Main-thread transfer is bounded to one MiB");
        var clip=upload.Clip; upload.Dispose(); Check(clip.Destroyed,"Interrupted/owned upload releases its clip");
    }
    string cachedPath=Wave("cached",true,false,524296); var cached=Definition("cached",cachedPath);
    ActionRegistry.Set(cached); Check(!ActionAssets.TryGet(cached,out _),"Playback query never performs a synchronous first load");
    Drain(cached); Check(ActionAssets.TryGet(cached,out var ready)&&ready.Audio!=null,"Ready assets are published after full upload");
    File.Delete(cachedPath); Check(ActionAssets.TryGet(cached,out var same)&&ReferenceEquals(ready,same),"Ready playback reuses assets without reopening the file");
    var removed=ready.Audio; ActionRegistry.Set(); ActionAssets.Tick(); Check(removed.Destroyed,"Provider removal releases cached audio");
    string bad=Path.Combine(root,"bad.wav"); File.WriteAllText(bad,"broken");
    var broken=Definition("broken",bad); var good=Definition("priority",Wave("priority",false,true,16));
    ActionRegistry.Set(broken,good); ActionAssets.Tick(); Check(!ActionAssets.TryGet(good,out _),"A pending requested action is explicitly queued");
    Drain(good); Check(ActionAssets.TryGet(good,out _),"Requested action is prepared despite an unrelated invalid pack");
    for(int i=0;i<100;i++){ActionAssets.Tick();Thread.Sleep(1);}
    Reject(()=>ActionAssets.TryGet(broken,out _),"Invalid audio reports a stable failure instead of retrying every frame");
    var silent=Definition("silent",""); silent.AudioFile=""; ActionRegistry.Set(silent); ActionAssets.Tick(); Check(ActionAssets.TryGet(silent,out var quiet)&&quiet.Audio==null,"Silent runtime actions need no decoder");
    Reject(()=>Task.Run(()=>WaveAudio.Decode(bad)).GetAwaiter().GetResult(),"Malformed WAV is rejected on the worker");
    // Exercise the actual native job and cache integration without a native engine.
    string mp3Path=Path.Combine(root,"中文 音乐#1.MP3");File.WriteAllText(mp3Path,"decoder fixture");
    var mp3=Definition("mp3",mp3Path);
    var request=new UnityEngine.Networking.UnityWebRequest{Clip=AudioClip.Create("decoded",44100,2,44100,false)};
    UnityEngine.Networking.UnityWebRequestMultimedia.Factory=_=>request;
    ActionRegistry.Set(mp3);ActionAssets.Tick();
    Check(request.Sent&&!request.Disposed&&!ActionAssets.TryGet(mp3,out _),"MP3 preparation is asynchronous and does not expose an incomplete clip");
    var nativeCall=UnityEngine.Networking.UnityWebRequestMultimedia.Calls.Last();
    Check(nativeCall.Type==AudioType.MPEG&&new Uri(nativeCall.Uri).LocalPath==mp3Path&&nativeCall.Uri.Contains("%23"),"MP3 uses the native MPEG decoder and safely encoded local Unicode/file URLs");
    request.isDone=true;request.result=UnityEngine.Networking.UnityWebRequest.Result.Success;
    ActionAssets.Tick();Check(ActionAssets.TryGet(mp3,out var mp3Ready)&&mp3Ready.Audio==request.Clip&&request.Disposed,"Completed MP3 ownership transfers to the existing asset cache");
    File.Delete(mp3Path);Check(ActionAssets.TryGet(mp3,out var mp3Again)&&ReferenceEquals(mp3Again,mp3Ready),"Repeated MP3 playback reuses decoded audio");
    ActionRegistry.Set();ActionAssets.Tick();Check(request.Clip.Destroyed,"Removing MP3 provider destroys its cached clip");

    var pending=Definition("pending-mp3",Path.Combine(root,"pending.mp3"));
    var interrupted=new UnityEngine.Networking.UnityWebRequest();
    UnityEngine.Networking.UnityWebRequestMultimedia.Factory=_=>interrupted;
    ActionRegistry.Set(pending);ActionAssets.Tick();ActionAssets.Clear();
    Check(interrupted.Aborted&&interrupted.Disposed,"Clearing a pending MP3 aborts and disposes the native request");

    var failed=Definition("failed-mp3",Path.Combine(root,"failed.mp3"));
    var failedRequest=new UnityEngine.Networking.UnityWebRequest();
    UnityEngine.Networking.UnityWebRequestMultimedia.Factory=_=>failedRequest;
    ActionRegistry.Set(failed);ActionAssets.Tick();
    failedRequest.isDone=true;failedRequest.result=UnityEngine.Networking.UnityWebRequest.Result.DataProcessingError;failedRequest.error="invalid MPEG";
    ActionAssets.Tick();Reject(()=>ActionAssets.TryGet(failed,out _),"Invalid MP3 reports a stable decoding failure");
    int attempts=UnityEngine.Networking.UnityWebRequestMultimedia.Calls.Count;ActionAssets.Tick();
    Check(failedRequest.Disposed&&UnityEngine.Networking.UnityWebRequestMultimedia.Calls.Count==attempts,"Failed MP3 requests are disposed without per-frame retries");

    var loading=Definition("loading-mp3",Path.Combine(root,"loading.mp3"));
    var loadingRequest=new UnityEngine.Networking.UnityWebRequest{Clip=AudioClip.Create("loading",44100,1,44100,false)};
    UnityEngine.Networking.UnityWebRequestMultimedia.Factory=_=>loadingRequest;
    ActionRegistry.Set(loading);ActionAssets.Tick();loadingRequest.isDone=true;loadingRequest.result=UnityEngine.Networking.UnityWebRequest.Result.Success;
    loadingRequest.Clip.loadState=AudioDataLoadState.Loading;ActionAssets.Tick();
    Check(!ActionAssets.TryGet(loading,out _),"Native request completion still waits for playable audio data");
    loadingRequest.Clip.loadState=AudioDataLoadState.Loaded;ActionAssets.Tick();
    Check(ActionAssets.TryGet(loading,out _),"Native loading completion publishes the prepared clip");
    var empty=Definition("empty-mp3",Path.Combine(root,"empty.mp3"));
    var emptyRequest=new UnityEngine.Networking.UnityWebRequest{Clip=AudioClip.Create("empty",1,1,44100,false),isDone=true,result=UnityEngine.Networking.UnityWebRequest.Result.Success};emptyRequest.Clip.length=0;
    UnityEngine.Networking.UnityWebRequestMultimedia.Factory=_=>emptyRequest;ActionRegistry.Set(empty);ActionAssets.Tick();ActionAssets.Tick();
    Reject(()=>ActionAssets.TryGet(empty,out _),"Empty decoded MP3 is never published");
    Check(emptyRequest.Disposed&&emptyRequest.Clip.Destroyed,"Invalid decoded MP3 releases the owned clip and request");
    var held=Definition("held-mp3",Path.Combine(root,"held.mp3"));
    var heldRequest=new UnityEngine.Networking.UnityWebRequest{Clip=AudioClip.Create("held",44100,1,44100,false)};
    UnityEngine.Networking.UnityWebRequestMultimedia.Factory=_=>heldRequest;ActionRegistry.Set(held);ActionAssets.Tick();
    heldRequest.isDone=true;heldRequest.result=UnityEngine.Networking.UnityWebRequest.Result.Success;heldRequest.Clip.loadState=AudioDataLoadState.Loading;ActionAssets.Tick();ActionAssets.Clear();
    Check(heldRequest.Disposed&&heldRequest.Clip.Destroyed,"Cancellation after content creation releases unpublished MP3 data");
    Check(NativeAudioLoad.Supports("UPPER.MP3")&&!NativeAudioLoad.Supports("BGM.wav"),"MP3 routing is case-insensitive and leaves the WAV decoder unchanged");
    Console.WriteLine($"PASS {count} assertions: WAV worker decode/upload, native MP3 async preparation, cache reuse, failure isolation and cleanup");
}
finally {ActionAssets.Clear();Directory.Delete(root,true);}

public static class Boundary
{
    public static int MainThread;
    public static void UnityCall() { if(Environment.CurrentManagedThreadId!=MainThread)throw new Exception("Unity API was called by a worker"); }
}
namespace UnityEngine
{
    public class Object { public bool Destroyed; public static void Destroy(Object value){Boundary.UnityCall();value.Destroyed=true;} }
    public sealed class GameObject {}
    public struct Vector3 { public float x,y,z; }
    public enum HideFlags { HideAndDontSave }
    public enum AudioDataLoadState { Loaded,Loading,Failed,Unloaded }
    public sealed class AudioClip : Object
    {
        public string name; public float[] Data; public int Channels,MaxTransfer; public float length; public HideFlags hideFlags; public AudioDataLoadState loadState=AudioDataLoadState.Loaded;
        public static AudioClip Create(string name,int frames,int channels,int rate,bool stream){Boundary.UnityCall();return new(){Data=new float[frames*channels],Channels=channels,length=frames/(float)rate};}
        public bool SetData(float[] data,int frameOffset){Boundary.UnityCall();MaxTransfer=Math.Max(MaxTransfer,data.Length);Array.Copy(data,0,Data,frameOffset*Channels,data.Length);return true;}
        public bool LoadAudioData()=>true;
    }
    public sealed class AnimationClip : Object { public float length=1; public HideFlags hideFlags;public bool isHumanMotion; }
    public sealed class AssetBundle
    {
        public static AssetBundle LoadFromFile(string path){Boundary.UnityCall();return new();}
        public T LoadAsset<T>(string path) where T:new()=>new(); public void Unload(bool all){}
    }
}
namespace SFM.Actions
{
    public sealed class ActionDefinition {public string Id,AudioFile,AudioPath,AudioAsset="",BundlePath="",ClipAsset="";public IActionSource RuntimeSource;}
    public static class ActionRegistry
    {
        static ActionDefinition[] items=[];public static int Revision;
        public static void Set(params ActionDefinition[] values){items=values;Revision++;}
        public static ActionDefinition[] GetAll()=>items;public static ActionDefinition Get(string id)=>items.FirstOrDefault(x=>x.Id==id);
    }
    public sealed class Source : IActionSource {public float Duration=>1;public IActionPose CreatePose(GameObject target,ActionDefinition definition)=>throw new NotSupportedException();}
    internal sealed class ActionPose : IActionPose { public ActionPose(GameObject target, LoadedAction loaded,bool native=false) {} public int BoneCount=>0;public bool Apply(float t)=>true;public void Dispose(){} }
    public sealed class Logger {public void LogInfo(string text){}public void LogWarning(string text){} }
    public static class Plugin {public static Logger LogSource=new();}
}
