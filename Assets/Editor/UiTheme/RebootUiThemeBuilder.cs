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
    // 대화 초상화: 노아(왼쪽), K-7(오른쪽). 원본 스프라이트 팩의 이미지를 그대로 쓴다.
    private const string LeftPortraitPath = "Assets/Sprites/UI/Dialogue/Dialogue_LeftCharacterPortrait.png";
    private const string RightPortraitPath = "Assets/Sprites/UI/Dialogue/Dialogue_RightCharacterPortrait.png";

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
        // 종료 화면의 시간 표시는 전투 HUD 프리팹과 같은 기준 위치에 둔다(EnsureFinishingTimer).
        ["Finishing/TimerPanel"] = new Layout(new Vector2(-40f, -40f)),
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
            if (prefabName == "Dialogue")
                EnsureDialogueLayout(root);
            if (prefabName == "Finishing")
                EnsureFinishingTimer(root);
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

    // 대화 프리팹에만 대화 화면 배치를 적용한다. 스프라이트·서체·다른 프리팹은 다시 만들지 않는다.
    // 실행: -executeMethod RebootUiThemeBuilder.ApplyDialogueLayoutBatch
    public static void ApplyDialogueLayoutBatch()
    {
        try
        {
            string path = PrefabRoot + "Dialogue.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsureDialogueLayout(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("[RebootUI] Dialogue layout applied.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // 종료 화면 프리팹에만 시간 표시를 더한다. 스프라이트·서체·다른 프리팹은 다시 만들지 않는다.
    // 실행: -executeMethod RebootUiThemeBuilder.ApplyFinishingTimerBatch
    public static void ApplyFinishingTimerBatch()
    {
        try
        {
            string path = PrefabRoot + "Finishing.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsureFinishingTimer(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("[RebootUI] Finishing timer applied.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // 연출 조절값(카운트다운·HUD·결과 View에 새로 생긴 필드)을 프리팹에 적는다. 다른 내용은 바꾸지 않는다.
    // 프리팹에 값이 적혀 있지 않으면 Unity는 그 프리팹을 가져온 시점의 코드 기본값을 쓴다. 그래서 나중에 코드의
    // 기본값만 바꾸면 이미 가져온 컴퓨터에는 반영되지 않아 컴퓨터마다 값이 달라진다. 값은 프리팹에 두고 Inspector에서 조절한다.
    // 실행: -executeMethod RebootUiThemeBuilder.SaveViewSettingsBatch
    public static void SaveViewSettingsBatch()
    {
        try
        {
            foreach (string name in new[] { "Countdown", "Battle", "Result" })
            {
                string path = PrefabRoot + name + ".prefab";
                // 이전에 가져올 때 굳은 기본값이 섞이지 않도록 먼저 다시 가져온다.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    root.GetComponent<UIScreenView>()?.AutoBind();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Debug.Log("[RebootUI] View settings saved into Countdown, Battle and Result prefabs.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // 대화 화면의 노아(왼쪽)·K-7(오른쪽) 초상화. 위치와 크기는 팀 원래 빌더(GameDemoUiPrefabBuilder.BuildDialogue)의 값이다.
    // 대사 상자 뒤에 그려지고 클릭은 가로채지 않는다. 이미 있으면 배치만 다시 맞춘다(여러 번 실행해도 같다).
    private static void EnsureDialogueLayout(GameObject root)
    {
        Image left = EnsurePortrait(root.transform, "LeftPortrait", LeftPortraitPath, new Vector2(0f, 0f), new Vector2(54f, 238f));
        Image right = EnsurePortrait(root.transform, "RightPortrait", RightPortraitPath, new Vector2(1f, 0f), new Vector2(-54f, 238f));
        Transform box = root.transform.Find("DialogueBox");
        if (box == null) return;
        // 대사 상자 바로 앞(먼저 그려지는 쪽)에 왼쪽, 오른쪽 순서로 둔다. 둘 다 뒤로 보낸 뒤 끼워 넣어 재실행해도 순서가 같다.
        left.transform.SetAsLastSibling();
        right.transform.SetAsLastSibling();
        left.transform.SetSiblingIndex(box.GetSiblingIndex());
        right.transform.SetSiblingIndex(box.GetSiblingIndex());
        Image next = EnsureNextIndicator(box);

        // View가 이름으로 찾지 않아도 되게 직렬화 참조를 연결한다.
        var view = root.GetComponent<DialogueView>();
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("leftPortrait").objectReferenceValue = left;
        so.FindProperty("rightPortrait").objectReferenceValue = right;
        so.FindProperty("nextIndicator").objectReferenceValue = next;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 종료 화면에도 시간 표시(00:00)를 남긴다. 설계상 Finishing에는 Timer 0과 Score만 남고 Pause와 Gauge는 사라진다.
    // 전투 HUD의 TimerPanel을 그대로 복사해 모양이 같다. 위치는 HUD 프리팹의 기준 위치(오른쪽 위에서 40px 안쪽)이고
    // 종료 화면의 점수 패널과 같은 높이다. 씬이 전투 HUD의 위치를 따로 재정의한 경우 그 값은 따라가지 않는다.
    // 같은 구조가 이미 있으면 다시 만들지 않고 값만 맞춘다(여러 번 실행해도 프리팹의 오브젝트 ID가 바뀌지 않는다).
    private static void EnsureFinishingTimer(GameObject root)
    {
        Transform panel = root.transform.Find("TimerPanel");
        bool expected = panel != null && panel.Find("TimerValue") != null && panel.Find("TimerLabel") != null;
        if (!expected)
        {
            // 팀 빌더가 루트 바로 아래에 낱개로 만든 것(TimerPanel, TimerValue)은 HUD와 모양이 달라 치우고 새로 복사한다.
            foreach (string stale in new[] { "TimerPanel", "TimerLabel", "TimerValue" })
            {
                Transform old = root.transform.Find(stale);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            GameObject battle = PrefabUtility.LoadPrefabContents(PrefabRoot + "Battle.prefab");
            try
            {
                Transform source = battle.transform.Find("TimerPanel");
                if (source == null) throw new InvalidOperationException("Battle.prefab has no TimerPanel to copy.");
                panel = Object.Instantiate(source.gameObject, root.transform, false).transform;
                panel.name = "TimerPanel";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(battle);
            }
        }

        // 점수 패널 바로 다음에 그린다. TIME UP 틀과 글자는 그 뒤에 온다.
        Transform score = root.transform.Find("ScorePanel");
        if (score != null && panel.GetSiblingIndex() != score.GetSiblingIndex() + 1)
        {
            panel.SetAsLastSibling();
            panel.SetSiblingIndex(score.GetSiblingIndex() + 1);
        }

        var rect = (RectTransform)panel;
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = LayoutFixes["Battle/TimerPanel"].Position;
        rect.localScale = Vector3.one;
        panel.gameObject.SetActive(true);

        TMP_Text value = panel.Find("TimerValue").GetComponent<TMP_Text>();
        value.text = "00:00";
        value.color = UiPalette.Alert;
        value.rectTransform.localScale = Vector3.one;
        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        // View가 이름으로 찾지 않아도 되게 직렬화 참조를 연결한다.
        var view = root.GetComponent<FinishingView>();
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("timerValue").objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 줄이 다 보이면 깜빡이는 넘김 표시. 글꼴에 기대지 않도록 45도 돌린 작은 사각형(마름모)으로 그린다.
    // 대사 상자 오른쪽 아래 안쪽에 두며, 클릭은 가로채지 않는다.
    private static Image EnsureNextIndicator(Transform box)
    {
        Transform existing = box.Find("NextIndicator");
        GameObject go = existing != null ? existing.gameObject : new GameObject("NextIndicator", typeof(RectTransform), typeof(Image));
        go.layer = box.gameObject.layer;
        var rect = (RectTransform)go.transform;
        if (existing == null) rect.SetParent(box, false);
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-64f, 46f);
        rect.sizeDelta = new Vector2(16f, 16f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        rect.localScale = Vector3.one;
        var image = go.GetComponent<Image>();
        image.sprite = null;
        image.color = UiPalette.Accent;
        image.raycastTarget = false;
        return image;
    }

    private static Image EnsurePortrait(Transform parent, string name, string spritePath, Vector2 corner, Vector2 position)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        if (existing == null) rect.SetParent(parent, false);
        rect.anchorMin = corner;
        rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(420f, 630f);
        rect.localScale = Vector3.one;
        var image = go.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath)
                       ?? throw new InvalidOperationException("Portrait sprite missing: " + spritePath);
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
        return image;
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

            // 화면별 기본 모습에 이어, 연출 도중의 한 순간(EffectFrames)도 같은 방식으로 찍는다.
            var shots = new List<(string prefab, string sample)>();
            foreach (string name in Prefabs) shots.Add((name, name));
            shots.AddRange(EffectFrames);
            foreach ((string prefabName, string sample) in shots)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabRoot + prefabName + ".prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasGo.transform);
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                instance.SetActive(true);
                FillSample(sample, instance);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                SavePng(rt, Path.Combine(dir, $"ui_{sample}.png"));
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
                const string sampleLine = "네 식별 정보가 수배 명단에 남아 있어. 초기화까지 100초가 필요해.";
                if (dialogue != null)
                {
                    dialogue.AutoBind();
                    typeof(DialogueView).GetMethod("ApplyDialogueFont", Any)?.Invoke(dialogue, null);
                    // 실제 실행과 같은 경로로 노아(왼쪽) 강조와 넘김 표시를 그린다.
                    dialogue.BeginSequence();
                    dialogue.Render(new DialogueUiModel("노아", sampleLine, true));
                    dialogue.UpdateNextIndicator(true, 0f);
                }
                else
                {
                    Set("SpeakerName", "노아");
                    Set("BodyText", sampleLine);
                }
                break;
            case "Finishing":
                Set("ScoreValue", "012540");
                break;
            case "Result":
                Set("FinalScoreValue", "012540"); Set("KillsValue", "78"); Set("MaxComboValue", "24"); Set("HitsTakenValue", "3"); Set("SpecialUsesValue", "4");
                break;

            // 아래는 연출 도중의 한 순간이다. 실제 실행과 같은 View 메서드로 자세를 만든다.
            case "Countdown_Pop":
                var pop = root.GetComponent<CountdownView>();
                pop.AutoBind();
                pop.ShowNumber(2);
                pop.AnimateNumber(0.05f);
                break;
            case "Countdown_Start":
                var start = root.GetComponent<CountdownView>();
                start.AutoBind();
                typeof(CountdownView).GetMethod("EnsureFlash", Any)?.Invoke(start, null);
                start.ShowStart();
                start.AnimateStart(0.05f);
                break;
            case "Battle_Warning":
                var warning = root.GetComponent<BattleView>();
                warning.AutoBind();
                typeof(BattleView).GetMethod("EnsureSpecialGauge", Any)?.Invoke(warning, null);
                warning.RenderScore(12540);
                warning.RenderTimer(7f);
                warning.UpdateHud(0.03f);   // 초가 바뀐 직후: 타이머가 살짝 커져 있다
                warning.RenderGauge(1f);
                warning.UpdateHud(0.33f);   // 게이지 맥동이 가장 커진 순간에 가깝다
                break;
            case "Result_Counting":
                var counting = root.GetComponent<ResultView>();
                counting.AutoBind();
                var countUp = new ResultCountUp();
                countUp.Begin(new ResultUiModel(12540, 78, 24, 3, 4), counting.ScoreCountSeconds, counting.StatCountSeconds,
                    counting.StatDelaySeconds, counting.StatStaggerSeconds);
                countUp.Tick(0.55f);
                for (int field = 0; field < ResultCountUp.FieldCount; field++)
                    counting.RenderValue(field, countUp.Shown(field));
                break;
        }
    }

    // 연출 미리보기: (프리팹, 예시 이름). 결과 파일은 ui_<예시 이름>.png다.
    private static readonly (string prefab, string sample)[] EffectFrames =
    {
        ("Countdown", "Countdown_Pop"), ("Countdown", "Countdown_Start"), ("Battle", "Battle_Warning"), ("Result", "Result_Counting"),
    };

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
