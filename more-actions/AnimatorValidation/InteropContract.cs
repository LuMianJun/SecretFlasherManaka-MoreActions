using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class InteropContract
{
    internal static void Verify(string game, string plugin)
    {
        using var modulePe = new PEReader(File.OpenRead(Path.Combine(game,"BepInEx/interop/UnityEngine.AnimationModule.dll")));
        var module = modulePe.GetMetadataReader();
        using var corePe=new PEReader(File.OpenRead(Path.Combine(game,"BepInEx/interop/UnityEngine.CoreModule.dll")));
        var core=corePe.GetMetadataReader();
        HashSet<string> Methods(MetadataReader reader,string typeName)
        {
            var type = reader.TypeDefinitions.Select(h=>reader.GetTypeDefinition(h)).Single(t=>reader.GetString(t.Name)==typeName);
            return type.GetMethods().Select(h=>reader.GetString(reader.GetMethodDefinition(h).Name)).ToHashSet();
        }
        var allowed=new Dictionary<string,string[]> {
            ["AnimationClipPlayable"]=new[]{"CreateHandleInternal_Injected","SetApplyFootIKInternal","SetApplyPlayableIKInternal","SetOverrideLoopTimeInternal","SetLoopTimeInternal"},
            ["AnimationPlayableGraphExtensions"]=new[]{"InternalCreateAnimationOutput"},
            ["AnimationPlayableOutput"]=new[]{"InternalSetTarget"},
            ["PlayableGraph"]=new[]{"Create_Injected","SetTimeUpdateMode_Injected","Play_Injected","Evaluate_Injected","IsValid_Injected","Destroy_Injected"},
            ["PlayableHandle"]=new[]{"SetDuration_Injected","SetTime_Injected","SetDone_Injected"},
            ["PlayableOutputHandle"]=new[]{"SetSourcePlayable_Injected","SetWeight_Injected"}
        };
        foreach(var pair in allowed)
        {
            var methods=Methods(pair.Key.StartsWith("Animation")?module:core,pair.Key);
            foreach(var method in pair.Value)if(!methods.Contains(method))throw new Exception("Native entry missing: "+pair.Key+"."+method);
        }
        using var pluginPe = new PEReader(File.OpenRead(plugin));var metadata=pluginPe.GetMetadataReader();
        var called=new HashSet<string>();
        foreach(var handle in metadata.MemberReferences)
        {
            var member=metadata.GetMemberReference(handle);
            if(member.Parent.Kind!=HandleKind.TypeReference)continue;
            var type=metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            string typeName=metadata.GetString(type.Name);
            string ns=metadata.GetString(type.Namespace);
            if((ns!="UnityEngine.Animations"&&ns!="UnityEngine.Playables")||!allowed.TryGetValue(typeName,out var methods))continue;
            string name=metadata.GetString(member.Name);
            if(!methods.Contains(name))throw new Exception("Plugin still calls a playback wrapper instead of a native entry: "+typeName+"."+name);
            called.Add(typeName+"."+name);
        }
        if(called.Count!=allowed.Values.Sum(a=>a.Length))throw new Exception("Not all expected native playback entries are referenced");
        Console.WriteLine($"PASS actual interop metadata: all {called.Count} graph/clip/output/clock/cleanup calls use native entries; no restored factories, constructors or conversion helpers. Native evaluation still requires gameplay testing.");
    }
}
