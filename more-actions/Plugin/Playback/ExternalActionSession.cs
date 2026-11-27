using System;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SFM.Actions;

internal sealed class ExternalActionSession : IExternalActionSession
{
    readonly ActionDriver driver;
    readonly Action<string> interrupted;
    readonly Func<bool> isExercising;
    readonly bool hasIku;
    readonly Animator animator;
    readonly int scene;
    readonly MotionFaceCamera faceCamera = new();
    TPCameraController thirdPerson;
    FPCameraController writtenFirstPerson;
    Transform originalTarget;
    Transform followTarget;
    Vector3 eye, priorEyeLocal, writtenEyeLocal;
    bool faceCameraActive, disposed;
    int poseFrame = -1;
    NativeIkuPlayback iku;
    readonly string providerDirectory;
    ExternalActionProfiles profiles;
    ActionStatValues frameStats;
    bool framePoseMission;
    int statsFrame = -1, ikuFrame = -1, ikuSequence;
    ExternalIkuFrame ikuSnapshot;

    internal PlayerController Player { get; }
    internal string ProviderId { get; }
    internal ActionDefinition Rules { get; }
    internal ActionStatValues Stats { get; }
    internal bool HasPoseThisFrame => poseFrame == Time.frameCount;
    internal bool AcceptsIku => (hasIku || profiles?.IkuResource != null) &&
        (iku != null || Player.Pca?.PlayerState?.IsEcstasyMotion == true);
    internal bool IsExercising => !disposed && iku == null && (isExercising?.Invoke() ?? true);
    internal ExternalStaminaDepletion StaminaDepletion { get; } = ExternalStaminaDepletion.EndSession;
    internal bool PoseMissionActive => profiles == null ? Rules.PoseMission :
        statsFrame == Time.frameCount && framePoseMission;
    internal bool IsCurrentPlayer => Player != null && Player == PlayerController.Instance && animator != null &&
        animator == Player.Pca?.Animator && scene == SceneManager.GetActiveScene().handle;

    internal ExternalActionSession(ActionDriver driver, PlayerController player, string providerId, ActionDefinition rules,
        ActionStatValues stats, Func<bool> isExercising, bool hasIku, Action<string> interrupted)
    {
        this.driver = driver;
        this.interrupted = interrupted;
        this.isExercising = isExercising;
        this.hasIku = hasIku;
        Stats = stats;
        Player = player;
        ProviderId = providerId;
        Rules = rules;
        animator = player.Pca.Animator;
        scene = SceneManager.GetActiveScene().handle;
    }

    internal ExternalActionSession(ActionDriver driver, PlayerController player, ActionDefinition rules,
        ExternalActionOptions options, string directory, ExternalActionProfiles profiles)
        : this(driver, player, options.ProviderId, rules, ActionStatValues.Empty, null, false, options.Interrupted)
    {
        providerDirectory = directory;
        this.profiles = profiles;
        StaminaDepletion = options.StaminaDepletion;
        Rules.FollowFaceCamera = profiles.FollowFaceCamera;
        Rules.PoseMission = profiles.PoseMissionAvailable;
    }

    public bool IsActive => !disposed && IsCurrentPlayer;
    public ExternalIkuFrame Iku => ikuSnapshot;
    public int IkuSampleFrame { get; private set; } = -1;

    public bool TrySetProfiles(ExternalActionProfile[] replacement, out string reason)
    {
        if (profiles == null || !IsActive)
        {
            reason = "external action not active";
            return false;
        }
        if (!ExternalActionProfiles.TryBind(providerDirectory, replacement, out var bound, out reason)) return false;
        if (AcceptsIku && !string.Equals(profiles.IkuResource, bound.IkuResource, StringComparison.Ordinal))
        {
            reason = "cannot change active IKU profile";
            return false;
        }
        profiles = bound;
        Rules.FollowFaceCamera = bound.FollowFaceCamera;
        Rules.PoseMission = bound.PoseMissionAvailable;
        // Keep the already-sampled channels consistent for the remainder of this frame.
        return true;
    }

    public void ReportPose(Vector3 thirdPersonTarget, Vector3 firstPersonEye) => Track(thirdPersonTarget, firstPersonEye);

    internal bool TryReadStats(out ActionStatValues values)
    {
        values = ActionStatValues.Empty;
        if (profiles == null && Stats == ActionStatValues.Empty && !Rules.PoseMission) return false;
        if (!IsExercising) return false;
        if (profiles == null)
        {
            values = Stats;
            return values != ActionStatValues.Empty || Rules.PoseMission;
        }
        if (statsFrame != Time.frameCount)
        {
            frameStats = profiles.Sample(out framePoseMission);
            statsFrame = Time.frameCount;
        }
        values = frameStats;
        return values != ActionStatValues.Empty || framePoseMission;
    }

    internal (float Progress, float Weight)? ReadIku()
    {
        if (disposed || !IsCurrentPlayer) return null;
        AdvanceIku();
        return ikuSnapshot.Phase is ExternalIkuPhase.Waiting or ExternalIkuPhase.Playing
            ? (ikuSnapshot.Progress, ikuSnapshot.Weight) : null;
    }

    internal void UpdateIkuSnapshot()
    {
        if (profiles == null || ikuFrame == Time.frameCount || (profiles.IkuResource == null && iku == null)) return;
        ikuFrame = Time.frameCount;
        AdvanceIku();
    }

    private void AdvanceIku()
    {
        if (disposed || !IsCurrentPlayer) return;
        bool nativeActive = Player.Pca.PlayerState.IsEcstasyMotion;
        if (iku == null && !nativeActive) return;
        IkuSampleFrame = Time.frameCount;
        if (iku == null)
        {
            ikuSequence++;
            iku = NativeIkuPlayback.TryStart(animator);
            if (iku == null)
            {
                ikuSnapshot = new(ExternalIkuPhase.Interrupted, ikuSequence, 0f, 0f);
                driver.EndExternal(this, "native IKU unavailable", true);
                return;
            }
            Plugin.LogSource.LogInfo("EXTERNAL_ACTION_IKU_BEGIN provider=" + ProviderId);
        }
        var status = iku.Read(animator, nativeActive, out float progress, out float weight);
        if (status == NativeIkuPlayback.Status.Sampling)
        {
            var phase = weight <= 0f && progress <= 0f ? ExternalIkuPhase.Waiting : ExternalIkuPhase.Playing;
            ikuSnapshot = new(phase, ikuSequence, progress, weight);
            return;
        }
        iku = null;
        if (status == NativeIkuPlayback.Status.Interrupted)
        {
            ikuSnapshot = new(ExternalIkuPhase.Interrupted, ikuSequence, progress, 0f);
            driver.EndExternal(this, "native IKU interrupted", true);
        }
        else
        {
            ikuSnapshot = new(ExternalIkuPhase.Completed, ikuSequence, progress, 0f);
            Plugin.LogSource.LogInfo("EXTERNAL_ACTION_IKU_RESUME provider=" + ProviderId);
        }
    }

    internal void Track(Vector3 thirdPersonTarget, Vector3 firstPersonEye)
    {
        if (disposed || !IsCurrentPlayer) return;
        EnsureFollowTarget();
        if (followTarget != null) followTarget.position = thirdPersonTarget;
        eye = firstPersonEye;
        poseFrame = Time.frameCount;
    }

    void EnsureFollowTarget()
    {
        if (followTarget != null) return;
        var controller = Player.Pca?.PlayerLocomotion?.tpCameraController;
        if (controller == null || controller.cameraTarget == null) return;
        thirdPerson = controller;
        originalTarget = controller.cameraTarget;
        followTarget = new GameObject("External action follow target").transform;
        followTarget.SetParent(Player.transform, false);
        thirdPerson.cameraTarget = followTarget;
    }

    internal void TrackFirstPerson(FPCameraController controller)
    {
        if (disposed || poseFrame != Time.frameCount || controller == null ||
            controller != Player.Pca?.PlayerAvatarObjectReferencer?.FPCameraController ||
            Player.Pca?.PlayerLocomotion?.baseCameraController?.IsLockCamera == true) return;
        // Native first-person follow has just calculated its own position.
        // The base camera that consumes it runs after this callback.
        var target = controller.transform;
        if (writtenFirstPerson != controller || (target.localPosition - writtenEyeLocal).sqrMagnitude > .0000001f)
            priorEyeLocal = target.localPosition;
        target.position = eye;
        writtenEyeLocal = target.localPosition;
        writtenFirstPerson = controller;
    }

    internal bool DeferFaceCamera(FaceCameraController camera) =>
        Plugin.FollowCamera.Value && Rules.FollowFaceCamera && faceCameraActive && faceCamera.ShouldDefer(camera);

    internal void RefreshFaceCamera()
    {
        if (!Plugin.FollowCamera.Value || !Rules.FollowFaceCamera)
        {
            if (faceCameraActive) faceCamera.End();
            faceCameraActive = false;
            return;
        }
        if (poseFrame != Time.frameCount && !faceCameraActive) return;
        if (!faceCameraActive) faceCameraActive = faceCamera.Begin(Player);
        if (faceCameraActive) faceCamera.RefreshAfterPose();
    }

    internal void End(string reason, bool notify)
    {
        if (disposed) return;
        disposed = true;
        if (ikuSnapshot.Phase is ExternalIkuPhase.Waiting or ExternalIkuPhase.Playing)
        {
            ikuSnapshot = new(ExternalIkuPhase.Interrupted, ikuSequence, ikuSnapshot.Progress, 0f);
            IkuSampleFrame = Time.frameCount;
        }
        try
        {
            if (notify) interrupted(reason);
        }
        finally
        {
            try
            {
                if (writtenFirstPerson != null &&
                    (writtenFirstPerson.transform.localPosition - writtenEyeLocal).sqrMagnitude <= .0000001f)
                    writtenFirstPerson.transform.localPosition = priorEyeLocal;
                if (thirdPerson != null && followTarget != null && thirdPerson.cameraTarget == followTarget)
                    thirdPerson.cameraTarget = originalTarget;
                if (followTarget != null) Object.Destroy(followTarget.gameObject);
            }
            finally { faceCamera.End(); }
        }
    }

    public void Dispose() => driver.EndExternal(this, "released by provider", false);
}
