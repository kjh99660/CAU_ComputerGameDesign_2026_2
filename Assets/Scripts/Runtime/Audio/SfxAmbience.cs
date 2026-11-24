using UnityEngine;

// The rules of the ambience bed, kept apart from AudioSource so they can be tested without audio.
public static class SfxAmbienceRules
{
    // The volume the bed heads for in a state: the battle volume in Battle and Finishing, a share of it before and after
    // the battle, nothing while the game is still starting. Paused is not a target: the bed holds where it was (Holds).
    public static float TargetVolume(GameState state, float battleVolume, float outsideShare)
    {
        switch (state)
        {
            case GameState.Battle:
            case GameState.Finishing:
                return Mathf.Max(0f, battleVolume);
            case GameState.Ready:
            case GameState.IntroDialogue:
            case GameState.Countdown:
            case GameState.OutroDialogue:
            case GameState.Result:
                return Mathf.Max(0f, battleVolume) * Mathf.Clamp01(outsideShare);
            default:
                return 0f;
        }
    }

    public static bool Holds(GameState state) => state == GameState.Paused;

    // One step of a fade at a steady rate: silence to the battle volume takes fadeSeconds.
    public static float Step(float current, float target, float battleVolume, float fadeSeconds, float deltaTime)
    {
        float rate = Mathf.Max(0.0001f, battleVolume) / Mathf.Max(0.01f, fadeSeconds);
        return Mathf.MoveTowards(current, target, rate * Mathf.Max(0f, deltaTime));
    }
}

// A quiet seamless loop under the music: the city's machine hum. SfxRuntime owns it and passes on state changes and
// Retry. Pause holds the loop where it is (the AudioSource is paused; the time scale is not involved); Retry stops it,
// and the next state starts it again from its beginning. Every start fades in from silence and level changes fade in
// real time, so neither a start nor the Finishing slow motion clicks or drags. The loop point itself is seamless in
// the clip.
[DisallowMultipleComponent]
public sealed class SfxAmbience : MonoBehaviour
{
    private AudioSource _source;
    private float _battleVolume;
    private float _outsideShare;
    private float _fadeSeconds = 1f;
    private float _volume;
    private float _target;
    private bool _running;     // started and not stopped since (also while held)
    private bool _held;

    public AudioSource Source => _source;
    public AudioClip Clip => _source != null ? _source.clip : null;
    // Read by tests and tuning tools.
    public float Volume => _volume;
    public float TargetVolume => _target;
    public bool IsRunning => _running;
    public bool IsHeld => _held;
    public int Starts { get; private set; }
    public int Restarts { get; private set; }

    // battleVolume already includes the master volume.
    public void Configure(AudioClip clip, float battleVolume, float outsideShare, float fadeSeconds)
    {
        _battleVolume = Mathf.Max(0f, battleVolume);
        _outsideShare = Mathf.Clamp01(outsideShare);
        _fadeSeconds = Mathf.Max(0.01f, fadeSeconds);
        _source = gameObject.AddComponent<AudioSource>();
        _source.clip = clip;
        _source.loop = true;
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
        _source.dopplerLevel = 0f;
        _source.priority = 96;
        _source.volume = 0f;
        enabled = false;
    }

    public void ApplyState(GameState state)
    {
        if (_source == null || _source.clip == null)
            return;
        if (SfxAmbienceRules.Holds(state))
        {
            if (_running && !_held)
            {
                _source.Pause();
                _held = true;
            }
            enabled = false;
            return;
        }
        if (_held)
        {
            _source.UnPause();
            _held = false;
        }
        _target = SfxAmbienceRules.TargetVolume(state, _battleVolume, _outsideShare);
        if (_target > 0f && !_running)
        {
            _volume = 0f;
            _source.volume = 0f;
            _source.time = 0f;
            _source.Play();
            _running = true;
            Starts++;
        }
        enabled = _running && !Mathf.Approximately(_volume, _target);
    }

    // Retry: the round's bed stops (it is held by the Pause the Retry came from, or quiet behind the result) and the next
    // state starts it again from the beginning.
    public void Restart()
    {
        if (_source == null)
            return;
        _source.Stop();
        _source.time = 0f;
        _source.volume = 0f;
        _volume = 0f;
        _target = 0f;
        _running = false;
        _held = false;
        enabled = false;
        Restarts++;
    }

    private void Update()
    {
        if (!_running || _held)
        {
            enabled = false;
            return;
        }
        _volume = SfxAmbienceRules.Step(_volume, _target, _battleVolume, _fadeSeconds, FeedbackTime.RealDelta(Time.unscaledDeltaTime));
        _source.volume = _volume;
        if (_volume > 0f || _target > 0f)
        {
            if (Mathf.Approximately(_volume, _target)) enabled = false;
            return;
        }
        // Faded out completely: stop, and start from the beginning next time.
        _source.Stop();
        _running = false;
        enabled = false;
    }

    private void OnDestroy()
    {
        if (_source != null)
            _source.Stop();
    }
}
