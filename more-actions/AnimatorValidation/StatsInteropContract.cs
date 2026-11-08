using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class StatsInteropContract
{
    static int Parameters(MetadataReader reader,BlobHandle signature)
    {
        var blob=reader.GetBlobReader(signature);var header=blob.ReadSignatureHeader();
        if(header.IsGeneric)blob.ReadCompressedInteger();
        return blob.ReadCompressedInteger();
    }
    internal static void Verify(string game,string plugin)
    {
        using var gamePe=new PEReader(File.OpenRead(Path.Combine(game,"BepInEx/interop/Assembly-CSharp.dll")));
        var native=gamePe.GetMetadataReader();
        void Method(string full,string name,int parameters)
        {
            var type=native.TypeDefinitions.Select(native.GetTypeDefinition).Single(t=>native.GetString(t.Namespace)+"."+native.GetString(t.Name)==full);
            var matches=type.GetMethods().Select(native.GetMethodDefinition).Count(m=>native.GetString(m.Name)==name&&Parameters(native,m.Signature)==parameters);
            if(matches!=1)throw new Exception($"Missing/ambiguous stat API: {full}.{name}/{parameters}");
        }
        var expected=new Dictionary<string,int>
        {
            ["PlayerEcstasyController.OnUpdate"]=0,["PlayerMiscProcess.CalcDokidoki"]=0,
            ["PlayerMoistureManager.OnUpdate"]=0,["StaminaManager.OnUpdate"]=0,
            ["PlayerPeeController.OnUpdate"]=1,["PlayerStrangenessController.CalcExposeStrange"]=1,
            ["PlayerStrangenessController.CalcMaxDiscomfort"]=1,["MissionPoseExpose.IsInCondition"]=0
        };
        using var pe=new PEReader(File.OpenRead(plugin));var code=pe.GetMetadataReader();int patches=0;
        foreach(var handle in code.TypeDefinitions)
        {
            var type=code.GetTypeDefinition(handle);string name=code.GetString(type.Name);
            if(!name.EndsWith("StatsPatch")&&name!="ActionPoseMissionPatch")continue;
            foreach(var attrHandle in type.GetCustomAttributes())
            {
                var attr=code.GetCustomAttribute(attrHandle);
                if(attr.Constructor.Kind!=HandleKind.MemberReference)continue;
                var constructor=code.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                if(constructor.Parent.Kind!=HandleKind.TypeReference)continue;
                var owner=code.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                if(code.GetString(owner.Namespace)!="HarmonyLib"||code.GetString(owner.Name)!="HarmonyPatch")continue;
                var blob=code.GetBlobReader(attr.Value);
                if(blob.ReadUInt16()!=1)throw new Exception("Invalid Harmony attribute");
                string full=blob.ReadSerializedString()!.Split(',')[0];
                string method=blob.ReadSerializedString()!;
                string key=full.Substring(full.LastIndexOf('.')+1)+"."+method;
                if(!expected.Remove(key,out int args))throw new Exception("Unexpected stat hook: "+key);
                Method(full,method,args);patches++;
            }
        }
        if(expected.Count!=0)throw new Exception("Missing stat patches: "+string.Join(",",expected.Keys));
        const string player="ExposureUnnoticed2.Object3D.Player.Scripts.";
        Method(player+"PlayerPeeController","StartPee",0);
        Method(player+"StaminaManager","ConsumePerSecond",1);
        Method(player+"PlayerFacade","AddAddMoisture",1);
        Method(player+"PlayerFacade","GetExposedBodyPartsByClothesAndCostume",0);
        Method("ExposureUnnoticed2.Master.Action.MAction","Get",1);
        Console.WriteLine($"PASS {patches} native stat hook targets and 5 native call APIs match local interop; detour execution requires game verification.");
    }
}
