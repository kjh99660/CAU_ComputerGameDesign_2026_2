using System;
using UnityEngine;

// Owns one round's combat values; Unity adapters validate and report combat events.
public sealed class RoundCombatStats
{
    private readonly CombatSettings _settings;
    private float _invulnerableUntil;
    private bool _accepting;
    private float _lastKillTime;
    private int _combo;
    public int Tick { get; private set; }
    public int Score { get; private set; }
    public float Gauge { get; private set; }
    public int KillCount { get; private set; }
    public int HitCount { get; private set; }
    public int MaxCombo { get; private set; }
    public int SpecialUseCount { get; private set; }
    public event Action Changed;

    public RoundCombatStats(CombatSettings settings)
    {
        _settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
    }

    public void Reset(int tick)
    {
        Tick = tick;
        Score = KillCount = HitCount = MaxCombo = SpecialUseCount = _combo = 0;
        _lastKillTime = float.NegativeInfinity;
        Gauge = 0f;
        _invulnerableUntil = float.NegativeInfinity;
        _accepting = false;
        Changed?.Invoke();
    }

    public void SetAccepting(bool accepting) => _accepting = accepting;

    // Call exactly once after EnemyRuntime accepts a hit on a freshly spawned enemy.
    public bool RecordEnemyKill(int tick, bool chargeGauge = true, float gameTime = 0f)
    {
        if (!_accepting || tick != Tick) return false;
        Score += Mathf.Max(0, _settings.hitScore) + Mathf.Max(0, _settings.killScore);
        if (chargeGauge)
            Gauge = Mathf.Clamp(Gauge + Mathf.Max(0f, _settings.hitGauge) + Mathf.Max(0f, _settings.killGauge), 0f, Mathf.Max(0.01f, _settings.maximumGauge));
        KillCount++;
        _combo = gameTime - _lastKillTime <= Mathf.Max(0.1f, _settings.comboTimeoutSeconds) ? _combo + 1 : 1;
        _lastKillTime = gameTime;
        MaxCombo = Mathf.Max(MaxCombo, _combo);
        Changed?.Invoke();
        return true;
    }

    // Provisional policy: floor the penalty amount to an integer.
    public bool RecordPlayerHit(int tick, float gameTime)
    {
        if (!_accepting || tick != Tick || gameTime < _invulnerableUntil) return false;
        int penalty = Mathf.FloorToInt(Score * Mathf.Clamp01(_settings.playerHitPenalty));
        Score = Mathf.Max(0, Score - penalty);
        Gauge = Mathf.Clamp(Gauge + Mathf.Max(0f, _settings.playerHitGauge), 0f, Mathf.Max(0.01f, _settings.maximumGauge));
        HitCount++;
        _combo = 0;
        _invulnerableUntil = gameTime + Mathf.Max(0f, _settings.invulnerabilitySeconds);
        Changed?.Invoke();
        return true;
    }

    public bool TryUseSpecial(int tick)
    {
        float maximum = Mathf.Max(0.01f, _settings.maximumGauge);
        if (!_accepting || tick != Tick || Gauge < maximum) return false;
        Gauge = Mathf.Max(0f, Gauge - maximum * Mathf.Clamp01(_settings.specialCostFraction));
        SpecialUseCount++;
        _combo = 0;
        _lastKillTime = float.NegativeInfinity;
        Changed?.Invoke();
        return true;
    }
}
