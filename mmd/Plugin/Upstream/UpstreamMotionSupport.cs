using System;
using System.Collections.Generic;
using System.Linq;

namespace SFM.Mmd.Upstream;

// Tracks consumed by the body solver with this game's humanoid binding. Reader
// enum membership alone does not mean an eye/twist/extension track is driven.
internal static class UpstreamMotionSupport
{
    static readonly HashSet<string> names=new(
        UpstreamBodySolver.StandardMapping.Values.Select(name=>name.ToString())
        .Concat(new[]{"全ての親","グルーブ","下半身","左足ＩＫ","右足ＩＫ","左つま先ＩＫ","右つま先ＩＫ"}),
        StringComparer.Ordinal);
    internal static bool Contains(string name)=>names.Contains(name);
    internal static IEnumerable<KeyValuePair<string,List<MmdMotion.Key>>> Tracks(MmdMotion motion)
        =>motion.RawBoneKeys.Count==0?motion.tracks:
            motion.RawBoneKeys.GroupBy(key=>key.Name,StringComparer.Ordinal)
            .Select(group=>new KeyValuePair<string,List<MmdMotion.Key>>(group.Key,group.Select(key=>key.Key).ToList()));
}
