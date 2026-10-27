using UnityEngine;
namespace Seleka2.Actions;

internal sealed class LoadedAction
{
    internal ActionDefinition Definition;
    internal AnimationClip Clip;
    internal AudioClip Audio;
    internal bool IsValid => Definition.RuntimeSource != null || Clip != null;
    internal float Duration => Definition.RuntimeSource?.Duration ?? Clip.length;
    internal IActionPose CreatePose(GameObject target) => Definition.RuntimeSource?.CreatePose(target, Definition) ?? new ActionPose(target, this);
}
