using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Object3D.Camera;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.ObjectUI.InGame.MessagePanel;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using Seleka2.ModSettings;

namespace Seleka2.Actions;

[BepInPlugin(Guid, "Seleka2 More Actions", "0.5.3")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "local.seleka2.moreactions";
    internal static ManualLogSource LogSource;
    internal static ConfigEntry<float> Volume;
    internal static ConfigEntry<bool> FollowCamera;
    internal static ConfigEntry<bool> KeepGameExpressions;
    public override void Load()
    {
        LogSource = Log;
        var packs = Config.Bind("Actions", "Directory", "actions", "Action pack directory relative to this plugin.");
        Volume = Config.Bind("Audio", "Volume", 1f, "Master custom-action music volume, 0-1, multiplied by pack volume and native sound settings.");
        FollowCamera = Config.Bind("Camera", "FollowFaceCamera", true, "Track the original face camera after the custom pose; preserves native visibility.");
        KeepGameExpressions = Config.Bind("Face", "KeepGameExpressions", true, "Overlay action expressions on the native game expressions. An action's keepGameExpressions=false always disables this.");
        ActionFaceSettings.KeepGameExpressions = KeepGameExpressions.Value;
        KeepGameExpressions.SettingChanged += (_, _) => ActionFaceSettings.KeepGameExpressions = KeepGameExpressions.Value;
        ModOptions.Log = text => Log.LogWarning(text);
        ModOptions.Toggle("actions.gameexpressions", "[更多动作] 叠加原游戏表情", "[Actions] Keep game expressions", KeepGameExpressions, "默认在原游戏表情上叠加动作表情。关闭后只显示动作表情。动作 JSON 的 keepGameExpressions=false 优先，开启此项也不能覆盖。", "Overlay action expressions on the game's expressions. When off, show only action expressions. An action's keepGameExpressions=false takes priority over this setting.");
        ModOptions.Toggle("actions.facecamera", "[更多动作] 面部镜头跟随", "[Actions] Face camera tracking", FollowCamera, "自定义动作播放时，让原版面部摄像机跟随角色头部。面部摄像机显示仍由原版设置控制。", "Track the character's head during custom actions. Camera visibility follows the original settings.");
        ModOptions.Number("actions.volume", "[更多动作] 动作音乐音量", "[Actions] Music volume", Volume, 0, 100, 0.01f, "", "0 为静音，1 为完整音量；同时受动作配置与游戏主音量、背景音乐音量影响。", "0 is silent; 1 is full volume. Pack volume and native master/BGM volume also apply.");
        RingActionMenu.LastActions = Config.Bind("Shortcuts", "LastActions", "", "Custom action IDs for the eight ring shortcuts, separated by commas; stored outside the game save.");
        NativeActionEvents.Register();
        ActionDriver.Current = AddComponent<ActionDriver>();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        int count = ActionRegistry.LoadPacks(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), packs.Value)), Guid);
        Log.LogInfo("MORE_ACTIONS_READY version=0.5.3 packs=" + count + " ring=true nativeRestrictions=true runtimeSources=true settings=true");
    }
}

public sealed class ActionDriver : MonoBehaviour
{
    public ActionDriver(IntPtr pointer) : base(pointer) { }
    internal static ActionDriver Current;
    internal PlayerController Owner { get; private set; }
    internal ActionDefinition Definition => running?.Definition;
    internal bool Playing => running != null;
    readonly ActionRequestQueue requests = new();
    readonly MotionAudioClock clock = new();
    readonly MotionFaceCamera faceCamera = new();
    LoadedAction running;
    IActionPose pose;
    ActionRootMotion rootMotion;
    ActionEventRunner events;
    int eventRevision;
    bool stopping;
    AudioSource audio;
    Vector3 expectedPosition;
    int tickFrame = -1, applyFrame = -1;
    internal bool CheckingAvailability;

    public void Update()
    {
        ActionAssets.Tick();
        var animator = PlayerController.Instance?.Pca?.Animator;
        ActionTargetApi.Target = animator == null ? null : animator.gameObject;
        ActionTargetApi.IsReady = animator != null && animator.enabled && animator.isInitialized;
        if (Owner != null && Owner != PlayerController.Instance) { Stop("scene/player changed"); requests.Clear(); Owner = null; }
        if (Playing && Owner == null) { Stop("player destroyed"); requests.Clear(); }
    }

    internal void Request(string id) => requests.Request(id);
    internal bool Owns(PlayerStateModel state) => Playing && Owner != null && Owner.Pca?.PlayerState?.Pointer == state?.Pointer;
    internal bool Owns(PlayerFacade facade) => Playing && Owner != null && facade?.pca?.PlayerState?.Pointer == Owner.Pca?.PlayerState?.Pointer;
    internal bool Owns(PlayerAction action) => Playing && Owner != null && Owner.Pca?.PlayerAction?.Pointer == action?.Pointer;

    internal string Reason(ActionDefinition definition, PlayerController player, bool uiOpen)
    {
        var state = player == null ? null : player.Pca?.PlayerState;
        var game = InGameManager.Instance;
        var temp = game == null ? null : game.TempInGameState;
        var availability = new ActionAvailability(state?.CurrentAction != null, state != null && (int)state.CurrentState == 0,
            state?.CurrentAction != null && (int)state.CurrentAction.Type != -1, state != null && state.IsEcstasyMotion,
            state != null && state.IsCrouch, state != null && state.IsSit,
            GameState.GameStateData != null && GameStateFacade.IsAttachSomeHandcuffs(), SexManager.Instance != null && SexManager.Instance.IsSexMode,
            InputManager.GetVector3(InputManager.InputType.Move).sqrMagnitude > 0.0001f, uiOpen,
            player == null || !player.gameObject.activeInHierarchy || temp == null || temp.IsGameOver || temp.IsPlayingSomePerformance || temp.IsTimeStop);
        var reason = ActionPolicy.BlockReason(definition, availability);
        if (reason != null) return reason;
        // Call the original hand gate with only the pack's declared skips. The
        // lease patch is bypassed solely during this owner's availability check.
        CheckingAvailability = true;
        try { return PlayerFacade.Instance.CanUseHand(false, !definition.RequiresHands, false, false, !definition.RequiresStanding) ? null : "native hand/action gate"; }
        finally { CheckingAvailability = false; }
    }

    internal void BeforeNativeUpdate(PlayerController player)
    {
        if (!Playing || Owner != player) return;
        (pose as IActionBodyPose)?.RestoreNativeBody();
        (pose as IActionFacePose)?.RestoreNativeFace();
        var ui = InGameUiManager.Instance;
        var reason = Reason(Definition, player, ui != null && ui.IsOpenSomeUi);
        bool nativeInput = InputResolver.RingShortcutInput != RingMenuParentView.RingItemType.None
            || InputManager.IsDownFrame(InputManager.InputType.OpenClothesA) || InputManager.IsDownFrame(InputManager.InputType.OpenClothesB)
            || InputManager.IsDownFrame(InputManager.InputType.CloseClothesA) || InputManager.IsDownFrame(InputManager.InputType.Crouch)
            || InputManager.IsDownFrame(InputManager.InputType.Interact) || InputManager.IsDownFrame(InputManager.InputType.DrinkWater)
            || InputManager.IsDownFrame(InputManager.InputType.EyeMask);
        if (reason != null || nativeInput || InputManager.IsDownFrame(InputManager.InputType.Cancel) || (player.transform.position - expectedPosition).sqrMagnitude > 0.0004f)
            Stop(reason ?? "native input/cancel");
    }

    internal void Tick(PlayerController player)
    {
        if (tickFrame == Time.frameCount) return; tickFrame = Time.frameCount;
        if (Owner != player) { Stop("player changed"); requests.Clear(); Owner = player; }
        if (InputManager.IsDownFrame(InputManager.InputType.Cancel)) { requests.Clear(); Stop("cancel"); return; }
        var ui = InGameUiManager.Instance;
        var ring = RingMenuParentView.Instance;
        var request = requests.Take(ring != null && ring.IsOpenRing, ui != null && ui.IsOpenSomeUi);
        if (request != null)
        {
            if (Definition?.Id == request) Stop("selected again");
            else { Stop("switch action"); Start(request); }
        }
        if (!Playing) return;
        var reason = Reason(Definition, Owner, ui != null && ui.IsOpenSomeUi);
        if (reason != null || ActionRegistry.Get(Definition.Id) != Definition || InputManager.IsDownFrame(InputManager.InputType.Cancel)) { Stop(reason ?? "action removed/cancel"); return; }
        UpdateVolume();
    }

    void Start(string id)
    {
        var definition = ActionRegistry.Get(id);
        if (definition == null) return;
        string reason = Reason(definition, Owner, InGameUiManager.Instance != null && InGameUiManager.Instance.IsOpenSomeUi);
        if (reason != null) { NotifyBlocked(reason); return; }
        try
        {
            if (!ActionAssets.TryGet(definition, out var loaded)) { requests.Request(id); return; }
            if (definition.RuntimeSource is IPreparedActionSource prepared && !prepared.IsPrepared(Owner.Pca.Animator.gameObject)) { requests.Request(id); return; }
            if (loaded.Audio != null && loaded.Audio.loadState != AudioDataLoadState.Loaded) throw new Exception("Audio still loading; select the action again");
            int revision = ++eventRevision;
            var embedded = definition.UseClipEvents && loaded.Clip != null ? ClipActionEvents.Read(loaded.Clip) : Array.Empty<ActionEventDefinition>();
            events = new ActionEventRunner(definition, embedded, loaded.Duration,
                () => IsCurrentEventPlayback(revision, loaded),
                message => Plugin.LogSource.LogWarning("id=" + id + " " + message));
            var poseTimer = System.Diagnostics.Stopwatch.StartNew();
            pose = loaded.CreatePose(Owner.Pca.Animator.gameObject);
            if (pose is IActionRootMotionPose moving && moving.HasRootMotion)
                rootMotion = new ActionRootMotion(Owner.Pca.CharacterController, Owner.transform, () => eventRevision == revision && Playing);
            clock.Start(AudioSettings.dspTime, loaded.Audio == null ? 0 : definition.AudioDelaySeconds);
            if (loaded.Audio != null)
            {
                if (audio == null) { audio = gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0; audio.pitch = 1; audio.loop = false; }
                audio.Stop(); audio.clip = loaded.Audio; audio.outputAudioMixerGroup = null;
                var options = UnityEngine.Object.FindObjectOfType<UiOptionApplyer>();
                if (options?.audioMixer != null) { var groups = options.audioMixer.FindMatchingGroups("Bgm"); if (groups.Length > 0) audio.outputAudioMixerGroup = groups[0]; }
                running = loaded; UpdateVolume(); audio.PlayScheduled(clock.AudioStartDsp);
            }
            running = loaded; expectedPosition = Owner.transform.position; applyFrame = -1;
            if (Plugin.FollowCamera.Value && definition.FollowFaceCamera) try { faceCamera.Begin(Owner); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_CAMERA_BEGIN_FAILED " + e.Message); }
            ActionPlaybackApi.Publish(definition, true);
            if (eventRevision != revision || !Playing) return;
            Plugin.LogSource.LogInfo("ACTION_START id=" + id + " bones=" + pose.BoneCount + " audioDelay=" + definition.AudioDelaySeconds + " poseMs=" + poseTimer.ElapsedMilliseconds + " rootMotion=" + (rootMotion != null));
        }
        catch (Exception e) { Stop("start failed"); Plugin.LogSource.LogError("ACTION_START_FAILED id=" + id + " " + e); NotifyBlocked(e.Message); }
    }

    void NotifyBlocked(string reason)
    {
        bool chinese = GameState.OptionData != null && ((int)GameState.OptionData.Language == 2 || (int)GameState.OptionData.Language == 3);
        string text = chinese ? reason switch { "handcuffed" => "佩戴手铐时无法执行这个动作。", "stand first" => "请先站立，再执行这个动作。", "moving" => "请先停止移动。", "root motion blocked" => "动作走位受阻，已停止。", _ => "自定义动作暂时无法开始：" + reason } : "Custom action unavailable: " + reason;
        MessagePanelView.AddMessage(text, MessagePanelView.MessageType.Log);
    }

    void UpdateVolume()
    {
        if (audio == null || running == null) return;
        float volume = Mathf.Clamp01(Plugin.Volume.Value) * running.Definition.Volume;
        if (audio.outputAudioMixerGroup == null && GameState.OptionData != null)
            volume *= Mathf.Clamp01(GameState.OptionData.SoundMaster100 / 100f * 1.995262f) * Mathf.Clamp01(GameState.OptionData.SoundBgm100 / 100f * 1.995262f);
        audio.volume = volume;
    }

    internal void Apply()
    {
        if (!Playing || applyFrame == Time.frameCount) return; applyFrame = Time.frameCount;
        var ui = InGameUiManager.Instance;
        string reason = Reason(Definition, Owner, ui != null && ui.IsOpenSomeUi);
        if (reason != null || (Owner.transform.position - expectedPosition).sqrMagnitude > 0.0004f) { Stop(reason ?? "moved"); return; }
        double eventTime = clock.Elapsed(AudioSettings.dspTime);
        float time = (float)eventTime;
        if (AudioSettings.dspTime < clock.MotionStartDsp) return;
        int revision = eventRevision;
        float duration = clock.Duration(running.Duration, running.Audio == null ? 0 : running.Audio.length);
        bool finished = !Definition.Loop && eventTime >= duration;
        float poseTime = Definition.Loop ? (float)(eventTime % running.Duration) : Mathf.Min(time, running.Duration);
        if (!pose.Apply(poseTime)) { Stop("avatar disappeared"); return; }
        ActionPlaybackApi.SetRootCycleOffset(default);
        if (rootMotion != null)
        {
            var mover = rootMotion;
            var moving = (IActionRootMotionPose)pose;
            double loops = Definition.Loop ? Math.Floor(eventTime / running.Duration) : 0;
            bool arrived = mover.Apply(moving.RootDisplacement, moving.LoopRootDisplacement, loops, out string motionReason);
            if (revision != eventRevision || !Playing) return;
            // Only a successful controller-owned step updates this guard. All
            // user input and unrelated position changes still cancel playback.
            expectedPosition = Owner.transform.position;
            if (!arrived) { Stop(motionReason); NotifyBlocked(motionReason); return; }
            var cycle = moving.LoopRootDisplacement;
            ActionPlaybackApi.SetRootCycleOffset(new Vector3((float)(cycle.x * loops), 0, (float)(cycle.z * loops)));
        }
        if (revision != eventRevision || !Playing) return;
        events.Advance(eventTime);
        // A native callback may have stopped this playback or changed state.
        if (revision != eventRevision || !Playing) return;
        reason = Reason(Definition, Owner, ui != null && ui.IsOpenSomeUi);
        if (reason != null) { Stop(reason); return; }
        if (Plugin.FollowCamera.Value && Definition.FollowFaceCamera)
            try { faceCamera.RefreshAfterPose(); } catch (Exception e) { try { faceCamera.End(); } catch { } Plugin.LogSource.LogWarning("ACTION_CAMERA_FAILED " + e.Message); }
        else faceCamera.End();
        ActionPlaybackApi.PublishPose(Definition.Id, Definition.Loop ? poseTime : eventTime);
        if (revision == eventRevision && Playing && finished) Stop("finished", true);
    }

    internal bool DeferFaceCamera(FaceCameraController camera) => Playing && Plugin.FollowCamera.Value && Definition.FollowFaceCamera && faceCamera.ShouldDefer(camera);
    internal IActionBodyPose BeginNativeBody(PlayerController player)
    {
        if (!Playing || Owner != player || AudioSettings.dspTime < clock.MotionStartDsp) return null;
        var body = pose as IActionBodyPose;
        body?.BeginNativeBody();
        return body;
    }
    internal IDisposable PreserveNativeCorrection(ActionBoneCorrection correction)
        => Playing ? (pose as IActionBodyPose)?.PreserveNativeCorrection(correction) : null;
    internal bool SuppressKneeOffset => Playing && ((pose as IActionBodyPose)?.SuppressKneeOffset ?? false);
    bool IsCurrentEventPlayback(int revision, LoadedAction action)
    {
        if (revision != eventRevision || running != action || Owner == null || Owner != PlayerController.Instance || !Owner.gameObject.activeInHierarchy || Owner.Pca == null) return false;
        if (!stopping)
        {
            var ui = InGameUiManager.Instance;
            string reason = Reason(action.Definition, Owner, ui != null && ui.IsOpenSomeUi);
            if (reason != null) { Stop(reason); return false; }
        }
        return true;
    }
    internal void Stop(string reason, bool completed = false)
    {
        if (stopping) return;
        stopping = true;
        var previous = running; var previousPose = pose; var previousEvents = events; var previousRoot = rootMotion;
        try
        {
            try { if (audio != null) audio.Stop(); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_AUDIO_END_FAILED " + e.Message); }
            clock.Stop();
            try { previousEvents?.Close(completed); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_EVENT_END_FAILED " + e.Message); }
            running = null; pose = null; events = null; rootMotion = null; eventRevision++;
            try { previousPose?.Dispose(); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_POSE_END_FAILED " + e.Message); }
            try { faceCamera.End(); } catch (Exception e) { Plugin.LogSource.LogWarning("ACTION_CAMERA_END_FAILED " + e.Message); }
            if (previous != null)
            {
                ActionPlaybackApi.Publish(previous.Definition, false);
                Plugin.LogSource.LogInfo("ACTION_STOP id=" + previous.Definition.Id + " reason=" + reason + " rootDistance=" + (previousRoot?.Distance ?? 0).ToString("F3"));
            }
        }
        finally { stopping = false; }
    }
    public void OnDisable() { requests.Clear(); Stop("disabled"); }
    public void OnDestroy() { requests.Clear(); Stop("destroyed"); if (audio != null) UnityEngine.Object.Destroy(audio); ActionAssets.Clear(); ActionTargetApi.Target = null; ActionTargetApi.IsReady = false; if (Current == this) Current = null; }
}

[HarmonyPatch(typeof(PlayerController), "OnUpdate")]
static class ActionTickPatch
{
    static void Prefix(PlayerController __instance) { try { ActionDriver.Current?.BeforeNativeUpdate(__instance); } catch (Exception e) { Plugin.LogSource.LogError(e); ActionDriver.Current?.Stop("pre-update error"); } }
    static void Postfix(PlayerController __instance) { try { ActionDriver.Current?.Tick(__instance); } catch (Exception e) { Plugin.LogSource.LogError(e); ActionDriver.Current?.Stop("update error"); } }
}
[HarmonyPatch(typeof(EventMethodManager), "LateUpdate")]
static class ActionOutputPatch
{
    static void Postfix()
    {
        string id = ActionDriver.Current?.Definition?.Id;
        try { ActionDriver.Current?.Apply(); }
        catch (Exception e) { Plugin.LogSource.LogError("ACTION_OUTPUT_FAILED id=" + id + " " + e); ActionDriver.Current?.Stop("pose/event error"); }
    }
}
[HarmonyPatch(typeof(FaceCameraController), "OnLateUpdate")]
static class FaceCameraOrderPatch { static bool Prefix(FaceCameraController __instance) => !(ActionDriver.Current?.DeferFaceCamera(__instance) ?? false); }
