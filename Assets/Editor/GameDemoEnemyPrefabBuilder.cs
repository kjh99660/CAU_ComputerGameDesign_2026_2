using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameDemoEnemyPrefabBuilder
{
    private const string EnemyFolder = "Assets/Prefabs/Enemy";
    private const string PrefabPath = EnemyFolder + "/EnemyPrototype.prefab";
    private const string MaterialPath = EnemyFolder + "/EnemyPrototype.mat";
    private const string SettingsPath = EnemyFolder + "/EnemySettings.asset";
    private const string MainScenePath = "Assets/Scenes/Main.unity";
    private const string EnemyLayerName = "Enemy";

    [MenuItem("Tools/Game Demo Enemy/Build Prototype Enemy")]
    public static void BuildAll()
    {
        EnsureFolder("Assets/Prefabs", "Enemy");
        int enemyLayer = EnsureEnemyLayer();
        EnemySettings settings = GetOrCreateSettings();
        Material material = GetOrCreateMaterial();
        EnemyAgent prefab = BuildPrefab(enemyLayer, material);
        ConnectMainScene(prefab, settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateAll();
        Debug.Log("[GameDemo Enemy] Built Enemy layer, Cube prefab, settings and Main scene runtime.");
    }

    [MenuItem("Tools/Game Demo Enemy/Validate Prototype Enemy")]
    public static void ValidateAll()
    {
        var failures = new List<string>();
        int enemyLayer = LayerMask.NameToLayer(EnemyLayerName);
        if (enemyLayer < 0)
            failures.Add("Enemy physics layer is missing.");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            failures.Add("EnemyPrototype prefab is missing.");
        else
        {
            if (prefab.layer != enemyLayer) failures.Add("EnemyPrototype is not on the Enemy layer.");
            if (prefab.GetComponent<MeshFilter>() == null) failures.Add("MeshFilter is missing.");
            if (prefab.GetComponent<MeshRenderer>() == null) failures.Add("MeshRenderer is missing.");
            if (prefab.GetComponent<BoxCollider>() == null) failures.Add("BoxCollider is missing.");
            if (prefab.GetComponent<Rigidbody>() == null) failures.Add("Rigidbody is missing.");
            if (prefab.GetComponent<EnemyAgent>() == null) failures.Add("EnemyAgent is missing.");
            if (prefab.GetComponent<Animator>() != null) failures.Add("Prototype must not have an Animator.");
        }

        if (AssetDatabase.LoadAssetAtPath<EnemySettings>(SettingsPath) == null)
            failures.Add("EnemySettings asset is missing.");

        CoreBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<CoreBootstrap>(FindObjectsInactive.Include);
        EnemyRuntime runtime = bootstrap != null ? bootstrap.GetComponent<EnemyRuntime>() : null;
        if (runtime == null)
            failures.Add("Main scene EnemyRuntime is missing.");
        else
        {
            var serializedRuntime = new SerializedObject(runtime);
            if (serializedRuntime.FindProperty("enemyPrefab").objectReferenceValue == null)
                failures.Add("EnemyRuntime prefab reference is missing.");
            if (serializedRuntime.FindProperty("settings").objectReferenceValue == null)
                failures.Add("EnemyRuntime settings reference is missing.");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("[GameDemo Enemy] Validation failed:\n" + string.Join("\n", failures));
        Debug.Log("[GameDemo Enemy] Validation passed.");
    }

    private static int EnsureEnemyLayer()
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
            if (value == EnemyLayerName)
                return i;
            if (emptyIndex < 0 && string.IsNullOrEmpty(value))
                emptyIndex = i;
        }

        if (emptyIndex < 0)
            throw new InvalidOperationException("No free user layer is available for Enemy.");
        layers.GetArrayElementAtIndex(emptyIndex).stringValue = EnemyLayerName;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return emptyIndex;
    }

    private static EnemySettings GetOrCreateSettings()
    {
        EnemySettings settings = AssetDatabase.LoadAssetAtPath<EnemySettings>(SettingsPath);
        if (settings != null)
            return settings;
        settings = ScriptableObject.CreateInstance<EnemySettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        return settings;
    }

    private static Material GetOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            throw new InvalidOperationException("No compatible shader was found for EnemyPrototype.");

        material = new Material(shader) { name = "EnemyPrototype" };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.9f, 0.22f, 0.18f, 1f));
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static EnemyAgent BuildPrefab(int layer, Material material)
    {
        GameObject prototype = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            prototype.name = "EnemyPrototype";
            prototype.layer = layer;
            prototype.transform.localScale = new Vector3(1f, 2f, 1f);
            prototype.GetComponent<MeshRenderer>().sharedMaterial = material;
            Rigidbody body = prototype.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.useGravity = true;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            prototype.AddComponent<EnemyAgent>();

            PrefabUtility.SaveAsPrefabAsset(prototype, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return saved != null ? saved.GetComponent<EnemyAgent>() : null;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prototype);
        }
    }

    private static void ConnectMainScene(EnemyAgent prefab, EnemySettings settings)
    {
        if (prefab == null || settings == null)
            throw new InvalidOperationException("Enemy prefab and settings must be persistent assets before scene binding.");

        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        // Scene Open 중 미사용 Asset이 Unload될 수 있으므로 열린 뒤 영속 Asset을 다시 조회한다.
        GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        prefab = prefabRoot != null ? prefabRoot.GetComponent<EnemyAgent>() : null;
        settings = AssetDatabase.LoadAssetAtPath<EnemySettings>(SettingsPath);
        if (prefab == null || settings == null)
            throw new InvalidOperationException("Enemy prefab or settings could not be reloaded after opening Main scene.");

        CoreBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<CoreBootstrap>(FindObjectsInactive.Include);
        if (bootstrap == null)
            throw new InvalidOperationException("CoreBootstrap was not found in Main scene.");

        EnemyRuntime runtime = bootstrap.GetComponent<EnemyRuntime>();
        if (runtime == null)
            runtime = bootstrap.gameObject.AddComponent<EnemyRuntime>();

        var serializedRuntime = new SerializedObject(runtime);
        serializedRuntime.Update();
        serializedRuntime.FindProperty("enemyPrefab").objectReferenceValue = prefab;
        serializedRuntime.FindProperty("settings").objectReferenceValue = settings;
        serializedRuntime.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(runtime);

        var serializedBootstrap = new SerializedObject(bootstrap);
        serializedBootstrap.Update();
        serializedBootstrap.FindProperty("enemyRuntime").objectReferenceValue = runtime;
        serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bootstrap);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }
}
