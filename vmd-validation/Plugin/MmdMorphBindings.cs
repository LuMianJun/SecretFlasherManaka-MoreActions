using System;
using System.Collections.Generic;
using System.Text;
using Seleka2.Actions;
using UnityEngine;

namespace Seleka2.Mmd;

internal static class MmdMorphBindings
{
    internal sealed record Binding(SkinnedMeshRenderer Renderer,int Index,string Source);
    internal static List<Binding> Create(SkinnedMeshRenderer renderer,MmdMotion motion,
        ActionFaceMorphLayer face,out int ambiguous)
    {
        ambiguous=0;var result=new List<Binding>();
        if(renderer==null)return result;
        var normalized=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(string source in motion.morphs.Keys)
        {
            string key=source.Normalize(NormalizationForm.FormKC);
            if(!normalized.TryAdd(key,source))normalized[key]=null;
        }
        foreach(var entry in FixedModelFace.MmdMorphs)
        {
            string source=entry.Name;
            if(!motion.morphs.ContainsKey(source))
            {
                if(!normalized.TryGetValue(source.Normalize(NormalizationForm.FormKC),out source))continue;
                if(source==null){ambiguous++;continue;}
            }
            if(face.BindMorph(renderer,entry.Index))result.Add(new Binding(renderer,entry.Index,source));
        }
        return result;
    }
}
