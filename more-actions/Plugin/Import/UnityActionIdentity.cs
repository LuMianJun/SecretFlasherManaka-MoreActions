using System;
using System.IO;

namespace SFM.Actions;

internal static class UnityActionIdentity
{
    internal static string LegacyBundleId(string relative, string asset)
        => ("bundle:" + relative.Replace('\\', '/') + ":" + asset).Replace(',', '_');
    static string Part(string value)=>value.Replace("%","%25").Replace(",","%2C").Replace("/","%2F").Replace("\\","%5C");
    internal static string ClipKey(string asset)
    {
        var name=(asset??"").Replace('\\','/');name=name.Substring(name.LastIndexOf('/')+1);
        return name.EndsWith(".anim",StringComparison.OrdinalIgnoreCase)?name.Substring(0,name.Length-5):name;
    }
    internal static string ForClip(string root,string directory,string clip)
    {
        if(string.IsNullOrWhiteSpace(clip))throw new ArgumentException("Animation clip name is required");
        var folder=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(directory)).Replace('\\','/');
        if(folder==".."||folder.StartsWith("../",StringComparison.Ordinal)||Path.IsPathRooted(folder))throw new ArgumentException("Action folder escapes the Actions directory");
        if(folder==".")folder=new DirectoryInfo(root).Name;
        return string.Join("/",Array.ConvertAll(folder.Split('/'),Part))+"/"+Part(clip);
    }
}
