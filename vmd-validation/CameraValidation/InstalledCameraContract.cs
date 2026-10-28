using System.Reflection.Metadata;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text.Json;

internal static class InstalledCameraContract
{
    internal static void VerifyPresenceDependency(string file)
    {
        using var pe=new PEReader(File.OpenRead(file));var metadata=pe.GetMetadataReader();
        var plugin=metadata.TypeDefinitions.Select(h=>metadata.GetTypeDefinition(h))
            .Single(t=>metadata.GetString(t.Name)=="Plugin"&&metadata.GetString(t.Namespace)=="");
        int found=0;
        foreach(var h in plugin.GetCustomAttributes())
        {
            var attribute=metadata.GetCustomAttribute(h);
            if(attribute.Constructor.Kind!=HandleKind.MemberReference)continue;
            var ctor=metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if(ctor.Parent.Kind!=HandleKind.TypeReference)continue;
            var type=metadata.GetTypeReference((TypeReferenceHandle)ctor.Parent);
            if(metadata.GetString(type.Name)!="BepInDependency"||metadata.GetString(type.Namespace)!="BepInEx")continue;
            var signature=metadata.GetBlobReader(ctor.Signature);signature.ReadSignatureHeader();
            if(signature.ReadCompressedInteger()!=2||signature.ReadSignatureTypeCode()!=SignatureTypeCode.Void||signature.ReadSignatureTypeCode()!=SignatureTypeCode.String)
                throw new Exception("Unexpected BepInDependency constructor");
            if(signature.ReadByte()!=0x11)throw new Exception("MMD dependency must use presence/load-order, not a version string");
            var blob=metadata.GetBlobReader(attribute.Value);
            if(blob.ReadUInt16()!=1||blob.ReadSerializedString()!="local.seleka2.moreactions"||blob.ReadInt32()!=1)
                throw new Exception("MMD must retain its More Actions hard dependency");
            found++;
        }
        if(found!=1)throw new Exception("Expected one MMD More Actions dependency");
        Console.WriteLine("PASS MMD dependency: More Actions presence/load-order only; no version lock");
    }

    internal static void VerifyDriverInjectionBoundary(string file)
    {
        using var pe = new PEReader(File.OpenRead(file)); var metadata = pe.GetMetadataReader();
        var handle=metadata.TypeDefinitions.Single(h=>metadata.GetString(metadata.GetTypeDefinition(h).Name)=="ActionDriver");
        var driver=metadata.GetTypeDefinition(handle);
        bool Hidden(CustomAttributeHandleCollection attributes)
        {
            foreach(var h in attributes)
            {
                var ctor=metadata.GetCustomAttribute(h).Constructor;
                EntityHandle type=ctor.Kind==HandleKind.MemberReference?metadata.GetMemberReference((MemberReferenceHandle)ctor).Parent:
                    ctor.Kind==HandleKind.MethodDefinition?metadata.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType():default;
                if(type.Kind==HandleKind.TypeReference)
                {
                    var t=metadata.GetTypeReference((TypeReferenceHandle)type);
                    if(metadata.GetString(t.Name)=="HideFromIl2CppAttribute"&&metadata.GetString(t.Namespace)=="Il2CppInterop.Runtime.Attributes")return true;
                }
                else if(type.Kind==HandleKind.TypeDefinition)
                {
                    var t=metadata.GetTypeDefinition((TypeDefinitionHandle)type);
                    if(metadata.GetString(t.Name)=="HideFromIl2CppAttribute"&&metadata.GetString(t.Namespace)=="Il2CppInterop.Runtime.Attributes")return true;
                }
            }
            return false;
        }
        var hiddenAccessors=new HashSet<MethodDefinitionHandle>();
        foreach(var h in driver.GetProperties())
        {
            var property=metadata.GetPropertyDefinition(h);if(!Hidden(property.GetCustomAttributes()))continue;
            var accessors=property.GetAccessors();
            if(!accessors.Getter.IsNil)hiddenAccessors.Add(accessors.Getter);
            if(!accessors.Setter.IsNil)hiddenAccessors.Add(accessors.Setter);
            foreach(var other in accessors.Others)hiddenAccessors.Add(other);
        }
        var exposed=new HashSet<string>();
        foreach(var h in driver.GetMethods())
        {
            var method=metadata.GetMethodDefinition(h);string name=metadata.GetString(method.Name);
            if((method.Attributes&MethodAttributes.Static)!=0||name==".ctor"||Hidden(method.GetCustomAttributes())||hiddenAccessors.Contains(h))continue;
            if(name!="Update"&&name!="OnDisable"&&name!="OnDestroy")
                throw new Exception("IL2CPP injection boundary: exposed managed ActionDriver method "+name);
            var signature=metadata.GetBlobReader(method.Signature);var header=signature.ReadSignatureHeader();
            if(header.IsGeneric||signature.ReadCompressedInteger()!=0||signature.ReadSignatureTypeCode()!=SignatureTypeCode.Void)
                throw new Exception("IL2CPP injection boundary: unsafe Unity callback "+name);
            exposed.Add(name);
        }
        if(!exposed.SetEquals(new[]{"Update","OnDisable","OnDestroy"}))throw new Exception("Missing exposed Unity lifecycle callbacks");
        Console.WriteLine("PASS ActionDriver IL2CPP injection boundary: only void lifecycle callbacks; managed helpers hidden");
    }

    internal static void Verify(string game, bool withExamplePacks = true)
    {
        string interop = Path.Combine(game, "BepInEx", "interop", "Assembly-CSharp.dll");
        Check(interop, new()
        {
            ["BaseCameraController"] = new[] { "OnLateUpdate", "get_Camera", "get_IsLockCamera", "set_IsLockCamera", "SetTpsCullingMask" },
            ["TPCameraController"] = new[] { "OnLateUpdate" },
            ["FPCameraController"] = new[] { "OnLateUpdate" },
            ["EventMethodManager"] = new[] { "LateUpdate" },
            ["PlayerController"] = new[] { "OnUpdate", "OnLateUpdate", "ChangeState", "get_Pca" },
            ["PlayerAction"] = new[] { "ChangeAction" },
            ["PlayerClassAccessor"] = new[] { "get_CharacterController" },
            ["PlayerKneeAdjuster"] = new[] { "OnLateUpdate", "get_KneeOffsetY" },
            ["PlayerToeHeelController"] = new[] { "OnLateUpdate" },
            ["LookAtController"] = new[] { "OnLateUpdate" },
            ["PlayerBoneScaleAdjuster"] = new[] { "OnChangeBody" },
            ["PlayerFacade"] = new[] { "IsPlayingAction", "CanUseHand" },
            ["PlayerSoundManager"] = new[] { "PlaySe" },
            ["PlayerAnimationReceiver"] = new[] { "OnPlaySe", "FootL", "FootR" },
            ["PlayerStateModel"] = new[] { "get_IsPlayingAction", "get_IsPlayingSomeNonMoveAction" },
            ["GameSystemMenuView"] = new[] { "Initialize" },
            ["OptionItemCheckView"] = new[] { "Initialize", "OnChangeToggle" },
            ["OptionItemBarView"] = new[] { "Initialize", "OnChangeSlider" },
            ["Localizer"] = new[] { "Trans" }
        });
        string baseDll = Path.Combine(game, "BepInEx", "plugins", "Seleka2MoreActions", "Seleka2.MoreActions.dll");
        string mmdDll = Path.Combine(game, "BepInEx", "plugins", "Seleka2VmdProbe", "Seleka2.VmdProbe.dll");
        string npcDll = Path.Combine(game, "BepInEx", "plugins", "Seleka2NpcSpectators", "Seleka2.NpcSpectators.dll");
        VerifyDriverInjectionBoundary(baseDll);
        VerifyPresenceDependency(mmdDll);
        Check(Path.Combine(game,"BepInEx","interop","UnityEngine.AnimationModule.dll"),new()
        {
            ["Animator"] = new[] { "GetLayerIndex", "IsInTransition", "GetCurrentAnimatorStateInfo" },
            ["AnimatorStateInfo"] = new[] { "IsName" }
        });
        Check(Path.Combine(game,"BepInEx","interop","UnityEngine.AudioModule.dll"),new()
        { ["AudioSource"] = new[] { "Pause", "UnPause", "PlayScheduled" } });
        VerifyWorkerMethods(baseDll, "WaveAudio", new[] { "Decode" });
        VerifyWorkerMethods(mmdDll, "MmdMotion", new[] { "Read", "ConvertPosition", "ConvertRotation" });
        VerifyWorkerMethods(mmdDll, "MmdSkeleton", new[] { "CopyData" });
        VerifyWorkerMethods(mmdDll, "GameMmdSkeleton", new[] { "CreateData" });
        VerifyWorkerMethods(mmdDll, "RuntimeVmdSource", new[] { ".ctor" });
        Check(baseDll, new() { ["ActionPlaybackApi"] = new[] { "get_PlaybackPositionSeconds", "add_PoseApplied", "remove_PoseApplied", "add_PlaybackChanged" },
            ["ActionDriver"] = new[] { "PrepareNativeIdle", "StopWithDiagnostics", "SyncPause" },
            ["ActionCancellationDiagnostics"] = new[] { "Record" },
            ["MotionAudioClock"] = new[] { "SetPaused", "HasStarted", "get_Paused" },
            ["ActionDefinition"] = new[] { "get_KeepGameExpressions", "set_KeepGameExpressions" },
            ["FixedModelFace"] = new[] { "Find", "Validate", "get_MmdMorphs", "get_NativeExpressions" }, ["ActionFaceMorphLayer"] = new[] { "BindMorph", "IsProtectedMorph", "Begin", "Write", "RestoreNativeFace" }, ["IActionFacePose"] = new[] { "RestoreNativeFace" },
            ["ActionBodyPoseLease"] = new[] { "ApplyBaseline", "RestoreNativeBody", "BeginNativeBody", "EndNativeBody", "Dispose" },
            ["NativeBodyPassPatch"] = new[] { "Prefix", "Finalizer" },
            ["NativeKneePosePatch"] = new[] { "Prefix", "Finalizer" },
            ["NativeToePosePatch"] = new[] { "Prefix", "Finalizer" },
            ["NativeHeadPosePatch"] = new[] { "Prefix", "Finalizer" },
            ["NativeKneeOffsetPatch"] = new[] { "Prefix" },
            ["ActionRootMotion"] = new[] { "Apply" }, ["IActionRootMotionPose"] = new[] { "get_RootDisplacement", "get_LoopRootDisplacement" },
            ["ActionEventRegistry"] = new[] { "Register" }, ["ActionEventRunner"] = new[] { "Advance", "Close" },
            ["NativeActionEvents"] = new[] { "Register" }, ["ClipActionEvents"] = new[] { "Read" } });
        Check(mmdDll, new() { ["MotionPlaybackApi"] = new[] { "get_IsPlaying", "get_CurrentMotionId", "add_PlaybackChanged" }, ["MmdCameraDriver"] = new[] { "Update", "OnDisable", "OnDestroy" }, ["MmdMorphBindings"] = new[] { "Create" } });
        Check(npcDll, new() { ["CrowdApi"] = new[] { "SetSource" }, ["CrowdDriver"] = new[] { "get_ManualRequested", "set_ManualRequested" }, ["ActionEventBridge"] = new[] { "Refresh", "Dispose" } });
        foreach (var file in new[] { baseDll, mmdDll, npcDll })
        {
            Check(file, new() { ["ModOptionsPagePatch"] = new[] { "Postfix" }, ["ModOptionLabelsPatch"] = new[] { "Prefix" }, ["ModOptions"] = new[] { "Add", "Translate" } });
            using var pe = new PEReader(File.OpenRead(file)); var metadata = pe.GetMetadataReader();
            var references = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToHashSet();
            if (references.Contains("UnityEngine.InputLegacyModule") || references.Contains("UnityEngine.IMGUIModule")) throw new Exception("Obsolete hotkey/IMGUI dependency: " + file);
            var types = metadata.TypeDefinitions.Select(h => metadata.GetString(metadata.GetTypeDefinition(h).Name)).ToHashSet();
            if (types.Contains("TabActionPreview") || types.Contains("ProbeUi")) throw new Exception("Obsolete test UI still compiled: " + file);
            if (file == npcDll && (references.Contains("Seleka2.MoreActions") || references.Contains("Seleka2.VmdProbe"))) throw new Exception("NPC plugin must remain standalone");
        }
        using var basePe = new PEReader(File.OpenRead(baseDll)); using var mmdPe = new PEReader(File.OpenRead(mmdDll));
        var b = basePe.GetMetadataReader(); var m = mmdPe.GetMetadataReader();
        string[] baseRefs = b.AssemblyReferences.Select(h => b.GetString(b.GetAssemblyReference(h).Name)).ToArray();
        string[] mmdRefs = m.AssemblyReferences.Select(h => m.GetString(m.GetAssemblyReference(h).Name)).ToArray();
        if (baseRefs.Contains("Seleka2.VmdProbe") || !mmdRefs.Contains("Seleka2.MoreActions") || mmdRefs.Contains("Seleka2.NpcSpectators")) throw new Exception("Incorrect plugin dependency direction");
        if (!baseRefs.Contains("UnityEngine.PhysicsModule")) throw new Exception("Root motion must reference the installed physics controller module");
        bool controllerMove = false;
        foreach (var handle in b.MemberReferences)
        {
            var member = b.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference) continue;
            string owner = b.GetString(b.GetTypeReference((TypeReferenceHandle)member.Parent).Name), name = b.GetString(member.Name);
            if (owner == "CharacterController" && name == "Move") controllerMove = true;
            if ((owner == "CharacterController" || owner == "Collider") && name == "set_enabled" || owner == "PlayerController" && name == "Warp")
                throw new Exception("Root motion cannot disable collision or warp the player");
        }
        if (!controllerMove) throw new Exception("Root motion must use CharacterController.Move");
        if (mmdRefs.Contains("UnityEngine.AssetBundleModule") || mmdRefs.Contains("UnityEngine.AnimationModule")) throw new Exception("MMD must use the direct VMD implementation");
        foreach (var typeHandle in m.TypeDefinitions)
        {
            var type = m.GetTypeDefinition(typeHandle);
            if (m.GetString(type.Name) == "MmdHandDiagnostics") throw new Exception("Temporary hand diagnostics still compiled");
            if (type.GetMethods().Any(h => m.GetString(m.GetMethodDefinition(h).Name) == "ReadPmd"))
                throw new Exception("Retired model parser still compiled");
        }
        foreach (var handle in m.MemberReferences)
        {
            var member = m.GetMemberReference(handle);
            if (member.Parent.Kind == HandleKind.TypeReference && m.GetString(m.GetTypeReference((TypeReferenceHandle)member.Parent).Name) == "ActionRegistry" && m.GetString(member.Name) == "Register")
                throw new Exception("Old MMD bundle registration branch still compiled");
        }
        string root = Path.GetDirectoryName(mmdDll)!;
        foreach (string retired in new[] { "vmd-probe.bundle", "wavefile.action.json", "wavefile_camera.vmd", "runtime-skeleton.txt" })
            if (File.Exists(Path.Combine(root, retired))) throw new Exception("Retired installed resource: " + retired);
        foreach (string folder in withExamplePacks ? new[] { "WAVEFILE", "Shake" } : Array.Empty<string>())
        {
            var pack = Path.Combine(root, "Motions", folder);
            if (Directory.GetFiles(pack, "*.action.json").Length != 0) throw new Exception("Old manifest was not retired");
            var resolved = Seleka2.Mmd.MmdActionPacks.Read(pack);
            if (resolved.Action.Id != folder || resolved.Action.DisplayName != folder || resolved.Action.AudioFile != "BGM.wav") throw new Exception("Folder defaults did not resolve");
            if (!File.Exists(Path.Combine(pack, "camera.vmd"))) throw new Exception("Missing camera resource");
            if (folder == "WAVEFILE")
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack, "action.json"))); var data = json.RootElement;
                if (data.EnumerateObject().Count() != 1 || Math.Abs(data.GetProperty("audioDelaySeconds").GetDouble() - 160d / 30) > .000001 || resolved.ReferencePose != Seleka2.Mmd.MmdReferencePose.A)
                    throw new Exception("WAVEFILE must retain only its music delay override and use the generated A-pose skeleton");
            }
            else if (File.Exists(Path.Combine(pack, "action.json"))) throw new Exception("Shake must use manifest-free defaults");
        }
        if (!File.Exists(Path.Combine(root, "README.zh-CN.md"))) throw new Exception("Player guide missing");
        foreach (var guideRoot in new[] { Path.GetDirectoryName(baseDll)!, root })
            foreach (string guide in new[] { "动作事件说明.zh-CN.md", "原版音效名称.txt" })
                if (!File.Exists(Path.Combine(guideRoot, guide))) throw new Exception("Event player guide missing: " + guide);
        var definition = b.TypeDefinitions.Select(b.GetTypeDefinition).Single(t => b.GetString(t.Name) == "ActionDefinition");
        if (definition.GetProperties().Any(h => b.GetString(b.GetPropertyDefinition(h).Name) == "SchemaVersion")) throw new Exception("Obsolete schema version remains");
        Console.WriteLine("PASS installed native settings/Harmony targets, three settings hooks, no custom hotkey/IMGUI/Tab preview dependencies, shared time API and standalone NPC interface");
        if (withExamplePacks) Console.WriteLine("PASS both installed motions use folder IDs and automatic VMD/WAV/camera discovery with game-generated skeletons; minimal WAVEFILE delay, manifest-free Shake, player guide and no schema version or retired resources");
        Console.WriteLine("PASS installed event registry/driver/native receivers/optional NPC bridge and event player guides");
    }

    static void VerifyWorkerMethods(string file, string typeName, string[] methodNames)
    {
        using var pe = new PEReader(File.OpenRead(file)); var metadata = pe.GetMetadataReader();
        var opcodes = typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode)).Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
            .ToDictionary(op => unchecked((ushort)op.Value));
        var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(t => metadata.GetString(t.Name) == typeName);
        foreach (string name in methodNames)
        {
            var method = type.GetMethods().Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == name);
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (int offset = 0; offset < il.Length;)
            {
                ushort code = il[offset++]; if (code == 0xfe) code = (ushort)(0xfe00 | il[offset++]);
                var op = opcodes[code];
                int size = op.OperandType switch
                {
                    System.Reflection.Emit.OperandType.InlineNone => 0,
                    System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                    System.Reflection.Emit.OperandType.InlineVar => 2,
                    System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                    System.Reflection.Emit.OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
                    _ => 4
                };
                if (op.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                {
                    var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.Handle(BitConverter.ToInt32(il, offset));
                    if (handle.Kind == HandleKind.MemberReference)
                    {
                        var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                        if (member.Parent.Kind == HandleKind.TypeReference)
                        {
                            var owner = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                            if (metadata.GetString(owner.Namespace).StartsWith("UnityEngine", StringComparison.Ordinal))
                                throw new Exception("Worker method calls Unity API: " + typeName + "." + name + " -> " + metadata.GetString(owner.Name) + "." + metadata.GetString(member.Name));
                        }
                    }
                }
                offset += size;
            }
        }
        Console.WriteLine("PASS worker IL has no Unity method calls: " + typeName + " / " + string.Join(",", methodNames));
    }
    static void Check(string file, Dictionary<string, string[]> contract)
    {
        using var pe = new PEReader(File.OpenRead(file)); var m = pe.GetMetadataReader();
        foreach (var (name, methods) in contract)
        {
            var type = m.TypeDefinitions.Select(m.GetTypeDefinition).Single(t => m.GetString(t.Name) == name);
            var available = type.GetMethods().Select(h => m.GetString(m.GetMethodDefinition(h).Name)).ToHashSet();
            foreach (var method in methods) if (!available.Contains(method)) throw new Exception("Missing installed method " + name + "." + method);
        }
    }
}
