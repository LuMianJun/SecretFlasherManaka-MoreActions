# 更多动作的播放流程与时序

[配置指南](README.zh-CN.md) · [外部 API 用法](API.zh-CN.md)

本文适用于更多动作 1.7.1。先确认谁控制播放和姿势写回，再看每帧如何更新。

## 三种用法怎么选

```mermaid
flowchart TD
    start{"你想做什么"}
    start -->|"播放自定义动作"| managed["更多动作负责管理播放"]
    start -->|"在自己的模组里驱动动作"| external["调用方播放，更多动作管理外部会话"]
    start -->|"替换一个原版动作"| replacement["更多动作提供替换片段，原版 Animator 播放"]
    managed --> request["从转盘选择，或注册后用 ActionPlaybackApi.Request 起播"]
    external --> begin["用 TryBeginAction 申请会话"]
    replacement --> manager["在动作替换管理器里选择来源"]
```

原动作替换继续由原版状态机决定什么时候播放。下面分别介绍自定义动作和外部会话怎么运行。

## 更多动作管理播放

玩家从转盘点选动作，或者模组通过 `ActionRegistry` 注册后调用 `ActionPlaybackApi.Request`。更多动作等待资源就绪、检查玩家能否开始，再协调动画、配乐和动作事件。

```mermaid
flowchart TD
    request["收到播放请求"] --> prepare["检查玩家状态并等待资源就绪"]
    prepare --> clock["设置动作与配乐的共同起点"]
    clock --> native["原版更新玩家和 Animator"]
    native --> sample["更多动作在本帧末尾采样并写回姿势"]
    sample --> motion["按动作配置应用走位与事件"]
    motion --> notify["发出 PoseApplied，提供动作和音频进度"]
    notify --> effects["按写回后的姿势处理粒子，补跑已延后的镜头更新"]
```

图中是普通播放帧；动作中途停止时会退出该帧的后续步骤。游戏暂停时冻结动作和配乐的时钟。停止、切换或正常播完时，更多动作释放自己占用的姿势、音频和镜头状态。

如果 Unity 独立动作配置了 `ikuClipAsset`，触发支持的原版站姿或普通蹲姿 IKU 后，主动作和配乐暂停。此时用原版动画的进度和混合权重采样专用片段；正常结束后从暂停点继续。

## 外部会话怎么接

调用方自己的代码负责播放动画、写骨骼、做 IK 和恢复自己接管的道具或输入。更多动作检查能否开始，管理动作互斥、玩家限制、已配置的属性，以及镜头跟随和原版粒子派发。

[开始交接](#开始时怎么交接) · [逐帧更新](#一帧里谁先做什么) · [结束交接](#结束时怎么交接)

### 开始时怎么交接

```mermaid
sequenceDiagram
    participant Game as 原版游戏
    participant Actions as 更多动作
    participant Caller as 调用方模组

    Caller->>Caller: 决定何时开始，准备自己的动画和道具
    Caller->>Actions: TryBeginAction 申请会话
    Actions->>Actions: 检查是否有其他动作占用
    Actions->>Game: 读取玩家、动画器和界面状态
    Actions->>Actions: 核对动作要求，读取 action.json
    Actions-->>Caller: 返回已建立的会话
    Caller->>Caller: 开始播放自己的动作
```

更多动作按资源名查找调用方 DLL 旁的 `Actions/<资源名>/action.json`。分组重载可以在同一次会话里管理多组属性。申请失败时返回 `null`，调用方可从 `reason` 读取原因，保持当前状态。

### 一帧里谁先做什么

三列分别是原版游戏、更多动作和调用方模组，时间从上往下走。图中以使用分组 API、混合专用 IKU 并上报姿势的一帧为例。

```mermaid
sequenceDiagram
    participant Game as 原版游戏
    participant Actions as 更多动作
    participant Caller as 调用方模组

    Game->>Game: 更新玩家和 Animator
    Note over Game,Caller: 原版玩家晚更新结束
    Actions->>Actions: 记录本帧 IKU 进度和混合权重
    Caller->>Actions: 读取 session.Iku
    Actions-->>Caller: 返回本帧进度和混合权重
    Caller->>Caller: 采样动画、应用 IK、写完角色和道具姿势
    Caller->>Actions: ReportPose 上报最终跟随点
    Note over Game,Caller: 原版事件管理器晚更新结束
    Actions->>Actions: 按最终姿势处理粒子
    Actions->>Game: 补跑本帧已延后的镜头更新
```

- 有专用 IKU 片段时，在更多动作采样本帧进度后、调用方写姿势前读取 `session.Iku`。早于采样读取会拿到上一次的结果。
- 需要跟随最终姿势的镜头或原版粒子时，等姿势和 IK 都写完再调用 `ReportPose`，传入两个世界坐标并标记本帧姿势已就绪。如果当帧没上报，依赖最终姿势的粒子调用可能丢失。
- 使用旧重载时，在写姿势前用 `readIku()` 当场读取原版状态，写完后用 `followCamera` 上报。

### 结束时怎么交接

图中两段是各自独立的结束方式。调用方主动结束时先清理再释放。更多动作中断时，调用方收到回调后清理。

```mermaid
sequenceDiagram
    participant Game as 原版游戏
    participant Actions as 更多动作
    participant Caller as 调用方模组

    rect rgba(128, 128, 128, 0.12)
        Note over Game,Caller: 调用方主动结束
        Caller->>Caller: 停止写姿势，恢复自己接管的状态
        Caller->>Actions: session.Dispose() 释放会话
        Actions->>Actions: 结束会话，清理会话粒子状态
        Actions->>Game: 恢复会话接管过的镜头挂点
    end

    rect rgba(128, 128, 128, 0.12)
        Note over Game,Caller: 更多动作中断
        Game->>Game: 游戏状态变化，例如当前玩家切换
        Actions->>Actions: 结束会话，清理会话粒子状态
        Actions->>Caller: Interrupted 回调，传入中断原因
        Caller->>Caller: 停止写姿势，恢复自己接管的状态
        Caller-->>Actions: 清理完成
        Actions->>Game: 恢复会话接管过的镜头挂点
    end
```

调用方在两种情况下都要恢复自己接管的姿势、输入和道具，并清空保存的会话。镜头挂点由更多动作恢复，前提是该会话动过这些挂点且它们仍由该会话控制。
