using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SFM.Mmd;

// MMD's default character faces -Z, while this game's character faces +Z.
// Rotate the MMD frame 180 degrees about Y, as UnityVMDPlayer does. This is
// applied to BOTH positions and quaternions, exactly once.
public sealed class MmdMotion
{
    public sealed class Key { public uint frame; public Vector3 p; public Quaternion q; public byte[] interpolation; }
    public readonly Dictionary<string,List<Key>> tracks=new Dictionary<string,List<Key>>();
    public readonly record struct MorphKey(float time, float value);
    public readonly Dictionary<string,List<MorphKey>> morphs=new Dictionary<string,List<MorphKey>>();
    readonly Dictionary<string,List<KeyValuePair<uint,bool>>> ikSwitches=new Dictionary<string,List<KeyValuePair<uint,bool>>>();
    public string modelName;public uint lastFrame;
    internal bool ExactBoneSamples;
    internal readonly HashSet<string> ResolvedAliases=new(StringComparer.Ordinal);
    internal MmdMotion BindAliases(IEnumerable<KeyValuePair<string,string>> aliases)
    {
        var result=new MmdMotion{modelName=modelName,lastFrame=lastFrame,ExactBoneSamples=ExactBoneSamples};
        foreach(var p in tracks)result.tracks.Add(p.Key,p.Value);
        foreach(var p in morphs)result.morphs.Add(p.Key,p.Value);
        foreach(var p in ikSwitches)result.ikSwitches.Add(p.Key,p.Value);
        foreach(var group in aliases.GroupBy(p=>p.Key,StringComparer.Ordinal))
        {
            var targets=group.Select(p=>p.Value).Distinct().ToArray();
            if(targets.Length!=1||!tracks.TryGetValue(group.Key,out var keys))continue;
            if(!result.tracks.ContainsKey(targets[0]))result.tracks.Add(targets[0],keys);
            result.ResolvedAliases.Add(group.Key);
        }
        return result;
    }
    static string ReadText(BinaryReader r,int size) { var bytes=r.ReadBytes(size); if(bytes.Length!=size)throw new EndOfStreamException(); return Encoding.GetEncoding(932).GetString(bytes).Split('\0')[0]; }
    static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    static void CheckSection(BinaryReader r,uint count,int size) { if(count>2000000||count*(long)size>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Truncated or excessive VMD section"); }
    public static Vector3 ConvertPosition(Vector3 v)=>new Vector3{x=-v.x,y=v.y,z=-v.z};
    public static Quaternion ConvertRotation(Quaternion q)
    {
        float inverse=1f/MathF.Sqrt(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w);
        return new Quaternion{x=-q.x*inverse,y=q.y*inverse,z=-q.z*inverse,w=q.w*inverse};
    }
    public static MmdMotion Read(string path)
    {
        var motion=new MmdMotion();
        using(var r=new BinaryReader(File.OpenRead(path)))
        {
            var header=ReadText(r,30); if(header!="Vocaloid Motion Data 0002"&&header!="Vocaloid Motion Data file")throw new Exception("Unsupported VMD header");
            motion.modelName=ReadText(r,header.EndsWith("0002")?20:10);var count=r.ReadUInt32();CheckSection(r,count,111);
            for(int i=0;i<count;i++)
            {
                var name=MmdBoneMap.Normalize(ReadText(r,15));var key=new Key{frame=r.ReadUInt32(),p=new Vector3{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle()},q=new Quaternion{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle(),w=r.ReadSingle()},interpolation=r.ReadBytes(64)};
                if(key.interpolation.Length!=64||key.frame>1080000||!Finite(key.p.x)||!Finite(key.p.y)||!Finite(key.p.z)||!Finite(key.q.x)||!Finite(key.q.y)||!Finite(key.q.z)||!Finite(key.q.w)||!Finite(key.q.sqrMagnitude())||key.q.sqrMagnitude()<0.0001f)throw new Exception("Invalid VMD bone key");
                key.p=ConvertPosition(key.p);key.q=ConvertRotation(key.q);
                if(!motion.tracks.ContainsKey(name))motion.tracks[name]=new List<Key>();motion.tracks[name].Add(key);motion.lastFrame=Math.Max(motion.lastFrame,key.frame);
            }
            count=r.ReadUInt32();CheckSection(r,count,23);
            for(int i=0;i<count;i++)
            {
                var name=ReadText(r,15);var frame=r.ReadUInt32();var weight=r.ReadSingle();if(frame>1080000||!Finite(weight))throw new InvalidDataException("Invalid morph key");
                if(!motion.morphs.ContainsKey(name))motion.morphs[name]=new List<MorphKey>();motion.morphs[name].Add(new MorphKey(frame/30f,weight*100));
                motion.lastFrame=Math.Max(motion.lastFrame,frame);
            }
            if(r.BaseStream.Position<r.BaseStream.Length){count=r.ReadUInt32();CheckSection(r,count,61);r.BaseStream.Seek(count*61L,SeekOrigin.Current);}
            if(r.BaseStream.Position<r.BaseStream.Length){count=r.ReadUInt32();CheckSection(r,count,28);r.BaseStream.Seek(count*28L,SeekOrigin.Current);}
            if(r.BaseStream.Position<r.BaseStream.Length){count=r.ReadUInt32();CheckSection(r,count,9);r.BaseStream.Seek(count*9L,SeekOrigin.Current);}
            if(r.BaseStream.Position<r.BaseStream.Length)
            {
                count=r.ReadUInt32();CheckSection(r,count,9);
                for(int i=0;i<count;i++)
                {
                    var frame=r.ReadUInt32();r.ReadByte();var entries=r.ReadUInt32();CheckSection(r,entries,21);
                    for(int j=0;j<entries;j++){var name=MmdBoneMap.Normalize(ReadText(r,20));var enabled=r.ReadByte()!=0;if(!motion.ikSwitches.ContainsKey(name))motion.ikSwitches[name]=new List<KeyValuePair<uint,bool>>();motion.ikSwitches[name].Add(new KeyValuePair<uint,bool>(frame,enabled));}
                }
            }
        }
        foreach(var name in motion.tracks.Keys.ToArray())motion.tracks[name]=motion.tracks[name].Select((key,index)=>new {key,index}).OrderBy(v=>v.key.frame).ThenBy(v=>v.index).GroupBy(v=>v.key.frame).Select(g=>g.Last().key).ToList();
        foreach(var name in motion.morphs.Keys.ToArray())motion.morphs[name]=motion.morphs[name].GroupBy(k=>k.time).OrderBy(g=>g.Key).Select(g=>g.Last()).ToList();
        foreach(var list in motion.ikSwitches.Values)list.Sort((a,b)=>a.Key.CompareTo(b.Key));
        return motion;
    }
    public float Morph(string name,float seconds)
    {
        if(!morphs.TryGetValue(name,out var keys)||keys.Count==0)return 0;
        if(seconds<=keys[0].time)return Mathf.Clamp(keys[0].value,0,100);if(seconds>=keys[keys.Count-1].time)return Mathf.Clamp(keys[keys.Count-1].value,0,100);
        int lo=0,hi=keys.Count-1;while(hi-lo>1){int mid=(lo+hi)/2;if(keys[mid].time<=seconds)lo=mid;else hi=mid;}
        return Mathf.Clamp(Mathf.Lerp(keys[lo].value,keys[hi].value,(seconds-keys[lo].time)/(keys[hi].time-keys[lo].time)),0,100);
    }
    public bool IkEnabled(string name,int frame)
    {
        bool enabled=true;if(ikSwitches.TryGetValue(name,out var switches))foreach(var item in switches)if(item.Key<=frame)enabled=item.Value;return enabled;
    }
    public Key Sample(string name,float frame)
    {
        if(!tracks.TryGetValue(name,out var keys)||keys.Count==0)return new Key{q=Quaternion.identity};
        if(frame<=keys[0].frame)return keys[0];if(frame>=keys[keys.Count-1].frame)return keys[keys.Count-1];
        int low=0,high=keys.Count-1;while(high-low>1){int mid=(low+high)/2;if(keys[mid].frame<=frame)low=mid;else high=mid;}
        var a=keys[low];var b=keys[high];var t=(frame-a.frame)/(b.frame-a.frame);
        return new Key{p=new Vector3(Mathf.Lerp(a.p.x,b.p.x,Bezier(b.interpolation,0,t)),Mathf.Lerp(a.p.y,b.p.y,Bezier(b.interpolation,1,t)),Mathf.Lerp(a.p.z,b.p.z,Bezier(b.interpolation,2,t))),q=Quaternion.Slerp(a.q,b.q,Bezier(b.interpolation,3,t))};
    }
    public static float Bezier(byte[] controls,int channel,float x)
    {
        if(x<=0||x>=1)return Mathf.Clamp01(x);
        // Match UnityVMDReader's four 16-byte channel blocks. Redundant values
        // in the first block can differ from the later Z/rotation blocks.
        int offset=channel*16;
        var x1=controls[offset]/127f;var y1=controls[offset+4]/127f;var x2=controls[offset+8]/127f;var y2=controls[offset+12]/127f;
        float lo=0,hi=1,t=0.5f;
        for(int i=0;i<20;i++){t=(lo+hi)*0.5f;var v=3*(1-t)*(1-t)*t*x1+3*(1-t)*t*t*x2+t*t*t;if(v<x)lo=t;else hi=t;}
        return 3*(1-t)*(1-t)*t*y1+3*(1-t)*t*t*y2+t*t*t;
    }
}
