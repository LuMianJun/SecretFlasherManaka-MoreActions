# VMD 导入实现方案（0.7.3）

面向玩家的说明见上级 README.zh-CN.md。本文保留默认规则及维护注意事项。

## 默认值与覆盖

| 字段 | 未填写时的行为 |
| --- | --- |
| id | 当前动作文件夹名；动作间不能重复 |
| displayName | 当前动作文件夹名；与 id 独立，覆盖 id 不改变默认显示名称 |
| vmdFile | 使用 `motion.vmd`，缺失则不能导入动作 |
| audioFile | 有 `BGM.wav` 就加载，没有则静音 |
| referencePose | 默认 A：参考 UnityVMDPlayer 以约 30° 上臂下垂建立虚拟参考姿势；T 保留原游戏绑定参考。每动作 JSON 可选择 A/T，缓存与预热按模式区分。 |
| audioDelaySeconds | 0，即音乐无额外延迟 |
| volume | 1，动作原始倍率；最终仍受设置页音量控制 |
| camera.vmdFile | 有 `camera.vmd` 就加载，没有则使用原版镜头 |
| camera.enabled | true，但仍服从设置页的 MMD 镜头总开关 |
| camera.timeOffsetSeconds | 0 |
| camera.scale | 0.08，将 MMD 镜头坐标换算到游戏尺度 |
| camera.heightOffset | 0 |

其他动作参数沿用代码默认值：requiresHands、requiresStanding、writeHipTranslation、writeFaceMorphs、followFaceCamera、showInRing 为 true；bodyMask 为 FullBody；loop 为 false。正常导入不用写。没有 schemaVersion，不维护 JSON 格式版本兼容。

嵌套配置逐字段合并。例如只写 `camera.heightOffset`，依然会自动发现默认的 camera.vmd，并保留镜头默认缩放。数值 0 和布尔 false 都是有效覆盖，不能当作“未填写”。

## 只写需要改变的参数

音乐比动作晚 1 秒开始：

```json
{
  "audioDelaySeconds": 1
}
```

文件名与默认约定不同：

```json
{
  "vmdFile": "动作＋表情.vmd",
  "audioFile": "音乐.wav",
  "camera": {
    "vmdFile": "镜头.vmd"
  }
}
```

文件存在，但不使用音乐或镜头：

```json
{
  "audioFile": "",
  "camera": {
    "enabled": false
  }
}
```

此处空 audioFile 是显式禁用音乐，不再查找 BGM.wav。其他资源字段不使用空字符串表达默认值；想使用默认值就省略字段。显式填写的文件路径必须有效，不能找不到时悄悄退回默认文件。默认可选音乐/镜头不存在则正常跳过；配置或资源错误应说明原因，镜头失败可以跳过镜头而保留动作与音乐。

## 两个现有动作按新规则的配置示例

Shake 使用默认文件名、游戏角色生成的虚拟 MMD 骨架和无延迟音乐。若音量统一交给设置页，它可以不带 JSON。

WAVEFILE 使用游戏生成骨架，只有音乐延迟需要显式填写：

```json
{
  "audioDelaySeconds": 5.333333333333333
}
```

如需继续使用小写固定 ID，可另外填写 `"id": "wavefile"`；默认 ID 则是其文件夹名称 `WAVEFILE`。NPC 的 MMD 动作名单匹配目前不区分大小写，其他使用 ID 的调用方应填写实际 ID。

## 播放与限制

重启游戏后从原环状列表选择动作。移动、取消、再次选择相同动作或原版状态限制会停止演出并恢复镜头。设置入口是“设置 → 游戏系统”，没有插件自定义快捷键或测试面板。开启围观插件的“MMD动作触发”和“所有MMD动作”，可让导入动作自动触发围观。

音乐支持单/双声道 PCM16 或 float32 WAV。镜头 VMD 与动作 VMD 分开存放。不导入外部模型；模型专属骨骼及脚部 IK 的已知偏差仍是后续事项。游戏骨架生成路径仍是假设标准 MMD 控制层级的近似，并非所有动作都能完全匹配。
