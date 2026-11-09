# 离线验证

模块目录、状态归属和更新顺序见[代码结构](ARCHITECTURE.md)。

验证项目使用 .NET 10，生产插件目标为 .NET 6。请使用 .NET 10 SDK，并准备目标游戏已生成的 `BepInEx/core` 和 `BepInEx/interop`。游戏及 Unity 程序集由使用者本地提供，不包含在仓库中。

## 从源码构建

在仓库根目录运行，将路径替换为自己的游戏目录：

```powershell
$gameDir = "D:/Games/SecretFlasherManaka"
dotnet build mmd/Plugin/VmdProbe.csproj -c Release "-p:GameDir=$gameDir"
dotnet build npc-spectators/Plugin/SFM.NpcSpectators.csproj -c Release "-p:GameDir=$gameDir"
```

第一条命令会同时构建更多动作与 MMD。仅构建更多动作时，使用 `more-actions/Plugin/MoreActions.csproj`。DLL 输出到各插件项目的 `bin/Release/net6.0`。

## 运行离线验证

在仓库根目录执行：

```powershell
dotnet run --project more-actions/AssetValidation -c Release
dotnet run --project more-actions/AnimatorValidation -c Release
dotnet run --project more-actions/CameraTrackValidation -c Release
dotnet run --project more-actions/ReplacementBrowserValidation -c Release
dotnet run --project more-actions/FaceRestoreValidation -c Release
dotnet run --project more-actions/EventValidation -c Release
dotnet run --project more-actions/RingValidation -c Release
dotnet run --project more-actions/RuntimeValidation -c Release
dotnet run --project more-actions/StatsValidation -c Release
dotnet run --project mmd/ImportValidation -c Release
dotnet run --project mmd/InterpolationValidation -c Release
dotnet run --project mmd/SkeletonValidation -c Release
dotnet run --project mmd/CameraValidation -c Release
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
dotnet run --project mmd/CameraValidation -c Release -- --package "D:/Games/SecretFlasherManaka"
```

此模式只检查插件、原版互操作接口和说明文件，不要求玩家拥有 WAVEFILE/Shake 测试素材。原有带素材入口仍会严格验证这些素材的默认导入规则。

## 安装与命名迁移验证

生成安装包后运行：

```powershell
./tests/InstallerValidation.ps1 -PackageDir "./releases/SFM-MoreActions-v1.4.0"
```

此验证在临时目录生成模拟文件，不修改实际游戏。覆盖首次安装、重复升级、Seleka2 到 SFM 的动作/配置迁移、新数据优先、占用检查与中途失败回滚。模拟标记不作为运行时程序集加载；原游戏接口检查仍独立执行。

StatsValidation 验证属性参数、原生输入、按帧去重、角色归属、排尿、方向感知及姿势任务条件；使用明确的原生边界夹具。EventValidation 另外覆盖真实播放驱动的开始/暂停/停止门禁。通过离线验证不代表已完成游戏内属性及任务实测。

核对属性补丁的原版入口：

```powershell
dotnet run --project more-actions/AnimatorValidation -c Release -- --stats-interop $gameDir more-actions/Plugin/bin/Release/net6.0/SFM.MoreActions.dll
```

AssetValidation 另覆盖 MP3 原生请求的异步准备、Unicode 本地路径、缓存、失败隔离、请求取消与音频所有权；这些是原生边界夹具，真实解码和游戏内播放需分别验证。
