using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SFM.Actions;

namespace SFM.Mmd;

// Bind the actual available subset of the shared fixed-character preset.
// Runtime renderer lists never expand that preset or admit accessory bones.
internal static class MmdBoneMap
{
    static readonly object cacheLock=new();
    static readonly Dictionary<string,IReadOnlyDictionary<string,string>> maps=new(StringComparer.Ordinal);
    static readonly Queue<string> mapOrder=new();
    internal static IReadOnlyDictionary<string,string> Create(IEnumerable<string> paths)
    {
        var all=paths.Distinct(StringComparer.Ordinal).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        string key=string.Join("\n",all);
        lock(cacheLock)
        {
            if(maps.TryGetValue(key,out var cached))return cached;
            var created=new ReadOnlyDictionary<string,string>(Build(all));
            if(maps.Count>=16)maps.Remove(mapOrder.Dequeue());
            maps.Add(key,created);mapOrder.Enqueue(key);return created;
        }
    }
    static Dictionary<string,string> Build(string[] all)
    {
        var availablePaths=new HashSet<string>(all,StringComparer.Ordinal);
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var group in all.GroupBy(p=>p.Substring(p.LastIndexOf('/')+1)))
        {
            bool unique=group.Count()==1;
            foreach(var path in group)
            {
                var source=MotionRig.MmdSource(path,availablePaths);
                if(source!=null)result.Add(path,source);
                // Production snapshots contain only preset paths. Internal
                // offline tools also accept custom topologies; ambiguous leaf
                // names retain their full paths instead of guessing a source.
                else if(!MotionRig.IsSupportedPath(path))result.Add(path,unique?group.Key:path);
            }
        }
        return result;
    }

    internal static string Normalize(string name)
    {
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
