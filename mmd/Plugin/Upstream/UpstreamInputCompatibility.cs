using System;
using System.Collections.Generic;
using System.Linq;

namespace SFM.Mmd.Upstream;

// File-format compatibility only. The reader and body solver still receive
// exactly the same data as upstream reading a VMD with these names written in.
internal static class UpstreamInputCompatibility
{
    internal static string BoneName(string name)
    {
        var text=name.Split('\0')[0];
        return Reader.VMDReader.BoneKeyFrameGroup.StringBoneNames.Contains(text)?text:MmdBoneMap.Normalize(text);
    }
    internal static MmdMotion Apply(MmdMotion motion)
    {
        if(motion==null)throw new ArgumentNullException(nameof(motion));
        if(motion.BodyInputCompatible||motion.RawBoneKeys.Count==0)return motion;
        var result=motion.BindAliases(Array.Empty<KeyValuePair<string,string>>());
        for(int i=0;i<result.RawBoneKeys.Count;i++)
        {
            var key=result.RawBoneKeys[i];result.RawBoneKeys[i]=(BoneName(key.Name),key.Key);
        }
        for(int i=0;i<result.RawIkFrames.Count;i++)
        {
            var frame=result.RawIkFrames[i];
            result.RawIkFrames[i]=(frame.Frame,frame.Display,frame.Entries.Select(entry=>new KeyValuePair<string,bool>(BoneName(entry.Key),entry.Value)).ToList());
        }
        result.BodyInputCompatible=true;return result;
    }
}
