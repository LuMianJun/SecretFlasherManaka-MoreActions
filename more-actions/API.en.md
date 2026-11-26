# More Actions external API

[简体中文](API.zh-CN.md) | English | [Configuration guide](README.en.md)

Start with who controls the player. If your mod drives movement, props, and poses like the bicycle mod does, use [`TryBeginAction`](#drive-an-action-from-another-mod) to acquire a session. If you want More Actions to play a motion you supply, [register it with `ActionRegistry`](#register-and-play-an-action) and play it through `ActionPlaybackApi`.

Reference `SFM.MoreActions.dll` and add `[BepInDependency("local.sfm.moreactions", "1.7.0")]` to your plugin class. The APIs below are in `SFM.Actions` and run on the game thread. External sessions are available starting with More Actions 1.7.0.

## Drive an action from another mod

The caller owns animation, props, movement, and IK, and decides when the action starts and ends. More Actions manages exclusive player access, native interaction restrictions, camera follow, and session cleanup.

```csharp
var session = ExternalActionApi.TryBeginAction(
    providerId: "example.mymod", requiresHands: true, requiresStanding: true,
    actionResourceName: "ExampleMotion", isExercising: null, hasIku: false,
    interrupted: StopAndRestore,
    followCamera: out var followCamera, readIku: out _, reason: out var reason);
if (session == null) return;
```

1. **Start** with your BepInEx plugin GUID as `providerId`. Give `actionResourceName` a single directory name and declare any hand or standing requirements. A failed request returns `null` and supplies a `reason`. Keep the caller's state as it was if acquisition fails.
2. **During playback**, keep the session and the callbacks you use. For camera follow or native particle alignment, call `followCamera(thirdPersonTarget, firstPersonEye)` after writing the pose and IK for the frame, before the camera update. Pass world-space positions.
3. **On completion**, restore the pose, input, and props your mod owns, then call `session.Dispose()`. More Actions calls `interrupted` when it ends the session. Use the same cleanup for interruptions and clear saved references to the session and callbacks.

### Resource settings and activity

More Actions reads the optional `Actions/ExampleMotion/action.json` next to the caller's DLL. This example sets the arousal rate. See [version support](#version-support) for the available fields.

```json
{
  "stats": {
    "ecstasyPerSecond": 0.1
  }
}
```

`isExercising: null` enables attributes throughout the active session. Pass a predicate to enable them only during the active phase of a multi-phase action. Menus, game pauses, and native IKU suspend the attribute contribution.

If your mod supplies a dedicated IKU animation, set `hasIku: true` and retain `readIku` to sample native progress and blend weight while updating the pose. The callback returns `null` when there is no IKU to sample.

## Register and play an action

- `ActionRegistry.Register(definition, packDirectory, providerId)` registers a definition with its assets. Use `RegisterRuntime(definition, source, packDirectory, providerId)` when your mod provides a sampler. The source implements `IActionSource`, reports a valid duration, and creates a separate `IActionPose` for each player or preview target.
- Give each action a unique ID. Use `ActionRegistry.Get(id)` and `GetAll()` to look up registered actions. Call `UnregisterProvider(providerId)` when unloading a provider to remove its actions.
- `ActionPlaybackApi.Request(id)` requests playback of a registered action. `Stop()` stops playback managed by More Actions. Callers release their own sessions acquired through `TryBeginAction`.

## Observe playback

`ActionPlaybackApi.IsPlaying`, `CurrentActionId`, and `CurrentProviderId` describe playback managed by More Actions. `PlaybackChanged` reports starts and stops. `PoseApplied` reports the action ID and playback position in seconds after the final gameplay pose is applied. Read `PlaybackPositionSeconds` and `AudioPositionSeconds` from the same clock. Camera and subtitle providers can filter events by provider ID and unsubscribe when they finish.

## Version support

- **Released 1.7.0** provides the `TryBeginAction` signature shown above. External sessions read the arousal rate from `ecstasyPerSecond` or the `nativeKind` preset.
- **The 1.7.1 development source** provides two `TryBeginAction` overloads. The original overload continues to support existing callers and the attribute settings already available in development builds. The overload taking `ExternalActionOptions` and `ExternalActionProfile[]` has moved from the local preview into the source tree and awaits release. The bicycle uses it to manage pedaling and saddle-use effects within one ride session.

### 1.7.1 grouped session parameters

`TryBeginAction(options, profiles, out reason)` returns an `IExternalActionSession` on success. On failure it returns `null` and supplies a `reason`. The original overload keeps its parameter order, return type, `out` parameters, and single-profile calling pattern.

**`ExternalActionOptions` applies to the whole action.**

| Field | Default | Meaning |
| --- | --- | --- |
| `ProviderId` | Required | The caller's BepInEx plugin GUID. It identifies the DLL and resource directory. |
| `RequiresHands` | `true` | Reject the action while the player is handcuffed. |
| `RequiresStanding` | `true` | Reject the action while the player is crouching or sitting. |
| `ProviderOwnsMovement` | `false` | Set to `true` to acquire a session with a direction key held. The caller handles movement. |
| `StaminaDepletion` | `EndSession` | End the session when stamina reaches zero. With `KeepSession`, the caller decides how its action continues. |
| `Interrupted` | Required | Runs on the game thread when More Actions interrupts the session. Restore the pose, input, and props the caller owns here. |

**Each `ExternalActionProfile` defines one group of effects.** Acquire a session with at least one profile.

| Field | Default | Meaning |
| --- | --- | --- |
| `ResourceName` | Required | A single directory name. For example, `Bicycle` points to `Actions/Bicycle/action.json` next to the caller's DLL. |
| `Channels` | `All` | The attributes this group handles. Assign distinct channels when using multiple profiles, even if their conditions never overlap. |
| `IsActive` | `null` | Return `true` when this group should contribute. Omit it to contribute whenever the session is eligible. More Actions calls it at most once per eligible frame on the game thread. |
| `HasIku` | `false` | The caller has a dedicated IKU clip ready. At most one profile per session can enable it. |

Use `IsActive` to decide when effects apply and JSON to define their values. Channels cover stamina, arousal, tension, bladder refill, suspicion, discomfort, urination permission, and pose-mission eligibility. Attribute fields can be at the root or under `stats`, with root fields taking precedence. `nativeKind` supplies a native preset, and the first profile supplies `followFaceCamera`. See the [configuration table](README.en.md#native-attributes-and-missions) for units and defaults.

For example, the bicycle's pedaling profile uses `Bicycle`, `Stamina`, and a pedaling check. The file configures a cost of `0.05` per second when the rider is exposed and actually pedaling forward or backward. The saddle-use profile uses `BicycleSeatUse` with the `SitDildo` preset. Both profiles can contribute during the same ride.

**Session operations**

- `IsActive` checks whether the session is still valid for the current player, animator, and scene.
- `TrySetProfiles(profiles, out reason)` takes the full replacement list, including profiles you want to keep. A failed update leaves the previous list active. Retain the current IKU resource while native IKU is in progress.
- `ReportPose(thirdPersonTarget, firstPersonEye)` reports both world-space positions after writing the pose and IK.
- `Iku` reports the latest phase, sequence, progress, and blend weight. Reading it does not advance playback.
- `Dispose()` normally ends the session after the caller restores its pose, input and props.
