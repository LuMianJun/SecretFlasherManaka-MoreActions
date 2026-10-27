using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seleka2.Actions;

// Owns only explicitly selected body transforms. Native animation still runs
// for body customization, facial animation and accessory physics.
public sealed class ActionBodyPoseLease : IActionBodyPose, IDisposable
{
    readonly Transform[] bones;
    readonly State[] raw, native;
    readonly Transform[][] correctionBones;
    readonly Animator animator;
    readonly AnimatorCullingMode savedCulling;
    bool nativePass, applied, restoredForUpdate, disposed;
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

    sealed class Correction : IDisposable
    {
        readonly Transform[] bones;
        readonly State[] states;
        bool restored;
        internal Correction(Transform[] bones)
        {
            this.bones=bones; states=new State[bones.Length];
            for(int i=0;i<bones.Length;i++) states[i]=new State(bones[i]);
        }
        public void Dispose()
        {
            if(restored)return; restored=true;
            for(int i=0;i<bones.Length;i++)states[i].Restore(bones[i]);
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
        Restore(raw); applied=false; restoredForUpdate=true;
    }
    public void BeginNativeBody()
    {
        if(disposed)return;
        Capture(raw); nativePass=true; restoredForUpdate=false;
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
        return selected.Length==0?null:new Correction(selected);
    }
    public bool SuppressKneeOffset => !disposed&&nativePass&&ownsLeftKnee;

    // Native body customization has run exactly once. Its positions/scales
    // form the live base; the provider supplies its own rotations and root Y.
    public void ApplyBaseline()
    {
        if(disposed)throw new ObjectDisposedException(nameof(ActionBodyPoseLease));
        Restore(native); applied=true; restoredForUpdate=false;
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true; nativePass=false;
        try { Restore(restoredForUpdate?raw:native); }
        finally
        { if(animator!=null&&animator.cullingMode==AnimatorCullingMode.AlwaysAnimate)animator.cullingMode=savedCulling; }
    }
}
