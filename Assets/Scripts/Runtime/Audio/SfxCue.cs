// Every sound effect the game plays. Gameplay cues (Swing to GaugeFull) belong to the round: they start only in
// Battle and Finishing, Pause holds them and Retry cuts them. Interface cues (CountdownTick on) play in every state.
// Some events play several cues at once as layers: a landed blow is Hit (a dull thud) + HitMetal (armour) +
// EnemyDown (the robot breaking), with HitSub (a deep thump) for heavy blows; a hit on the player is PlayerHurt +
// PlayerGlitch.
public enum SfxCue
{
    Swing,
    SwingHeavy,
    Hit,
    HitMetal,
    HitSub,
    EnemyDown,
    PlayerHurt,
    PlayerGlitch,
    Dodge,
    SkillDash,
    Special,
    GaugeFull,
    CountdownTick,
    CountdownGo,
    TimeUp,
    Result,
    UiClick,
    UiConfirm,
    UiBack,
    DialogueNext
}

public static class SfxCues
{
    // DialogueNext stays the last cue; a test checks that this matches the enum.
    public const int Count = (int)SfxCue.DialogueNext + 1;

    public static bool IsGameplay(SfxCue cue) => cue <= SfxCue.GaugeFull;
}
