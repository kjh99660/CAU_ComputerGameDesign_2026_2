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

    // Seconds of round time a combo survives without a new kill (comboTimeoutSeconds, at least 0.1 s).
    public float ComboWindowSeconds => Mathf.Max(0.1f, _settings.comboTimeoutSeconds);

    // The combo that is still alive at gameTime (the round time RecordEnemyKill receives): 0 once the window has run out,
    // after a player hit, or after a special (until the special's own kills start a new one, when
    // CombatSettings.comboCountsSpecialKills lets them).
    public int ComboAt(float gameTime) =>
        _combo > 0 && gameTime - _lastKillTime <= ComboWindowSeconds ? _combo : 0;

    // Round seconds left before the combo alive at gameTime drops (0 when there is none).
    public float ComboTimeLeft(float gameTime) =>
        ComboAt(gameTime) > 0 ? Mathf.Clamp(ComboWindowSeconds - (gameTime - _lastKillTime), 0f, ComboWindowSeconds) : 0f;

    // Score multiplier of the kill that makes the given combo, with this round's settings (1 = no bonus).
    public float ComboScoreMultiplier(int combo) =>
        ComboMultiplier(combo, _settings.comboScoreStep, _settings.comboScoreMaxBonus);

    // 1 + min(maxBonus, step * (combo - 1)). The first kill of a chain (combo 1) never gets a bonus; step 0 turns it off.
    public static float ComboMultiplier(int combo, float step, float maxBonus) =>
        (float)(1.0 + ComboBonus(combo, step, maxBonus));

    // The one rounding rule of the combo bonus: the multiplied kill score is floored to an int. The sum is done in double
    // with a 0.0001 tolerance so that Inspector values stored slightly below their decimal (0.7 is 0.69999999 as a float)
    // still give the decimal result (110 x 1.7 = 187, not 186). With the bonus off this returns baseScore exactly.
    public static int ComboKillScore(int baseScore, int combo, float step, float maxBonus)
    {
        if (baseScore <= 0) return 0;
        return (int)Math.Floor(baseScore * (1.0 + ComboBonus(combo, step, maxBonus)) + 0.0001);
    }

    private static double ComboBonus(int combo, float step, float maxBonus)
    {
        if (combo <= 1 || step <= 0f || maxBonus <= 0f) return 0.0;
        return Math.Min(maxBonus, (double)step * (combo - 1));
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

    // Call exactly once after EnemyRuntime accepts a hit on a freshly spawned enemy. specialKill marks a kill made by the
    // special attack itself (CombatRoundRuntime knows: the special hits everyone in range inside TryUseSpecial).
    // CombatSettings.comboCountsSpecialKills decides whether such a kill is a combo kill like any other (the default: it
    // extends the chain the special has just restarted, counts toward MaxCombo and gets the combo bonus) or stays out of
    // the combo (base score, the chain and MaxCombo are left as they are).
    public bool RecordEnemyKill(int tick, bool chargeGauge = true, float gameTime = 0f, bool specialKill = false)
    {
        if (!_accepting || tick != Tick) return false;
        int baseScore = Mathf.Max(0, _settings.hitScore) + Mathf.Max(0, _settings.killScore);
        if (specialKill && !_settings.comboCountsSpecialKills)
            Score += baseScore;
        else
        {
            _combo = gameTime - _lastKillTime <= ComboWindowSeconds ? _combo + 1 : 1;
            _lastKillTime = gameTime;
            Score += ComboKillScore(baseScore, _combo, _settings.comboScoreStep, _settings.comboScoreMaxBonus);
            MaxCombo = Mathf.Max(MaxCombo, _combo);
        }
        if (chargeGauge)
            Gauge = Mathf.Clamp(Gauge + Mathf.Max(0f, _settings.hitGauge) + Mathf.Max(0f, _settings.killGauge), 0f, Mathf.Max(0.01f, _settings.maximumGauge));
        KillCount++;
        Changed?.Invoke();
        return true;
    }

    // A hit the enemy survives (a tough enemy's first hit): the hit's own score and gauge, and one more step of the
    // combo, which counts landed hits (every hit was a kill before tough enemies existed). No kill is counted. The
    // combo bonus applies to the hit score by the same rounding rule as a kill.
    public bool RecordEnemyHit(int tick, bool chargeGauge = true, float gameTime = 0f)
    {
        if (!_accepting || tick != Tick) return false;
        _combo = gameTime - _lastKillTime <= ComboWindowSeconds ? _combo + 1 : 1;
        _lastKillTime = gameTime;
        Score += ComboKillScore(Mathf.Max(0, _settings.hitScore), _combo, _settings.comboScoreStep, _settings.comboScoreMaxBonus);
        MaxCombo = Mathf.Max(MaxCombo, _combo);
        if (chargeGauge)
            Gauge = Mathf.Clamp(Gauge + Mathf.Max(0f, _settings.hitGauge), 0f, Mathf.Max(0.01f, _settings.maximumGauge));
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
