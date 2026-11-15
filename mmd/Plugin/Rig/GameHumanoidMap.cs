using System;
using UnityEngine;

namespace SFM.Mmd;

// The shared JSON remains the path inventory. This adapter translates the
// game's leaf naming convention to Unity's Humanoid identifiers; it contains
// no second hierarchy or MMD grant/IK definitions.
internal static class GameHumanoidMap
{
    internal static bool TryGet(string path, out HumanBodyBones human)
    {
        string name=path.Substring(path.LastIndexOf('/')+1);
        if(name is "Hips" or "Spine" or "Chest" or "Neck" or "Head")
            return Enum.TryParse(name,out human);
        string side=name.EndsWith("_L",StringComparison.Ordinal)?"Left":name.EndsWith("_R",StringComparison.Ordinal)?"Right":null;
        if(side==null){human=default;return false;}
        string part=name.Substring(0,name.Length-2);
        if(part=="Toe")part="Toes";
        if(part is "Shoulder" or "UpperArm" or "LowerArm" or "Hand" or "UpperLeg" or "LowerLeg" or "Foot" or "Toes"
            ||part.StartsWith("Thumb",StringComparison.Ordinal)||part.StartsWith("Index",StringComparison.Ordinal)
            ||part.StartsWith("Middle",StringComparison.Ordinal)||part.StartsWith("Ring",StringComparison.Ordinal)||part.StartsWith("Little",StringComparison.Ordinal))
            return Enum.TryParse(side+part,out human);
        human=default;return false;
    }
}
