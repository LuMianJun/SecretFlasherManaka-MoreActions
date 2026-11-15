# 上游输出差分验证

此项目将固定的 `hobosore/UnityVMDPlayer@2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8` 原始源码与未注入游戏控制层的求解路径编译进同一个离线程序，比较同一骨架、输入和调用顺序下的输出。启动时校验 `reference` 内三份原始源码的 SHA256，不修改参考实现，也不使用单独的旧求解开关。

```powershell
dotnet run --project mmd/UpstreamValidation/UpstreamValidation.csproj -c Release
```

默认运行合成动作对照，包含骨骼局部轴、稀疏／重复 key、零 key、上下半身、全亲、头颈父子、脚与脚尖 IK 开关，以及前后跳转的相同查询历史。位置和旋转均比较局部与世界坐标；这些断言要求继承上游行为，不把另写的物理算法作为期望值。

传入真实动作目录，可同时验证原始输入与名称兼容后的输入：

```powershell
dotnet run --project mmd/UpstreamValidation/UpstreamValidation.csproj -c Release -- --motions "游戏目录/BepInEx/plugins/SFMVmdProbe/Motions"
```

原生骨架的完整动作门禁使用 `CompatibilityValidation/Fixtures/native-model-bind.json` 的 80 根参考骨骼，在 A／T 参考、0.9／1.125／1.2 缩放系数下逐帧比较。每帧另采样 `n+0.375`、`n+0.75`，并检查原版终帧保持。输出 JSON 包含源码与动作哈希、有效输入哈希、样本数、容差和最差位置／旋转点：

```powershell
dotnet run --project mmd/UpstreamValidation/UpstreamValidation.csproj -c Release -- --native-motions "游戏目录/BepInEx/plugins/SFMVmdProbe/Motions" --summary "输出目录/native-parity-summary.json" --native-only
```

原生门禁的生产输入使用 `UpstreamInputCompatibility.Apply`。原版独立读取按固定字段长度重写名称后的 VMD，从而获得相同的有效标准轨道。例如 addiction 的原始名称不被固定上游识别；直接把两边都不播放当成通过，不能证明动作求解一致。原始输入的行为另由 `--motions` 覆盖。

原版 `Play` 固定请求 A 姿势。T 对照只将离线边界的 `Animator.isHuman` 设为 false，跳过原版已有条件中的额外 30° 手臂旋转；原版恢复参考姿势和求解代码仍保持不变。当前固定源码只有该条件读取 `isHuman`。

门禁要求世界／局部位置误差小于 0.1 mm、旋转误差小于 0.05°。所有祖先和骨骼局部 TRS 逐分量完全一致的样点，其世界输出亦完全一致；出现任意局部分量差异时，再逐骨计算世界位置与旋转误差。比较包含控制根与 Armature，另行统计 80 根骨骼。

本项目验证固定上游求解路径。独立控制层、足部采样修正、附加轨道及游戏写回另在 CompatibilityValidation 中验证；启用附加轨道后的预期变化不能被算作标准求解已对齐的证据。离线数学边界不模拟渲染、蒙皮表面或场景碰撞。
