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

ImportValidation 覆盖顶层文件字段、旧 `camera.vmdFile` 兼容、冲突优先级和路径限制。SkeletonValidation 使用实际后台加载器与播放源，验证“主动作 → 表情 → 口型”按通道覆盖、缺省通道保留、零值覆盖、名称归一化、口型范围限制、时长截断与末帧保持、跳回零秒、重复采样、停止恢复，以及附加文件缺失、损坏、无可用轨道时的独立警告和回退；另覆盖自定义歌词路径与禁用。

相应入口还提供针对已安装游戏的接口检查。离线替身不能证明实际渲染、导航、碰撞或原生 Harmony 派发，仍需游戏内验收。

### 2026-11-13 分离文件实测

用户在游戏中测试同一支《ヴァンパイア》动作的完整表情、仅口型、仅表情、仅动作四种配置，反馈四组视觉表现正常。测试使用原作者分别提供的身体、表情和口型 VMD，不合并文件；每组关闭原游戏表情叠加，未附音乐和镜头。第三方测试素材不随仓库或安装包分发。

核对该次 `BepInEx/LogOutput.log` 快照：四组均成功准备，时长均为178.5秒，存在多次开始、切换、移动停止及返回标题时停止记录；没有 Error/Fatal，也没有附加表情或口型加载拒绝。快照含555行，SHA256 为 `be20fec80b0f139ccbd03d2f56932fe21405144de9e38d6aecbb28a8cad3488f`。七条警告涉及 Il2CppInterop 初始化/参数注册、缺少 CompassView 和 blankmap 的 mission.bundle。本次反馈确认四组样例的观察结果，不扩大为所有动作或全部178.5秒播放完毕的验证。

更多动作1.6.2同步纳入此前已安装的膝盖偏移补丁移除：不再挂载 `PlayerKneeAdjuster.get_KneeOffsetY`，保留原生膝盖、脚尖和视线更新边界。配套验证不再要求该补丁存在。候选三个 DLL 共49条 Harmony 声明、43个原生目标，按匹配游戏二进制、元数据及完整 Cpp2IL 输出复核，43个入口均为单一方法。

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
./tests/InstallerValidation.ps1 -PackageDir "./releases/SFM-MoreActions-v1.7.0"
```

此验证在临时目录生成模拟文件，不修改实际游戏。覆盖首次安装、重复升级、Seleka2 到 SFM 的动作/配置迁移、新数据优先、占用检查与中途失败回滚。模拟标记不作为运行时程序集加载；原游戏接口检查仍独立执行。

StatsValidation 验证属性参数、原生输入、按帧去重、角色归属、排尿、方向感知及姿势任务条件；使用明确的原生边界夹具。EventValidation 另外覆盖真实播放驱动的开始/暂停/停止门禁。通过离线验证不代表已完成游戏内属性及任务实测。

核对属性补丁的原版入口：

```powershell
dotnet run --project more-actions/AnimatorValidation -c Release -- --stats-interop $gameDir more-actions/Plugin/bin/Release/net6.0/SFM.MoreActions.dll
```

AssetValidation 另覆盖 MP3 原生请求的异步准备、Unicode 本地路径、缓存、失败隔离、请求取消与音频所有权；这些是原生边界夹具，真实解码和游戏内播放需分别验证。
