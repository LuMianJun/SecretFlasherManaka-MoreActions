using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class InstalledContract
{
    internal static void Verify(string gameDirectory, string crowdDll, string motionDll)
    {
        CheckApi(Path.Combine(gameDirectory,"BepInEx","interop","Assembly-CSharp.dll"),new()
        {
            ["NpcManager"]=new[]{"OnUpdate","get_ExistNpcList","get_initialized"},
            ["NpcController"]=new[]{"ChangeState","get_Nca","get_id"},
            ["NpcLocomotion"]=new[]{"OnUpdate","OnFixedUpdate","SimpleUpdateFromOtherState","SetMoveZero","get_nca","get_lastSetDestinationPoint","set_lastSetDestinationPoint","get_targetRotation","set_targetRotation"},
            ["NpcAnimationManager"]=new[]{"SetSpeed","GetCurrentMoveSpeed","get_speed"},
            ["NpcStateModel"]=new[]{"get_IsPolice","get_CurrentFixedType","get_CurrentState","get_RouteData","get_HasInteractToken"},
            ["PlayerController"]=new[]{"get_Instance","get_Pca"},
            ["InGameManager"]=new[]{"get_TempInGameState"}
        });
        CheckApi(Path.Combine(gameDirectory,"BepInEx","interop","UnityEngine.AIModule.dll"),new()
        {
            ["NavMesh"]=new[]{"SamplePosition"},
            ["NavMeshAgent"]=new[]{"CalculatePath","SetDestination","ResetPath","get_isOnNavMesh","get_isStopped","set_isStopped"},
            ["NavMeshPath"]=new[]{"get_status"}
        });
        CheckApi(motionDll,new(){["MotionPlaybackApi"]=new[]{"get_IsPlaying","get_CurrentMotionId","add_PlaybackChanged","remove_PlaybackChanged"}});
        using var crowd=new PEReader(File.OpenRead(crowdDll));var cm=crowd.GetMetadataReader();
        using var motion=new PEReader(File.OpenRead(motionDll));var mm=motion.GetMetadataReader();
        string[] cr=cm.AssemblyReferences.Select(h=>cm.GetString(cm.GetAssemblyReference(h).Name)).ToArray();
        string[] mr=mm.AssemblyReferences.Select(h=>mm.GetString(mm.GetAssemblyReference(h).Name)).ToArray();
        if(cr.Contains("Seleka2.VmdProbe")||mr.Contains("Seleka2.NpcSpectators"))throw new Exception("Plugins have a mandatory assembly dependency.");
        bool nativeApproach=false;
        foreach(var handle in cm.MemberReferences)
        {
            var member=cm.GetMemberReference(handle);if(member.Parent.Kind!=HandleKind.TypeReference)continue;
            string owner=cm.GetString(cm.GetTypeReference((TypeReferenceHandle)member.Parent).Name),name=cm.GetString(member.Name);
            if(owner=="NpcLocomotion"&&name=="SimpleUpdateFromOtherState")nativeApproach=true;
            if(owner=="NpcLocomotion"&&name=="set_moveV"||owner=="NavMeshAgent"&&(name=="Raycast"||name=="SetPath"||name=="set_updatePosition"||name=="set_updateRotation"))
                throw new Exception("Custom spectator movement owner still compiled: "+owner+"."+name);
        }
        if(!nativeApproach)throw new Exception("Native spectator approach call missing");
        if(cm.TypeDefinitions.Any(h=>cm.GetString(cm.GetTypeDefinition(h).Name)=="CrowdFixedStepPatch"))throw new Exception("Old custom fixed-step patch still compiled");
        Console.WriteLine("PASS: native target approach; no custom movement velocity or navigation-step override");
        Console.WriteLine("PASS: two separate DLLs without mutual assembly references; optional playback API exists");
    }
    static void CheckApi(string file,Dictionary<string,string[]> contract)
    {
        using var pe=new PEReader(File.OpenRead(file));var metadata=pe.GetMetadataReader();
        foreach(var (name,required) in contract)
        {
            var type=metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(t=>metadata.GetString(t.Name)==name);
            var methods=type.GetMethods().Select(h=>metadata.GetString(metadata.GetMethodDefinition(h).Name)).ToHashSet();
            foreach(var method in required)if(!methods.Contains(method))throw new Exception("API missing: "+name+"."+method);
            Console.WriteLine("PASS: installed API "+name);
        }
    }
}
