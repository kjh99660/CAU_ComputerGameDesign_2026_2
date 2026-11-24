using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The instant a blow lands. When the player hits: the round holds still for a few hundredths of a second (hit stop,
// heavier blows hold longer, the special longest) and the struck enemy flashes white. When the player is hit, in the
// same language: the player's white outline flashes red and then blinks for as long as the player cannot be hit again
// (together with the camera push, the screen glitch and the hurt sound, it reads as one moment).
// The hit stop itself is kept by CombatRoundRuntime, which owns the time scale. Flashes and the blink run on game
// time, so they stay bright through a hit stop and freeze with Pause; Reset clears them.
[DisallowMultipleComponent]
public sealed class CombatImpactFeedback : MonoBehaviour, IRoundResettable
{
    private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
    private static readonly int HitFlashColorId = Shader.PropertyToID("_HitFlashColor");

    private readonly Dictionary<EnemyAgent, Renderer[]> _renderers = new Dictionary<EnemyAgent, Renderer[]>();
    private readonly List<Flash> _flashes = new List<Flash>(32);
    private CombatVfxSettings _settings;
    private EnemyRuntime _enemies;
    private CombatRoundRuntime _combat;
    private RoundCombatStats _stats;
    private Animator _player;
    private PlayerOutline _outline;
    private SpecialHitWindow _special;
    private MaterialPropertyBlock _block;
    private int _tick;
    private bool _advancing;
    private int _hitCount;
    private float _hurtLeft;
    private float _hurtTotal;

    private struct Flash
    {
        public EnemyAgent Enemy;
        public Renderer[] Renderers;
        public float Remaining;
    }

    // Read by tests and tuning tools.
    public int ActiveFlashes => _flashes.Count;
    public float HurtRemaining => _hurtLeft;

    public void Configure(CombatVfxSettings settings, EnemyRuntime enemies, CombatRoundRuntime combat, Animator player)
    {
        _settings = settings;
        _enemies = enemies;
        _combat = combat;
        _player = player;
        _stats = combat != null ? combat.Stats : null;
        _hitCount = _stats != null ? _stats.HitCount : 0;
        _special = new SpecialHitWindow(_stats);
        _block = new MaterialPropertyBlock();
        if (_enemies != null) _enemies.EnemyStruck += OnStruck;
        if (_combat != null) _combat.SpecialActivated += OnSpecial;
        if (_stats != null) _stats.Changed += OnStatsChanged;
        enabled = false;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        _tick = notification.Tick;
        _advancing = notification.Current == GameState.Battle || notification.Current == GameState.Finishing;
        enabled = _advancing && (_flashes.Count > 0 || _hurtLeft > 0f);
    }

    private void OnStruck(EnemyAgent enemy, Vector3 direction, int tick)
    {
        if (_settings == null || tick != _tick || !_advancing || enemy == null) return;
        StartFlash(enemy);
        if (_special.IsSpecialHit || _combat == null) return;
        CombatHitWeight weight = CombatHitWeights.FromPlayer(_player);
        float seconds = weight == CombatHitWeight.Heavy ? _settings.hitStopHeavy
            : weight == CombatHitWeight.Medium ? _settings.hitStopMedium : _settings.hitStopLight;
        _combat.RequestHitStop(seconds, _settings.hitStopTimeScale);
    }

    private void OnSpecial(Vector3 center, float radius, int tick)
    {
        if (_settings == null || tick != _tick || !_advancing || _combat == null) return;
        _combat.RequestHitStop(_settings.hitStopSpecial, _settings.hitStopTimeScale);
    }

    // A hit the round accepted on the player (its score went down): flash the outline, then blink it.
    private void OnStatsChanged()
    {
        if (_stats == null) return;
        bool hurt = _stats.HitCount > _hitCount;
        _hitCount = _stats.HitCount;
        if (!hurt || _settings == null || _stats.Tick != _tick || !_advancing) return;
        if (_outline == null && _player != null) _outline = _player.GetComponent<PlayerOutline>();
        if (_outline == null) return;
        _hurtTotal = Mathf.Max(0.2f, _combat != null ? _combat.Settings.invulnerabilitySeconds : 0.8f);
        _hurtLeft = _hurtTotal;
        _outline.ShowColor(_settings.hurtOutlineColor);
        enabled = true;
    }

    private void StartFlash(EnemyAgent enemy)
    {
        if (_settings.hitFlashSeconds <= 0f) return;
        if (!_renderers.TryGetValue(enemy, out Renderer[] renderers))
        {
            renderers = enemy.GetComponentsInChildren<Renderer>(true);
            _renderers.Add(enemy, renderers);
        }
        for (int i = 0; i < _flashes.Count; i++)
        {
            if (_flashes[i].Enemy != enemy) continue;
            Flash again = _flashes[i];
            again.Remaining = _settings.hitFlashSeconds;
            _flashes[i] = again;
            Write(renderers, 1f);
            return;
        }
        _flashes.Add(new Flash { Enemy = enemy, Renderers = renderers, Remaining = _settings.hitFlashSeconds });
        Write(renderers, 1f);
        enabled = true;
    }

    private void LateUpdate()
    {
        if (!_advancing) return;
        float deltaTime = Time.deltaTime;
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            Flash flash = _flashes[i];
            flash.Remaining -= deltaTime;
            float strength = Mathf.Clamp01(flash.Remaining / Mathf.Max(0.0001f, _settings.hitFlashSeconds));
            Write(flash.Renderers, strength * strength);
            if (flash.Remaining <= 0f)
            {
                _flashes[i] = _flashes[_flashes.Count - 1];
                _flashes.RemoveAt(_flashes.Count - 1);
            }
            else
            {
                _flashes[i] = flash;
            }
        }
        if (_hurtLeft > 0f) Blink(deltaTime);
        if (_flashes.Count == 0 && _hurtLeft <= 0f) enabled = false;
    }

    // Solid hurt colour first, then blinking between it (fading) and the usual colour until the player can be hit again.
    private void Blink(float deltaTime)
    {
        _hurtLeft -= deltaTime;
        if (_outline == null) return;
        if (_hurtLeft <= 0f)
        {
            _hurtLeft = 0f;
            _outline.ShowRestColor();
            return;
        }
        float elapsed = _hurtTotal - _hurtLeft;
        if (elapsed < _settings.hurtSolidSeconds)
        {
            _outline.ShowColor(_settings.hurtOutlineColor);
            return;
        }
        bool lit = Mathf.Repeat(elapsed * _settings.hurtBlinkHz, 1f) < 0.5f;
        Color fading = Color.Lerp(_outline.RestColor, _settings.hurtOutlineColor, _hurtLeft / _hurtTotal);
        _outline.ShowColor(lit ? fading : _outline.RestColor);
    }

    // Keeps whatever else the enemy has in its property block (its tint) and changes only the flash.
    private void Write(Renderer[] renderers, float strength)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            renderer.GetPropertyBlock(_block);
            _block.SetFloat(HitFlashId, strength);
            _block.SetColor(HitFlashColorId, _settings.hitFlashColor);
            renderer.SetPropertyBlock(_block);
        }
    }

    public void StopProducing() => Clear();

    public IEnumerator ResetRound(int tick)
    {
        _tick = tick;
        Clear();
        yield break;
    }

    private void Clear()
    {
        for (int i = 0; i < _flashes.Count; i++) Write(_flashes[i].Renderers, 0f);
        _flashes.Clear();
        _hurtLeft = 0f;
        if (_outline != null) _outline.ShowRestColor();
        enabled = false;
    }

    private void OnDestroy()
    {
        if (_enemies != null) _enemies.EnemyStruck -= OnStruck;
        if (_combat != null) _combat.SpecialActivated -= OnSpecial;
        if (_stats != null) _stats.Changed -= OnStatsChanged;
        _special?.Dispose();
    }
}
