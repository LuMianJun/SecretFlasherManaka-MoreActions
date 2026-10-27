# 骨骼插值布局验证

运行：

```powershell
dotnet run --project vmd-validation/InterpolationValidation -c Release
```

测试链接生产 `RuntimeMmdMotion.cs` 与离线 Unity 数学边界，不包含游戏程序集或配布素材。

布局对齐 UnityVMDPlayer 固定版本 `2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8` 的 [UnityVMDReader.cs](https://github.com/hobosore/UnityVMDPlayer/blob/2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8/UnityVMDReader.cs)：X、Y、Z、旋转分别从 64 字节中的 0、16、32、48 段读取控制点，每段使用偏移 0、4、8、12。

使用具有闭式反函数的四组独立曲线核对数值；故意让首段的冗余数据与其他通道段不一致，并改动未使用字节。另生成两关键帧的最小 VMD，经过实际读取器与 Sample 验证 XYZ、旋转、目标键插值和边界行为。贝塞尔求值精度、Slerp、首末帧和缺失轨道策略保持现有语义。

这些离线检查证明控制点选择与采样行为，不证明完整 UnityVMDPlayer、原作者模型或游戏画面逐帧等价。
