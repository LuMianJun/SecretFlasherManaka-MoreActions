using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seleka2.Actions;

public static class ActionFaceSettings
{
    // The settings UI updates this in the same session; pack=false is a hard veto.
    public static bool KeepGameExpressions { get; internal set; } = true;
}

// Per-pose transient overlay, shared by VMD and Generic clips. Never changes
// the native face state machine, animator parameters, appearance or save data.
public sealed class ActionFaceMorphLayer
{
    // Original PlayerFaceController.OnChangeBody appearance writes. Some eye
    // channels also receive expression curves; keep the custom face intact.
    static readonly HashSet<int> appearance = new() { 55,64,145,146,147,148,149,150,158,159,160,161,162,163,164,165,166,167,168,169,170,199,200,312,313,363,364,365,373,374,375,376,379,380,388,392,393,394,395,400,401,414 };
    sealed class Channel
    {
        internal SkinnedMeshRenderer Renderer;
        internal int Index;
        internal float Native, Output;
    }
    readonly ActionDefinition action;
    readonly List<Channel> channels = new();
    readonly Dictionary<(int renderer,int index),Channel> byIndex = new();
    bool applied, overlay;

    public ActionFaceMorphLayer(GameObject target, ActionDefinition definition)
    {
        action = definition;
        if (!action.WriteFaceMorphs || target == null) return;
        var renderer=FixedModelFace.Find(target);
        if(renderer!=null)foreach(int index in FixedModelFace.NativeExpressions)BindMorph(renderer,index);
    }

    // VMD binding registers the declared MMD channels before Begin.
    // Native customization is protected even if an imported track has the same name.
    public static bool IsProtectedMorph(SkinnedMeshRenderer renderer,int index)
    {
        if(renderer==null||renderer.sharedMesh==null||index<0||index>=renderer.sharedMesh.blendShapeCount)return true;
        return renderer.name=="Body.001"&&renderer.sharedMesh.blendShapeCount>=420&&appearance.Contains(index);
    }
    public bool BindMorph(SkinnedMeshRenderer renderer,int index)
    {
        if(!action.WriteFaceMorphs||IsProtectedMorph(renderer,index))return false;
        if(applied)throw new InvalidOperationException("Bind face channels before applying the expression layer.");
        var key=(renderer.GetInstanceID(),index);
        if(byIndex.ContainsKey(key))return true;
        var channel=new Channel{Renderer=renderer,Index=index};channels.Add(channel);byIndex.Add(key,channel);
        return true;
    }

    public void Begin()
    {
        RestoreNativeFace();
        overlay = action.KeepGameExpressions && ActionFaceSettings.KeepGameExpressions;
        foreach (var channel in channels)
        {
            if (channel.Renderer == null) continue;
            channel.Native = channel.Renderer.GetBlendShapeWeight(channel.Index);
            channel.Output = overlay ? channel.Native : 0;
            if (!overlay) channel.Renderer.SetBlendShapeWeight(channel.Index,0);
        }
        applied = true;
    }

    public void Write(SkinnedMeshRenderer renderer, int index, float motionWeight)
    {
        if (!applied || renderer == null || !float.IsFinite(motionWeight) || !byIndex.TryGetValue((renderer.GetInstanceID(),index),out var channel)) return;
        channel.Output = Mathf.Clamp((overlay ? channel.Native : 0) + motionWeight,0,100);
        renderer.SetBlendShapeWeight(index,channel.Output);
    }

    public void RestoreNativeFace()
    {
        if (!applied) return;
        applied = false;
        foreach (var channel in channels)
        {
            // Preserve any newer native write made after our last overlay.
            if (channel.Renderer != null && channel.Renderer.GetBlendShapeWeight(channel.Index) == channel.Output)
                channel.Renderer.SetBlendShapeWeight(channel.Index,channel.Native);
        }
    }
}
