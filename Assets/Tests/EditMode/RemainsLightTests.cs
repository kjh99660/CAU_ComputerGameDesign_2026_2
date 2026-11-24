using NUnit.Framework;

// What stays lit on a destroyed enemy's remains (RemainsLight): broken parts start as bright as the living enemy and
// dim to a faint glow, a body comes up to it, crackling remains spark in short bursts, and everything goes out as it
// is eaten.
public sealed class RemainsLightTests
{
    private const float Afterglow = 0.16f;

    [Test]
    public void TheSharesDecideWhichRemainsGlowAndWhichCrackle()
    {
        Assert.AreEqual(RemainsLight.Kind.Crackle, RemainsLight.Pick(0.1f, 0.3f, 0.45f));
        Assert.AreEqual(RemainsLight.Kind.Afterglow, RemainsLight.Pick(0.5f, 0.3f, 0.45f));
        Assert.AreEqual(RemainsLight.Kind.Dark, RemainsLight.Pick(0.9f, 0.3f, 0.45f));
        Assert.AreEqual(RemainsLight.Kind.Dark, RemainsLight.Pick(0f, 0f, 0f), "With no shares every light dies.");
    }

    [Test]
    public void PartsStartLitAndDimToAFaintGlowWhileABodyComesUpToIt()
    {
        Assert.AreEqual(1f, Part(RemainsLight.Kind.Afterglow, 0f), 0.0001f, "A part flies off as bright as the enemy was.");
        Assert.AreEqual(1f, Part(RemainsLight.Kind.Dark, 0f), 0.0001f);
        for (float age = 0.7f; age < 3f; age += 0.1f)
        {
            float kept = Part(RemainsLight.Kind.Afterglow, age);
            Assert.That(kept, Is.InRange(Afterglow * 0.5f, Afterglow), "A faint, uneven glow is kept at " + age + " s.");
            Assert.AreEqual(0f, Part(RemainsLight.Kind.Dark, age), 0.0001f, "The others go dark.");
        }
        Assert.Less(Part(RemainsLight.Kind.Afterglow, 0.3f), 0.9f, "It dims while it flies.");

        Assert.AreEqual(0f, Body(0f), 0.0001f, "A body starts as dark as its enemy died.");
        Assert.That(Body(1f), Is.InRange(Afterglow * 0.5f, Afterglow), "...and comes up to the faint glow.");
    }

    [Test]
    public void TheGlowGoesOutAsTheRemainsAreEaten()
    {
        float whole = RemainsLight.Light(RemainsLight.Kind.Afterglow, 1.5f, 1f, 0.6f, Afterglow, 0f, 3f, false);
        float half = RemainsLight.Light(RemainsLight.Kind.Afterglow, 1.5f, 1f, 0.6f, Afterglow, 0.5f, 3f, false);
        float gone = RemainsLight.Light(RemainsLight.Kind.Afterglow, 1.5f, 1f, 0.6f, Afterglow, 1f, 3f, false);
        Assert.AreEqual(whole * 0.5f, half, 0.0001f);
        Assert.AreEqual(0f, gone, 0.0001f);
    }

    [Test]
    public void CrackleComesInShortBurstsOfFlashesAndMakesTheLightsJump()
    {
        const float seed = 41.3f;
        int on = 0, starts = 0, samples = 0, longestOff = 0, off = 0;
        bool last = false;
        for (float age = 0f; age < 8f; age += 1f / 60f)
        {
            bool sparking = RemainsLight.Sparking(RemainsLight.Kind.Crackle, age, seed, 2.5f, 30f);
            samples++;
            if (sparking) on++;
            if (sparking && !last) starts++;
            off = sparking ? 0 : off + 1;
            if (off > longestOff) longestOff = off;
            last = sparking;
            Assert.IsFalse(RemainsLight.Sparking(RemainsLight.Kind.Afterglow, age, seed, 2.5f, 30f));
            Assert.IsFalse(RemainsLight.Sparking(RemainsLight.Kind.Dark, age, seed, 2.5f, 30f));
        }
        Assert.That(on / (float)samples, Is.InRange(0.12f, 0.5f), "It sparks some of the time, not all of it.");
        Assert.Greater(starts, 20, "It flickers: many separate flashes.");
        Assert.Greater(longestOff, 20, "Between bursts it rests for a moment (over a third of a second).");
        Assert.AreEqual(30, RemainsLight.Flash(1f + 0.001f, seed, 30f) - RemainsLight.Flash(0.001f, seed, 30f),
                        "Every flash draws new arcs, 30 times a second.");

        float steady = RemainsLight.Light(RemainsLight.Kind.Crackle, 2f, 1f, 0.6f, Afterglow, 0f, seed, false);
        float flashing = RemainsLight.Light(RemainsLight.Kind.Crackle, 2f, 1f, 0.6f, Afterglow, 0f, seed, true);
        Assert.Greater(flashing, steady, "Its lights jump while it sparks.");
        Assert.LessOrEqual(flashing, Afterglow * RemainsLight.CrackleBoost + 0.0001f, "...but stay well below the living enemy.");
    }

    private static float Part(RemainsLight.Kind kind, float age) =>
        RemainsLight.Light(kind, age, 1f, 0.6f, Afterglow, 0f, 7.7f, false);

    private static float Body(float age) =>
        RemainsLight.Light(RemainsLight.Kind.Afterglow, age, 0f, 0.6f, Afterglow, 0f, 12.1f, false);
}
