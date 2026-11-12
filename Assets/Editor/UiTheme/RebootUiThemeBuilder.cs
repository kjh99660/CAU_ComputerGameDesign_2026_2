using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// REBOOT UI 테마("D 고스트 + 사선 스트립 + 선만 + 육각 게이지 + 숫자 타이머 + 글리치, 발광 2")를 적용한다.
// - 원본 스프라이트 팩(Assets/Sprites/UI)은 수정하지 않고, 같은 크기의 새 스프라이트를 Assets/Sprites/UI_Reboot에 생성한다.
// - UI 프리팹 8개에서 이미지 스프라이트, 글자색, TMP 재질, 제목 글리치 컴포넌트만 바꾼다. 배치(RectTransform)와 View 바인딩은 그대로다.
// - 다시 실행해도 같은 경로를 덮어써 GUID를 유지한다.
public static class RebootUiThemeBuilder
{
    private const string Root = "Assets/Sprites/UI_Reboot";
    private const string PrefabRoot = "Assets/Prefabs/UI/";
    private const string LegacyFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string FontRoot = "Assets/Fonts/ChakraPetch";
    // 영문 UI 문구와 숫자만 쓰므로 정적 문자셋으로 굽는다(편집 중 동적 아틀라스가 에셋을 더럽히지 않게).
    private const string FontCharacters = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·×—…";
    private static readonly string[] Prefabs = { "Start", "Dialogue", "Countdown", "Battle", "Pause", "RetryConfirmation", "Finishing", "Result" };

    private const float SkewTan = 0.249f;   // tan(14°)
    private const float MaxSkewSmall = 14f; // 높이 200 이하(HUD 패널, 버튼, 타일): 글자와 겹치지 않게 얕게
    private const float MaxSkewLarge = 26f;

    // 원본 스프라이트는 투명 여백이 커서 요소가 화면 모서리에 붙어 있었다. 새 스프라이트는 여백이 작아
    // 모서리 요소만 화면 안쪽(약 40px)으로 옮긴다. 값은 원래 빌더(GameDemoUiPrefabBuilder)의 의도 위치에 맞췄다.
    private readonly struct Layout
    {
        public readonly Vector2 Position; public readonly Vector2? Size;
        public Layout(Vector2 position, Vector2? size = null) { Position = position; Size = size; }
    }

    private static readonly Dictionary<string, Layout> LayoutFixes = new()
    {
        ["Battle/PauseButton"] = new Layout(new Vector2(40f, -40f)),
        ["Battle/ScorePanel"] = new Layout(new Vector2(148f, -40f)),
        ["Battle/TimerPanel"] = new Layout(new Vector2(-40f, -40f)),
        ["Dialogue/DialogueBox"] = new Layout(new Vector2(0f, 32f)),
        ["Dialogue/SkipButton"] = new Layout(new Vector2(-40f, -36f)),
        // 메뉴 패널이 0.8배 축소되어 있고 버튼이 오른쪽으로 치우쳐 패널 밖으로 나갔다. 배율 1, 가운데 정렬로 맞춘다.
        ["Pause/MenuPanel"] = new Layout(new Vector2(0f, 30f), new Vector2(520f, 620f)),
        ["Pause/HeaderFrame"] = new Layout(new Vector2(0f, 243f)),
        ["Pause/ResumeButton"] = new Layout(new Vector2(0f, 105f)),
        ["Pause/RetryButton"] = new Layout(new Vector2(0f, -35f)),
        ["Pause/QuitButton"] = new Layout(new Vector2(0f, -175f)),
    };
    private const float GlowAlpha = 0f;     // 블러(발광 번짐) 제거 요청으로 0. 다시 켜려면 0.32(시안의 발광 2)
    private const float GlowSigma = 7f;

    // 진단용 스위치: false면 글자 재질을 TMP 기본 재질로 둔다(밑그림자/발광 셰이더 변형 미사용).
    private static bool UseTextMaterials => Environment.GetEnvironmentVariable("REBOOT_UI_NO_TEXT_MATERIALS") != "1";

    private enum Kind { Panel, Header, Button, Thin, Tile, Pause, Hex, Warn, TimeUp, Modal }
    private enum Tone { Primary, Muted, Alert, Accent }

    private readonly struct Spec
    {
        public readonly string Name; public readonly int W, H; public readonly Kind Kind; public readonly Tone Tone;
        public Spec(string name, int w, int h, Kind kind, Tone tone) { Name = name; W = w; H = h; Kind = kind; Tone = tone; }
    }

    // 프리팹이 실제로 쓰는 원본 스프라이트 이름과 RectTransform 크기(1920x1080 기준)를 그대로 따른다.
    private static readonly Spec[] Specs =
    {
        new("Battle_ScorePanel", 510, 122, Kind.Panel, Tone.Primary),
        new("Battle_TimerPanel", 470, 142, Kind.Panel, Tone.Primary),
        new("Battle_PauseButton", 94, 116, Kind.Pause, Tone.Primary),
        new("Start_TitleFrame", 820, 300, Kind.Header, Tone.Primary),
        new("Start_StartButton", 430, 124, Kind.Button, Tone.Accent),
        new("Start_ControlHintPanel", 760, 128, Kind.Thin, Tone.Muted),
        new("Dialogue_TextBox", 1510, 300, Kind.Panel, Tone.Primary),
        new("Dialogue_SkipButton", 230, 78, Kind.Button, Tone.Muted),
        new("Countdown_NumberFrame", 410, 410, Kind.Hex, Tone.Primary),
        new("Countdown_ReadyFrame", 560, 145, Kind.Button, Tone.Accent),
        new("Pause_MenuPanel", 520, 620, Kind.Panel, Tone.Primary),
        new("Pause_HeaderFrame", 420, 105, Kind.Header, Tone.Primary),
        new("Pause_ButtonPrimary", 360, 104, Kind.Button, Tone.Primary),
        new("Pause_ButtonSecondary", 360, 104, Kind.Button, Tone.Muted),
        new("Pause_ButtonDanger", 360, 104, Kind.Button, Tone.Alert),
        new("RetryConfirmation_ModalPanel", 880, 410, Kind.Modal, Tone.Alert),
        new("RetryConfirmation_WarningIcon", 120, 120, Kind.Warn, Tone.Alert),
        new("RetryConfirmation_ConfirmButton", 250, 82, Kind.Button, Tone.Alert),
        new("RetryConfirmation_CancelButton", 250, 82, Kind.Button, Tone.Muted),
        new("Finishing_ScorePanel", 510, 122, Kind.Panel, Tone.Primary),
        new("Finishing_TimeUpFrame", 700, 220, Kind.TimeUp, Tone.Alert),
        new("Result_TitleFrame", 550, 130, Kind.Header, Tone.Primary),
        new("Result_FinalScorePanel", 650, 170, Kind.Panel, Tone.Accent),
        new("Result_StatTile", 196, 188, Kind.Tile, Tone.Muted),
        new("Result_RetryButton", 400, 112, Kind.Button, Tone.Accent),
    };

    // 제목 글리치는 사용자 요청으로 제거했다. 남아 있는 UiGlitchText 컴포넌트는 적용 시 지운다.

    [MenuItem("Tools/REBOOT UI/Apply Theme (Ghost Skew)")]
    public static void ApplyFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Apply();
    }

    public static void ApplyBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void Apply()
    {
        EnsureFolder(Root);
        EnsureFolder(Root + "/Materials");
        var sprites = new Dictionary<string, Sprite>();
        foreach (Spec spec in Specs)
            sprites[spec.Name] = WriteSprite(spec);
        TMP_FontAsset font = EnsureFontAssets();
        Material text = BuildTextMaterial(font);
        foreach (string prefab in Prefabs)
            ApplyToPrefab(prefab, sprites, font, text);
        foreach (string stale in new[] { "LiberationSans SDF - Reboot Text", "LiberationSans SDF - Reboot Heading" })
            AssetDatabase.DeleteAsset($"{Root}/Materials/{stale}.mat");
        foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { Root }))
        {
            Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
        }
        Debug.Log($"[RebootUI] Generated {sprites.Count} sprites and themed {Prefabs.Length} UI prefabs.");
    }

    // ------------------------------------------------------------------ prefabs

    private static void ApplyToPrefab(string prefabName, Dictionary<string, Sprite> sprites, TMP_FontAsset font, Material text)
    {
        string path = PrefabRoot + prefabName + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            TMP_FontAsset legacyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LegacyFontPath);
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null)
                {
                    if (image.name == "DimmedOverlay")
                        image.color = UiPalette.WithAlpha(UiPalette.Base, image.color.a);
                    continue;
                }
                string spriteName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(image.sprite));
                if (sprites.TryGetValue(spriteName, out Sprite replacement))
                {
                    image.sprite = replacement;
                    image.color = Color.white;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = false;
                }
            }

            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                if (rect.parent == root.transform && LayoutFixes.TryGetValue(prefabName + "/" + rect.name, out Layout layout))
                {
                    rect.anchoredPosition = layout.Position;
                    if (layout.Size.HasValue)
                    {
                        rect.sizeDelta = layout.Size.Value;
                        rect.localScale = Vector3.one;
                    }
                }

            foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>(true))
            {
                tmp.color = TextColor(prefabName, tmp);
                if (tmp.font == legacyFont || tmp.font == font)
                {
                    tmp.font = font;
                    tmp.fontSharedMaterial = UseTextMaterials ? text : font.material;
                }
                if (prefabName == "Start" && tmp.name == "Title") tmp.text = "REBOOT";
                if (prefabName == "Start" && tmp.name == "Subtitle") { tmp.text = "COMM HUB 07  //  100s EMERGENCY REBOOT"; tmp.fontSize = 22; }
                if (prefabName == "Start" && tmp.name == "Title") tmp.characterSpacing = 18f;
                if (prefabName == "Start" && tmp.name == "ControlHint")
                {
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 14f;
                    tmp.fontSizeMax = 21f;
                }
                foreach (MonoBehaviour behaviour in tmp.GetComponents<MonoBehaviour>())
                    if (behaviour != null && behaviour.GetType().Name == "UiGlitchText")
                        Object.DestroyImmediate(behaviour, true);
            }

            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.Lerp(Color.white, UiPalette.Accent, 0.35f);
                colors.pressedColor = Color.Lerp(Color.white, UiPalette.Accent, 0.7f);
                colors.selectedColor = colors.highlightedColor;
                button.colors = colors;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Color TextColor(string prefab, TMP_Text tmp)
    {
        string name = tmp.name;
        string parent = tmp.transform.parent != null ? tmp.transform.parent.name : string.Empty;
        if (name == "Label")
        {
            switch (parent)
            {
                case "StartButton": return UiPalette.Accent;
                case "QuitButton":
                case "ConfirmButton": return UiPalette.Alert;
                case "SkipButton":
                case "ContinuePrompt": return UiPalette.Muted;
                case "RetryButton": return prefab == "Result" ? UiPalette.Accent : UiPalette.Ink;
                default: return UiPalette.Ink;
            }
        }
        if (name.EndsWith("Label", StringComparison.Ordinal) || name == "Subtitle" || name == "ControlHint") return UiPalette.Muted;
        if (name == "SpeakerName" || name == "ReadyText" || name == "SlowMotion") return UiPalette.Accent;
        if (name == "TimeUp" || (prefab == "RetryConfirmation" && name == "Title") || (prefab == "Finishing" && name == "TimerValue")) return UiPalette.Alert;
        return UiPalette.Ink;
    }

    // Chakra Petch Medium(기본)과 Bold(굵게)를 정적 SDF 에셋으로 만들고, Medium의 700 굵기를 Bold 에셋에 연결한다.
    private static TMP_FontAsset EnsureFontAssets()
    {
        TMP_FontAsset medium = EnsureFontAsset("ChakraPetch-Medium");
        TMP_FontAsset bold = EnsureFontAsset("ChakraPetch-Bold");
        TMP_FontWeightPair[] weights = medium.fontWeightTable; // 읽기 전용 속성이지만 배열 원소는 수정할 수 있다
        if (weights != null && weights.Length > 7)
            weights[7].regularTypeface = bold;
        else
            Debug.LogWarning("[RebootUI] Font weight table missing; Bold text will use faux bold.");
        EditorUtility.SetDirty(medium);
        AssetDatabase.SaveAssetIfDirty(medium);
        return medium;
    }

    private static TMP_FontAsset EnsureFontAsset(string name)
    {
        string path = $"{FontRoot}/{name} SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<Font>($"{FontRoot}/{name}.ttf")
                     ?? throw new InvalidOperationException("Font file missing: " + name);
        TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
        asset.name = name + " SDF";
        if (!asset.TryAddCharacters(FontCharacters, out string missing) && !string.IsNullOrEmpty(missing))
            Debug.LogWarning($"[RebootUI] {name}: missing characters '{missing}'");
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(asset, path);
        asset.atlasTextures[0].name = name + " SDF Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
        asset.material.name = name + " SDF Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        return asset;
    }

    // 번짐(밑그림자 흐림, 발광) 없이 얇은 외곽선으로만 밝은 배경 위 가독성을 확보한다.
    private static Material BuildTextMaterial(TMP_FontAsset font)
    {
        Material text = SaveMaterial("ChakraPetch SDF - Reboot Text", new Material(font.material));
        text.DisableKeyword("UNDERLAY_ON");
        text.DisableKeyword("UNDERLAY_INNER");
        text.DisableKeyword("GLOW_ON");
        text.SetFloat("_OutlineWidth", 0.14f);
        text.SetColor("_OutlineColor", new Color(0.02f, 0.03f, 0.04f, 0.9f));
        text.SetFloat("_OutlineSoftness", 0f);
        text.SetFloat("_FaceDilate", 0.05f);
        EditorUtility.SetDirty(text);
        return text;
    }

    private static Material SaveMaterial(string name, Material material)
    {
        string path = $"{Root}/Materials/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        existing.shader = material.shader;
        existing.CopyPropertiesFromMaterial(material);
        existing.shaderKeywords = material.shaderKeywords;
        Object.DestroyImmediate(material);
        return existing;
    }

    // ------------------------------------------------------------------ sprites

    private static Color ToneColor(Tone tone) => tone switch
    {
        Tone.Muted => UiPalette.Muted,
        Tone.Alert => UiPalette.Alert,
        Tone.Accent => UiPalette.Accent,
        _ => UiPalette.Primary,
    };

    private static Sprite WriteSprite(Spec spec)
    {
        var p = new Painter(spec.W, spec.H);
        Color line = ToneColor(spec.Tone);
        Color fill = UiPalette.Base;
        // 게임 화면과 구분되도록 반투명 검정 바탕을 깐다(시안의 '선만'보다 진하게, 사용자 요청 2026-11-11).
        // 프로젝트가 Linear 색공간이라 UI 알파 합성이 옅게 보이므로 체감 기준으로 60~75%를 쓴다.
        float m = spec.Kind == Kind.Warn ? 22f : spec.W < 140 ? 8f : 12f;   // 발광이 잘리지 않도록 가장자리 여백
        float ml = 6f;                         // 왼쪽 굵은 선은 가장자리 가까이 두어 안쪽 글자와 겹치지 않게 한다
        float W = spec.W, H = spec.H;
        float k = Mathf.Min((H - 2 * m) * SkewTan, H <= 200f ? MaxSkewSmall : MaxSkewLarge);

        switch (spec.Kind)
        {
            case Kind.Panel:
            case Kind.Header:
            case Kind.Button:
            case Kind.Tile:
            case Kind.Thin:
            case Kind.Modal:
            case Kind.TimeUp:
            {
                Vector2[] body = Parallelogram(ml, m, W - m, H - m, k);
                float fillAlpha = spec.Kind switch
                {
                    Kind.Button => 0.68f, Kind.Thin => 0.58f, Kind.Modal => 0.78f, Kind.TimeUp => 0.66f, _ => 0.64f,
                };
                p.Fill(body, fill, fillAlpha);
                float bar = spec.Kind == Kind.Thin ? 4f : 7f;
                Vector2[] leftBar = Parallelogram(ml, m, ml + bar + k, H - m, k, leftOnly: bar);
                Vector2[] bottom = Parallelogram(ml, m, W - m - k, m + 2.5f, 0f);
                p.Glow(leftBar, line, GlowAlpha);
                p.Glow(bottom, line, GlowAlpha * 0.5f);
                p.Fill(leftBar, line, 1f);
                p.Fill(bottom, line, 0.55f);
                if (spec.Kind == Kind.Header || spec.Kind == Kind.Button || spec.Kind == Kind.Modal || spec.Kind == Kind.TimeUp)
                {
                    // 오른쪽 위 경고 사선 조각(사선 스트립 시그니처)
                    float sw = Mathf.Min(70f, W * 0.22f);
                    Vector2[] tick = Parallelogram(W - m - sw - 10f, H - m - 9f, W - m - 10f, H - m - 3f, 1.5f);
                    p.Stripes(tick, line, 10f, 1f);
                }
                if (spec.Kind == Kind.Modal)
                    p.Stripes(Parallelogram(m + k + 24f, H - m - 30f, W - m - 24f, H - m - 16f, 3.5f), line, 28f, 0.9f);
                if (spec.Kind == Kind.TimeUp)
                {
                    p.Stripes(Parallelogram(m + k + 20f, H - m - 26f, W - m - 90f, H - m - 14f, 3f), line, 24f, 0.85f);
                    p.Stripes(Parallelogram(m + 20f, m + 14f, W - m - k - 20f, m + 26f, 3f), line, 24f, 0.85f);
                }
                break;
            }
            case Kind.Pause:
            {
                Vector2[] body = Parallelogram(m, m, W - m, H - m, Mathf.Min(k, 14f));
                p.Fill(body, fill, 0.68f);
                Vector2[] bar = Parallelogram(m, m, m + 6f + Mathf.Min(k, 14f), H - m, Mathf.Min(k, 14f), leftOnly: 6f);
                p.Glow(bar, line, GlowAlpha);
                p.Fill(bar, line, 1f);
                p.Fill(Parallelogram(m, m, W - m - Mathf.Min(k, 14f), m + 2.5f, 0f), line, 0.55f);
                float cx = W * 0.5f + 4f, cy = H * 0.5f, bh = H * 0.32f;
                // 막대 폭(9)이 기울기보다 작으므로 왼쪽 띠 형태(leftOnly)로 만들어 도형이 뒤집히지 않게 한다.
                float tilt = bh * SkewTan * 0.6f;
                Vector2[] a = Parallelogram(cx - 18f, cy - bh * 0.5f, 0f, cy + bh * 0.5f, tilt, leftOnly: 9f);
                Vector2[] b = Parallelogram(cx + 1f, cy - bh * 0.5f, 0f, cy + bh * 0.5f, tilt, leftOnly: 9f);
                p.Glow(a, line, GlowAlpha); p.Glow(b, line, GlowAlpha);
                p.Fill(a, line, 1f); p.Fill(b, line, 1f);
                break;
            }
            case Kind.Hex:
            {
                Vector2 c = new(W * 0.5f, H * 0.5f);
                Vector2[] outer = Hexagon(c, Mathf.Min(W, H) * 0.5f - m - 4f);
                Vector2[] inner = Hexagon(c, Mathf.Min(W, H) * 0.5f - m - 26f);
                p.Fill(outer, fill, 0.62f);
                p.StrokeGlow(outer, 5f, line, GlowAlpha);
                p.Stroke(outer, 5f, line, 1f);
                p.Stroke(inner, 2f, UiPalette.Muted, 0.6f);
                foreach (Vector2 v in outer)
                {
                    Vector2 d = (v - c).normalized;
                    p.Fill(Quad(v - d * 2f, v - d * 16f, 3f), UiPalette.Accent, 1f);
                }
                break;
            }
            case Kind.Warn:
            {
                Vector2[] tri = { new(W * 0.5f, H - m - 4f), new(m + 2f, m + 6f), new(W - m - 2f, m + 6f) };
                p.Fill(tri, fill, 0.62f);
                p.StrokeGlow(tri, 6f, line, GlowAlpha);
                p.Stroke(tri, 6f, line, 1f);
                p.Fill(Parallelogram(W * 0.5f - 5f, H * 0.42f, W * 0.5f + 5f, H * 0.72f, 0f), line, 1f);
                p.Fill(Parallelogram(W * 0.5f - 5f, H * 0.26f, W * 0.5f + 5f, H * 0.35f, 0f), line, 1f);
                break;
            }
        }
        return p.Save($"{Root}/{spec.Name}.png");
    }

    // y-up 좌표. 위쪽 가장자리가 오른쪽으로 k만큼 밀린 평행사변형(CCW). leftOnly>0이면 왼쪽 띠만 만든다.
    private static Vector2[] Parallelogram(float x0, float y0, float x1, float y1, float k, float leftOnly = 0f)
    {
        if (leftOnly > 0f)
            return new[] { new Vector2(x0, y0), new Vector2(x0 + leftOnly, y0), new Vector2(x0 + leftOnly + k, y1), new Vector2(x0 + k, y1) };
        return new[] { new Vector2(x0, y0), new Vector2(x1 - k, y0), new Vector2(x1, y1), new Vector2(x0 + k, y1) };
    }

    private static Vector2[] Hexagon(Vector2 c, float r)
    {
        var pts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = (90f + i * 60f) * Mathf.Deg2Rad; // 꼭짓점이 위아래, CCW
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return pts;
    }

    private static Vector2[] Quad(Vector2 a, Vector2 b, float halfWidth)
    {
        Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * halfWidth;
        Vector2[] q = { a - n, b - n, b + n, a + n };
        return Area(q) < 0 ? new[] { q[3], q[2], q[1], q[0] } : q;
    }

    private static float Area(Vector2[] p)
    {
        float s = 0f;
        for (int i = 0; i < p.Length; i++) { Vector2 a = p[i], b = p[(i + 1) % p.Length]; s += a.x * b.y - b.x * a.y; }
        return s * 0.5f;
    }

    // 볼록 다각형의 부호 거리(안쪽 음수)로 안티앨리어싱한 도형을 그리는 작은 래스터라이저.
    private sealed class Painter
    {
        private readonly int _w, _h;
        private readonly Color[] _px;

        public Painter(int w, int h) { _w = w; _h = h; _px = new Color[w * h]; }

        private static float Sd(Vector2[] poly, float x, float y)
        {
            float d = float.NegativeInfinity;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                Vector2 e = b - a;
                Vector2 n = new Vector2(e.y, -e.x).normalized;
                d = Mathf.Max(d, (x - a.x) * n.x + (y - a.y) * n.y);
            }
            return d;
        }

        private void Bounds(Vector2[] poly, float pad, out int x0, out int y0, out int x1, out int y1)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector2 v in poly) { minX = Mathf.Min(minX, v.x); minY = Mathf.Min(minY, v.y); maxX = Mathf.Max(maxX, v.x); maxY = Mathf.Max(maxY, v.y); }
            x0 = Mathf.Max(0, Mathf.FloorToInt(minX - pad)); y0 = Mathf.Max(0, Mathf.FloorToInt(minY - pad));
            x1 = Mathf.Min(_w - 1, Mathf.CeilToInt(maxX + pad)); y1 = Mathf.Min(_h - 1, Mathf.CeilToInt(maxY + pad));
        }

        private void Blend(int x, int y, Color c, float a)
        {
            if (a <= 0f) return;
            int i = y * _w + x;
            Color d = _px[i];
            float oa = a + d.a * (1f - a);
            if (oa <= 0f) return;
            _px[i] = new Color((c.r * a + d.r * d.a * (1f - a)) / oa, (c.g * a + d.g * d.a * (1f - a)) / oa, (c.b * a + d.b * d.a * (1f - a)) / oa, oa);
        }

        public void Fill(Vector2[] poly, Color c, float alpha)
        {
            Bounds(poly, 1f, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Blend(x, y, c, alpha * Mathf.Clamp01(0.5f - Sd(poly, x + 0.5f, y + 0.5f)));
        }

        public void Glow(Vector2[] poly, Color c, float alpha)
        {
            if (alpha <= 0f) return;
            Bounds(poly, GlowSigma * 3f, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Max(0f, Sd(poly, x + 0.5f, y + 0.5f));
                Blend(x, y, c, alpha * Mathf.Exp(-d * d / (2f * GlowSigma * GlowSigma)));
            }
        }

        public void Stroke(Vector2[] poly, float width, Color c, float alpha)
        {
            Bounds(poly, width + 1f, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Blend(x, y, c, alpha * Mathf.Clamp01(0.5f - (Mathf.Abs(Sd(poly, x + 0.5f, y + 0.5f)) - width * 0.5f)));
        }

        public void StrokeGlow(Vector2[] poly, float width, Color c, float alpha)
        {
            if (alpha <= 0f) return;
            Bounds(poly, GlowSigma * 3f + width, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Max(0f, Mathf.Abs(Sd(poly, x + 0.5f, y + 0.5f)) - width * 0.5f);
                Blend(x, y, c, alpha * Mathf.Exp(-d * d / (2f * GlowSigma * GlowSigma)));
            }
        }

        public void Stripes(Vector2[] poly, Color c, float period, float alpha)
        {
            Bounds(poly, 1f, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float cover = Mathf.Clamp01(0.5f - Sd(poly, x + 0.5f, y + 0.5f));
                // 45° 사선 줄무늬, 채움 비율 50%, 경계는 1px 안티앨리어싱
                float s = Mathf.Repeat(x - y, period);
                float on = Mathf.Clamp01(period * 0.25f - Mathf.Abs(s - period * 0.25f) + 0.5f);
                Blend(x, y, c, alpha * cover * on);
            }
        }

        public Sprite Save(string assetPath)
        {
            var tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false);
            tex.SetPixels(_px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), assetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }

    // ------------------------------------------------------------------ previews

    // 맵 캡처를 배경으로 각 UI 프리팹을 1920x1080으로 렌더링해 저장한다(Scene 저장 없음).
    // 실행: -executeMethod RebootUiThemeBuilder.CapturePreviews -rebootOut <dir> -rebootBackdrop <jpg|png>
    public static void CapturePreviews()
    {
        try
        {
            string dir = GetArg("-rebootOut") ?? Path.GetFullPath("Docs/UiTheme/Previews");
            string backdropPath = GetArg("-rebootBackdrop");
            Directory.CreateDirectory(dir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("PreviewCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << LayerMask.NameToLayer("UI");
            cam.orthographic = true;

            var canvasGo = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 5f;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            Texture2D backdrop = null;
            if (!string.IsNullOrEmpty(backdropPath) && File.Exists(backdropPath))
            {
                backdrop = new Texture2D(2, 2);
                backdrop.LoadImage(File.ReadAllBytes(backdropPath));
                var bg = new GameObject("Backdrop", typeof(RectTransform), typeof(RawImage));
                bg.layer = canvasGo.layer;
                var bgRect = (RectTransform)bg.transform;
                bgRect.SetParent(canvasGo.transform, false);
                bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
                bg.GetComponent<RawImage>().texture = backdrop;
            }

            foreach (string name in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + name + ".prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasGo.transform);
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                instance.SetActive(true);
                FillSample(name, instance);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SavePng(rt, Path.Combine(dir, $"ui_{name}.png"));
                Object.DestroyImmediate(instance);
            }
            cam.targetTexture = null;
            rt.Release();
            if (backdrop != null) Object.DestroyImmediate(backdrop);
            Debug.Log("[RebootUI] Previews written to " + dir);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Edit Mode에서는 View의 OnEnable이 돌지 않으므로 캡처용 예시 값을 직접 채운다.
    private static void FillSample(string name, GameObject root)
    {
        void Set(string node, string value)
        {
            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t.name == node) t.text = value;
        }
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        switch (name)
        {
            case "Battle":
                var battle = root.GetComponent<BattleView>();
                if (battle != null)
                {
                    battle.AutoBind();
                    typeof(BattleView).GetMethod("EnsureSpecialGauge", Any)?.Invoke(battle, null);
                    battle.RenderScore(12540);
                    battle.RenderTimer(72f);
                    battle.RenderGauge(0.62f);
                }
                break;
            case "Dialogue":
                var dialogue = root.GetComponent<DialogueView>();
                if (dialogue != null)
                {
                    dialogue.AutoBind();
                    typeof(DialogueView).GetMethod("ApplyDialogueFont", Any)?.Invoke(dialogue, null);
                }
                Set("SpeakerName", "노아");
                Set("BodyText", "네 식별 정보가 수배 명단에 남아 있어. 초기화까지 100초가 필요해.");
                break;
            case "Finishing":
                Set("ScoreValue", "012540");
                break;
            case "Result":
                Set("FinalScoreValue", "012540"); Set("KillsValue", "78"); Set("MaxComboValue", "24"); Set("HitsTakenValue", "3"); Set("SpecialUsesValue", "4");
                break;
        }
    }

    private static void SavePng(RenderTexture rt, string path)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static string GetArg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
