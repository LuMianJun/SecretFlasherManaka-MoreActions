using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static class SettingsInteropContract
{
    internal static void Verify(string game,string plugin,string mmd)
    {
        using var gamePe=new PEReader(File.OpenRead(Path.Combine(game,"BepInEx/interop/Assembly-CSharp.dll")));
        var native=gamePe.GetMetadataReader();
        void Require(string typeName,params string[] names)
        {
            var type=native.TypeDefinitions.Select(h=>native.GetTypeDefinition(h)).Single(t=>native.GetString(t.Name)==typeName);
            var methods=type.GetMethods().Select(h=>native.GetString(native.GetMethodDefinition(h).Name)).ToHashSet();
            foreach(var name in names)if(!methods.Contains(name))throw new Exception("Native settings method missing: "+typeName+"."+name);
        }
        Require("OptionMenuView","Initialize","CommonOnClickProcess","OnCloseChild","get_currentOpen","set_currentOpen","set_currentOpenView");
        Require("GameSystemMenuView","Update","Close","AllReset","OnAnimCloseEnd");
        Require("ButtonView","Initialize","RegisterAction","SetText");
        using var pe=new PEReader(File.OpenRead(plugin));var metadata=pe.GetMetadataReader();
        var required=new[]{"ActionSettingsApi","MoreActionsSettingsPage","NativeAnimationReplacements","ReplacementSelection","ReplacementCatalog","ReplacementBrowserModel","ReplacementUi","NativeSettingsUi","ReplacementBrowserView","GameAnimationNames","MoreActionsSettingsClosePatch","MoreActionsSettingsResetPatch","MoreActionsCategoryPatch"};
        var types=metadata.TypeDefinitions.Select(h=>metadata.GetString(metadata.GetTypeDefinition(h).Name)).ToHashSet();
        foreach(var type in required)if(!types.Contains(type))throw new Exception("Settings/replacement owner missing: "+type);
        var animationCalls=new HashSet<string>();
        foreach(var handle in metadata.MemberReferences)
        {
            var member=metadata.GetMemberReference(handle);if(member.Parent.Kind!=HandleKind.TypeReference)continue;
            var type=metadata.GetTypeReference((TypeReferenceHandle)member.Parent);string typeName=metadata.GetString(type.Name),method=metadata.GetString(member.Name);
            if(typeName=="AnimatorOverrideController")
            {
                if(method==".ctor")
                {
                    // IL2CPP wrappers may wrap an existing native pointer.
                    // ECMA-335: instance, one argument, void, native int.
                    // Object creation itself must use native Internal_Create.
                    if(!metadata.GetBlobBytes(member.Signature).SequenceEqual(new byte[]{0x20,0x01,0x01,0x18}))
                        throw new Exception("Restored override constructor still used; only the IntPtr wrapper is allowed");
                    continue;
                }
                if(method is "GetOverrides" or "ApplyOverrides" or "get_Item" or "set_Item" or "get_clips" or "set_clips")throw new Exception("Restored override helper still used: "+method);
                animationCalls.Add(method);
            }
            if(typeName=="GameSystemMenuView"&&method is "Initialize" or "Close" or "AllReset")throw new Exception("Custom page must not invoke native OptionData save/reset path: "+method);
            if(typeName=="UnityEventBase"&&method=="GetPersistentMethodName")throw new Exception("Restored persistent-listener helper must not be used");
            if(typeName=="SettingSelectItemView")throw new Exception("Replacement browser still depends on native settings selectors");
        }
        foreach(var method in new[]{"Internal_Create","GetClip","SetClip","SendNotification"})if(!animationCalls.Contains(method))throw new Exception("Direct override entry not referenced: "+method);
        using var mmdPe=new PEReader(File.OpenRead(mmd));var mmdMeta=mmdPe.GetMetadataReader();
        bool joined=false;
        foreach(var handle in mmdMeta.MemberReferences)
        {
            var member=mmdMeta.GetMemberReference(handle);if(member.Parent.Kind!=HandleKind.TypeReference)continue;
            var type=mmdMeta.GetTypeReference((TypeReferenceHandle)member.Parent);
            if(mmdMeta.GetString(type.Name)=="ActionSettingsApi"&&mmdMeta.GetString(member.Name)=="Toggle")joined=true;
        }
        if(!joined)throw new Exception("MMD settings still use a separate page registry");
        UiInteropContract.Verify(game,plugin);
        Console.WriteLine("PASS settings/replacement metadata: native menu hooks present, custom page excludes native save/reset methods, MMD joins one registry, overrides use native creation/clip entries with IntPtr wrappers only. UI dispatch requires gameplay verification.");
    }
}
