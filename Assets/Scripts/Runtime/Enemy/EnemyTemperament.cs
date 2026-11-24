// How a lively enemy comes at the player (EnemySettings.Liveliness). Picked once per spawn from the enemy's variation,
// so the same round always has the same mix.
public enum EnemyTemperament
{
    Rusher,     // straight in, attacks as soon as its turn comes
    Flanker,    // swings around the side on the way in
    Circler,    // circles the player at a distance and takes its time
    Hesitant    // stop and go on the way in, feints before it commits
}
