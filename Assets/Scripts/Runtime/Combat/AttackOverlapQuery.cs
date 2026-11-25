using UnityEngine;

public static class AttackOverlapQuery
{
    public static Collider[] Sphere(Vector3 center, float radius, Collider[] buffer, int mask, out int count,
        QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
    {
        if (OptimizationManager.NonAllocHitQueries)
        {
            count = Physics.OverlapSphereNonAlloc(center, radius, buffer, mask, triggers);
            if (count < buffer.Length) return buffer;
        }
        Collider[] results = Physics.OverlapSphere(center, radius, mask, triggers);
        count = results.Length;
        return results;
    }

    public static Collider[] Box(Vector3 center, Vector3 halfExtents, Collider[] buffer, Quaternion rotation,
        int mask, out int count)
    {
        if (OptimizationManager.NonAllocHitQueries)
        {
            count = Physics.OverlapBoxNonAlloc(center, halfExtents, buffer, rotation, mask);
            if (count < buffer.Length) return buffer;
        }
        Collider[] results = Physics.OverlapBox(center, halfExtents, rotation, mask);
        count = results.Length;
        return results;
    }
}
