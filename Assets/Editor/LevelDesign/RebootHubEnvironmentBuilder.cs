using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// REBOOT 통신 허브 광장의 환경 에셋을 생성하고 Main Scene에 배치한다.
// - 새 에셋은 Assets/LevelDesign/RebootHub 아래에만 만든다. 다시 실행해도 같은 경로를 덮어써 GUID를 유지한다.
// - 기존 Prefab, Material, 게임 시스템 코드는 수정하지 않는다. Scene에서는 Renderer 머티리얼 교체,
//   조명/안개 설정, Env_RebootHub 루트 추가만 수행한다. 맵·플레이어 위치는 옮기지 않는다
//   (JH_Work 수정 이후 적은 "라운드 시작 플레이어 높이 + SpawnHeight"에 생성되므로 높이 정렬이 필요 없다).
// - 장식은 측정한 광장 바닥 높이를 기준으로 배치한다. 맵 루트는 MainMap(JH_Work 이후) 또는 Map(이전 맵)이다.
// - 기본 대상은 미리보기 씬(Main_RebootHub.unity)이다. 팀 공용 Main.unity는 채택이 결정됐을 때만
//   "Adopt" 메뉴나 -rebootScene main 인자로 적용한다. 미리보기 씬은 Main.unity를 파일 그대로 복사해 만들므로
//   내부 fileID가 Main과 같고, 두 씬의 diff에는 이 빌더가 만든 변경만 남는다.
public static class RebootHubEnvironmentBuilder
{
    public const string MainScenePath = "Assets/Scenes/Main.unity";
    public const string PreviewScenePath = "Assets/Scenes/Main_RebootHub.unity";
    private const string Root = "Assets/LevelDesign/RebootHub";
    private const string EnvRootName = "Env_RebootHub";
    private const string KitMaterialPath = "Assets/SciFi_Modular_Kit_Demo/Materials/Mat_BasicMaterial_00.mat";
    private const string KitPalettePath = "Assets/SciFi_Modular_Kit_Demo/Materials/Palette_00.png";
    private const string SourceVolumePath = "Assets/Settings/SampleSceneProfile.asset";

    // 맵 측정값: 광장 중심 (-24, 48.2), 반경 약 20m, 바닥 상단 y=-2(실행 시 다시 측정한다).
    private static readonly Vector2 PlazaCenterXZ = new Vector2(-24f, 48.2f);
    private static readonly string[] MapRootNames = { "MainMap", "Map" };
    private const float HubCenterZ = 80f;

    private static Material _darkMetal, _steel, _steelTwoSided, _glowTeal, _glowMagenta, _alertRed, _hubScreen, _hazard, _floorGuide, _holoPanel, _kitNight;
    private static TMP_FontAsset _font;

    [MenuItem("Tools/REBOOT Level/Preview: Rebuild Main_RebootHub From Main")]
    public static void RebuildPreviewFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        BuildAssets();
        RefreshPreviewFromMain();
        ApplyToScene(PreviewScenePath);
    }

    [MenuItem("Tools/REBOOT Level/Preview: Re-apply Environment Only")]
    public static void ReapplyPreviewFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        BuildAssets();
        ApplyToScene(PreviewScenePath);
    }

    [MenuItem("Tools/REBOOT Level/Adopt: Apply To Main.unity")]
    public static void AdoptFromMenu()
    {
        if (!EditorUtility.DisplayDialog("REBOOT Level",
                "팀 공용 Main.unity에 레벨 디자인(머티리얼 교체, 조명, Env_RebootHub)을 적용합니다. 팀 합의 후에만 실행하세요.",
                "적용", "취소"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        BuildAssets();
        ApplyToScene(MainScenePath);
    }

    // 배치 실행 인자: -rebootScene preview|main (기본 preview), -rebootRefreshPreview (Main.unity에서 미리보기를 다시 복사)
    public static void BuildBatch()
    {
        try
        {
            string target = GetArg("-rebootScene") == "main" ? MainScenePath : PreviewScenePath;
            BuildAssets();
            if (target == PreviewScenePath && (HasArg("-rebootRefreshPreview") || !File.Exists(PreviewScenePath)))
                RefreshPreviewFromMain();
            ApplyToScene(target);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    // ---------------------------------------------------------------- assets

    public static void BuildAssets()
    {
        foreach (string sub in new[] { "", "/Textures", "/Materials", "/Meshes", "/Prefabs", "/Settings" })
            EnsureFolder(Root + sub);

        Texture2D palette = BuildNightPalette();
        Texture2D hubScreen = WriteTexture("T_Reboot_HubScreen", BuildHubScreenPixels(256, 128), 256, 128, FilterMode.Bilinear, TextureWrapMode.Repeat);
        Texture2D hazard = WriteTexture("T_Reboot_Hazard", BuildHazardPixels(128, 32), 128, 32, FilterMode.Bilinear, TextureWrapMode.Repeat);
        Texture2D holo = WriteTexture("T_Reboot_HoloPanel", BuildHoloPanelPixels(256, 128), 256, 128, FilterMode.Bilinear, TextureWrapMode.Clamp);

        // 기존 SciFi 키트 셰이더를 그대로 쓰고 팔레트만 야간용으로 바꾼 변형 머티리얼.
        var kitSource = AssetDatabase.LoadAssetAtPath<Material>(KitMaterialPath);
        if (kitSource == null)
            throw new InvalidOperationException("SciFi kit material not found: " + KitMaterialPath);
        _kitNight = SaveMaterial("M_Reboot_KitNight", new Material(kitSource));
        _kitNight.SetTexture("_Texture", palette);
        EditorUtility.SetDirty(_kitNight);

        _darkMetal = SaveMaterial("M_Reboot_DarkMetal", Lit(new Color(0.09f, 0.10f, 0.13f), 0.65f, 0.45f));
        _steel = SaveMaterial("M_Reboot_Steel", Lit(new Color(0.20f, 0.23f, 0.28f), 0.55f, 0.5f));
        var twoSided = Lit(new Color(0.20f, 0.23f, 0.28f), 0.55f, 0.5f);
        twoSided.SetFloat("_Cull", 0f);
        _steelTwoSided = SaveMaterial("M_Reboot_SteelTwoSided", twoSided);
        _glowTeal = SaveMaterial("M_Reboot_GlowTeal", Glow(new Color(0.05f, 0.95f, 0.9f), 4.5f));
        _glowMagenta = SaveMaterial("M_Reboot_GlowMagenta", Glow(new Color(1f, 0.12f, 0.75f), 4f));
        _alertRed = SaveMaterial("M_Reboot_AlertRed", Glow(new Color(1f, 0.02f, 0.02f), 2.2f));
        _floorGuide = SaveMaterial("M_Reboot_FloorGuide", Glow(new Color(0.05f, 0.85f, 0.85f), 0.85f));

        var screen = Lit(new Color(0.02f, 0.03f, 0.05f), 0.2f, 0.25f);
        screen.SetTexture("_EmissionMap", hubScreen);
        screen.EnableKeyword("_EMISSION");
        screen.SetColor("_EmissionColor", new Color(0.1f, 1f, 0.95f) * 2.6f);
        _hubScreen = SaveMaterial("M_Reboot_HubScreen", screen);

        var hazardMat = Lit(Color.white, 0.3f, 0.35f);
        hazardMat.SetTexture("_BaseMap", hazard);
        hazardMat.SetTexture("_EmissionMap", hazard);
        hazardMat.EnableKeyword("_EMISSION");
        hazardMat.SetColor("_EmissionColor", new Color(1f, 0.15f, 0.1f) * 1.4f);
        _hazard = SaveMaterial("M_Reboot_Hazard", hazardMat);

        var holoMat = Lit(new Color(0.02f, 0.01f, 0.03f), 0f, 0.15f);
        holoMat.SetTexture("_EmissionMap", holo);
        holoMat.EnableKeyword("_EMISSION");
        holoMat.SetColor("_EmissionColor", new Color(1f, 0.2f, 0.8f) * 2.2f);
        _holoPanel = SaveMaterial("M_Reboot_HoloPanel", holoMat);

        SaveMaterial("M_Reboot_NightSky", NightSky());
        BuildVolumeProfile();

        _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset")
                ?? TMP_Settings.defaultFontAsset;

        BuildCommHubPrefab();
        BuildSecuritySignPrefab();
        BuildBillboardPrefab();
        BuildColumnBeaconPrefab();
        BuildFloorGuidePrefabs();
        // URP 머티리얼 후처리가 새 머티리얼의 키워드를 초기화할 수 있어 Emission 키워드를 마지막에 다시 확정한다.
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root + "/Materials" }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat == null || mat.shader == null || mat.shader.name != "Universal Render Pipeline/Lit" || !mat.HasProperty("_EmissionColor")) continue;
            Color e = mat.GetColor("_EmissionColor");
            if (e.maxColorComponent <= 0.001f) { mat.DisableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
            else { mat.EnableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            EditorUtility.SetDirty(mat);
        }
        // AssetDatabase.SaveAssets()는 TMP 기본 폰트 같은 공용 에셋까지 다시 저장하므로 이 폴더의 에셋만 저장한다.
        foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { Root }))
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
        }
        AssetDatabase.Refresh();
    }

    private static Texture2D BuildNightPalette()
    {
        var source = new Texture2D(2, 2);
        source.LoadImage(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), KitPalettePath)));
        var pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = NightColor(pixels[i]);
        Object.DestroyImmediate(source);
        return WriteTexture("T_Reboot_KitPalette_Night", pixels, 128, 32, FilterMode.Point, TextureWrapMode.Clamp);
    }

    // 무채색은 어두운 청회색 금속으로, 파랑 계열은 청록으로, 주황 계열은 마젠타로 옮긴다. 명도 순서는 유지한다.
    private static Color NightColor(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < 0.18f)
            return Color.HSVToRGB(0.62f, 0.14f, 0.15f + v * 0.43f);
        if (h > 0.45f && h < 0.72f)
            return Color.HSVToRGB(h < 0.53f ? h : 0.5f, 0.85f, Mathf.Max(v, 0.75f));
        if (h < 0.11f || h > 0.92f)
            return Color.HSVToRGB(h < 0.04f || h > 0.92f ? 0.99f : 0.9f, 0.85f, Mathf.Max(v, 0.7f));
        return Color.HSVToRGB(h, s * 0.9f, v * 0.85f);
    }

    private static Color[] BuildHubScreenPixels(int w, int h)
    {
        // 비상 복구 진행을 암시하는 데이터 막대와 스캔 라인. 알파 없이 Emission 마스크로 쓴다.
        var px = new Color[w * h];
        var rnd = new System.Random(707);
        int[] bars = new int[32];
        for (int i = 0; i < bars.Length; i++) bars[i] = rnd.Next(h / 6, h - 18);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 0.04f;
            int col = x / (w / bars.Length);
            int inCol = x % (w / bars.Length);
            if (inCol > 1 && inCol < (w / bars.Length) - 2 && y > 10 && y < bars[col]) v = 0.35f + 0.65f * (y % 6 < 4 ? 1f : 0.3f);
            if (y < 6) v = x < w * 0.72f ? 1f : 0.15f; // 진행 바
            if (y == h - 4 || y == h - 3) v = 0.8f;
            if (y % 16 == 0) v = Mathf.Max(v, 0.18f);
            px[y * w + x] = new Color(v, v, v, 1f);
        }
        return px;
    }

    private static Color[] BuildHazardPixels(int w, int h)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            bool stripe = ((x + y) / 16) % 2 == 0;
            px[y * w + x] = stripe ? new Color(0.85f, 0.08f, 0.06f) : new Color(0.04f, 0.04f, 0.05f);
        }
        return px;
    }

    private static Color[] BuildHoloPanelPixels(int w, int h)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 0.05f + 0.06f * ((y / 3) % 2);
            bool border = x < 4 || x >= w - 4 || y < 4 || y >= h - 4;
            bool corner = (x < 24 || x >= w - 24) && (y < 24 || y >= h - 24) && (x < 8 || x >= w - 8 || y < 8 || y >= h - 8);
            if (border) v = 0.55f;
            if (corner) v = 1f;
            px[y * w + x] = new Color(v, v, v, 1f);
        }
        return px;
    }

    private static Texture2D WriteTexture(string name, Color[] pixels, int w, int h, FilterMode filter, TextureWrapMode wrap)
    {
        string path = $"{Root}/Textures/{name}.png";
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = filter != FilterMode.Point;
        importer.filterMode = filter;
        importer.wrapMode = wrap;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material Lit(Color baseColor, float metallic, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        m.enableInstancing = true;
        return m;
    }

    private static Material Glow(Color color, float intensity)
    {
        var m = Lit(new Color(color.r * 0.2f, color.g * 0.2f, color.b * 0.2f, 1f), 0f, 0.6f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * intensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }

    private static Material NightSky()
    {
        var m = new Material(Shader.Find("Skybox/Procedural"));
        m.SetFloat("_SunDisk", 0f);
        m.SetFloat("_SunSize", 0.02f);
        m.SetFloat("_AtmosphereThickness", 0.42f);
        m.SetColor("_SkyTint", new Color(0.32f, 0.2f, 0.5f));
        m.SetColor("_GroundColor", new Color(0.05f, 0.04f, 0.08f));
        m.SetFloat("_Exposure", 0.5f);
        return m;
    }

    private static Material SaveMaterial(string name, Material material)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        existing.shader = material.shader;
        existing.CopyPropertiesFromMaterial(material);
        existing.shaderKeywords = material.shaderKeywords;
        existing.globalIlluminationFlags = material.globalIlluminationFlags;
        existing.enableInstancing = material.enableInstancing;
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(material);
        return existing;
    }

    private static void BuildVolumeProfile()
    {
        string path = $"{Root}/Settings/VP_Reboot_Night.asset";
        var source = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SourceVolumePath);
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        // 기존 프로필을 복사한 뒤 야간 장면에 맞게 Bloom과 색 보정만 조정한다.
        for (int i = profile.components.Count - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(profile.components[i], true);
        }
        profile.components.Clear();
        if (source != null)
            foreach (VolumeComponent component in source.components)
            {
                var copy = Object.Instantiate(component);
                copy.name = component.name;
                AssetDatabase.AddObjectToAsset(copy, profile);
                profile.components.Add(copy);
            }
        if (profile.TryGet(out Bloom bloom))
        {
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.65f);
            bloom.scatter.Override(0.6f);
        }
        if (!profile.TryGet(out ColorAdjustments color))
        {
            color = profile.Add<ColorAdjustments>(true);
            AssetDatabase.AddObjectToAsset(color, profile);
        }
        color.postExposure.Override(0.15f);
        color.contrast.Override(12f);
        color.saturation.Override(8f);
        if (profile.TryGet(out Vignette vignette))
            vignette.intensity.Override(0.28f);
        EditorUtility.SetDirty(profile);
    }

    // ---------------------------------------------------------------- meshes

    private static Mesh SaveMesh(string name, Mesh mesh)
    {
        string path = $"{Root}/Meshes/{name}.asset";
        mesh.name = name;
        mesh.RecalculateBounds();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        existing.Clear();
        existing.SetVertices(mesh.vertices);
        existing.SetNormals(mesh.normals);
        existing.SetUVs(0, mesh.uv);
        existing.SetTriangles(mesh.triangles, 0);
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }

    // 평면 셰이딩 각기둥/원뿔대. LowPoly 배경과 같은 면 분할 느낌을 낸다.
    private static Mesh Prism(int sides, float bottomRadius, float topRadius, float height, bool caps = true, float rotationDeg = 0f)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float rot = rotationDeg * Mathf.Deg2Rad;
        for (int i = 0; i < sides; i++)
        {
            float a0 = rot + i * Mathf.PI * 2f / sides, a1 = rot + (i + 1) * Mathf.PI * 2f / sides;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * bottomRadius, 0f, Mathf.Sin(a0) * bottomRadius);
            Vector3 b1 = new Vector3(Mathf.Cos(a1) * bottomRadius, 0f, Mathf.Sin(a1) * bottomRadius);
            Vector3 t0 = new Vector3(Mathf.Cos(a0) * topRadius, height, Mathf.Sin(a0) * topRadius);
            Vector3 t1 = new Vector3(Mathf.Cos(a1) * topRadius, height, Mathf.Sin(a1) * topRadius);
            Vector3 normal = Vector3.Cross(t0 - b0, b1 - b0).normalized;
            int s = v.Count;
            v.AddRange(new[] { b0, b1, t1, t0 });
            n.AddRange(new[] { normal, normal, normal, normal });
            uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            t.AddRange(new[] { s, s + 3, s + 2, s, s + 2, s + 1 });
            if (caps)
            {
                int c = v.Count;
                v.AddRange(new[] { new Vector3(0, height, 0), t0, t1 });
                n.AddRange(new[] { Vector3.up, Vector3.up, Vector3.up });
                uv.AddRange(new[] { new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(1, 1) });
                t.AddRange(new[] { c, c + 2, c + 1 });
                c = v.Count;
                v.AddRange(new[] { Vector3.zero, b0, b1 });
                n.AddRange(new[] { Vector3.down, Vector3.down, Vector3.down });
                uv.AddRange(new[] { new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1, 0) });
                t.AddRange(new[] { c, c + 1, c + 2 });
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        return mesh;
    }

    private static Mesh Torus(float major, float minor, int majorSegments, int minorSegments)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3 P(int i, int j)
        {
            float a = i * Mathf.PI * 2f / majorSegments, b = j * Mathf.PI * 2f / minorSegments;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            return dir * (major + Mathf.Cos(b) * minor) + Vector3.up * Mathf.Sin(b) * minor;
        }
        for (int i = 0; i < majorSegments; i++)
        for (int j = 0; j < minorSegments; j++)
        {
            Vector3 p0 = P(i, j), p1 = P(i + 1, j), p2 = P(i + 1, j + 1), p3 = P(i, j + 1);
            Vector3 normal = Vector3.Cross(p3 - p0, p1 - p0).normalized;
            int s = v.Count;
            v.AddRange(new[] { p0, p1, p2, p3 });
            n.AddRange(new[] { normal, normal, normal, normal });
            uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            t.AddRange(new[] { s, s + 3, s + 2, s, s + 2, s + 1 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        return mesh;
    }

    private static Mesh Annulus(float inner, float outer, int segments, float arcDegrees = 360f)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float arc = arcDegrees * Mathf.Deg2Rad;
        for (int i = 0; i <= segments; i++)
        {
            float a = i * arc / segments;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(d * inner); v.Add(d * outer);
            n.Add(Vector3.up); n.Add(Vector3.up);
            uv.Add(new Vector2(i / (float)segments, 0)); uv.Add(new Vector2(i / (float)segments, 1));
            if (i < segments)
            {
                int s = i * 2;
                t.AddRange(new[] { s, s + 3, s + 1, s, s + 2, s + 3 });
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        return mesh;
    }

    // +Z 방향을 가리키는 평면 화살표(쉐브론).
    private static Mesh Chevron(float width, float depth, float thickness)
    {
        float hw = width * 0.5f;
        Vector3[] outline =
        {
            new Vector3(-hw, 0, 0), new Vector3(0, 0, depth), new Vector3(hw, 0, 0),
            new Vector3(hw - thickness, 0, 0), new Vector3(0, 0, depth - thickness * 1.4f), new Vector3(-hw + thickness, 0, 0)
        };
        var mesh = new Mesh();
        mesh.SetVertices(outline);
        mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        mesh.SetUVs(0, new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.right, Vector2.up, Vector2.zero });
        mesh.SetTriangles(new[] { 0, 1, 4, 0, 4, 5, 4, 1, 2, 4, 2, 3 }, 0);
        return mesh;
    }

    // ---------------------------------------------------------------- prefabs

    private static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3? euler = null, Vector3? scale = null, bool shadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localEulerAngles = euler ?? Vector3.zero;
        go.transform.localScale = scale ?? Vector3.one;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    private static Mesh Builtin(string name) => Resources.GetBuiltinResource<Mesh>(name);

    private static TextMeshPro Label(Transform parent, string name, string text, Vector3 position, float size, Color color, float glow)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.color = color * glow;
        tmp.fontStyle = FontStyles.Bold;
        tmp.rectTransform.sizeDelta = new Vector2(30f, 4f);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    private static void SavePrefab(GameObject root, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/Prefabs/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    // 통신 허브: 광장 북쪽 통로 위에 걸친 탑. 피벗 y=0은 광장 바닥 높이다.
    // 다리는 광장 밖 공중(아래 도시)으로 내려가며, 본체 하단은 통로 위 6m에서 시작해 이동 공간을 막지 않는다. Collider 없음.
    private static void BuildCommHubPrefab()
    {
        Mesh leg = SaveMesh("SM_Reboot_HubLeg", Prism(4, 1.1f, 0.75f, 28f, true, 45f));
        Mesh legStrip = SaveMesh("SM_Reboot_HubLegStrip", Prism(4, 0.12f, 0.12f, 14f, true, 45f));
        Mesh deck = SaveMesh("SM_Reboot_HubDeck", Prism(8, 9.2f, 8.6f, 1.3f, true, 22.5f));
        Mesh deckRing = SaveMesh("SM_Reboot_HubDeckRing", Torus(9.25f, 0.14f, 8, 4));
        Mesh coreBase = SaveMesh("SM_Reboot_HubCoreBase", Prism(8, 5.2f, 4.6f, 8f, true, 22.5f));
        Mesh screenBand = SaveMesh("SM_Reboot_HubScreenBand", Prism(8, 5.12f, 4.95f, 3.2f, false, 22.5f));
        Mesh coreMid = SaveMesh("SM_Reboot_HubCoreMid", Prism(8, 4.1f, 3.3f, 12f, true, 22.5f));
        Mesh band = SaveMesh("SM_Reboot_HubBand", Prism(8, 4.0f, 3.9f, 0.32f, false, 22.5f));
        Mesh ringA = SaveMesh("SM_Reboot_HubDataRingA", Torus(7.6f, 0.32f, 40, 6));
        Mesh ringB = SaveMesh("SM_Reboot_HubDataRingB", Torus(6.1f, 0.24f, 36, 6));
        Mesh head = SaveMesh("SM_Reboot_HubHead", Prism(8, 3.3f, 2.1f, 4f, true, 22.5f));
        Mesh mast = SaveMesh("SM_Reboot_HubMast", Prism(6, 0.38f, 0.12f, 13f));
        Mesh dish = SaveMesh("SM_Reboot_HubDish", Prism(14, 0.45f, 2.6f, 1.1f, false));
        Mesh feed = SaveMesh("SM_Reboot_HubDishFeed", Prism(6, 0.12f, 0.08f, 1.9f));

        var root = new GameObject("P_Reboot_CommHub");
        var structure = new GameObject("Structure").transform; structure.SetParent(root.transform, false);
        var glow = new GameObject("Glow").transform; glow.SetParent(root.transform, false);
        var antenna = new GameObject("Antenna").transform; antenna.SetParent(root.transform, false);

        Vector3[] legPositions = { new Vector3(-6.6f, 0, -5.2f), new Vector3(6.6f, 0, -5.2f), new Vector3(-6.6f, 0, 5.2f), new Vector3(6.6f, 0, 5.2f) };
        for (int i = 0; i < legPositions.Length; i++)
        {
            Part(structure, $"Leg_{i}", leg, _darkMetal, legPositions[i] + Vector3.down * 22f);
            Vector3 outward = new Vector3(Mathf.Sign(legPositions[i].x), 0f, Mathf.Sign(legPositions[i].z)).normalized * 0.82f;
            Part(glow, $"LegStrip_{i}", legStrip, _glowTeal, legPositions[i] + outward + Vector3.down * 9f, null, null, false);
        }
        Part(structure, "Deck", deck, _steel, new Vector3(0, 6f, 0));
        Part(glow, "DeckEdgeLight", deckRing, _glowMagenta, new Vector3(0, 6.05f, 0), new Vector3(0, 22.5f, 0), null, false);
        Part(structure, "CoreBase", coreBase, _darkMetal, new Vector3(0, 7.3f, 0));
        Part(glow, "StatusScreenBand", screenBand, _hubScreen, new Vector3(0, 9.4f, 0), null, null, false);
        Part(structure, "CoreMid", coreMid, _steel, new Vector3(0, 15.3f, 0));
        for (int i = 0; i < 3; i++)
            Part(glow, $"CoreBand_{i}", band, _glowTeal, new Vector3(0, 17.4f + i * 3.4f, 0), null, new Vector3(1f - i * 0.07f, 1f, 1f - i * 0.07f), false);
        Part(glow, "DataRing_Teal", ringA, _glowTeal, new Vector3(0, 19.5f, 0), new Vector3(7f, 0, 4f), null, false);
        Part(glow, "DataRing_Magenta", ringB, _glowMagenta, new Vector3(0, 25f, 0), new Vector3(-10f, 30f, 0), null, false);
        Part(structure, "Head", head, _darkMetal, new Vector3(0, 27.3f, 0));
        Part(antenna, "Mast", mast, _darkMetal, new Vector3(0, 31.3f, 0));
        Part(antenna, "Beacon", Builtin("Sphere.fbx"), _alertRed, new Vector3(0, 44.5f, 0), null, Vector3.one * 0.9f, false);
        Part(antenna, "BeaconMid", Builtin("Sphere.fbx"), _alertRed, new Vector3(0, 38f, 0), null, Vector3.one * 0.5f, false);
        for (int i = 0; i < 3; i++)
        {
            float yaw = 200f + i * 120f;
            var pivot = new GameObject($"Dish_{i}").transform;
            pivot.SetParent(antenna, false);
            pivot.localPosition = new Vector3(0, 29.2f, 0);
            pivot.localEulerAngles = new Vector3(0, yaw, 0);
            // 접시는 허브 바깥쪽 위를 향한다.
            Part(pivot, "DishBowl", dish, _steelTwoSided, new Vector3(0, 0.4f, 3.2f), new Vector3(55f, 0, 0));
            Part(pivot, "DishFeed", feed, _glowTeal, new Vector3(0, 0.6f, 3.4f), new Vector3(55f, 0, 0), null, false);
        }

        // 광장을 향한 간판(-Z가 광장 방향). 통로 위 3.2m부터 걸려 이동을 막지 않는다.
        var sign = new GameObject("PlazaSign").transform;
        sign.SetParent(root.transform, false);
        sign.localPosition = new Vector3(0, 0, -9.4f);
        Part(sign, "Frame", Builtin("Cube.fbx"), _darkMetal, new Vector3(0, 4.9f, 0.15f), null, new Vector3(13.5f, 3.6f, 0.25f));
        Part(sign, "Panel", Builtin("Quad.fbx"), _holoPanel, new Vector3(0, 4.9f, -0.01f), new Vector3(0, 0, 0), new Vector3(13f, 3.2f, 1f), false);
        Part(sign, "TopLight", Builtin("Cube.fbx"), _glowTeal, new Vector3(0, 6.75f, 0), null, new Vector3(13.6f, 0.12f, 0.3f), false);
        Part(sign, "HangerL", Builtin("Cube.fbx"), _darkMetal, new Vector3(-5.5f, 6.4f, 0.15f), null, new Vector3(0.18f, 1.2f, 0.18f));
        Part(sign, "HangerR", Builtin("Cube.fbx"), _darkMetal, new Vector3(5.5f, 6.4f, 0.15f), null, new Vector3(0.18f, 1.2f, 0.18f));
        var title = Label(sign, "Title", "COMM HUB 07", new Vector3(0, 5.45f, -0.06f), 14f, new Color(0.35f, 1f, 0.95f), 1.6f);
        title.characterSpacing = 8f;
        var sub = Label(sign, "Subtitle", "EMERGENCY REBOOT  //  HOLD 100s", new Vector3(0, 4.1f, -0.06f), 6.5f, new Color(1f, 0.35f, 0.85f), 1.5f);
        sub.characterSpacing = 4f;

        SavePrefab(root, "P_Reboot_CommHub");
    }

    // 계단/통로 입구의 보안 경고판. 바닥 위 Collider 없이 벽면에 붙여 쓴다.
    private static void BuildSecuritySignPrefab()
    {
        var root = new GameObject("P_Reboot_SecuritySign");
        Part(root.transform, "Board", Builtin("Cube.fbx"), _darkMetal, new Vector3(0, 0.75f, 0.05f), null, new Vector3(3.2f, 1.5f, 0.1f));
        Part(root.transform, "HazardTop", Builtin("Quad.fbx"), _hazard, new Vector3(0, 1.36f, -0.005f), null, new Vector3(3.1f, 0.22f, 1f), false);
        Part(root.transform, "HazardBottom", Builtin("Quad.fbx"), _hazard, new Vector3(0, 0.14f, -0.005f), null, new Vector3(3.1f, 0.22f, 1f), false);
        var text = Label(root.transform, "Text", "ARCA SECURITY\nRESTRICTED ZONE", new Vector3(0, 0.75f, -0.02f), 3.4f, new Color(1f, 0.3f, 0.25f), 1.4f);
        text.lineSpacing = -10f;
        text.rectTransform.sizeDelta = new Vector2(3f, 1f);
        SavePrefab(root, "P_Reboot_SecuritySign");
    }

    // 배경 홀로그램 광고판(아르카 보안 홍보). 광장 밖 공중에 띄운다.
    private static void BuildBillboardPrefab()
    {
        var root = new GameObject("P_Reboot_HoloBillboard");
        Part(root.transform, "Frame", Builtin("Cube.fbx"), _darkMetal, new Vector3(0, 0, 0.2f), null, new Vector3(10.6f, 5.6f, 0.3f));
        Part(root.transform, "Panel", Builtin("Quad.fbx"), _holoPanel, new Vector3(0, 0, 0f), null, new Vector3(10f, 5f, 1f), false);
        Part(root.transform, "Post", Builtin("Cube.fbx"), _darkMetal, new Vector3(0, -8f, 0.3f), null, new Vector3(0.6f, 11f, 0.6f));
        var a = Label(root.transform, "Brand", "ARCA", new Vector3(0, 0.9f, -0.05f), 22f, new Color(1f, 0.3f, 0.85f), 1.8f);
        a.characterSpacing = 20f;
        Label(root.transform, "Slogan", "SAFE CITY  //  WE ARE WATCHING", new Vector3(0, -1.3f, -0.05f), 5.5f, new Color(0.4f, 1f, 0.95f), 1.3f);
        SavePrefab(root, "P_Reboot_HoloBillboard");
    }

    private static void BuildColumnBeaconPrefab()
    {
        var root = new GameObject("P_Reboot_ColumnBeacon");
        Part(root.transform, "Cap", Builtin("Cube.fbx"), _darkMetal, new Vector3(0, 0.05f, 0), null, new Vector3(0.7f, 0.1f, 0.7f), false);
        Part(root.transform, "Light", Builtin("Cube.fbx"), _alertRed, new Vector3(0, 0.13f, 0), null, new Vector3(0.5f, 0.06f, 0.12f), false);
        SavePrefab(root, "P_Reboot_ColumnBeacon");
    }

    private static void BuildFloorGuidePrefabs()
    {
        Mesh ringInner = SaveMesh("SM_Reboot_FloorRingStart", Annulus(2.6f, 2.68f, 48));
        Mesh ringOuter = SaveMesh("SM_Reboot_FloorRingArc", Annulus(9.94f, 10.04f, 24, 60f));
        Mesh chevron = SaveMesh("SM_Reboot_FloorChevron", Chevron(1.6f, 0.9f, 0.22f));

        var start = new GameObject("P_Reboot_FloorStartMark");
        Part(start.transform, "StartRing", ringInner, _floorGuide, Vector3.zero, null, null, false);
        for (int i = 0; i < 4; i++)
            Part(start.transform, $"Arc_{i}", ringOuter, _floorGuide, Vector3.zero, new Vector3(0, 15f + i * 90f, 0), null, false);
        SavePrefab(start, "P_Reboot_FloorStartMark");

        var lane = new GameObject("P_Reboot_FloorChevrons");
        for (int i = 0; i < 3; i++)
            Part(lane.transform, $"Chevron_{i}", chevron, _alertRed, new Vector3(0, 0, i * 1.1f), null, null, false);
        SavePrefab(lane, "P_Reboot_FloorChevrons");
    }

    // ---------------------------------------------------------------- scene

    // Main.unity를 파일 그대로 덮어써 미리보기 씬을 만든다. 미리보기의 .meta(GUID)는 유지된다.
    public static void RefreshPreviewFromMain()
    {
        string root = Directory.GetCurrentDirectory();
        File.Copy(Path.Combine(root, MainScenePath), Path.Combine(root, PreviewScenePath), true);
        AssetDatabase.ImportAsset(PreviewScenePath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[RebootHub] Copied {MainScenePath} -> {PreviewScenePath}");
    }

    public static void ApplyToScene(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        LoadBuiltMaterials();

        GameObject map = FindMapRoot();

        // 1) 광장 바닥 높이 측정. 모든 장식은 이 높이를 기준(floor)으로 배치한다.
        Physics.SyncTransforms();
        float floor = MeasurePlazaFloor(map.transform);

        // 2) 맵 머티리얼: Scene의 Renderer에서만 야간 팔레트 변형으로 교체한다(원본 에셋은 그대로).
        var kitSource = AssetDatabase.LoadAssetAtPath<Material>(KitMaterialPath);
        var defaultLit = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat");
        int swapped = 0;
        foreach (Renderer renderer in map.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == kitSource) { materials[i] = _kitNight; changed = true; }
                else if (materials[i] == defaultLit) { materials[i] = _darkMetal; changed = true; }
            }
            if (changed)
            {
                Undo.RecordObject(renderer, "REBOOT material swap");
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                swapped++;
            }
        }

        // 3) 환경 루트 재생성.
        GameObject old = GameObject.Find(EnvRootName);
        if (old != null) Object.DestroyImmediate(old);
        var env = new GameObject(EnvRootName);
        Vector3 c = new Vector3(PlazaCenterXZ.x, floor, PlazaCenterXZ.y);
        Vector3 At(float x, float up, float z) => new Vector3(x, floor + up, z);

        var landmark = Group(env, "Landmark");
        Place("P_Reboot_CommHub", landmark, At(c.x, 0f, HubCenterZ), 0f);

        // 출입구(적이 들어오는 축): 광장 중심선에서 남·북으로 바닥이 처음 높아지는 곳(계단 첫 단)을 맵에서 잰다.
        float southGateZ = GateZ(c, -1f, 29.3f);
        float northGateZ = GateZ(c, 1f, 66.9f);

        var signage = Group(env, "Signage");
        // 남쪽 계단 입구 양옆 벽면(광장 쪽을 향함). 판 뒷면(두께 0.1m)이 벽면에 닿도록 벽 위치를 잰다.
        foreach (float side in new[] { -1f, 1f })
        {
            float x = c.x + side * 6.6f;
            float wallZ = WallFaceZ(new Vector3(x, floor, southGateZ + 3f), Vector3.back, floor, 28.2f);
            Place("P_Reboot_SecuritySign", signage, At(x, 0.35f, wallZ + 0.11f), 180f);
        }
        // 광고판 위치는 시작 카메라에서 가리지 않고 건물과 겹치지 않는 곳으로 골랐다(진단 보고서의 Signage check로 확인).
        Place("P_Reboot_HoloBillboard", signage, At(c.x - 27.9f, 11f, c.z + 2.4f), -85f);
        Place("P_Reboot_HoloBillboard", signage, At(c.x + 31f, 13f, c.z - 4f), 70f);

        var floorGuides = Group(env, "FloorGuides");
        Place("P_Reboot_FloorStartMark", floorGuides, At(c.x, 0.02f, 42f), 0f);
        // 쉐브론 3개(길이 약 3.1m)가 계단 첫 단 0.3m 앞에서 광장 쪽을 가리킨다.
        Place("P_Reboot_FloorChevrons", floorGuides, At(c.x, 0.02f, southGateZ + 0.3f), 0f);
        Place("P_Reboot_FloorChevrons", floorGuides, At(c.x, 0.02f, northGateZ - 0.3f), 180f);

        var beacons = Group(env, "PerimeterBeacons");
        int beaconCount = 0;
        Transform columns = map.transform.Find("Center_Map/Area_A/Area_A_Floor/Area_A_Column");
        if (columns != null)
            foreach (Transform column in columns)
            {
                var renderers = column.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds b = renderers[0].bounds;
                foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                float d = new Vector2(b.center.x - c.x, b.center.z - c.z).magnitude;
                if (d < 12f) continue; // 중앙 안테나 기둥은 제외
                Place("P_Reboot_ColumnBeacon", beacons, new Vector3(b.center.x, b.max.y, b.center.z), 0f);
                beaconCount++;
            }

        var lighting = Group(env, "Lighting");
        AddLight(lighting, "HubKey_Teal", LightType.Spot, new Color(0.15f, 0.95f, 0.9f), 9f, 46f, At(c.x, 9f, 71f), new Vector3(28f, 180f, 0f), 75f);
        AddLight(lighting, "Fill_NW_Teal", LightType.Point, new Color(0.2f, 0.9f, 1f), 6f, 18f, At(c.x - 14f, 5f, c.z + 11f), Vector3.zero);
        AddLight(lighting, "Fill_NE_Magenta", LightType.Point, new Color(1f, 0.25f, 0.8f), 6f, 18f, At(c.x + 14f, 5f, c.z + 11f), Vector3.zero);
        AddLight(lighting, "Fill_SW_Magenta", LightType.Point, new Color(1f, 0.25f, 0.8f), 5f, 17f, At(c.x - 14f, 5f, c.z - 12f), Vector3.zero);
        AddLight(lighting, "Fill_SE_Teal", LightType.Point, new Color(0.2f, 0.9f, 1f), 5f, 17f, At(c.x + 14f, 5f, c.z - 12f), Vector3.zero);
        AddLight(lighting, "Alert_SouthGate", LightType.Point, new Color(1f, 0.12f, 0.08f), 3f, 9f, At(c.x, 3.2f, southGateZ - 0.3f), Vector3.zero);
        AddLight(lighting, "Alert_NorthGate", LightType.Point, new Color(1f, 0.12f, 0.08f), 2.2f, 7f, At(c.x, 2.8f, northGateZ + 1.1f), Vector3.zero);

        // 미래 스폰 보정 후보(보고서 참고)를 위한 기준점. 현재 런타임 코드는 사용하지 않는다.
        var gameplay = Group(env, "DesignReference_NotUsedByRuntime");
        Marker(gameplay, "PlazaCenter_r20", c);
        Marker(gameplay, "EntryGate_North", At(c.x, 0f, northGateZ - 0.4f));
        Marker(gameplay, "EntryGate_South", At(c.x, 0f, southGateZ + 0.7f));

        foreach (Transform t in env.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Light>() == null)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

        // 4) 조명, 하늘, 안개, 후처리.
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/M_Reboot_NightSky.mat");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.20f, 0.22f, 0.36f);
        RenderSettings.ambientEquatorColor = new Color(0.14f, 0.12f, 0.22f);
        RenderSettings.ambientGroundColor = new Color(0.06f, 0.06f, 0.08f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.10f, 0.08f, 0.17f);
        RenderSettings.fogDensity = 0.011f;
        Light sun = GameObject.Find("Directional Light")?.GetComponent<Light>();
        if (sun != null)
        {
            Undo.RecordObject(sun, "REBOOT moon light");
            sun.color = new Color(0.62f, 0.68f, 1f);
            sun.intensity = 0.85f;
            RenderSettings.sun = sun;
        }
        var volume = Object.FindFirstObjectByType<Volume>();
        if (volume != null)
        {
            Undo.RecordObject(volume, "REBOOT volume");
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{Root}/Settings/VP_Reboot_Night.asset");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[RebootHub] Applied to {scenePath} on {map.name}: plaza floor y={floor:0.###}, swapped {swapped} renderers, {beaconCount} beacons.");
    }

    private static void LoadBuiltMaterials()
    {
        Material L(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{n}.mat");
        _kitNight = L("M_Reboot_KitNight");
        _darkMetal = L("M_Reboot_DarkMetal");
        if (_kitNight == null || _darkMetal == null)
            throw new InvalidOperationException("Build assets first.");
    }

    private static float MeasurePlazaFloor(Transform mapRoot)
    {
        var origin = new Vector3(PlazaCenterXZ.x, 60f, PlazaCenterXZ.y - 8f);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.GetComponentInParent<CharacterController>() != null) continue;
            if (!hit.transform.IsChildOf(mapRoot)) continue;
            best = Mathf.Max(best, hit.point.y);
        }
        if (float.IsNegativeInfinity(best))
            throw new InvalidOperationException("Plaza floor not found under the plaza center.");
        return best;
    }

    // 광장 중심에서 남(-1)/북(+1)으로 0.25m씩 나아가며 바닥이 처음 높아지는 곳(계단 첫 단)의 z를 잰다.
    // 중앙 소품(벤치·기둥)을 피하려고 중심에서 8m 떨어진 곳부터 찾는다. 못 찾으면 이전 맵 값으로 둔다.
    private static float GateZ(Vector3 center, float side, float fallbackZ)
    {
        const float skip = 8f;
        for (float d = 0f; d <= 24f; d += 0.25f)
        {
            float z = center.z + side * (skip + d);
            if (FirstHit(new Vector3(center.x, center.y + 1.2f, z), Vector3.down, 2f, out RaycastHit hit) && hit.point.y > center.y + 0.08f)
            {
                Debug.Log($"[RebootHub] {(side < 0 ? "South" : "North")} gate (first step) at z={z:0.##}");
                return z;
            }
        }
        Debug.LogWarning($"[RebootHub] {(side < 0 ? "South" : "North")} gate not found; using z={fallbackZ}.");
        return fallbackZ;
    }

    // start에서 dir 방향의 벽면 z를 잰다. 세 높이(0.5/1.0/1.5m)가 모두 맞아야 벽으로 보고, 가장 가까운 면을 쓴다.
    private static float WallFaceZ(Vector3 start, Vector3 dir, float floor, float fallbackZ)
    {
        float nearest = float.PositiveInfinity;
        foreach (float h in new[] { 0.5f, 1f, 1.5f })
        {
            if (!FirstHit(new Vector3(start.x, floor + h, start.z), dir, 10f, out RaycastHit hit))
            {
                Debug.LogWarning($"[RebootHub] No wall from {start} at +{h}m; using z={fallbackZ}.");
                return fallbackZ;
            }
            nearest = Mathf.Min(nearest, hit.distance);
        }
        float z = start.z + dir.z * nearest;
        Debug.Log($"[RebootHub] Wall face for x={start.x:0.##} at z={z:0.##}");
        return z;
    }

    // 플레이어·적 캡슐을 제외한 가장 가까운 충돌체.
    private static bool FirstHit(Vector3 origin, Vector3 dir, float maxDistance, out RaycastHit best)
    {
        best = default;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, dir, maxDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<CharacterController>() != null || hit.collider.GetComponentInParent<EnemyAgent>() != null)
                continue;
            if (!found || hit.distance < best.distance)
            {
                best = hit;
                found = true;
            }
        }
        return found;
    }

    private static GameObject FindMapRoot()
    {
        foreach (string name in MapRootNames)
        {
            GameObject found = GameObject.Find(name);
            if (found != null) return found;
        }
        throw new InvalidOperationException($"Map root not found (tried {string.Join(", ", MapRootNames)}).");
    }

    private static Transform Group(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go.transform;
    }

    private static void Place(string prefabName, Transform parent, Vector3 position, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/{prefabName}.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    private static void Marker(Transform parent, string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
    }

    private static void AddLight(Transform parent, string name, LightType type, Color color, float intensity, float range, Vector3 position, Vector3 euler, float spotAngle = 30f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
        var light = go.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.spotAngle = spotAngle;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
    }

    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }

    private static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
