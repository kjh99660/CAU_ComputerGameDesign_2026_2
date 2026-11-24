// Every sound effect the game plays. Gameplay cues (Swing to GaugeFull, and EnemyWindup and EnemyStrike) belong to the
// round: they start only in Battle and Finishing, Pause holds them and Retry cuts them. Interface cues (CountdownTick to
// DialogueNext) play in every state.
// Some events play several cues at once as layers: a landed blow is Hit (a dull thud) + HitMetal (armour) +
// EnemyDown (the robot breaking), with HitSub (a deep thump) for heavy blows; a hit on the player is PlayerHurt +
// PlayerGlitch.
// New cues go at the end: SfxSettings stores each cue by its number.
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
    DialogueNext,
    // An enemy's attack, played where the enemy stands: the rising wind-up while it glows the warning colour, and the
    // swish of its strike. Higher and quieter than the player's blows, so they warn without covering them.
    EnemyWindup,
    EnemyStrike
}

public static class SfxCues
{
    // EnemyStrike stays the last cue; a test checks that this matches the enum.
    public const int Count = (int)SfxCue.EnemyStrike + 1;

    public static bool IsGameplay(SfxCue cue) => cue <= SfxCue.GaugeFull || cue >= SfxCue.EnemyWindup;
}
