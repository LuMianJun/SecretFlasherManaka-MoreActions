# 离线验证

模块目录、状态归属和更新顺序见[代码结构](ARCHITECTURE.md)。

验证项目使用 .NET 10，生产插件目标为 .NET 6。请使用 .NET 10 SDK，并准备目标游戏已生成的 `BepInEx/core` 和 `BepInEx/interop`。游戏及 Unity 程序集由使用者本地提供，不包含在仓库中。

## 从源码构建

在仓库根目录运行，将路径替换为自己的游戏目录：

```powershell
$gameDir = "D:/Games/SecretFlasherManaka"
dotnet build mmd/Plugin/VmdProbe.csproj -c Release "-p:GameDir=$gameDir"
dotnet build npc-spectators/Plugin/SFM.NpcSpectators.csproj -c Release "-p:GameDir=$gameDir"
```

第一条命令会同时构建更多动作与 MMD。仅构建更多动作时，使用 `more-actions/Plugin/MoreActions.csproj`。DLL 输出到各插件项目的 `bin/Release/net6.0`。

## 运行离线验证

在仓库根目录执行：

```powershell
dotnet run --project more-actions/AssetValidation -c Release
dotnet run --project more-actions/AnimatorValidation -c Release
dotnet run --project more-actions/BonePresetValidation -c Release
dotnet run --project more-actions/CameraTrackValidation -c Release
dotnet run --project more-actions/ReplacementBrowserValidation -c Release
dotnet run --project more-actions/FaceRestoreValidation -c Release
dotnet run --project more-actions/EventValidation -c Release
dotnet run --project more-actions/RingValidation -c Release
dotnet run --project more-actions/RuntimeValidation -c Release
dotnet run --project more-actions/StatsValidation -c Release
dotnet run --project mmd/ImportValidation -c Release
dotnet run --project mmd/UpstreamValidation -c Release
dotnet run --project mmd/CompatibilityValidation -c Release
dotnet run --project mmd/CameraValidation -c Release
dotnet run --project npc-spectators/Validation -c Release
```

测试自行生成临时样例。真实动作对照使用 `mmd/UpstreamValidation -c Release -- --motions "动作目录"`；给 ImportValidation 传入自备 WAVEFILE/Shake 示例的父目录。素材均不随仓库提供。

ImportValidation 覆盖顶层文件字段、旧 `camera.vmdFile` 兼容、冲突优先级和路径限制。UpstreamValidation 直接编译未修改的固定上游 Player/Reader，与未注入游戏控制层的路径比较。PlaybackParityValidation 测量游戏写回对固定上游的差异，CompatibilityValidation 检查扩展契约、游戏绑定、尺寸换算、走位、遮罩、缓存和原生恢复。范围及命令见各验证目录 README。

相应入口还提供针对已安装游戏的接口检查。离线替身不能证明实际渲染、导航、碰撞或原生 Harmony 派发，仍需游戏内验收。

### 2026-11-13 分离文件实测

用户在游戏中测试同一支《ヴァンパイア》动作的完整表情、仅口型、仅表情、仅动作四种配置，反馈四组视觉表现正常。测试使用原作者分别提供的身体、表情和口型 VMD，不合并文件；每组关闭原游戏表情叠加，未附音乐和镜头。第三方测试素材不随仓库或安装包分发。

核对该次 `BepInEx/LogOutput.log` 快照：四组均成功准备，时长均为178.5秒，存在多次开始、切换、移动停止及返回标题时停止记录；没有 Error/Fatal，也没有附加表情或口型加载拒绝。快照含555行，SHA256 为 `be20fec80b0f139ccbd03d2f56932fe21405144de9e38d6aecbb28a8cad3488f`。七条警告涉及 Il2CppInterop 初始化/参数注册、缺少 CompassView 和 blankmap 的 mission.bundle。本次反馈确认四组样例的观察结果，不扩大为所有动作或全部178.5秒播放完毕的验证。

更多动作1.6.2同步纳入此前已安装的膝盖偏移补丁移除：不再挂载 `PlayerKneeAdjuster.get_KneeOffsetY`，保留原生膝盖、脚尖和视线更新边界。配套验证不再要求该补丁存在。候选三个 DLL 共49条 Harmony 声明、43个原生目标，按匹配游戏二进制、元数据及完整 Cpp2IL 输出复核，43个入口均为单一方法。

## MMD 骨骼、求解与离线回归

当前构建已移除 MMD 运行时的腿部、姿势、高度和蒙皮测量，包括旧采样配置、诊断查询、辅助 FK 副本与网格烘焙。试验性接地仅调用支撑地面射线。旧配置中的测量开关不再生效。骨骼路径和映射使用公共固定角色预设，源码仅保留 `shared/body-bones.json`，随包复制到 `BepInEx/plugins/SFMMoreActions/body-bones.json`。生产读取这份清单，不扫描或保存运行时骨骼目录；网格索引 JSON 已移除。

生产仍保留动作加载、未匹配轨道、取消原因及异常日志。

### v1.9.0 本机验证

用户实测来回切换 `ganimatawalkwide`，日志记录 19 次应用替换，镜头挂点每次都回到相同高度。完整蹲起后恢复到蹲前镜头高度。正式版移除了临时镜头状态日志。

同一批 11 支 PCM16 WAV 在游戏启动时均走 Unity 原生 WAV 加载，未触发 PCM 回退。日志记录的最长配乐就绪等待从 38.238 秒降到 1.102 秒，包含排队。最长主线程音频加载步骤从 222 ms 降到 53.9 ms。离线对照确认采样数、采样率、声道数和逐采样数据一致。

### v1.8.1 本机试播记录

实机日志确认人设404 和 Blue Planet 使用含足 IK 父层级修复的 DLL 启动播放，左右足 IK 父均已匹配。日志未采集逐帧的脚尖目标及实际落点。按原版角色参考尺寸复算，Blue Planet 约 11 秒、人设404 约 13.6 秒仍有足目标超出腿可达范围。

### v1.8.0 播放回归

MMD 1.5.0 的上游对照覆盖 398,481 项原始与兼容输入检查。游戏兼容回归另检查六支动作的 A／T 播放、控制层级、足部零位移、IK 切换、原生所有权及停止恢复，共 35,782 项。原版跟随镜头的同帧走位由 EventValidation 覆盖。

手脚接地原型以六支动作共 21,169 帧验证同一判定规则。盛装坐下段足底穿地超过 1 cm 的帧从 555 降至 39，腰振中段手部从 417 降至 8。原版角色的实机试验分别记录到盛装 484 帧、腰振完整播放 323 帧发生修正，这些是整段计数，无法定位到具体手脚或单帧。安装后的试验设置已恢复关闭。

公开包省略本地计划。生成及构建命令见[接触计划生成](mmd/ContactPlanGeneration/README.md)。整包检查接受接地用的六参数射线查询，继续检查已退役的测量类型与 `BakeMesh`，并拒绝此游戏中不可调用的 `LayerMask.GetMask`。

### 2026-11-15 上游对齐的历史验收（MMD 1.4.14 候选）

标准身体求解、Reader 筛选和游标、全亲与上下半身补偿、Ghost 顺序、足／脚尖 IK、IK 开关及 n/n+1 插值恢复固定 UnityVMDPlayer 的原有行为。生产已删除替代求解分支，验证直接调用默认构造，不再定义 `UPSTREAM_VALIDATION` 或以旧模式通过代替生产输出一致。

验证分两步执行。先去除附加轨道，比较标准求解核心及 RuntimeVmdPose 完整写回；基准通过后再恢复附加轨道，与同一上游输出逐骨比较，记录其位置和旋转变化，并用独立扩展契约检查这些变化。附加阶段出现非零差值不能直接当成通过，也不能与标准阶段的误差混成一个数字。

标准核心直接使用未修改的固定上游源码。六支动作、A／T 参考、0.9／1.125／1.2 缩放，共36组合、380,970次采样、30,477,600个骨骼样点；局部／世界位置与旋转的最大差均为0。每个整数帧另采样 n+0.375 与 n+0.75，并检查终帧保持。另有64组合、2,048次合成查询，覆盖大角度全亲、复合控制旋转、零／稀疏／重复 key、下半身、头颈、IK开关及相同的前后查询历史。合成加真实原始／兼容输入的最终门禁通过398,481项检查。

无附加层的完整写回使用原生80骨、A／T和1.125缩放，六支动作共12组合；在还原角色朝向及控制器水平位移后，最大位置差约0.002331 mm、旋转差约0.00004608°。只校准固定首帧水平起点，没有逐帧、逐骨或贴地校准。标准门禁容差为位置0.1 mm、旋转0.05°；完整回执记录每支动作和骨点的极值。

恢复全部附加轨道后，另完成12组合、28,232次查询、2,258,560个骨骼样点的测量。相对原版标准输出的最大位置变化依动作约7.87～660.30 mm，最大旋转变化约13.46～180°，此阶段只记录变化，不标为上游一致或视觉修复通过。Shake的髋、腿和脚仍在标准门限内；盛装登场的足首D、腰振的腰与足先EX等确有非单位输入，必须分组定位其影响，不能把全部差异归给源动作或视为正常。

同时修复全单位附加轨道仍注册前置回调、改变标准有效帧数和无效回写的问题。20组A／T、IK开关和附加组组合对照中，80骨位置及四元数分量差精确为0；微小但非单位的输入仍保留。CompatibilityValidation通过35,647项检查，其中六动作12条路径和5,600次同历史查询重放通过。最终Release构建零警告、零错误；NativePatchGuard扫描49条Harmony声明、43个独有原生入口，完整同址方法集合与入口字节和已审基线一致。

附加层分组测量另完成50个有效组合：腰振182.8秒的左脚约179.449°差异可由腰／取消组单独重现；盛装71.2秒的右脚趾约70.46 mm变化来自D/EX组。Shake各分组腿足均在标准门限内。腕／手捩的独立轴和支点预测未发现重复施加，但没有原配PMX时不能据此认证源局部轴或grant语义。

按用户后续指令，已从隔离源码快照构建并安装1.4.14无附加身体轨道测试版，启动标识为 `bodyExtensions=false build=core-only-test`；正式源码仍保留完整扩展。测试版使用完整六动作再跑12组合，80骨最大位置差0.002331 mm、旋转差0.00004608°；另通过原生入口复核、55项镜头和包接口检查。该记录对应当时的无附加层安装，之后已换为完整扩展测试版。

addiction 的原始骨名带 NUL 后填充，固定上游无法识别。有效输入对照由原版独立读取名称兼容后的临时二进制，生产侧对原文件执行实际兼容；该动作有39,222个支持 key、末帧5,997，不能用双方空轨静止作为验收。A／T在相同参考几何下对照，原生身形测量与最终游戏写回另行验证。

```powershell
dotnet run --project mmd/UpstreamValidation -c Release -- --native-motions "动作目录" --summary "输出目录/native-parity-summary.json" --native-only

dotnet run --project mmd/PlaybackParityValidation -c Release -- baseline "动作目录" "输出目录/playback-baseline.json"

dotnet run --project mmd/PlaybackParityValidation -c Release -- extended "动作目录" "输出目录/playback-extended.json"
```

这些结果验证同一游戏参考骨架和同一查询历史下的输出一致性，不证明原作者 PMX 姿势、网格接触或游戏视觉效果。候选保留上游已有的目标残差和状态历史；坐下穿地是否改善仍需实机复测。这段记录的是该候选当时的离线验证。实现范围见[功能对照](mmd/docs/UnityVMDPlayer功能对照.md)。

### 2026-11-15 求解改写的历史验收（MMD 1.4.13，已被取代）

1.4.13 曾修改零位置 key、Reader 游标、最新 IK 状态、控制空间、下半身补偿、Ghost 父先子后写回及端点采样，并用真实两段向量重写腿 IK。独立反例中的约2～9.7mm可达目标残差降至数值精度内，但该验收未覆盖完整膝弯曲约束，也没有证明生产输出与上游一致。

随后对坐下段的直接原版对照确认，新 IK 能命中踝目标，却可能偏离参考膝弯曲轴并产生额外膝姿势异常。这批标准算法改写已在1.4.14候选中撤回；原版差分取代了“优于上游”的几何和任意 seek 确定性断言。原生颈长、肩宽、腿间距等参考几何、缓存及原生姿势交接仍属于游戏适配范围。

### 2026-11-15 上游一致求解测试版（MMD 1.4.11）

身体求解改为固定 UnityVMDPlayer `2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8` 的方法移植。旧 MmdSkeleton/GameMmdSkeleton、临时足部诊断及其专属验证工程已从活动源码移除；下面的旧版本记录保留为历史证据，不代表本版求解行为或验收结果。

新的对照先核验原始上游源码哈希，分别比较原始 VMD 和经过明确输入兼容的 VMD；兼容组把清理后的名字写入临时二进制供原版读取，生产侧则读取原文件并执行实际输入适配。addiction 原始骨名带 NUL 后填充，原版不能识别；必须检查兼容后的非空轨道及实际舞蹈输出，不能以空轨静止作为通过依据。详见 [对照方法](mmd/UpstreamValidation/README.md) 与 [当前差异](mmd/docs/UnityVMDPlayer功能对照.md)。

游戏适配回归使用真实生产 Rig/Pose/Cache 和原生姿势租约，检查原生骨轴、已支持体型参数、髋高、循环根位移、扩展隔离、停止恢复、分区遮罩、缓存复用及活动体型失效。离线 Unity 替身不能代替游戏内视觉验收。

该轮记录246,553项原版源码对照检查（32组/1,024次夹具查询、六动作原始与兼容两组/1,812次真实查询），位置与旋转差为0；这些检查调用当时的旧模式，不能证明后续生产修正路径也与原版一致。另通过4,905项游戏适配、83项导入和55项镜头检查。候选程序集的原生接口、后台线程边界及旧求解器/测量代码移除检查通过。生产构建零警告、零错误；三个候选DLL共49条Harmony声明、43个原生目标，完整入口字节与同址方法集合均与已审基线相同，43个目标均为单一方法。该轮未进行游戏内视觉验收。

### 2026-11-15 控制空间与附加轨道修复（MMD 1.4.12）

隐藏根和Armature控制空间归一化，保留原生世界bind轴与已换算尺寸，修复Center重置局部旋转造成的90度错位及局部位移再次乘根缩放。采样树覆盖80条公共路径；眼、捩骨、腰／取消、肩P/C、上半身3、D/EX、拇指0和原名可选骨按实际轨道与遮罩启用。腰等虚拟控制进入原IK之前，D/EX在原IK之后叠加，不恢复旧自写腿部求解器。

旧模式的标准上游对照246,553项通过，输出差为0。游戏接入和附加功能24,108项通过，六动作A/T共2,674个真实时间样点，包含Shake全片、盛装登场60～90秒和腰振末段的密集采样。逐点重复、有限值、原生root／Armature所有权和停止80骨局部TRS恢复通过；该历史版本另记录纯核心已有的头部／后代非单调查询历史依赖，未将任意seek确定性作为通过条件。1.4.13曾调整Ghost写回顺序处理该现象；该改写已撤回，当前按相同查询历史直接与原版比较。

Release构建0警告、0错误；最终三DLL仍为49条Harmony声明、43个单一原生入口，与已审基线的入口字节及同址方法集合一致。本候选及六动作完整资源已安装，实际文件哈希与候选一致，原配置及更多动作／NPC／骨骼JSON依赖保持原内容。当前检查未代替游戏内蒙皮和视觉验收；原Avatar描述与bind的拇指参考差异，以及颈长、肩宽、腿间距等位置输入缺口仍见[功能对照](mmd/docs/UnityVMDPlayer功能对照.md)。

### 2026-11-13 腿部求解回归

MMD 1.4.1 将腿部受限 CCD 改为解析铰链求解，保留关节限位、足与脚尖的独立开关及原动作髋部轨迹。SkeletonValidation 新增近平直、深折叠、零距离、反向目标、非平面骨段、不可达目标最近位置、重复与随机采样，以及自定义大腿/小腿长度的实际重定向检查。

以本地六支 VMD（addiction、Shake、盛装登场、腰振りダンス、越南鼓甜舞、queencard）逐帧运行，共通过 88,805 项断言。历史运行骨架夹具下，可达脚部目标的最大误差低于 0.1mm；相对于最近可达位置的额外误差低于 0.001mm。不可达目标允许保留原目标残差，不拉长腿或额外移动髋部。该结果为离线数学和重定向验证，不表示已完成修复版的游戏内视觉验收。

MMD 1.4.2 进一步移除最短弧旋转后的强制膝盖平面扭转。该扭转会在脚踝到位的同时改变弯膝方向；脚部位置误差小不能证明姿势正确。新增已知整腿旋转下的膝盖位置回归，修改前可复现失败、修改后通过；反向目标改为检查有限值、脚部到位及随机跳转后的确定性，不再强制所有目标维持原铰轴投影。

六支动作的新一轮回归通过 88,913 项断言，包括盛装登场35.2/45.7秒、腰振りダンス181.6/181.7秒的方向检查。对照使用到位误差小于0.1mm的充分迭代 CCD，仅作为方向诊断：盛装两帧平面夹角由约179°降至约30°/33°；腰振181.7秒由约71°降至约17°。该对照不是原作者模型的真实姿势，最终视觉表现仍需实机验收。

历史 MMD 1.4.3 测试版增加过只读姿势与场景诊断，播放沿用1.4.2的求解规则。相关运行时测量及专属测试现已移除；既有日志仍可用保留的离线工具重放。

MMD 1.4.4 补充眼球和脚趾、IK 后 D 链继承、腰／肩取消旋转、原生手臂扭转骨及两关节拇指适配。躯干映射同时补偿被压缩或分支不同的关节位置，普通刚性骨段的补偿为零，原生外观基线仍保留。附加骨仅在存在对应轨道时接管；手臂扭转骨随被控制的对应手臂接管。该阶段曾使用运行时发现并持久化的身体骨骼目录，现已改为随包提供的公共固定清单。

整链回归使用六支真实动作，每0.5秒检查全身主要骨骼和扭转骨的位置；另覆盖位置与旋转组合、足首 D 与脚趾 IK 顺序、普通和三关节拇指、眼球组合轨道、原生骨骼名、歧义别名、停止恢复、重复应用以及无蒙皮的胸部控制父级。记录中的原有膝盖方向夹具沿用生成它们时的无腰取消输入，新拓扑另做整链与目标误差回归；不会用旧输入的 CCD 方向判定新输入的作者意图。

该历史轮次通过244,692项断言，其中包含当时的骨骼清单写入与复用测试；清单持久化测试已随实现移除。A/T两种参考姿势的整链位置最大偏差约0.0017mm，此数值仅衡量生产源骨架到离线目标骨架的换算一致性，不代表原作者姿势、网格变形或场景碰撞已经验证。

MMD 1.4.5 修正后台轨道检查通过辅助函数调用 Unity `Vector3.sqrMagnitude` 的问题，改为直接读取结构体字段并用托管数值计算。1.4.4 启动白屏现场的 GC 日志记录 `Collecting from unknown thread`，Windows 记录 GameAssembly.dll 的0x80000003异常。离线线程检查现递归跟踪同程序集辅助方法和委托目标，能拒绝旧DLL中的 `RuntimeVmdSource → MmdBoneMap.Animated → 谓词 → Vector3.get_sqrMagnitude` 调用链，修复后的候选DLL通过。该检查覆盖可静态解析的调用，不宣称可以验证任意反射或跨程序集回调。

### 2026-11-14 历史测量记录与运行时清理

以下是已撤除测量实现的历史证据，不是当前插件可启用的功能。1.4.6记录原生LateUpdate与MMD写入的同帧位置、高度及周围碰撞面；1.4.7尝试通过离线顶点索引表测量CPU蒙皮。实机因Avatar与Player骨骼路径不同而未匹配身体网格，没有取得皮肤最低点。此前候选补充过原始renderer的有序骨骼路径变体，但未完成实机测量验证。现在运行时测量、索引表、导出器及其专属回归已一并移除，历史日志和报告保留。

Shake（游戏显示名 MMDExample）的既有离线分析显示：约11.59秒右脚的绑定上方向在脚尖IK前朝上，之后接近侧向并略向下；源骨架到游戏脚部的旋转误差极小，D轨道未叠加额外旋转，受测后续阶段未观察到原生改写。独立CCD在该侧腿与脚尖目标均收敛时仍复现侧翻。位置可达及重定向一致性检查不能覆盖绕脚踝至脚趾方向的滚转。绑定上方向是骨架代理量，不能当作脚底网格法线，FK也不能替代原作者模型最终姿势。

本日采集的历史游戏日志加载MMD 1.4.7，覆盖腰振0.049～189.937秒、盛装登场0.049～103.303秒。盛装85.404秒的严格同帧记录中，右足IK目标已低于实际道路表面约54.171毫米，实际脚踝与脚趾分别低约54.170毫米、58.763毫米；控制链准确跟随，右D／EX额外旋转为零。89.905秒右大腿至膝盖的骨段也直接穿过道路表面。腰振87.40933秒右腕低约98.855毫米，适配FK与最终腕骨一致。受测记录未发现超过诊断门限的后写，不能把这些现象全部归因于迭代未收敛或游戏IK抢写。

上述记录确认当前角色适配后的目标／姿势存在接触偏差，不能证明原作者模型的动作本来穿地；脚目标到位也不验证完整脚部朝向或弯膝平面。日志没有成功匹配身体网格，骨点／骨段相对碰撞面的距离均不作为皮肤穿透深度。日志快照SHA256为`275DEFD858EF2BF21087058AF2C540AE14168B6E1EB872EA52B22493B1A81AFE`。

2026-11-14 清理前的公共预设回归通过244,744项SkeletonValidation断言，包含80条预设与原版离线骨骼清单一致性检查；RuntimeValidation、FaceRestoreValidation、EventValidation、ImportValidation分别通过116、45、189、83项断言，InterpolationValidation此前通过63项。该轮覆盖统一预设校验、49条核心骨模型、可选骨显式驱动、默认原生控制保留、缺骨拒绝及停止恢复。生产编译零警告、零错误；该批候选DLL共49条Harmony声明、43个原生目标，均为单一所有者。这些数字对应清理前候选，不作为清理后构建已完成验收的证据，也不表示坐下穿地或完整脚部朝向已通过实机验收。

### 2026-11-14 运行时清理后的回归（已安装测试版）

该轮运行时清理测试版已安装到游戏。六支动作的 SkeletonValidation 通过245,152项断言。新增检查覆盖数值采样与原接口的一致性、准备后的全骨姿势对照、超过缓存容量后反复切换、随机跳转、IK开关、拇指与D链，以及可变输入修改后重新采样。固定Mesh下可选骨新增、消失及重排会更新绑定缓存；转盘显隐、别名及同参考姿势的新动作不重采角色快照，真实体型、Mesh或角色变化仍重新校验。

FaceRestoreValidation通过78项断言，包含同角色采样树复用、当前姿势及尺寸刷新、并发租用隔离、拓扑／Avatar失效、目标销毁、构造与Graph清理失败淘汰代理，以及嵌套修正和旧token重复释放。AnimatorValidation、RuntimeValidation、EventValidation、ImportValidation、InterpolationValidation分别通过964、116、189、83、63项断言。

在.NET 10离线边界内，预热后的数值骨骼采样连续50,000次分配0字节；该结果只衡量该采样函数，不表示游戏播放全路径零分配或已测得帧率提升。原生修正仍每次使用独立的轻量释放token。Unity采样树和原生PlayableGraph的最终渲染表现仍需游戏内复测。

该轮三个插件生产构建零警告、零错误；无素材候选包通过55项CameraValidation断言及原生接口、后台Unity调用边界检查，确认不含已撤除的测量类型、BakeMesh调用或MMD场景物理依赖。候选DLL扫描出49条Harmony声明、43个原生目标，43条完整入口与已审基线一致，均为单一方法。

安装前检查候选包时，可同时提供游戏目录（读取interop接口）和候选包目录：

```powershell
dotnet run --project mmd/CameraValidation -c Release -- --package "游戏目录" "候选包目录"
```

### 2026-11-14 公共骨骼 JSON

公共清单仅在源码 `shared/body-bones.json` 维护一份，构建和离线验证均使用这份数据，随包文件为 `BepInEx/plugins/SFMMoreActions/body-bones.json`。MotionRig 首次使用时通过线程安全的 Lazy 读取并缓存，Plugin.Load 早期主动校验，文件缺失或损坏时报告路径和原因。MMD 源名与缺骨回退来源也从 JSON 读取，不另存硬编码映射清单。该文件不由游戏运行时扫描生成，也不保存当前角色的姿态、体型或绑定快照。

`MotionRig.BonePaths()` 提供清单中的80条已知身体骨骼路径，Unity动作和MMD统一按该预设校验，不增加提供者专用接口。`RequiredBonePaths()` 读取其中原49条必要路径，并继续作为Unity动作的默认接管集，避免未动画的次级骨骼被冻结。Unity动作可通过显式 `bonePaths` 选择附加骨。MMD当前绑定其中78条，`BreastRotateL/R`两条原生乳部控制父级保持游戏控制，不直接驱动；预设完整不等于所有路径均由MMD接管。MMD支持的附加骨仍以有对应轨道为接管条件，手臂扭转骨随对应手臂接管。显式路径均受动作范围、重复及实际目标存在检查约束；既有动作JSON字段继续保留。

本轮 JSON 候选已按用户指令安装到游戏，安装后的DLL、公共JSON及随包文件与已审候选清单哈希一致。当前候选的 BonePresetValidation 通过374项断言，覆盖并发首次加载、80／49条路径、四条缺扭转骨回退、格式／重复／父链／依赖校验、缺文件报错路径及加载后缓存。六支动作的 SkeletonValidation 通过245,165项断言；RuntimeValidation、FaceRestoreValidation、EventValidation、ImportValidation、InterpolationValidation分别通过116、78、189、83、63项。

当前三个插件生产构建零警告、零错误，候选包通过55项CameraValidation断言及公共JSON位置／内容、原生接口和后台Unity调用边界检查。随包只保留更多动作目录中的一份公共JSON；候选DLL仍为49条Harmony声明、43个目标，43条完整入口与此前基线一致，均为单一方法。

该历史候选的 `PoseTraceReplay` 曾用于重放旧日志和对照 CCD；它随旧求解器专属验证一起归档，不再是当前工程入口。当前求解应使用上游源码对照及游戏兼容回归。

## 生成无素材安装包

```powershell
./build-mods.ps1 -GameDir "D:/Games/SecretFlasherManaka" -OutputDir "./releases/local-build"
```

只收集三个插件自身的 DLL、公共 `body-bones.json`、玩家说明、动作事件文档和第三方说明。不会复制游戏程序集、个人配置或动作/模型/音乐。


无素材安装包的接口检查可显式使用：

```powershell
dotnet run --project mmd/CameraValidation -c Release -- --package "D:/Games/SecretFlasherManaka"
```

此模式只检查插件、公共骨骼清单、原版互操作接口和说明文件，不要求玩家拥有 WAVEFILE/Shake 测试素材。原有带素材入口仍会严格验证这些素材的默认导入规则。

## 安装与命名迁移验证

生成安装包后运行：

```powershell
./tests/InstallerValidation.ps1 -PackageDir "./releases/local-build"
```

此验证在临时目录生成模拟文件，不修改实际游戏。覆盖首次安装、重复升级、Seleka2 到 SFM 的动作/配置迁移、新数据优先、占用检查与中途失败回滚。模拟标记不作为运行时程序集加载；原游戏接口检查仍独立执行。

StatsValidation 验证属性参数、原生输入、按帧去重、角色归属、排尿、方向感知及姿势任务条件；使用明确的原生边界夹具。EventValidation 另外覆盖真实播放驱动的开始/暂停/停止门禁。通过离线验证不代表已完成游戏内属性及任务实测。

核对属性补丁的原版入口：

```powershell
dotnet run --project more-actions/AnimatorValidation -c Release -- --stats-interop $gameDir more-actions/Plugin/bin/Release/net6.0/SFM.MoreActions.dll
```

AssetValidation 另覆盖 MP3 原生请求的异步准备、Unicode 本地路径、缓存、失败隔离、请求取消与音频所有权；这些是原生边界夹具，真实解码和游戏内播放需分别验证。
