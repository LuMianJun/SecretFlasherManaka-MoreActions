using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SFM.Actions;
using UnityEngine;

namespace SFM.Mmd;

internal static class MmdMorphBindings
{
    static readonly HashSet<string> mouthNames = FixedModelFace.MmdMorphs
        .Where(entry => entry.Index >= 443 && entry.Index <= 462)
        .Select(entry => entry.Name.Normalize(NormalizationForm.FormKC)).ToHashSet(StringComparer.Ordinal);
    internal static bool IsMouthMorph(string name) => mouthNames.Contains(name.Normalize(NormalizationForm.FormKC));

    // Overlay whole named tracks once during preparation. Preserve each layer's
    // spelling/ambiguity rules while replacing normalized aliases from below.
    internal static MmdMotion Compose(MmdMotion motion, MmdMotion face, MmdMotion lip)
    {
        if (face == null && lip == null) return motion;
        var result = new MmdMotion { lastFrame = motion.lastFrame };
        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Overlay(MmdMotion layer, bool mouthOnly)
        {
            if (layer == null) return;
            foreach (var group in layer.morphs.GroupBy(entry => entry.Key.Normalize(NormalizationForm.FormKC), StringComparer.Ordinal))
            {
                if (mouthOnly && !mouthNames.Contains(group.Key)) continue;
                if (names.TryGetValue(group.Key, out var previous))
                    foreach (string name in previous) result.morphs.Remove(name);
                names[group.Key] = group.Select(entry => entry.Key).ToList();
                foreach (var entry in group) result.morphs[entry.Key] = entry.Value;
            }
        }
        Overlay(motion, false);
        Overlay(face, false);
        Overlay(lip, true);
        return result;
    }

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
