namespace Seleka2.Actions;
using System;

public sealed class MotionAudioClock
{
    // The paired WAVEFILE example starts its song at motion frame 160.
    public const double DefaultAudioDelay = 160 / 30d;
    public double MotionStartDsp { get; private set; }
    public double AudioStartDsp { get; private set; }
    public bool Active { get; private set; }
    public bool Paused { get; private set; }
    double pausedAt;
    public void Start(double now, double delay)
    {
        if (!double.IsFinite(now) || !double.IsFinite(delay) || delay < 0) throw new ArgumentException("Invalid audio schedule");
        MotionStartDsp = now + 0.1;
        AudioStartDsp = MotionStartDsp + delay;
        Active = true; Paused = false;
    }
    public bool SetPaused(double now, bool paused)
    {
        if (!double.IsFinite(now)) throw new ArgumentException("Invalid pause time");
        if (!Active || Paused == paused) return false;
        if (paused) pausedAt = now;
        else
        {
            if (now < pausedAt) throw new ArgumentException("Pause time moved backwards");
            double duration = now - pausedAt;
            MotionStartDsp += duration; AudioStartDsp += duration;
        }
        Paused = paused; return true;
    }
    double EffectiveTime(double now) => Paused ? pausedAt : now;
    public bool HasStarted(double now) => Active && EffectiveTime(now) >= MotionStartDsp;
    public double Elapsed(double now) => Active ? Math.Max(0, EffectiveTime(now) - MotionStartDsp) : 0;
    public float Position(double now) => (float)Elapsed(now);
    public float Duration(float motionSeconds, float audioSeconds) => (float)Math.Max(motionSeconds, AudioStartDsp - MotionStartDsp + audioSeconds);
    public void Stop() { Active = false; Paused = false; }
}
