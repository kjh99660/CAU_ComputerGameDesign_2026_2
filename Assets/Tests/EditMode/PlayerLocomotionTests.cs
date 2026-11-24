using NUnit.Framework;
using UnityEngine;

// The player's planar velocity rule: quick to start, quicker to stop, and it turns around through zero.
public sealed class PlayerLocomotionTests
{
    private const float Step = 1f / 60f;

    [Test]
    public void ReachesFullSpeedWithinAFewFrames()
    {
        Vector3 velocity = Vector3.zero;
        Vector3 wanted = Vector3.forward * 10f;
        int frames = 0;
        while (velocity.magnitude < 9f && frames < 600)
        {
            velocity = PlayerLocomotion.Step(velocity, wanted, 90f, 120f, 1.6f, Step);
            frames++;
        }
        Assert.LessOrEqual(frames * Step, 0.15f, "Full run speed must come within 0.15 s of the input.");
        Assert.AreEqual(Vector3.forward, velocity.normalized);
    }

    [Test]
    public void BrakesWithItsOwnRateWhenInputIsReleased()
    {
        Vector3 velocity = Vector3.forward * 10f;
        float travelled = 0f;
        int frames = 0;
        while (velocity.sqrMagnitude > 0.0001f && frames < 600)
        {
            velocity = PlayerLocomotion.Step(velocity, Vector3.zero, 90f, 120f, 1.6f, Step);
            travelled += velocity.magnitude * Step;
            frames++;
        }
        Assert.LessOrEqual(frames * Step, 0.1f, "Releasing the input must stop the body within 0.1 s.");
        Assert.Less(travelled, 0.5f, "The body must not slide on after the input is released.");
    }

    [Test]
    public void TurnsAroundThroughZeroFasterThanAStandingStart()
    {
        Vector3 velocity = Vector3.forward * 10f;
        Vector3 wanted = Vector3.back * 10f;
        int frames = 0;
        float furthestForward = 0f, position = 0f;
        while (Vector3.Dot(velocity, Vector3.back) < 9f && frames < 600)
        {
            velocity = PlayerLocomotion.Step(velocity, wanted, 90f, 120f, 1.6f, Step);
            position += velocity.z * Step;
            furthestForward = Mathf.Max(furthestForward, position);
            // It never moves sideways while reversing: the velocity stays on the input line.
            Assert.AreEqual(0f, velocity.x, 0.0001f);
            frames++;
        }
        Assert.LessOrEqual(frames * Step, 0.18f, "A full reversal (20 m/s of change) must take under 0.18 s.");
        Assert.Less(furthestForward, 0.5f, "Overshoot in the old direction must stay under half a metre.");
    }

    [Test]
    public void ZeroOrNegativeTimeChangesNothing()
    {
        Vector3 velocity = new Vector3(3f, 0f, 4f);
        Assert.AreEqual(velocity, PlayerLocomotion.Step(velocity, Vector3.forward * 10f, 90f, 120f, 1.6f, 0f));
        Assert.AreEqual(velocity, PlayerLocomotion.Step(velocity, Vector3.zero, 90f, 120f, 1.6f, -1f));
    }
}
