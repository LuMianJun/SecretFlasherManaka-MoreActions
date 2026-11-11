# MMD 动作

简体中文 | [English](README.en.md)

与更多动作插件配套使用，将 VMD 直接放入动作目录即可在游戏角色上播放。支持 WAV／MP3 配乐、镜头、走位和基础模型标准表情，资源按目录准备即可使用。

## 快速开始

1. 按[仓库首页](../README.md#安装)从 Release 安装“更多动作 + MMD”。
2. 在 `BepInEx/plugins/SFMVmdProbe/Motions/` 下为每个动作建一个文件夹，放入 `motion.vmd`。配套镜头命名为 `camera.vmd`，配乐命名为 `BGM.wav` 或 `BGM.mp3`。需要显示歌词时，在同一文件夹放入 UTF-8 编码的 `lyrics.lrc`。
3. 重启游戏并进入场景，站立不动，按住数字键 **1–8** 打开转盘，用 **Z / X** 切页，鼠标指向动作后松开数字键播放。移动或再次选择同一动作可停止。
4. 音乐、表情和镜头在 **设置 → 更多动作** 中调整。

目录示例、转盘快捷操作和围观设置见[完整快速开始](README.zh-CN.md#快速开始)。以上为默认键位。

## 高级使用

- [高级使用](README.zh-CN.md#高级使用)：JSON 字段、默认值和完整配置示例。
- 音效、脚步及围观事件：[动作事件说明](../shared/动作事件说明.zh-CN.md)。
- 开源参考与许可：[THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md)。
