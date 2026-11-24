using UnityEngine;

// Camera zoom punch: a quick push in, then a release that swings slightly past the resting field of view.
// Pure state with no scene access, so the curve can be unit-tested. Value is the number of degrees to take off the
// field of view (negative while it swings back out past the resting value).
public struct ZoomPulse
{
    private const float ReleaseShare = 0.6f;   // share of the way out spent going from the peak to the overshoot

    private float _from;
    private float _peak;
    private float _elapsed;
    private float _inSeconds;
    private float _outSeconds;
    private float _overshoot;

    public bool Active { get; private set; }
    public float Value { get; private set; }

    // Starts from wherever the previous pulse is, so repeated hits never snap the camera back first, and a weak
    // pulse arriving during a strong one does not cut the strong one short.
    public void Begin(float degrees, float inSeconds, float outSeconds, float overshoot)
    {
        if (degrees <= 0f)
            return;
        _from = Mathf.Max(0f, Value);
        _peak = Mathf.Max(degrees, _from);
        _inSeconds = Mathf.Max(0.001f, inSeconds);
        _outSeconds = Mathf.Max(0.001f, outSeconds);
        _overshoot = Mathf.Clamp01(overshoot);
        _elapsed = 0f;
        Active = true;
    }

    public float Advance(float deltaTime)
    {
        if (!Active)
            return Value = 0f;
        _elapsed += Mathf.Max(0f, deltaTime);
        if (_elapsed >= _inSeconds + _outSeconds)
        {
            Active = false;
            return Value = 0f;
        }
        return Value = Evaluate(_elapsed);
    }

    public void Clear()
    {
        Active = false;
        Value = 0f;
        _elapsed = 0f;
    }

    private float Evaluate(float time)
    {
        if (time <= _inSeconds)
        {
            float rise = 1f - time / _inSeconds;
            return Mathf.Lerp(_from, _peak, 1f - rise * rise);
        }
        float release = (time - _inSeconds) / _outSeconds;
        float low = -_overshoot * _peak;
        return release < ReleaseShare
            ? Mathf.Lerp(_peak, low, Mathf.SmoothStep(0f, 1f, release / ReleaseShare))
            : Mathf.Lerp(low, 0f, Mathf.SmoothStep(0f, 1f, (release - ReleaseShare) / (1f - ReleaseShare)));
    }
}
