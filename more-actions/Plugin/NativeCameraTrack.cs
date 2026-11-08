using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ExposureUnnoticed2.Object3D.Player.Scripts;

namespace Seleka2.Actions;

// Replacement packs carry skeleton bones only. The native clips also key the
// camera markers (CameraTarget, FirstPersonCameraParent/FirstPersonCamera):
// posture clips author the crouch/pose height, and Animator write-defaults
// restores the bind value when the next clip has no such track. A replacement
// removes both behaviours, the marker keeps the crouch value and the camera
// stays low after standing up.
//
// Reconstruct only properties affected by replacements. Current/next native
// clips participate with their actual weights; unrelated properties remain
// under native control. The fixed player controller uses override body layers
// for these markers (its additive face layer has no marker curves).
internal static class NativeCameraTrack
{
    const float Rate=30f;
    const float Sentinel=12345.5f;
    static readonly Vector3 SentinelPosition=new(Sentinel,Sentinel,Sentinel);
    static readonly Quaternion SentinelRotation=new(0.6f,0.8f,0f,0f);
    static readonly List<Transform> real=new();
    static readonly List<Transform> targets=new();
    static readonly List<string> paths=new();
    static readonly Dictionary<int,Track> cache=new();
    static readonly List<Candidate> candidates=new();
    static GameObject proxyRoot;
    static Animator owner;
    static bool failed,announced,defaults;
    static Vector3[] defaultPosition;
    static Quaternion[] defaultRotation;
    static string state="";

    sealed class Track
    {
        internal readonly byte[] Position;
        internal readonly bool[] Rotation;
        internal readonly Dictionary<int,Frame> Frames=new();
        internal bool Any;
        internal Track(int nodes){Position=new byte[nodes];Rotation=new bool[nodes];}
    }

    readonly struct Frame
    {
        internal readonly Vector3[] Position;
        internal readonly Quaternion[] Rotation;
        internal Frame(int nodes){Position=new Vector3[nodes];Rotation=new Quaternion[nodes];}
        internal Frame(Vector3[] position,Quaternion[] rotation){Position=position;Rotation=rotation;}
    }

    readonly struct Candidate
    {
        internal readonly AnimationClip Original, Replacement;
        internal readonly float Weight, Time;
        internal readonly int Layer;
        internal Candidate(AnimationClip original,AnimationClip replacement,float weight,float time,int layer)
        { Original=original;Replacement=replacement;Weight=weight;Time=time;Layer=layer; }
    }

    internal static string State=>state;

    internal static void Release()
    {
        if(proxyRoot!=null){UnityEngine.Object.Destroy(proxyRoot);proxyRoot=null;}
        real.Clear();targets.Clear();paths.Clear();cache.Clear();candidates.Clear();
        owner=null;announced=false;defaults=false;defaultPosition=null;defaultRotation=null;
        state="";
    }

    internal static void Fail(Exception e)
    {
        failed=true;
        Plugin.LogSource?.LogWarning("ACTION_CAMERA_TRACK_FAILED "+e.Message);
        try{Release();}catch{}
    }

    internal static void Apply(PlayerController player)
    {
        if(failed||player==null)return;
        var animator=player.Pca==null?null:player.Pca.Animator;
        if(animator==null)return;
        if(!NativeAnimationReplacements.IsBoundTo(animator))return;
        if(!Prepare(player,animator))return;
        CaptureDefaults();
        if(!TryEvaluate(animator,out var position,out var rotation,out var positionMask,out var rotationMask)){state="";return;}
        for(int i=0;i<real.Count;i++)
        {
            var node=real[i];
            if(node==null)continue;
            var local=node.localPosition;
            if((positionMask[i]&1)!=0)local.x=position[i].x;
            if((positionMask[i]&2)!=0)local.y=position[i].y;
            if((positionMask[i]&4)!=0)local.z=position[i].z;
            if(positionMask[i]!=0)node.localPosition=local;
            if(rotationMask[i])node.localRotation=rotation[i];
        }
        if(!announced)
        {
            announced=true;
            Plugin.LogSource?.LogInfo("ACTION_CAMERA_TRACK_PRESERVED nodes="+real.Count);
        }
    }

    // Bind/default marker pose. Animator write-defaults restores exactly this
    // when the active clip has no marker track; replacements must do the same.
    static void CaptureDefaults()
    {
        if(defaults||real.Count==0)return;
        defaultPosition=new Vector3[real.Count];
        defaultRotation=new Quaternion[real.Count];
        for(int i=0;i<real.Count;i++)
        {
            var node=real[i];
            if(node==null)continue;
            defaultPosition[i]=node.localPosition;
            defaultRotation[i]=node.localRotation;
        }
        defaults=true;
    }

    static void Collect(Animator animator,int layer,bool next)
    {
        var info=next?animator.GetNextAnimatorStateInfo(layer):animator.GetCurrentAnimatorStateInfo(layer);
        if(!float.IsFinite(info.normalizedTime))return;
        var clips=next?animator.GetNextAnimatorClipInfo(layer):animator.GetCurrentAnimatorClipInfo(layer);
        for(int i=0;i<clips.Length;i++)
        {
            var clip=clips[i].clip;
            if(clip==null||!float.IsFinite(clips[i].weight)||clips[i].weight<=0f)continue;
            bool replaced=NativeAnimationReplacements.TryGetCameraSource(clip,out var original);
            candidates.Add(new Candidate(replaced?original:clip,replaced?clip:null,clips[i].weight,info.normalizedTime,layer));
        }
    }

    static bool TryEvaluate(Animator animator,out Vector3[] position,out Quaternion[] rotation,
        out byte[] positionMask,out bool[] rotationMask)
    {
        position=null;rotation=null;positionMask=new byte[real.Count];rotationMask=new bool[real.Count];
        candidates.Clear();
        for(int layer=0;layer<animator.layerCount;layer++)
        {
            if(animator.GetLayerWeight(layer)<=0f)continue;
            Collect(animator,layer,false);
            if(animator.IsInTransition(layer))Collect(animator,layer,true);
        }
        var label="";
        foreach(var candidate in candidates)
        {
            if(candidate.Replacement==null)continue;
            var original=TrackFor(candidate.Original);
            var replacement=TrackFor(candidate.Replacement);
            for(int node=0;node<real.Count;node++)
            {
                positionMask[node]|=(byte)(original.Position[node]|replacement.Position[node]);
                rotationMask[node]|=original.Rotation[node]||replacement.Rotation[node];
            }
            if(original.Any)label+=(label.Length==0?"":"+")+"O:"+candidate.Original.name+"@"+candidate.Time.ToString("F2");
        }
        bool affected=false;
        for(int node=0;node<real.Count;node++)affected|=positionMask[node]!=0||rotationMask[node];
        if(!affected)return false;

        var result=DefaultFrame();position=result.Position;rotation=result.Rotation;
        var layerPosition=new Vector3[real.Count];
        var layerRotation=new Quaternion[real.Count];
        var layerPositionMask=new byte[real.Count];
        var layerRotationMask=new bool[real.Count];
        for(int layer=0;layer<animator.layerCount;layer++)
        {
            float layerWeight=Mathf.Clamp01(animator.GetLayerWeight(layer));
            if(layerWeight<=0f)continue;
            Array.Clear(layerPosition,0,layerPosition.Length);
            Array.Clear(layerRotation,0,layerRotation.Length);
            Array.Clear(layerPositionMask,0,layerPositionMask.Length);
            Array.Clear(layerRotationMask,0,layerRotationMask.Length);
            float total=0f;
            foreach(var candidate in candidates)
            {
                if(candidate.Layer!=layer)continue;
                var track=TrackFor(candidate.Original);
                var frame=FrameFor(candidate.Original,candidate.Time);
                var replacement=candidate.Replacement==null?null:TrackFor(candidate.Replacement);
                float combined=total+candidate.Weight;
                for(int node=0;node<real.Count;node++)
                {
                    layerPosition[node]+=frame.Position[node]*candidate.Weight;
                    layerRotation[node]=total<=0f?frame.Rotation[node]
                        :Quaternion.Slerp(layerRotation[node],frame.Rotation[node],candidate.Weight/combined);
                    layerPositionMask[node]|=(byte)(track.Position[node]|(replacement==null?0:replacement.Position[node]));
                    layerRotationMask[node]|=track.Rotation[node]||(replacement!=null&&replacement.Rotation[node]);
                }
                total=combined;
            }
            if(total<=0f)continue;
            float remaining=Mathf.Max(0f,1f-total);
            for(int node=0;node<real.Count;node++)
            {
                var value=(layerPosition[node]+defaultPosition[node]*remaining)/Mathf.Max(1f,total);
                var turn=remaining>0f?Quaternion.Slerp(layerRotation[node],defaultRotation[node],remaining):layerRotation[node];
                byte mask=layerPositionMask[node];
                if((mask&1)!=0)position[node].x=Mathf.Lerp(position[node].x,value.x,layerWeight);
                if((mask&2)!=0)position[node].y=Mathf.Lerp(position[node].y,value.y,layerWeight);
                if((mask&4)!=0)position[node].z=Mathf.Lerp(position[node].z,value.z,layerWeight);
                if(layerRotationMask[node])rotation[node]=Quaternion.Slerp(rotation[node],turn,layerWeight);
            }
        }
        state=label.Length>200?label.Substring(0,200):label;
        return true;
    }

    static Frame DefaultFrame()
    {
        var frame=new Frame(real.Count);
        if(defaultPosition==null)return frame;
        Array.Copy(defaultPosition,frame.Position,frame.Position.Length);
        Array.Copy(defaultRotation,frame.Rotation,frame.Rotation.Length);
        return frame;
    }

    static Frame FrameFor(AnimationClip clip,float normalized)
    {
        var length=clip.length;
        if(!float.IsFinite(length)||length<=0f)return DefaultFrame();
        var track=TrackFor(clip);
        if(!track.Any)return new Frame(defaultPosition,defaultRotation);
        int last=Mathf.Max(0,Mathf.CeilToInt(length*Rate));
        float seconds=(clip.isLooping?Mathf.Repeat(normalized,1f):Mathf.Clamp01(normalized))*length;
        float scaled=Mathf.Clamp(seconds*Rate,0,last);
        int low=Mathf.FloorToInt(scaled);
        int high=last<=low?low:low+1;
        float lowTime=low/Rate,highTime=Mathf.Min(high/Rate,length);
        float blend=highTime<=lowTime?0f:Mathf.Clamp01((seconds-lowTime)/(highTime-lowTime));
        var a=FrameAt(track,clip,low);
        var b=high==low?a:FrameAt(track,clip,high);
        if(high==low||blend<=0f)return a;
        var frame=new Frame(real.Count);
        for(int i=0;i<real.Count;i++)
        {
            frame.Position[i]=Vector3.Lerp(a.Position[i],b.Position[i],blend);
            frame.Rotation[i]=Quaternion.Slerp(a.Rotation[i],b.Rotation[i],blend);
        }
        return frame;
    }

    static Frame FrameAt(Track track,AnimationClip clip,int bucket)
    {
        if(track.Frames.TryGetValue(bucket,out var frame))return frame;
        frame=Sample(clip,Mathf.Min(bucket/Rate,clip.length),false);track.Frames[bucket]=frame;
        return frame;
    }

    static Track TrackFor(AnimationClip clip)
    {
        int id=clip.GetInstanceID();
        if(!cache.TryGetValue(id,out var track)){track=Create(clip);cache[id]=track;}
        return track;
    }

    static bool Prepare(PlayerController player,Animator animator)
    {
        if(owner==animator&&real.Count>0)return true;
        Release();owner=animator;
        proxyRoot=new GameObject("Seleka2CameraTrack"){hideFlags=HideFlags.HideAndDontSave};
        var referencer=player.Pca.PlayerAvatarObjectReferencer;
        // The referencer points at CameraTarget2; native clips key its parent,
        // the actual camera pivot. Sampling the child silently binds nothing.
        var child=referencer==null?null:referencer.CameraTarget;
        var target=child==null?null:child.parent;
        var locomotion=player.Pca.PlayerLocomotion;
        var fp=locomotion==null||locomotion.fpCameraController==null?null:locomotion.fpCameraController.Child;
        Add(animator.transform,target);
        Add(animator.transform,fp);
        if(real.Count==0){Release();return false;}
        return true;
    }

    static bool Add(Transform root,Transform node)
    {
        var path=PathOf(root,node);
        if(path==null||path.Length==0)return false;
        if(paths.Contains(path))return false;
        paths.Add(path);
        real.Add(node);
        targets.Add(BuildProxy(path));
        return true;
    }

    static string PathOf(Transform root,Transform node)
    {
        if(root==null||node==null||node==root)return null;
        var parts=new List<string>();
        var current=node;
        while(current!=null&&current!=root)
        {
            if(string.IsNullOrEmpty(current.name))return null;
            parts.Add(current.name);current=current.parent;
        }
        if(current!=root)return null;
        parts.Reverse();
        return string.Join("/",parts);
    }

    static Transform BuildProxy(string path)
    {
        var parent=proxyRoot.transform;
        foreach(var part in path.Split('/'))
        {
            var child=parent.Find(part);
            if(child==null)
            {
                var go=new GameObject(part){hideFlags=HideFlags.HideAndDontSave};
                child=go.transform;child.SetParent(parent,false);
            }
            parent=child;
        }
        return parent;
    }

    static Track Create(AnimationClip clip)
    {
        var track=new Track(real.Count);
        // Humanoid clips still carry generic transform curves; the Unity probe
        // confirmed SampleAnimation applies their CameraTarget track.
        var probe=Sample(clip,0f,true);
        for(int i=0;i<real.Count;i++)
        {
            byte mask=0;
            if(Mathf.Abs(probe.Position[i].x-Sentinel)!=0f)mask|=1;
            if(Mathf.Abs(probe.Position[i].y-Sentinel)!=0f)mask|=2;
            if(Mathf.Abs(probe.Position[i].z-Sentinel)!=0f)mask|=4;
            track.Position[i]=mask;
            track.Rotation[i]=Mathf.Abs(Quaternion.Dot(probe.Rotation[i],SentinelRotation))<0.9999f;
            track.Any|=mask!=0||track.Rotation[i];
        }
        return track;
    }

    static Frame Sample(AnimationClip clip,float time,bool probe)
    {
        var frame=new Frame(real.Count);
        for(int i=0;i<targets.Count;i++)
        {
            var node=targets[i];
            if(node==null)continue;
            node.localPosition=probe?SentinelPosition:defaultPosition[i];
            node.localRotation=probe?SentinelRotation:defaultRotation[i];
        }
        clip.SampleAnimation(proxyRoot,time);
        for(int i=0;i<targets.Count;i++)
        {
            var node=targets[i];
            frame.Position[i]=node==null?SentinelPosition:node.localPosition;
            frame.Rotation[i]=node==null?SentinelRotation:node.localRotation;
        }
        return frame;
    }
}

// Runs before PlayerController.OnLateUpdate: the animator already evaluated in
// Update, and the native height correction inside OnLateUpdate reads the marker
// afterwards, so the written value takes the same place as the clip's track.
[HarmonyPatch(typeof(PlayerController),"OnLateUpdate")]
internal static class NativeCameraTrackPatch
{
    static void Prefix(PlayerController __instance)
    {
        try{NativeCameraTrack.Apply(__instance);}
        catch(Exception e){NativeCameraTrack.Fail(e);}
    }
}
