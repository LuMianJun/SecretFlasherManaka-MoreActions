using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using SFM.Actions;
using SFM.Mmd.Upstream;

namespace SFM.Mmd;

// Game-side source extensions. The upstream solver still owns standard FK/IK;
// virtual parents run before its existing IK, deform grants run afterwards,
// and native optional bones receive deltas over their current native pose.
internal sealed class MmdAdditionalTracks
{
    const float Unit=.08f;
    readonly RuntimeVmdRig rig;
    readonly Quaternion[] reference, savedRotation;
    readonly Vector3[] referencePosition, savedPosition;
    readonly int[] parents;
    readonly bool[] owned,nativeOnly;
    readonly NativeTrack[] native;
    readonly Dictionary<HumanBodyBones,int> humans=new();
    readonly Dictionary<string,Track> tracks=new(StringComparer.Ordinal);
    readonly HashSet<string> consumed=new(StringComparer.Ordinal);
    readonly Side[] sides;
    readonly Track waist,upper3,bothEyes;
    readonly int hips,chest;
    readonly bool hasAfter;
    bool saved;

    readonly struct Sample
    {
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;
        internal Sample(Vector3 p,Quaternion q){Position=p;Rotation=q;}
        internal static Sample Identity=>new(Vector3.zero,Quaternion.identity);
    }
    sealed class Track
    {
        readonly List<MmdMotion.Key> keys;
        internal readonly bool Active;
        float lastFrame=float.NaN;
        Sample last;
        internal Track(List<MmdMotion.Key> keys)
        {this.keys=keys;Active=keys.Any(key=>!Neutral(new Sample(key.p,MmdMotion.ControlRotation(key.q))));}
        internal bool SameKeys(Track other)=>other!=null&&ReferenceEquals(keys,other.keys);
        internal Sample At(float frame)
        {
            if(frame==lastFrame)return last;
            lastFrame=frame;
            var sample=MmdMotion.SampleControl(keys,frame);
            return last=new(sample.p,sample.q);
        }
    }
    sealed class NativeTrack
    {
        internal Track Keys,Twist;
        internal Vector3 TwistAxis;
        internal bool Eye;
    }
    sealed class Side
    {
        internal int Shoulder=-1,Arm=-1,Elbow=-1,Hand=-1,Leg=-1,Knee=-1,Foot=-1,Toe=-1;
        internal int Thumb0=-1,Thumb1=-1,Thumb2=-1;
        internal Track ShoulderP,ShoulderC,Cancel,ArmTwist,HandTwist;
        internal Track DeformLeg,DeformKnee,DeformFoot,DeformToe,ToeFk;
        internal Track ThumbTrack0,ThumbTrack1,ThumbTrack2;
        internal bool RemapThumb;
        internal Vector3 ArmAxis,HandAxis;
    }

    internal bool HasWaistControls {get;}
    internal bool HasBeforeIk {get;}
    internal bool Owns(int index)=>owned[index];
    // A core-controlled Toe or explicitly remapped thumb tip can also have a
    // native-name delta: transfer its source result before adding that delta.
    internal bool UsesNativeDelta(int index)=>nativeOnly[index];
    internal bool ContainsTrack(string name)=>consumed.Contains(MmdBoneMap.Normalize(name.Split('\0')[0]));

    internal MmdAdditionalTracks(RuntimeVmdRig rig,MmdMotion motion,ActionDefinition action)
    {
        this.rig=rig;
        int count=rig.bindings.Count;
        reference=new Quaternion[count];referencePosition=new Vector3[count];parents=new int[count];
        savedPosition=new Vector3[count];savedRotation=new Quaternion[count];owned=new bool[count];nativeOnly=new bool[count];native=new NativeTrack[count];
        var indexes=new Dictionary<RuntimeVmdRig.Binding,int>();
        for(int i=0;i<count;i++)
        {
            var b=rig.bindings[i];indexes.Add(b,i);
            reference[i]=b.sample.rotation;referencePosition[i]=b.sample.position;
            parents[i]=b.parent!=null&&indexes.TryGetValue(b.parent,out var p)?p:-1;
            if(b.human.HasValue)humans[b.human.Value]=i;
        }
        Track Find(string name)
        {
            if(string.IsNullOrEmpty(name))return null;
            name=MmdBoneMap.Normalize(name);
            if(tracks.TryGetValue(name,out var cached))return cached;
            if(!motion.tracks.TryGetValue(name,out var keys)||keys.Count==0)
            {
                // VMD names are fixed 15-byte Shift-JIS fields. Native helper
                // names are accepted only through their unambiguous preset.
                var encoding=Encoding.GetEncoding(932);var bytes=encoding.GetBytes(name);
                if(bytes.Length<=15||!motion.tracks.TryGetValue(MmdBoneMap.Normalize(encoding.GetString(bytes,0,15)),out keys)||keys.Count==0)return null;
            }
            var value=new Track(keys);tracks.Add(name,value);consumed.Add(name);return value;
        }
        bool Selected(int i)=>i>=0&&ActionPolicy.WritesBone(action,rig.bindings[i].path);
        hips=Human(HumanBodyBones.Hips);chest=Human(HumanBodyBones.Chest);
        waist=hips>=0?Find("腰"):null;upper3=chest>=0?Find("上半身3"):null;
        bool hasEyes=rig.bindings.Any(b=>b.presetSource is "左目" or "右目");
        bothEyes=hasEyes?Find("両目"):null;
        sides=new Side[2];
        for(int s=0;s<2;s++)
        {
            bool left=s==0;string side=left?"左":"右";
            var value=new Side
            {
                Shoulder=Human(left?HumanBodyBones.LeftShoulder:HumanBodyBones.RightShoulder),
                Arm=Human(left?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm),
                Elbow=Human(left?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm),
                Hand=Human(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand),
                Leg=Human(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg),
                Knee=Human(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg),
                Foot=Human(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot),
                Toe=Human(left?HumanBodyBones.LeftToes:HumanBodyBones.RightToes),
                Thumb0=Human(left?HumanBodyBones.LeftThumbProximal:HumanBodyBones.RightThumbProximal),
                Thumb1=Human(left?HumanBodyBones.LeftThumbIntermediate:HumanBodyBones.RightThumbIntermediate),
                Thumb2=Human(left?HumanBodyBones.LeftThumbDistal:HumanBodyBones.RightThumbDistal),
                ShoulderP=Find(side+"肩P"),ShoulderC=Find(side+"肩C"),Cancel=Find("腰キャンセル"+side),
                ArmTwist=Find(side+"腕捩"),HandTwist=Find(side+"手捩"),
                DeformLeg=Find(side+"足D"),DeformKnee=Find(side+"ひざD"),DeformFoot=Find(side+"足首D"),DeformToe=Find(side+"足先EX"),
                ThumbTrack0=Find(side+"親指０")
            };
            if(value.Toe>=0)value.ToeFk=Find(side+"つま先");
            if(value.ThumbTrack0!=null)
            {
                value.ThumbTrack1=Find(side+"親指１");value.ThumbTrack2=Find(side+"親指２");
                // A neutral zero track can still declare three-joint naming
                // when source 1/2 animate; all-neutral thumb controls do not
                // replace the pinned core's two-joint output.
                value.RemapThumb=Active(value.ThumbTrack0)||Active(value.ThumbTrack1)||Active(value.ThumbTrack2);
                if(value.RemapThumb&&Selected(value.Thumb2))owned[value.Thumb2]=true;
            }
            value.ArmAxis=Axis(value.Arm,value.Elbow);value.HandAxis=Axis(value.Elbow,value.Hand);
            sides[s]=value;
        }
        HasWaistControls=Active(waist)||sides.Any(s=>Active(s.Cancel));
        HasBeforeIk=HasWaistControls||Active(upper3)||sides.Any(s=>Active(s.ShoulderP)||Active(s.ShoulderC)||s.RemapThumb);
        hasAfter=sides.Any(s=>Active(s.ArmTwist)||Active(s.HandTwist)||Active(s.DeformLeg)||Active(s.DeformKnee)||Active(s.DeformFoot)||Active(s.DeformToe)||Active(s.ToeFk)||s.RemapThumb);
        for(int i=0;i<count;i++)
        {
            var b=rig.bindings[i];string source=b.presetSource;
            bool core=b.human.HasValue&&UpstreamBodySolver.StandardMapping.ContainsKey(b.human.Value);
            bool coreToe=b.human is HumanBodyBones.LeftToes or HumanBodyBones.RightToes;
            bool thumbTip=b.human is HumanBodyBones.LeftThumbDistal or HumanBodyBones.RightThumbDistal;
            // Standard native-name aliases already enter the upstream reader.
            // Only otherwise undriven native nodes receive a second delta.
            if(core)continue;
            var value=new NativeTrack();
            value.Eye=source is "左目" or "右目";
            if(value.Eye)value.Keys=Find(source)??Find(b.sample.name);
            // Semantic thumb 2 belongs to the core's two-joint convention, or
            // to SourceChild when thumb 0 explicitly selects three joints.
            // A native tip-name track is a separate optional delta only.
            else if(thumbTip)value.Keys=Find(b.sample.name);
            else if(!IsVirtual(source))value.Keys=Find(source)??Find(b.sample.name);
            else value.Keys=Find(b.sample.name);
            for(int s=0;s<2;s++)
            {
                string suffix=s==0?"_L":"_R";
                if(b.sample.name=="UpperArm_twist"+suffix){value.Twist=sides[s].ArmTwist;value.TwistAxis=sides[s].ArmAxis;}
                if(b.sample.name=="LowerArm_twist"+suffix){value.Twist=sides[s].HandTwist;value.TwistAxis=sides[s].HandAxis;}
            }
            // A preset may itself name the semantic Japanese twist channel.
            // Its projected rotation is a single grant, never full Q * Q.
            if(value.Keys!=null&&value.Keys.SameKeys(value.Twist))value.Keys=null;
            if(!Active(value.Keys)&&!Active(value.Twist)&&!(value.Eye&&Active(bothEyes)))continue;
            native[i]=value;
            if(Selected(i))owned[i]=true;
            bool remappedThumb=sides.Any(s=>s.RemapThumb&&s.Thumb2==i);
            nativeOnly[i]=!coreToe&&!remappedThumb;
        }
    }

    static bool IsVirtual(string source)=>source!=null&&(source.EndsWith("足D",StringComparison.Ordinal)||source.EndsWith("ひざD",StringComparison.Ordinal)||source.EndsWith("足首D",StringComparison.Ordinal)||source.EndsWith("足先EX",StringComparison.Ordinal));
    static bool Active(Track track)=>track?.Active==true;
    // Use exact components: small authored translations/rotations remain
    // active even when Unity's overloaded equality considers them zero.
    static bool Zero(Vector3 value)=>value.x==0&&value.y==0&&value.z==0;
    static bool Identity(Quaternion value)=>value.x==0&&value.y==0&&value.z==0&&(value.w==1||value.w==-1);
    static bool Neutral(Sample value)=>Zero(value.Position)&&Identity(value.Rotation);
    int Human(HumanBodyBones role)=>humans.TryGetValue(role,out var i)?i:-1;
    Vector3 Axis(int a,int b)=>a<0||b<0?Vector3.zero:(referencePosition[b]-referencePosition[a]).normalized;
    static Sample At(Track track,float frame)=>track==null?Sample.Identity:track.At(frame);
    Quaternion Frame(int i)=>i<0?rig.proxy.transform.rotation:rig.bindings[i].sample.rotation*Quaternion.Inverse(reference[i]);
    Quaternion NativeParentFrame(int i,float frame)
    {
        int p=parents[i];if(p<0)return rig.proxy.transform.rotation;
        // An optional parent's authored source delta participates in its child
        // source frame, without mistaking native customization for an MMD key.
        if(native[p]!=null)
        {
            var value=native[p];var q=At(value.Keys,frame).Rotation;
            if(value.Twist!=null)q=q*Twist(At(value.Twist,frame).Rotation,value.TwistAxis);
            if(value.Eye&&bothEyes!=null)q=At(bothEyes,frame).Rotation*q;
            return NativeParentFrame(p,frame)*q;
        }
        if(rig.bindings[p].human.HasValue)return Frame(p);
        return NativeParentFrame(p,frame);
    }
    static Quaternion Twist(Quaternion q,Vector3 axis)
    {
        if(axis.sqrMagnitude<1e-12f)return Quaternion.identity;
        var vector=axis*Vector3.Dot(new Vector3(q.x,q.y,q.z),axis);
        return MmdMotion.ControlRotation(new Quaternion(vector.x,vector.y,vector.z,q.w));
    }
    void Rigid(int index,Vector3 pivot,Quaternion rotation,Vector3 offset)
    {
        if(index<0||(Identity(rotation)&&Zero(offset)))return;
        var t=rig.bindings[index].sample;var p=t.position;var q=t.rotation;
        t.rotation=rotation*q;t.position=pivot+rotation*(p-pivot)+offset;
    }
    void LocalGrant(int index,Sample key,Quaternion parentFrame)
    {
        if(index<0||Neutral(key))return;
        var t=rig.bindings[index].sample;
        Rigid(index,t.position,parentFrame*key.Rotation*Quaternion.Inverse(parentFrame),parentFrame*key.Position*Unit);
    }

    internal void ApplyControls(int frame,MmdControlHierarchy controls)
    {
        var waistKey=At(waist,frame);
        var left=At(sides[0].Cancel,frame);var right=At(sides[1].Cancel,frame);
        controls.SetWaist(waistKey.Position,waistKey.Rotation);
        controls.SetWaistCancel(true,left.Position,left.Rotation);
        controls.SetWaistCancel(false,right.Position,right.Rotation);
    }

    // These controls use the transferred FK pose before the leg solve.
    internal void ApplyBeforeIk(int frame)
    {
        if(upper3!=null)LocalGrant(chest,At(upper3,frame),Frame(chest));
        foreach(var side in sides)
        {
            if((side.ShoulderP!=null||side.ShoulderC!=null)&&side.Shoulder>=0)
            {
                var p=At(side.ShoulderP,frame);var c=At(side.ShoulderC,frame);
                LocalGrant(side.Shoulder,p,Frame(parents[side.Shoulder]));
                LocalGrant(side.Arm,new Sample(c.Position,c.Rotation*Quaternion.Inverse(p.Rotation)),Frame(side.Shoulder));
            }
            if(side.RemapThumb)
            {
                SourceChild(side.Thumb0,side.Hand,At(side.ThumbTrack0,frame));
                SourceChild(side.Thumb1,side.Thumb0,At(side.ThumbTrack1,frame));
            }
        }
    }
    void SourceChild(int index,int parent,Sample key)
    {
        if(index<0||parent<0)return;
        var basis=Frame(parent);var target=rig.bindings[index].sample;
        target.rotation=basis*key.Rotation*reference[index];
        target.position=rig.bindings[parent].sample.position+basis*(referencePosition[index]-referencePosition[parent]+key.Position*Unit);
    }

    internal void RestoreSamples()
    {
        if(!saved)return;
        for(int i=0;i<rig.bindings.Count;i++)
        {var t=rig.bindings[i].sample;t.localPosition=savedPosition[i];t.localRotation=savedRotation[i];}
        saved=false;
    }
    internal void Apply(float frame)
    {
        if(!hasAfter)return;
        for(int i=0;i<rig.bindings.Count;i++)
        {var t=rig.bindings[i].sample;savedPosition[i]=t.localPosition;savedRotation[i]=t.localRotation;}
        saved=true;
        foreach(var side in sides)
        {
            if(side.ArmTwist!=null&&side.Arm>=0&&side.Elbow>=0)
            {
                var key=At(side.ArmTwist,frame);var twist=Twist(key.Rotation,side.ArmAxis);
                if(!Neutral(new Sample(key.Position,twist)))
                {
                    var basis=Frame(side.Arm);
                    Rigid(side.Elbow,rig.bindings[side.Arm].sample.position,basis*twist*Quaternion.Inverse(basis),basis*key.Position*Unit);
                }
            }
            if(side.HandTwist!=null&&side.Elbow>=0&&side.Hand>=0)
            {
                var key=At(side.HandTwist,frame);var twist=Twist(key.Rotation,side.HandAxis);
                if(!Neutral(new Sample(key.Position,twist)))
                {
                    var basis=Frame(side.Elbow);
                    Rigid(side.Hand,rig.bindings[side.Elbow].sample.position,basis*twist*Quaternion.Inverse(basis),basis*key.Position*Unit);
                }
            }
            var cancel=At(side.Cancel,frame).Rotation*Quaternion.Inverse(At(waist,frame).Rotation);
            if(side.DeformLeg!=null)LocalGrant(side.Leg,At(side.DeformLeg,frame),Frame(hips)*cancel);
            if(side.DeformKnee!=null)LocalGrant(side.Knee,At(side.DeformKnee,frame),Frame(side.Leg));
            if(side.DeformFoot!=null)LocalGrant(side.Foot,At(side.DeformFoot,frame),Frame(side.Knee));
            if(Active(side.ToeFk))SourceChild(side.Toe,side.Foot,At(side.ToeFk,frame));
            if(side.DeformToe!=null)LocalGrant(side.Toe,At(side.DeformToe,frame),Frame(side.Foot));
            if(side.RemapThumb)SourceChild(side.Thumb2,side.Thumb1,At(side.ThumbTrack2,frame));
        }
    }

    // Called after ordinary humanoid transfer and native baseline restoration.
    // It never assigns a bind scale or an absolute optional-bone reference pose.
    internal void ApplyNative(float frame,Quaternion heading)
    {
        Vector3 eyeCenter=Vector3.zero;int eyeCount=0;
        if(bothEyes!=null)
            for(int i=0;i<native.Length;i++)if(native[i]?.Eye==true){eyeCenter+=rig.bindings[i].live.position;eyeCount++;}
        if(eyeCount>0)eyeCenter/=eyeCount;
        for(int i=0;i<native.Length;i++)
        {
            var value=native[i];if(value==null||!owned[i])continue;
            var t=rig.bindings[i].live;var key=At(value.Keys,frame);
            Quaternion rotation=key.Rotation;
            Vector3 position=key.Position*Unit;
            if(value.Twist!=null)rotation=rotation*Twist(At(value.Twist,frame).Rotation,value.TwistAxis);
            var both=value.Eye?At(bothEyes,frame):Sample.Identity;
            if(Identity(rotation)&&Zero(position)&&Neutral(both))continue;
            var basis=heading*NativeParentFrame(i,frame);
            if(!Neutral(both))
            {
                var bothWorld=basis*both.Rotation*Quaternion.Inverse(basis);
                t.position=eyeCenter+bothWorld*(t.position-eyeCenter)+basis*(both.Position*Unit+both.Rotation*position);
                rotation=both.Rotation*rotation;
            }
            else t.position+=basis*position;
            t.rotation=basis*rotation*Quaternion.Inverse(basis)*t.rotation;
        }
    }
}
