using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public sealed class PlayerSystemsTests
{
    [Test]
    public void StateBeforeAwakePreservesAnimatorResumeSpeed()
    {
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Character/Player.prefab");
        try
        {
            PlayerRuntime runtime = root.GetComponent<PlayerRuntime>();
            Animator animator = root.GetComponent<Animator>();
            float speed = animator.speed;
            runtime.ApplyState(GameState.Initializing, 1);
            Assert.AreEqual(0f, animator.speed);
            runtime.ApplyState(GameState.Battle, 1);
            Assert.AreEqual(speed, animator.speed);
            runtime.ApplyState(GameState.Paused, 1);
            Assert.AreEqual(0f, animator.speed);
            runtime.ApplyState(GameState.Finishing, 1);
            Assert.AreEqual(speed, animator.speed);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void PlayerPrefabKeepsAnimationRigAndUsesRuntimeControls()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Character/Player.prefab");
        Assert.IsNotNull(prefab);
        PlayerRuntime runtime = prefab.GetComponent<PlayerRuntime>();
        Assert.IsNotNull(runtime);
        Assert.IsTrue(runtime.enabled);
        var serialized = new SerializedObject(runtime);
        Assert.AreSame(prefab.GetComponent<Animator>(), serialized.FindProperty("animator").objectReferenceValue);
        Assert.AreSame(prefab.GetComponent<CharacterController>(), serialized.FindProperty("characterController").objectReferenceValue);
        Assert.IsNotNull(serialized.FindProperty("inputActions").objectReferenceValue);
        Assert.AreEqual(2, prefab.GetComponentsInChildren<Animator>(true).Length);

        foreach (MonoBehaviour component in prefab.GetComponents<MonoBehaviour>())
        {
            Assert.IsNotNull(component, "Player must not contain missing scripts.");
            string type = component.GetType().Name;
            if (type == "PlayerController" || type == "PlayerComboAttack")
                Assert.IsFalse(component.enabled, "Legacy input must not run alongside PlayerRuntime.");
            if (type == "ReferenceAnimatorSync")
            {
                var sync = new SerializedObject(component);
                Assert.AreSame(prefab.GetComponent<Animator>(), sync.FindProperty("mine").objectReferenceValue);
                Assert.IsNotNull(sync.FindProperty("reference").objectReferenceValue);
            }
        }

        MonoBehaviour relay = System.Array.Find(prefab.GetComponents<MonoBehaviour>(), c => c is IPlayerPresentation);
        Assert.IsNotNull(relay, "Sword events must receive the round lifecycle.");
        var sword = new SerializedObject(relay).FindProperty("sword").objectReferenceValue;
        Assert.IsNotNull(sword);
        var swordData = new SerializedObject(sword);
        foreach (string reference in new[] { "refWeapon", "refRightHand", "myRightHand", "mySheath" })
            Assert.IsNotNull(swordData.FindProperty(reference).objectReferenceValue, reference);
    }

    [Test]
    public void CharacterTestKeepsStandaloneAnimationControls()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/CharacterTest.unity");
            PlayerRuntime runtime = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out PlayerRuntime candidate)) runtime = candidate;
            Assert.IsNotNull(runtime);
            Assert.IsFalse(runtime.enabled);
            foreach (MonoBehaviour component in runtime.GetComponents<MonoBehaviour>())
            {
                string type = component.GetType().Name;
                if (type == "PlayerController" || type == "PlayerComboAttack")
                    Assert.IsTrue(component.enabled, "Animation sandbox must retain its standalone input.");
            }
        }
        finally
        {
            if (System.Array.Exists(setup, scene => scene.isLoaded && scene.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    [Test]
    public void PlayerInputIsCapturedOnlyDuringBattle()
    {
        var runtime = new FakePlayerRuntime();
        var system = new PlayerInputSystem(runtime);

        system.OnGameStateChanged(new GameStateChangedEvent(GameState.Initializing, GameState.Ready, 1));
        system.Update(0.02f, 0.02f);
        Assert.AreEqual(0, runtime.CaptureCount);

        system.OnGameStateChanged(new GameStateChangedEvent(GameState.Ready, GameState.Battle, 1));
        system.Update(0.02f, 0.02f);
        Assert.AreEqual(1, runtime.CaptureCount);
        Assert.AreEqual(GameState.Battle, runtime.State);
    }

    [Test]
    public void PlayerActionContinuesInFinishingWithoutCapturingNewInput()
    {
        var runtime = new FakePlayerRuntime();
        var input = new PlayerInputSystem(runtime);
        var action = new PlayerActionSystem(runtime);
        var notification = new GameStateChangedEvent(GameState.Battle, GameState.Finishing, 3);

        input.OnGameStateChanged(notification);
        action.OnGameStateChanged(notification);
        input.Update(0.02f, 0.02f);
        action.Update(0.02f, 0.02f);

        Assert.AreEqual(0, runtime.CaptureCount);
        Assert.AreEqual(1, runtime.ActionCount);
    }

    [Test]
    public void EnemyTargetUsesRegisteredPlayerTransform()
    {
        GameObject enemyObject = new("EnemyRuntime");
        GameObject playerObject = new("Player");
        try
        {
            EnemyRuntime runtime = enemyObject.AddComponent<EnemyRuntime>();
            runtime.SetTarget(playerObject.transform);
            Assert.AreSame(playerObject.transform, runtime.TargetTransform);
        }
        finally
        {
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(playerObject);
        }
    }

    private sealed class FakePlayerRuntime : IPlayerRuntime
    {
        public Transform TargetTransform => null;
        public GameState State { get; private set; }
        public int CaptureCount { get; private set; }
        public int ActionCount { get; private set; }

        public void ApplyState(GameState state, int tick) => State = state;
        public void CaptureInput() => CaptureCount++;
        public void UpdateAction(float deltaTime) => ActionCount++;
    }
}
