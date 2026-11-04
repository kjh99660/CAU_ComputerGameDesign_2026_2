using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class GameDemoMechaEnemyBuilder
{
    private const string ModelPath = "Assets/Models/Enemy/MechaRobot/MechaRobot.fbx";
    private const string PrefabPath = "Assets/Prefabs/Enemy/EnemyPrototype.prefab";
    private const string AnimationFolder = "Assets/Animations/Enemy/MechaRobot";
    private const string MaterialPath = "Assets/Models/Enemy/MechaRobot/MechaRobot.mat";
    private const string ControllerPath = AnimationFolder + "/MechaRobotEnemy.controller";
    private const float TargetHeight = 2f;

    public static void BuildAll()
    {
        EnsureFolders();
        ConfigureModelImporter();
        ConfigureTextureImporters();
        Material material = BuildMaterial();
        Dictionary<string, AnimationClip> clips = BuildClips();
        AnimatorController controller = BuildController(clips);
        BuildPrefab(material, controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateAll();
        Debug.Log("[Mecha Enemy] Static model, transform animation clips, controller and Enemy prefab were built.");
    }

    public static void ValidateAll()
    {
        var failures = new List<string>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            failures.Add("EnemyPrototype prefab is missing.");
        else
        {
            if (prefab.GetComponent<EnemyAgent>() == null) failures.Add("EnemyAgent is missing.");
            if (prefab.GetComponent<Rigidbody>() == null) failures.Add("Rigidbody is missing.");
            if (prefab.GetComponent<BoxCollider>() == null) failures.Add("BoxCollider is missing.");
            Animator animator = prefab.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) failures.Add("Animator Controller is missing.");
            if (prefab.transform.Find("Visual") == null) failures.Add("Visual animation root is missing.");
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) failures.Add("Mecha renderer is missing.");
            else if (renderers[0].sharedMaterial == null ||
                     renderers[0].sharedMaterial.GetTexture("_BaseMap") == null)
                failures.Add("Mecha material or Base Color texture is missing.");
            if (prefab.GetComponent<MeshFilter>() != null || prefab.GetComponent<MeshRenderer>() != null)
                failures.Add("Prototype cube renderer was not removed.");
        }

        string[] clipNames = { "Idle", "Chase", "Attack", "Knockback", "Dying" };
        foreach (string clipName in clipNames)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AnimationFolder}/{clipName}.anim");
            if (clip == null)
                failures.Add($"{clipName} clip is missing.");
            else if (AnimationUtility.GetCurveBindings(clip).Length == 0)
                failures.Add($"{clipName} clip has no animation curves.");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException("[Mecha Enemy] Validation failed:\n" + string.Join("\n", failures));
        Debug.Log("[Mecha Enemy] Validation passed.");
    }

    private static void ConfigureModelImporter()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException("MechaRobot FBX could not be imported.");
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.isReadable = false;
        importer.SaveAndReimport();
    }

    private static Material BuildMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            material = new Material(shader) { name = "MechaRobot" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        Texture2D baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Enemy/MechaRobot/mecha_robot_3d_model_basecolor.JPEG");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Enemy/MechaRobot/mecha_robot_3d_model_normal.PNG");
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", baseColor);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.65f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.5f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ConfigureTextureImporters()
    {
        ConfigureTexture("Assets/Models/Enemy/MechaRobot/mecha_robot_3d_model_basecolor.JPEG", false, true);
        ConfigureTexture("Assets/Models/Enemy/MechaRobot/mecha_robot_3d_model_normal.PNG", true, false);
    }

    private static void ConfigureTexture(string path, bool normalMap, bool sRgb)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Texture could not be imported: {path}");
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = sRgb;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }

    private static Dictionary<string, AnimationClip> BuildClips()
    {
        var clips = new Dictionary<string, AnimationClip>
        {
            ["Idle"] = CreateClip("Idle", 2f, true,
                PositionY((0f, 0f), (0.5f, 0.025f), (1f, 0f), (1.5f, 0.025f), (2f, 0f)),
                RotationZ((0f, -0.8f), (1f, 0.8f), (2f, -0.8f))),
            ["Chase"] = CreateClip("Chase", 0.5f, true,
                PositionY((0f, 0f), (0.125f, 0.07f), (0.25f, 0f), (0.375f, 0.07f), (0.5f, 0f)),
                RotationZ((0f, -2.5f), (0.25f, 2.5f), (0.5f, -2.5f))),
            ["Attack"] = CreateClip("Attack", 0.35f, false,
                PositionZ((0f, 0f), (0.1f, -0.08f), (0.22f, 0.42f), (0.35f, 0f)),
                RotationX((0f, 0f), (0.1f, 8f), (0.22f, -12f), (0.35f, 0f))),
            ["Knockback"] = CreateClip("Knockback", 1.25f, false,
                PositionY((0f, 0f), (0.18f, 0.12f), (1.25f, 0f)),
                RotationX((0f, 0f), (0.3f, 18f), (1.25f, 28f))),
            ["Dying"] = CreateClip("Dying", 0.25f, false,
                PositionY((0f, 0f), (0.25f, -0.65f)),
                RotationZ((0f, 0f), (0.25f, 78f)))
        };
        return clips;
    }

    private static AnimationClip CreateClip(string name, float length, bool loop, params CurveBinding[] bindings)
    {
        string path = $"{AnimationFolder}/{name}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name, frameRate = 60f };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        foreach (CurveBinding binding in bindings)
            clip.SetCurve("Visual", typeof(Transform), binding.Property, binding.Curve);
        SetLoop(clip, loop);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        machine.states = Array.Empty<ChildAnimatorState>();
        foreach (string name in new[] { "Idle", "Chase", "Attack", "Knockback", "Dying" })
        {
            AnimatorState state = machine.AddState(name);
            state.motion = clips[name];
            state.writeDefaultValues = true;
            if (name == "Idle") machine.defaultState = state;
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void BuildPrefab(Material material, RuntimeAnimatorController controller)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
            throw new InvalidOperationException("MechaRobot model asset is missing.");

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.transform.localScale = Vector3.one;
            MeshFilter oldFilter = root.GetComponent<MeshFilter>();
            MeshRenderer oldRenderer = root.GetComponent<MeshRenderer>();
            if (oldRenderer != null) UnityEngine.Object.DestroyImmediate(oldRenderer);
            if (oldFilter != null) UnityEngine.Object.DestroyImmediate(oldFilter);

            Transform oldVisual = root.transform.Find("Visual");
            if (oldVisual != null) UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.scene);
            model.name = "MechaRobotModel";
            model.transform.SetParent(visual.transform, false);

            Renderer renderer = model.GetComponentInChildren<Renderer>(true);
            MeshFilter filter = model.GetComponentInChildren<MeshFilter>(true);
            if (renderer == null || filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("MechaRobot requires a static mesh renderer.");

            Bounds bounds = filter.sharedMesh.bounds;
            float scale = TargetHeight / Mathf.Max(0.001f, bounds.size.y);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
            renderer.sharedMaterial = material;

            Animator animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            BoxCollider collider = root.GetComponent<BoxCollider>();
            collider.center = new Vector3(0f, TargetHeight * 0.5f, 0f);
            collider.size = new Vector3(
                Mathf.Clamp(bounds.size.x * scale * 0.72f, 0.8f, 1.4f),
                TargetHeight,
                Mathf.Clamp(bounds.size.z * scale * 0.72f, 0.8f, 1.4f));

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SetLoop(AnimationClip clip, bool loop)
    {
        var serialized = new SerializedObject(clip);
        serialized.FindProperty("m_AnimationClipSettings.m_LoopTime").boolValue = loop;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private readonly struct CurveBinding
    {
        public CurveBinding(string property, AnimationCurve curve) { Property = property; Curve = curve; }
        public string Property { get; }
        public AnimationCurve Curve { get; }
    }

    private static CurveBinding PositionY(params (float time, float value)[] keys) => Curve("m_LocalPosition.y", keys);
    private static CurveBinding PositionZ(params (float time, float value)[] keys) => Curve("m_LocalPosition.z", keys);
    private static CurveBinding RotationX(params (float time, float value)[] keys) => Curve("localEulerAnglesRaw.x", keys);
    private static CurveBinding RotationZ(params (float time, float value)[] keys) => Curve("localEulerAnglesRaw.z", keys);
    private static CurveBinding Curve(string property, params (float time, float value)[] keys)
    {
        var keyframes = new Keyframe[keys.Length];
        for (int i = 0; i < keys.Length; i++) keyframes[i] = new Keyframe(keys[i].time, keys[i].value);
        var curve = new AnimationCurve(keyframes);
        for (int i = 0; i < keyframes.Length; i++)
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
        return new CurveBinding(property, curve);
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder("Assets/Animations/Enemy")) AssetDatabase.CreateFolder("Assets/Animations", "Enemy");
        if (!AssetDatabase.IsValidFolder(AnimationFolder)) AssetDatabase.CreateFolder("Assets/Animations/Enemy", "MechaRobot");
    }
}
