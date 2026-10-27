# 更多动作 0.5.0

玩家用法见 [中文使用说明](README.zh-CN.md)，安装与插件组合见 [仓库首页](../README.md)。

Plugin 为生产源码。AssetValidation、EventValidation、RingValidation、RuntimeValidation 分别检查资源准备、事件/驱动、转盘与注册策略。验证项目链接生产逻辑并模拟外部游戏边界。

MMD 通过 IActionSource / IActionPose 注册运行时动作，可选 IActionRootMotionPose 交付水平走位，IActionFacePose 在原版更新前撤回临时表情层。普通动画包仍使用已适配游戏骨架的 Unity Generic 动画。

扩展动作事件可通过 ActionEventRegistry.Register 注册校验与执行处理器，返回 IDisposable 清理临时效果。用法见 [动作事件说明](../shared/动作事件说明.zh-CN.md)。
