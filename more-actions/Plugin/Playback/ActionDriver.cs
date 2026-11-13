using System;
using System.Linq;
using UnityEngine;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.ObjectUI.InGame.MessagePanel;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using Il2CppInterop.Runtime.Attributes;
using ExposureUnnoticed2.Master.Action;

namespace SFM.Actions;

public sealed class ActionDriver : MonoBehaviour
{
    public ActionDriver(IntPtr pointer) : base(pointer) { }
    internal static ActionDriver Current;
    [HideFromIl2Cpp]
    internal PlayerController Owner { get; private set; }
    [HideFromIl2Cpp]
    internal ActionDefinition Definition => session.Action?.Definition;
    [HideFromIl2Cpp]
    internal bool Playing => session.Action != null;
    readonly ActionRequestQueue requests = new();
    readonly PlaybackSession session = new();
    int tickFrame = -1;
    internal bool CheckingAvailability;
    int requestRevision;
    sealed class NativeExit
    {
        internal string Id;
        internal int FromType, Attempts, ReadyFrame = -1;
        internal bool Accepted;
        internal float WaitSeconds;
        internal PlayerController Player;
        internal Animator Animator;
        internal ActionDefinition Definition;
    }
    NativeExit nativeExit;

    public void Update()
    {
        ActionSamplingRigPool.Prune();
        ActionAssets.Tick();
        ActionBundleImport.Tick();
        var animator = PlayerController.Instance?.Pca?.Animator;
        NativeAnimationReplacements.Tick(animator);
        ActionTargetApi.Target = animator == null ? null : animator.gameObject;
        ActionTargetApi.IsReady = animator != null && animator.enabled && animator.isInitialized;
        if (Owner != null && Owner != PlayerController.Instance) { StopWithDiagnostics("driver update", "scene/player changed"); Owner = null; }
        if (nativeExit != null && Owner == null) Stop("pending player destroyed");
        if (Playing && Owner == null) StopWithDiagnostics("driver update", "player destroyed");
        SyncPause();
    }

    [HideFromIl2Cpp]
    internal void Request(string id)
    {
        if(id!=null)id=ActionRegistry.Get(id)?.Id??id;
        if (!Playing && id != null && ActionRegistry.Get(id) != null && (requests.PendingId == id || nativeExit?.Id == id))
        {
            requests.Clear(); requestRevision++; AbandonNativeExit("selected again"); return;
        }
        requests.Request(id); requestRevision++; AbandonNativeExit("new request");
    }
    [HideFromIl2Cpp]
    internal bool Owns(PlayerStateModel state) => Playing && Owner != null && Owner.Pca?.PlayerState?.Pointer == state?.Pointer;
    [HideFromIl2Cpp]
    internal bool Owns(PlayerFacade facade) => Playing && Owner != null && facade?.pca?.PlayerState?.Pointer == Owner.Pca?.PlayerState?.Pointer;
    [HideFromIl2Cpp]
    internal bool Owns(PlayerAction action) => Playing && Owner != null && Owner.Pca?.PlayerAction?.Pointer == action?.Pointer;

    [HideFromIl2Cpp]
    internal bool TryGetStats(PlayerStateModel state, out ActionStatValues values)
    {
        values = default;
        if (session.Stopping || !Owns(state) || Owner != PlayerController.Instance || !Owner.gameObject.activeInHierarchy
            || !session.Clock.HasStarted(AudioSettings.dspTime) || session.Clock.Paused || Time.timeScale <= 0 || Time.deltaTime <= 0
            || state.CurrentAction == null || (int)state.CurrentAction.Type != -1 || (int)state.CurrentState != 0
            || state.IsEcstasyMotion || ActionRegistry.Get(Definition.Id) != Definition) return false;
        values = session.Stats; return true;
    }
    [HideFromIl2Cpp]
    internal bool TakeStatFrame(ActionStatChannel channel) => session.StatFrames.Take(channel, Time.frameCount);

    [HideFromIl2Cpp]
    bool PlayerInputBlocked => Time.timeScale <= 0 || InGameUiManager.Instance?.IsOpenSomeCancelInputUi == true
        || InGameUiManager.Instance?.IsOpenFade == true;
    [HideFromIl2Cpp]
    bool NativeOperationInputBlocked => PlayerInputBlocked || InGameUiManager.Instance?.IsOpenSomeUi == true;

    [HideFromIl2Cpp]
    void SyncPause()
    {
        if (!Playing) return;
        bool paused = Time.timeScale <= 0;
        if (paused == session.Clock.Paused) return;
        double now = AudioSettings.dspTime;
        if (paused) session.AudioPendingWhenPaused = session.Action.Audio != null && now < session.Clock.AudioStartDsp;
        session.Clock.SetPaused(now, paused);
        if (session.Audio == null || session.Action.Audio == null) return;
        if (paused)
        {
            // Stop a future schedule so delayed BGM cannot start in a pause.
            if (session.AudioPendingWhenPaused) session.Audio.Stop(); else session.Audio.Pause();
        }
        else if (session.AudioPendingWhenPaused) session.Audio.PlayScheduled(session.Clock.AudioStartDsp);
        else session.Audio.UnPause();
    }

    [HideFromIl2Cpp]
    static bool SwitchablePose(PlayerStateModel state) => state?.CurrentAction != null
        && (int)state.CurrentAction.Type >= 50000 && (int)state.CurrentAction.Type < 60001 && !state.IsGamanBaibu;

    [HideFromIl2Cpp]
    internal string Reason(ActionDefinition definition, PlayerController player, bool uiOpen, bool allowNativeExit = false)
    {
        var state = player == null ? null : player.Pca?.PlayerState;
        var game = InGameManager.Instance;
        var temp = game == null ? null : game.TempInGameState;
        bool skipPlayingAction = allowNativeExit && SwitchablePose(state);
        var availability = new ActionAvailability(state?.CurrentAction != null, state != null && (int)state.CurrentState == 0,
            !skipPlayingAction && state?.CurrentAction != null && (int)state.CurrentAction.Type != -1, state != null && state.IsEcstasyMotion,
            state != null && state.IsCrouch, state != null && state.IsSit,
            GameState.GameStateData != null && GameStateFacade.IsAttachSomeHandcuffs(), SexManager.Instance != null && SexManager.Instance.IsSexMode,
            !PlayerInputBlocked && InputManager.GetVector3(InputManager.InputType.Move).sqrMagnitude > 0.0001f, uiOpen && !Playing,
            player == null || !player.gameObject.activeInHierarchy || temp == null || temp.IsGameOver || temp.IsPlayingSomePerformance || temp.IsTimeStop);
        var reason = ActionPolicy.BlockReason(definition, availability);
        if (reason != null) return reason;
        return CanUseNativeHands(definition, false, skipPlayingAction) ? null : "native hand/action gate";
    }

    [HideFromIl2Cpp]
    bool CanUseNativeHands(ActionDefinition definition, bool showWarning, bool skipPlayingAction = false)
    {
        // Call the original hand gate with only the pack's declared skips. The
        // lease patch is bypassed solely during this owner's availability check.
        bool previous = CheckingAvailability;
        CheckingAvailability = true;
        try { return PlayerFacade.Instance.CanUseHand(showWarning, !definition.RequiresHands, false, skipPlayingAction, !definition.RequiresStanding); }
        finally { CheckingAvailability = previous; }
    }

    [HideFromIl2Cpp]
    bool ShowNativePostureWarning(ActionDefinition definition)
    {
        var state = Owner?.Pca?.PlayerState;
        if (state?.CurrentAction == null || GameState.GameStateData == null || PlayerFacade.Instance == null) return false;
        bool handcuffed = definition.RequiresHands && GameStateFacade.IsAttachSomeHandcuffs();
        bool crouching = definition.RequiresStanding && state.IsCrouch;
        if (!handcuffed && !crouching) return false;
        // Native CanUseHand owns warning priority, localization and Caution
        // styling. Do not use this to report normal custom-action switching.
        return !CanUseNativeHands(definition, true);
    }

    [HideFromIl2Cpp]
    void AbandonNativeExit(string reason)
    {
        if (nativeExit == null) return;
        var previous = nativeExit;
        nativeExit = null;
        Plugin.LogSource?.LogInfo("ACTION_NATIVE_EXIT_CANCELLED id="+previous.Id+" from="+previous.FromType+" reason="+reason);
    }

    [HideFromIl2Cpp]
    void RequeueNativeExit(string id, int revision, NativeExit exit)
    {
        if (requestRevision == revision && nativeExit == exit) requests.Request(id);
    }

    [HideFromIl2Cpp]
    bool PrepareNativeIdle(string id, int revision)
    {
        var state = Owner.Pca.PlayerState; var animator = Owner.Pca.Animator;
        if (animator == null || !animator.enabled || !animator.isInitialized)
        { AbandonNativeExit("animator unavailable"); return false; }
        int type = (int)state.CurrentAction.Type;
        int layer = animator.GetLayerIndex("Action");
        if (layer < 0) throw new InvalidOperationException("Native Action animation layer missing");
        bool idlePose = !animator.IsInTransition(layer) && animator.GetCurrentAnimatorStateInfo(layer).IsName("Action.NoMotion");
        if (nativeExit == null)
        {
            if (type == -1 && idlePose) return true;
            nativeExit = new NativeExit { Id=id, Definition=ActionRegistry.Get(id), FromType=type, Player=Owner, Animator=animator, Accepted=type == -1 };
        }
        var exit = nativeExit;
        if (exit.Id != id || exit.Definition != ActionRegistry.Get(id) || exit.Player != Owner || exit.Animator != animator || Owner != PlayerController.Instance)
        { AbandonNativeExit("target changed"); return false; }
        exit.WaitSeconds += Math.Max(0, Time.deltaTime);
        if (exit.WaitSeconds > 5)
        { AbandonNativeExit("timeout"); return false; }
        if (type != -1)
        {
            if (exit.Accepted || type != exit.FromType || !SwitchablePose(state))
            { AbandonNativeExit("native action changed"); return false; }
            if (!float.IsFinite(state.PoseActionCoolTime))
            { AbandonNativeExit("invalid pose cooldown"); return false; }
            if (state.PoseActionCoolTime > 0) { RequeueNativeExit(id, revision, exit); return false; }
            // Native may reject one same-frame switch. Retry on the next
            // controller frame, never repeatedly reset a pending transition.
            if (exit.Attempts >= 2) { AbandonNativeExit("native exit rejected"); return false; }
            exit.Attempts++;
            Owner.Pca.PlayerAction.ChangeAction(ActionType.None);
            if (requestRevision != revision || nativeExit != exit) return false;
            if ((int)state.CurrentAction.Type == -1)
            {
                exit.Accepted = true;
                Plugin.LogSource?.LogInfo("ACTION_NATIVE_EXIT id="+id+" from="+exit.FromType+" frame="+Time.frameCount);
            }
            RequeueNativeExit(id, revision, exit); return false;
        }
        if (!idlePose) { exit.ReadyFrame = -1; RequeueNativeExit(id, revision, exit); return false; }
        // A further native frame supplies a fresh baseline after NoMotion is
        // reached. Do not capture the old pose immediately after ChangeAction.
        if (exit.ReadyFrame < 0) { exit.ReadyFrame = Time.frameCount; RequeueNativeExit(id, revision, exit); return false; }
        if (Time.frameCount <= exit.ReadyFrame) { RequeueNativeExit(id, revision, exit); return false; }
        nativeExit = null;
        Plugin.LogSource?.LogInfo("ACTION_NATIVE_IDLE_READY id="+id+" from="+exit.FromType+" frame="+Time.frameCount);
        return true;
    }

    [HideFromIl2Cpp]
    internal void StopWithDiagnostics(string phase, string reason, Vector3? expected = null)
    {
        try
        {
            if (Playing)
            {
                bool available = Owner != null;
                var state = available ? Owner.Pca?.PlayerState : null;
                ActionCancellationDiagnostics.Record(Definition.Id, phase, reason, session.Clock.Elapsed(AudioSettings.dspTime),
                    expected ?? session.ExpectedPosition, available ? Owner.transform.position : default, available,
                    PlayerInputBlocked, NativeOperationInputBlocked, InGameUiManager.Instance?.IsOpenSomeUi == true, session.Clock.Paused,
                    state, expected.HasValue);
            }
        }
        catch (Exception e) { Plugin.LogSource?.LogWarning("ACTION_CANCEL_DIAGNOSTIC_FAILED "+e.Message); }
        finally { Stop(reason); }
    }

    [HideFromIl2Cpp]
    internal void BeforeNativeUpdate(PlayerController player)
    {
        CameraTransitionDiagnostics.BeginFrame(player);
        if (!Playing || Owner != player) return;
        SyncPause();
        (session.Pose as IActionBodyPose)?.RestoreNativeBody();
        (session.Pose as IActionFacePose)?.RestoreNativeFace();
        var ui = InGameUiManager.Instance;
        var reason = Reason(Definition, player, ui != null && ui.IsOpenSomeUi);
        bool nativeInput = !NativeOperationInputBlocked && (InputResolver.RingShortcutInput != RingMenuParentView.RingItemType.None
            || InputManager.IsDownFrame(InputManager.InputType.OpenClothesA) || InputManager.IsDownFrame(InputManager.InputType.OpenClothesB)
            || InputManager.IsDownFrame(InputManager.InputType.CloseClothesA) || InputManager.IsDownFrame(InputManager.InputType.Crouch)
            || InputManager.IsDownFrame(InputManager.InputType.Interact) || InputManager.IsDownFrame(InputManager.InputType.DrinkWater)
            || InputManager.IsDownFrame(InputManager.InputType.EyeMask));
        if (reason != null || nativeInput || (player.transform.position - session.ExpectedPosition).sqrMagnitude > 0.0004f)
            StopWithDiagnostics("before native update", reason ?? (nativeInput ? "native input" : "external displacement"));
    }

    [HideFromIl2Cpp]
    internal void Tick(PlayerController player)
    {
        if (tickFrame == Time.frameCount) return; tickFrame = Time.frameCount;
        if (Owner != player) { StopWithDiagnostics("player update", "player changed"); Owner = player; }
        SyncPause();
        var ui = InGameUiManager.Instance;
        var ring = RingMenuParentView.Instance;
        var request = requests.Take(ring != null && ring.IsOpenRing, (ui != null && ui.IsOpenSomeUi) || Time.timeScale <= 0);
        if (request != null)
        {
            if (Definition?.Id == request) Stop("selected again");
            else
            {
                int selected = requestRevision;
                if (Playing) { Stop("switch action"); if (requestRevision != selected + 1) return; }
                Start(request);
            }
        }
        if (!Playing) return;
        var reason = Reason(Definition, Owner, ui != null && ui.IsOpenSomeUi);
        if (reason != null || ActionRegistry.Get(Definition.Id) != Definition) { StopWithDiagnostics("request/update", reason ?? "action removed"); return; }
        UpdateVolume();
    }

    [HideFromIl2Cpp]
    void Start(string id)
    {
        var definition = ActionRegistry.Get(id);
        if (definition == null) { AbandonNativeExit("action removed"); return; }
        int request = requestRevision;
        string reason = Reason(definition, Owner, InGameUiManager.Instance != null && InGameUiManager.Instance.IsOpenSomeUi, true);
        if (reason != null)
        {
            AbandonNativeExit(reason);
            if (reason == "player unavailable") return;
            if (ShowNativePostureWarning(definition)) return;
            if (definition.RequiresStanding && Owner?.Pca?.PlayerState?.IsSit == true) return;
            NotifyBlocked(reason);
            return;
        }
        try
        {
            if (!ActionAssets.TryGet(definition, out var loaded)) { requests.Request(id); return; }
            if (definition.RuntimeSource is IPreparedActionSource prepared && !prepared.IsPrepared(Owner.Pca.Animator.gameObject)) { requests.Request(id); return; }
            if (loaded.Audio != null && loaded.Audio.loadState != AudioDataLoadState.Loaded) throw new Exception("Audio still loading; select the action again");
            if (request != requestRevision || !PrepareNativeIdle(id, request)) return;
            if (request != requestRevision || ActionRegistry.Get(id) != definition) return;
            var resolvedStats = NativeActionStats.Resolve(definition);
            int revision = ++session.Revision;
            var eventClip = loaded.Clip ?? (definition.RuntimeSource as IActionClipSource)?.Clip;
            var embedded = definition.UseClipEvents && eventClip != null ? ClipActionEvents.Read(eventClip) : Array.Empty<ActionEventDefinition>();
            session.Events = new ActionEventRunner(definition, embedded, loaded.Duration,
                () => IsCurrentEventPlayback(revision, loaded),
                message => Plugin.LogSource.LogWarning("id=" + id + " " + message));
            var poseTimer = System.Diagnostics.Stopwatch.StartNew();
            session.Pose = loaded.CreatePose(Owner.Pca.Animator.gameObject);
            if (session.Pose is IActionRootMotionPose moving && moving.HasRootMotion)
                session.RootMotion = new ActionRootMotion(Owner.Pca.CharacterController, Owner.transform, () => session.Revision == revision && Playing);
            session.Clock.Start(AudioSettings.dspTime, loaded.Audio == null ? 0 : definition.AudioDelaySeconds); session.AudioPendingWhenPaused = false;
            if (loaded.Audio != null)
            {
                if (session.Audio == null) { session.Audio = gameObject.AddComponent<AudioSource>(); session.Audio.playOnAwake = false; session.Audio.spatialBlend = 0; session.Audio.pitch = 1; session.Audio.loop = false; }
                session.Audio.Stop(); session.Audio.clip = loaded.Audio; session.Audio.outputAudioMixerGroup = null;
                var options = UnityEngine.Object.FindObjectOfType<UiOptionApplyer>();
                if (options?.audioMixer != null) { var groups = options.audioMixer.FindMatchingGroups("Bgm"); if (groups.Length > 0) session.Audio.outputAudioMixerGroup = groups[0]; }
                session.Action = loaded; UpdateVolume(); session.Audio.PlayScheduled(session.Clock.AudioStartDsp);
            }
            session.Stats = resolvedStats;
            session.Action = loaded; session.ExpectedPosition = Owner.transform.position; session.ApplyFrame = -1;
            if (Plugin.FollowCamera.Value && definition.FollowFaceCamera) try { session.FaceCamera.Begin(Owner); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_CAMERA_BEGIN_FAILED " + e.Message); }
            ActionPlaybackApi.Publish(definition, true);
            if (session.Revision != revision || !Playing) return;
            Plugin.LogSource.LogInfo("ACTION_STATS id=" + id + " kind=" + definition.NativeKind + " ecstasy=" + session.Stats.EcstasyPerSecond + " dokidoki=" + session.Stats.DokidokiAdd + " moisture=" + session.Stats.MoisturePerSecond + " stamina=" + session.Stats.StaminaPerSecond + " strange=" + session.Stats.Strange + " scale=" + session.Stats.StrangenessScale + " discomfort=" + session.Stats.Discomfort + " pee=" + session.Stats.CanStartPee + " poseMission=" + definition.PoseMission);
            Plugin.LogSource.LogInfo("ACTION_START id=" + id + " bones=" + session.Pose.BoneCount + " audioDelay=" + definition.AudioDelaySeconds + " poseMs=" + poseTimer.ElapsedMilliseconds + " rootMotion=" + (session.RootMotion != null));
        }
        catch (Exception e) { Stop("start failed"); Plugin.LogSource.LogError("ACTION_START_FAILED id=" + id + " " + e); }
    }

    [HideFromIl2Cpp]
    void NotifyBlocked(string reason)
    {
        if (reason == "stand first" || reason == "player unavailable") return;
        bool chinese = GameState.OptionData != null && ((int)GameState.OptionData.Language == 2 || (int)GameState.OptionData.Language == 3);
        string text = chinese ? reason switch { "handcuffed" => "佩戴手铐时无法执行这个动作。", "moving" => "请先停止移动。", "root motion blocked" => "动作走位受阻，已停止。", _ => "自定义动作暂时无法开始：" + reason } : "Custom action unavailable: " + reason;
        MessagePanelView.AddMessage(text, MessagePanelView.MessageType.Log);
    }

    [HideFromIl2Cpp]
    void UpdateVolume()
    {
        if (session.Audio == null || session.Action == null) return;
        float volume = Mathf.Clamp01(Plugin.Volume.Value) * session.Action.Definition.Volume;
        if (session.Audio.outputAudioMixerGroup == null && GameState.OptionData != null)
            volume *= Mathf.Clamp01(GameState.OptionData.SoundMaster100 / 100f * 1.995262f) * Mathf.Clamp01(GameState.OptionData.SoundBgm100 / 100f * 1.995262f);
        session.Audio.volume = volume;
    }

    [HideFromIl2Cpp]
    internal void Apply()
    {
        if (!Playing || session.ApplyFrame == Time.frameCount) return; session.ApplyFrame = Time.frameCount;
        SyncPause();
        var ui = InGameUiManager.Instance;
        string reason = Reason(Definition, Owner, ui != null && ui.IsOpenSomeUi);
        if (reason != null || (Owner.transform.position - session.ExpectedPosition).sqrMagnitude > 0.0004f) { StopWithDiagnostics("before pose", reason ?? "external displacement"); return; }
        double now = AudioSettings.dspTime;
        double eventTime = session.Clock.Elapsed(now);
        float time = (float)eventTime;
        if (!session.Clock.HasStarted(now)) return;
        int revision = session.Revision;
        float duration = session.Clock.Duration(session.Action.Duration, session.Action.Audio == null ? 0 : session.Action.Audio.length);
        bool finished = !session.Clock.Paused && !Definition.Loop && eventTime >= duration;
        float poseTime = Definition.Loop ? (float)(eventTime % session.Action.Duration) : Mathf.Min(time, session.Action.Duration);
        if (!session.Pose.Apply(poseTime)) { StopWithDiagnostics("pose apply", "avatar disappeared"); return; }
        if (!session.Clock.Paused) ActionPlaybackApi.SetRootCycleOffset(default);
        if (session.RootMotion != null && !session.Clock.Paused)
        {
            var mover = session.RootMotion;
            var moving = (IActionRootMotionPose)session.Pose;
            double loops = Definition.Loop ? Math.Floor(eventTime / session.Action.Duration) : 0;
            bool arrived = mover.Apply(moving.RootDisplacement, moving.LoopRootDisplacement, loops, out string motionReason);
            if (revision != session.Revision || !Playing) return;
            // Only a successful controller-owned step updates this guard. All
            // user input and unrelated position changes still cancel playback.
            if (!arrived) { StopWithDiagnostics("root motion", motionReason, mover.HasGoal ? mover.Goal : null); NotifyBlocked(motionReason); return; }
            session.ExpectedPosition = Owner.transform.position;
            var cycle = moving.LoopRootDisplacement;
            ActionPlaybackApi.SetRootCycleOffset(new Vector3((float)(cycle.x * loops), 0, (float)(cycle.z * loops)));
        }
        if (revision != session.Revision || !Playing) return;
        if (!session.Clock.Paused) session.Events.Advance(eventTime);
        // A native callback may have stopped this playback or changed state.
        if (revision != session.Revision || !Playing) return;
        reason = Reason(Definition, Owner, ui != null && ui.IsOpenSomeUi);
        if (reason != null) { StopWithDiagnostics("after events", reason); return; }
        if (Plugin.FollowCamera.Value && Definition.FollowFaceCamera)
            try { session.FaceCamera.RefreshAfterPose(); } catch (Exception e) { try { session.FaceCamera.End(); } catch { } Plugin.LogSource.LogWarning("ACTION_CAMERA_FAILED " + e.Message); }
        else session.FaceCamera.End();
        double audioTime = eventTime - (session.Clock.AudioStartDsp - session.Clock.MotionStartDsp);
        var audio = session.Action.Audio;
        double? audioPosition = audio != null && audioTime >= 0 && audioTime < audio.length ? audioTime : null;
        ActionPlaybackApi.PublishPose(Definition.Id, Definition.Loop ? poseTime : eventTime, audioPosition);
        if (revision == session.Revision && Playing && finished) Stop("finished", true);
    }

    [HideFromIl2Cpp]
    internal bool DeferFaceCamera(FaceCameraController camera) => Playing && Plugin.FollowCamera.Value && Definition.FollowFaceCamera && session.FaceCamera.ShouldDefer(camera);
    [HideFromIl2Cpp]
    internal IActionBodyPose BeginNativeBody(PlayerController player)
    {
        if (!Playing || Owner != player || !session.Clock.HasStarted(AudioSettings.dspTime)) return null;
        var body = session.Pose as IActionBodyPose;
        body?.BeginNativeBody();
        return body;
    }
    [HideFromIl2Cpp]
    internal IDisposable PreserveNativeCorrection(ActionBoneCorrection correction)
        => Playing ? (session.Pose as IActionBodyPose)?.PreserveNativeCorrection(correction) : null;
    [HideFromIl2Cpp]
    bool IsCurrentEventPlayback(int revision, LoadedAction action)
    {
        if (revision != session.Revision || session.Action != action || Owner == null || Owner != PlayerController.Instance || !Owner.gameObject.activeInHierarchy || Owner.Pca == null) return false;
        if (!session.Stopping)
        {
            var ui = InGameUiManager.Instance;
            string reason = Reason(action.Definition, Owner, ui != null && ui.IsOpenSomeUi);
            if (reason != null) { StopWithDiagnostics("event callback", reason); return false; }
        }
        return true;
    }
    [HideFromIl2Cpp]
    internal void Stop(string reason, bool completed = false)
    {
        if (session.Stopping) return;
        session.Stopping = true;
        requests.Clear(); requestRevision++; AbandonNativeExit(reason);
        var previous = session.Action; var previousPose = session.Pose; var previousEvents = session.Events; var previousRoot = session.RootMotion;
        try
        {
            try { if (session.Audio != null) session.Audio.Stop(); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_AUDIO_END_FAILED " + e.Message); }
            session.Clock.Stop();
            try { previousEvents?.Close(completed); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_EVENT_END_FAILED " + e.Message); }
            session.Action = null; session.Pose = null; session.Events = null; session.RootMotion = null; session.Revision++;
            try { previousPose?.Dispose(); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_POSE_END_FAILED " + e.Message); }
            try { session.FaceCamera.End(); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_CAMERA_END_FAILED " + e.Message); }
            if (previous != null)
            {
                ActionPlaybackApi.Publish(previous.Definition, false);
                Plugin.LogSource.LogInfo("ACTION_STOP id=" + previous.Definition.Id + " reason=" + reason + " rootDistance=" + (previousRoot?.Distance ?? 0).ToString("F3"));
            }
        }
        finally { session.Stopping = false; }
    }
    public void OnDisable() { requests.Clear(); Stop("disabled"); NativeAnimationReplacements.Release(); }
    public void OnDestroy() { requests.Clear(); Stop("destroyed"); ActionSamplingRigPool.Clear(); if (session.Audio != null) UnityEngine.Object.Destroy(session.Audio); ActionAssets.Clear(); ActionBundleImport.Clear(); NativeAnimationReplacements.Release(); ActionTargetApi.Target = null; ActionTargetApi.IsReady = false; if (Current == this) Current = null; }
}
