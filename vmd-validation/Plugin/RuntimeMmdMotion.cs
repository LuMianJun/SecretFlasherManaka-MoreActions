using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Seleka2.Mmd;

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
                var name=ReadText(r,15);var key=new Key{frame=r.ReadUInt32(),p=new Vector3{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle()},q=new Quaternion{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle(),w=r.ReadSingle()},interpolation=r.ReadBytes(64)};
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
                    for(int j=0;j<entries;j++){var name=ReadText(r,20);var enabled=r.ReadByte()!=0;if(!motion.ikSwitches.ContainsKey(name))motion.ikSwitches[name]=new List<KeyValuePair<uint,bool>>();motion.ikSwitches[name].Add(new KeyValuePair<uint,bool>(frame,enabled));}
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

public sealed class MmdSkeleton : IDisposable
{
    public sealed class Bone { public string name;public int parent,tail,type;public Vector3 rest;public Transform node; }
    public sealed class Ik { public int target,effector,iterations;public float limit;public int[] links;public bool limitKnee;public float kneeMax=180,kneeMin; }
    public readonly List<Bone> bones=new List<Bone>();public readonly List<Ik> iks=new List<Ik>();
    public readonly Dictionary<string,Bone> byName=new Dictionary<string,Bone>();
    public GameObject root;public bool useIk;
    static string ReadText(BinaryReader r,int size)=>Encoding.GetEncoding(932).GetString(r.ReadBytes(size)).Split('\0')[0];
    public static MmdSkeleton ReadPmd(string path,bool createTransforms=true)
    {
        var skeleton=new MmdSkeleton();
        using(var r=new BinaryReader(File.OpenRead(path)))
        {
            if(ReadText(r,3)!="Pmd"||Math.Abs(r.ReadSingle()-1)>0.001f)throw new Exception("Expected PMD 1.0");
            ReadText(r,20);ReadText(r,256);
            var count=r.ReadUInt32();r.BaseStream.Seek(count*38L,SeekOrigin.Current);
            count=r.ReadUInt32();r.BaseStream.Seek(count*2L,SeekOrigin.Current);count=r.ReadUInt32();r.BaseStream.Seek(count*70L,SeekOrigin.Current);
            count=r.ReadUInt16();if(count==0||count>5000)throw new InvalidDataException("Invalid PMD bone count");
            for(int i=0;i<count;i++)
            {
                var bone=new Bone{name=ReadText(r,20),parent=r.ReadUInt16(),tail=r.ReadUInt16(),type=r.ReadByte()};r.ReadUInt16();
                bone.rest=MmdMotion.ConvertPosition(new Vector3{x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle()});
                if(bone.parent==65535)bone.parent=-1;
                float lengthSquared=bone.rest.x*bone.rest.x+bone.rest.y*bone.rest.y+bone.rest.z*bone.rest.z;
                if(!float.IsFinite(lengthSquared)||skeleton.byName.ContainsKey(bone.name))throw new InvalidDataException("Invalid PMD bone");
                skeleton.bones.Add(bone);skeleton.byName.Add(bone.name,bone);
            }
            count=r.ReadUInt16();
            for(int i=0;i<count;i++)
            {
                var ik=new Ik{target=r.ReadUInt16(),effector=r.ReadUInt16()};var links=r.ReadByte();ik.iterations=r.ReadUInt16();ik.limit=r.ReadSingle()*4*Mathf.Rad2Deg;ik.links=new int[links];
                for(int j=0;j<links;j++)ik.links[j]=r.ReadUInt16();skeleton.iks.Add(ik);
            }
        }
        for(int i=0;i<skeleton.bones.Count;i++)
        {
            var visited=new HashSet<int>();int parent=i;
            while(parent>=0){if(parent>=skeleton.bones.Count||!visited.Add(parent))throw new InvalidDataException("Invalid PMD hierarchy");parent=skeleton.bones[parent].parent;}
        }
        foreach(var ik in skeleton.iks)if(ik.target>=skeleton.bones.Count||ik.effector>=skeleton.bones.Count||ik.links.Any(i=>i>=skeleton.bones.Count)||ik.iterations>10000||float.IsNaN(ik.limit)||float.IsInfinity(ik.limit)||ik.limit<0)throw new InvalidDataException("Invalid PMD IK");
        if(createTransforms)skeleton.CreateTransforms();return skeleton;
    }
    internal void CreateTransforms() { foreach (var _ in CreateTransformSteps()) { } }
    internal IEnumerable<int> CreateTransformSteps()
    {
        root=new GameObject("MmdSourceSkeleton");root.hideFlags=HideFlags.HideAndDontSave;root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root); yield return 0;
        foreach(var b in bones) { b.node=new GameObject(b.name).transform; b.node.SetParent(root.transform,false); yield return 0; }
        foreach(var b in bones) { b.node.SetParent(b.parent<0?root.transform:bones[b.parent].node,false); b.node.localPosition=b.rest-(b.parent<0?Vector3.zero:bones[b.parent].rest); yield return 0; }
    }
    internal MmdSkeleton Instantiate()
    {
        var instance = CopyData();
        try { instance.CreateTransforms(); return instance; } catch { instance.Dispose(); throw; }
    }
    internal MmdSkeleton CopyData()
    {
        var instance = new MmdSkeleton();
        foreach (var b in bones)
        {
            var copy = new Bone { name = b.name, parent = b.parent, tail = b.tail, type = b.type, rest = b.rest };
            instance.bones.Add(copy); instance.byName.Add(copy.name, copy);
        }
        foreach (var ik in iks) instance.iks.Add(new Ik { target = ik.target, effector = ik.effector, iterations = ik.iterations, limit = ik.limit, links = (int[])ik.links.Clone(), limitKnee = ik.limitKnee, kneeMax = ik.kneeMax, kneeMin = ik.kneeMin });
        return instance;
    }
    public void Evaluate(MmdMotion motion,float frame)
    {
        foreach(var b in bones)
        {
            var key=motion.Sample(b.name,frame);var q=key.q;
            if(b.type==8&&b.tail>0&&b.tail<bones.Count)
            {
                var axis=(bones[b.tail].rest-b.rest).normalized;var v=new Vector3(q.x,q.y,q.z);v=axis*Vector3.Dot(v,axis);
                q=new Quaternion(v.x,v.y,v.z,q.w).normalized;
            }
            b.node.localRotation=q;b.node.localPosition=b.rest-(b.parent<0?Vector3.zero:bones[b.parent].rest)+key.p;
        }
        if(useIk)foreach(var ik in iks)
        {
            if(!bones[ik.target].name.Contains("足")&&!bones[ik.target].name.Contains("つま先"))continue;
            if(!motion.tracks.ContainsKey(bones[ik.target].name)||!motion.IkEnabled(bones[ik.target].name,(int)frame))continue;
            var target=bones[ik.target].node;var effector=bones[ik.effector].node;
            if(ik.limitKnee)foreach(var index in ik.links)
                if(bones[index].name.Contains("ひざ"))bones[index].node.localRotation=LimitKnee(bones[index].node.localRotation,Quaternion.identity,ik.kneeMax,ik.kneeMin);
            for(int iteration=0;iteration<Math.Min(ik.iterations,80);iteration++)
            {
                bool moved=false;
                foreach(var index in ik.links)
                {
                    var link=bones[index].node;var inv=Quaternion.Inverse(link.rotation);
                    var a=(inv*(effector.position-link.position)).normalized;var b=(inv*(target.position-link.position)).normalized;
                    var angle=Vector3.Angle(a,b);if(angle<0.001f)continue;var axis=Vector3.Cross(a,b).normalized;if(axis.sqrMagnitude<0.001f)continue;
                    var previous=link.localRotation;
                    var candidate=(previous*Quaternion.AngleAxis(Math.Min(angle,ik.limit),axis)).normalized;
                    if(bones[index].name.Contains("ひざ"))
                    {
                        if(ik.limitKnee)candidate=LimitKnee(candidate,previous,ik.kneeMax,ik.kneeMin);
                        else { var q=candidate;candidate=new Quaternion(Mathf.Sqrt(Math.Max(0,1-q.w*q.w)),0,0,q.w).normalized; }
                    }
                    link.localRotation=candidate;
                    moved=true;
                }
                if(!moved||Vector3.Distance(effector.position,target.position)<0.001f)break;
            }
        }
    }

    // Project onto the actual hinge and unwrap around the previous CCD step.
    // Keeping w while forcing x positive makes q and -q mean different poses
    // and lets a deeply folded knee enter the >180-degree inverse branch.
    internal static Quaternion LimitKnee(Quaternion candidate,Quaternion previous,float maximum,float minimum=0)
    {
        static double Angle(Quaternion q)
        {
            double angle=Math.IEEERemainder(2*Math.Atan2(q.x,q.w)*180/Math.PI,360);
            // At a half turn both signs are equally close; prefer the allowed
            // bending side, including float sin/cos roundoff around w=0.
            return Math.Abs(Math.Abs(angle)-180)<.0001?180:angle;
        }
        double prior=Math.Clamp(Angle(previous),minimum,maximum);
        double squared=(double)candidate.x*candidate.x+(double)candidate.w*candidate.w;
        if(squared<1e-12)return Quaternion.AngleAxis((float)prior,new Vector3(1,0,0));
        double angle=Angle(candidate);
        angle+=360*Math.Round((prior-angle)/360);
        return Quaternion.AngleAxis((float)Math.Clamp(angle,minimum,maximum),new Vector3(1,0,0));
    }

    public void Dispose(){if(root!=null)UnityEngine.Object.Destroy(root);}
}

static class QuaternionMagnitudeExtension
{
    public static float sqrMagnitude(this Quaternion q)=>q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
}
