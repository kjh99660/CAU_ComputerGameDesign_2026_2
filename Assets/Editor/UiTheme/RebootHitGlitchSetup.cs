using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// 피격 글리치 렌더러 기능을 PC 렌더러에 등록하고(중복 등록 없음), 글리치 프레임을 캡처한다.
// 실행: -executeMethod RebootHitGlitchSetup.InstallBatch
//       -executeMethod RebootHitGlitchSetup.CaptureBatch -rebootOut <dir>
public static class RebootHitGlitchSetup
{
    private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
    private const string ShaderName = "Hidden/Reboot/HitGlitch";

    [MenuItem("Tools/REBOOT UI/Install Hit Glitch Renderer Feature")]
    public static void Install()
    {
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath)
                   ?? throw new InvalidOperationException("Renderer data not found: " + RendererPath);
        foreach (ScriptableRendererFeature existing in data.rendererFeatures)
            if (existing is RebootHitGlitchFeature)
            {
                Debug.Log("[RebootHitGlitch] Already installed.");
                return;
            }

        var feature = ScriptableObject.CreateInstance<RebootHitGlitchFeature>();
        feature.name = "RebootHitGlitch";
        var featureSo = new SerializedObject(feature);
        featureSo.FindProperty("shader").objectReferenceValue = Shader.Find(ShaderName)
            ?? throw new InvalidOperationException("Shader not found: " + ShaderName);
        featureSo.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.AddObjectToAsset(feature, data);

        // URP 렌더러 에디터와 같은 방식으로 기능 목록과 로컬 ID 맵을 함께 갱신한다.
        var so = new SerializedObject(data);
        SerializedProperty features = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        Debug.Log("[RebootHitGlitch] Installed into " + RendererPath);
    }

    public static void InstallBatch()
    {
        try { Install(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // 미리보기 씬의 플레이어 시점에서 글리치 0 / 0.5 / 1.0 프레임을 렌더링한다(Scene 저장 없음, UI 제외).
    public static void CaptureBatch()
    {
        try
        {
            string dir = GetArg("-rebootOut") ?? Path.GetFullPath("Docs/UiTheme/Previews");
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(RebootHubEnvironmentBuilder.PreviewScenePath, OpenSceneMode.Single);
            Camera camera = Camera.main ?? throw new InvalidOperationException("Main Camera not found.");
            Transform spawn = null;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "PlayerSpawnPoint") spawn = t;
            Vector3 target = (spawn != null ? spawn.position : Vector3.zero) + Vector3.up * 1.5f;
            Quaternion rotation = Quaternion.Euler(20f, spawn != null ? spawn.eulerAngles.y : 0f, 0f);
            camera.transform.position = target - rotation * Vector3.forward * 4f;
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            foreach ((float amount, string name) in new[] { (0f, "hit_glitch_0"), (0.5f, "hit_glitch_50"), (1f, "hit_glitch_100") })
            {
                ScreenGlitchState.Set(amount, 417.3f);
                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = null;
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            ScreenGlitchState.Clear();
            rt.Release();
            Object.DestroyImmediate(rt);
            EditorSceneManager.OpenScene(RebootHubEnvironmentBuilder.PreviewScenePath, OpenSceneMode.Single);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
