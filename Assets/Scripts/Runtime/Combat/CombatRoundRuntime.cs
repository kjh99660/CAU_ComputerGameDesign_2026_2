using System;
using System.Collections;
using UnityEngine;

// Adapts combat results to round lifecycle and existing UI presenters.
[DisallowMultipleComponent]
public sealed class CombatRoundRuntime : MonoBehaviour, IRoundInitializable, IRoundResettable
{
    private CombatSettings _settings;
    private bool _ownsSettings;
    private RoundCombatStats _stats;
    private EnemyRuntime _enemy;
    private UIRoot _ui;
    private float _combatTime;
    private bool _clockActive;
    private EventManager _events;
    private PlayerRuntime _player;
    private GameState _state = GameState.Initializing;
    private float _remaining;
    private float _finishingRemaining;
    private bool _timerEnded;
    private bool _finishingEnded;
    private float _hitStopRemaining;
    private bool _hitStopsEnabled = true;
    private const float MaxHitStopSeconds = 0.3f;
    private const float MinHitStopTimeScale = 0.02f;

    public RoundCombatStats Stats => _stats;
    public CombatSettings Settings => _settings;
    public event Action<Vector3, float, int> SpecialActivated;

    // Real seconds left of the current hit stop, and of the round clock. Read by tests and tuning tools.
    public float HitStopRemaining => _hitStopRemaining;
    public float RoundRemaining => _remaining;

    // Holds the round still for a moment of real time when a blow lands (Time.timeScale drops to timeScale).
    // Only in Battle: Finishing keeps its own slow motion. A longer request extends the current stop and a shorter one
    // is ignored. This runtime already owns the Finishing time scale, so it also owns this one, and every way out
    // (the stop ending, any state change, Reset, destroy) puts the time scale back.
    public void RequestHitStop(float seconds, float timeScale)
    {
        if (!_hitStopsEnabled || _state != GameState.Battle || _timerEnded || seconds <= _hitStopRemaining) return;
        _hitStopRemaining = Mathf.Min(seconds, MaxHitStopSeconds);
        Time.timeScale = Mathf.Clamp(timeScale, MinHitStopTimeScale, 1f);
    }

    // Benchmarks turn hit stops off: a stop changes the time scale and would skew the frame timings they compare.
    public bool HitStopsEnabled
    {
        get => _hitStopsEnabled;
        set
        {
            _hitStopsEnabled = value;
            if (!value) EndHitStop();
        }
    }

    private void EndHitStop()
    {
        if (_hitStopRemaining <= 0f) return;
        _hitStopRemaining = 0f;
        if (_state != GameState.Finishing) Time.timeScale = 1f;
    }

    public void Configure(EnemyRuntime enemy, UIRoot ui, CombatSettings settings, EventManager events = null, PlayerRuntime player = null)
    {
        if (_stats != null) throw new InvalidOperationException("Combat round is already configured.");
        _enemy = enemy != null ? enemy : throw new ArgumentNullException(nameof(enemy));
        _ui = ui != null ? ui : throw new ArgumentNullException(nameof(ui));
        _events = events;
        _player = player;
        _settings = settings;
        if (_settings == null)
        {
            _settings = ScriptableObject.CreateInstance<CombatSettings>();
            _ownsSettings = true;
        }
        _stats = new RoundCombatStats(_settings);
        _stats.Changed += Present;
        _enemy.HitAccepted += RecordEnemyHit;
        _enemy.PlayerHit += RecordPlayerHit;
    }

    public IEnumerator InitializeRound(int tick)
    {
        _stats.Reset(tick);
        _combatTime = 0f;
        _clockActive = false;
        ResetClock();
        yield break;
    }

    public void StopProducing() { _stats.SetAccepting(false); _clockActive = false; EndHitStop(); }

    public IEnumerator ResetRound(int tick)
    {
        _stats.Reset(tick);
        _combatTime = 0f;
        _clockActive = false;
        ResetClock();
        yield break;
    }

    public void ApplyState(GameStateChangedEvent state)
    {
        if (state.Tick != _stats.Tick) return;
        // A hit stop never outlives Battle; Finishing sets its own slow motion just below.
        if (_hitStopRemaining > 0f)
        {
            _hitStopRemaining = 0f;
            Time.timeScale = 1f;
        }
        if (_state == GameState.Finishing && state.Current != GameState.Finishing)
            Time.timeScale = 1f;
        _state = state.Current;
        if (_state == GameState.Finishing)
        {
            _finishingRemaining = Mathf.Max(0.1f, _settings.finishingDuration);
            _finishingEnded = false;
            Time.timeScale = 0.5f;
        }
        _stats.SetAccepting(state.Current == GameState.Battle || state.Current == GameState.Finishing);
        _clockActive = state.Current == GameState.Battle || state.Current == GameState.Finishing;
        Present();
    }

    private void RecordEnemyHit(int tick, bool chargeGauge)
    {
        int previous = _stats.Score;
        if (_stats.RecordEnemyKill(tick, chargeGauge, _combatTime) && _state == GameState.Battle)
            _ui.PresentScoreChange(_stats.Score - previous);
    }

    private void RecordPlayerHit(int tick)
    {
        // [WH] 회피 무적 중이면 피격으로 치지 않는다. (점수 차감, 게이지 증가, 피격 후 무적 시작 모두 없음)
        if (_player != null && _player.IsInvulnerable) return;
        int previous = _stats.Score;
        if (!_stats.RecordPlayerHit(tick, _combatTime) || _state != GameState.Battle) return;
        _ui.PresentScoreChange(_stats.Score - previous);
        _ui.PresentPlayerHit();
    }
    public bool TryUseSpecial(int tick, Vector3 center)
    {
        if (_state != GameState.Battle || _timerEnded || !_stats.TryUseSpecial(tick)) return false;
        _enemy.ApplySpecial(center, _settings.specialRadius, _settings.specialKnockbackMultiplier, tick, _settings.specialKillsRestoreGauge);
        SpecialActivated?.Invoke(center, _settings.specialRadius, tick);
        return true;
    }
    public void Advance(float deltaTime, float unscaledDeltaTime)
    {
        // Hit stops must not lengthen the round: while one holds, the round clock runs on real time.
        float realDelta = FeedbackTime.RealDelta(unscaledDeltaTime);
        float clockDelta = _hitStopRemaining > 0f ? realDelta : deltaTime;
        if (_hitStopRemaining > 0f)
        {
            _hitStopRemaining -= realDelta;
            if (_hitStopRemaining <= 0f)
            {
                _hitStopRemaining = 0f;
                if (_state != GameState.Finishing) Time.timeScale = 1f;
            }
        }
        if (_clockActive) _combatTime += deltaTime;
        if (_state == GameState.Battle && !_timerEnded)
        {
            _remaining = Mathf.Max(0f, _remaining - clockDelta);
            _ui.PresentBattleTimer(_remaining);
            if (_remaining <= 0f && _events != null)
            {
                _timerEnded = true;
                _player?.StopProducing();
                _enemy.StopProducing();
                _events.EnqueueRequest(new TimerReachedZeroRequest(_stats.Tick));
            }
        }
        if (_state != GameState.Finishing || _finishingEnded) return;
        _finishingRemaining -= unscaledDeltaTime;
        if (_finishingRemaining > 0f) return;
        _finishingEnded = true;
        _stats.SetAccepting(false);
        _clockActive = false;
        Time.timeScale = 1f;
        _events?.EnqueueRequest(new FinishingCompleteRequest(_stats.Tick));
    }

    private void ResetClock()
    {
        _remaining = Mathf.Max(0.1f, _settings.roundDuration);
        _timerEnded = false;
        _finishingEnded = false;
        if (_hitStopRemaining > 0f)
        {
            _hitStopRemaining = 0f;
            Time.timeScale = 1f;
        }
        if (_state == GameState.Finishing) Time.timeScale = 1f;
        _state = GameState.Ready;
    }

    private void Present()
    {
        _ui.PresentBattleScore(_stats.Score);
        _ui.PresentBattleGauge(_stats.Gauge / Mathf.Max(0.01f, _settings.maximumGauge));
        _ui.PresentBattleTimer(_remaining);
        _ui.PresentFinishing(new FinishingUiModel(_stats.Score));
        _ui.PresentResult(new ResultUiModel(_stats.Score, _stats.KillCount, _stats.MaxCombo, _stats.HitCount, _stats.SpecialUseCount));
    }

    private void OnDestroy()
    {
        if (_enemy != null)
        {
            _enemy.HitAccepted -= RecordEnemyHit;
            _enemy.PlayerHit -= RecordPlayerHit;
        }
        if (_stats != null) _stats.Changed -= Present;
        if (_ownsSettings && _settings != null) Destroy(_settings);
        if (_state == GameState.Finishing || _hitStopRemaining > 0f) Time.timeScale = 1f;
    }
}
