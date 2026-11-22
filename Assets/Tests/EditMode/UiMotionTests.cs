using NUnit.Framework;

// UI 연출 계산식: 시작값과 끝값, 범위 밖 입력, 한 방향으로만 변하는지를 검증한다.
public sealed class UiMotionTests
{
    private const float Tolerance = 1e-5f;

    [Test]
    public void PopStartsAtGivenScaleAndSettlesAtOneWithoutOvershoot()
    {
        Assert.AreEqual(1.55f, UiMotion.Pop(0f, 0.22f, 1.55f), Tolerance);
        Assert.AreEqual(1f, UiMotion.Pop(0.22f, 0.22f, 1.55f), Tolerance);
        Assert.AreEqual(1f, UiMotion.Pop(5f, 0.22f, 1.55f), Tolerance);
        Assert.AreEqual(1.55f, UiMotion.Pop(-1f, 0.22f, 1.55f), Tolerance, "Negative time stays at the start pose.");

        float previous = float.MaxValue;
        for (int i = 0; i <= 22; i++)
        {
            float scale = UiMotion.Pop(i * 0.01f, 0.22f, 1.55f);
            Assert.LessOrEqual(scale, previous + Tolerance, "Pop must shrink monotonically.");
            Assert.GreaterOrEqual(scale, 1f - Tolerance);
            previous = scale;
        }
    }

    [Test]
    public void PopWithZeroDurationIsAlreadySettled()
    {
        Assert.AreEqual(1f, UiMotion.Pop(0f, 0f, 1.5f), Tolerance);
    }

    [Test]
    public void DecayFallsFromOneToZeroAndStaysThere()
    {
        Assert.AreEqual(1f, UiMotion.Decay(0f, 0.35f), Tolerance);
        Assert.AreEqual(0f, UiMotion.Decay(0.35f, 0.35f), Tolerance);
        Assert.AreEqual(0f, UiMotion.Decay(2f, 0.35f), Tolerance);
        Assert.AreEqual(0f, UiMotion.Decay(0f, 0f), Tolerance, "Zero duration means no flash.");
        Assert.Less(UiMotion.Decay(0.2f, 0.35f), UiMotion.Decay(0.1f, 0.35f));
    }

    [Test]
    public void TailOnlyProgressesInTheLastPartOfTheStep()
    {
        Assert.AreEqual(0f, UiMotion.Tail(0f, 0.8f, 0.22f), Tolerance);
        Assert.AreEqual(0f, UiMotion.Tail(0.58f, 0.8f, 0.22f), Tolerance);
        Assert.AreEqual(0.5f, UiMotion.Tail(0.69f, 0.8f, 0.22f), 1e-4f);
        Assert.AreEqual(1f, UiMotion.Tail(0.8f, 0.8f, 0.22f), Tolerance);
        Assert.AreEqual(1f, UiMotion.Tail(3f, 0.8f, 0.22f), Tolerance);
        Assert.AreEqual(0f, UiMotion.Tail(0.79f, 0.8f, 0f), Tolerance);
        Assert.AreEqual(1f, UiMotion.Tail(0.8f, 0.8f, 0f), Tolerance);
    }

    [Test]
    public void PulseIsZeroAtWholePeriodsAndOneAtHalfPeriods()
    {
        Assert.AreEqual(0f, UiMotion.Pulse01(0f), Tolerance);
        Assert.AreEqual(1f, UiMotion.Pulse01(0.5f), Tolerance);
        Assert.AreEqual(0f, UiMotion.Pulse01(1f), Tolerance);
        Assert.AreEqual(0.5f, UiMotion.Pulse01(0.25f), Tolerance);
        for (int i = 0; i <= 40; i++)
            Assert.That(UiMotion.Pulse01(i * 0.05f), Is.InRange(-Tolerance, 1f + Tolerance));
    }

    [Test]
    public void LerpAndEaseClampTheirInput()
    {
        Assert.AreEqual(2f, UiMotion.Lerp(2f, 6f, -1f), Tolerance);
        Assert.AreEqual(4f, UiMotion.Lerp(2f, 6f, 0.5f), Tolerance);
        Assert.AreEqual(6f, UiMotion.Lerp(2f, 6f, 9f), Tolerance);
        Assert.AreEqual(0f, UiMotion.EaseOutCubic(-1f), Tolerance);
        Assert.AreEqual(1f, UiMotion.EaseOutCubic(2f), Tolerance);
        Assert.Greater(UiMotion.EaseOutCubic(0.25f), 0.25f, "Ease-out moves faster than linear at the start.");
    }
}
