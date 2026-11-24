using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// Pure rules and assets behind the combat feedback: the camera zoom pulse curve, the enemy's attack looks and its
// glow material. What they do in the running game is covered by CombatFeedbackSceneTests.
public sealed class CombatFeedbackTests
{
    private const float Step = 1f / 240f;
    private const string EnemyPrefab = "Assets/Prefabs/Enemy/EnemyPrototype.prefab";
    private const string ClipFolder = "Assets/Animations/Enemy/Humanoid/";

    [Test]
    public void ZoomPulsePushesInSwingsPastRestAndEnds()
    {
        var pulse = new ZoomPulse();
        Assert.IsFalse(pulse.Active);
        pulse.Begin(2.6f, 0.05f, 0.24f, 0.2f);
        Assert.IsTrue(pulse.Active);

        float highest = 0f, lowest = 0f, previous = 0f, biggestJump = 0f, timeOfPeak = 0f;
        for (float time = Step; time < 0.4f; time += Step)
        {
            float value = pulse.Advance(Step);
            if (value > highest) { highest = value; timeOfPeak = time; }
            lowest = Mathf.Min(lowest, value);
            biggestJump = Mathf.Max(biggestJump, Mathf.Abs(value - previous));
            previous = value;
        }
        Assert.AreEqual(2.6f, highest, 0.05f, "It reaches the asked strength.");
        Assert.AreEqual(0.05f, timeOfPeak, 0.01f, "The push in takes the asked time.");
        Assert.AreEqual(-0.52f, lowest, 0.05f, "On the way out it swings past rest by the overshoot share.");
        Assert.Less(biggestJump, 0.5f, "The view never jumps from one frame to the next.");
        Assert.IsFalse(pulse.Active, "It ends by itself.");
        Assert.AreEqual(0f, pulse.Value);
    }

    [Test]
    public void ZoomPulseKeepsAStrongPulseWhenAWeakOneArrives()
    {
        var pulse = new ZoomPulse();
        pulse.Begin(2.6f, 0.05f, 0.24f, 0.2f);
        for (int i = 0; i < 12; i++) pulse.Advance(Step);   // at the peak
        float atPeak = pulse.Value;

        pulse.Begin(0.8f, 0.05f, 0.24f, 0.2f);
        Assert.GreaterOrEqual(pulse.Advance(Step), atPeak - 0.05f, "A weak pulse must not pull the view back out first.");

        pulse.Begin(0f, 0.05f, 0.24f, 0.2f);
        Assert.GreaterOrEqual(pulse.Value, atPeak - 0.05f, "A pulse of zero changes nothing.");

        pulse.Clear();
        Assert.IsFalse(pulse.Active);
        Assert.AreEqual(0f, pulse.Advance(Step));
    }

    [Test]
    public void AttackLooksShareTheAttackLengthAndStrikeDifferently()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        var attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + "Attack.anim");
        var hook = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + "Attack2.anim");
        var smash = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + "Attack3.anim");
        Assert.IsNotNull(hook, "Attack2 clip");
        Assert.IsNotNull(smash, "Attack3 clip");
        Assert.AreEqual(attack.length, hook.length, 0.0001f, "Every look lasts as long as the attack, so the hit timing is shared.");
        Assert.AreEqual(attack.length, smash.length, 0.0001f);

        // Forward reach of each hand at the contact moment (50%), relative to the root, and the hands at the top
        // of the smash's lift (30%).
        Vector3 Hand(AnimationClip clip, float share, string side)
        {
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clip.SampleAnimation(instance, clip.length * share);
                return Find(instance.transform, "mixamorig:" + side + "Hand").position;
            }
            finally { Object.DestroyImmediate(instance); }
        }
        float headHeight;
        {
            GameObject instance = Object.Instantiate(prefab);
            attack.SampleAnimation(instance, 0f);
            headHeight = Find(instance.transform, "mixamorig:Head").position.y - instance.transform.position.y;
            Object.DestroyImmediate(instance);
        }

        Assert.Greater(Hand(attack, 0.5f, "Right").z, Hand(attack, 0.5f, "Left").z + 0.15f, "Attack strikes with the right hand.");
        Assert.Greater(Hand(hook, 0.5f, "Left").z, Hand(hook, 0.5f, "Right").z + 0.15f, "Attack2 strikes with the left hand.");
        Assert.Greater(Hand(hook, 0.5f, "Left").z, 0.3f, "The left hand reaches forward at contact.");
        Assert.Greater(Hand(smash, 0.3f, "Left").y, headHeight, "Attack3 lifts both hands above the head first.");
        Assert.Greater(Hand(smash, 0.3f, "Right").y, headHeight);
        Assert.Greater(Hand(smash, 0.5f, "Left").z, 0.2f, "Both hands are in front at contact.");
        Assert.Greater(Hand(smash, 0.5f, "Right").z, 0.2f);
        Assert.Less(Hand(smash, 0.5f, "Left").y, headHeight, "They have come down by the contact moment.");
    }

    [Test]
    public void EnemyPrefabUsesTheGlowMaterialWithItsMask()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
        var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Material material = skin.sharedMaterial;
        Assert.AreEqual("REBOOT/Enemy Lit", material.shader.name);
        Assert.IsNotNull(material.GetTexture("_BaseMap"), "The colour texture of the model is kept.");
        var mask = material.GetTexture("_GlowMap") as Texture2D;
        Assert.IsNotNull(mask, "Glow mask");
        Assert.Greater(material.GetFloat("_GlowStrength"), 0f);
        Assert.Greater(material.GetFloat("_RimStrength"), 0f);

        // Only parts of the body glow: the mask must be mostly dark with some lit texels.
        var readable = new Texture2D(2, 2, TextureFormat.RGB24, false);
        try
        {
            Assert.IsTrue(readable.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(mask))));
            Color32[] texels = readable.GetPixels32();
            int lit = 0;
            foreach (Color32 texel in texels) if (texel.r > 128) lit++;
            float share = (float)lit / texels.Length;
            Assert.Greater(share, 0.001f, "Some texels glow.");
            Assert.Less(share, 0.05f, "Only a small part of the body glows.");
        }
        finally { Object.DestroyImmediate(readable); }
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        Assert.Fail("Bone not found: " + name);
        return null;
    }
}
