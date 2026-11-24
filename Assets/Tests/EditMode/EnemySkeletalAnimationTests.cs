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
    [TestCase("Walk", 0.25f, 0.75f)]
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
    [TestCase("Walk")]
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

    // The prefab used to be fitted to padded renderer bounds, which left the enemy hovering 22 cm above the floor.
    [Test]
    public void ModelStandsOnTheGameplayRootPlane()
    {
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        try
        {
            Assert.AreEqual(0f, LowestVertex(instance) - instance.transform.position.y, 0.01f, "The soles must rest on the plane the gameplay root moves on.");
        }
        finally { Object.DestroyImmediate(instance); }
    }

    // While a foot is flat on the ground it must not move against the ground when the body travels at the speed the
    // clip is authored for. This is what "no foot sliding" means; the runtime then plays the clip at real / authored speed.
    [TestCase("Chase")]
    [TestCase("Walk")]
    public void PlantedFootStaysPutAtTheAuthoredSpeed(string name)
    {
        var settings = AssetDatabase.LoadAssetAtPath<EnemySettings>("Assets/Prefabs/Enemy/EnemySettings.asset");
        float speed = name == "Chase" ? settings.RunClipSpeed : settings.WalkClipSpeed;
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        try
        {
            instance.transform.position = Vector3.zero;
            instance.GetComponent<Animator>().enabled = false;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + name + ".anim");
            Transform[] bones = instance.GetComponentInChildren<SkinnedMeshRenderer>().bones;
            Transform[] feet = { bones.First(b => b.name == "mixamorig:LeftFoot"), bones.First(b => b.name == "mixamorig:RightFoot") };
            Transform hips = bones.First(b => b.name == "mixamorig:Hips");
            Quaternion[] flat = { feet[0].rotation, feet[1].rotation };
            float[] restHeight = { feet[0].position.y, feet[1].position.y };

            const int samples = 240;
            float step = clip.length / samples;
            var world = new Vector3[2, samples];
            var planted = new bool[2, samples];
            float hipsLow = float.MaxValue, hipsHigh = float.MinValue;
            int airborne = 0;
            for (int i = 0; i < samples; i++)
            {
                clip.SampleAnimation(instance, i * step);
                hipsLow = Mathf.Min(hipsLow, hips.position.y);
                hipsHigh = Mathf.Max(hipsHigh, hips.position.y);
                for (int f = 0; f < 2; f++)
                {
                    Vector3 p = feet[f].position;
                    world[f, i] = new Vector3(p.x, p.y, p.z + speed * i * step);   // where the ankle is if the body travels at the authored speed
                    planted[f, i] = Mathf.Abs(p.y - restHeight[f]) < 0.005f && Quaternion.Angle(flat[f], feet[f].rotation) < 1.5f;
                    Assert.Greater(p.y, restHeight[f] - 0.01f, "A foot must never sink below its standing height.");
                }
                if (!planted[0, i] && !planted[1, i]) airborne++;
            }

            float drift = 0f;
            int count = 0;
            for (int f = 0; f < 2; f++)
                for (int i = 1; i < samples; i++)
                {
                    if (!planted[f, i] || !planted[f, i - 1]) continue;
                    drift += Vector3.Distance(world[f, i], world[f, i - 1]) / step;
                    count++;
                }
            Assert.Greater(count, samples / 10, "Each foot needs a clear planted phase.");
            Assert.Less(drift / count, 0.3f, "A planted foot must stay where it is (m/s against the ground at the authored speed).");
            Assert.Greater(hipsHigh - hipsLow, 0.01f, "The hips must rise and fall with the steps; a level pelvis is what made the old gait glide.");
            if (name == "Walk")
                Assert.Less(airborne, samples / 2, "A walk keeps a foot on or rolling over the ground most of the cycle.");
        }
        finally { Object.DestroyImmediate(instance); }
    }

    private static float LowestVertex(GameObject instance)
    {
        var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
        Mesh mesh = skin.sharedMesh;
        Transform[] bones = skin.bones;
        Matrix4x4[] bind = mesh.bindposes;
        BoneWeight[] weights = mesh.boneWeights;
        Vector3[] vertices = mesh.vertices;
        float lowest = float.MaxValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            int bone = weights[i].boneIndex0;
            lowest = Mathf.Min(lowest, bones[bone].localToWorldMatrix.MultiplyPoint3x4(bind[bone].MultiplyPoint3x4(vertices[i])).y);
        }
        return lowest;
    }
}
