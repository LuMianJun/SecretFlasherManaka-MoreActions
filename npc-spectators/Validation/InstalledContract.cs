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
            ["NpcLocomotion"]=new[]{"OnUpdate","OnFixedUpdate","SetMoveZero","get_nca","get_moveV","set_moveV","get_skipFixedUpdateFrame","get_lastSetDestinationPoint","set_lastSetDestinationPoint","get_targetRotation","set_targetRotation"},
            ["NpcAnimationManager"]=new[]{"SetSpeed","GetCurrentMoveSpeed","get_speed"},
            ["NpcStateModel"]=new[]{"get_IsPolice","get_CurrentFixedType","get_CurrentState","get_RouteData","get_HasInteractToken"},
            ["PlayerController"]=new[]{"get_Instance","get_Pca"},
            ["InGameManager"]=new[]{"get_TempInGameState"}
        });
        CheckApi(Path.Combine(gameDirectory,"BepInEx","interop","UnityEngine.AIModule.dll"),new()
        {
            ["NavMesh"]=new[]{"SamplePosition"},
            ["NavMeshAgent"]=new[]{"CalculatePath","SetPath","Raycast","SetDestination","ResetPath","get_isOnNavMesh","get_updatePosition","set_updatePosition","get_updateRotation","set_updateRotation","get_baseOffset","get_pathStatus"},
            ["NavMeshPath"]=new[]{"get_status"}
        });
        CheckApi(motionDll,new(){["MotionPlaybackApi"]=new[]{"get_IsPlaying","get_CurrentMotionId","add_PlaybackChanged","remove_PlaybackChanged"}});
        using var crowd=new PEReader(File.OpenRead(crowdDll));var cm=crowd.GetMetadataReader();
        using var motion=new PEReader(File.OpenRead(motionDll));var mm=motion.GetMetadataReader();
        string[] cr=cm.AssemblyReferences.Select(h=>cm.GetString(cm.GetAssemblyReference(h).Name)).ToArray();
        string[] mr=mm.AssemblyReferences.Select(h=>mm.GetString(mm.GetAssemblyReference(h).Name)).ToArray();
        if(cr.Contains("Seleka2.VmdProbe")||mr.Contains("Seleka2.NpcSpectators"))throw new Exception("Plugins have a mandatory assembly dependency.");
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
