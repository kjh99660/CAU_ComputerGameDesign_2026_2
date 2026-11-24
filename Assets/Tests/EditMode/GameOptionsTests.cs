using System.Collections.Generic;
using NUnit.Framework;

// 일시정지 메뉴의 플레이어 설정(마우스 감도, 음량): 한 번에 한 단계, 범위 밖으로 나가지 않고, 바뀔 때만 저장하고 알리며,
// 저장된 값은 다음 실행에서 가장 가까운 단계로 돌아온다. 가짜 저장소로 검증한다.
public sealed class GameOptionsTests
{
    private sealed class FakeStore : IOptionsStore
    {
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>();
        public int Saves;
        public bool TryLoad(string key, out float value) => Values.TryGetValue(key, out value);
        public void Save(string key, float value) { Values[key] = value; Saves++; }
    }

    [Test]
    public void ANewPlayerStartsAtTheDesignedValues()
    {
        var store = new FakeStore();
        var options = new GameOptions(store);
        Assert.AreEqual(100, options.LookPercent);
        Assert.AreEqual(100, options.VolumePercent);
        Assert.AreEqual(1f, options.LookScale);
        Assert.AreEqual(1f, options.Volume);
        Assert.IsTrue(options.CanLowerLook && options.CanRaiseLook && options.CanLowerVolume);
        Assert.IsFalse(options.CanRaiseVolume, "100% is the loudest.");
        Assert.AreEqual(0, store.Saves, "Reading the defaults writes nothing.");
    }

    [Test]
    public void AStepMovesOneStepSavesAndTellsOnce()
    {
        var store = new FakeStore();
        var options = new GameOptions(store);
        int changes = 0;
        options.Changed += () => changes++;

        Assert.IsTrue(options.StepLook(1));
        Assert.AreEqual(100 + GameOptions.LookStepPercent, options.LookPercent);
        Assert.AreEqual(1.1f, options.LookScale, 0.0001f);
        Assert.AreEqual(1.1f, store.Values[GameOptions.LookScaleKey], 0.0001f);
        Assert.AreEqual(1, changes);

        Assert.IsTrue(options.StepVolume(-1));
        Assert.AreEqual(100 - GameOptions.VolumeStepPercent, options.VolumePercent);
        Assert.AreEqual(0.9f, options.Volume, 0.0001f);
        Assert.AreEqual(0.9f, store.Values[GameOptions.VolumeKey], 0.0001f);
        Assert.AreEqual(2, changes);
        Assert.AreEqual(2, store.Saves);

        Assert.IsFalse(options.StepLook(0), "No direction, no step.");
        Assert.AreEqual(2, changes);
        Assert.AreEqual(2, store.Saves);
    }

    [Test]
    public void TheEndsOfTheRangesHold()
    {
        var store = new FakeStore();
        var options = new GameOptions(store);
        for (int i = 0; i < 100; i++) options.StepLook(-1);
        Assert.AreEqual(GameOptions.MinLookPercent, options.LookPercent);
        Assert.IsFalse(options.CanLowerLook);
        int saves = store.Saves;
        int changes = 0;
        options.Changed += () => changes++;
        Assert.IsFalse(options.StepLook(-1), "Already at the lowest sensitivity.");
        Assert.AreEqual(saves, store.Saves);
        Assert.AreEqual(0, changes);
        Assert.Greater(options.LookScale, 0f, "The camera can always be turned.");

        for (int i = 0; i < 100; i++) options.StepLook(1);
        Assert.AreEqual(GameOptions.MaxLookPercent, options.LookPercent);
        Assert.IsFalse(options.CanRaiseLook);

        for (int i = 0; i < 100; i++) options.StepVolume(-1);
        Assert.AreEqual(0, options.VolumePercent);
        Assert.AreEqual(0f, options.Volume);
        Assert.IsFalse(options.CanLowerVolume);
        Assert.IsFalse(options.StepVolume(-1));
        for (int i = 0; i < 100; i++) options.StepVolume(1);
        Assert.AreEqual(100, options.VolumePercent);
        Assert.IsFalse(options.StepVolume(1), "Never louder than the game was mixed.");
    }

    [Test]
    public void TheNextRunReadsWhatThisOneSaved()
    {
        var store = new FakeStore();
        var first = new GameOptions(store);
        first.StepLook(1);
        first.StepLook(1);
        first.StepVolume(-1);
        first.StepVolume(-1);
        first.StepVolume(-1);

        var second = new GameOptions(store);
        Assert.AreEqual(120, second.LookPercent);
        Assert.AreEqual(70, second.VolumePercent);
    }

    [TestCase(1.24f, 120)]      // snaps to the nearest step
    [TestCase(1.25f, 130)]
    [TestCase(0.26f, 30)]       // just under the range: the lowest step
    [TestCase(3.04f, 300)]
    [TestCase(5f, 100)]         // far outside: not a value this game wrote, back to the default
    [TestCase(-1f, 100)]
    [TestCase(float.NaN, 100)]
    [TestCase(float.PositiveInfinity, 100)]
    public void ASavedSensitivityComesBackAsAValidStep(float saved, int expectedPercent)
    {
        var store = new FakeStore();
        store.Values[GameOptions.LookScaleKey] = saved;
        Assert.AreEqual(expectedPercent, new GameOptions(store).LookPercent);
    }

    [TestCase(0f, 0)]
    [TestCase(0.55f, 60)]
    [TestCase(1.04f, 100)]
    [TestCase(2f, 100)]
    [TestCase(-0.5f, 100)]
    [TestCase(float.NaN, 100)]
    public void ASavedVolumeComesBackAsAValidStep(float saved, int expectedPercent)
    {
        var store = new FakeStore();
        store.Values[GameOptions.VolumeKey] = saved;
        Assert.AreEqual(expectedPercent, new GameOptions(store).VolumePercent);
    }
}
