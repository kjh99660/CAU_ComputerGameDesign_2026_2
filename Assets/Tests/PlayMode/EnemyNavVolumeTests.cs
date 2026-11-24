using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Measures a small hand-built level with real colliders: ground, a deck 3 m above it, a ramp between them,
// a wall and a pillar.
public sealed class EnemyNavVolumeTests
{
    private GameObject _root;
    private EnemyNavVolume _volume;

    [SetUp]
    public void BuildLevel()
    {
        _root = new GameObject("NavTestLevel");
        Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), Quaternion.identity);
        Box("Deck", new Vector3(6f, 2.8f, 0f), new Vector3(8f, 0.4f, 20f), Quaternion.identity);   // top at 3 m, x = 2..10
        // Ramp whose top runs from (-3, 0) up to (2, 3) in x/y, 3 m wide around z = 8.
        float angle = Mathf.Atan2(3f, 5f) * Mathf.Rad2Deg;
        Quaternion tilt = Quaternion.Euler(0f, 0f, angle);
        Box("Ramp", new Vector3(-0.5f, 1.5f, 8f) - tilt * Vector3.up * 0.1f, new Vector3(Mathf.Sqrt(34f), 0.2f, 3f), tilt);
        Box("Wall", new Vector3(-5f, 1f, -4f), new Vector3(0.3f, 2f, 6f), Quaternion.identity);
        Box("Pillar", new Vector3(5f, 1.3f, -5f), new Vector3(1f, 2.6f, 1f), Quaternion.identity);

        var volumeObject = new GameObject("NavVolume");
        volumeObject.transform.SetParent(_root.transform, false);
        volumeObject.transform.position = new Vector3(0f, 2f, 0f);
        _volume = volumeObject.AddComponent<EnemyNavVolume>();
        _volume.Configure(new Vector3(20f, 8f, 20f), 0.5f, 1 << 0);
    }

    [TearDown]
    public void RemoveLevel()
    {
        if (_root != null)
            Object.Destroy(_root);
    }

    private void Box(string name, Vector3 position, Vector3 scale, Quaternion rotation)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(_root.transform, false);
        box.transform.SetPositionAndRotation(position, rotation);
        box.transform.localScale = scale;
    }

    [UnityTest]
    public IEnumerator FloorsWallsAndTheRampAreMeasuredFromColliders()
    {
        yield return null;
        EnemyNavField field = _volume.Bake(new Vector3(-8f, 0f, -8f));
        Assert.That(_volume.LastBakeMilliseconds, Is.GreaterThan(0d));
        Assert.That(field.CellCount, Is.GreaterThan(1500));

        int open = field.FindCell(new Vector3(-8f, 0f, 0f), 0.2f);
        Assert.That(open, Is.Not.EqualTo(EnemyNavField.None));
        Assert.That(field.Height(open), Is.EqualTo(0f).Within(0.01f));

        Assert.That(field.FloorsAt(7.2f, 3.2f), Is.EqualTo(2), "The deck and the ground under it.");
        Assert.That(field.FindCell(new Vector3(-4.9f, 0f, -4f), 1f), Is.EqualTo(EnemyNavField.None), "Inside the wall.");
        Assert.That(field.FindCell(new Vector3(5.1f, 0f, -4.9f), 0.5f), Is.EqualTo(EnemyNavField.None), "Inside the pillar.");
        Assert.That(field.FindCell(new Vector3(5.1f, 3f, -4.9f), 0.5f), Is.Not.EqualTo(EnemyNavField.None), "The deck above the pillar.");
        Assert.That(field.FindCell(new Vector3(-1.2f, 0f, 8f), 0.3f), Is.EqualTo(EnemyNavField.None), "No head room under the low end of the ramp.");
    }

    [UnityTest]
    public IEnumerator TheWayOntoTheDeckIsTheRamp()
    {
        yield return null;
        EnemyNavField field = _volume.Bake(new Vector3(-8f, 0f, -8f));
        Assert.That(field.SetGoal(new Vector3(6f, 3f, 0f)), Is.True);

        int under = field.FindCell(new Vector3(6f, 0f, 0f), 0.5f);
        Assert.That(under, Is.Not.EqualTo(EnemyNavField.None));
        Assert.That(field.HasDirectPath(under), Is.False);
        Assert.That(field.PathDistance(under), Is.GreaterThan(12f), "Under the player is a long walk from the player.");

        int cell = under, rampCells = 0, steps = 0;
        while (cell != field.Goal)
        {
            int next = field.Next(cell);
            Assert.That(next, Is.Not.EqualTo(EnemyNavField.None));
            Vector3 from = field.Position(cell), to = field.Position(next);
            Assert.That(Mathf.Abs(to.y - from.y), Is.LessThan(0.62f), "Nobody climbs a wall.");
            if (to.y > 0.3f && to.y < 2.7f)
            {
                rampCells++;
                Assert.That(to.z, Is.InRange(6.5f, 9.5f), "Half-way up is only possible on the ramp.");
            }
            cell = next;
            Assert.That(++steps, Is.LessThan(5000));
        }
        Assert.That(rampCells, Is.GreaterThanOrEqualTo(5));

        // Standing on the ramp, the ground height follows the slope.
        int onRamp = field.FindCell(new Vector3(-0.25f, 1.65f, 8.25f), 0.3f);
        Assert.That(onRamp, Is.Not.EqualTo(EnemyNavField.None));
        Assert.That(field.SampleHeight(onRamp, new Vector3(-0.1f, 0f, 8.25f)), Is.EqualTo(1.74f).Within(0.03f));
    }
}
