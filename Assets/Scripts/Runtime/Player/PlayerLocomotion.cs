using UnityEngine;

// Planar velocity rule for the player. Kept free of scene access so it can be unit-tested.
public static class PlayerLocomotion
{
    // Moves the velocity toward the wanted one. With a target it accelerates (faster while turning around),
    // without one it brakes with its own rate, so starting, stopping and reversing can be tuned separately.
    public static Vector3 Step(Vector3 velocity, Vector3 desiredVelocity, float acceleration, float deceleration,
        float reverseBoost, float deltaTime)
    {
        if (deltaTime <= 0f)
            return velocity;

        bool hasTarget = desiredVelocity.sqrMagnitude > 0.0001f;
        float rate = hasTarget ? acceleration : deceleration;
        if (hasTarget && Vector3.Dot(desiredVelocity, velocity) < 0f)
            rate *= Mathf.Max(1f, reverseBoost);
        return Vector3.MoveTowards(velocity, desiredVelocity, Mathf.Max(0f, rate) * deltaTime);
    }
}
