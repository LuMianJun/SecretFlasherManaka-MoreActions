using System;

namespace SFM.Actions;

public readonly record struct ActionAvailability(bool Initialized, bool Idle, bool NativeAction, bool Ecstasy, bool Crouching, bool Sitting, bool Handcuffed, bool SexMode, bool Moving, bool UiOpen, bool GameUnavailable);

public static class ActionPolicy
{
    public static string BlockReason(ActionDefinition action, ActionAvailability state)
    {
        if (!state.Initialized || state.GameUnavailable) return "player unavailable";
        if (!state.Idle || state.NativeAction || state.Ecstasy || state.SexMode) return "native action/state active";
        if (action.RequiresHands && state.Handcuffed) return "handcuffed";
        if (action.RequiresStanding && (state.Crouching || state.Sitting)) return "stand first";
        if (state.Moving) return "moving";
        if (state.UiOpen) return "menu open";
        return null;
    }

    public static bool InMask(string path, ActionBodyMask mask)
    {
        bool leg = path.Contains("/UpperLeg_L", StringComparison.Ordinal) || path.Contains("/UpperLeg_R", StringComparison.Ordinal);
        return mask == ActionBodyMask.FullBody || (mask == ActionBodyMask.LowerBody ? leg : !leg && path != "Armature/Hips");
    }

    public static bool WritesBone(ActionDefinition action, string path)
        => InMask(path, action.BodyMask) && (action.BonePaths.Length == 0 || Array.IndexOf(action.BonePaths, path) >= 0);
}
