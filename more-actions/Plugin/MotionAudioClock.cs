namespace Seleka2.Actions;
using System;

public sealed class MotionAudioClock
{
    // The paired WAVEFILE example starts its song at motion frame 160.
    public const double DefaultAudioDelay = 160 / 30d;
    public double MotionStartDsp { get; private set; }
    public double AudioStartDsp { get; private set; }
    public bool Active { get; private set; }
    public void Start(double now, double delay)
    {
        if (!double.IsFinite(now) || !double.IsFinite(delay) || delay < 0) throw new ArgumentException("Invalid audio schedule");
        MotionStartDsp = now + 0.1;
        AudioStartDsp = MotionStartDsp + delay;
        Active = true;
    }
    public double Elapsed(double now) => Active ? Math.Max(0, now - MotionStartDsp) : 0;
    public float Position(double now) => (float)Elapsed(now);
    public float Duration(float motionSeconds, float audioSeconds) => (float)Math.Max(motionSeconds, AudioStartDsp - MotionStartDsp + audioSeconds);
    public void Stop() => Active = false;
}
