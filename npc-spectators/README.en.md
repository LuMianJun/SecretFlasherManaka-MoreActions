# SFM NPC Spectators 1.0.0

[简体中文](README.md) | English

An independently installable NPC spectator plugin for **BepInEx 6 IL2CPP**. Its DLL is `SFM.NpcSpectators.dll`.

The plugin directs NPCs already in the current scene. Animation playback is handled by the relevant action plugin. Used alone, NPC Spectators supports manual gathering and native action triggers. Game source files and native saves remain unchanged.

## Quick start

### What you can do

- Manually gather ordinary pedestrians through the settings menu.
- Automatically gather during specified native actions; the default trigger is HipShake.
- Gather during MMD playback when More Actions and MMD are installed.

NPCs approach using game navigation, then stand and face the player. Walls, floors, routes, and NPC states affect whether they can reach a position.

### Installation

Install NPC Spectators from a Release as described on the [repository home](../README.en.md#installation). Its DLL goes in `BepInEx/plugins/SFMNpcSpectators/`.

| Installed plugins | Behavior |
| --- | --- |
| NPC Spectators only | Manual gathering through native settings, plus configured native action triggers. |
| With More Actions and MMD | Configured MMD motions can trigger gathering and release their requests when playback ends. |

### Manual gathering

1. Enter a scene with ordinary pedestrians and stand where you want them to gather.
2. Open **Settings → Game System**, find the options prefixed **[Spectators]**, and make sure **Enable** is on.
3. Turn on **Manual gathering** and adjust **Radius** and **Spacing** as needed.
4. Close settings. Pedestrians begin approaching; distant NPCs need time to arrive.

### Gather automatically during actions

- **Native actions:** turn on **Native actions** on the same page, close settings, and perform the native HipShake action.
- **MMD motions:** install More Actions and MMD, enable **MMD actions** and **All MMD actions**, close settings, and play an MMD motion from the radial menu. With **All MMD actions** off, only listed motions trigger gathering; the default is WAVEFILE.

### Dismiss and gather elsewhere

Movement or cancellation ends the current manual request. You can also turn off **Manual gathering** in settings. Action-triggered requests end with their corresponding action. Sources are managed separately; turning off **Enable** releases all current spectator control.

A gathering uses the player's position at its start as a fixed center. During player movement, NPCs keep their assigned positions and navigation targets. To gather at a new location, end the current gathering and trigger another.

Opening another UI releases current spectator control. A manual request made in settings runs after settings close. Changing scenes also ends the current gathering.

## Advanced use

### Trigger configuration

The configuration is created on first load at `BepInEx/config/local.sfm.npcspectators.cfg`.

The default native trigger is `HipShake`, native ActionType 50002. Set `Triggers/NativeActions` to an empty string to disable listed native triggers, or use comma-separated enum names or numeric IDs, such as `HipShake,Dogeza`. Use actual native actions; `None/-1` is excluded. Native unlock progression still determines which actions the character can perform.

`Integration/FollowMmd` defaults to true and `MmdActions` to `wavefile`. An optional playback API supplies MMD state; without MMD, manual gathering and native triggers still work.

`Integration/FollowAllMmd` defaults to false, retaining the configured motion list. Enabling it permits every imported MMD motion to trigger gathering. `Triggers/FollowNativeActions` defaults to true. `Gathering/Radius` defaults to 2.8 meters and `Spacing` to 1.1 meters; adjust them in settings or the configuration file.

### All plugin settings

Settings are stored in BepInEx/config/local.sfm.npcspectators.cfg. In-game switches and sliders update these values directly. Restart after editing the file manually.

| Setting | Default | Meaning and range |
| --- | --- | --- |
| `[General] Enabled` | true | Global spectator switch. Turning it off releases current NPC control. |
| `[Triggers] FollowNativeActions` | true | Automatically gathers during listed native actions. |
| `[Triggers] NativeActions` | HipShake | Comma-separated native ActionType enum names or numbers. Empty disables listed triggers; None/-1 is not accepted. |
| `[Integration] FollowMmd` | true | Listens to MMD playback. Without MMD, other trigger types still work. |
| `[Integration] FollowAllMmd` | false | Every MMD motion can trigger gathering, ignoring the list. Still requires FollowMmd=true. |
| `[Integration] MmdActions` | wavefile | Comma-separated MMD action IDs. Used only with FollowMmd=true and FollowAllMmd=false. |
| `[Gathering] Radius` | 2.8 meters | Distance from this gathering's center to the inner ring. Clamped to 1.8–8 meters. |
| `[Gathering] Spacing` | 1.1 meters | Minimum separation between assigned spectator positions. Clamped to 0.8–3 meters. |

**Manual gathering** defaults to off and lasts only for the current scene. The npc.spectators action JSON event provides another optional trigger, described below.

### Action event trigger

With More Actions installed, add `{"event":"npc.spectators"}` to an action's onStart or events list; entries in events also require time. This trigger follows the global **Enable** switch and native scene conditions, independently of the **MMD actions** switch. Each playback owns a separate request, and stopping it releases only that request.

### NPC eligibility, navigation, and restoration

The plugin searches the whole current scene's `NpcManager.ExistNpcList` for ordinary spawned pedestrians. Candidates must have FixedType=None, be Idle, not be police or seated, hold no interaction token, and have a valid navigation agent and patrol route.

Police, fixed shop staff, fixed seated or chatting NPCs, and NPCs sleeping, interacting, frightened, or detecting the player retain native behavior. Eligible newly spawned pedestrians can also join. Up to four recruitment attempts run every 0.25 seconds; distant NPCs must walk to the gathering.

Positions are assigned in concentric rings, preferring inner rings and free positions closer to each NPC. Visibility checks run from the player's waist and chest heights toward candidate positions. Walls and pillars exclude corresponding positions; the player, NPCs, and triggers are not treated as obstructions. After NavMesh projection, visibility is checked again, followed by floor, complete-path, and assigned-spacing checks. Visibility results are reused only within the current recruitment scan and queried again on later scans.

Obstructions may leave some pedestrians without a reachable position; the plugin retries every five seconds. Spectators are existing scene pedestrians, approaching through navigation with native position and collision behavior.

Approach movement uses native `NpcLocomotion.SimpleUpdateFromOtherState` with a temporary navigation target. Turning, speed, frame step, navigation synchronization, FixedUpdate, and nearby collision corrections remain native. The normal patrol entry is paused during spectator control. The plugin adds no separate stuck timer, repath monitor, or teleportation logic.

Each gathering has a fixed center captured at its start. Later recruits use the same center. During MMD movement, NPCs retain current control, assigned positions, and targets. Once arrived, they stand and face the player's current position. Extra cheering or clapping animations are not provided. When the trigger ends, is cancelled, or the scene changes, control is released; the next gathering uses a new center.

On stop, the plugin restores the previous navigation target, target cache, stopped state, and walking animation speed. Agent position/rotation update flags retain native behavior. Original patrol routes and indices are preserved; control is released before the native state-entry logic resumes.

Native vision, suspicion, player detection, mission checks, and time-stop rules remain active during gathering.

Opening another UI releases current NPC control; manual requests created in settings remain pending until the UI closes. Leaving the scene, losing the player, or ending the game clears manual requests and spectator control.

### Upgrade compatibility

When upgrading from Seleka2-prefixed versions, run the installer to migrate settings and back up/move legacy DLLs. For manual upgrades, move the old DLLs out first to prevent both spectator plugins loading together.

### Troubleshooting

- **Nobody approaches:** check the global switch and trigger settings, and make sure eligible ordinary pedestrians exist nearby. Walls, floors, and navigation paths can leave no suitable positions.
- **NPCs do not gather again after the player moves:** each gathering keeps its original center. End it and trigger a new one to assign positions around the new location.
- **NPCs disperse after opening a menu:** opening another UI releases current control. A manual request made in settings runs after the menu closes.

When reporting a problem, include `BepInEx/LogOutput.log`, the trigger type, scene, and NPC behavior.
