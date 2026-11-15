using UnityEngine;
using System.Reflection;
using System.Text;

internal sealed class OracleFixture
{
    internal readonly GameObject Actor=new("Actor");
    internal readonly Animator Animator;
    internal readonly Dictionary<HumanBodyBones,Transform> Bones=new();
    internal OracleFixture(bool rotatedAxes=true,float scale=1.125f)
    {
        Animator=Actor.AddComponent<Animator>();
        Actor.transform.localScale=Vector3.one*scale;
        var armature=new GameObject("Armature").transform;armature.SetParent(Actor.transform,false);
        Transform Add(HumanBodyBones name,Transform parent,Vector3 position,Quaternion q)
        {
            var bone=new GameObject(name.ToString()).transform;bone.position=position;bone.rotation=q;bone.SetParent(parent,true);Bones.Add(name,bone);return bone;
        }
        var identity=Quaternion.identity;var legQ=rotatedAxes?Quaternion.AngleAxis(176,Vector3.right):identity;
        var hips=Add(HumanBodyBones.Hips,armature,new(0,1,0),rotatedAxes?Quaternion.AngleAxis(83,Vector3.right):identity);
        var spine=Add(HumanBodyBones.Spine,hips,new(0,1.18f,0),rotatedAxes?Quaternion.AngleAxis(6,Vector3.forward):identity);
        var chest=Add(HumanBodyBones.Chest,spine,new(0,1.38f,0),identity);
        var neck=Add(HumanBodyBones.Neck,chest,new(0,1.55f,0),identity);Add(HumanBodyBones.Head,neck,new(0,1.7f,0),identity);
        foreach(bool left in new[]{true,false})
        {
            string side=left?"Left":"Right";float sign=left?-1:1;
            HumanBodyBones N(string suffix)=>Enum.Parse<HumanBodyBones>(side+suffix);
            var thigh=Add(N("UpperLeg"),hips,new(sign*.09f,.94f,0),legQ);
            var knee=Add(N("LowerLeg"),thigh,new(sign*.09f,.52f,.02f),legQ);
            var foot=Add(N("Foot"),knee,new(sign*.09f,.1f,0),rotatedAxes?Quaternion.AngleAxis(114,Vector3.right):identity);
            Add(N("Toes"),foot,new(sign*.09f,.045f,.15f),rotatedAxes?Quaternion.AngleAxis(88,Vector3.right):identity);
            var shoulder=Add(N("Shoulder"),chest,new(sign*.11f,1.44f,0),identity);
            var arm=Add(N("UpperArm"),shoulder,new(sign*.19f,1.44f,0),rotatedAxes?Quaternion.AngleAxis(sign*90,Vector3.forward):identity);
            var elbow=Add(N("LowerArm"),arm,new(sign*.46f,1.44f,0),arm.rotation);
            var hand=Add(N("Hand"),elbow,new(sign*.7f,1.44f,0),arm.rotation);
            foreach(var finger in new[]{"Thumb","Index","Middle","Ring","Little"})
            {
                Transform parent=hand;int fingerIndex=Array.IndexOf(new[]{"Thumb","Index","Middle","Ring","Little"},finger);
                foreach(var segment in new[]{"Proximal","Intermediate","Distal"})
                {
                    int index=Array.IndexOf(new[]{"Proximal","Intermediate","Distal"},segment);
                    var name=side+finger+segment;
                    if(Enum.TryParse<HumanBodyBones>(name,out var value))parent=Add(value,parent,new(sign*(.73f+.025f*index),1.44f,(fingerIndex-2)*.018f),hand.rotation);
                }
            }
        }
        foreach(var pair in Bones)Animator.Bones.Add(pair.Key,pair.Value);
        var transforms=new List<Transform>();void Visit(Transform t){transforms.Add(t);foreach(Transform c in t)Visit(c);}Visit(Actor.transform);
        Animator.avatar.humanDescription=new(){skeleton=transforms.Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray()};
    }
}

internal sealed class OriginalOracle
{
    internal readonly OracleFixture Fixture;
    internal readonly UnityVMDPlayer Player;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    internal OriginalOracle(string path,bool axes=true,float scale=1.125f)
    {
        Fixture=new(axes,scale);Player=new(){gameObject=Fixture.Actor};
        Invoke("Start");Player.Play(path);
    }
    void Invoke(string method)=>typeof(UnityVMDPlayer).GetMethod(method,Flags)!.Invoke(Player,null);
    internal void Evaluate(float frame)
    {
        typeof(UnityVMDPlayer).GetProperty("FrameNumber",Flags)!.SetValue(Player,(int)frame);
        typeof(UnityVMDPlayer).GetField("internalFrameNumber",Flags)!.SetValue(Player,frame);
        Time.deltaTime=0;Invoke("Update");
    }
}

internal static class OracleVmd
{
    internal static string Write(string path,bool switches=true,bool sparse=false,bool rawKeys=false,bool controlStress=false)
    {
        using var file=File.Create(path);using var writer=new BinaryWriter(file);
        static byte[] Text(string value,int bytes)
        {
            var result=new byte[bytes];var encoded=Encoding.GetEncoding(932).GetBytes(value);Array.Copy(encoded,result,Math.Min(encoded.Length,bytes));return result;
        }
        writer.Write(Text("Vocaloid Motion Data 0002",30));writer.Write(Text("OracleFixture",20));
        string[] names=["全ての親","センター","グルーブ","下半身","上半身","上半身2","首","頭","左肩","右肩","左腕","右腕","左ひじ","右ひじ","左手首","右手首","左足","右足","左ひざ","右ひざ","左足首","右足首","左足ＩＫ","右足ＩＫ","左つま先ＩＫ","右つま先ＩＫ","左親指１","右親指１","左人指１","右人指１","左中指２","右薬指３"];
        uint[] frames=rawKeys?(sparse?[8u,17u,17u,35u,60u]:[0u,10u,25u,25u,60u]):sparse?[8u,17u,35u,60u]:[0u,10u,25u,60u];
        if(rawKeys)names=names.Concat(new[]{"0","99","未対応ボーン","上半身２","左腕捩","左腕捩れ","左足IK","左親指1"}).ToArray();
        writer.Write(names.Length*frames.Length);
        foreach(var (name,index) in names.Select((n,i)=>(n,i)))
        foreach(var (frame,keyIndex) in frames.Select((f,i)=>(f,i)))
        {
            writer.Write(Text(name,15));writer.Write(frame);
            bool zero=frame==10;float phase=frame/60f;
            Vector3 position=zero?Vector3.zero:name is "センター" or "グルーブ"?new(.1f*phase,.2f*phase,-.08f*phase):name.Contains("足ＩＫ")?new(.2f*phase,.3f*phase,.1f*phase):name.Contains("つま先ＩＫ")?new(.02f*phase,.04f*phase,-.03f*phase):new(.001f*phase,-.002f*phase,.001f*phase);
            Quaternion q=zero?Quaternion.identity:Quaternion.Euler((index%5-2)*6*phase,(index%7-3)*8*phase,(index%3-1)*11*phase);
            if(controlStress&&!zero)
            {
                // Exercise the original control-space order with large root
                // yaw, compound center/foot rotation, and authored lower-body
                // translation. Expected values still come solely from Player.
                if(name=="全ての親"){q=Quaternion.Euler(12*phase,165*phase,-13*phase);position=new(.9f*phase,-.7f*phase,.25f*phase);}
                else if(name=="センター")q=Quaternion.Euler(16*phase,75*phase,-9*phase);
                else if(name=="グルーブ")q=Quaternion.Euler(-12*phase,35*phase,8*phase);
                else if(name=="下半身"){q=Quaternion.Euler(48*phase,-35*phase,22*phase);position=new(.03f*phase,.06f*phase,.07f*phase);}
                else if(name is "左足ＩＫ" or "右足ＩＫ")q=Quaternion.Euler(45*phase,-70*phase,110*phase);
                else if(name=="首")q=Quaternion.Euler(-35*phase,55*phase,-25*phase);
                else if(name=="頭")q=Quaternion.Euler(60*phase,-80*phase,40*phase);
            }
            if(rawKeys&&!zero){float multiplier=keyIndex==2?1.17f:1.03f;q=new(q.x*multiplier,q.y*multiplier,q.z*multiplier,q.w*multiplier);position+=new Vector3(.0002f*keyIndex,0,0);}
            if(rawKeys&&name=="頭"&&keyIndex==0)q=new Quaternion(0,0,0,0);
            writer.Write(position.x);writer.Write(position.y);writer.Write(position.z);writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);
            var interp=new byte[64];for(int channel=0;channel<4;channel++){interp[channel*16]=20;interp[channel*16+4]=35;interp[channel*16+8]=95;interp[channel*16+12]=110;}writer.Write(interp);
        }
        writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
        writer.Write(switches?(rawKeys?6:4):0);
        if(switches)foreach(var (frame,foot,toe) in new[]{(0,true,true),(15,true,false),(30,false,true),(45,true,true)})
        {
            if(rawKeys&&frame==15){writer.Write(15u);writer.Write((byte)1);writer.Write(0u);}
            writer.Write((uint)frame);writer.Write((byte)1);writer.Write(4u);
            foreach(string side in new[]{"左","右"}){writer.Write(Text(side+"足ＩＫ",20));writer.Write((byte)(foot?1:0));writer.Write(Text(side+"つま先ＩＫ",20));writer.Write((byte)(toe?1:0));}
            if(rawKeys&&frame==30){writer.Write(30u);writer.Write((byte)1);writer.Write(1u);writer.Write(Text("左足ＩＫ",20));writer.Write((byte)1);}
        }
        return path;
    }
}
