using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public static class GameDemoPlayerPrefabBuilder
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Character/Player 1.prefab";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string MainScenePath = "Assets/Scenes/Main.unity";
    private const string PlayerLayerName = "Player";
    private const string SpawnPointName = "PlayerSpawnPoint";

    [MenuItem("Tools/Game Demo Player/Build Player Layer")]
    public static void BuildAll()
    {
        int playerLayer = EnsurePlayerLayer();
        BuildPlayerPrefab(playerLayer);
        ConnectMainScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateAll();
        Debug.Log("[GameDemo Player] Player Layer, input, center spawn and Enemy target are connected.");
    }

    [MenuItem("Tools/Game Demo Player/Validate Player Layer")]
    public static void ValidateAll()
    {
        var failures = new List<string>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        PlayerRuntime prefabRuntime = prefab != null ? prefab.GetComponent<PlayerRuntime>() : null;
        if (prefab == null)
            failures.Add("Player 1 prefab is missing.");
        else
        {
            if (prefabRuntime == null) failures.Add("PlayerRuntime is missing from Player 1 prefab.");
            if (prefab.GetComponent<CharacterController>() == null) failures.Add("CharacterController is missing.");
            if (prefab.GetComponentInChildren<Animator>(true) == null) failures.Add("Animator is missing.");
            PlayerController oldController = prefab.GetComponent<PlayerController>();
            PlayerComboAttack oldAttack = prefab.GetComponent<PlayerComboAttack>();
            if (oldController != null && oldController.enabled) failures.Add("Legacy PlayerController must be disabled.");
            if (oldAttack != null && oldAttack.enabled) failures.Add("Legacy PlayerComboAttack must be disabled.");
        }

        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        PlayerRuntime runtime = UnityEngine.Object.FindFirstObjectByType<PlayerRuntime>(FindObjectsInactive.Include);
        CoreBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<CoreBootstrap>(FindObjectsInactive.Include);
        EnemyRuntime enemy = UnityEngine.Object.FindFirstObjectByType<EnemyRuntime>(FindObjectsInactive.Include);
        GameObject spawnObject = GameObject.Find(SpawnPointName);
        if (runtime == null) failures.Add("Main scene PlayerRuntime is missing.");
        if (spawnObject == null) failures.Add("PlayerSpawnPoint is missing.");
        if (runtime != null && spawnObject != null && runtime.transform.position != spawnObject.transform.position)
            failures.Add("Player is not positioned at PlayerSpawnPoint.");
        if (bootstrap == null) failures.Add("CoreBootstrap is missing.");
        else
        {
            var serialized = new SerializedObject(bootstrap);
            if (serialized.FindProperty("playerRuntime").objectReferenceValue != runtime)
                failures.Add("CoreBootstrap PlayerRuntime reference is missing.");
        }
        if (enemy == null || runtime == null || enemy.TargetTransform != runtime.TargetTransform)
            failures.Add("EnemyRuntime does not target the Player.");

        if (failures.Count > 0)
            throw new InvalidOperationException("[GameDemo Player] Validation failed:\n" + string.Join("\n", failures));
        Debug.Log($"[GameDemo Player] Validation passed in {scene.name}.");
    }

    private static void BuildPlayerPrefab(int playerLayer)
    {
        InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (actions == null)
            throw new InvalidOperationException("InputSystem_Actions asset is missing.");

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            root.layer = playerLayer;
            root.tag = "Player";
            Animator animator = root.GetComponentInChildren<Animator>(true);
            CharacterController controller = root.GetComponent<CharacterController>();
            if (animator == null || controller == null)
                throw new InvalidOperationException("Player 1 requires an Animator and CharacterController.");

            PlayerController legacyController = root.GetComponent<PlayerController>();
            PlayerComboAttack legacyAttack = root.GetComponent<PlayerComboAttack>();
            if (legacyController != null) legacyController.enabled = false;
            if (legacyAttack != null) legacyAttack.enabled = false;

            PlayerRuntime runtime = root.GetComponent<PlayerRuntime>();
            if (runtime == null)
                runtime = root.AddComponent<PlayerRuntime>();

            var serialized = new SerializedObject(runtime);
            serialized.Update();
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.FindProperty("characterController").objectReferenceValue = controller;
            serialized.FindProperty("inputActions").objectReferenceValue = actions;
            if (legacyController != null)
            {
                serialized.FindProperty("moveSpeed").floatValue = legacyController.moveSpeed;
                serialized.FindProperty("rotationSpeed").floatValue = legacyController.rotationSpeed;
                serialized.FindProperty("acceleration").floatValue = legacyController.acceleration;
            }
            if (legacyAttack != null)
            {
                serialized.FindProperty("comboWindowStart").floatValue = legacyAttack.comboWindowStart;
                serialized.FindProperty("comboWindowEnd").floatValue = legacyAttack.comboWindowEnd;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtime);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConnectMainScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        CoreBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<CoreBootstrap>(FindObjectsInactive.Include);
        EnemyRuntime enemy = UnityEngine.Object.FindFirstObjectByType<EnemyRuntime>(FindObjectsInactive.Include);
        if (bootstrap == null || enemy == null)
            throw new InvalidOperationException("Main scene requires CoreBootstrap and EnemyRuntime.");

        GameObject center = GameObject.Find("Center_Map");
        if (center == null)
            throw new InvalidOperationException("Center_Map was not found in Main scene.");

        GameObject spawnObject = GameObject.Find(SpawnPointName);
        if (spawnObject == null)
        {
            spawnObject = new GameObject(SpawnPointName);
            spawnObject.transform.SetParent(center.transform, false);
        }
        spawnObject.transform.localPosition = Vector3.zero;
        spawnObject.transform.localRotation = Quaternion.identity;

        PlayerRuntime runtime = UnityEngine.Object.FindFirstObjectByType<PlayerRuntime>(FindObjectsInactive.Include);
        if (runtime == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Player 1 prefab could not be loaded.");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "Player";
            runtime = instance.GetComponent<PlayerRuntime>();
        }

        runtime.transform.SetPositionAndRotation(spawnObject.transform.position, spawnObject.transform.rotation);
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            throw new InvalidOperationException("Main scene requires a camera tagged MainCamera.");

        var serializedRuntime = new SerializedObject(runtime);
        serializedRuntime.Update();
        serializedRuntime.FindProperty("spawnPoint").objectReferenceValue = spawnObject.transform;
        serializedRuntime.FindProperty("cameraTransform").objectReferenceValue = mainCamera.transform;
        serializedRuntime.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(runtime);

        var serializedBootstrap = new SerializedObject(bootstrap);
        serializedBootstrap.Update();
        serializedBootstrap.FindProperty("playerRuntime").objectReferenceValue = runtime;
        serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bootstrap);

        var serializedEnemy = new SerializedObject(enemy);
        serializedEnemy.Update();
        serializedEnemy.FindProperty("target").objectReferenceValue = runtime.TargetTransform;
        serializedEnemy.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(enemy);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static int EnsurePlayerLayer()
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets.Length == 0)
            throw new InvalidOperationException("TagManager.asset could not be loaded.");

        var serialized = new SerializedObject(assets[0]);
        SerializedProperty layers = serialized.FindProperty("layers");
        int emptyIndex = -1;
        for (int i = 8; i < layers.arraySize; i++)
        {
            string value = layers.GetArrayElementAtIndex(i).stringValue;
            if (value == PlayerLayerName)
                return i;
            if (emptyIndex < 0 && string.IsNullOrEmpty(value))
                emptyIndex = i;
        }

        if (emptyIndex < 0)
            throw new InvalidOperationException("No free user layer is available for Player.");
        layers.GetArrayElementAtIndex(emptyIndex).stringValue = PlayerLayerName;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return emptyIndex;
    }
}
