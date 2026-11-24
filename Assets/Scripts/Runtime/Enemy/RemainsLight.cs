using UnityEngine;

// What stays lit on an enemy's remains (a body or a broken part), as pure functions of their age so that Pause holds
// them (the age only grows in Battle and Finishing) and tests can check them without a scene.
//   Dark      - its lights die with it.
//   Afterglow - it keeps a faint, uneven share of the living enemy's glow.
//   Crackle   - the same faint glow, plus short bursts of electric sparks; while the sparks flash its lights jump.
// Broken parts start as bright as the living enemy and dim to what is kept; a body (its enemy already dark while it
// died) comes up to what is kept. Everything goes out as the remains are eaten.
public static class RemainsLight
{
    public enum Kind
    {
        Dark,
        Afterglow,
        Crackle
    }

    // How bright the lights jump while sparks flash, as a multiple of the kept glow.
    public const float CrackleBoost = 2.5f;

    public static Kind Pick(float roll, float crackleShare, float glowShare)
    {
        if (roll < crackleShare) return Kind.Crackle;
        return roll < crackleShare + glowShare ? Kind.Afterglow : Kind.Dark;
    }

    // The share of the living enemy's light still shown (0 dark, 1 as alive), going from start to what is kept over
    // changeSeconds.
    public static float Light(Kind kind, float age, float start, float changeSeconds, float afterglow, float eaten,
                              float seed, bool sparking)
    {
        float kept = 0f;
        if (kind != Kind.Dark)
        {
            // A failing light: a slow uneven pulse, never quite out.
            kept = afterglow * (0.55f + 0.45f * Mathf.Clamp01(Mathf.PerlinNoise(seed, age * 2.5f)));
            if (sparking) kept = Mathf.Min(1f, afterglow * CrackleBoost);
        }
        float change = Mathf.Clamp01(age / Mathf.Max(0.01f, changeSeconds));
        return Mathf.Lerp(Mathf.Clamp01(start), kept, change * (2f - change)) * (1f - Mathf.Clamp01(eaten));
    }

    // Sparks come in bursts: a chance to start one comes burstsPerSecond times a second and about half are taken;
    // within a burst the sparks flash on and off flickerHz times a second.
    public static bool Sparking(Kind kind, float age, float seed, float burstsPerSecond, float flickerHz)
    {
        if (kind != Kind.Crackle) return false;
        if (Hash(Mathf.FloorToInt(age * burstsPerSecond + seed), seed) < 0.5f) return false;
        return Hash(Flash(age, seed, flickerHz), seed + 17f) >= 0.4f;
    }

    // Which flash it is: every flash draws a new set of arcs.
    public static int Flash(float age, float seed, float flickerHz) => Mathf.FloorToInt(age * flickerHz + seed * 3f);

    // 0..1 from an integer and a seed (integer mixing, the same on every platform).
    private static float Hash(int n, float seed)
    {
        unchecked
        {
            uint h = (uint)n * 747796405u + (uint)Mathf.RoundToInt(seed * 1000f) * 2891336453u + 1013904223u;
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }
}
