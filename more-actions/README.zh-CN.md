# 更多动作使用说明

为《Secret Flasher Manaka》提供转盘动作、Unity 动画包导入、原动作替换、音乐、表情叠加和动作事件。安装方式与插件组合见[仓库首页](../README.md)。

## 添加动画包

默认目录是 `BepInEx/plugins/Seleka2MoreActions/Actions/`。不同资源包各放一个子文件夹，允许文件同名：

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

目录由 `BepInEx/config/local.seleka2.moreactions.cfg` 的 `[Actions] Directory` 指定，路径相对于更多动作 DLL 所在目录。修改目录、资源或 JSON 后重启游戏。

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

## 使用 JSON 配置动画

需要指定显示名称、音乐、事件或播放方式时，在 bundle 旁放置 `名称.action.json`：

```text
Actions/
└─ MyPack/
   ├─ MyPose.bundle
   └─ MyPose.action.json
```

```json
{
  "id": "my-pose",
  "displayName": "我的姿势",
  "bundle": "MyPose.bundle",
  "clipAsset": "MyPose",
  "loop": true
}
```

`id` 必须唯一，不能包含英文逗号；`bundle` 是相对于 JSON 的文件路径，`clipAsset` 是包内动画资源名。被 JSON 引用的 bundle 由 JSON 接管，不再自动展开包内其他动画；同一包需要多个动作时，分别编写配置并使用不同 ID。

若 clip 对应原版动画，仍会作为替换候选，不需要额外的替换字段。

音乐用 `audioFile` 指定外部 WAV，或用 `audioAsset` 指定包内音频，两者选其一。带音乐的动作不支持循环，请设置 `"loop": false`。外部 WAV 支持单/双声道 PCM16 或 float32。

例如，单次播放并配置音乐：

```json
{
  "id": "my-dance",
  "displayName": "我的舞蹈",
  "bundle": "MyPose.bundle",
  "clipAsset": "MyPose",
  "audioFile": "BGM.wav",
  "audioDelaySeconds": 1,
  "volume": 0.7
}
```

这些音乐、表情和事件设置用于转盘独立播放。原动作替换只使用选中的动画片段，不会另行播放 JSON 中的背景音乐或事件。

VMD 使用 MMD 插件的 `motion.vmd` 与可选 `action.json`，无需制作 bundle，见 [MMD 使用说明](../vmd-validation/README.zh-CN.md)。

## 动作事件

通过 `events`、`onStart`、`onFinish`、`onCancel`，可以定时播放原版音效、脚步或召集 NPC。参数与示例见 [动作事件说明](../shared/动作事件说明.zh-CN.md)。

转盘播放可读取 Unity 动画内置的 `OnPlaySe`、`FootL`、`FootR`。内置事件不受支持时，可设置 `"useClipEvents": false` 并按需用 JSON 配置支持的事件。

## 常见问题

- **动作未出现：** 检查目录、JSON 和资源名，修改后重启。替换资源只在设置中显示，不进入转盘。
- **替换没有立即生效：** 关闭菜单，结束当前动作并站起，等待角色恢复站立。
- **两手无法合拢或脚部不贴地：** 动作作者与游戏角色的骨架比例可能不同，不保证精确接触自动适配。
- **提前停止：** 在商场某些位置播放部分动作可能提前退出，属于已知问题。

反馈问题时请附 `BepInEx/LogOutput.log`、动作名和大致时间点。保护性取消会记录一条 `ACTION_CANCEL_DIAGNOSTIC`，包含原因、输入及位置差值，不逐帧打印。

## 升级兼容

- 从使用 `BepInEx/plugins/AnimReplacer/` 的试用版升级时，将资源包移至 `Actions/` 并重新选择替换资源和转盘快捷槽。`[LegacyAnimations] Directory` 不生效。
- 本插件不读取原 AnimReplacer 的勾选记录。不要同时让两个插件替换相同的原动画。

## 开源参考

动画包导入参考 [Secret Flasher Manaka Anim Replacer](https://f95zone.to/threads/secret-flasher-manaka-anim-replacer.299100/)，动画替换与独立姿势播放也参考了作者提供的源码。作者与许可原文见 [THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md)。
