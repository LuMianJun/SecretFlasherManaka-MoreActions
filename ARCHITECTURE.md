# 代码结构

本文件面向维护者；安装、导入和播放方式见 [README](README.md)，构建与验证见 [VALIDATION](VALIDATION.md)。

## 模块与程序集

| 源码目录 | 程序集 | 依赖 |
| --- | --- | --- |
| more-actions | SFM.MoreActions.dll | 游戏、BepInEx、Il2CppInterop、HarmonyX、Unity |
| mmd | SFM.VmdProbe.dll | 更多动作公共 API |
| npc-spectators | SFM.NpcSpectators.dll | 可独立使用；可选接口联动更多动作与 MMD |
| shared | 不单独生成 DLL | 公共设置源码与生产项目引用配置 |

程序集名、公共命名空间、插件 GUID 和运行时资源目录保持稳定。源码中的 mmd 是生产模块；VmdProbe 是其程序集名。

## 更多动作内部

```text
more-actions/Plugin/
├─ Plugin.cs             插件入口：配置、服务注册、组件与补丁安装
├─ Actions/              动作定义、注册表、规则、属性模型与注册校验
├─ Api/                  对外的姿势源契约、目标与播放通知
├─ Import/               配置解析、JSON 默认值、动画包与音频准备
├─ Playback/             播放协调、会话状态、时钟、事件、走位
├─ Rig/                  动画采样、骨骼、表情及原生姿势恢复
├─ Native/               原游戏接口、生命周期补丁与原版镜头适配
├─ Replacements/         替换候选、选择、配置持久化和控制器绑定
├─ UI/                   转盘、设置入口、替换浏览器与界面注册
└─ Diagnostics/          取消原因和镜头变化诊断
```

### 配置、注册与资源

- ActionConfiguration 是公共 JSON 解析入口，统一处理平铺属性、stats 优先级及原始 JSON 校验。
- ActionPackLoader 负责显式 Unity JSON 的发现与默认值；UnityActionOverrides 为自动导入片段合并同名精简配置。
- ActionDefinitionValidation 在注册时检查动作规则和资源路径；ActionRegistry 保存动作快照、提供查询、别名与提供者移除通知。
- ActionRegistry.LoadPacks 保留为公共兼容入口，转交 ActionPackLoader；插件内部直接调用导入器。
- ActionEventSchema.ValidateJson 保留公共调用方式，转交公共配置校验；事件校验本身只处理事件字段。
- ActionBundleImport 负责自动导入 Unity 片段；ActionAssets 负责动作媒体资源缓存与准备调度，LoadedAction 表示可播放资源。WaveAudio 分离 WAV 解码和分帧上传；NativeAudioLoad 使用 Unity 原生接口异步准备 MP3，在缓存接管前持有并清理请求与未发布的音频。

注册表和资源缓存属于插件生命周期。单次播放只借用资源，不在停止时销毁其他动作的缓存。

### 播放与状态归属

- ActionDriver 协调请求等待、原动作退出、播放、取消和原生生命周期。
- PlaybackSession 集中保存当前动作、姿势、音源、时钟、事件、属性、走位及播放修订号。
- 会话容器在同一驱动器内持续存在；切换动作不会重置事件修订号或按帧去重记录。
- ActionRequestQueue 和退出原动作的等待状态由驱动器管理，不混入姿势或媒体资源。
- IActionSource 提供动作数据，IActionPose 负责单次采样和恢复；可选接口分别声明身体、表情、走位和预热能力。

更新顺序保持为：

1. PlayerController.OnUpdate 前撤去上次自定义输出，恢复原生姿势基线并检查取消条件。
2. 原版执行更新，属性接入在对应原生计算入口处理。
3. PlayerController.OnUpdate 后处理请求与播放状态。
4. 原生 LateUpdate 保留原有流程，姿势租约只保护当前动作拥有的骨骼。
5. EventMethodManager.LateUpdate 后输出自定义姿势、走位与事件，再刷新跟随镜头。

事件回调可能在执行中停止、切换动作或更换玩家。修订号、所有权检查和停止时的清理顺序不可因为移动文件而省略。

### 原动作替换与 UI

- ReplacementSelection 保存选择；ReplacementCatalog 管理原版槽位和各来源候选。
- ReplacementService 管理持久化、别名迁移、候选刷新和批量选择，向界面发送状态变化通知。
- NativeAnimationReplacements 只负责当前 Animator、OverrideController、原版片段及对应镜头来源的绑定与恢复。
- ReplacementSettings 负责把选择服务注册到设置界面；ReplacementBrowserModel/View 管理浏览状态和控件。
- UI 通过 ReplacementService 修改选择，不直接分配或销毁原生控制器。

替换绑定仍等待原版站立待机边界；自定义动作、蹲下、坐下和过渡过程中延后应用。

## MMD 内部

```text
mmd/Plugin/
├─ Plugin.cs             插件入口与公共版本信息
├─ Api/                  可选的 MMD 播放通知
├─ Import/               MMD 配置、目录发现、后台动作准备与注册
├─ Motion/               MmdMotion 数据读取/采样，MmdSkeleton 求解
├─ Rig/                  游戏骨架映射、体型快照、生成、缓存与绑定
├─ Playback/             RuntimeVmdSource 与单次 RuntimeVmdPose
└─ Camera/               镜头 VMD、配置和镜头生命周期
```

- MmdMotion 的关键帧数据由动作源共享；每个播放实例使用自己的姿势状态。
- MmdTargetSnapshot 收集当前角色的绑定与体型；GameMmdSkeleton 生成对应参考姿势数据。
- MmdRigPreparation 负责后台数据计算和主线程分帧构建；MmdRigCache 管理骨架租借、体型匹配、跨场景重绑和淘汰。
- RuntimeVmdRig 保存可复用采样对象；RuntimeVmdPose 负责当次动作应用、表情、走位以及结束时归还和恢复。
- MmdCameraDriver 单独管理配套镜头，通过更多动作 API 对齐播放时间，不接管播放请求。
- Plugin.Version 是 MMD 入口和准备完成日志共同使用的版本来源。

不要把“数据解码”“Unity 对象构建”“播放租约”合并成同一生命周期。后台任务不直接接管活角色。

## NPC 围观内部

```text
npc-spectators/Plugin/
├─ Plugin.cs             配置、设置注册与补丁安装
├─ Integration/          CrowdApi、MmdBridge、ActionEventBridge
├─ Gathering/            CrowdDriver、资格/站位策略、遮挡检查
└─ Native/               单个 Spectator 接管与原生更新/中断补丁
```

CrowdDriver 持有一轮聚集的固定中心、站位、招募与重试状态。Spectator 只持有单个 NPC 的导航/动画快照、赶路和到位逻辑；释放时尊重已经接手的原版状态。各请求来源独立释放，不因一个来源结束而撤销其他来源。

## 共享代码、构建与验证

- shared/ModOptions.cs 分别编译到三个插件，保证可选模块独立安装。
- shared/PluginReferences.props 集中生产项目的 .NET 目标和共同游戏引用，各插件声明自己的额外模块与程序集名。
- 三个生产项目显式列出编译文件，并保留既有补丁类型的相对顺序；新增补丁时应核对同一原生方法的执行顺序。
- 验证项目直接链接相应生产文件，夹具仅提供明确的游戏/Unity 边界；改变文件位置后必须同步这些链接。
- 验证项目中的简化模型不代表生产实现；JSON 验证、骨架数学、原生接口、安装器和实际游戏效果分别验证。

普通 `.anim` 直接导入等延期事项见[更多动作待办](more-actions/docs/待办.md)。
