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
