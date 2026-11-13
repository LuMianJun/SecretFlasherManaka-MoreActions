# More Actions manual

[简体中文](README.zh-CN.md) | English

Adds radial-menu actions, Unity animation bundle import, native animation replacements, music, expression overlays, action events, attribute changes, and pose-mission support to **Secret Flasher Manaka**. See the [repository home](../README.en.md) for installation and plugin combinations.

## Quick start

### What you can do

- Import Unity animation bundles, play new actions from the native radial menu, and save an action to each number-key slot.
- Apply replacement packs in bulk or choose a separate source for each native animation.
- Adjust music, expression overlays, and camera options in game. Install MMD to play VMD motions.

### Add animation bundles

Install More Actions from a Release as described on the [repository home](../README.en.md#installation). Exit the game, then place Unity animation bundles under `BepInEx/plugins/SFMMoreActions/Actions/`. Use a separate subfolder for each pack:

```text
Actions/
├─ MyPack/
│  ├─ MyPose.bundle
│  └─ Walk.bundle
└─ OtherPack/
   └─ MyPose.bundle
```

Restart after adding resources. Select new actions from the radial menu. Select clips with native animation names in **Manage animation replacements**.

### Play from the radial menu

1. Enter a scene, stand up, and stop moving.
2. Hold any number key from **1–8** to open its native radial menu.
3. Keep holding the key and use **Z / X** to switch pages until you find the imported action.
4. Point the mouse toward the action and release the number key to play it, or confirm with the left mouse button.

These are the default bindings; use your own bindings if you changed them. Each slot remembers its selection. A short press of its number key later executes the saved action.

Resources prepare in the background. If an action has not appeared yet, reopen the menu later. Selecting an action that is still loading queues it until ready. Select it again to cancel the pending request, or select another action to replace the request.

### Stop, switch, and open menus

Move or select the same action again to stop it. Select another custom action to switch playback. Switching to a native operation or entering a restricted state also stops playback.

When switching from a native pose that allows transitions, the player prepares the resources first, exits the native action, and starts the custom action. Handcuffs, crouching, sitting, and special interactions still follow native restrictions.

Opening a menu with Tab or Esc preserves the current action. If the menu pauses the game, the action, music, and camera pause together and resume when it closes.

Actions with movement use the game's character controller and retain collision. An obstruction stops playback, and the character stays at the position actually reached. Hair, skirt, and other secondary physics remain under game control.

### Replace native animations

1. Add animation bundles, restart the game, and open **Settings → More Actions → Manage animation replacements**.
2. Choose a view: select items or apply a whole pack in **By pack**, or choose sources per native animation in **By animation**.
3. Turn on **Enable replacements**.
4. Close settings and return to standing idle to apply the selections. Trigger the animation through its normal game controls afterward.

The manager supports mouse and keyboard navigation. Both views share the same configuration; switching views preserves selections.

- **By pack:** choose a pack on the left, then select individual entries, **Apply pack**, or **Clear pack** on the right.
- **By animation:** hides the pack sidebar and uses the full width for native animations. Click the source on the right and choose **Original** or a pack.
- **Selected only:** shows entries with a valid saved selection.
- **Enable replacements:** off by default. Selected resources apply only while enabled; turning it off preserves the selections.
- **Back or Esc:** closes a source picker first, then returns from the manager to More Actions settings. Further back navigation follows the native settings behavior.

Each native animation uses one source; different animations can use different packs. **Apply pack** updates animations covered by that pack. **Clear pack** removes selections currently supplied by that pack and preserves other selections.

Selections apply with menus closed while the character is standing and performing no action. Application is deferred during crouching, sitting, playback, or transitions.

Replaced animations still use native triggers, state machines, movement, collision, and event receivers. Bundles supply the body animation; the main view retains the original clip's camera tracks.

Native animations display their localized game names. External action and pack names use their authors' names.

### In-game settings

Open **Settings → More Actions**:

- **Action music volume:** scales custom action music, together with the game's master and BGM volumes.
- **Overlay game expressions:** on by default. Adds motion expressions to the game's expressions; turn it off to use only the motion's expressions.
- **Face camera tracking:** lets the native face camera track the head during playback. Its visibility still follows the game's settings.
- **Use supplied camera:** available with MMD installed. Enables or disables the motion's supplied camera.
- **Manage animation replacements:** opens the replacement manager described above.

The page supports Japanese, English, Simplified Chinese, Traditional Chinese, and Korean. NPC spectator options are under **Settings → Game System**.

Expression overlay preserves the character's customized face shape and returns expression control to the game when playback stops.

## Advanced use

### Bundle compatibility and import rules

Use subdirectories to keep packs separate; identically named files in different directories are imported independently.

Place Unity AssetBundles containing AnimationClip (.anim) assets in the action directory and restart to import their clips automatically. Animation bundles supplied for AnimReplacer are compatible. Bundled controllers and OverrideController templates are not imported; only the animation bundles are needed. The original AnimReplacer DLL and hotkey configuration are optional. Ordinary `.anim` or FBX files must first be bundled for the game's skeleton.

Humanoid animations play through the game character's Avatar. Generic animations match game bone paths. Skeletons, expressions, and animation events each have compatibility requirements; successful import still requires checking the resulting playback.

More Actions and MMD share the packaged `BepInEx/plugins/SFMMoreActions/body-bones.json`, which lists supported game bone paths, required bones, and MMD mappings. The preset loads once on first use and is validated at startup; it is supplied with the plugin rather than generated by scanning the avatar. Keep it with the DLLs, replace it when updating, and restart after editing it.

- A clip whose name matches a native clip becomes a replacement resource and appears only in replacement settings.
- Other clips become new radial-menu actions and loop by default when automatically imported.
- Native clip-name matching is case-sensitive and independent of bundle filenames or asset paths.
- When multiple packs supply the same native animation, choose one source in settings.

### Replacement storage and expression priority

If a pack contains several candidates for the same native animation, bulk application keeps an already selected candidate when possible; otherwise, it uses the first listed source. Loading a save automatically binds the new character.

Selections are stored in `[Replacements] Enabled/Selections` in the plugin configuration, independently of game saves. The original AnimReplacer `CheckedReplacements.json` is maintained separately. See **Upgrade compatibility** for migration details.

An action's `"keepGameExpressions": false` forces expression overlay off; the in-game switch cannot override it. Omit the field or set it to `true` to follow the switch. `"writeFaceMorphs": false` means the action does not take control of expressions at all.

### JSON is optional

Unity AssetBundle clips and their IDs are imported automatically. MMD also supplies defaults. Add JSON when you need to change the default behavior.

| Import mode | Configuration | Default behavior |
| --- | --- | --- |
| Automatic Unity import | No JSON | Imports every clip, loops silently, and generates IDs. |
| Automatic Unity import with overrides | Matching .action.json beside the bundle | Specify only changed fields; other values inherit automatic import. No ID, resource name, or loop field is required. |
| Explicit Unity clip | Name.action.json with bundle and clipAsset | Imports only the specified clip; other fields use the defaults below. |
| MMD import | Optional action.json | Uses motion.vmd and any available BGM.wav/BGM.mp3 and camera.vmd. See the [MMD manual](../mmd/README.en.md). |

Unity and MMD share playback, music, expression, body-scope, event, and native attribute settings. Resource fields differ: Unity uses bundle and clipAsset; MMD uses vmdFile, referencePose, and camera.

### Default IDs

Unity actions use **relative folder path + clip name**, such as MyPack/Pose. Subdirectories are part of the path. Clips directly in the Actions root use Actions/Pose.

```text
Actions/
├─ MyPack/
│  └─ pose.bundle       clip Pose → MyPack/Pose
└─ OtherPack/
   └─ pose.bundle       clip Pose → OtherPack/Pose
```

Automatic import and matching minimal configurations use the actual clip name. Explicit configurations use the last component of clipAsset with the .anim extension removed. Identically named clips in the same directory produce duplicate IDs; separate them into directories or assign explicit unique id values. Renaming a folder or clip changes its default ID. Set id when you need a stable identifier.

Legacy auto-generated IDs remain resolvable. Shortcut and replacement-source records migrate when resources are identified again.

### Configure JSON

Put a matching configuration beside the bundle: catpose uses catpose.action.json; pose.bundle uses pose.bundle.action.json or pose.action.json, with the former taking priority. Specify only needed values. DisplayName/displayName are both accepted; omitting the field keeps the imported name.

For example, CatPose only needs:

```json
{ "poseMission": true, "ecstasyPerSecond": 0.1 }
```

A matching minimal configuration applies to every clip in its bundle. Resources, clip names, and IDs come from the loaded bundle, and looping defaults to true. To select a single clip explicitly, specify bundle and clipAsset:

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose"
}
```

Inside MyPack, omitting id produces MyPack/Pose and omitting displayName displays Pose.

Once bundle and clipAsset are specified, that bundle uses explicit configuration. Create one configuration per desired clip and keep their IDs unique.

Restart after editing. Field names are case-insensitive. Use double-quoted strings, numeric values for numbers, and true/false for switches. Keep explanations and comments outside the JSON file. **The tables below describe explicit-resource defaults; automatic import and matching minimal configurations default loop to true.**

### All action parameters

#### Identity and resources

| Parameter | Default | Meaning and limits |
| --- | --- | --- |
| `id` | Folder + clip name | Unique internal identifier for shortcuts and replacement sources; normally omit it. Explicit values must be nonempty and unique, with no ASCII commas or control characters. It does not determine which native animation is replaced. |
| `displayName` | Imported name; clipAsset's clip name in explicit mode | Radial-menu label. Does not change the ID or replacement target. Native animations retain localized game names in the manager. |
| `description` | Empty string | Action note with no effect on playback; currently not displayed as a radial-menu description. |
| `bundle` | Omitted for automatic import; required in explicit mode | AssetBundle path relative to this JSON. Any extension is accepted; ordinary .anim and FBX files cannot be used directly. |
| `clipAsset` | Omitted for automatic import; required in explicit mode | AnimationClip asset name within the bundle: a resolvable short name or full asset path, such as Pose or assets/animations/pose.anim. Not a filesystem path. |

External file paths are relative to the JSON and restricted to its directory and subdirectories. Replacement slots match the loaded clip's actual name, case-sensitively. Bundle filenames and IDs locate resources and store selections.

#### Music and timing

| Parameter | Default | Meaning and limits |
| --- | --- | --- |
| `audioFile` | Empty string | External WAV or MP3 path. WAV supports mono/stereo PCM16 or float32; MP3 uses native game decoding. Omitted or empty means no external music. Unity actions do not automatically search for BGM files. |
| `audioAsset` | Empty string | Bundled AudioClip, available only with explicit bundle and clipAsset. Cannot coexist with a nonempty audioFile. For minimal configurations, use audioFile and set loop=false. |
| `audioDelaySeconds` | 0 | Nonnegative delay, in seconds, before music starts after the character motion. Does not shift the motion or events; has no effect without music. |
| `volume` | 1 | Per-action music multiplier, from 0 to 1, also affected by plugin and game volumes. A value of 0 mutes configured music; it does not remove it. |
| `loop` | false | Whether character motion loops. Explicit-resource actions play once by default; automatic import and matching parameter-only configurations loop by default. Music currently cannot loop: with true, omit audioFile/audioAsset. volume=0 does not bypass this restriction. |

Play once with music:

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "audioFile": "BGM.wav",
  "audioDelaySeconds": 1,
  "volume": 0.7
}
```

MP3 also works with a minimal configuration beside its Unity bundle:

```json
{ "audioFile": "音乐.mp3", "loop": false }
```

Paths are relative to the configuration. MP3 is decoded asynchronously by the game's native API and cached once ready. Delay, volume, pause, and synchronization use the shared playback pipeline. WAV and MP3 accompany single playback; set loop=false when adding music to a minimal configuration. ACTION_ASSETS_READY records mp3, wav, or bundled audio in audioFormat.

#### Body scope and movement

| Parameter | Default | Meaning and limits |
| --- | --- | --- |
| `bodyMask` | "FullBody" | FullBody controls the whole body; UpperBody excludes hips and legs; LowerBody controls only the left/right leg chains and also excludes hips. |
| `bonePaths` | [] | Controls only the specified full game bone paths. Empty selects from the 49 main bones within bodyMask, leaving secondary bones under native control. Explicit paths can select additional bones from the shared fixed character preset. Paths must be unique, inside the chosen scope, present on the target, and game paths rather than Japanese MMD bone names. |
| `writeHipTranslation` | true | Applies hip translation only when the scope includes Armature/Hips. Humanoid horizontal movement uses the collision controller; vertical motion applies to the hips. Generic clips sample game-skeleton local translation. false disables hip translation while retaining rotation. |

Play only the head bone:

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "bodyMask": "UpperBody",
  "writeFaceMorphs": false,
  "bonePaths": [
    "Armature/Hips/Spine/Chest/Neck/Head"
  ]
}
```

#### Availability, menu visibility, and cameras

| Parameter | Default | Meaning and limits |
| --- | --- | --- |
| `requiresHands` | true | Requires usable hands; native restrictions such as handcuffs apply. false is allowed only with bodyMask=LowerBody and writeFaceMorphs=false, and does not bypass other state checks. |
| `requiresStanding` | true | Requires standing; crouching and sitting restrict playback. false removes only this requirement, without forcibly exiting special interactions or bypassing native state checks. |
| `showInRing` | true | Shows a new action in the radial menu. Native replacement resources remain managed through the replacement page even if true. |
| `followFaceCamera` | true | Allows native face-camera tracking, also requiring its global settings switch. Does not control MMD's supplied main camera or native replacement camera tracks. |

A silent leg-only loop:

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "loop": true,
  "bodyMask": "LowerBody",
  "requiresHands": false,
  "writeFaceMorphs": false
}
```

#### Expressions

| Parameter | Default | Meaning and limits |
| --- | --- | --- |
| `writeFaceMorphs` | true | Uses mapped BlendShape tracks in the action, requiring matching paths and channels. Arbitrary custom expressions are not guaranteed. false leaves all expression control to the game. |
| `keepGameExpressions` | true | Allows native expression overlay. Omitted/true follows settings; false forces overlay off and cannot be overridden in game. Has no effect when writeFaceMorphs=false. |

Expressions come from the animation tracks. Overlay preserves customized face shape and returns control to the game on stop. Music only provides audio; mouth movement comes from expression tracks.

#### Events and lifecycle

| Parameter | Default | Meaning |
| --- | --- | --- |
| `useClipEvents` | true | Reads embedded clip events for radial-menu playback, adapting OnPlaySe, FootL, and FootR. Unsupported embedded events reject playback. false disables embedded events while JSON events still run. |
| `events` | [] | Timed events relative to the character motion's start, independent of music delay. |
| `onStart` | [] | Runs once when playback actually starts, not while waiting for resources. |
| `onFinish` | [] | Runs once after both motion and music finish normally. A looping action does not finish naturally. |
| `onCancel` | [] | Runs when an action that has started is interrupted. Callbacks are skipped if the scene or player is invalid, but requests still clean up. Cancelling before playback starts does not trigger it. |

Each events entry accepts:

| Field | Default | Meaning and limits |
| --- | --- | --- |
| `time` | Required | Seconds after motion start, from 0 through the motion duration. |
| `event` | Required | Registered event name from the table below. |
| `args` | {} | Event arguments object; cannot be null. |
| `repeat` | "EachLoop" | EachLoop runs once per loop; Once runs only once per playback. Replaying resets the count. |

onStart/onFinish/onCancel entries use event and optional args. Their timing follows the lifecycle; time and repeat are only for timed events.

| Event | args | Purpose |
| --- | --- | --- |
| native.playSe | Required type: native sound name | Calls the native player sound method. |
| native.animationSound | Required type: native sound name | Calls the native animation sound callback, retaining its effects branches. Usually native.playSe is sufficient. |
| native.footstep | Optional fields below | Uses native shoe, floor, direction, and state handling for footsteps. |
| npc.spectators | {} | Requires NPC Spectators and its global enable switch. Releases this playback's request when it ends. |

native.footstep args:

| Field | Default | Accepted values |
| --- | --- | --- |
| `foot` | "Left" | Left or Right. |
| `seType` | "FootStepHeel" | Native sound enum name, mainly used by the high-heel branch. |
| `direction` | "Forward" | Forward, Back, Left, Right, ForwardLeft, ForwardRight, BackLeft, BackRight. |
| `isMoveAction` | false | Uses native movement-action footstep handling, affecting sideways direction matching. |

Use the exact casing shown for args keys and enum names. Sound names include FootStepSneaker, FootStepHeel, Barefoot, ClothesSound, and UiDecide. The full list is supplied in 原版音效名称.txt. Native sound detection and footstep intervals still apply.

Footstep and spectator example:

```json
{
  "bundle": "MyPose.bundle",
  "clipAsset": "Pose",
  "onStart": [
    { "event": "npc.spectators" }
  ],
  "events": [
    {
      "time": 0.4,
      "event": "native.footstep",
      "args": { "foot": "Left" },
      "repeat": "EachLoop"
    }
  ]
}
```

See the [action events reference — Chinese](../shared/动作事件说明.zh-CN.md) for the full event notes.

### Native attributes and missions

These settings are shared by standalone Unity actions and MMD motions played from the radial menu. Native replacements retain native attributes. Standalone actions add the declared contributions; omitted values preserve the game's existing values and mission eligibility.

| Parameter | Default | Meaning |
| --- | --- | --- |
| `nativeKind` | "None" | Reads an attribute preset from a representative native action below: arousal, heart-rate contribution, suspicion multiplier, and discomfort. Does not switch the native action or modify shared master data. |
| `stats` | {} | Per-field preset overrides. Omitted fields inherit the preset; an explicit 0 disables that action contribution. |
| `poseMission` | false | Makes a standalone action eligible for native pose missions. Native exposure and mission-area conditions still apply; the game handles timing rather than receiving directly added progress. |

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

Presets read current master-data values for one representative action; other actions in the same category may differ. Urination permission, pose missions, bladder replenishment, and stamina consumption have separate fields.

Prefer placing attribute fields at the JSON root, or use the stats object. Root-level fields take priority: ecstasyPerSecond=0 overrides the same field inside stats and disables this action's arousal contribution.

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

With no fields or nativeKind preset, additional numeric contributions default to 0, suspicion scale to 1, and urination/pose-mission switches to false. Specify only needed fields; displayName is optional.

Numbers must be finite and nonnegative; dokidokiAdd is limited to 0–1. Attributes can be at the root or inside stats. Numeric strings, null values, and unknown or duplicate fields inside stats fail validation.

When native climax conditions are reached, the custom action stops and the game takes over its climax animation, completion, statistics, and mission counting. Dedicated climax clips for standalone actions are not integrated. Urination respects the game's urination restrictions and liquid-visibility settings. Once started, it continues on native timing after playback stops and changes bladder values normally.

Attribute contributions begin with actual playback, pause with it, and end on stop, switching, or loading a save. Loading waits and previews retain native calculations. One ACTION_STATS entry at each start records resolved values and urination/pose-mission flags.

CatPose adds base arousal at 0.1/second and counts toward pose missions:

```json
{
  "poseMission": true,
  "ecstasyPerSecond": 0.1
}
```

V-sign permits urination when the bladder contains liquid and counts toward pose missions:

```json
{
  "poseMission": true,
  "canStartPee": true
}
```

Save these as catpose.action.json and v-sign.action.json beside their respective bundles. Configurations apply in the bundle's directory; supply your own action assets.

### Settings by playback mode

| Setting group | Standalone radial-menu playback | Native animation replacement |
| --- | --- | --- |
| bundle, clipAsset | Specifies playback resources. | Supplies candidates matched to native slots by actual clip name. |
| id, displayName, description | Identifier and display metadata. | id stores the selected source; native animations retain localized names. |
| Music, delay, volume, loop | Uses the JSON settings. | Does not play JSON music separately; native state machines control looping. |
| Body scope, translation, expressions, conditions, face camera | Controls standalone sampling and playback. | Uses native action behavior rather than JSON ownership rules. |
| JSON events, useClipEvents | Handled by the plugin event scheduler. | JSON events do not run; native Animator and receivers handle embedded clip events. |
| Attribute fields, nativeKind, poseMission, stats | Adds attribute contributions, urination permission, and pose-mission eligibility. | Retains native action values without applying these settings again. |
| showInRing | Controls new action entries. | Does not change replacement candidate availability in the manager. |

### Plugin configuration defaults

The action root is specified by `[Actions] Directory` in `BepInEx/config/local.sfm.moreactions.cfg`, relative to the More Actions DLL. Restart after changing the directory, resources, or JSON.

Global settings are in BepInEx/config/local.sfm.moreactions.cfg; action JSON stores per-action settings. Common global options are also available in game.

| Setting | Default | Meaning |
| --- | --- | --- |
| [Actions] Directory | Actions | Action root, relative to the More Actions DLL. |
| [Audio] Volume | 1 | Music multiplier for all standalone actions. |
| [Face] KeepGameExpressions | true | Global expression overlay switch; cannot override action keepGameExpressions=false. |
| [Camera] FollowFaceCamera | true | Global face-camera tracking switch; visibility still follows the game. |
| [Replacements] Enabled | false | Applies selected replacements while on; turning it off preserves selections. |
| [Replacements] Selections | {} | Native-clip-to-resource-ID mapping maintained by the manager. |
| [Shortcuts] LastActions | Empty string | Eight radial-menu slot selections maintained by the UI; usually no manual edits are needed. |
| [Diagnostics] CameraTransitions | true | Bounded camera diagnostics on state changes, not continuous per-frame logging. |

### Troubleshooting

- **Action missing:** check paths, JSON, asset names, or duplicate IDs, then restart. Replacement resources appear only in the manager.
- **Replacement not applied immediately:** close menus, exit the current action, stand up, and wait for native standing idle.
- **Loop configuration fails:** check for configured audio. Muted audio still prevents looping.
- **Hands do not meet or feet miss the floor:** skeleton proportions may differ; precise contact is not automatically adapted.
- **Attributes do not change:** restart after JSON edits, check the matching configuration filename and supported fields, and inspect resolved values in ACTION_STATS. Native replacements use native attribute calculations.
- **Urination or pose missions do not work:** urination requires liquid in the bladder and permission from the game. Pose missions require poseMission plus native exposure and mission-area conditions.
- **Playback ends early:** native climax conditions or stamina exhaustion stop custom playback. Some actions may also stop early at certain shopping-mall locations, a known issue.

Include BepInEx/LogOutput.log, the action name, and approximate time when reporting a problem. Protective cancellation emits one ACTION_CANCEL_DIAGNOSTIC entry, not per-frame logs.

### Upgrade compatibility

- When upgrading from Seleka2-prefixed versions, run the installer to migrate actions and settings. It backs up and moves legacy DLLs while preserving existing SFM configuration. For manual upgrades, move old DLLs out first to prevent duplicate loading.
- When upgrading from a trial version using `BepInEx/plugins/AnimReplacer/`, move packs into `Actions/` and reselect replacements and radial shortcuts. `[LegacyAnimations] Directory` is inactive.
- Choose replacement sources in this plugin. Original AnimReplacer selections are not imported automatically. A native animation should be replaced by one plugin.

### Open-source reference

Bundle import references [Secret Flasher Manaka Anim Replacer](https://f95zone.to/threads/secret-flasher-manaka-anim-replacer.299100/). Animation replacement and standalone pose playback also reference source code supplied by its author. Author details and license text are in [THIRD_PARTY_NOTICES](../THIRD_PARTY_NOTICES.md).
