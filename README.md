# SecretFlasherManaka More Actions

为《Secret Flasher Manaka》添加自定义动作、MMD 播放和可选的 NPC 围观功能，适用于 **BepInEx 6 IL2CPP**。

直接放入 VMD 即可播放，无需先用 Unity 或 Blender 转换动画。支持配套音乐、镜头、动作走位和部分表情，沿用游戏原有转盘和设置界面。

## 插件组成

| 插件 | 当前版本 | 功能与依赖 |
| --- | --- | --- |
| 更多动作 | 1.4.0 | 提供转盘入口、播放控制、音乐、表情叠加、设置和动作事件；支持 Unity 动画包导入和原动作替换。 |
| MMD | 1.1.0 | 直接读取 VMD，支持配套音乐、镜头和走位；需要安装更多动作。 |
| NPC 围观 | 1.0.0 | 从无遮挡位置分配围观点，调用原版 NPC 目标移动；一轮围观保持固定中心，到位后面向玩家。可独立使用。 |

只想播放 MMD，安装“更多动作 + MMD”即可；需要围观时再安装 NPC 插件。

## 安装

1. 在游戏中安装 **BepInEx 6 的 IL2CPP 版本**，启动一次，确认 BepInEx 正常加载后退出游戏。
2. 从本仓库的 [Releases](https://github.com/LuMianJun/SecretFlasherManaka-MoreActions/releases) 下载与上表版本对应的安装包；尚无对应版本时，可按[构建说明](VALIDATION.md)自行构建。
3. 解压，在 PowerShell 中进入解压目录，运行：

   ```powershell
   .\install-mods.ps1 -GameDir "D:\Games\SecretFlasherManaka"
   ```

   将示例路径换成自己的游戏根目录，也就是游戏 EXE 所在目录。安装器会检查目标文件占用、备份旧文件并校验复制结果，失败时回滚。

也可以手动把包内 `BepInEx` 文件夹合并到游戏根目录。更新时先退出游戏并备份旧插件。

安装后的插件目录：

```text
游戏目录/
└─ BepInEx/
   └─ plugins/
      ├─ Seleka2MoreActions/
      │  └─ Seleka2.MoreActions.dll
      ├─ Seleka2VmdProbe/
      │  └─ Seleka2.VmdProbe.dll
      └─ Seleka2NpcSpectators/
         └─ Seleka2.NpcSpectators.dll
```

`Seleka2`、`VmdProbe` 是现有插件的内部目录和程序集名称，安装时保留即可。

公开仓库和公开安装包不附带游戏程序集、MMD 动作、镜头、模型或音乐。请自行准备资源，并遵守作者的使用与配布条款。

## 添加并播放 MMD 动作

在下面的目录中新建一个文件夹：

```text
BepInEx/plugins/Seleka2VmdProbe/Motions/我的舞蹈/
```

按默认文件名放入资源：

| 文件 | 用途 | 是否必需 |
| --- | --- | --- |
| `motion.vmd` | 角色动作 | 必需 |
| `camera.vmd` | 配套镜头 | 可选 |
| `BGM.wav` | 配套音乐 | 可选 |
| `action.json` | 调整名称、文件名和播放参数 | 可选 |

**只放入 `motion.vmd` 就能播放，不需要编写 JSON。** 文件夹名默认同时作为动作名称和 ID，支持中文；不同动作请使用不同名称，不要包含英文逗号。

插件统一从游戏角色的绑定姿势生成虚拟 MMD 骨架，不需要额外模型。默认采用 A-pose 参考校准，可用动作 JSON 的 `referencePose` 切换 T-pose。

放好资源后重启游戏，进入场景，沿用游戏原有转盘的打开、切页和选择方式，找到并选择自定义动作。1–8 号转盘入口均可访问自定义动作页；快捷槽选择记录保存在插件配置中。

动作和音乐会在后台准备，动作逐步加入列表。刚进入游戏时列表尚未更新，可稍后重新打开转盘；选中的动作尚未准备好时会等待，再次选择同一动作可撤销等待。

### 停止与走位

移动、再次选择同一动作、切换原版操作或进入受限状态会停止播放；手铐、坐下等原版状态仍可能限制动作。通用返回/取消输入不直接结束动作；打开菜单保留动作，并随原版游戏暂停或继续。

默认保留 MMD 动作的水平走位，通过游戏现有角色控制器移动并保留碰撞。走位受阻会停止动作，取消或结束后角色留在实际到达的位置。动作中的上下起伏应用于髋部，并不等同于角色控制器的物理跳跃。

配套镜头按动作自身的轨迹播放，不额外叠加玩家走位补偿。

默认使用 A-pose 参考校准。需要沿用 T-pose 参考的动作，可在原有 `action.json` 中添加 `"referencePose": "T"`；省略或填写 `"A"` 使用默认值。修改后重启游戏。

MMD 支持基础模型的微笑、眉毛、单眼眨眼等标准表情，保留捏脸形状。动作需要自带相应的 VMD 表情轨道；没有轨道时不会自动生成表情或口型。

播放时保留游戏角色的体型、骨长和鞋跟计算，停止后恢复原版姿势控制。头发、裙摆等物理仍由游戏处理。

## 游戏内设置

进入 **设置 → 更多动作**，可调整：

- 动作音乐音量。
- 原游戏表情叠加。
- 原版面部摄像机跟随。
- MMD 配套镜头开关。
- 原动作替换总开关与独立替换管理面板。

NPC 围观保持独立，相关选项仍位于 **设置 → 游戏系统**。

打开设置保留当前动作，并随原版游戏暂停或继续。设置保存在 `BepInEx/config`，无需修改游戏存档。

“面部摄像机跟随”控制游戏原有面部摄像机；“MMD 配套镜头”控制 `camera.vmd`，两者分别设置。

### 原游戏表情叠加

默认在原游戏表情上叠加动作表情。同通道权重相加后限制在 0–100；MMD 使用基础模型的 48 个标准表情槽位，实际播放已匹配的 VMD 轨道，包含眨眼、元音口型、眉毛和微笑等通道。

关闭“叠加原游戏表情”后，仅显示动作表情，保留捏脸形状；动作停止后归还原版表情控制。

每个动作还可以通过 JSON 强制关闭叠加：

```json
{
  "keepGameExpressions": false
}
```

优先级如下：

| 动作 JSON | 游戏设置 | 结果 |
| --- | --- | --- |
| 省略或 `true` | 开启 | 在原游戏表情上叠加动作表情。 |
| 省略或 `true` | 关闭 | 只显示动作表情。 |
| `false` | 任意 | 强制关闭叠加，游戏设置无法覆盖。 |

`writeFaceMorphs: false` 则表示这个动作完全不接管表情，原游戏表情照常运行。

## 可选的 action.json

在动作文件夹中创建 `action.json`，只写需要覆盖的参数，其余保持默认。修改后重启游戏。JSON 不支持注释，注意文件名不能是 `action.json.txt`。

例如，让音乐晚一秒开始、降低音量，并关闭这个动作的原游戏表情叠加：

```json
{
  "audioDelaySeconds": 1,
  "volume": 0.7,
  "keepGameExpressions": false
}
```

如果不想重命名下载的文件：

```json
{
  "vmdFile": "舞蹈动作.vmd",
  "audioFile": "歌曲.wav",
  "camera": {
    "vmdFile": "配套镜头.vmd"
  }
}
```

资源路径相对于当前动作文件夹。没有对应资源，就省略相应字段。

常用参数：

| 参数 | 默认值 | 作用 |
| --- | --- | --- |
| `displayName` | 文件夹名 | 转盘中的显示名称。 |
| `audioDelaySeconds` | `0` | 音乐比动作晚开始的秒数，只支持非负数。 |
| `volume` | `1` | 单个动作音乐音量，范围 0–1，还受游戏和插件音量影响。 |
| `audioFile` | 自动查找 `BGM.wav` | 指定音乐文件；空字符串禁用音乐。 |
| `writeHipTranslation` | `true` | 使用水平走位和髋部上下起伏；动作范围需包含髋部。 |
| `writeFaceMorphs` | `true` | 应用已支持的动作表情。 |
| `keepGameExpressions` | `true` | 允许叠加原游戏表情；`false` 强制关闭。 |
| `loop` | `false` | 循环角色动作；当前仅支持关闭音乐后的静音循环。 |
| `camera.enabled` | `true` | 是否使用配套镜头，还需开启游戏内的 MMD 镜头开关。 |
| `camera.scale` | `0.08` | 配套镜头位置和距离的换算比例。 |
| `camera.heightOffset` | `0` | 配套镜头整体高度偏移，单位为游戏中的米。 |

`camera.*` 参数填写在 `camera` 对象内，写法见上面的示例。完整参数和更多示例见 [MMD 使用说明](vmd-validation/README.zh-CN.md)。

## NPC 围观

安装 NPC 插件后，可在游戏设置中使用“手动召集”，关闭设置后生效；移动或取消会结束手动请求。

要让所有导入的 MMD 动作自动触发围观，同时开启：

1. `[NPC围观] 启用`。
2. `[NPC围观] MMD动作触发`。
3. `[NPC围观] 所有MMD动作`。

关闭“所有MMD动作”时，使用配置文件中的动作 ID 名单。也可以仅给某个动作添加事件：

```json
{
  "onStart": [
    { "event": "npc.spectators" }
  ]
}
```

此事件需要安装 NPC 插件并开启其总开关。动作停止时会自动释放本次围观请求。

NPC 使用原版寻路实际走近，只招募符合条件的普通行人。警察、固定店员、坐着或正在交互等 NPC 不会被招募；障碍物和场景导航会影响到场情况。原版发现玩家和任务判定仍然生效。

更多说明见 [NPC 围观说明](npc-spectators/README.md)。

## 添加 Unity 动画包

将动画 AssetBundle 放入更多动作插件的 `Actions` 目录，每个资源包可单独建立子文件夹：

```text
BepInEx/plugins/Seleka2MoreActions/Actions/
├─ 我的动作包/
│  ├─ pose.bundle
│  └─ walk.bundle
└─ 另一个动作包/
   └─ pose.bundle
```

支持 AnimReplacer 风格的动画包，无需安装原插件 DLL、快捷键配置或 OverrideController 模板。只放 bundle 即可自动导入其中的动画；普通 `.anim` 或 FBX 文件不能直接使用。Humanoid 动画使用游戏角色的 Avatar，Generic 动画需要匹配游戏骨骼路径。

- 与原版动画同名的资源用于原动作替换，只出现在设置页。
- CatPose、V-sign 等新增动作从转盘播放，自动导入的动作默认循环，再次选择或移动可停止。
- 不同目录中的同名文件可以共存；同名原动作的不同来源在设置中选择。

需要自定义名称、音乐或事件时，可添加 `名称.action.json`。被 JSON 引用的 bundle 不会再自动展开，避免重复导入。配置示例见 [更多动作使用说明](more-actions/README.zh-CN.md)。

动画包目录由更多动作配置 `[Actions] Directory` 指定，默认是插件目录下的 `Actions`；修改资源或配置后重启游戏。

### 替换原版动作

1. 打开 **设置 → 更多动作 → 管理动作替换**。
2. 默认按资源包浏览：左侧选包，右侧勾选动作；也可“应用本包”或“清除此包”。
3. 切到“按原动作”后，全宽列表显示全部可替换原动作；点击右侧来源，在选择列表中切换原版或其他包。“仅已选择”可筛选已配置项。
4. 开启“启用动作替换”，关闭设置；角色恢复站立待机后应用选择。

同一原动作只启用一个来源，选择新来源会取消旧来源。应用本包只更新该包提供的动作，其他动作保留；清除此包只撤销当前来自该包的选择。包内同一动作有多个候选时，保留已选候选，否则使用列表中的第一个来源。

总开关默认关闭，关闭后恢复全部原动画并保留选择。来源选择列表中的“返回”或 Esc 只关闭列表；管理页中返回更多动作设置。切换设置分类可正常离开面板。

匹配依据是包内 clip 名与游戏原动画名，区分大小写，与 bundle 文件名无关。设置只显示当前角色控制器中有匹配资源的动画。原版动作显示本地化名称，外部新增动作和资源包保留作者名称。

替换沿用原版动作的触发方式、状态机、移动和碰撞规则，镜头保留原版取景；动作包无需提供镜头。选择保存在更多动作配置的 `[Replacements]` 中，不写入游戏存档，也不读取原 AnimReplacer 的勾选记录。

### 动作事件

JSON 可在指定时间播放原版音效、触发脚步或召集 NPC，见 [动作事件说明](shared/动作事件说明.zh-CN.md)。

## 常见问题

**列表里没有动作？**

检查插件依赖、`Motions/动作名称/motion.vmd` 的路径和文件名。首次准备需要时间，可稍后重新打开转盘。修改文件后需要重启游戏；JSON 拼写、格式或资源路径错误请查看 `BepInEx/LogOutput.log`。

**没有音乐或镜头？**

检查 `BGM.wav`、`camera.vmd`，以及 JSON 中是否禁用了对应资源。音乐支持单/双声道 PCM16 或 float32 WAV；镜头还需开启游戏设置中的 MMD 配套镜头。

**动作、脚部或镜头与原 MMD 视频不完全一致？**

游戏角色与动作作者模型的骨架和比例不同，部分 IK、特殊骨骼和表情仍有兼容差异。合掌、摸脸、牵手等精确接触可能存在偏差；当前不提供自动接触适配，也不保证所有 MMD 特性完整还原。

**没有操作却提前停止？**

已知问题：在商场某些位置播放部分动作时，可能提前退出。取消时会记录一条 `ACTION_CANCEL_DIAGNOSTIC`，不会逐帧打印，详见 [更多动作使用说明中的诊断章节](more-actions/README.zh-CN.md)。

**为什么表情开关开启后仍不叠加？**

检查动作 JSON 是否设置了 `keepGameExpressions: false`，它优先于界面开关。若设置了 `writeFaceMorphs: false`，动作本身不会接管表情。

## 升级兼容说明

- 更新时请同时安装包内的更多动作与 MMD DLL，避免只更新其中一个。
- 从使用 `BepInEx/plugins/AnimReplacer/` 目录的试用版升级时，请把动画包移至 `BepInEx/plugins/Seleka2MoreActions/Actions/`；旧的 `[LegacyAnimations] Directory` 配置不生效。迁移后请重新选择替换资源和转盘快捷槽。
- 使用过配套 PMD 模型配置的动作，请删除 `action.json` 中的 `modelFile` 字段。`source.pmd` 不参与导入，插件使用游戏角色生成骨架。安装器保留已有动作、模型、音乐和配置文件。

## 开源参考与依赖

### MMD 实现参考

| 项目或资料 | 参考范围 |
| --- | --- |
| [UnityVMDPlayer](https://github.com/hobosore/UnityVMDPlayer/blob/2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8/UnityVMDPlayer.cs) | 参考中心、槽骨骼与全亲位移、0.08 单位比例、默认 A-pose、骨骼插值参数布局及 MorphPlayer 表情绑定。当前位移接入游戏现有角色控制器，表情沿用游戏侧叠加与外观保护。 |
| [three.js r160 — MMDLoader](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/loaders/MMDLoader.js) | VMD 动画、插值、坐标及镜头实现参考。 |
| [three.js r160 — MMDAnimationHelper](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/animation/MMDAnimationHelper.js) | MMD 动画求值与镜头行为参考。 |
| [mmdparser（three.js r160 内附版本）](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/libs/mmdparser.module.js) | VMD 文件结构及数据读取参考。 |
| [blender_mmd_tools 单位说明](https://github.com/powroupi/blender_mmd_tools/wiki/1-MMD-unit-in-real-world-units) | 核对 MMD 单位到米的 0.08 换算约定；运行插件不需要 Blender。 |

上述项目用于算法、格式或行为参考。本插件保留自己的 C# VMD 求解流程；游戏骨架生成、后台准备、骨架缓存、碰撞控制器接入、取消逻辑、转盘与设置联动在本仓库中实现。

UnityVMDPlayer 的参考版本及 MIT 许可说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。其他参考项目及玩家自行添加的素材仍适用各自许可；插件代码的许可不代表可以重新配布动作、镜头、模型或音乐。

### Unity 动画包参考

[AnimReplacer](https://f95zone.to/threads/secret-flasher-manaka-anim-replacer.299100/)：本插件的 Unity 动画包导入参考此项目，并参考作者提供的源码实现动画替换与独立姿势播放。本插件将其接入原版转盘与设置页，不包含原插件的窗口或快捷键系统。作者与 MIT 许可原文见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

### 运行基础

- [BepInEx](https://github.com/BepInEx/BepInEx)：插件加载与配置。
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)：IL2CPP 互操作。
- [HarmonyX](https://github.com/BepInEx/HarmonyX)：游戏方法补丁。
