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
public sealed class MmdSkeleton : IDisposable
{
    public sealed class Bone { public string name;public int parent,tail,type;public Vector3 rest;public Transform node;public int append=-1;public float appendRatio=1;public bool afterIk;internal Vector3 localRest,twistAxis; }
    public sealed class Ik { public int target,effector,iterations;public float limit;public int[] links;public bool limitKnee;public float kneeMax=180,kneeMin;internal bool supportedTarget; }
    public readonly List<Bone> bones=new List<Bone>();public readonly List<Ik> iks=new List<Ik>();
    public readonly Dictionary<string,Bone> byName=new Dictionary<string,Bone>();
    public GameObject root;public bool useIk;
    sealed class PreparedMotion
    {
        internal MmdMotion Motion;
        internal List<MmdMotion.Key>[] Keys;
        internal bool[] IkTracks;
    }
    readonly Dictionary<MmdMotion,PreparedMotion> preparedMotions=new();
    PreparedMotion prepared;
    Quaternion[] authoredRotations;
    MmdMotion sampledMotion;
    float sampledFrame;
    internal void CreateTransforms() { foreach (var _ in CreateTransformSteps()) { } }
    internal IEnumerable<int> CreateTransformSteps()
    {
        root=new GameObject("MmdSourceSkeleton");root.hideFlags=HideFlags.HideAndDontSave;root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root); yield return 0;
        foreach(var b in bones) { b.node=new GameObject(b.name).transform; b.node.SetParent(root.transform,false); yield return 0; }
        foreach(var b in bones)
        {
            b.node.SetParent(b.parent<0?root.transform:bones[b.parent].node,false);
            b.localRest=b.rest-(b.parent<0?Vector3.zero:bones[b.parent].rest);
            b.twistAxis=b.type==8&&b.tail>0&&b.tail<bones.Count?(bones[b.tail].rest-b.rest).normalized:Vector3.zero;
            b.node.localPosition=b.localRest;yield return 0;
        }
        foreach(var ik in iks)ik.supportedTarget=bones[ik.target].name.Contains("足")||bones[ik.target].name.Contains("つま先");
        authoredRotations=new Quaternion[bones.Count];prepared=null;sampledMotion=null;preparedMotions.Clear();
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
            var copy = new Bone { name = b.name, parent = b.parent, tail = b.tail, type = b.type, rest = b.rest, append=b.append, appendRatio=b.appendRatio, afterIk=b.afterIk };
            instance.bones.Add(copy); instance.byName.Add(copy.name, copy);
        }
        foreach (var ik in iks) instance.iks.Add(new Ik { target = ik.target, effector = ik.effector, iterations = ik.iterations, limit = ik.limit, links = (int[])ik.links.Clone(), limitKnee = ik.limitKnee, kneeMax = ik.kneeMax, kneeMin = ik.kneeMin });
        return instance;
    }
    public void Evaluate(MmdMotion motion,float frame)
    {
        bool bound=prepared!=null&&ReferenceEquals(prepared.Motion,motion);
        for(int index=0;index<bones.Count;index++)
        {
            var b=bones[index];
            // Standard two-joint thumbs start at the game's proximal joint.
            // Semi-standard motions explicitly include thumb 0 and retain 0/1/2.
            var key=bound?MmdMotion.SampleValue(prepared.Keys[index],frame):SampleBoneValue(motion,b.name,frame);var q=key.q;
            authoredRotations[index]=q;
            if(b.type==8&&b.tail>0&&b.tail<bones.Count)
            {
                var axis=b.twistAxis;var v=new Vector3(q.x,q.y,q.z);v=axis*Vector3.Dot(v,axis);
                q=new Quaternion(v.x,v.y,v.z,q.w).normalized;
            }
            b.node.localRotation=q;b.node.localPosition=b.localRest+key.p;
            if(b.append>=0&&!b.afterIk)ApplyAppend(b);
        }
        sampledMotion=motion;sampledFrame=frame;
        if(useIk)for(int ikIndex=0;ikIndex<iks.Count;ikIndex++)
        {
            var ik=iks[ikIndex];
            if(!ik.supportedTarget)continue;
            if(!(bound?prepared.IkTracks[ikIndex]:motion.tracks.ContainsKey(bones[ik.target].name))||!motion.IkEnabled(bones[ik.target].name,(int)frame))continue;
            var target=bones[ik.target].node;var effector=bones[ik.effector].node;
            if(ik.limitKnee) { SolveLeg(ik,target,effector); continue; }
            for(int iteration=0;iteration<Math.Min(ik.iterations,80);iteration++)
            {
                bool moved=false;
                foreach(var index in ik.links)
                {
                    var link=bones[index].node;var inv=Quaternion.Inverse(link.rotation);
                    var a=(inv*(effector.position-link.position)).normalized;var b=(inv*(target.position-link.position)).normalized;
                    var cross=Vector3.Cross(a,b);var sine=cross.magnitude;
                    var angle=(float)(Math.Atan2(sine,Vector3.Dot(a,b))*180/Math.PI);
                    if(angle<0.001f)continue;
                    var axis=sine>1e-8f?cross/sine:Perpendicular(a,new Vector3(1,0,0));
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
        // The deform D chain inherits the FINAL control-chain rotations,
        // including knee and toe IK. It must never feed back into either solve.
        RefreshDeform(motion,frame);
    }

    internal void PrepareMotion(MmdMotion motion)
    {
        if(!motion.PreparedForPlayback){prepared=null;return;}
        if(preparedMotions.TryGetValue(motion,out var cached)){prepared=cached;return;}
        var value=new PreparedMotion{Motion=motion,Keys=new List<MmdMotion.Key>[bones.Count],IkTracks=new bool[iks.Count]};
        for(int i=0;i<bones.Count;i++)motion.tracks.TryGetValue(TrackName(motion,bones[i].name),out value.Keys[i]);
        for(int i=0;i<iks.Count;i++)value.IkTracks[i]=motion.tracks.ContainsKey(bones[iks[i].target].name);
        prepared=value;
        if(preparedMotions.Count>=16)preparedMotions.Remove(preparedMotions.Keys.First());
        preparedMotions.Add(motion,value);
    }

    static string TrackName(MmdMotion motion,string track)
    {
        if(!motion.ExactBoneSamples&&track.Length==4&&track.Substring(1,2)=="親指"&&!motion.tracks.ContainsKey(track.Substring(0,3)+"０"))
            return track[3]=='０'?track.Substring(0,3)+"１":track[3]=='１'?track.Substring(0,3)+"２":"";
        return track;
    }
    static MmdMotion.BoneSample SampleBoneValue(MmdMotion motion,string track,float frame)
        =>motion.SampleValue(TrackName(motion,track),frame);

    internal static MmdMotion.Key SampleBone(MmdMotion motion,string track,float frame)
    {
        return motion.Sample(TrackName(motion,track),frame);
    }

    internal void RefreshDeform(MmdMotion motion,float frame)
    {
        bool reuse=ReferenceEquals(sampledMotion,motion)&&sampledFrame==frame;
        for(int i=0;i<bones.Count;i++)
        {
            var b=bones[i];
            if(b.append<0||!b.afterIk)continue;
            b.node.localRotation=reuse?authoredRotations[i]:SampleBoneValue(motion,b.name,frame).q;ApplyAppend(b);
        }
    }

    void ApplyAppend(Bone bone)
    {
        var q=bones[bone.append].node.localRotation;
        if(bone.appendRatio==-1)q=Quaternion.Inverse(q);
        else if(bone.appendRatio!=1)q=Quaternion.Slerp(Quaternion.identity,q,bone.appendRatio);
        // PMX append order: user rotation * inherited rotation (nanoem Bone.cc).
        bone.node.localRotation=(bone.node.localRotation*q).normalized;
    }

    // Solve the hinge analytically from this frame's sampled geometry. Limited
    // CCD can stop near a straight knee when acos(dot) rounds a small step to
    // zero; starting each frame from FK then selects inconsistent branches.
    // The X components are retained, so an oblique bind segment is not silently
    // treated as a planar triangle. No positions, scales or targets are changed.
    void SolveLeg(Ik ik,Transform target,Transform effector)
    {
        if(ik.links.Length!=2)throw new InvalidOperationException("Leg IK needs two links");
        var knee=bones[ik.links[0]].node;var thigh=bones[ik.links[1]].node;
        if(knee.parent!=thigh||effector.parent!=knee)throw new InvalidOperationException("Leg IK hierarchy mismatch");
        var upper=knee.localPosition;var lower=effector.localPosition;
        var delta=target.position-thigh.position;
        double requested=delta.magnitude;
        double a=(double)upper.y*lower.y+(double)upper.z*lower.z;
        double b=(double)upper.z*lower.y-(double)upper.y*lower.z;
        double radius=Math.Sqrt(a*a+b*b);
        double constant=(double)upper.x*upper.x+(double)upper.y*upper.y+(double)upper.z*upper.z
            +(double)lower.x*lower.x+(double)lower.y*lower.y+(double)lower.z*lower.z+2d*upper.x*lower.x;
        double phase=Math.Atan2(b,a);
        double minimum=ik.kneeMin*Math.PI/180,maximum=ik.kneeMax*Math.PI/180;
        double authored=2*Math.Atan2(knee.localRotation.x,knee.localRotation.w);
        authored=Math.Clamp(Math.IEEERemainder(authored,2*Math.PI),minimum,maximum);
        double best=authored,bestError=double.PositiveInfinity,bestChange=double.PositiveInfinity;
        void Candidate(double angle)
        {
            angle=Math.Clamp(angle,minimum,maximum);
            double length=Math.Sqrt(Math.Max(0,constant+2*(a*Math.Cos(angle)+b*Math.Sin(angle))));
            double error=Math.Abs(length-requested),change=Math.Abs(angle-authored);
            if(error<bestError-1e-10||Math.Abs(error-bestError)<=1e-10&&change<bestChange)
            { best=angle;bestError=error;bestChange=change; }
        }
        Candidate(minimum);Candidate(maximum);Candidate(authored);
        if(radius>1e-12)
        {
            double bend=Math.Acos(Math.Clamp((requested*requested-constant)/(2*radius),-1,1));
            // Include extrema for limits crossing a turning point, not just
            // the usual forward-bending branch. The nearest radius is reachable.
            for(int turn=-2;turn<=2;turn++)
            {
                Candidate(phase+bend+turn*2*Math.PI);Candidate(phase-bend+turn*2*Math.PI);
                double extremum=phase+turn*Math.PI;
                if(extremum>=minimum&&extremum<=maximum)Candidate(extremum);
            }
        }
        var hint=thigh.rotation*new Vector3(1,0,0);
        knee.localRotation=Quaternion.AngleAxis((float)(best*180/Math.PI),new Vector3(1,0,0));
        var current=effector.position-thigh.position;
        if(current.sqrMagnitude>1e-16f&&delta.sqrMagnitude>1e-16f)
            thigh.rotation=(Aim(current,delta,hint)*thigh.rotation).normalized;
    }

    static Vector3 Perpendicular(Vector3 direction,Vector3 hint)
    {
        var axis=hint-direction*Vector3.Dot(hint,direction);
        if(axis.sqrMagnitude<1e-10f)
        {
            var alternate=Math.Abs(direction.y)<.9f?Vector3.up:new Vector3(0,0,1);
            axis=alternate-direction*Vector3.Dot(alternate,direction);
        }
        return axis/axis.magnitude;
    }

    static Quaternion Aim(Vector3 from,Vector3 to,Vector3 hinge)
    {
        from/=from.magnitude;to/=to.magnitude;
        var cross=Vector3.Cross(from,to);float sine=cross.magnitude;
        float dot=Vector3.Dot(from,to);
        // Carry the sampled thigh and knee plane by the shortest swing. An
        // extra twist to restore the original projected hinge can reverse the
        // knee while leaving the ankle exactly on its goal (large leg turns).
        // A true half turn has no unique shortest axis: use the sampled hinge
        // only in that degenerate case, without imposing it on ordinary poses.
        return sine>1e-8f?Quaternion.AngleAxis((float)(Math.Atan2(sine,dot)*180/Math.PI),cross/sine)
            :dot<0?Quaternion.AngleAxis(180,Perpendicular(from,hinge)):Quaternion.identity;
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

    public void Dispose(){if(root!=null)UnityEngine.Object.Destroy(root);prepared=null;sampledMotion=null;preparedMotions.Clear();}
}

static class QuaternionMagnitudeExtension
{
    public static float sqrMagnitude(this Quaternion q)=>q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
}
