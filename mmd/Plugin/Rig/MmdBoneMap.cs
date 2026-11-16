using System;
using System.Collections.Generic;
using System.Linq;

namespace SFM.Mmd;

// File-name compatibility and managed track auditing. Humanoid binding lives
// in GameHumanoidMap; standard solve mapping stays in the upstream core.
internal static class MmdBoneMap
{
    static readonly char[] NamePadding={' ','\u3000'};

    internal static string Normalize(string name)
    {
        name=name.TrimEnd(NamePadding);
        name=name.Replace("ＩＫ親","IK親").Replace("足IK","足ＩＫ").Replace("つま先IK","つま先ＩＫ");
        // IK parents use ASCII IK in the semi-standard convention.
        name=name.Replace("足ＩＫ親","足IK親").Replace("人差指","人指").Replace("捩れ","捩");
        if(name.StartsWith("上半身",StringComparison.Ordinal)) name=name.Replace('２','2').Replace('３','3');
        if(name.Contains("指",StringComparison.Ordinal))
            for(int i=0;i<=3;i++) name=name.Replace((char)('0'+i),(char)('０'+i));
        return name;
    }

    internal static bool Animated(System.Collections.Generic.List<MmdMotion.Key> keys)
        // Runs during background parsing. Unity's Vector3 properties invoke
        // native IL2CPP methods; read the struct fields and use managed math.
        => keys.Any(k=>(double)k.p.x*k.p.x+(double)k.p.y*k.p.y+(double)k.p.z*k.p.z>1e-10f || (double)k.q.x*k.q.x+(double)k.q.y*k.q.y+(double)k.q.z*k.q.z>1e-8);

}
