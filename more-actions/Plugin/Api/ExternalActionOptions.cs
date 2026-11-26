using System;
using UnityEngine;

namespace SFM.Actions;

/// <summary>Attributes a profile contributes while active. Each channel belongs to at most one profile in a session.</summary>
[Flags]
public enum ExternalActionChannels
{
    /// <summary>Contribute no attributes.</summary>
    None = 0,
    /// <summary>Base arousal rate from <c>ecstasyPerSecond</c>.</summary>
    Ecstasy = 1 << 0,
    /// <summary>Native tension contribution from <c>dokidokiAdd</c>.</summary>
    Dokidoki = 1 << 1,
    /// <summary>Bladder refill rate from <c>moisturePerSecond</c>.</summary>
    Moisture = 1 << 2,
    /// <summary>Stamina cost per second from <c>staminaPerSecond</c>.</summary>
    Stamina = 1 << 3,
    /// <summary>Native suspicion contribution from <c>strange</c> and <c>strangenessScale</c>.</summary>
    Exposure = 1 << 4,
    /// <summary>Native discomfort contribution from <c>discomfort</c>.</summary>
    Discomfort = 1 << 5,
    /// <summary>Permission to use the native urination flow from <c>canStartPee</c>.</summary>
    Pee = 1 << 6,
    /// <summary>Native pose-mission eligibility from <c>poseMission</c>. The mission's own conditions still apply.</summary>
    PoseMission = 1 << 7,
    /// <summary>All supported channels. Use explicit flags when registering multiple contributing profiles.</summary>
    All = Ecstasy | Dokidoki | Moisture | Stamina | Exposure | Discomfort | Pee | PoseMission
}

/// <summary>What More Actions does when this session's stamina cost brings stamina to zero.</summary>
public enum ExternalStaminaDepletion
{
    /// <summary>End the session and invoke its interruption callback.</summary>
    EndSession,
    /// <summary>Keep the session. The calling mod decides how its movement or action continues.</summary>
    KeepSession
}

/// <summary>The state of the latest native IKU attempt observed by the session.</summary>
public enum ExternalIkuPhase
{
    /// <summary>No native IKU attempt has been observed.</summary>
    Idle,
    /// <summary>Native IKU has started but has no progress or blend weight yet.</summary>
    Waiting,
    /// <summary>A native IKU pose can be sampled.</summary>
    Playing,
    /// <summary>The latest native IKU ended normally.</summary>
    Completed,
    /// <summary>The latest attempt could not start or was interrupted.</summary>
    Interrupted
}

/// <summary>Latest native IKU sample. Reading it does not advance the session.</summary>
/// <param name="Phase">Current native IKU playback phase.</param>
/// <param name="Sequence">Increments for each native IKU attempt in this session.</param>
/// <param name="Progress">Normalized native clip time, from 0 to 1.</param>
/// <param name="Weight">Native blend weight for the IKU clip, from 0 to 1.</param>
public readonly record struct ExternalIkuFrame(ExternalIkuPhase Phase, int Sequence, float Progress, float Weight);

/// <summary>Player requirements and cleanup for the entire session.</summary>
public sealed class ExternalActionOptions
{
    /// <summary>The caller's registered BepInEx plugin GUID. The plugin's directory is the root for profile resources.</summary>
    public string ProviderId { get; set; }
    /// <summary>Reject the action while the player is handcuffed. Defaults to true.</summary>
    public bool RequiresHands { get; set; } = true;
    /// <summary>Reject the action while the player is crouching or sitting. Defaults to true.</summary>
    public bool RequiresStanding { get; set; } = true;
    /// <summary>Allow acquisition with a direction key held. Defaults to false. The caller remains responsible for movement.</summary>
    public bool ProviderOwnsMovement { get; set; }
    /// <summary>Whether to end the session on stamina depletion. Defaults to EndSession.</summary>
    public ExternalStaminaDepletion StaminaDepletion { get; set; }
    /// <summary>Required callback that restores the caller's pose, inputs and props when More Actions ends the session. Runs on the game thread.</summary>
    public Action<string> Interrupted { get; set; }
}

/// <summary>One resource and its activity condition within an external action session.</summary>
public sealed class ExternalActionProfile
{
    /// <summary>
    /// One directory name under the caller's Actions directory. More Actions reads its optional action.json.
    /// The first profile supplies the session's followFaceCamera setting.
    /// </summary>
    public string ResourceName { get; set; }
    /// <summary>Which attributes to read from this resource. Defaults to All. Channels cannot overlap between profiles.</summary>
    public ExternalActionChannels Channels { get; set; } = ExternalActionChannels.All;
    /// <summary>The caller already has a dedicated IKU clip to sample. Defaults to false. At most one profile may enable it.</summary>
    public bool HasIku { get; set; }
    /// <summary>Return true while this profile should contribute attributes. Null enables it whenever the session is eligible. Called at most once per frame on the game thread.</summary>
    public Func<bool> IsActive { get; set; }
}

/// <summary>An exclusive player lease. Use its methods on the game thread.</summary>
public interface IExternalActionSession : IDisposable
{
    /// <summary>Whether the lease is still valid for the current player, animator and scene.</summary>
    bool IsActive { get; }
    /// <summary>The latest native IKU sample. Reading it does not advance playback.</summary>
    ExternalIkuFrame Iku { get; }
    /// <summary>Replace the full set of profiles within the current player lease.</summary>
    /// <remarks>Keep the native IKU resource in the set while that IKU is in progress.</remarks>
    /// <param name="profiles">The complete new set. Include profiles you want to keep.</param>
    /// <param name="reason">Why the update was declined. The previous profiles stay active on failure.</param>
    /// <returns>True if accepted. Attributes already sampled this frame keep their previous values until the next sample.</returns>
    bool TrySetProfiles(ExternalActionProfile[] profiles, out string reason);
    /// <summary>Report the final pose after animation and IK, before camera update. Native particle alignment uses the same report.</summary>
    /// <param name="thirdPersonTarget">World-space point for the third-person camera to follow.</param>
    /// <param name="firstPersonEye">World-space first-person eye position.</param>
    void ReportPose(Vector3 thirdPersonTarget, Vector3 firstPersonEye);
}
