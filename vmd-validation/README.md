# MMD 0.9.7

安装、导入与常用设置见 [仓库首页](../README.md)，完整参数见 [中文使用说明](README.zh-CN.md)。

- Plugin：运行时 VMD 求解、配套镜头、后台准备和骨架缓存。
- ImportValidation：配置导入、默认值与资源路径验证。
- SkeletonValidation：绑定姿势、根位移、表情及缓存验证。
- CameraValidation：镜头采样、坐标与安装接口验证。
- docs：实现说明及历史演进，各文档标注的版本用于区分当时与当前行为。

公开仓库不附带本地测试动作、配套模型、音乐或 Unity 转换工程。无需安装 Unity 编辑器或转换动画包即可使用 VMD。

实现资料：[生成骨架](docs/游戏骨架默认路径.md)、[异步准备](docs/异步加载与骨架复用.md)、[重绑定](docs/重绑定与分帧重建.md)、[走位控制器](docs/根位移与角色控制器.md)。开源参考及许可见 [THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md)。
