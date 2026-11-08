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
    public sealed class Bone { public string name;public int parent,tail,type;public Vector3 rest;public Transform node; }
    public sealed class Ik { public int target,effector,iterations;public float limit;public int[] links;public bool limitKnee;public float kneeMax=180,kneeMin; }
    public readonly List<Bone> bones=new List<Bone>();public readonly List<Ik> iks=new List<Ik>();
    public readonly Dictionary<string,Bone> byName=new Dictionary<string,Bone>();
    public GameObject root;public bool useIk;
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
