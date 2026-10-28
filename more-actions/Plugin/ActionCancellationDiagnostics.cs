using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.IngameManager;
using ExposureUnnoticed2.Scripts.InGame;

namespace Seleka2.Actions;

// One snapshot on cancellation, never a per-frame trace. Numbers are world
// metres; named nonfinite values stay serializable so diagnostics cannot hide
// an invalid controller position or prevent cleanup.
internal static class ActionCancellationDiagnostics
{
    static readonly JsonSerializerOptions json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    static readonly InputManager.InputType[] inputs = {
        InputManager.InputType.Cancel, InputManager.InputType.OpenClothesA, InputManager.InputType.OpenClothesB,
        InputManager.InputType.CloseClothesA, InputManager.InputType.Crouch, InputManager.InputType.Interact,
        InputManager.InputType.DrinkWater, InputManager.InputType.EyeMask,
        InputManager.InputType.SystemMenu, InputManager.InputType.GameMenu
    };
    static float[] Coordinates(Vector3 v) => new[] { v.x, v.y, v.z };
    internal static void Record(string id, string phase, string reason, double seconds, Vector3 expected, Vector3 actual,
        bool ownerAvailable, bool movementInputBlocked, bool operationInputBlocked, bool uiOpen, bool paused,
        PlayerStateModel state, bool rootGoal)
    {
        var pressed = new List<string>();
        foreach (var input in inputs) if (InputManager.IsDownFrame(input)) pressed.Add(input.ToString());
        var delta = actual - expected;
        var temp = InGameManager.Instance?.TempInGameState;
        bool? handcuffed = GameState.GameStateData == null ? null : GameStateFacade.IsAttachSomeHandcuffs();
        Plugin.LogSource?.LogInfo("ACTION_CANCEL_DIAGNOSTIC " + JsonSerializer.Serialize(new {
            id, phase, reason, seconds, frame = Time.frameCount, inputs = pressed,
            ringShortcut = InputResolver.RingShortcutInput.ToString(), moveInput = Coordinates(InputManager.GetVector3(InputManager.InputType.Move)),
            ownerAvailable, expected = Coordinates(expected), actual = ownerAvailable ? Coordinates(actual) : null,
            delta = ownerAvailable ? Coordinates(delta) : null,
            distanceM = ownerAvailable ? (double?)Math.Sqrt(delta.sqrMagnitude) : null,
            horizontalM = ownerAvailable ? (double?)Math.Sqrt((double)delta.x * delta.x + (double)delta.z * delta.z) : null,
            positionReference = rootGoal ? "root motion goal" : "last accepted player position",
            exceedsTolerance = ownerAvailable && delta.sqrMagnitude > .0004f,
            externalDisplacement = !rootGoal && ownerAvailable && delta.sqrMagnitude > .0004f, toleranceM = .02,
            movementInputBlocked, operationInputBlocked, uiOpen, paused,
            playerState = state == null ? -1 : (int)state.CurrentState,
            nativeAction = state?.CurrentAction == null ? -1 : (int)state.CurrentAction.Type,
            ecstasy = state?.IsEcstasyMotion, crouching = state?.IsCrouch, sitting = state?.IsSit, handcuffed,
            sexMode = SexManager.Instance?.IsSexMode,
            gameOver = temp?.IsGameOver, performance = temp?.IsPlayingSomePerformance, timeStop = temp?.IsTimeStop
        }, json));
    }
}
