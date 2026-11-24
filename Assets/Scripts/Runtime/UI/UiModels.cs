using UnityEngine;

public readonly struct DialogueUiModel
{
    public readonly string Speaker;
    public readonly string Body;
    public readonly bool IsLeftSpeaker;

    public DialogueUiModel(string speaker, string body, bool isLeftSpeaker)
    {
        Speaker = speaker ?? string.Empty;
        Body = body ?? string.Empty;
        IsLeftSpeaker = isLeftSpeaker;
    }
}

public readonly struct BattleUiModel
{
    public readonly int Score;
    public readonly float RemainingSeconds;
    public readonly float GaugeNormalized;

    public BattleUiModel(int score, float remainingSeconds, float gaugeNormalized)
    {
        Score = score;
        RemainingSeconds = remainingSeconds;
        GaugeNormalized = Mathf.Clamp01(gaugeNormalized);
    }
}

// The combo the HUD shows (Battle and Finishing). Count 0 means there is no combo alive.
// TimeLeft/Window are round seconds; the bar shows TimeLeftNormalized. ScoreMultiplier is 1 while the combo bonus is off.
public readonly struct ComboUiModel
{
    public readonly int Count;
    public readonly float TimeLeft;
    public readonly float Window;
    public readonly float ScoreMultiplier;

    public ComboUiModel(int count, float timeLeft, float window, float scoreMultiplier)
    {
        Count = count < 0 ? 0 : count;
        Window = window > 0f ? window : 0f;
        TimeLeft = Mathf.Clamp(timeLeft, 0f, Window);
        ScoreMultiplier = scoreMultiplier > 1f ? scoreMultiplier : 1f;
    }

    public float TimeLeftNormalized => Window > 0f ? TimeLeft / Window : 0f;
}

public readonly struct FinishingUiModel
{
    public readonly int Score;
    public FinishingUiModel(int score) => Score = score;
}

public readonly struct ResultUiModel
{
    public readonly int FinalScore;
    public readonly int KillCount;
    public readonly int MaxCombo;
    public readonly int HitCount;
    public readonly int SpecialUseCount;

    public ResultUiModel(int finalScore, int killCount, int maxCombo, int hitCount, int specialUseCount)
    {
        FinalScore = finalScore;
        KillCount = killCount;
        MaxCombo = maxCombo;
        HitCount = hitCount;
        SpecialUseCount = specialUseCount;
    }
}
