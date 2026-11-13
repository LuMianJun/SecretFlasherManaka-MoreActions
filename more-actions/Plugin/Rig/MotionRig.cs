namespace SFM.Actions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public static class MotionRig
{
    sealed class Bone
    {
        internal string Path, Source, MissingPath, FallbackSource;
        internal bool Required;
    }
    sealed class Preset
    {
        internal string[] Supported, Required;
        internal HashSet<string> SupportedSet, RequiredSet;
        internal Dictionary<string,Bone> Bones;
    }
    static readonly Lazy<Preset> preset=new(Load);

    // Resolve relative to the shared core DLL, including calls from MMD.
    // The JSON is the only preset; a failed load never invents a second one.
    static Preset Load()
    {
        string file=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(MotionRig).Assembly.Location),"body-bones.json");
        try { return Parse(File.ReadAllText(file)); }
        catch(Exception error) when(error is IOException||error is UnauthorizedAccessException||error is JsonException||error is InvalidDataException)
        { throw new InvalidDataException("Cannot load body bone preset '"+file+"': "+error.Message,error); }
    }

    static bool ValidPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!path.StartsWith("Armature/",StringComparison.Ordinal)||path.Contains('\\'))return false;
        return path.Split('/').All(part=>part.Length>0&&part!="."&&part!=".."&&part==part.Trim()&&!part.Any(char.IsControl)&&part.IndexOfAny(new[]{':','*','?','"','<','>','|'})<0);
    }
    static string Text(JsonElement item,string field)
    {
        if(!item.TryGetProperty(field,out var value)||value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("Bone preset requires a nonempty "+field+" string");
        string text=value.GetString();
        if(text!=text.Trim()||text.Any(char.IsControl))throw new InvalidDataException("Invalid bone preset "+field+": "+text);
        return text;
    }
    static Preset Parse(string json)
    {
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("Schema",out var schema)||schema.ValueKind!=JsonValueKind.Number||!schema.TryGetInt32(out int version)||version!=1)
            throw new InvalidDataException("Unsupported body bone preset Schema");
        if(!root.TryGetProperty("Bones",out var entries)||entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()==0)
            throw new InvalidDataException("Body bone preset requires a nonempty Bones array");
        var bones=new Dictionary<string,Bone>(StringComparer.Ordinal);
        foreach(var item in entries.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Bone preset entry must be an object");
            var bone=new Bone{Path=Text(item,"Path"),Source=Text(item,"Source")};
            if(!ValidPath(bone.Path))throw new InvalidDataException("Invalid bone preset Path: "+bone.Path);
            if(!item.TryGetProperty("Required",out var required)||(required.ValueKind!=JsonValueKind.True&&required.ValueKind!=JsonValueKind.False))
                throw new InvalidDataException("Bone preset requires a Required boolean: "+bone.Path);
            bone.Required=required.GetBoolean();
            if(item.TryGetProperty("Fallback",out var fallback))
            {
                if(fallback.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid bone preset Fallback: "+bone.Path);
                bone.MissingPath=Text(fallback,"WhenMissing");bone.FallbackSource=Text(fallback,"Source");
                if(!ValidPath(bone.MissingPath)||bone.MissingPath==bone.Path)throw new InvalidDataException("Invalid fallback dependency: "+bone.Path);
            }
            if(!bones.TryAdd(bone.Path,bone))throw new InvalidDataException("Duplicate bone preset Path: "+bone.Path);
        }
        foreach(var bone in bones.Values)
        {
            string parent=bone.Path.Substring(0,bone.Path.LastIndexOf('/'));
            if(parent!="Armature"&&!bones.ContainsKey(parent))throw new InvalidDataException("Bone preset parent missing: "+parent);
            if(bone.Required&&parent!="Armature"&&!bones[parent].Required)throw new InvalidDataException("Required bone has an optional parent: "+bone.Path);
            if(bone.MissingPath!=null&&!bones.ContainsKey(bone.MissingPath))throw new InvalidDataException("Unknown fallback dependency: "+bone.MissingPath);
        }
        var requiredPaths=bones.Values.Where(b=>b.Required).Select(b=>b.Path).ToArray();
        if(requiredPaths.Length==0)throw new InvalidDataException("Body bone preset requires core bones");
        return new Preset{Bones=bones,Supported=bones.Keys.ToArray(),Required=requiredPaths,SupportedSet=bones.Keys.ToHashSet(StringComparer.Ordinal),RequiredSet=requiredPaths.ToHashSet(StringComparer.Ordinal)};
    }

    // Startup verifies the shared resource before any player-facing setup.
    public static void Initialize() { _=preset.Value; }

    // Iterators keep the cached arrays private to the preset.
    public static IEnumerable<string> BonePaths()
    { foreach(string path in preset.Value.Supported)yield return path; }
    public static IEnumerable<string> RequiredBonePaths()
    { foreach(string path in preset.Value.Required)yield return path; }
    public static bool IsRequiredPath(string path)=>path!=null&&preset.Value.RequiredSet.Contains(path);
    public static bool IsSupportedPath(string path)=>path!=null&&preset.Value.SupportedSet.Contains(path);

    public static string MmdSource(string path,ISet<string> availablePaths)
    {
        if(availablePaths==null)throw new ArgumentNullException(nameof(availablePaths));
        if(path==null||!preset.Value.Bones.TryGetValue(path,out var bone))return null;
        return bone.MissingPath!=null&&!availablePaths.Contains(bone.MissingPath)?bone.FallbackSource:bone.Source;
    }
}
