using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class EnemySkeletalAnimationTests
{
    private const string PrefabPath = "Assets/Prefabs/Enemy/EnemyPrototype.prefab";
    private const string Folder = "Assets/Animations/Enemy/Humanoid/";

    [Test]
    public void GameplayEnemyHasWeightedSkinAndOneAnimator()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Assert.IsNotNull(skin);
        Assert.GreaterOrEqual(skin.bones.Length, 50);
        Assert.AreEqual(skin.bones.Length, skin.sharedMesh.bindposes.Length);
        Assert.IsTrue(skin.bones.All(b => b != null && b.IsChildOf(prefab.transform)));
        Assert.AreEqual(1, prefab.GetComponentsInChildren<Animator>(true).Length);
        Assert.IsFalse(prefab.GetComponent<Animator>().applyRootMotion);
        var avatar = AssetDatabase.LoadAllAssetsAtPath("Assets/MeshyImports/EnemyHumanoid/EmenyHumanoid.fbx").OfType<Avatar>().Single();
        Assert.IsTrue(avatar.isValid);
        Assert.IsTrue(avatar.isHuman);
    }

    [TestCase("Idle", 0.125f, 0.625f)]
    [TestCase("Chase", 0.25f, 0.75f)]
    [TestCase("Attack", 0f, 0.5f)]
    [TestCase("Knockback", 0f, 0.5f)]
    [TestCase("Dying", 0f, 1f)]
    public void SkeletalClipDeformsVerticesWithoutMovingGameplayRoot(string name, float start, float end)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = Object.Instantiate(prefab);
        var firstMesh = new Mesh();
        var secondMesh = new Mesh();
        try
        {
            instance.GetComponent<Animator>().enabled = false;
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + name + ".anim");
            Assert.IsNotNull(clip);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.IsTrue(bindings.All(b => b.path.Contains("mixamorig:")));
            Assert.IsTrue(bindings.All(b => instance.transform.Find(b.path) != null));
            Vector3 position = instance.transform.position;
            clip.SampleAnimation(instance, start * clip.length);
            skin.BakeMesh(firstMesh);
            clip.SampleAnimation(instance, end * clip.length);
            skin.BakeMesh(secondMesh);
            var a = firstMesh.vertices;
            var b = secondMesh.vertices;
            int changed = 0;
            for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude > 0.00000001f) changed++;
            Assert.Greater(changed, 100, name + " must deform the actual skin, not just a wrapper object.");
            Assert.AreEqual(position, instance.transform.position);
        }
        finally
        {
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(firstMesh);
            Object.DestroyImmediate(secondMesh);
        }
    }

    [TestCase("Idle")]
    [TestCase("Chase")]
    public void LoopingPosesMeetAtSeam(string name)
    {
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        try
        {
            instance.GetComponent<Animator>().enabled = false;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + name + ".anim");
            Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(clip).loopTime);
            clip.SampleAnimation(instance, 0f);
            var bones = instance.GetComponentInChildren<SkinnedMeshRenderer>().bones;
            var initial = bones.Select(b => b.localRotation).ToArray();
            clip.SampleAnimation(instance, clip.length);
            for (int i = 0; i < bones.Length; i++) Assert.Less(Quaternion.Angle(initial[i], bones[i].localRotation), 0.1f);
        }
        finally { Object.DestroyImmediate(instance); }
    }
}
