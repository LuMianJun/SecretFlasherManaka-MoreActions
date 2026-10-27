using System.Text;
using Seleka2.Actions;
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
    Console.WriteLine($"PASS {count} assertions: production worker decode, exact PCM16/float32 mono/stereo upload, bounded frames, cache reuse, priority, bad-pack isolation and cleanup");
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
    public enum AudioDataLoadState { Loaded,Loading,Failed }
    public sealed class AudioClip : Object
    {
        public float[] Data; public int Channels,MaxTransfer; public float length; public HideFlags hideFlags; public AudioDataLoadState loadState=AudioDataLoadState.Loaded;
        public static AudioClip Create(string name,int frames,int channels,int rate,bool stream){Boundary.UnityCall();return new(){Data=new float[frames*channels],Channels=channels,length=frames/(float)rate};}
        public bool SetData(float[] data,int frameOffset){Boundary.UnityCall();MaxTransfer=Math.Max(MaxTransfer,data.Length);Array.Copy(data,0,Data,frameOffset*Channels,data.Length);return true;}
        public bool LoadAudioData()=>true;
    }
    public sealed class AnimationClip : Object { public float length=1; public HideFlags hideFlags; }
    public sealed class AssetBundle
    {
        public static AssetBundle LoadFromFile(string path){Boundary.UnityCall();return new();}
        public T LoadAsset<T>(string path) where T:new()=>new(); public void Unload(bool all){}
    }
}
namespace Seleka2.Actions
{
    public sealed class ActionDefinition {public string Id,AudioFile,AudioPath,AudioAsset="",BundlePath="",ClipAsset="";public IActionSource RuntimeSource;}
    public static class ActionRegistry
    {
        static ActionDefinition[] items=[];public static int Revision;
        public static void Set(params ActionDefinition[] values){items=values;Revision++;}
        public static ActionDefinition[] GetAll()=>items;public static ActionDefinition Get(string id)=>items.FirstOrDefault(x=>x.Id==id);
    }
    public sealed class Source : IActionSource {public float Duration=>1;public IActionPose CreatePose(GameObject target,ActionDefinition definition)=>throw new NotSupportedException();}
    internal sealed class ActionPose : IActionPose { public ActionPose(GameObject target, LoadedAction loaded) {} public int BoneCount=>0;public bool Apply(float t)=>true;public void Dispose(){} }
    public sealed class Logger {public void LogInfo(string text){}public void LogWarning(string text){} }
    public static class Plugin {public static Logger LogSource=new();}
}
