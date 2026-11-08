using UnityEngine;

namespace SFM.Actions;

// State owned by one driver's playback lifecycle. Kept across action switches
// so request/event reentrancy guards and per-frame charging stay monotonic.
internal sealed class PlaybackSession
{
    internal LoadedAction Action;
    internal IActionPose Pose;
    internal ActionRootMotion RootMotion;
    internal ActionEventRunner Events;
    internal ActionStatValues Stats;
    internal readonly MotionAudioClock Clock = new();
    internal readonly MotionFaceCamera FaceCamera = new();
    internal readonly ActionStatFrames StatFrames = new();
    internal AudioSource Audio;
    internal Vector3 ExpectedPosition;
    internal int Revision, ApplyFrame = -1;
    internal bool Stopping, AudioPendingWhenPaused;
}
