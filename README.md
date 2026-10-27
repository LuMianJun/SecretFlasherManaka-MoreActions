# SecretFlasherManaka More Actions

为《Secret Flasher Manaka》添加自定义动作、MMD 播放和可选的 NPC 围观功能，适用于 **BepInEx 6 IL2CPP**。

直接放入 VMD 即可播放，无需先用 Unity 或 Blender 转换动画。支持配套音乐、镜头、动作走位和部分表情，沿用游戏原有转盘和设置界面。

## 插件组成

| 插件 | 当前版本 | 功能与依赖 |
| --- | --- | --- |
| 更多动作 | 0.5.0 | 提供转盘入口、播放控制、音乐、表情叠加、设置和动作事件；可独立加载已适配游戏骨架的 Unity 动画包。 |
| MMD | 0.9.0 | 直接读取 VMD，支持配套音乐、镜头和走位；需要安装更多动作。 |
| NPC 围观 | 0.2.0 | 让符合条件的普通行人靠近围观；可单独使用，也可与 MMD 或动作事件联动。 |

只想播放 MMD，安装“更多动作 + MMD”即可；需要围观时再安装 NPC 插件。

## 安装

1. 在游戏中安装 **BepInEx 6 的 IL2CPP 版本**，启动一次，确认 BepInEx 正常加载后退出游戏。
2. 从本仓库的 [Releases](https://github.com/LuMianJun/SecretFlasherManaka-MoreActions/releases) 下载插件安装包。
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
| `source.pmd` | 动作配套模型，用于辅助骨架匹配 | 可选 |
| `action.json` | 调整名称、文件名和播放参数 | 可选 |

**只放入 `motion.vmd` 就能播放，不需要编写 JSON。** 文件夹名默认同时作为动作名称和 ID，支持中文；不同动作请使用不同名称，不要包含英文逗号。

没有 `source.pmd` 时，插件从游戏角色的绑定姿势生成虚拟 MMD 骨架，不需要额外的默认模型。配套 PMD 只用于动作匹配，不改变玩家外观。

放好资源后重启游戏，进入场景，沿用游戏原有转盘的打开、切页和选择方式，找到并选择自定义动作。1–8 号转盘入口均可访问自定义动作页；快捷槽选择记录保存在插件配置中。

动作和音乐会在后台准备，动作逐步加入列表。刚进入游戏时列表尚未更新，可稍后重新打开转盘；选中的动作尚未准备好时会等待，按取消可撤销等待。

### 停止与走位

移动、取消、再次选择同一动作或打开其他界面会停止播放；手铐、坐下等原版状态仍可能限制动作。

默认保留 MMD 动作的水平走位，通过游戏现有角色控制器移动并保留碰撞。走位受阻会停止动作，取消或结束后角色留在实际到达的位置。动作中的上下起伏应用于髋部，并不等同于角色控制器的物理跳跃。

配套镜头按动作自身的轨迹播放，不额外叠加玩家走位补偿。

## 游戏内设置

进入 **设置 → 游戏系统**，可调整：

- 动作音乐音量。
- 原游戏表情叠加。
- 原版面部摄像机跟随。
- MMD 配套镜头开关。
- NPC 围观总开关、手动召集、自动触发、半径和间距。

打开设置会沿用原规则中止当前动作，修改后下次播放生效。设置保存在 `BepInEx/config`，无需修改游戏存档。

“面部摄像机跟随”控制游戏原有面部摄像机；“MMD 配套镜头”控制 `camera.vmd`，两者分别设置。

### 原游戏表情叠加

默认在原游戏表情上叠加动作表情。同通道权重相加后限制在 0–100，目前支持眨眼和五个元音口型。

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

## 更多动作与事件

更多动作还支持已经适配游戏骨架的 Unity Generic 动画包，见 [更多动作使用说明](more-actions/README.zh-CN.md)。普通 `.anim` 或 FBX 不能直接当作动作包导入。

JSON 动作事件可在指定时间播放原版音效、触发脚步或召集 NPC，见 [动作事件说明](shared/动作事件说明.zh-CN.md)。

## 常见问题

**列表里没有动作？**

检查插件依赖、`Motions/动作名称/motion.vmd` 的路径和文件名。首次准备需要时间，可稍后重新打开转盘。修改文件后需要重启游戏；JSON 拼写、格式或资源路径错误请查看 `BepInEx/LogOutput.log`。

**没有音乐或镜头？**

检查 `BGM.wav`、`camera.vmd`，以及 JSON 中是否禁用了对应资源。音乐支持单/双声道 PCM16 或 float32 WAV；镜头还需开启游戏设置中的 MMD 配套镜头。

**动作、脚部或镜头与原 MMD 视频不完全一致？**

游戏角色与动作作者模型的骨架和比例不同，部分 IK、特殊骨骼和表情仍有兼容差异。可尝试提供原动作配套的 `source.pmd`，或调整镜头比例和高度；目前不支持 PMX，也不保证所有 MMD 特性完整还原。

**为什么表情开关开启后仍不叠加？**

检查动作 JSON 是否设置了 `keepGameExpressions: false`，它优先于界面开关。若设置了 `writeFaceMorphs: false`，动作本身不会接管表情。

## 开源参考与依赖

### MMD 实现参考

| 项目或资料 | 参考范围 |
| --- | --- |
| [UnityVMDPlayer](https://github.com/hobosore/UnityVMDPlayer/blob/2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8/UnityVMDPlayer.cs) | 参考中心、槽骨骼与全亲的位移处理，以及 0.08 的单位比例。当前实现从求解后的层级提取位移，接入游戏现有角色控制器。 |
| [three.js r160 — MMDLoader](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/loaders/MMDLoader.js) | 早期 VMD 动画、插值、坐标及镜头实现的参考。 |
| [three.js r160 — MMDAnimationHelper](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/animation/MMDAnimationHelper.js) | MMD 动画求值与镜头行为参考。 |
| [mmdparser（three.js r160 内附版本）](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/libs/mmdparser.module.js) | VMD、PMD 文件结构及数据读取参考。 |
| [blender_mmd_tools 单位说明](https://github.com/powroupi/blender_mmd_tools/wiki/1-MMD-unit-in-real-world-units) | 核对 MMD 单位到米的 0.08 换算约定；运行插件不需要 Blender。 |

上述项目用于算法、格式或行为参考。本插件保留自己的 C# VMD 求解流程；游戏骨架生成、后台准备、骨架缓存、碰撞控制器接入、取消逻辑、转盘与设置联动在本仓库中实现。

UnityVMDPlayer 的参考版本及 MIT 许可说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。其他参考项目及玩家自行添加的素材仍适用各自许可；插件代码的许可不代表可以重新配布动作、镜头、模型或音乐。

### 运行基础

- [BepInEx](https://github.com/BepInEx/BepInEx)：插件加载与配置。
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)：IL2CPP 互操作。
- [HarmonyX](https://github.com/BepInEx/HarmonyX)：游戏方法补丁。

## 从源码构建

构建插件需要支持 .NET 6 目标框架的 SDK，以及目标游戏已经生成的 `BepInEx/core` 和 `BepInEx/interop`。游戏和 Unity 程序集由使用者本地提供，不包含在本仓库中。

在仓库根目录运行：

```powershell
$gameDir = "D:\Games\SecretFlasherManaka"

# 同时构建 MMD 及其依赖的更多动作
dotnet build ./vmd-validation/Plugin/VmdProbe.csproj -c Release "-p:GameDir=$gameDir"

# 构建可独立安装的 NPC 围观插件
dotnet build ./npc-spectators/Plugin/Seleka2.NpcSpectators.csproj -c Release "-p:GameDir=$gameDir"
```

DLL 输出到各插件项目的 `bin/Release/net6.0`。只需要更多动作时，可以单独构建 `more-actions/Plugin/MoreActions.csproj`。

仓库中的验证项目用于离线测试解析、播放规则、表情、缓存、事件和围观逻辑。它们使用模拟的 Unity/游戏边界，不能代替原游戏中的实际画面、导航和碰撞测试。
