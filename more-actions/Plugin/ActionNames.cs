using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;

namespace Seleka2.Actions;

internal static class ActionNames
{
    internal static int Language => GameState.OptionData==null ? 1 : Math.Clamp((int)GameState.OptionData.Language,0,4);
    internal static string Text(params string[] translations) => translations[Language];
    internal static string Clip(string name) => GameAnimationNames.Label(name);
    internal static string Display(ActionDefinition definition)
    {
        if(definition?.RuntimeSource is AnimatorActionSource source)
            return "["+source.Group+"] "+Clip(source.ClipName);
        return definition?.DisplayName??"";
    }
}
