using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Buffers.Binary;

// Offline check only: never injected into game startup or used as a version
// lock. A compiling UI call may still target an unstripping-failure stub.
internal static class UiInteropContract
{
    static int Parameters(MetadataReader reader,BlobHandle signature)
    {
        var blob=reader.GetBlobReader(signature);var header=blob.ReadSignatureHeader();
        if(header.IsGeneric)blob.ReadCompressedInteger();return blob.ReadCompressedInteger();
    }
    internal static void Verify(string game,string plugin)
    {
        using var source=new PEReader(File.OpenRead(plugin));var code=source.GetMetadataReader();int checkedMethods=0;
        foreach(var assembly in new[]{"UnityEngine.CoreModule","UnityEngine.UI","Unity.TextMeshPro"})
        {
            using var pe=new PEReader(File.OpenRead(Path.Combine(game,"BepInEx/interop",assembly+".dll")));var api=pe.GetMetadataReader();
            foreach(var handle in code.MemberReferences)
            {
                var member=code.GetMemberReference(handle);
                if(member.GetKind()!=MemberReferenceKind.Method||member.Parent.Kind!=HandleKind.TypeReference)continue;
                var type=code.GetTypeReference((TypeReferenceHandle)member.Parent);
                if(type.ResolutionScope.Kind!=HandleKind.AssemblyReference)continue;
                if(code.GetString(code.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)!=assembly)continue;
                string name=code.GetString(type.Name),ns=code.GetString(type.Namespace),method=code.GetString(member.Name);
                var found=api.TypeDefinitions.Select(h=>api.GetTypeDefinition(h)).Where(t=>api.GetString(t.Name)==name&&api.GetString(t.Namespace)==ns).ToArray();
                if(found.Length!=1)continue;
                int count=Parameters(code,member.Signature);
                var matches=found[0].GetMethods().Select(h=>api.GetMethodDefinition(h))
                    .Where(m=>api.GetString(m.Name)==method&&Parameters(api,m.Signature)==count).ToArray();
                if(matches.Length!=1||matches[0].RelativeVirtualAddress==0)continue;
                var il=pe.GetMethodBody(matches[0].RelativeVirtualAddress).GetILBytes();checkedMethods++;
                for(int i=0;i+4<il.Length;i++)
                {
                    if(il[i]!=0x72)continue; // ldstr
                    int token=BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(i+1,4));
                    if((token&unchecked((int)0xff000000))!=0x70000000)continue;
                    string literal;
                    try{literal=api.GetUserString(System.Reflection.Metadata.Ecma335.MetadataTokens.UserStringHandle(token&0xffffff));}
                    catch(BadImageFormatException){continue;}
                    if(literal.Contains("unstripping",StringComparison.OrdinalIgnoreCase))
                        throw new Exception("UI/native call resolves to an unstripping stub: "+ns+"."+name+"."+method);
                }
            }
        }
        Console.WriteLine($"PASS {checkedMethods} uniquely resolved Unity/TMP interop method bodies: no unstripping-failure stubs.");
    }
}
