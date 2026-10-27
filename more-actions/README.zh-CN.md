# 更多动作使用说明

更多动作可独立使用，也为 MMD 提供环状列表、状态限制、动作音乐、镜头跟随和动作事件。

## 播放

在原版环状列表选择动作。移动、取消、再次选择同一动作或进入受限状态会停止。设置 → 游戏系统中可调整动作音乐音量及面部镜头跟随。

支持走位的 MMD 动作会通过原角色控制器移动，碰撞受阻会停止。动作走位不会被当作玩家主动移动取消；停止后角色留在实际到达位置。

## 导入普通 Unity 动画

将已经适配游戏骨架的 Unity Generic 动画包放入本插件 actions 下的独立文件夹，并添加 名称.action.json，例如：

```json
{
  "id": "clap",
  "displayName": "鼓掌",
  "bundle": "clap.bundle",
  "clipAsset": "clap"
}
```

bundle 为动画包文件名，clipAsset 为包内动画资源名称。音频可用 audioFile 指定外部 WAV，或用 audioAsset 指定包内音频，二者选择其一。普通 .anim 或 FBX 不能直接放入使用，需要准备好匹配骨架的动画包。

如果导入 VMD，请同时安装 MMD 插件，按照其 README.zh-CN.md 放置 motion.vmd 等文件；不需要 Unity 编辑器或先转换动画包。

## 动作事件

可以在现有配置中添加 events、onStart、onFinish 和 onCancel，让动作到指定位置时调用已接入的原版功能。音效、原版脚步以及可选 NPC 围观的全部用法见旁边的 [动作事件说明.zh-CN.md](../shared/动作事件说明.zh-CN.md)，音效名见 原版音效名称.txt。

Unity 动画自带的 OnPlaySe、FootL、FootR 会自动接入。出现不支持的内置事件时，可以配置 `"useClipEvents": false`，改用 JSON 事件。修改配置后重启游戏。


## 原游戏表情叠加（更多动作 0.5.0 / MMD 0.9.0）

游戏设置新增 `[更多动作] 叠加原游戏表情`，默认开启。打开游戏设置会按原规则中止当前动作，修改后下次播放生效。开启时在原游戏表情上叠加动作表情，同通道权重相加并限制在 0–100（动作当前支持眨眼与五个元音口型）；关闭时仅显示动作表情，未映射到动作的动态表情通道归零。捏脸形状保留，停止/取消后归还原游戏表情，不修改游戏存档。

每个动作 JSON 可设置 `"keepGameExpressions": false` 强制关闭叠加，此时游戏设置开启也无效。省略或为 true 时由游戏设置决定。例如：

```json
{ "keepGameExpressions": false }
```

`writeFaceMorphs: false` 仍表示动作完全不接管表情，此时两种叠加设置均不影响原游戏表情。界面开关保存于更多动作插件配置的 `[Face] KeepGameExpressions`，不重写动作 JSON。JSON 在下次启动导入动作时读取。
