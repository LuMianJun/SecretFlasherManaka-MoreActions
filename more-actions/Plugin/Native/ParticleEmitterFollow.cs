using UnityEngine;

namespace SFM.Actions;

// World-space particles are born in the engine's particle update, after the
// previous LateUpdate pose has been restored. Hold the last finished pose
// independently of that restore until the native particle system stops.
internal sealed class ParticleEmitterFollow
{
    ParticleSystem system;
    Transform parent, stableParent;
    Vector3 localPosition, localScale;
    Quaternion localRotation;

    internal void Capture(ParticleSystem emitter, Transform stableRoot)
    {
        if (system == emitter && emitter && emitter.transform.parent == stableParent) return;
        Release();
        if (!emitter || !stableRoot) return;
        var main = emitter.main;
        var scaling = main.scalingMode;
        if (main.simulationSpace != ParticleSystemSimulationSpace.World ||
            (scaling != ParticleSystemScalingMode.Local && scaling != ParticleSystemScalingMode.Shape)) return;
        var target = emitter.transform;
        parent = target.parent;
        if (!parent || parent == stableRoot) return;
        localPosition = target.localPosition;
        localRotation = target.localRotation;
        localScale = target.localScale;
        var position = target.position;
        var rotation = target.rotation;
        // Keep the emitter on the player for normal destruction. Local scaling
        // ignores the hierarchy; shape scaling uses it for emission geometry.
        stableParent = stableRoot;
        target.SetParent(stableParent, scaling == ParticleSystemScalingMode.Shape);
        system = emitter;
        target.SetPositionAndRotation(position, rotation);
    }

    internal void AfterPose()
    {
        if (!system || !parent) return;
        var target = system.transform;
        if (target.parent == stableParent)
            target.SetPositionAndRotation(parent.TransformPoint(localPosition), parent.rotation * localRotation);
    }

    internal void ReleaseIfStopped(ParticleSystem current)
    {
        if (!ReferenceEquals(system, null) && (!system || current != system || !system.isPlaying)) Release();
    }

    internal void Release()
    {
        var previous = system;
        system = null;
        if (previous && parent)
        {
            var target = previous.transform;
            if (target.parent == stableParent)
            {
                target.SetParent(parent, false);
                target.localPosition = localPosition;
                target.localRotation = localRotation;
                target.localScale = localScale;
            }
        }
        parent = stableParent = null;
    }
}
