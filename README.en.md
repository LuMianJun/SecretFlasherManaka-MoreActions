# SecretFlasherManaka More Actions

[简体中文](README.md) | English

Custom actions, Unity animation bundle import, MMD playback, and optional NPC spectators for **Secret Flasher Manaka**, using **BepInEx 6 IL2CPP**.

## Plugins

| Plugin | Version | Purpose |
| --- | --- | --- |
| More Actions | 1.6.1 | Radial-menu playback, Unity animation bundle import, native animation replacements, and in-game settings. |
| MMD | 1.3.1 | Direct VMD playback with optional music, camera tracks, and timed lyrics. Requires More Actions. |
| NPC Spectators | 1.0.0 | Lets eligible ordinary pedestrians approach and watch. Can be used independently. |

Install **More Actions + MMD** for MMD playback. Add NPC Spectators if you want an audience. Public packages contain plugins and documentation; supply your own game assemblies, motions, models, cameras, and music, and follow their authors' terms.

## Installation

Install the **IL2CPP version of BepInEx 6** first. Download the latest package from [Releases](https://github.com/LuMianJun/SecretFlasherManaka-MoreActions/releases), exit the game, extract the package, and merge its `BepInEx` folder into the game directory.

Exit the game before updating, and update More Actions and MMD together. If upgrading from a version with the Seleka2 prefix, use the included `install-mods.ps1` to migrate actions and settings. The installer backs up and moves the legacy DLLs it handles out of the plugin loading directory. Use the SFM plugin folders after migration.

## Import MMD motions

Create a separate folder for each motion under `BepInEx/plugins/SFMVmdProbe/Motions/`:

```text
Motions/
└─ MyDance/
   ├─ motion.vmd
   ├─ camera.vmd
   ├─ BGM.wav
   └─ lyrics.lrc
```

- `motion.vmd`: character motion, required.
- `camera.vmd`: supplied camera track, optional.
- `BGM.wav` or `BGM.mp3`: accompanying music, optional.
- An optional UTF-8 `lyrics.lrc` displays timed lyrics at the bottom of the screen during music playback.

Use a different folder name for each motion. Restart the game after adding files.

## Import Unity animation bundles

Place Unity AssetBundles containing animation clips under `BepInEx/plugins/SFMMoreActions/Actions/`. You can organize packs in separate subfolders:

```text
Actions/
├─ MyPack/
│  ├─ catpose
│  └─ v-sign
└─ OtherPack/
   └─ pose.bundle
```

The plugin reads AnimationClip (.anim) assets inside bundles, including animation bundles supplied for AnimReplacer. Bundled controller templates are not read. Ordinary `.anim` or FBX files must first be built into animation bundles.

- New actions such as CatPose and V-sign are played from the radial menu. Automatically imported actions loop by default.
- Clips whose names match native clips are replacement resources and appear only in the replacement manager.
- Restart after adding files. See the More Actions manual for import compatibility requirements.

## Playback and settings

1. Enter a scene, stand up, and stop moving.
2. Hold any number key from **1–8** to open the native radial menu. Keep holding it and use **Z / X** to reach the custom action pages.
3. Point the mouse toward an action and release the number key to play it, or confirm with the left mouse button. A short press of that number key later executes its saved selection.

These are the default bindings. Resources prepare in the background. If an action has not appeared yet, reopen the menu later. Selecting an action that is still loading queues it until its resources are ready.

Move, select the same action again, or switch to a native operation to stop playback. Native restrictions such as handcuffs and crouching still apply. Opening a menu preserves the action; when the game pauses, the action and music pause together.

Music, expression overlay, and camera options are under **Settings → More Actions**. For native animation replacements, open **Manage animation replacements**, select sources, and enable replacements. Changes apply when the character returns to standing idle.

NPC spectator options are under **Settings → Game System**. Enable spectators and choose a trigger. NPCs approach using native navigation; walls, floors, routes, and NPC states affect whether they can reach a position.

## Optional JSON and detailed manuals

Import resources directly to use the defaults. To customize names, playback, attributes, or mission eligibility, add a matching `.action.json` beside a Unity bundle, or an `action.json` inside an MMD motion folder. Specify only the fields you need, then restart the game.

For features, field meanings, defaults, and complete examples, see:

- [More Actions manual](more-actions/README.en.md): Unity bundles, replacement management, minimal JSON, all action fields, and attribute settings.
- [MMD manual](mmd/README.en.md): VMD, music, supplied cameras, reference poses, expressions, and attributes.
- [NPC Spectators manual](npc-spectators/README.en.md): triggers, configuration defaults, gathering rules, and dismissal.
- [Action events reference — Chinese](shared/动作事件说明.zh-CN.md): sound effects, footsteps, and spectator events.

## Known issues and reporting

- Some actions may end early at certain locations in the shopping mall.
- Differences between the game character's proportions and the motion author's model can affect precise contacts such as joined hands, touching the face, or kneeling.

When reporting a problem, include `BepInEx/LogOutput.log`, the action name, approximate playback time, and scene.

## Open-source references and dependencies

### MMD references

| Project or resource | Reference scope |
| --- | --- |
| [UnityVMDPlayer](https://github.com/hobosore/UnityVMDPlayer/blob/2cee8abd56c68ad3e88bcf4086e33185aa5ca4e8/UnityVMDPlayer.cs) | Center, groove, and all-parent translation; the 0.08 unit scale; default A-pose; bone interpolation parameter layout; and MorphPlayer expression binding. Movement integrates with the game's character controller, while expressions use the game's overlay and appearance protection. |
| [three.js r160 — MMDLoader](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/loaders/MMDLoader.js) | VMD animation, interpolation, coordinate conversion, and camera implementation. |
| [three.js r160 — MMDAnimationHelper](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/animation/MMDAnimationHelper.js) | MMD animation evaluation and camera behavior. |
| [mmdparser bundled with three.js r160](https://github.com/mrdoob/three.js/blob/r160/examples/jsm/libs/mmdparser.module.js) | VMD file structure and parsing. |
| [blender_mmd_tools unit reference](https://github.com/powroupi/blender_mmd_tools/wiki/1-MMD-unit-in-real-world-units) | Checking the convention of 0.08 meters per MMD unit. Blender is not required to run the plugin. |

These projects serve as algorithm, format, and behavior references. This repository uses its own C# VMD solver and implements game-derived skeleton generation, background preparation, skeleton caching, character controller collision integration, cancellation, and radial-menu/settings integration.

The referenced UnityVMDPlayer revision and MIT license are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Other projects and user-supplied assets retain their own licenses. Check the relevant authors' terms before redistributing motions, cameras, models, or music.

### Unity animation bundle reference

[AnimReplacer](https://f95zone.to/threads/secret-flasher-manaka-anim-replacer.299100/): bundle import, animation replacements, and standalone pose playback reference this project and source code supplied by its author. This plugin uses the game's radial menu and settings page; the original plugin supplies its own window and hotkeys. Author details and the original MIT license are included in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

### Runtime dependencies

- [BepInEx](https://github.com/BepInEx/BepInEx): plugin loading and configuration.
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop): IL2CPP interoperability.
- [HarmonyX](https://github.com/BepInEx/HarmonyX): patches to game methods.
