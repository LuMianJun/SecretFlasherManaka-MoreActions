using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SFM.Actions;

// Owns only explicitly selected body transforms. Native animation still runs
// for body customization, facial animation and accessory physics.
public sealed class ActionBodyPoseLease : IActionBodyPose, IDisposable
{
    readonly Transform[] bones;
    readonly State[] raw, native;
    readonly Transform[][] correctionBones;
    readonly List<CorrectionBuffer>[] correctionBuffers = { new(), new(), new() };
    readonly Animator animator;
    readonly AnimatorCullingMode savedCulling;
    bool nativePass, applied, disposed;
    readonly bool ownsLeftKnee;

    readonly struct State
    {
        readonly Vector3 position, scale;
        readonly Quaternion rotation;
        internal State(Transform bone)
        { position=bone.localPosition; rotation=bone.localRotation; scale=bone.localScale; }
        internal void Restore(Transform bone)
        { if (bone != null) { bone.localPosition=position; bone.localRotation=rotation; bone.localScale=scale; } }
    }

    sealed class CorrectionBuffer
    {
        internal readonly State[] States;
        internal bool InUse;
        internal CorrectionBuffer(int count) { States = new State[count]; }
    }

    // Keep the lease token distinct: a caller may retain and dispose an old
    // token again after a newer scope has reused its state buffer.
    sealed class Correction : IDisposable
    {
        readonly ActionBodyPoseLease owner;
        readonly Transform[] bones;
        readonly CorrectionBuffer buffer;
        bool restored;
        internal Correction(ActionBodyPoseLease owner,Transform[] bones,CorrectionBuffer buffer)
        {
            this.owner=owner;this.bones=bones;this.buffer=buffer;
            for(int i=0;i<bones.Length;i++) buffer.States[i]=new State(bones[i]);
            buffer.InUse=true;
        }
        public void Dispose()
        {
            if(restored)return; restored=true;
            try
            {
                if(owner.disposed)return;
                for(int i=0;i<bones.Length;i++)buffer.States[i].Restore(bones[i]);
            }
            finally { buffer.InUse=false; }
        }
    }

    public ActionBodyPoseLease(GameObject target, IEnumerable<Transform> controlledBones)
    {
        bones=controlledBones.Where(b=>b!=null).Distinct().ToArray();
        raw=new State[bones.Length]; native=new State[bones.Length];
        Capture(raw); Capture(native);
        ownsLeftKnee=bones.Any(b=>b.name=="LowerLeg_L");
        correctionBones=new[] {
            bones.Where(b=>b.name=="LowerLeg_L"||b.name=="LowerLeg_R").ToArray(),
            bones.Where(b=>b.name=="Toe_L"||b.name=="Toe_R").ToArray(),
            bones.Where(b=>b.name=="Head").ToArray()
        };
        animator=target==null?null:target.GetComponent<Animator>();
        if(animator!=null) { savedCulling=animator.cullingMode; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; }
    }

    void Capture(State[] states)
    { for(int i=0;i<bones.Length;i++)if(bones[i]!=null)states[i]=new State(bones[i]); }
    void Restore(State[] states)
    { for(int i=0;i<bones.Length;i++)states[i].Restore(bones[i]); }

    // Executed before native Update/Animator evaluation: remove both MMD's
    // output and the previous frame's additive size adjustment. This also
    // protects bones whose positions are not keyed by the current native clip.
    public void RestoreNativeBody()
    {
        if(disposed||!applied)return;
        Restore(raw); applied=false;
    }
    public void BeginNativeBody()
    {
        if(disposed)return;
        Capture(raw); nativePass=true;
    }
    public void EndNativeBody(bool succeeded)
    {
        if(disposed||!nativePass)return;
        nativePass=false;
        if(succeeded)Capture(native); else Restore(raw);
    }
    public IDisposable PreserveNativeCorrection(ActionBoneCorrection correction)
    {
        if(disposed||!nativePass)return null;
        var selected=correctionBones[(int)correction];
        if(selected.Length==0)return null;
        var buffers=correctionBuffers[(int)correction];
        CorrectionBuffer available=null;
        foreach(var buffer in buffers)if(!buffer.InUse){available=buffer;break;}
        if(available==null){available=new CorrectionBuffer(selected.Length);buffers.Add(available);}
        return new Correction(this,selected,available);
    }
    public bool SuppressKneeOffset => !disposed&&nativePass&&ownsLeftKnee;

    // Native body customization has run exactly once. Its positions/scales
    // form the live base; the provider supplies its own rotations and root Y.
    public void ApplyBaseline()
    {
        if(disposed)throw new ObjectDisposedException(nameof(ActionBodyPoseLease));
        Restore(native); applied=true;
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true; nativePass=false;
        // Once output has been withdrawn (or before its first application),
        // the live transforms belong to native animation again. Do not write
        // constructor/previous-frame snapshots over its newer result.
        try { if(applied)Restore(native); }
        finally
        { if(animator!=null&&animator.cullingMode==AnimatorCullingMode.AlwaysAnimate)animator.cullingMode=savedCulling; }
    }
}
