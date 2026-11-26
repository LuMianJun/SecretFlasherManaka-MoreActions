using System;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace SFM.Actions;

public sealed partial class ActionDriver
{
    ExternalActionSession externalAction;
    bool endingExternalAction;

    [HideFromIl2Cpp]
    internal ExternalActionSession TryBeginExternal(string providerId, bool requiresHands, bool requiresStanding,
        string actionConfigPath, Func<bool> isExercising, bool hasIku, Action<string> interrupted, out string reason)
    {
        if (!PrepareExternal(requiresHands, requiresStanding, false, out var player, out var rules, out reason))
            return null;
        ActionStatValues stats;
        try
        {
            var configuration = ExternalActionApi.ReadConfiguration(actionConfigPath);
            rules.PoseMission = configuration.PoseMission;
            rules.FollowFaceCamera = configuration.FollowFaceCamera;
            stats = NativeActionStats.Resolve(configuration);
        }
        catch (Exception error)
        {
            reason = "invalid action configuration";
            Plugin.LogSource.LogWarning($"EXTERNAL_ACTION_CONFIG_FAILED provider={providerId} file={actionConfigPath} {error.Message}");
            return null;
        }
        externalAction = new ExternalActionSession(this, player, providerId, rules, stats, isExercising, hasIku, interrupted);
        Plugin.LogSource.LogInfo($"EXTERNAL_ACTION_START provider={providerId} ecstasy={stats.EcstasyPerSecond} iku={hasIku}"
            + $" dokidoki={stats.DokidokiAdd} moisture={stats.MoisturePerSecond} stamina={stats.StaminaPerSecond}"
            + $" strange={stats.Strange} scale={stats.StrangenessScale} discomfort={stats.Discomfort} pee={stats.CanStartPee}"
            + $" poseMission={rules.PoseMission} followFaceCamera={rules.FollowFaceCamera}");
        return externalAction;
    }

    [HideFromIl2Cpp]
    internal ExternalActionSession TryBeginExternal(ExternalActionOptions options, string directory,
        ExternalActionProfile[] profiles, out string reason)
    {
        if (!PrepareExternal(options.RequiresHands, options.RequiresStanding, options.ProviderOwnsMovement,
                out var player, out var rules, out reason) ||
            !ExternalActionProfiles.TryBind(directory, profiles, out var bound, out reason))
            return null;
        externalAction = new ExternalActionSession(this, player, rules, options, directory, bound);
        Plugin.LogSource.LogInfo($"EXTERNAL_ACTION_START provider={options.ProviderId} profiles={profiles.Length}"
            + $" iku={bound.IkuResource != null} staminaDepletion={options.StaminaDepletion}");
        return externalAction;
    }

    [HideFromIl2Cpp]
    private bool PrepareExternal(bool requiresHands, bool requiresStanding, bool providerOwnsMovement,
        out PlayerController player, out ActionDefinition rules, out string reason)
    {
        player = null;
        rules = null;
        if (externalAction != null || endingExternalAction || Playing || session.Stopping ||
            requests.PendingId != null || nativeExit != null)
        {
            reason = "action busy";
            return false;
        }
        player = PlayerController.Instance;
        if (player == null || player.Pca?.Animator == null || PlayerFacade.Instance == null)
        {
            reason = "player unavailable";
            return false;
        }
        if (Time.timeScale <= 0 || InGameUiManager.Instance?.IsOpenFade == true)
        {
            reason = "game paused or fading";
            return false;
        }
        rules = new ActionDefinition { RequiresHands = requiresHands, RequiresStanding = requiresStanding };
        reason = Reason(rules, player, InGameUiManager.Instance?.IsOpenSomeUi == true,
            providerHandlesMovement: providerOwnsMovement);
        return reason == null;
    }

    [HideFromIl2Cpp]
    internal void EndExternal(ExternalActionSession external, string reason, bool notify)
    {
        if (external == null || !ReferenceEquals(externalAction, external)) return;
        externalAction = null;
        playerParticles.Clear();
        ActionProjectileUpdatePatch.Clear();
        playerParticles.ReleaseEmitters();
        endingExternalAction = true;
        try { external.End(reason, notify); }
        catch (Exception error) { Plugin.LogSource.LogWarning("EXTERNAL_ACTION_CLEANUP_FAILED " + error); }
        finally { endingExternalAction = false; }
        Plugin.LogSource.LogInfo("EXTERNAL_ACTION_STOP provider=" + external.ProviderId + " reason=" + reason);
    }

    [HideFromIl2Cpp]
    void UpdateExternal(ExternalActionSession external, bool refreshCamera)
    {
        try
        {
            string reason = !external.IsCurrentPlayer || PlayerFacade.Instance == null ? "scene/player changed"
                : Reason(external.Rules, external.Player, false, allowIku: external.AcceptsIku, providerHandlesMovement: true);
            if (reason != null)
            {
                EndExternal(external, reason, true);
                return;
            }
            if (refreshCamera)
            {
                external.UpdateIkuSnapshot();
                if (!ReferenceEquals(externalAction, external)) return;
                if (external.HasPoseThisFrame) playerParticles.AfterPose(ParticlePlayer);
                if (ReferenceEquals(externalAction, external)) external.RefreshFaceCamera();
            }
        }
        catch (Exception error)
        {
            Plugin.LogSource.LogWarning("EXTERNAL_ACTION_UPDATE_FAILED " + error);
            EndExternal(external, "session update failed", true);
        }
    }

    [HideFromIl2Cpp]
    bool TryGetExternalStats(ExternalActionSession external, PlayerStateModel state, out ActionStatValues values)
    {
        values = default;
        if (!external.IsCurrentPlayer || !external.Player.gameObject.activeInHierarchy || NativeOperationInputBlocked ||
            Time.deltaTime <= 0 || state == null || state.Pointer != external.Player.Pca?.PlayerState?.Pointer ||
            state.CurrentAction?.Type != ActionType.None || (int)state.CurrentState != 0 || state.IsEcstasyMotion)
            return false;
        try
        {
            return external.TryReadStats(out values) && ReferenceEquals(externalAction, external);
        }
        catch (Exception error)
        {
            Plugin.LogSource.LogWarning("EXTERNAL_ACTION_ACTIVITY_FAILED " + error);
            EndExternal(external, "exercise status unavailable", true);
            return false;
        }
    }
}
