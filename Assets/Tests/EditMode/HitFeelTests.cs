using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Hit-feel rules and assets that need no scene: the special's own hits are recognised, the enemy's body parts are
// built from its mesh, and the hit stop and shake values keep their order (light < heavy < special).
public sealed class HitFeelTests
{
    [Test]
    public void TheFrameTheSpecialIsUsedMarksItsHits()
    {
        var settings = ScriptableObject.CreateInstance<CombatSettings>();
        try
        {
            var stats = new RoundCombatStats(settings);
            stats.Reset(1);
            stats.SetAccepting(true);
            var window = new SpecialHitWindow(stats);
            Assert.IsFalse(window.IsSpecialHit);
            stats.RecordEnemyKill(1);
            Assert.IsFalse(window.IsSpecialHit, "A normal kill is not the special.");
            bool used = false;
            for (int i = 0; i < 200 && !used; i++)
            {
                stats.RecordEnemyKill(1);
                used = stats.TryUseSpecial(1);
            }
            Assert.IsTrue(used);
            Assert.IsTrue(window.IsSpecialHit, "Hits in the frame the special was used belong to it.");
            window.Dispose();
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void TheEnemyBreaksIntoBodyPartsMadeFromItsOwnMesh()
    {
        var settings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        Assert.IsNotNull(settings.debrisSet, "Built by Tools/REBOOT Feel/Build Enemy Remains");
        Assert.IsNotNull(settings.remainsMaterial);
        Assert.IsNotNull(settings.remainsBodyModel);
        Assert.AreEqual("REBOOT/Enemy Remains", settings.remainsMaterial.shader.name);
        Assert.IsNotNull(settings.remainsBodyModel.GetComponentInChildren<SkinnedMeshRenderer>(true));
        var names = new HashSet<string>();
        foreach (EnemyDebrisSet.Part part in settings.debrisSet.parts)
        {
            Assert.IsNotNull(part.mesh, part.name);
            Assert.Greater(part.mesh.vertexCount, 100, part.name);
            Assert.Greater(part.bakedScale, 0f, part.name);
            Assert.That(part.radius, Is.InRange(0.04f, 1.5f), part.name);
            Assert.IsFalse(string.IsNullOrEmpty(part.bone), part.name);
            names.Add(part.name);
        }
        CollectionAssert.IsSubsetOf(new[] { "Head", "Chest", "Pelvis", "LeftUpperArm", "RightUpperArm", "LeftThigh", "RightThigh" }, names);
    }

    [Test]
    public void HeavierBlowsHoldAndShakeMoreAndLightHitsDoNotShake()
    {
        var settings = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
        Assert.Less(settings.hitStopLight, settings.hitStopHeavy);
        Assert.LessOrEqual(settings.hitStopMedium, settings.hitStopHeavy);
        Assert.LessOrEqual(settings.hitStopHeavy, settings.hitStopSpecial);
        Assert.LessOrEqual(settings.hitStopSpecial, 0.3f, "A hit stop stays a blink.");
        Assert.AreEqual(0f, settings.shakeLight, "Light hits keep the view readable.");
        Assert.Less(settings.shakeMedium, settings.shakeHeavy);
        Assert.Less(settings.shakeHeavy, settings.shakeSpecial);
        Assert.That(settings.cameraShakeAmount, Is.InRange(0f, 1f));
    }
}
