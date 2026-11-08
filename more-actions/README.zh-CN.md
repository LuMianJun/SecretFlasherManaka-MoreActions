# 更多动作使用说明

为《Secret Flasher Manaka》提供转盘动作、Unity 动画包导入、原动作替换、音乐、表情叠加和动作事件。安装方式与插件组合见[仓库首页](../README.md)。

## 添加动画包

默认目录是 `BepInEx/plugins/SFMMoreActions/Actions/`。不同资源包各放一个子文件夹，允许文件同名：

```text
Actions/
├─ MyPack/
│  ├─ MyPose.bundle
│  └─ Walk.bundle
└─ OtherPack/
   └─ MyPose.bundle
```

将 AnimReplacer 风格的 Unity AssetBundle 放进去，重启游戏即可自动导入包内的动画。不需要原 AnimReplacer DLL、快捷键配置或 OverrideController 模板。普通 `.anim`、FBX 文件需要先制作成匹配游戏的动画包，不能直接放入播放。

Humanoid 动画使用游戏角色的 Avatar；Generic 动画需要匹配游戏骨骼路径。能导入不代表所有骨骼、表情和动画事件都兼容。

- clip 名与原版片段同名时，作为替换资源，只出现在替换设置中。
- 其余新增动作出现在原版转盘，自动导入时默认循环。
- 原动作匹配区分大小写，与 bundle 文件名或资产路径无关。
- 不同包提供同名原动作时，可在设置中选择其中一个来源。

目录由 `BepInEx/config/local.sfm.moreactions.cfg` 的 `[Actions] Directory` 指定，路径相对于更多动作 DLL 所在目录。修改目录、资源或 JSON 后重启游戏。

## 从转盘播放

沿用游戏原版转盘的打开、切页和选择方式，1–8 号入口均可进入自定义动作页。资源逐步加载，列表未更新时稍后重新打开转盘。

选择动作开始；移动、再次选择同一动作、切换原版操作或进入受限状态会停止。选中的资源尚未准备好时会等待，再次选择同一动作可撤销等待，另选动作可替换请求。

正在执行原版允许互切的姿势时，会等资源准备好，再退出原动作并接续播放。手铐、蹲下、坐下及特殊交互等限制仍生效，请站立并停止移动后选择动作。

Tab、Esc 等菜单操作本身不直接停止动作。原版菜单暂停游戏时，动作、音乐和镜头同步暂停，关闭后继续。

支持走位的动作经过游戏角色控制器移动，保留碰撞；受阻会停止，结束后留在实际到达的位置。头发、裙摆等物理仍由游戏处理。

## 原动作替换

打开 **设置 → 更多动作 → 管理动作替换**。原有选择会保留，无需重新配置。

管理页内嵌在游戏设置中，使用游戏字体，支持鼠标操作和键盘导航。两种浏览方式共用同一份替换配置，切换视图不会清空选择。

- **按资源包：** 左侧选择包，右侧显示紧凑勾选列表，可逐项选择、“应用本包”或“清除此包”。
- **按原动作：** 隐藏包列表，以全宽显示全部可替换原动作。点击右侧来源，在列表中选择“原版”或某个资源包。
- **仅已选择：** 只显示已有有效选择的项目。
- **启用动作替换：** 总开关默认关闭，开启后才应用所选资源；关闭保留各项选择。
- **返回或 Esc：** 来源选择列表中先关闭列表，管理页中返回更多动作设置，再次返回可按原版方式离开设置。

同一原动作只启用一个来源，不同原动作可以混用不同包。应用本包只更新该包覆盖的原动作，其余保留；清除此包只撤销当前来自该包的选择，不清除其他包的选择。同一包有多个同名候选时，批量应用保留本包已选候选，否则使用列表中的第一个来源。

关闭菜单且角色站立、未执行动作时应用选择；蹲下、坐下、动作播放及过渡期间会延后应用。读档后自动绑定新角色。

替换动作继续使用原版的触发方式、状态机、移动、碰撞及事件接收流程。主视角保留原片段的相机轨道，动画包只需提供身体动作，无需自带镜头。

游戏原版动画显示本地化名称；外部新增动作和包名保持作者原名。选择保存在更多动作配置的 `[Replacements] Enabled/Selections`，不写入游戏存档，不读取原插件的 `CheckedReplacements.json`。

## 游戏内设置

**设置 → 更多动作** 提供动作音乐音量、原游戏表情叠加、面部镜头跟随和原动作替换。安装 MMD 插件后还会显示 MMD 配套镜头开关。页面支持日、英、简中、繁中、韩语；NPC 围观选项在 **设置 → 游戏系统**。

默认在原游戏表情上叠加动作表情，保留捏脸形状，停止后归还原版表情控制。关闭叠加时只显示动作表情。

动作 JSON 的 `"keepGameExpressions": false` 强制关闭叠加，界面开关不能覆盖；省略或为 `true` 时遵循界面设置。`"writeFaceMorphs": false` 表示动作完全不接管表情。

## JSON 可以省略

只放 Unity AssetBundle 就能自动导入包内全部动画，**不需要 JSON，也不用手写 ID**。MMD 同样可以省略 JSON。只有需要改变默认行为时才添加配置。

| 导入方式 | 配置文件 | 默认行为 |
| --- | --- | --- |
| Unity 自动导入 | 不放 JSON | 导入包内全部片段、静音循环、自动生成 ID。 |
| Unity 指定片段或参数 | 名称.action.json | 必须指定 bundle、clipAsset；其余使用下表默认值。 |
| MMD 导入 | 可选 action.json | 使用 motion.vmd，以及存在的 BGM.wav、camera.vmd。完整参数见 [MMD 使用说明](../vmd-validation/README.zh-CN.md)。 |

两种配置共享播放、音乐、表情、范围和事件参数，但资源字段不同。Unity 使用 bundle、clipAsset；vmdFile、referencePose、camera 属于 MMD 配置。

## 默认 ID

Unity 动作使用 **相对文件夹路径＋片段名**，例如 MyPack/Pose。子目录计入路径，直接放在 Actions 根目录的片段使用 Actions/Pose。

```text
Actions/
├─ MyPack/
│  └─ pose.bundle       片段 Pose → MyPack/Pose
└─ OtherPack/
   └─ pose.bundle       片段 Pose → OtherPack/Pose
```

自动导入使用片段自身名称；JSON 使用 clipAsset 的最后一段并去掉 .anim 扩展名。同一文件夹中片段名相同会产生重复 ID，请分目录存放，或在 JSON 中指定不同 id。改目录或片段名会改变默认 ID；需要固定标识时可显式填写。旧版自动导入 ID 仍可兼容解析，快捷槽和替换来源会在重新识别资源后迁移。

## 使用 JSON

在 bundle 旁放置 名称.action.json。最小配置：

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose"
}
```

若位于 MyPack 文件夹，省略 id 会生成 MyPack/Pose，省略 displayName 会显示 Pose。

被 JSON 引用的 bundle 由配置接管，不再自动展开包内其他动画。同一包需要多个片段时，分别建立配置；默认或显式 ID 都必须唯一。

修改后重启游戏。参数名不区分大小写；JSON 不支持注释，文本用英文双引号，数字不加引号，开关写 true/false。**以下默认值适用于 JSON 模式；无 JSON 自动导入时仅 loop 默认 true。**

## 全部动作参数

### 标识与资源

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `id` | 文件夹＋片段名 | 内部唯一标识，用于快捷槽、替换来源等记录，一般省略即可。显式值不能为空、重复、含英文逗号或控制字符；它不决定替换哪个原动作。 |
| `displayName` | clipAsset 的片段名 | 转盘显示名称，不改变 ID 或替换目标；原动作在管理页中仍使用游戏本地化名称。 |
| `description` | 空字符串 | 动作备注，不影响播放，当前不作为转盘说明展示。 |
| `bundle` | 无，必填 | AssetBundle 文件路径，相对于本 JSON。扩展名不限；普通 .anim、FBX 不能直接使用。 |
| `clipAsset` | 无，必填 | 包内 AnimationClip 资源名，可用可解析的短名称或完整包内路径，如 Pose、assets/animations/pose.anim。它不是磁盘路径。 |

外部文件路径必须位于 JSON 所在目录或其子目录，不能使用绝对路径或跳到目录外。替换按加载后的 clip 名匹配原版槽位，区分大小写，与 bundle 文件名和 id 无关。

### 音乐与时间

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `audioFile` | 空字符串 | 外部 WAV 路径。支持单/双声道 PCM16 或 float32；省略或空字符串表示不使用外部音乐。Unity 动作不会自动查找 BGM.wav。 |
| `audioAsset` | 空字符串 | 包内 AudioClip 资源名；不能与非空 audioFile 同时使用。 |
| `audioDelaySeconds` | 0 | 音乐比角色动作晚开始的秒数，只能大于或等于 0。不改变动作或事件的开始时间；没有音乐时无效果。 |
| `volume` | 1 | 此动作音乐音量倍率，范围 0～1，还受插件和游戏音量影响。0 只是静音，并非未配置音乐。 |
| `loop` | false | 是否循环角色动作。JSON 默认单次播放，无 JSON 导入默认循环。目前带音乐不能循环：true 时不应设置 audioFile/audioAsset，volume=0 也不能绕过限制。 |

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

### 身体范围与位移

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

### 可用条件、列表与镜头

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

### 表情

| 参数 | 默认值 | 含义与限制 |
| --- | --- | --- |
| `writeFaceMorphs` | true | 使用动作中已映射的 BlendShape 轨道，要求匹配的路径与通道；不保证任意自定义表情生效。false 完全交还原版表情控制。 |
| `keepGameExpressions` | true | 允许叠加原游戏表情。省略/true 时遵循设置页；false 强制关闭叠加，界面开关不能覆盖。writeFaceMorphs=false 时无作用。 |

表情叠加保留捏脸形状，停止后恢复原版控制。没有轨道时不会从音乐生成口型。

### 事件与生命周期

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

onStart/onFinish/onCancel 每项只填 event 和可选 args，不填写 time 或 repeat。

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

## 参数在不同播放方式中的作用

| 参数类别 | 转盘独立播放 | 原动作替换 |
| --- | --- | --- |
| bundle、clipAsset | 指定播放资源 | 提供候选，按实际 clip 名匹配原版槽位。 |
| id、displayName、description | 标识和显示名称 | id 保存所选来源，原动作仍显示游戏本地化名称。 |
| 音乐、延迟、音量、loop | 使用本 JSON 配置 | 不另播 JSON 音乐；循环由原版状态机决定。 |
| 范围、位移、表情、条件、面部镜头 | 控制独立采样与播放 | 使用原版动作流程，不以 JSON 接管规则覆盖原版。 |
| JSON 事件、useClipEvents | 由插件事件调度器处理 | 不执行 JSON 事件；片段内置事件由原生 Animator 与接收器处理。 |
| showInRing | 控制新增动作入口 | 不改变替换候选在管理页中的可用性。 |

## 插件配置默认值

以下保存在 BepInEx/config/local.sfm.moreactions.cfg，不写进动作 JSON；界面提供相应的常用选项。

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

## 常见问题

- **动作未出现：** 检查目录、JSON、资源名或 ID 冲突，修改后重启；替换资源只出现在管理页。
- **替换未立即生效：** 关闭菜单、退出当前动作并站起，等待原版站立待机。
- **循环配置加载失败：** 检查是否仍设置音频。音量为 0 的音频也不允许循环。
- **两手不合拢或脚部不贴地：** 骨架比例可能不同，不保证精确接触自动适配。
- **提前停止：** 商场某些位置播放部分动作可能提前退出，属于已知问题。

反馈请附 BepInEx/LogOutput.log、动作名和时间点。保护性取消会记录一条 ACTION_CANCEL_DIAGNOSTIC，不逐帧打印。

## 升级兼容

- 从 Seleka2 前缀版本升级时，请运行安装脚本迁移动作与配置。安装器会备份移走旧 DLL，保留已有 SFM 配置；手动升级需先移走旧 DLL，避免重复加载。
- 从使用 `BepInEx/plugins/AnimReplacer/` 的试用版升级时，将资源包移至 `Actions/` 并重新选择替换资源和转盘快捷槽。`[LegacyAnimations] Directory` 不生效。
- 本插件不读取原 AnimReplacer 的勾选记录。不要同时让两个插件替换相同的原动画。

## 开源参考

动画包导入参考 [Secret Flasher Manaka Anim Replacer](https://f95zone.to/threads/secret-flasher-manaka-anim-replacer.299100/)，动画替换与独立姿势播放也参考了作者提供的源码。作者与许可原文见 [THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md)。
