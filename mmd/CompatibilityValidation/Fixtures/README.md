# 原模型骨骼回归数据

`native-model-bind.json` 由原始解密 `models.unity3d` 的只读提取证据压缩而成，包含全部 80 条公共骨路径、两套身体网格各 78 个原始 bind 矩阵、独立提取的模型空间 bind P/Q/S，以及 Armature 和 renderer 的真实 TRS。没有包含网格顶点、贴图或模型资源。

原 Bundle 为 223101993 字节，SHA256：

```text
98621b2c4fa4f9de80f355fae5776ab17efc016bcc81c581bcd0917af4308367
```

原证据位于工作区 `outputs/mmd-control-space-fix-20261115/native-bundle-binds.json` 和同目录 `native-bundle-transforms.json`。压缩文件另记录这两份证据的 SHA256。保留证据时可复算：

```powershell
python mmd/CompatibilityValidation/Fixtures/export-native-bundle.py D:/Reverse/Seleka2/UnityProject/outputs/mmd-control-space-fix-20261115
```

矩阵按 Unity 行列顺序展开，四元数为 XYZW，位置单位米；世界数据都相对模型根，不含 prefab 的 1.2 模型比例。原 prefab 的当前动画姿态与 bind 不同，因此不将它当作零动作参考。

`BreastRotateL`、`BreastRotateR` 是原 prefab 中存在、但不占 mesh bind 槽位的两个辅助父节点。live 树和采样树都按其真实 local identity 保留这两层，共80个节点；其它78个 bind 关节逐项验证模型空间 P/Q。可选轨道还覆盖这两个辅助父的独立写入及其子骨在非单位原生父TRS下的换算。

原始 bind 的 scale 接近 1，双精度最大分量误差约 9.0452e-7；Body.001 的 renderer TRS 为 P≈0、Q≈Rx(-90°)、S≈1。验证直接输入原始矩阵，以独立记录的 bind P/Q 作预期，避免从预期姿态反算 bind 矩阵后再证明自身。
