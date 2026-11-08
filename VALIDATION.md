# 离线验证

验证项目使用 .NET 10，生产插件目标为 .NET 6。请使用 .NET 10 SDK，并准备目标游戏已生成的 `BepInEx/core` 和 `BepInEx/interop`。游戏及 Unity 程序集由使用者本地提供，不包含在仓库中。

## 从源码构建

在仓库根目录运行，将路径替换为自己的游戏目录：

```powershell
$gameDir = "D:/Games/SecretFlasherManaka"
dotnet build vmd-validation/Plugin/VmdProbe.csproj -c Release "-p:GameDir=$gameDir"
dotnet build npc-spectators/Plugin/Seleka2.NpcSpectators.csproj -c Release "-p:GameDir=$gameDir"
```

第一条命令会同时构建更多动作与 MMD。仅构建更多动作时，使用 `more-actions/Plugin/MoreActions.csproj`。DLL 输出到各插件项目的 `bin/Release/net6.0`。

## 运行离线验证

在仓库根目录执行：

```powershell
dotnet run --project more-actions/AssetValidation -c Release
dotnet run --project more-actions/AnimatorValidation -c Release
dotnet run --project more-actions/FaceRestoreValidation -c Release
dotnet run --project more-actions/EventValidation -c Release
dotnet run --project more-actions/RingValidation -c Release
dotnet run --project more-actions/RuntimeValidation -c Release
dotnet run --project vmd-validation/ImportValidation -c Release
dotnet run --project vmd-validation/InterpolationValidation -c Release
dotnet run --project vmd-validation/SkeletonValidation -c Release
dotnet run --project vmd-validation/CameraValidation -c Release
dotnet run --project npc-spectators/Validation -c Release
```

测试自行生成临时样例。需要验证真实动作时，可给 SkeletonValidation 传入自备 Motions 目录；给 ImportValidation 传入自备 WAVEFILE/Shake 示例的父目录。素材均不随仓库提供。

相应入口还提供针对已安装游戏的接口检查。离线替身不能证明实际渲染、导航、碰撞或原生 Harmony 派发，仍需游戏内验收。

## 生成无素材安装包

```powershell
./build-mods.ps1 -GameDir "D:/Games/SecretFlasherManaka" -OutputDir "./releases/local-build"
```

只收集三个插件自身的 DLL、玩家说明、动作事件文档和第三方说明。不会复制游戏程序集、个人配置或动作/模型/音乐。


无素材安装包的接口检查可显式使用：

```powershell
dotnet run --project vmd-validation/CameraValidation -c Release -- --package "D:/Games/SecretFlasherManaka"
```

此模式只检查插件、原版互操作接口和说明文件，不要求玩家拥有 WAVEFILE/Shake 测试素材。原有带素材入口仍会严格验证这些素材的默认导入规则。
