# 更多动作外部 API

简体中文 | [English](API.en.md) | [配置指南](README.zh-CN.md) | [播放流程与时序](PLAYBACK-FLOW.zh-CN.md)

先看谁来控制角色。像自行车这样，由自己的代码控制移动、车和骑行姿势，就用 [`TryBeginAction`](#让其他模组驱动动作) 申请会话。想让更多动作播放你提供的动画，就用 [`ActionRegistry`](#注册动作并接入播放) 注册，再用 `ActionPlaybackApi` 播放。

引用 `SFM.MoreActions.dll`，在插件类上声明 `[BepInDependency("local.sfm.moreactions", "1.7.0")]`。以下接口在游戏主线程调用，命名空间为 `SFM.Actions`。外部会话从更多动作 1.7.0 起提供。

## 让其他模组驱动动作

调用方决定何时开始和结束，并负责角色动画、道具、移动和 IK。更多动作管理玩家动作互斥、原版操作限制、镜头与会话退出时的恢复。

```csharp
var session = ExternalActionApi.TryBeginAction(
    providerId: "example.mymod", requiresHands: true, requiresStanding: true,
    actionResourceName: "ExampleMotion", isExercising: null, hasIku: false,
    interrupted: StopAndRestore,
    followCamera: out var followCamera, readIku: out _, reason: out var reason);
if (session == null) return;
```

1. **开始**时以调用方的 BepInEx 插件 GUID 作为 `providerId`。填写单层资源目录名 `actionResourceName`，按需声明双手和站姿要求。申请失败返回 `null`，具体原因见 `reason`，此时保持调用方现有状态。
2. **播放**时保存会话及所需回调。需要镜头跟随或原版粒子对齐时，在本帧写完身体姿势和 IK 后、镜头更新前调用 `followCamera(thirdPersonTarget, firstPersonEye)`。两个参数均为世界坐标。
3. **结束**时恢复本模组接管的姿势、输入和道具，再调用 `session.Dispose()`。更多动作中断会话时会调用 `interrupted`。用同一套清理逻辑恢复调用方状态，并清空保存的会话和回调。

### 资源配置与生效时机

更多动作从调用方 DLL 旁的 `Actions/ExampleMotion/action.json` 读取配置。例如下面的配置给外部动作设置快感速率。各版本支持的字段见[版本说明](#版本说明)。

```json
{
  "stats": {
    "ecstasyPerSecond": 0.1
  }
}
```

`isExercising: null` 表示整个有效会话都可以计算属性。如果动作有准备、运动和收尾阶段，可以传入一个谓词，只在运动阶段返回 `true`。暂停、打开菜单或原版 IKU 播放期间会暂停属性贡献。

调用方有专用 IKU 动画时，设置 `hasIku: true`，保留 `readIku` 并在更新姿势时读取原版进度和混合权重。没有可采样片段时回调返回 `null`。

## 注册动作并接入播放

- `ActionRegistry.Register(definition, packDirectory, providerId)` 注册带资源的动作。`RegisterRuntime(definition, source, packDirectory, providerId)` 用于自带采样器的模组。运行时来源需实现 `IActionSource`，提供有效时长，并为目标角色或预览创建各自的 `IActionPose`。
- 为注册动作选用唯一 ID。`ActionRegistry.Get(id)` 和 `GetAll()` 用于查找动作。`UnregisterProvider(providerId)` 移除指定来源的动作，适合在卸载提供方时调用。
- `ActionPlaybackApi.Request(id)` 请求播放已注册的动作。`Stop()` 停止由更多动作管理的播放。其他模组用 `TryBeginAction` 获得的会话由其调用方释放。

## 读取播放进度

`ActionPlaybackApi.IsPlaying`、`CurrentActionId` 和 `CurrentProviderId` 描述由更多动作管理的播放。`PlaybackChanged` 通知开始和结束。`PoseApplied` 在写入最终游戏姿势后触发，提供动作 ID 和播放秒数。此时可读取同一时钟下的 `PlaybackPositionSeconds` 和 `AudioPositionSeconds`。字幕或镜头可按来源筛选事件，结束时取消订阅。

## 版本说明

- **已发布的 1.7.0** 提供上面示例中的 `TryBeginAction`。外部会话可读取 `ecstasyPerSecond`，也可以用 `nativeKind` 取得预设速率。
- **1.7.1** 保留旧重载，并新增接收 `ExternalActionOptions` 和 `ExternalActionProfile[]` 的重载。调用新重载的模组应将最低依赖版本设为 `1.7.1`。自行车用它在一次骑行里分别管理踩踏和座椅使用的属性。

### 1.7.1 分组会话参数速查

`TryBeginAction(options, profiles, out reason)` 成功时返回 `IExternalActionSession`。失败时返回 `null`，`reason` 给出原因。旧重载保留参数顺序、返回类型、`out` 参数和单组会话调用方式。

**`ExternalActionOptions` 管整趟动作。**

| 字段 | 默认值 | 含义 |
| --- | --- | --- |
| `ProviderId` | 必填 | 调用方的 BepInEx 插件 GUID。用它定位调用方 DLL 和资源目录。 |
| `RequiresHands` | `true` | 戴手铐时拒绝动作。 |
| `RequiresStanding` | `true` | 蹲着或坐着时拒绝动作。 |
| `ProviderOwnsMovement` | `false` | 设为 `true` 后，按住方向键也能申请会话。移动由调用方处理。 |
| `StaminaDepletion` | `EndSession` | 体力耗尽时结束会话。设为 `KeepSession` 可保留会话，由调用方决定如何继续。 |
| `Interrupted` | 必填 | MoreActions 中断会话时，在游戏主线程回调。调用方在这里恢复自己接管的姿势、输入和道具。 |

**`ExternalActionProfile` 管一组效果。** 一次申请至少提供一组。

| 字段 | 默认值 | 含义 |
| --- | --- | --- |
| `ResourceName` | 必填 | 单层目录名。例如 `Bicycle` 指调用方 DLL 旁的 `Actions/Bicycle/action.json`。 |
| `Channels` | `All` | 这组负责哪些属性。多组要明确各自的范围，同一项属性只能归一组，即使两组的生效条件互斥。 |
| `IsActive` | `null` | 返回 `true` 时这组参与结算。省略时随有效会话生效。MoreActions 符合结算条件时，在主线程每帧最多调用一次。 |
| `HasIku` | `false` | 调用方已有专用 IKU 片段。每个会话最多一组启用。 |

代码用 `IsActive` 判断什么时候算，JSON 决定怎么算。`Channels` 可分配体力、快感、紧张度、膀胱补充、可疑度、不适、排尿许可和姿势任务。数值可以写在 JSON 根级或 `stats` 里，根级同名字段优先。`nativeKind` 提供原版预设，第一组的 `followFaceCamera` 决定面部镜头跟随。[字段单位和默认值](README.zh-CN.md#原版属性与任务)见配置指南。

例如，自行车的踩踏组使用 `Bicycle`、`Stamina` 和踩踏条件，露出且确实在踩踏或倒踩时，按文件配置每秒扣 `0.05` 体力。座椅使用组使用 `BicycleSeatUse` 和 `SitDildo` 预设，两组可以在同一次骑行里同时生效。

**会话操作**

- `IsActive` 查询会话是否仍对当前玩家、动画器和场景有效。
- `TrySetProfiles(profiles, out reason)` 传入完整的新列表，包括要保留的组。校验失败时继续用原列表。原版 IKU 进行中需保留对应资源。
- `ReportPose(thirdPersonTarget, firstPersonEye)` 在写完姿势和 IK 后上报两个世界坐标。
- `Iku` 提供最新的阶段、序号、进度和混合权重，读取时不会推进播放。快照由更多动作在玩家 `OnLateUpdate` 的高优先级后置补丁中更新。调用方在该补丁之后读取，再写回姿势。
- `IkuSampleFrame` 记录生成这份快照时的游戏帧号。首次采样前为 `-1`，可用它核对采样和写回是否同帧。
- `Dispose()` 用于正常结束。调用方先恢复自己的姿势、输入和道具。
