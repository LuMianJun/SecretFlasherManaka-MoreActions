# 游戏骨架写回的两阶段对照

此离线项目直接编译固定版本的原始 UnityVMDPlayer、Reader 和 Quaternion 源码，同时链接生产 `RuntimeVmdPose`、参考骨架、姿势租约和附加轨道层。启动时核对原始源码 SHA256。动作文件保留在用户本地，不加入仓库。

先关闭附加轨道的执行，量化完整游戏输出相对原版的差异。随后可恢复附加轨道，观察其作用。1.5.0 的游戏控制层和足部采样修正可能超过固定上游阈值，`baseline` 的超限仍按原有规则报告。发行验收分别使用 UpstreamValidation 和 CompatibilityValidation。

```powershell
dotnet run --project mmd/PlaybackParityValidation -c Release -- baseline '<Motions目录>' '<baseline.json>'
dotnet run --project mmd/PlaybackParityValidation -c Release -- extended '<Motions目录>' '<extended.json>'
```

`baseline` 保留原始 Reader 能识别的全部原始 key，避免眼或 twist 轨道改变 `FrameCount` 后误比不同生命周期；只限制游戏附加层使用的查找字典。原版和生产分别从同一原生 bind fixture 建立参考骨架，使用相同的名称兼容输入、A/T 选择和单精度秒数。每三帧采整数与 `n+0.375`，包括支持区间的末端。更密集的整数／分数帧、三种缩放对照由 `UpstreamValidation` 执行。

对最终写回的 80 根骨骼比较位置和世界旋转。角色朝向被还原，游戏控制器接管的水平位移被补回；仅加入第一帧控制器起点的固定差值，没有逐帧、逐骨校准，也没有贴地处理。位置阈值为 0.1 mm，旋转阈值为 0.05°；超限退出码为 1。

`extended` 恢复原动作全部附加轨道。它仍与同一原版基准比较，逐骨记录位置／旋转最大变化、对应帧和超阈值样点数量。有效附加控制本就可能改变标准骨骼，所以此阶段标为 `MEASURED`，JSON 中 `parityPassed` 为 `null`，不会把非零差异自动宣称为正确或上游一致。附加功能的预期作用、单位轨道无影响和原生姿势恢复由 `CompatibilityValidation` 的独立契约验证。

还可逐组测量附加层，把完整播放中的变化归因到具体输入组：

```powershell
dotnet run --project mmd/PlaybackParityValidation -c Release -- groups '<Motions目录>' '<extension-groups.json>'
```

分组包括上半身3、腰／取消、肩P/C、腕／手捩、親指０及配套1/2、D/EX、眼和原生名称轨道。仅运行存在有效控制的组；有效性采用生产附加层相同的精确中立判定，不用近似动画幅度阈值。每组保留完整标准 Reader 输入和开关记录，只向查找字典加入该组轨道；未选原生轨道的未知 Raw 名称不加入，避免别名绑定重新激活其它组。工具检查各组 Reader 末帧与原版一致。

每个动作、每种参考姿势只缓存一次原版与完整附加播放输出，再逐组比较到这两个基准；时间、控制器位移和首帧起点规则与前两种模式相同。输出包含每骨位置／旋转的最大变化及其帧、活跃轨道名单、未分组的活跃轨道，以及相对完整附加输出的差值。Shake、盛装登场和腰振优先运行；每个动作／参考组合完成后写入 JSON，最终以 `comparisonCompleted=true` 标记全部完成。分组数据同样标为测量，不自动判为正确。

这些结果使用离线 Unity 数学与场景边界，不包含游戏渲染、蒙皮表面或墙／地碰撞，也不证明作者原配 PMX 的画面。
