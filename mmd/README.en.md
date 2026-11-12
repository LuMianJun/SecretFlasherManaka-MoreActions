# MMD manual

[简体中文](README.zh-CN.md) | English

## Quick start

### What you can do

Put VMD motions in a folder and play them on the game character from the native radial menu. Supports accompanying music, supplied camera tracks, movement, and standard base-model expressions. With NPC Spectators installed, ordinary pedestrians can approach and watch.

Install **More Actions + MMD** from a Release as described on the [repository home](../README.en.md#installation), then prepare your motion files.

### Add a motion

1. Open `BepInEx/plugins/SFMVmdProbe/Motions`.
2. Create a folder. Its name becomes the action's in-game display name.
3. Put the motion files inside and use the filenames below.
4. Restart the game, then play the action as described in the next section.

| Filename | Purpose | Required |
| --- | --- | --- |
| `motion.vmd` | Character motion | Yes |
| `face.vmd` | Separate expressions, overriding matching main-motion channels | No |
| `lip.vmd` | Separate lip motion, overriding matching mouth channels last | No |
| `camera.vmd` | Supplied camera track | No |
| `BGM.wav` or `BGM.mp3` | Accompanying music | No |
| `lyrics.lrc` | Line-timed lyrics for the music | No |

When expressions and lips are supplied separately, name them `face.vmd` and `lip.vmd` and put them in the motion folder. Layers apply in order: main motion → separate expressions → separate lips. Each later layer overrides matching channels and retains unmentioned channels. The lip file only applies supported mouth morphs. Body bones, eye bones, and camera data in either companion file are ignored.

Both companions start at motion time zero and share its timeline, holding their last frame if shorter and stopping at the main motion's duration if longer. Explicitly selected files that are missing, damaged, or unusable log `MMD_FACE_REJECTED` or `MMD_LIP_REJECTED` and retain the preceding layer; other valid layers still apply. An omitted field with no default file is skipped silently. The existing native expression overlay setting still applies.

Save UTF-8 lyrics as `lyrics.lrc` beside `motion.vmd`, or use `lyricsFile` for a different filename. A timestamp such as `[00:17.90]` shows that line at the bottom of the screen 17.90 seconds after the music starts. Lyrics pause and resume with the music and clear when it ends.

Example:

```text
Motions/
  MyDance/
    motion.vmd
    face.vmd
    lip.vmd
    camera.vmd
    BGM.wav
    lyrics.lrc
```

### Play from the radial menu

1. Enter a scene, stand up, and stop moving.
2. Hold any number key from **1–8** to open its native radial menu.
3. Keep holding it and use **Z / X** to switch pages until you find the imported action.
4. Point the mouse toward the action and release the number key to play it, or confirm with the left mouse button.

These are the default bindings; use your own if you changed them. Each slot remembers its selection. A short press of its number key later executes the saved action.

Resources prepare in the background. If an action has not appeared, reopen the menu later. Selecting an action that is still loading queues it until ready. Select it again to cancel the pending request, or select another action to replace the request.

### Stop, switch, and open menus

Move or select the same action again to stop it. Select another custom action to switch playback. Switching to a native operation or entering a restricted state also stops playback.

When switching from a native pose that allows transitions, resources prepare first, then the native action exits and the custom action starts. Handcuffs, crouching, sitting, and special interactions still follow native rules.

Opening a menu with Tab or Esc preserves the action. If the menu pauses the game, motion, music, and camera pause together and resume when it closes.

Actions with movement use the game's character controller and retain collision. An obstruction stops playback, and the character stays at the position actually reached. Hair, skirt, and other secondary physics remain under game control.

### In-game settings

Open **Settings → More Actions**:

- **Action music volume:** adjusts the action's accompanying music.
- **Overlay game expressions:** on by default. Adds motion expressions to the game's expressions; turn it off to use only the motion's expressions.
- **Face camera tracking:** lets the native face camera track the head during playback. Visibility still follows the game's settings.
- **Use supplied camera:** enables or disables the motion's supplied camera.

For an audience, install NPC Spectators, open **Settings → Game System**, enable spectators, **MMD actions**, and **All MMD actions**, then close settings and play a motion.

## Advanced use

### Resources and loading

Name music BGM.wav or BGM.mp3. If both exist, BGM.wav takes priority. Use audioFile in action.json for other filenames.

Supported audio is MP3 or mono/stereo PCM16 or float32 WAV. MP3 is decoded asynchronously by the game's native API. Give every motion folder a unique name and avoid ASCII commas.

### Playback diagnostics

When playback ends early, `ACTION_CANCEL_DIAGNOSTIC` records the cancellation reason, current input, and the three-dimensional difference in player position. A general Cancel value is a record of input at that moment.

### When to use JSON

`action.json` is optional and customizes names, filenames, and playback settings.

Default filenames work directly. To change settings, add `action.json` inside the motion folder and specify only the fields you need. Omitted fields use their defaults.

You can edit the file in a text editor and copy the examples below. Save it as `action.json`; if it appears as `action.json.txt`, remove the final .txt. Put comments outside the JSON file.

#### Start music one second later

```json
{
  "audioDelaySeconds": 1
}
```

#### Lower this action's music volume

```json
{
  "volume": 0.7
}
```

The default is 1; 0 mutes music. The in-game action music volume also applies.

#### Keep the downloaded filenames

```json
{
  "vmdFile": "动作.vmd",
  "audioFile": "音乐.wav",
  "faceVmdFile": "表情.vmd",
  "lipVmdFile": "口型.vmd",
  "cameraVmdFile": "镜头.vmd",
  "lyricsFile": "歌词.lrc"
}
```

Place resources beside action.json and specify only files you use. Omitting an optional file field searches the default filenames above; `""` disables that companion file. A disabled expression or lip layer leaves the preceding layer's channels intact.

Existing `"camera": { "vmdFile": "camera-custom.vmd" }` configurations remain supported. When both forms are present, top-level `cameraVmdFile` wins and logs `MMD_CAMERA_FILE_OVERRIDE`. `camera.enabled: false` still disables the camera.

#### Use a differently named MP3

BGM.mp3 is detected automatically. For another filename, specify:

```json
{ "audioFile": "音乐.mp3" }
```

Paths are relative to action.json. An explicitly specified MP3 takes priority over BGM.wav. A missing explicit file is an error.

#### Change the display name and adjust camera height

```json
{
  "displayName": "我的舞蹈特别版",
  "camera": {
    "heightOffset": 0.1
  }
}
```

The camera still uses camera.vmd automatically. Height offset defaults to 0; positive values raise it and negative values lower it.

#### Disable music and the supplied camera even when files exist

```json
{
  "audioFile": "",
  "camera": {
    "enabled": false
  }
}
```

Examples can be combined in one JSON object. Use one action.json per motion folder and restart after editing.

### JSON parameters

All fields have defaults; specify only what you need. Quote filenames and text with double quotes, use numbers for numeric settings, and `true` or `false` for switches. `null` values and misspelled field names produce errors in the BepInEx log.

#### Names and files

| Parameter | Default | Purpose and examples |
| --- | --- | --- |
| `id` | Folder name | Stable action identifier, unique and without ASCII commas. Example: `"id": "mydance"`. Normally omit it. Changing it does not change the default display name. Renaming a folder changes the default ID; set this field if you need a stable one. |
| `displayName` | Folder name | Radial-menu display name, such as `"displayName": "MyDance"`. |
| `description` | Empty | Action note with no playback effect; currently not displayed in game. |
| `vmdFile` | `motion.vmd` | Motion filename, such as `"vmdFile": "dance.vmd"`. The file must exist. |
| `audioFile` | `BGM.wav` if present, otherwise `BGM.mp3` | WAV or MP3 music file, such as `"audioFile": "music.mp3"`. An empty string `""` disables music. Omission searches in the stated order; a missing explicitly specified file does not fall back. |
| `faceVmdFile` | `face.vmd` if present | Separate expression VMD, such as `"faceVmdFile": "expressions.vmd"`. Supports expression-only files and overrides matching main-motion morph channels. Empty disables this layer. Missing, damaged, or expression-free files warn and retain the main expressions; a valid lip layer still applies. |
| `lipVmdFile` | `lip.vmd` if present | Separate lip VMD, such as `"lipVmdFile": "lips.vmd"`. Only supported mouth channels apply, overriding matching main and expression channels last. Empty disables this layer. A file error or lack of supported mouth tracks warns and retains preceding layers. |
| `cameraVmdFile` | `camera.vmd` if present | Supplied camera VMD, such as `"cameraVmdFile": "camera-custom.vmd"`. Empty disables it. File errors warn and skip the camera while motion and music continue. Takes priority over legacy `camera.vmdFile`. |
| `lyricsFile` | `lyrics.lrc` if present | UTF-8 lyrics, such as `"lyricsFile": "song.lrc"`. Empty disables lyrics. Loaded only with music; file errors warn and skip lyrics. |
| `referencePose` | `"A"` | Reference pose for the generated skeleton. `"A"` calibrates upper arms roughly 30° downward; `"T"` retains the game's bind reference. Field and value are case-insensitive; only A/T strings are accepted. Affects the virtual skeleton, not the game mesh's bind data. |

Resource paths are relative to action.json and restricted to the motion folder and its subdirectories, such as `"music/BGM.wav"`.

#### Playback, volume, and body scope

| Parameter | Default | Purpose and conditions |
| --- | --- | --- |
| `audioDelaySeconds` | `0` | Nonnegative seconds before music starts after the motion, such as `1.5`. |
| `volume` | `1` | Music multiplier for this action, from `0` to `1`. In-game action volume also applies. |
| `loop` | `false` | Loops character motion. Currently only silent actions can loop; disable music when using `true`. |
| `requiresHands` | `true` | Requires usable hands. Handcuffs restrict actions needing hands. `false` is allowed only with `bodyMask` set to `"LowerBody"` and `writeFaceMorphs` set to `false`. Other game restrictions still apply. |
| `requiresStanding` | `true` | Requires standing. `false` removes only this requirement; other game states may still restrict playback. |
| `bodyMask` | `"FullBody"` | `"FullBody"` controls the whole body; `"UpperBody"` excludes the hips; `"LowerBody"` controls only the legs. |
| `writeHipTranslation` | `true` | Applies motion translation: horizontal movement uses the native character controller; vertical motion applies to the hips. Requires a body scope containing the hips. false disables both. |
| `keepGameExpressions` | `true` | Retains native expression overlay. false takes priority over the in-game switch. |
| `writeFaceMorphs` | `true` | Uses supported VMD expressions, such as blinking and mouth shapes. Not all MMD expressions have a matching game effect. |
| `followFaceCamera` | `true` | Allows the native face camera to follow this action, subject to the settings switch. Independent of the supplied main camera. |
| `showInRing` | `true` | Shows the action in the radial menu. false makes it unavailable for menu selection; normally keep the default. |
| `bonePaths` | `[]` | Advanced option: only animate the specified bones. Empty uses all bones in bodyMask. Requires full game bone paths, not MMD bone names. Paths must be within the chosen body scope and unique. |

A leg-only loop without music or expressions:

```json
{
  "audioFile": "",
  "loop": true,
  "bodyMask": "LowerBody",
  "requiresHands": false,
  "writeFaceMorphs": false
}
```

An advanced example using only the head bone:

```json
{
  "bodyMask": "UpperBody",
  "writeFaceMorphs": false,
  "bonePaths": [
    "Armature/Hips/Spine/Chest/Neck/Head"
  ]
}
```

#### Camera settings

Put camera settings inside a `"camera": { ... }` object. Specify changed fields only; others retain their defaults.

| Parameter | Default | Purpose and examples |
| --- | --- | --- |
| `camera.vmdFile` | `camera.vmd` if present | Supported legacy filename inside `camera`. Prefer top-level `cameraVmdFile` for new configurations; the top-level field wins when both are supplied. |
| `camera.enabled` | `true` | Uses this action's supplied camera. The global MMD camera switch must also be enabled. |
| `camera.timeOffsetSeconds` | `0` | Camera timing relative to motion. Positive starts the camera later; negative starts further into its track. Independent of music delay. |
| `camera.scale` | `0.08` | Camera position and distance scale. Use to adjust framing; normally leave unchanged. Must be greater than 0 and no greater than 100. |
| `camera.heightOffset` | `0` | Overall camera height offset in game meters. Positive raises it, negative lowers it; for example, `0.1`. |

Camera position and distance use a fixed conversion, defaulting to 0.08 game units per MMD unit. If character height or leg length changes, adjust `camera.scale` and `camera.heightOffset` manually as needed.

For example, start the camera half a second later and raise it slightly:

```json
{
  "camera": {
    "timeOffsetSeconds": 0.5,
    "heightOffset": 0.1
  }
}
```

The updated plugin continues to read old JSON. New `faceVmdFile`, `lipVmdFile`, `cameraVmdFile`, and `lyricsFile` fields are for the updated plugin. schemaVersion, bundle, clipAsset, audioAsset, and showInTab are outside the supported MMD configuration and fail validation.

### Action events

Events can play native sound effects or footsteps at specified times, or gather NPC spectators. They are optional, like music delays and camera settings; omission retains normal playback behavior.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `events` | `[]` | Timed events. Each entry uses time and event, with optional args and repeat. |
| `onStart` | `[]` | Runs when playback actually starts. Each entry uses event and optional args. |
| `onFinish` | `[]` | Runs after motion and music finish normally. |
| `onCancel` | `[]` | Runs when an action that has started is interrupted. Temporary requests still clean up automatically. |
| `useClipEvents` | `true` | Only applies to embedded Unity animation events; has no effect on VMD. |

For example, trigger spectators only for this action:

```json
{
  "onStart": [
    { "event": "npc.spectators" }
  ]
}
```

Requires NPC Spectators with its global switch enabled. Stopping the action releases this request automatically. Full event parameters and sound/footstep examples are in the accompanying [动作事件说明.zh-CN.md — Chinese](../shared/动作事件说明.zh-CN.md).

### Reference poses and ground contact

motion.vmd is the only required file; add other resources as needed. Without music, playback is silent. Without a supplied camera, the game camera is used. The virtual MMD skeleton is generated from the game character's mesh bind pose; `referencePose` selects an A/T reference.

By default, the plugin generates an A-pose skeleton using the character's existing bone lengths and body proportions. For a motion using a T-pose reference, set:

```json
{
  "referencePose": "T"
}
```

Use `"A"` or omit the field for the default reference, then restart after editing. This is a general A-pose reference and may differ from the author's model in pose and proportions. Joined hands, face touching, hand holding, kneeling, and similar contacts can still be offset; the plugin does not automatically adapt contact points.

Motion movement is retained by default. Horizontal movement uses the game character controller with collision; blocking or cancellation leaves the character at the position reached. Vertical motion is relative to the reference pose and retains first-frame crouching or lifting, so feet may leave the floor as part of the motion. The supplied camera and character movement follow their original tracks independently.

The game handles hair, skirt, and other secondary physics. The plugin automatically prepares resources, warms the skeleton cache, and rebinds the character after loading saves or changing scenes.

### Expression overlay

Motion expressions overlay native expressions by default, preserving customized face shape. Turn off **Settings → More Actions → Overlay game expressions** to use only motion expressions. Stopping playback restores native expression control.

An action can force overlay off:

```json
{
  "keepGameExpressions": false
}
```

The UI cannot override false. Omit the field or set it to `true` to follow the UI setting. `writeFaceMorphs: false` leaves all expression control to the game.

Supported base-model channels include smiles, eyebrows, one-eye blinking, and vowel mouth shapes. Only mapped tracks present in the VMD are played. Music provides audio; mouth movement comes from VMD data. The base model supplies a shared channel mapping that motions can reuse. Verify the expression layout if you replace the model.

### Native attributes and pose missions

Specify needed attributes in the motion folder's action.json. Other resource and playback settings keep their defaults. Prefer root-level fields, or use the stats object described below.

```json
{ "poseMission": true, "ecstasyPerSecond": 0.1 }
```

| Parameter | Default | Meaning |
| --- | --- | --- |
| `nativeKind` | "None" | Reads a preset from a representative native action below; does not change the actual action type. |
| `poseMission` | false | Counts toward native pose missions, still subject to exposure and mission-area conditions. |
| `stats` | {} | Optional object for attribute fields. Matching fields at the JSON root take priority. |

nativeKind values are case-sensitive:

| Value | Native action used |
| --- | --- |
| None, Interaction | No additional values. |
| Pose | HipShake |
| Onani | OnaniNormal |
| Dildo | UseDildoWallPussy1 |
| DildoFella | UseDildoWallFella1 |
| DildoWait | UseDildoWallWaitPussy |
| SitDildo | SitDildo |
| Sex | SexStandBack |
| Pee | PeeNormal |

Presets read current master data for one representative native action; actions in the same category may differ. Urination permission, pose missions, bladder replenishment, and stamina consumption have separate fields.

Prefer fields at the JSON root, or use stats. Root-level values take priority: ecstasyPerSecond=0 overrides the same field in stats and disables this action's arousal contribution.

All attribute fields:

| Parameter | Default when omitted | Meaning and limits |
| --- | --- | --- |
| `ecstasyPerSecond` | Preset AddEcstasy; 0 for None | Base arousal gain per second. **Full arousal is 1 (100%); 0.1 means a base gain of 10% per second.** Native action-speed, heart-rate, and enhancement multipliers still apply, so the final rate is not guaranteed to equal this number. Native item contributions, natural decrease, and climax thresholds remain active. 0 disables only this action's contribution, not item effects. |
| `dokidokiAdd` | Preset AddDokidoki100 ÷ 100; 0 for None | Action tension contribution, from 0 to 1, still affected by native level multipliers. Not BPM or a per-second accumulation. The game adds it once after each frame's recalculation, clamps to 0–1, and smooths heart rate and breathing. Native heart-rate rules at home are retained. |
| `moisturePerSecond` | 0 | Amount added each second to the bladder's pending absorption queue, in normalized 0–1 units. Native absorption speed is limited to about 0.01/second in current master data; the automatic replenishment skill can override the queue through native rules. Already queued amounts continue absorbing after playback stops. This is not clothing wetness. |
| `staminaPerSecond` | 0 | Native stamina units consumed per second, not a percentage. Uses native consumption, recovery cooldown, and exhaustion flashing. Exhaustion stops the custom action. |
| `strange` | Preset Strange; 0 for None | Base action suspicion after native exposure calculation, still multiplied by native lighting, awareness, and sight factors. Does not directly raise every NPC's suspicion bar. |
| `strangenessScale` | Preset StrangenessScale; 1 for None | Multiplies native directional exposure suspicion before adding strange. 0 disables that visual-exposure contribution, not independent channels such as native hearing. |
| `discomfort` | Preset Discomfort; 0 for None | Action-provided base discomfort limit. Takes the greater of this and native clothing/direction/state values, then follows native sight rules. Does not accumulate into the cache each frame. |
| `canStartPee` | false | Allows the action to start native urination when the bladder contains liquid, retaining timing, particles, flow rate, and completion handling. Does not fill the bladder. false does not block natural urination caused by a full bladder. |

With no fields or nativeKind preset, additional contributions default to 0, suspicion scale to 1, and urination/pose-mission switches to false. Specify only needed fields; displayName is optional.

Numbers must be finite and nonnegative; dokidokiAdd is limited to 0–1. Attributes may be at the root or inside stats. Numeric strings, null, and unknown or duplicate fields inside stats fail validation.

Full arousal is 1 (100%). ecstasyPerSecond=0.1 gives a base gain of 10% per second, affected by native action-speed, heart-rate, and enhancement multipliers. dokidokiAdd is a tension contribution, not a per-second increase.

When native climax conditions are reached, MMD stops and the game handles its climax animation and completion. Dedicated climax clips for standalone actions are not integrated. Urination requires liquid in the bladder and follows native urination restrictions and liquid-visibility settings. Once started, it ends on native timing. Stamina exhaustion also stops playback.

Contributions start with actual playback, pause with it, and end on stop, switching, or loading a save. Loading waits and previews retain native calculations. Already queued bladder replenishment continues through native absorption. Each start logs one ACTION_STATS entry with resolved values.

### Global plugin configuration

Global options are stored in BepInEx/config/local.sfm.vmdprobe.cfg, separately from per-action JSON.

| Setting | Default | Meaning |
| --- | --- | --- |
| `[Actions] Directory` | . | MMD search root relative to the MMD DLL. Subfolders are searched, so the default discovers packs under Motions. |
| `[Camera] EnableDistribution` | true | Uses supplied camera tracks; adjustable in game. Turning it off retains the game camera. |

More Actions supplies global music volume, expression overlay, and face-camera switches. See [More Actions configuration defaults](../more-actions/README.en.md#plugin-configuration-defaults).

### Troubleshooting

- **Action missing:** check the motion.vmd filename, folder, and JSON. Wait for preparation and reopen the radial menu. Restart after file changes.
- **Specified file does not load:** check paths relative to action.json. A missing explicit file does not select the default file. Expression and lip layers retain preceding results; camera and lyrics are skipped. Check the log for the cause. vmdFile cannot be empty.
- **No music:** check BGM.wav, BGM.mp3, or audioFile, plus game and plugin volumes.
- If lyrics do not appear, check that the `lyricsFile` path or default `lyrics.lrc` contains UTF-8 timed lines and that the motion is playing music. Restart the game after editing files.
- **No supplied camera:** check camera.vmd and enable **Use supplied camera**.
- **Expressions still overlay or do not move:** check keepGameExpressions, writeFaceMorphs, and whether the VMD contains supported expression tracks.
- **Playback stops without input:** some actions can stop early at certain shopping-mall locations, a known issue.

Include `BepInEx/LogOutput.log`, the action name, and approximate playback time when reporting a problem.

### Upgrade compatibility

- Separate expressions, lips, and the new top-level file fields require MMD 1.4.0 or later. This version reads old JSON; older plugins do not accept the new fields.
- When upgrading from Seleka2-prefixed versions, use the installer to migrate actions and settings. It backs up and moves legacy DLLs while preserving existing SFM configuration. For manual upgrades, move old DLLs out first to prevent duplicate loading.
- Update More Actions and MMD DLLs together.
- If an action used a companion PMD model configuration, remove modelFile from action.json. source.pmd is not imported; existing model files are not needed by this plugin.

See [THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md) for open-source references and licenses.
