# 生成手脚接地 IK 计划

普通安装包保留接地 IK 代码。带本地计划的构建才显示 **手脚接地IK（试验性）**，首次使用默认关闭。计划按主动作 VMD 的 SHA-256 匹配，重新导出动作后需要重生成计划。

在仓库根目录运行下面的命令。准备 Python 3.11 或更新版本、UnityPy 1.25.4、.NET 10 SDK，以及本地解密的原版 `models.unity3d`。提取脚本会核对 Bundle 哈希和绑定矩阵。当前索引对应原版 `Selestia_body`，换了网格的角色需要对应的绑定数据及接触顶点索引。

```powershell
python -m pip install UnityPy==1.25.4
python mmd/ContactPlanGeneration/extract-skin.py "本地/models.unity3d" "outputs/contact-skin.json"
dotnet run --project mmd/ContactPlanGeneration -c Release -- "本地/Motions" "outputs/contact-skin.json" "outputs/contact-plans"
./build-mods.ps1 -GameDir "游戏目录" -OutputDir "outputs/ground-contact-build" -MmdGroundContactPlans "outputs/contact-plans"
```

生成器也接受单个身体 VMD 文件。传入目录时递归读取 `motion.vmd`。当前采样固定使用原版绑定、A 参考姿势、1.125 角色尺度和离线平地。用来生成计划的手脚关键点来自原版蒙皮；公开仓库只保留接触顶点索引和绑定回归数据。原始 Bundle、提取后的蒙皮、VMD 和派生计划由使用者在本地准备。

运行时只在完整身体播放时使用计划。角色控制器完成本帧走位后，插件按实机地面高度解算预计承重的手脚，保留端点的原有朝向。接地计算可能改变手臂或腿的姿势；当地面不可用、计划无法匹配或试验层出错时，动作仍可继续播放。
