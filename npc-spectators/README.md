# Seleka2 NPC Spectators 0.2.0

独立的 BepInEx 6 IL2CPP 插件，输出 `Seleka2.NpcSpectators.dll`。
不修改游戏源码，不依赖 MMD 插件，不加载动画包，不写原版存档。

## 使用

将 DLL 放入 `BepInEx/plugins/Seleka2NpcSpectators/`。
配置由首次加载生成于 `BepInEx/config/local.seleka2.npcspectators.cfg`。

| 安装组合 | 行为 |
| --- | --- |
| 仅围观插件 | 原版设置页手动召集；配置的原版动作也会触发 |
| 更多动作 + MMD | WAVEFILE / Shake 动画和音频正常播放 |
| 三模组 | 配置的 MMD 动作自动触发围观，结束时释放 |

原版“设置 → 游戏系统”下提供启用、手动召集、原版动作触发、MMD动作触发、所有MMD动作、围观半径和间距。没有插件自定义快捷键。手动召集关闭设置后生效；关闭它不取消其他自动来源，关闭“启用”会释放围观。
移动或取消会结束手动触发。MMD 的停止条件仍由 MMD 插件处理。

默认原版触发为 `HipShake`（原版 ActionType 50002）。
可以将 `Triggers/NativeActions` 设为空，关闭原版动作触发；或填写逗号分隔的枚举名/数字，例如 `HipShake,Dogeza`。
`None/-1` 不可配置为触发动作。插件不会解锁原版动作。

`Integration/FollowMmd` 默认 true；`MmdActions` 默认 `wavefile`。
没有可选播放接口或未安装 MMD 时，围观插件仍可独立运行。当前 MMD 0.9.0 保留此接口。
`Integration/FollowAllMmd` 默认 false，保持现有动作名单；开启后包括 Shake 和未来导入动作。`Triggers/FollowNativeActions` 默认 true。`Gathering/Radius` 默认 2.8 米，`Spacing` 默认 1.1 米；可在设置页或配置文件调整。

## 动作事件触发

安装更多动作 0.3.0 后，可以在动作配置的 onStart 或 events 中添加 `{"event":"npc.spectators"}`（events 内还需 time）。不需要开启 MMD 动作触发，但仍服从“启用”总开关和原版场景条件。每次播放持有自己的请求，停止时自动释放，不影响其他来源。

## NPC 范围与恢复

从原版 `NpcManager.ExistNpcList` 招募当前场景已生成的普通行人，无额外距离过滤。
只接管 FixedType=None、Idle、非警察、未坐下、没有交互令牌且有有效寻路代理和巡逻路线的 NPC。
不招募警察、固定店员、固定坐着/聊天的人、睡眠或正在交互/受惊/发现玩家的 NPC。
符合条件的新生成行人也会加入。每 0.25 秒最多处理 4 次招募，远处行人需要实际走过来。

按多圈分配位置，并检查导航采样、目标楼层、完整路径和实际采样位置间距。
障碍物可能导致部分行人无可达围观位置；每 5 秒重试，不传送、不穿墙强拉、不额外生成 NPC。
赶路调用原版 `NpcLocomotion.SimpleUpdateFromOtherState`，围观只提供临时导航目标，保留原版转向、移动、导航同步、FixedUpdate 和近距离碰撞修正。插件不再自行计算移动速度、裁剪固定帧步长或修改代理的位置/旋转更新标志。原版巡逻入口在接管期间暂停，避免覆盖围观目标；不新增卡住计时、重寻路监测或传送。
到位后停止走路并转向玩家；这里没有额外欢呼/鼓掌动画。

停止时恢复原寻路目标、目标缓存、停止状态和步行动画速度；代理的位置/旋转更新标志不由插件改动。
巡逻路线和路线索引在围观期间不推进。先释放接管，再让原版状态进入逻辑接手。
原版视野、可疑度、发现玩家、任务判定和时间停止仍生效；围观不会屏蔽这些规则。
打开其他 UI 会结束当前接管，设置页手动请求保留至关闭界面后执行；退出场景、失去玩家或游戏结束会结束手动请求及接管。

## 可选接口

MMD 插件公开独立的 CLR API `Seleka2.Motion.MotionPlaybackApi`：

```csharp
public static bool IsPlaying { get; }
public static string CurrentMotionId { get; }
public static event Action<string, bool> PlaybackChanged;
```

围观插件通过反射发现此类型，订阅通知，并读取当前快照以支持晚订阅。
对更多动作只有可选加载顺序依赖（SoftDependency），没有更多动作或 MMD 的程序集引用，仍可单独安装。
MMD 对监听者异常逐个隔离，其他插件失败不会中止动作播放。

围观插件另公开 `Seleka2.NpcSpectators.CrowdApi.SetSource(string sourceId, bool active)`，
供其他动作插件使用。每个调用者使用自己的稳定 sourceId，并在停止/卸载时用同一 ID 传 false。
不同来源的请求独立计数；Unity 操作仅在主线程执行。
调用者也可以反射此方法以避免硬依赖。

## 构建与检查

```powershell
dotnet build ./Plugin/Seleka2.NpcSpectators.csproj -c Release "-p:GameDir=$gameDir"
dotnet run --project ./Validation/Validation.csproj
```

`$gameDir` 由使用者指定，不写入源码。依赖来自目标游戏现有 BepInEx/core 和 interop。
验证项目链接生产围观逻辑、播放 API 和联动代码，以替身模拟外部 Unity/游戏边界。
它检查触发来源、过滤、间距、晚订阅、停止恢复和状态打断，但不模拟原版导航/物理或 IL2CPP Harmony 实际派发。
实机效果必须在原版游戏中验证。

日志保留启动、开始/结束、招募/释放和异常信息；旧的周期状态报告、驱动次数计数和到位测试日志已移除。
