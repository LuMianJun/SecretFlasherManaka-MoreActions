# 更多动作使用说明

简体中文 | [English](README.en.md)

为《Secret Flasher Manaka》提供转盘动作、Unity 动画包导入、原动作替换、音乐、表情叠加、动作事件、属性变化与姿势任务。安装方式与插件组合见[仓库首页](../README.md)。

## 快速开始

### 可以做什么

- 导入 Unity 动画包，在原版转盘中播放新增动作，并记住各数字键对应的动作。
- 按资源包批量替换原版动作，或为每个原动作单独选择来源。
- 在游戏设置中调整动作音乐、表情叠加和镜头；安装 MMD 插件后还可播放 VMD。

### 添加动画包

按[仓库首页](../README.md#安装)从 Release 安装更多动作插件，退出游戏后将 Unity 动画包放入 `BepInEx/plugins/SFMMoreActions/Actions/`，不同资源包各建一个子文件夹：

```text
Actions/
├─ MyPack/
│  ├─ MyPose.bundle
│  └─ Walk.bundle
└─ OtherPack/
   └─ MyPose.bundle
```

放好资源后重启游戏。新增动作在转盘里选择；与原版片段同名的动画在“管理动作替换”里选择。

### 从转盘播放

1. 进入场景，让角色站立并停止移动。
2. 按住数字键 **1–8** 中任意一个，打开对应的原版转盘。
3. 保持按住数字键，用 **Z / X** 切页，找到导入的自定义动作。
4. 移动鼠标指向动作，松开数字键即可播放；也可以按鼠标左键确认。

以上为默认键位，改过游戏按键时以自己的绑定为准。每个转盘会记住所选动作，之后短按对应数字键即可再次执行。

资源在后台准备，动作尚未出现在列表时，稍后重新打开转盘。选中仍在加载的动作后会等待资源就绪；再次选择同一动作可撤销等待，选择其他动作则切换等待目标。

### 停止、切换与菜单

移动或再次选择同一动作可停止播放，选择其他自定义动作可切换播放。切换原版操作或进入受限状态也会停止。

从原版允许互切的姿势切换时，播放器先等资源准备好，再退出原动作并接续播放。手铐、蹲下、坐下和特殊交互等状态仍按原版规则处理。

按 Tab、Esc 打开菜单时保留当前动作。菜单使游戏暂停时，动作、音乐和镜头一起暂停，关闭后继续。

支持走位的动作经过游戏角色控制器移动，保留碰撞；受阻会停止，结束后留在实际到达的位置。头发、裙摆等物理仍由游戏处理。

### 原动作替换

1. 放好动画包并重启游戏，打开 **设置 → 更多动作 → 管理动作替换**。
2. 选择浏览方式：按资源包逐项勾选或批量应用，或按原动作选择各自的来源。
3. 打开 **启用动作替换** 总开关。
4. 关闭设置，让角色恢复站立待机，选择便会应用。此后按原版方式执行该动作即可。

管理页支持鼠标与键盘导航。两种浏览方式共用同一份配置，切换视图后选择仍保留。

- **按资源包：** 左侧选择包，右侧显示紧凑勾选列表，可逐项选择、“应用本包”或“清除此包”。
- **按原动作：** 隐藏包列表，以全宽显示全部可替换原动作。点击右侧来源，在列表中选择“原版”或某个资源包。
- **仅已选择：** 只显示已有有效选择的项目。
- **启用动作替换：** 总开关默认关闭，开启后才应用所选资源；关闭保留各项选择。
- **返回或 Esc：** 来源选择列表中先关闭列表，管理页中返回更多动作设置，再次返回可按原版方式离开设置。

每个原动作选择一个来源，不同动作可使用不同包。“应用本包”更新该包覆盖的动作，“清除此包”撤销当前来自该包的选择，其余选择保留。

关闭菜单且角色站立、未执行动作时应用选择；蹲下、坐下、动作播放及过渡期间会延后应用。

替换后，动作仍由原版触发方式、状态机、移动、碰撞和事件接收流程驱动。动画包提供身体动作，主视角继续使用原片段的相机轨道。

原版动画显示游戏中的本地化名称，外部动作和包名沿用作者原名。

### 游戏内设置

打开 **设置 → 更多动作**：

- **动作音乐音量：** 调整自定义动作配乐的音量，同时受游戏主音量和背景音乐音量影响。
- **叠加原游戏表情：** 默认开启，在游戏表情上叠加动作表情；关闭后使用动作表情。
- **面部镜头跟随：** 播放时让原版面部摄像机跟随头部，是否显示由原版设置控制。
- **使用配套镜头：** 安装 MMD 插件后显示，开关动作自带的镜头。
- **管理动作替换：** 打开上节的替换管理页。

页面支持日、英、简中、繁中、韩语；NPC 围观选项在 **设置 → 游戏系统**。

默认在原游戏表情上叠加动作表情，保留捏脸形状，停止后归还原版表情控制。关闭叠加时只显示动作表情。

## 高级使用

### 动画包兼容与导入规则

不同资源包可各建子目录，同名文件在不同目录中分别导入。

放入 AnimReplacer 风格的 Unity AssetBundle 并重启游戏，包内动画会自动导入。只需复制动画包，原 AnimReplacer DLL、快捷键配置和 OverrideController 模板均可省略。普通 `.anim`、FBX 文件需先打成匹配游戏骨架的动画包。

Humanoid 动画通过游戏角色的 Avatar 播放；Generic 动画按游戏骨骼路径匹配。骨骼、表情和动画事件各有兼容要求，导入成功后仍需检查实际播放效果。

- clip 名与原版片段同名时，作为替换资源，只出现在替换设置中。
- 其余新增动作出现在原版转盘，自动导入时默认循环。
- 原动作匹配区分大小写，与 bundle 文件名或资产路径无关。
- 不同包提供同名原动作时，可在设置中选择其中一个来源。

### 替换配置与表情优先级

包内存在多个同名候选时，批量应用优先保留已选候选，否则取列表中的第一个来源。读档后自动绑定新角色。

替换选择保存在插件配置 `[Replacements] Enabled/Selections`，独立于游戏存档；原插件的 `CheckedReplacements.json` 与本插件选择分别维护，迁移要求见“升级兼容”。

动作 JSON 的 `"keepGameExpressions": false` 强制关闭叠加，界面开关不能覆盖；省略或为 `true` 时遵循界面设置。`"writeFaceMorphs": false` 表示动作完全不接管表情。

### JSON 可以省略

Unity AssetBundle 的片段和 ID 会自动导入；MMD 也有默认配置。要调整默认行为时再添加 JSON。

| 导入方式 | 配置文件 | 默认行为 |
| --- | --- | --- |
| Unity 自动导入 | 不放 JSON | 导入包内全部片段、静音循环、自动生成 ID。 |
| Unity 自动导入参数 | 与 bundle 同名的 .action.json | 只填写需要修改的字段，其余继承自动导入；不必填 ID、资源名或 loop。 |
| Unity 显式指定片段 | 名称.action.json，填写 bundle、clipAsset | 仅导入指定片段，其余使用下表默认值。 |
| MMD 导入 | 可选 action.json | 使用 motion.vmd，以及存在的 BGM.wav/BGM.mp3、camera.vmd。完整参数见 [MMD 使用说明](../mmd/README.zh-CN.md)。 |

Unity 和 MMD 共用播放、音乐、表情、范围、事件及原版属性参数。资源部分分别配置：Unity 使用 bundle、clipAsset，MMD 使用 vmdFile、referencePose 和 camera。

### 默认 ID

Unity 动作使用 **相对文件夹路径＋片段名**，例如 MyPack/Pose。子目录计入路径，直接放在 Actions 根目录的片段使用 Actions/Pose。

```text
Actions/
├─ MyPack/
│  └─ pose.bundle       片段 Pose → MyPack/Pose
└─ OtherPack/
   └─ pose.bundle       片段 Pose → OtherPack/Pose
```

自动导入及同名精简配置按实际片段名生成 ID；显式资源配置取 clipAsset 的最后一段，并去掉 .anim 扩展名。同目录的同名片段会产生重复 ID，可分目录存放或显式指定不同 id。修改目录或片段名也会改变默认 ID，需要固定时请填写 id。旧版自动导入 ID 可继续解析，资源重新识别后会迁移快捷槽和替换来源记录。

### 使用 JSON

在 bundle 旁放置同名配置：catpose 对应 catpose.action.json；pose.bundle 对应 pose.bundle.action.json，也可用 pose.action.json，前者优先。按需填写参数即可，DisplayName/displayName 均可，省略时沿用自动导入的名称。

例如 CatPose 只需要：

```json
{ "poseMission": true, "ecstasyPerSecond": 0.1 }
```

同名精简配置作用于包内全部片段，资源、片段名和 ID 从实际加载结果生成，默认循环。要单独选取一个片段，再填写 bundle 与 clipAsset：

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose"
}
```

若位于 MyPack 文件夹，省略 id 会生成 MyPack/Pose，省略 displayName 会显示 Pose。

填写 bundle、clipAsset 后，该 bundle 改为按配置导入。每个需要的片段各建一份配置，并为它们保留唯一 ID。

修改后重启游戏。参数名大小写均可，字符串使用英文双引号，数字直接填写，开关写 true/false；说明文字和注释放在 JSON 文件外。**下表按显式资源配置列出默认值；自动导入及同名精简配置的 loop 默认 true。**

### 全部动作参数

#### 标识与资源

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `id` | 文件夹＋片段名 | 内部唯一标识，用于快捷槽、替换来源等记录，一般省略即可。显式值不能为空、重复、含英文逗号或控制字符；它不决定替换哪个原动作。 |
| `displayName` | 自动导入的名称；显式资源模式为 clipAsset 的片段名 | 转盘显示名称，不改变 ID 或替换目标；原动作在管理页中仍使用游戏本地化名称。 |
| `description` | 空字符串 | 动作备注，不影响播放，当前不作为转盘说明展示。 |
| `bundle` | 自动导入时省略；显式资源模式必填 | AssetBundle 文件路径，相对于本 JSON。扩展名不限；普通 .anim、FBX 不能直接使用。 |
| `clipAsset` | 自动导入时省略；显式资源模式必填 | 包内 AnimationClip 资源名，可用可解析的短名称或完整包内路径，如 Pose、assets/animations/pose.anim。它不是磁盘路径。 |

外部文件使用相对于 JSON 的路径，范围限于当前目录及子目录。替换槽位按实际加载的 clip 名区分大小写匹配，bundle 文件名和 id 用于资源定位与记录。

#### 音乐与时间

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `audioFile` | 空字符串 | 外部 WAV 或 MP3 路径。WAV 支持单/双声道 PCM16 或 float32；MP3 使用游戏原生解码。省略或空字符串表示不使用外部音乐；Unity 动作不会自动查找 BGM。 |
| `audioAsset` | 空字符串 | 仅显式填写 bundle、clipAsset 时可用，指定包内 AudioClip；不能与非空 audioFile 同时使用。精简配置添加音乐请使用 audioFile，并填写 loop=false。 |
| `audioDelaySeconds` | 0 | 音乐比角色动作晚开始的秒数，只能大于或等于 0。不改变动作或事件的开始时间；没有音乐时无效果。 |
| `volume` | 1 | 此动作音乐音量倍率，范围 0～1，还受插件和游戏音量影响。0 只是静音，并非未配置音乐。 |
| `loop` | false | 是否循环角色动作。显式资源配置默认单次播放；自动导入及仅填写参数的同名配置默认循环。目前带音乐不能循环：true 时不应设置 audioFile/audioAsset，volume=0 也不能绕过限制。 |

单次播放并添加音乐：

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "audioFile": "BGM.wav",
  "audioDelaySeconds": 1,
  "volume": 0.7
}
```

MP3 也可用精简配置；将此文件放在对应 Unity bundle 旁：

```json
{ "audioFile": "音乐.mp3", "loop": false }
```

路径相对于配置所在目录。MP3 由游戏原生接口异步解码，准备完成后复用缓存，延迟、音量、暂停和同步沿用统一播放流程。WAV 和 MP3 配乐均用于单次播放，精简配置添加音乐时请写 loop=false。ACTION_ASSETS_READY 的 audioFormat 会记录 mp3、wav 或包内音频。

#### 身体范围与位移

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `bodyMask` | "FullBody" | FullBody 控制全身；UpperBody 控制上半身，不包含髋部和腿；LowerBody 仅控制左右腿链，也不包含髋部。 |
| `bonePaths` | [] | 仅控制指定的游戏骨骼完整路径。空数组使用 bodyMask 内全部受支持骨骼；路径必须在所选范围内，不能重复，也不能填 MMD 日文骨名。 |
| `writeHipTranslation` | true | 是否应用髋部位移，仅在控制范围包含 Armature/Hips 时生效。Humanoid 水平走位经过角色控制器处理碰撞，上下起伏应用于髋部；Generic 使用游戏骨架局部位移采样。false 关闭髋部位移但保留旋转。 |

仅播放头部骨骼动作：

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "bodyMask": "UpperBody",
  "writeFaceMorphs": false,
  "bonePaths": [
    "Armature/Hips/Spine/Chest/Neck/Head"
  ]
}
```

#### 可用条件、列表与镜头

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `requiresHands` | true | 是否要求双手可用，手铐等原版状态仍会限制。只有 bodyMask=LowerBody 且 writeFaceMorphs=false 时才允许 false；不会绕过其他状态检查。 |
| `requiresStanding` | true | 是否要求站立，蹲下或坐下时受限。false 仅取消这一要求，不强制退出特殊交互或绕过原版状态检查。 |
| `showInRing` | true | 是否显示新增动作的转盘入口。替换原动作的资源由管理页选择，不因填写 true 而强制进入转盘。 |
| `followFaceCamera` | true | 是否允许原版面部小摄像机跟随，还需设置页总开关开启。它不控制 MMD 配套主镜头或原动作替换的镜头轨道。 |

腿部静音循环：

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "loop": true,
  "bodyMask": "LowerBody",
  "requiresHands": false,
  "writeFaceMorphs": false
}
```

#### 表情

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `writeFaceMorphs` | true | 使用动作中已映射的 BlendShape 轨道，要求匹配的路径与通道；不保证任意自定义表情生效。false 完全交还原版表情控制。 |
| `keepGameExpressions` | true | 允许叠加原游戏表情。省略/true 时遵循设置页；false 强制关闭叠加，界面开关不能覆盖。writeFaceMorphs=false 时无作用。 |

表情使用动作里的轨道，叠加时保留捏脸形状，停止后恢复原版控制。音乐只负责播放，口型由表情轨道提供。

#### 事件与生命周期

| 参数 | 默认值 | 含义 |
| --- | --- | --- |
| `useClipEvents` | true | 转盘播放时读取片段自带事件，适配 OnPlaySe、FootL、FootR。不支持的内置事件会拒绝播放；false 关闭内置事件，但 JSON 事件仍执行。 |
| `events` | [] | 指定时间点事件；时间以角色动作开始为基准，与音乐延迟独立。 |
| `onStart` | [] | 动作实际开始时执行一次，等待加载期间不执行。 |
| `onFinish` | [] | 动作和音乐全部正常结束时执行一次；循环动作不会自然触发。 |
| `onCancel` | [] | 已开始的动作中断时执行。场景或玩家失效时跳过回调，但仍清理请求；尚未开始就取消不会触发。 |

events 每项的字段：

| 字段 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `time` | 无，必填 | 动作开始后的秒数，不能小于 0 或超过动作时长。 |
| `event` | 无，必填 | 已注册的事件名称，见下表。 |
| `args` | {} | 事件参数对象，不能是 null。 |
| `repeat` | "EachLoop" | 循环时每轮执行；Once 为本次播放仅执行一次。重新播放重新计数。 |

onStart/onFinish/onCancel 每项填写 event 和可选 args，执行时机由生命周期决定；time、repeat 仅用于定时事件。

| 事件名 | args | 用途 |
| --- | --- | --- |
| native.playSe | type 必填：原版音效名 | 使用原版玩家音效方法。 |
| native.animationSound | type 必填：原版音效名 | 使用原版动画音效回调，保留其特效分支。一般选 native.playSe 即可。 |
| native.footstep | 见下表，均可省略 | 按原版鞋子、地面、方向与状态处理脚步。 |
| npc.spectators | {} | 需要 NPC 围观插件及其总开关；动作结束时释放本次请求。 |

native.footstep 的 args：

| 字段 | 默认值 | 可填内容 |
| --- | --- | --- |
| `foot` | "Left" | Left 或 Right。 |
| `seType` | "FootStepHeel" | 原版音效枚举名，主要用于高跟鞋分支。 |
| `direction` | "Forward" | Forward、Back、Left、Right、ForwardLeft、ForwardRight、BackLeft、BackRight。 |
| `isMoveAction` | false | 是否按原版移动动作脚步处理，影响侧移方向匹配。 |

args 字段名和枚举名按表中大小写填写。音效名如 FootStepSneaker、FootStepHeel、Barefoot、ClothesSound、UiDecide，完整名单见随包原版音效名称.txt。原版声音检测与脚步间隔仍生效。

音效与围观示例：

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "onStart": [
    { "event": "npc.spectators" }
  ],
  "events": [
    {
      "time": 0.4,
      "event": "native.footstep",
      "args": { "foot": "Left" },
      "repeat": "EachLoop"
    }
  ]
}
```

完整事件注意事项见 [动作事件说明](../shared/动作事件说明.zh-CN.md)。

### 原版属性与任务

以下配置由独立转盘播放的 Unity 动作和 MMD 共用。原动作替换沿用原版属性；独立动作按声明追加效果，省略时保持原版数值与任务资格。

| 参数 | 默认值 | 含义 |
| --- | --- | --- |
| `nativeKind` | "None" | 从下表的原版代表动作读取快感、心跳、可疑度倍率与不适值，作为预设。不会切换原版动作或修改共享主数据。 |
| `stats` | {} | 按字段覆盖预设；未填的字段继承预设，明确填 0 则关闭该项动作贡献。 |
| `poseMission` | false | 是否将独立动作计入原版姿势任务；仍需满足原版暴露与任务区域条件，由原版计时，不直接增加任务进度。 |

`nativeKind` 可填值区分大小写：

| 值 | 读取的原版动作 |
| --- | --- |
| None、Interaction | 无额外数值。 |
| Pose | HipShake |
| Onani | OnaniNormal |
| Dildo | UseDildoWallPussy1 |
| DildoFella | UseDildoWallFella1 |
| DildoWait | UseDildoWallWaitPussy |
| SitDildo | SitDildo |
| Sex | SexStandBack |
| Pee | PeeNormal |

预设读取当前游戏主数据中某个代表动作的数值，同类动作之间仍可能有差异。排尿许可、姿势任务、膀胱补充和体力消耗分别由对应字段控制。

推荐把属性直接写在 JSON 根级，也可放入 stats 对象；根级同名字段优先。例如 ecstasyPerSecond=0 会覆盖 stats 内的同名值，关闭本动作的快感贡献。

全部属性字段：

| 参数 | 省略时的默认值 | 含义与限制 |
| --- | --- | --- |
| `ecstasyPerSecond` | 预设 AddEcstasy；None 为 0 | 基础快感增长/秒。**快感满值为 1（100%），0.1 表示基础每秒增加 10%**。继续使用原版动作速度、心跳和强化倍率，因此不保证最终每秒恰好增加该数值。原版仍处理道具贡献、自然回落和高潮阈值；0 只关闭本动作贡献，不阻止原版道具效果。 |
| `dokidokiAdd` | 预设 AddDokidoki100 ÷ 100；None 为 0 | 本动作的紧张度贡献，范围 0～1，仍受原版等级倍率影响。不是 BPM，也不是每秒累加量。原版每帧重算后叠加一次、限制在 0～1，再平滑心率、呼吸等；自宅保留原版心率规则。 |
| `moisturePerSecond` | 0 | 每秒排入膀胱待吸收队列的量，采用 0～1 的归一化单位。实际吸收速度由原版限制，当前主数据约 0.01/秒；自动补充技能会按原版规则覆盖队列。停止动作后，已排入的部分仍继续吸收。不是衣物湿度。 |
| `staminaPerSecond` | 0 | 每秒消耗的原生体力单位，不是百分比。使用原版消耗方法，保留恢复冷却和耗尽闪烁；耗尽时停止自定义动作。 |
| `strange` | 预设 Strange；None 为 0 | 原版暴露计算之后的动作可疑度基础值，之后继续乘原版光照、察觉度和视线系数。不会直接增加全部 NPC 的可疑条。 |
| `strangenessScale` | 预设 StrangenessScale；None 为 1 | 对原版计算出的方向性暴露可疑度乘此倍率，再加 strange。0 可关闭该部分视觉暴露贡献，不会关闭原版听觉等独立渠道。 |
| `discomfort` | 预设 Discomfort；None 为 0 | 动作提供的不适上限基础值，与原版服装、方向和状态的值取较大者，再沿用原版视线规则；不按帧持续加到缓存。 |
| `canStartPee` | false | 有膀胱存量时允许动作调用原版排尿流程，沿用计时、粒子、排出速度和结束处理。不是强制填满膀胱；false 也不会阻止原版满值后的自然排尿。 |

属性未填写且未使用 nativeKind 预设时，各项额外数值默认为 0，可疑度倍率为 1，排尿和姿势任务开关为 false。按需填写字段即可，displayName 可省略。

数值填写有限的非负数字，dokidokiAdd 范围为 0～1。属性可写在根级或 stats 对象中；数值字段中的字符串、null，以及 stats 内未知或重复的字段会被校验拒绝。

快感达到原版高潮条件时，自定义动作停止，由原版接续高潮动画、结束处理、统计和任务计数；独立动作的专属高潮片段暂未接入。排尿遵循游戏的禁止排尿与液体显示设置，已启动的排尿在动作停止后继续由原版计时结束，膀胱值按正常流程变化。

属性贡献从动作实际开始后生效，暂停时暂停，停止、切换或读档后结束。加载等待和预览阶段沿用原版属性计算。每次开始记录一条 ACTION_STATS，列出实际数值、排尿与姿势任务开关。

CatPose 增加基础快感 0.1/秒，并计入姿势任务：

```json
{
  "poseMission": true,
  "ecstasyPerSecond": 0.1
}
```

V-sign 在膀胱有存量时排尿，并计入姿势任务：

```json
{
  "poseMission": true,
  "canStartPee": true
}
```

分别保存为 catpose.action.json、v-sign.action.json，放在对应 bundle 旁。配置随 bundle 所在目录使用，动作素材由玩家自行准备。


### 参数在不同播放方式中的作用

| 参数类别 | 转盘独立播放 | 原动作替换 |
| --- | --- | --- |
| bundle、clipAsset | 指定播放资源 | 提供候选，按实际 clip 名匹配原版槽位。 |
| id、displayName、description | 标识和显示名称 | id 保存所选来源，原动作仍显示游戏本地化名称。 |
| 音乐、延迟、音量、loop | 使用本 JSON 配置 | 不另播 JSON 音乐；循环由原版状态机决定。 |
| 范围、位移、表情、条件、面部镜头 | 控制独立采样与播放 | 使用原版动作流程，不以 JSON 接管规则覆盖原版。 |
| JSON 事件、useClipEvents | 由插件事件调度器处理 | 不执行 JSON 事件；片段内置事件由原生 Animator 与接收器处理。 |
| 原版属性字段、nativeKind、poseMission（或 stats） | 增加独立动作的属性贡献、排尿及姿势任务资格 | 沿用原动作，不重复应用这些配置。 |
| showInRing | 控制新增动作入口 | 不改变替换候选在管理页中的可用性。 |

### 插件配置默认值

目录由 `BepInEx/config/local.sfm.moreactions.cfg` 的 `[Actions] Directory` 指定，路径相对于更多动作 DLL 所在目录。修改目录、资源或 JSON 后重启游戏。

全局配置保存在 BepInEx/config/local.sfm.moreactions.cfg，动作 JSON 只保存逐动作选项。常用全局选项也可在设置页修改。

| 配置项 | 默认值 | 含义 |
| --- | --- | --- |
| [Actions] Directory | Actions | 动作根目录，相对于更多动作 DLL。 |
| [Audio] Volume | 1 | 全部独立动作音乐的音量倍率。 |
| [Face] KeepGameExpressions | true | 表情叠加总开关，不能覆盖动作 keepGameExpressions=false。 |
| [Camera] FollowFaceCamera | true | 面部镜头跟随总开关，显示仍由原版设置控制。 |
| [Replacements] Enabled | false | 是否应用已选替换；关闭保留选择。 |
| [Replacements] Selections | {} | 原片段到资源 ID 的记录，由管理页维护。 |
| [Shortcuts] LastActions | 空字符串 | 八个转盘槽位记录，由界面维护，一般不用手改。 |
| [Diagnostics] CameraTransitions | true | 状态变化时限次记录镜头诊断，不连续逐帧打印。 |

### 常见问题

- **动作未出现：** 检查目录、JSON、资源名或 ID 冲突，修改后重启；替换资源只出现在管理页。
- **替换未立即生效：** 关闭菜单、退出当前动作并站起，等待原版站立待机。
- **循环配置加载失败：** 检查是否仍设置音频。音量为 0 的音频也不允许循环。
- **两手不合拢或脚部不贴地：** 骨架比例可能不同，不保证精确接触自动适配。
- **属性没有变化：** 修改 JSON 后重启游戏，检查配置是否与 bundle 同名、插件是否支持所填参数，并查看 ACTION_STATS 中的实际值。原动作替换的数值由原版流程计算。
- **不能排尿或任务没有进度：** 排尿需要膀胱有存量且未禁用排尿；姿势任务需要开启 poseMission，并满足原版暴露和任务区域条件。
- **提前停止：** 快感达到原版高潮条件或体力耗尽时会停止自定义动作；另外，商场某些位置播放部分动作可能提前退出，属于已知问题。

反馈请附 BepInEx/LogOutput.log、动作名和时间点。保护性取消会记录一条 ACTION_CANCEL_DIAGNOSTIC，不逐帧打印。

### 升级兼容

- 从 Seleka2 前缀版本升级时，请运行安装脚本迁移动作与配置。安装器会备份移走旧 DLL，保留已有 SFM 配置；手动升级需先移走旧 DLL，避免重复加载。
- 从使用 `BepInEx/plugins/AnimReplacer/` 的试用版升级时，将资源包移至 `Actions/` 并重新选择替换资源和转盘快捷槽。`[LegacyAnimations] Directory` 不生效。
- 替换来源需在本插件中选择，原 AnimReplacer 的勾选记录不会自动导入。同一原动画应由一个插件负责替换。

### 开源参考

动画包导入参考 [Secret Flasher Manaka Anim Replacer](https://f95zone.to/threads/secret-flasher-manaka-anim-replacer.299100/)，动画替换与独立姿势播放也参考了作者提供的源码。作者与许可原文见 [THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md)。
