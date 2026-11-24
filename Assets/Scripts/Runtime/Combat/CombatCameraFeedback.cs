using System.Collections;
using UnityEngine;

// Camera reaction to combat:
//   - a field-of-view pulse on every swing, a stronger one when the swing lands and the strongest for the special;
//   - a shake that grows with the weight of the blow (light hits barely move the view, the charged attack and the
//     special shake it hard, a hit on the player shakes it too), built from smooth noise and "trauma": every event
//     adds trauma, the shake is trauma squared and trauma drains away;
//   - a kick: the view jolts a little toward the blow and springs back.
// PlayerRuntime keeps placing the camera; the shake and kick are added on top after it, and the true pose is put
// back when they are over. The FOV pulse runs on game time (it freezes with Pause and a hit stop); the shake and kick
// run on real time so they keep moving through a hit stop, and hold still while paused.
[DisallowMultipleComponent]
public sealed class CombatCameraFeedback : MonoBehaviour, IRoundResettable
{
    private Camera _camera;
    private CombatVfxSettings _settings;
    private EnemyRuntime _enemies;
    private CombatVfxRuntime _vfx;
    private CombatRoundRuntime _combat;
    private RoundCombatStats _stats;
    private Animator _player;
    private SpecialHitWindow _special;
    private ZoomPulse _pulse;
    private float _restFieldOfView;
    private bool _hasRest;
    private int _tick;
    private bool _advancing;
    private int _hitCount;

    private float _trauma;
    private float _shakeTime;
    private Vector3 _kick;
    private Vector3 _kickVelocity;
    private bool _applied;
    private Vector3 _basePosition;
    private Quaternion _baseRotation = Quaternion.identity;
    private Vector3 _appliedPosition;
    private Quaternion _appliedRotation = Quaternion.identity;
    private int _traumaFrame = -1;
    private float _frameTrauma;
    private Vector3 _struckFrom;
    private int _struckFrame = -1;

    // Degrees currently taken off the field of view. Read by tests and tuning tools.
    public float ZoomDegrees => _pulse.Value;
    public bool IsPulsing => _pulse.Active;
    // Shake state (0-1) and the offset the camera carries this frame. Read by tests and tuning tools.
    public float Trauma => _trauma;
    public bool IsShaking => _applied;
    public Vector3 ShakeOffset => _applied ? _appliedPosition - _basePosition : Vector3.zero;

    public void Configure(Camera camera, EnemyRuntime enemies, CombatVfxRuntime vfx, CombatRoundRuntime combat,
        CombatVfxSettings settings, Animator player = null)
    {
        _camera = camera;
        _settings = settings;
        _enemies = enemies;
        _vfx = vfx;
        _combat = combat;
        _player = player;
        _stats = combat != null ? combat.Stats : null;
        _special = new SpecialHitWindow(_stats);
        _hitCount = _stats != null ? _stats.HitCount : 0;
        if (_enemies != null)
        {
            _enemies.HitPresented += OnHit;
            _enemies.PlayerStruckFrom += OnStruckFrom;
        }
        if (_vfx != null) _vfx.SlashPlayed += OnSwing;
        if (_combat != null) _combat.SpecialActivated += OnSpecial;
        if (_stats != null) _stats.Changed += OnStatsChanged;
        enabled = false;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        _tick = notification.Tick;
        _advancing = notification.Current == GameState.Battle || notification.Current == GameState.Finishing;
        // Paused keeps the frame exactly as it was; every other non-combat state goes back to the resting view.
        if (!_advancing && notification.Current != GameState.Paused) Restore();
        else if (_advancing && (_pulse.Active || _applied || _trauma > 0f)) enabled = true;
    }

    // Adds shake. Several hits in one frame count as the strongest of them, not their sum.
    public void AddTrauma(float amount)
    {
        if (_settings != null) amount *= _settings.cameraShakeAmount;
        if (!_advancing || amount <= 0f) return;
        if (_traumaFrame == Time.frameCount)
        {
            if (amount <= _frameTrauma) return;
            _trauma = Mathf.Max(0f, _trauma - _frameTrauma);
        }
        _traumaFrame = Time.frameCount;
        _frameTrauma = amount;
        _trauma = Mathf.Clamp01(_trauma + amount);
        enabled = true;
    }

    // Jolts the view this many metres along a direction; the stronger kick wins.
    public void Kick(Vector3 direction, float metres)
    {
        if (_settings != null) metres *= _settings.cameraShakeAmount;
        if (!_advancing || metres <= 0f || direction.sqrMagnitude < 1e-6f) return;
        Vector3 kick = direction.normalized * metres;
        if (kick.sqrMagnitude <= _kick.sqrMagnitude) return;
        _kick = kick;
        _kickVelocity = Vector3.zero;
        enabled = true;
    }

    private void OnSwing(int tick) => BeginPulse(tick, _settings != null ? _settings.swingZoomDegrees : 0f);

    private void OnHit(Vector3 position, Vector3 direction, int tick)
    {
        if (_settings == null || tick != _tick) return;
        // The special's own hits are answered once by OnSpecial.
        if (_special.IsSpecialHit) return;
        CombatHitWeight weight = CombatHitWeights.FromPlayer(_player);
        float zoom = weight == CombatHitWeight.Heavy ? 1.6f : weight == CombatHitWeight.Medium ? 1.2f : 1f;
        BeginPulse(tick, _settings.hitZoomDegrees * zoom);
        AddTrauma(weight == CombatHitWeight.Heavy ? _settings.shakeHeavy
            : weight == CombatHitWeight.Medium ? _settings.shakeMedium : _settings.shakeLight);
        Kick(direction, weight == CombatHitWeight.Heavy ? _settings.kickHeavy : _settings.kickLight);
    }

    private void OnSpecial(Vector3 center, float radius, int tick)
    {
        if (_settings == null || tick != _tick) return;
        BeginPulse(tick, _settings.specialZoomDegrees);
        AddTrauma(_settings.shakeSpecial);
        Kick(Vector3.down, _settings.kickSpecial);
    }

    // Where the strike that is reaching the player came from (raised just before the round decides on the hit).
    private void OnStruckFrom(Vector3 attacker, int tick)
    {
        _struckFrom = attacker;
        _struckFrame = Time.frameCount;
    }

    // A hit the round accepted on the player (the score went down): shake, and push the view away from the blow.
    private void OnStatsChanged()
    {
        if (_stats == null) return;
        bool hurt = _stats.HitCount > _hitCount;
        _hitCount = _stats.HitCount;
        if (!hurt || _settings == null || _stats.Tick != _tick || _camera == null) return;
        AddTrauma(_settings.shakePlayerHurt);
        Vector3 away = -_camera.transform.forward;
        if (_struckFrame == Time.frameCount)
        {
            Vector3 fromBlow = _camera.transform.position - _struckFrom;
            fromBlow.y = 0f;
            if (fromBlow.sqrMagnitude > 0.0001f) away = fromBlow;
        }
        Kick(away, _settings.kickHeavy);
    }

    private void BeginPulse(int tick, float degrees)
    {
        if (tick != _tick || !_advancing || _camera == null || degrees <= 0f) return;
        if (!_pulse.Active)
        {
            _restFieldOfView = _camera.fieldOfView;
            _hasRest = true;
        }
        _pulse.Begin(degrees, _settings.zoomInSeconds, _settings.zoomOutSeconds, _settings.zoomOvershoot);
        enabled = true;
    }

    private void LateUpdate()
    {
        if (!_advancing || _camera == null) return;
        if (_pulse.Active)
        {
            float degrees = _pulse.Advance(Time.deltaTime);
            _camera.fieldOfView = _pulse.Active ? Mathf.Clamp(_restFieldOfView - degrees, 10f, 150f) : _restFieldOfView;
        }
        bool moving = _settings != null && Shake(FeedbackTime.RealDelta(Time.unscaledDeltaTime));
        if (!_pulse.Active && !moving) enabled = false;
    }

    private bool Shake(float deltaTime)
    {
        Transform view = _camera.transform;
        // PlayerRuntime places the camera from its own state. If it has not moved the camera since our last offset,
        // the camera still carries that offset and the true pose is the one remembered.
        if (!_applied || view.position != _appliedPosition || view.rotation != _appliedRotation)
        {
            _basePosition = view.position;
            _baseRotation = view.rotation;
        }
        _trauma = Mathf.Max(0f, _trauma - _settings.shakeDecay * deltaTime);
        if (_kick != Vector3.zero)
        {
            _kick = Vector3.SmoothDamp(_kick, Vector3.zero, ref _kickVelocity, _settings.kickReturnSeconds, Mathf.Infinity, deltaTime);
            if (_kick.sqrMagnitude < 1e-8f)
            {
                _kick = Vector3.zero;
                _kickVelocity = Vector3.zero;
            }
        }
        if (_trauma <= 0f && _kick == Vector3.zero)
        {
            PutBack();
            return false;
        }
        _shakeTime += deltaTime * _settings.shakeFrequency;
        float shake = _trauma * _trauma;
        float move = _settings.shakeMaxOffset * shake, turn = _settings.shakeMaxAngle * shake;
        Vector3 offset = _baseRotation * new Vector3(Noise(0.1f), Noise(17.3f), 0f) * move + _kick;
        Quaternion tilt = Quaternion.Euler(Noise(31.7f) * turn, Noise(47.9f) * turn, Noise(63.1f) * turn * 1.5f);
        _appliedPosition = _basePosition + offset;
        _appliedRotation = _baseRotation * tilt;
        view.SetPositionAndRotation(_appliedPosition, _appliedRotation);
        _applied = true;
        return true;
    }

    private float Noise(float seed) => Mathf.PerlinNoise(seed, _shakeTime) * 2f - 1f;

    private void PutBack()
    {
        if (!_applied || _camera == null) return;
        Transform view = _camera.transform;
        if (view.position == _appliedPosition && view.rotation == _appliedRotation)
            view.SetPositionAndRotation(_basePosition, _baseRotation);
        _applied = false;
    }

    public void StopProducing() => Restore();

    public IEnumerator ResetRound(int tick)
    {
        _tick = tick;
        Restore();
        yield break;
    }

    private void Restore()
    {
        _pulse.Clear();
        if (_hasRest && _camera != null) _camera.fieldOfView = _restFieldOfView;
        _trauma = 0f;
        _kick = Vector3.zero;
        _kickVelocity = Vector3.zero;
        PutBack();
        enabled = false;
    }

    private void OnDestroy()
    {
        if (_enemies != null)
        {
            _enemies.HitPresented -= OnHit;
            _enemies.PlayerStruckFrom -= OnStruckFrom;
        }
        if (_vfx != null) _vfx.SlashPlayed -= OnSwing;
        if (_combat != null) _combat.SpecialActivated -= OnSpecial;
        if (_stats != null) _stats.Changed -= OnStatsChanged;
        _special?.Dispose();
        Restore();
    }
}
