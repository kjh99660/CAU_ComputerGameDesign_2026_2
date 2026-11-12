using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Authors deterministic skeletal poses against the imported bind/rest hierarchy.
// Clips use Transform curves, so they do not need retargeting or modify the source Avatar.
public static class GameDemoHumanoidEnemyBuilder
{
    public const string ModelPath = "Assets/MeshyImports/EnemyHumanoid/EmenyHumanoid.fbx";
    public const string PrefabPath = "Assets/Prefabs/Enemy/EnemyPrototype.prefab";
    public const string Folder = "Assets/Animations/Enemy/Humanoid";
    private static readonly string[] States = { "Idle", "Chase", "Attack", "Knockback", "Dying" };

    [MenuItem("Tools/Game Demo Enemy/Build Humanoid Enemy")]
    public static void BuildAll()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Animations/Enemy", "Humanoid");
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null) throw new InvalidOperationException("Rigged enemy model is missing.");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // Keep the root EnemyAgent, body, collider, scale and their existing file IDs.
            Transform visual = root.transform.Find("Visual");
            foreach (Transform child in visual.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.scene);
            model.transform.SetParent(visual, false);
            // Unpack only this newly-created instance. Keep the original FBX/prefab untouched.
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "EnemyHumanoidModel";
            foreach (Animator nested in model.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(nested);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null || skin.bones.Length < 20) throw new InvalidOperationException("Model does not have a skinned skeleton.");
            // Fit the new model to the existing collision volume, including the root's configured scale.
            Bounds bounds = skin.bounds;
            float height = root.GetComponent<BoxCollider>().size.y;
            float factor = height * root.transform.lossyScale.y / bounds.size.y;
            model.transform.localScale *= factor;
            bounds = skin.bounds;
            Vector3 floorCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.transform.position += root.transform.position - floorCenter;

            Transform[] bones = model.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("mixamorig:", StringComparison.Ordinal)).ToArray();
            var settings = AssetDatabase.LoadAssetAtPath<EnemySettings>("Assets/Prefabs/Enemy/EnemySettings.asset");
            float[] durations = { 2f, 0.65f, settings.AttackDuration, settings.KnockbackFallbackDuration, Mathf.Max(0.05f, settings.DeathDuration) };
            var clips = new Dictionary<string, AnimationClip>();
            for (int i = 0; i < States.Length; i++)
                clips.Add(States[i], BuildClip(States[i], durations[i], i < 2, root.transform, model.transform, bones));
            AnimatorController controller = BuildController(clips);
            Animator animator = root.GetComponent<Animator>();
            animator.avatar = null; // Exact bone paths drive the skin directly; the FBX retains its Humanoid Avatar.
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        ValidateAll();
        Debug.Log("[Humanoid Enemy] Built five skeletal clips and connected the gameplay prefab.");
    }

    private static AnimationClip BuildClip(string state, float duration, bool loop, Transform root, Transform model, Transform[] bones)
    {
        string path = Folder + "/" + state + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = state }; AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = 30f;
        int steps = Mathf.Max(8, Mathf.CeilToInt(duration * 30));
        foreach (Transform bone in bones)
        {
            string boneName = bone.name.Substring("mixamorig:".Length);
            string bonePath = AnimationUtility.CalculateTransformPath(bone, root);
            Quaternion rest = bone.localRotation;
            Quaternion axes = Quaternion.Inverse(bone.rotation) * model.rotation;
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            for (int frame = 0; frame <= steps; frame++)
            {
                float t = (float)frame / steps;
                Quaternion q = rest * axes * Quaternion.Euler(Pose(state, boneName, t)) * Quaternion.Inverse(axes);
                curves[0].AddKey(t * duration, q.x); curves[1].AddKey(t * duration, q.y);
                curves[2].AddKey(t * duration, q.z); curves[3].AddKey(t * duration, q.w);
            }
            string[] components = { "x", "y", "z", "w" };
            for (int j = 0; j < 4; j++) clip.SetCurve(bonePath, typeof(Transform), "m_LocalRotation." + components[j], curves[j]);
            // Every clip also restores all bone positions, preventing pool/state pose leakage.
            Vector3 pos = bone.localPosition;
            for (int j = 0; j < 3; j++)
            {
                var positionCurve = AnimationCurve.Constant(0f, duration, pos[j]);
                if (boneName == "Hips" && state == "Dying")
                {
                    Vector3 drop = bone.parent.InverseTransformVector(-root.up * root.GetComponent<BoxCollider>().size.y * root.lossyScale.y * 0.35f);
                    positionCurve = AnimationCurve.EaseInOut(0f, pos[j], duration, pos[j] + drop[j]);
                }
                clip.SetCurve(bonePath, typeof(Transform), "m_LocalPosition." + components[j], positionCurve);
            }
        }
        var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
        clipSettings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // Provisional motion styling. Angles are in model axes, converted to each bone's rest frame above.
    private static Vector3 Pose(string state, string bone, float t)
    {
        float wave = Mathf.Sin(t * Mathf.PI * 2f);
        Vector3 pose = Vector3.zero;
        // This source model has an A pose (left is -X), not a T pose.
        if (bone == "LeftArm") pose.z = 8f;
        if (bone == "RightArm") pose.z = -8f;
        if (bone == "LeftForeArm" || bone == "RightForeArm") pose.x = -20f;
        if (state == "Idle")
        {
            if (bone == "Spine1") pose.x = wave * 1.5f;
            if (bone == "Head") pose.y = wave * 2f;
        }
        else if (state == "Chase")
        {
            if (bone == "LeftUpLeg") pose.x = -wave * 32f;
            if (bone == "RightUpLeg") pose.x = wave * 32f;
            if (bone == "LeftLeg") pose.x = Mathf.Max(0f, wave) * 48f;
            if (bone == "RightLeg") pose.x = Mathf.Max(0f, -wave) * 48f;
            if (bone == "LeftFoot") pose.x = -Mathf.Max(0f, wave) * 16f;
            if (bone == "RightFoot") pose.x = -Mathf.Max(0f, -wave) * 16f;
            if (bone == "LeftArm") pose.x = wave * 25f;
            if (bone == "RightArm") pose.x = -wave * 25f;
            if (bone == "Spine") pose.x = 9f;
            if (bone == "Spine1") pose.y = wave * 6f;
        }
        else if (state == "Attack")
        {
            // Windup, contact exactly at the existing 50% hit check, recovery.
            float punch = t < 0.25f ? -0.25f * t / 0.25f : t < 0.5f ? Mathf.Lerp(-0.25f, 1f, (t - 0.25f) / 0.25f) : 1f - (t - 0.5f) / 0.5f;
            if (bone == "RightArm") { pose.x = -85f * punch; pose.z = -8f - 12f * Mathf.Max(0f, punch); }
            if (bone == "RightForeArm") pose.x = -55f * (1f - Mathf.Max(0f, punch));
            if (bone == "Spine") pose.x = 12f * punch;
            if (bone == "Spine1") pose.y = -22f * punch;
            if (bone == "LeftArm") pose.x = -25f;
            if (bone == "LeftForeArm") pose.x = -45f;
        }
        else if (state == "Knockback")
        {
            float recoil = Mathf.Sin(Mathf.Min(1f, t * 4f) * Mathf.PI * 0.5f);
            if (bone == "Spine") pose.x = -24f * recoil;
            if (bone == "Head") pose.x = -15f * recoil;
            if (bone == "LeftArm") pose.z = 8f - 40f * recoil;
            if (bone == "RightArm") pose.z = -8f + 40f * recoil;
            if (bone == "LeftUpLeg" || bone == "RightUpLeg") pose.x = -20f * recoil;
            if (bone == "LeftLeg" || bone == "RightLeg") pose.x = 35f * recoil;
        }
        else if (state == "Dying")
        {
            float collapse = Mathf.SmoothStep(0f, 1f, t);
            if (bone == "Hips") pose.z = 85f * collapse;
            if (bone == "Spine") pose.x = -24f + 35f * collapse;
            if (bone == "Head") pose.x = -15f + 35f * collapse;
            if (bone == "LeftUpLeg" || bone == "RightUpLeg") pose.x = -20f - 20f * collapse;
            if (bone == "LeftLeg" || bone == "RightLeg") pose.x = 35f + 25f * collapse;
        }
        return pose;
    }

    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        string path = Folder + "/HumanoidEnemy.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var machine = controller.layers[0].stateMachine;
        foreach (string name in States)
        {
            AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = clips[name]; state.writeDefaultValues = true;
            if (name == "Idle") machine.defaultState = state;
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }

    [MenuItem("Tools/Game Demo Enemy/Validate Humanoid Enemy")]
    public static void ValidateAll()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null || skin.bones.Length < 20 || skin.sharedMesh.bindposes.Length == 0)
            throw new InvalidOperationException("Enemy has no valid skinned skeleton.");
        if (prefab.GetComponentsInChildren<Animator>(true).Length != 1 || prefab.GetComponent<Animator>().runtimeAnimatorController == null)
            throw new InvalidOperationException("Enemy must have one root Animator with a controller.");
        foreach (string name in States)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + name + ".anim");
            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length < 100) throw new InvalidOperationException(name + " has no skeletal motion.");
            foreach (var binding in bindings)
                if (prefab.transform.Find(binding.path) == null) throw new InvalidOperationException("Unresolved animation bone: " + binding.path);
        }
        Debug.Log("[Humanoid Enemy] Skin, single Animator and all skeletal curve paths validated.");
    }

    // Contact sheet for reviewing the generated motions without changing the open Scene.
    public static void BuildAndPreview() { BuildAll(); ExportPreview(); }

    [MenuItem("Tools/Game Demo Enemy/Export Humanoid Pose Preview")]
    public static void ExportPreview()
    {
        var sheet = new Texture2D(1600, 800, TextureFormat.RGB24, false);
        float[] samples = { 0.25f, 0.25f, 0.5f, 0.5f, 1f };
        try
        {
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < States.Length; i++)
            {
                var preview = new PreviewRenderUtility();
                var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                try
                {
                    instance.GetComponent<Animator>().enabled = false;
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + States[i] + ".anim");
                    clip.SampleAnimation(instance, clip.length * samples[i]);
                    preview.AddSingleGO(instance);
                    preview.camera.orthographic = true;
                    preview.camera.orthographicSize = 1.45f;
                    preview.camera.nearClipPlane = 0.01f;
                    preview.camera.farClipPlane = 50f;
                    preview.camera.transform.position = row == 0 ? new Vector3(3f, 1.7f, 7f) : new Vector3(7f, 1.7f, 0f);
                    preview.camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
                    preview.camera.clearFlags = CameraClearFlags.Color;
                    preview.camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
                    preview.lights[0].intensity = 1.3f;
                    preview.lights[0].transform.rotation = Quaternion.Euler(35f, -30f, 0f);
                    preview.lights[1].intensity = 0.8f;
                    preview.ambientColor = Color.gray;
                    preview.BeginStaticPreview(new Rect(0f, 0f, 320f, 400f));
                    preview.Render(true);
                    Texture2D image = preview.EndStaticPreview();
                    sheet.SetPixels(i * 320, (1 - row) * 400, 320, 400, image.GetPixels());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                finally { preview.Cleanup(); }
            }
            sheet.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/humanoid-enemy-poses.png", sheet.EncodeToPNG());
            Debug.Log("[Humanoid Enemy] Preview exported: Idle, Chase, Attack, Knockback, Dying; front and side rows.");
        }
        finally { UnityEngine.Object.DestroyImmediate(sheet); }
    }
}
