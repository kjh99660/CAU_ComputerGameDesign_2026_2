using System;
using UnityEngine;

// Owns one round's combat values; Unity adapters validate and report combat events.
public sealed class RoundCombatStats
{
    private readonly CombatSettings _settings;
    private float _invulnerableUntil;
    private bool _accepting;
    public int Tick { get; private set; }
    public int Score { get; private set; }
    public float Gauge { get; private set; }
    public int KillCount { get; private set; }
    public int HitCount { get; private set; }
    public event Action Changed;

    public RoundCombatStats(CombatSettings settings)
    {
        _settings = settings != null ? settings : throw new ArgumentNullException(nameof(settings));
    }

    public void Reset(int tick)
    {
        Tick = tick;
        Score = KillCount = HitCount = 0;
        Gauge = 0f;
        _invulnerableUntil = float.NegativeInfinity;
        _accepting = false;
        Changed?.Invoke();
    }

    public void SetAccepting(bool accepting) => _accepting = accepting;

    // Call exactly once after EnemyRuntime accepts a hit on a freshly spawned enemy.
    public bool RecordEnemyKill(int tick)
    {
        if (!_accepting || tick != Tick) return false;
        Score += Mathf.Max(0, _settings.hitScore) + Mathf.Max(0, _settings.killScore);
        Gauge = Mathf.Clamp(Gauge + Mathf.Max(0f, _settings.hitGauge) + Mathf.Max(0f, _settings.killGauge), 0f, Mathf.Max(0.01f, _settings.maximumGauge));
        KillCount++;
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
        _invulnerableUntil = gameTime + Mathf.Max(0f, _settings.invulnerabilitySeconds);
        Changed?.Invoke();
        return true;
    }
}
