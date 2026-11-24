using System.Collections;
using UnityEngine;

// Camera reaction to combat: a small zoom pulse on every swing, a stronger one when the swing lands and the
// strongest for the special attack. Only the field of view is touched; PlayerRuntime keeps placing the camera.
// The pulse runs on game time, so it freezes with Pause and slows down with the Finishing slow motion.
[DisallowMultipleComponent]
public sealed class CombatCameraFeedback : MonoBehaviour, IRoundResettable
{
    private Camera _camera;
    private CombatVfxSettings _settings;
    private EnemyRuntime _enemies;
    private CombatVfxRuntime _vfx;
    private CombatRoundRuntime _combat;
    private ZoomPulse _pulse;
    private float _restFieldOfView;
    private bool _hasRest;
    private int _tick;
    private bool _advancing;

    // Degrees currently taken off the field of view. Read by tests and tuning tools.
    public float ZoomDegrees => _pulse.Value;
    public bool IsPulsing => _pulse.Active;

    public void Configure(Camera camera, EnemyRuntime enemies, CombatVfxRuntime vfx, CombatRoundRuntime combat, CombatVfxSettings settings)
    {
        _camera = camera;
        _settings = settings;
        _enemies = enemies;
        _vfx = vfx;
        _combat = combat;
        if (_enemies != null) _enemies.HitPresented += OnHit;
        if (_vfx != null) _vfx.SlashPlayed += OnSwing;
        if (_combat != null) _combat.SpecialActivated += OnSpecial;
        enabled = false;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        _tick = notification.Tick;
        _advancing = notification.Current == GameState.Battle || notification.Current == GameState.Finishing;
        // Paused keeps the frame exactly as it was; every other non-combat state goes back to the resting view.
        if (!_advancing && notification.Current != GameState.Paused) Restore();
    }

    private void OnSwing(int tick) => Begin(tick, _settings != null ? _settings.swingZoomDegrees : 0f);
    private void OnHit(Vector3 position, Vector3 direction, int tick) => Begin(tick, _settings != null ? _settings.hitZoomDegrees : 0f);
    private void OnSpecial(Vector3 center, float radius, int tick) => Begin(tick, _settings != null ? _settings.specialZoomDegrees : 0f);

    private void Begin(int tick, float degrees)
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
        float degrees = _pulse.Advance(Time.deltaTime);
        if (_pulse.Active)
        {
            _camera.fieldOfView = Mathf.Clamp(_restFieldOfView - degrees, 10f, 150f);
            return;
        }
        _camera.fieldOfView = _restFieldOfView;
        enabled = false;
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
        enabled = false;
    }

    private void OnDestroy()
    {
        if (_enemies != null) _enemies.HitPresented -= OnHit;
        if (_vfx != null) _vfx.SlashPlayed -= OnSwing;
        if (_combat != null) _combat.SpecialActivated -= OnSpecial;
        Restore();
    }
}
