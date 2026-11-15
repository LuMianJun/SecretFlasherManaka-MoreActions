using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SFM.Actions;
using SFM.Mmd.Upstream;

namespace SFM.Mmd;

// Upstream owns the complete hidden-avatar solve. This class only transfers
// selected outputs, native-body ownership, expressions and controller motion.
public sealed partial class RuntimeVmdPose : IActionPose,IActionRootMotionPose,IActionFacePose,IActionBodyPose
{
    readonly RuntimeVmdRig rig;
    readonly MmdMotion motion,faceMotion;
    readonly UpstreamBodySolver solver;
    readonly MmdAdditionalTracks additional;
    readonly ActionBodyPoseLease body;
    readonly ActionFaceMorphLayer face;
    readonly List<MmdMorphBindings.Binding> morphs=new();
    readonly bool[] write;
    readonly bool writeHip;
    readonly Vector3 originHip;
    readonly Vector3 horizontalHipAnchor;
    readonly Quaternion rootBasis;
    bool disposed;
    public int BoneCount {get;}
    public bool HasRootMotion=>writeHip;
    public Vector3 RootDisplacement {get;private set;}
    public Vector3 LoopRootDisplacement {get;}
    Vector3 HorizontalWorld(Vector3 delta)
    {var result=rootBasis*delta;result.y=0;return result;}

    public RuntimeVmdPose(GameObject target,ActionDefinition action,MmdMotion keys,MmdReferencePose referencePose=MmdReferencePose.A)
        :this(target,action,keys,referencePose,null){}
    internal RuntimeVmdPose(GameObject target,ActionDefinition action,MmdMotion keys,MmdReferencePose referencePose,MmdMotion faceKeys)
    {
        rig=MmdRigCache.Rent(target,referencePose);faceMotion=faceKeys??keys;
        try
        {
            motion=rig.BindMotion(keys);
            foreach(string path in action.BonePaths)
                if(!rig.bindings.Any(b=>b.path==path))throw new InvalidDataException("MMD target bone missing: "+path);
            solver=new UpstreamBodySolver(rig.avatar,motion,referencePose==MmdReferencePose.A,controlWaistPivot:rig.ControlWaistPivot);
            additional=new MmdAdditionalTracks(rig,motion,action);
            if(additional.HasBeforeIk)
            {
                if(additional.HasWaistControls)solver.ControlTracks=additional.ApplyControls;
                solver.BeforeIk=additional.ApplyBeforeIk;
                solver.ExtensionFrameCount=(int)motion.lastFrame;
            }
            write=new bool[rig.bindings.Count];int count=0;
            for(int i=0;i<write.Length;i++)
            {
                var b=rig.bindings[i];
                write[i]=(b.human.HasValue&&ActionPolicy.WritesBone(action,b.path))||additional.Owns(i);
                if(write[i])count++;
                if(b.path=="Armature/Hips")writeHip=write[i]&&action.WriteHipTranslation;
            }
            BoneCount=count;body=new ActionBodyPoseLease(target,rig.bindings.Where((b,i)=>write[i]).Select(b=>b.live));
            var unsupported=UpstreamMotionSupport.Tracks(motion).Where(p=>!UpstreamMotionSupport.Contains(p.Key)&&!additional.ContainsTrack(p.Key)&&!motion.ResolvedAliases.Contains(p.Key)&&MmdBoneMap.Animated(p.Value)).Select(p=>p.Key).OrderBy(n=>n,StringComparer.Ordinal);
            global::Plugin.LogSource?.LogInfo("MMD_BONES_BOUND id="+action.Id+" solver=UnityVMDPlayer@2cee8abd targetBones="+rig.bindings.Count+" controlled="+count+" unsupportedAnimated="+string.Join(",",unsupported));
            if(action.WriteFaceMorphs)
            {
                face=new ActionFaceMorphLayer(target,action);
                morphs=MmdMorphBindings.Create(rig.faceRenderer,faceMotion,face,out int ambiguous);
                if(faceMotion.morphs.Count>0)
                    global::Plugin.LogSource?.LogInfo("MMD_MORPHS_BOUND id="+action.Id+" tracks="+faceMotion.morphs.Count+" boundTracks="+morphs.Select(m=>m.Source).Distinct().Count()+" targets="+morphs.Count+" ambiguousTargets="+ambiguous);
            }
            rootBasis=rig.liveRoot.rotation;
            if(writeHip)
            {
                // Keep the starting horizontal placement relative to the
                // avatar. The native idle hip otherwise adds sway on top of
                // the absolute horizontal travel applied by the controller.
                var nativeHip=Quaternion.Inverse(rootBasis)*(rig.hip.position-rig.liveRoot.position);
                horizontalHipAnchor=new Vector3(nativeHip.x,0,nativeHip.z);
            }
            Evaluate(0);originHip=rig.sampleHip.position;
            if(writeHip&&action.Loop)
            {
                additional.RestoreSamples();solver.EvaluateLoopEndpoint();
                additional.Apply(motion.lastFrame);LoopRootDisplacement=HorizontalWorld(rig.sampleHip.position-originHip);
                additional.RestoreSamples();solver.Reset();Evaluate(0);
            }
            if(writeHip)global::Plugin.LogSource?.LogInfo("MMD_VERTICAL_BASELINE id="+action.Id+" mode=avatar-root-reference firstOffsetM="+(originHip.y-rig.ReferenceHipPosition.y).ToString("F5",System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            // A failed solver constructor may already own partial helpers.
            // Retiring the entire hidden tree also covers that unassigned owner.
            rig.Retired=true;
            try
            {
                try{solver?.Dispose();}
                finally{try{body?.Dispose();}finally{face?.RestoreNativeFace();}}
            }
            finally{MmdRigCache.Return(rig);}
            throw;
        }
    }
    void Evaluate(float frame)
    {
        additional.RestoreSamples();solver.Evaluate(frame);additional.Apply(frame);
    }
    public bool Apply(float seconds)
    {
        if(disposed||!rig.IsUsable)return false;
        seconds=Mathf.Clamp(seconds,0,motion.lastFrame/30f);
        Evaluate(seconds*30);
        body.ApplyBaseline();
        var heading=rig.liveRoot.rotation;var sampleHipPosition=rig.sampleHip.position;
        Vector3 hipAnchor;
        if(writeHip)
        {
            RootDisplacement=HorizontalWorld(sampleHipPosition-originHip);
            // Sampled height and the stable starting X/Z follow the current
            // avatar placement, including movement from the controller.
            hipAnchor=rig.liveRoot.position+heading*(horizontalHipAnchor+Vector3.up*sampleHipPosition.y);
        }
        else hipAnchor=rig.hip.position;
        for(int i=0;i<rig.bindings.Count;i++)
        {
            if(!write[i]||additional.UsesNativeDelta(i))continue;
            var b=rig.bindings[i];
            b.live.rotation=heading*b.sample.rotation;
            // Upstream Ghost writes positions. Preserve those positions in the
            // native actor, with its horizontal root delegated to the controller.
            if(b.live!=rig.hip||writeHip)
                b.live.position=hipAnchor+heading*(b.sample.position-sampleHipPosition);
        }
        additional.ApplyNative(seconds*30,heading);
        face?.Begin();
        if(face!=null)foreach(var m in morphs)
            if(m.Renderer!=null)face.Write(m.Renderer,m.Index,faceMotion.Morph(m.Source,seconds));
        MarkGroundContactFrame(seconds);
        return true;
    }
    partial void MarkGroundContactFrame(float seconds);
    partial void ReleaseGroundContact();
    public void RestoreNativeFace()=>face?.RestoreNativeFace();
    public void RestoreNativeBody()=>body.RestoreNativeBody();
    public void BeginNativeBody()=>body.BeginNativeBody();
    public void EndNativeBody(bool succeeded)=>body.EndNativeBody(succeeded);
    public IDisposable PreserveNativeCorrection(ActionBoneCorrection correction)=>body.PreserveNativeCorrection(correction);
    public bool SuppressKneeOffset=>body.SuppressKneeOffset;
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        ReleaseGroundContact();
        try
        {
            try{body?.Dispose();}
            finally{try{face?.RestoreNativeFace();}finally{solver?.Dispose();}}
        }
        catch{rig.Retired=true;throw;}
        finally{MmdRigCache.Return(rig);}
    }
}
