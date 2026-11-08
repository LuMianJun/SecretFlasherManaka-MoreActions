# SecretFlasherManaka More Actions

为《Secret Flasher Manaka》提供自定义动作、Unity 动画包导入、MMD 播放和可选的 NPC 围观，适用于 **BepInEx 6 IL2CPP**。

## 插件组成

| 插件 | 当前版本 | 用途 |
| --- | --- | --- |
| 更多动作 | 1.5.0 | 转盘播放、Unity 动画包导入、原动作替换及游戏内设置。 |
| MMD | 1.2.0 | 直接播放 VMD，可配套音乐和镜头；需要更多动作。 |
| NPC 围观 | 1.0.0 | 让符合条件的普通行人走近围观；可独立使用。 |

播放 MMD 安装“更多动作 + MMD”；需要围观时再保留 NPC 插件。公开安装包不附带游戏程序集、动作、模型、镜头或音乐，请自行准备并遵守素材作者的使用条款。

## 安装

先安装 **BepInEx 6 的 IL2CPP 版本**。从本仓库 [Releases](https://github.com/LuMianJun/SecretFlasherManaka-MoreActions/releases) 下载最新安装包，退出游戏后解压，将包内的 `BepInEx` 文件夹合并到游戏根目录即可。

更新时也请先退出游戏，并配套更新更多动作与 MMD。使用过 Seleka2 前缀版本的用户，请使用包内安装器 `install-mods.ps1` 完成动作与配置迁移，避免两套 DLL 同时加载。

## 导入 MMD 动作

在 `BepInEx/plugins/SFMVmdProbe/Motions/` 下，每个动作单独建一个文件夹：

```text
Motions/
└─ 我的舞蹈/
   ├─ motion.vmd
   ├─ camera.vmd
   └─ BGM.wav
```

- `motion.vmd`：角色动作，必需。
- `camera.vmd`：配套镜头，可选。
- `BGM.wav`：配套音乐，可选，支持单/双声道 PCM16 或 float32 WAV。

**只放 motion.vmd 就能播放，不需要配套模型或预先转换。** 文件夹名作为动作显示名称，不同动作请用不同的文件夹名。放好资源后重启游戏。

## 导入 Unity 动画包

将 AnimReplacer 风格的 Unity AssetBundle 放入 `BepInEx/plugins/SFMMoreActions/Actions/`，不同资源包可各建一个子文件夹：

```text
Actions/
├─ 我的动作包/
│  ├─ catpose
│  └─ v-sign
└─ 另一个动作包/
   └─ pose.bundle
```

只放 bundle 即可自动导入，不需要原 AnimReplacer 插件。普通 `.anim` 或 FBX 文件不能直接导入。

- CatPose、V-sign 等新增动作从转盘播放，自动导入时默认循环。
- 与原版片段同名的动作作为替换资源，只出现在替换管理页。
- 放好资源后重启游戏；导入的兼容条件见更多动作说明。

## 播放与设置

进入场景后，沿用原版转盘的打开、切页和选择方式；1–8 号入口均可访问自定义动作页。资源会在后台准备，列表未更新时稍后重新打开；已选择但尚未准备好的动作会等待加载。

移动、再次选择同一动作或切换原版操作可以停止播放。手铐、蹲下等原版限制仍生效；打开菜单本身不直接停止动作，原版暂停游戏时动作与音乐同步暂停。

打开 **设置 → 更多动作** 调整动作音乐、表情叠加和镜头选项。替换原版动作时，进入 **管理动作替换** 选择来源并开启总开关，角色恢复站立待机后应用。

NPC 围观在 **设置 → 游戏系统** 中开启并选择触发方式。NPC 使用原版导航走近，墙壁、楼层、路线和 NPC 状态会影响能否到场。

## 可选 JSON 与详细说明

基础导入不需要 JSON。需要修改显示名称、播放行为、属性或任务资格时，可为 Unity 动画包添加同名 `.action.json`，或在 MMD 动作文件夹添加 `action.json`；只填写需要修改的字段即可。修改后重启游戏。

各 Mod 的功能、参数含义、默认值和完整示例见：

- [更多动作说明](more-actions/README.zh-CN.md)：Unity 动画包、替换管理、精简 JSON、全部动作字段及属性参数。
- [MMD 说明](mmd/README.zh-CN.md)：VMD、音乐、配套镜头、参考姿势、表情及属性配置。
- [NPC 围观说明](npc-spectators/README.md)：触发方式、配置默认值、围观范围与解散条件。
- [动作事件说明](shared/动作事件说明.zh-CN.md)：音效、脚步及围观事件。

## 已知问题与反馈

- 在商场某些位置播放部分动作时，可能提前退出。
- 游戏角色与动作作者模型的比例不同，合掌、摸脸、跪姿等精确接触可能存在偏差。

反馈请附 `BepInEx/LogOutput.log`、动作名、大致时间点和场景。

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
