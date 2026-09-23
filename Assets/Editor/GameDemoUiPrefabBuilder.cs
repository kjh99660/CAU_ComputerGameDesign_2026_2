using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameDemoUiPrefabBuilder
{
    private const string PrefabRoot = "Assets/Prefabs/UI/";
    private const string SpriteRoot = "Assets/Sprites/UI/";
    private const string MainScenePath = "Assets/Scenes/Main.unity";

    private static readonly Color Cyan = new(0.12f, 0.92f, 1f, 1f);
    private static readonly Color White = new(0.94f, 0.98f, 1f, 1f);
    private static readonly Color Muted = new(0.62f, 0.76f, 0.82f, 1f);
    private static readonly Color Warning = new(1f, 0.36f, 0.28f, 1f);

    [MenuItem("Tools/Game Demo UI/Build All UI Prefabs")]
    public static void BuildAll()
    {
        BuildPrefab("Start", BuildStart);
        BuildPrefab("Dialogue", BuildDialogue);
        BuildPrefab("Countdown", BuildCountdown);
        BuildPrefab("Battle", BuildBattle);
        BuildPrefab("Pause", BuildPause);
        BuildPrefab("RetryConfirmation", BuildRetryConfirmation);
        BuildPrefab("Finishing", BuildFinishing);
        BuildPrefab("Result", BuildResult);

        UpdateMainSceneInstances();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GameDemo UI] Built 8 UI prefabs and synchronized Main scene instances.");
    }

    [MenuItem("Tools/Game Demo UI/Validate UI Prefabs")]
    public static void ValidateAll()
    {
        string[] names =
        {
            "Start", "Dialogue", "Countdown", "Battle", "Pause",
            "RetryConfirmation", "Finishing", "Result"
        };

        var failures = new List<string>();
        foreach (string name in names)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}{name}.prefab");
            if (prefab == null)
            {
                failures.Add($"Missing prefab: {name}");
                continue;
            }

            if (prefab.transform.childCount == 0)
                failures.Add($"Empty prefab: {name}");

            RectTransform root = prefab.GetComponent<RectTransform>();
            if (root == null || root.anchorMin != Vector2.zero || root.anchorMax != Vector2.one)
                failures.Add($"Root does not stretch: {name}");
        }

        if (failures.Count == 0)
            Debug.Log("[GameDemo UI] Validation passed: all 8 prefabs are populated and stretch to the canvas.");
        else
            Debug.LogError("[GameDemo UI] Validation failed:\n" + string.Join("\n", failures));
    }

    private static void BuildPrefab(string name, Action<Transform> build)
    {
        string path = $"{PrefabRoot}{name}.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ClearChildren(root.transform);
            root.layer = LayerMask.NameToLayer("UI");
            Stretch(root.GetComponent<RectTransform>());

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null)
                group = root.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            group.ignoreParentGroups = false;

            build(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildStart(Transform root)
    {
        CreateOverlay(root, 0.48f);
        CreateImage(root, "TitleFrame", "Start/Start_TitleFrame.png", Center, new Vector2(0, 210), new Vector2(820, 300));
        CreateText(root, "Title", "CYBER ARENA", Center, new Vector2(0, 245), new Vector2(720, 110), 68, White, FontStyles.Bold);
        CreateText(root, "Subtitle", "ONE VERSUS MANY", Center, new Vector2(0, 170), new Vector2(600, 55), 27, Cyan, FontStyles.Bold);

        CreateButton(root, "StartButton", "Start/Start_StartButton.png", "START", Center,
            new Vector2(0, -105), new Vector2(430, 124), 42, Cyan);

        CreateImage(root, "ControlHintPanel", "Start/Start_ControlHintPanel.png", BottomCenter,
            new Vector2(0, 58), new Vector2(760, 128));
        CreateText(root, "ControlHint", "WASD  MOVE     MOUSE  CAMERA     LMB  ATTACK     SPACE  DODGE", BottomCenter,
            new Vector2(0, 82), new Vector2(690, 48), 21, Muted, FontStyles.Bold);
    }

    private static void BuildDialogue(Transform root)
    {
        CreateImage(root, "LeftPortrait", "Dialogue/Dialogue_LeftCharacterPortrait.png", BottomLeft,
            new Vector2(54, 238), new Vector2(420, 630), true);
        CreateImage(root, "LeftPortraitFrame", "Dialogue/Dialogue_LeftPortraitFrame.png", BottomLeft,
            new Vector2(26, 210), new Vector2(476, 675));

        Image right = CreateImage(root, "RightPortrait", "Dialogue/Dialogue_RightCharacterPortrait.png", BottomRight,
            new Vector2(-54, 238), new Vector2(420, 630), true);
        right.color = new Color(0.42f, 0.48f, 0.52f, 0.84f);
        CreateImage(root, "RightPortraitFrame", "Dialogue/Dialogue_RightPortraitFrame.png", BottomRight,
            new Vector2(-26, 210), new Vector2(476, 675));

        CreateImage(root, "DialogueBox", "Dialogue/Dialogue_TextBox.png", BottomCenter,
            new Vector2(0, 38), new Vector2(1510, 300));
        CreateImage(root, "SpeakerUnderline", "Dialogue/Dialogue_SpeakerNameUnderline.png", BottomLeft,
            new Vector2(304, 244), new Vector2(360, 55));
        CreateText(root, "SpeakerName", "COMMANDER", BottomLeft, new Vector2(326, 251), new Vector2(330, 48),
            30, Cyan, FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText(root, "BodyText", "The enemy wave is approaching. Hold the line and wait for the signal.", BottomLeft,
            new Vector2(326, 102), new Vector2(1180, 125), 30, White, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        CreateButton(root, "SkipButton", "Dialogue/Dialogue_SkipButton.png", "SKIP", TopRight,
            new Vector2(-46, -42), new Vector2(230, 78), 24, Muted);
        CreateImage(root, "ContinuePrompt", "Dialogue/Dialogue_ContinuePrompt.png", BottomRight,
            new Vector2(-96, 47), new Vector2(330, 82));
        CreateText(root, "ContinueText", "SPACE   CONTINUE", BottomRight,
            new Vector2(-118, 67), new Vector2(280, 40), 19, White, FontStyles.Bold);
    }

    private static void BuildCountdown(Transform root)
    {
        CreateImage(root, "StartFlash", "Countdown/Countdown_StartFlash.png", Center,
            Vector2.zero, new Vector2(760, 440));
        CreateImage(root, "NumberFrame", "Countdown/Countdown_NumberFrame.png", Center,
            new Vector2(0, 55), new Vector2(410, 410));
        CreateText(root, "Number", "3", Center, new Vector2(0, 62), new Vector2(300, 300), 190,
            White, FontStyles.Bold);
        CreateImage(root, "ReadyFrame", "Countdown/Countdown_ReadyFrame.png", Center,
            new Vector2(0, -245), new Vector2(560, 145));
        CreateText(root, "ReadyText", "READY", Center, new Vector2(0, -240), new Vector2(430, 80), 52,
            Cyan, FontStyles.Bold);
    }

    private static void BuildBattle(Transform root)
    {
        CreateButton(root, "PauseButton", "Battle/Battle_PauseButton.png", "II", TopLeft,
            new Vector2(40, -38), new Vector2(94, 94), 30, White);

        CreateImage(root, "ScorePanel", "Battle/Battle_ScorePanel.png", TopLeft,
            new Vector2(158, -42), new Vector2(510, 122));
        CreateText(root, "ScoreLabel", "SCORE", TopLeft, new Vector2(195, -56), new Vector2(150, 34),
            20, Cyan, FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText(root, "ScoreValue", "000000", TopLeft, new Vector2(195, -86), new Vector2(400, 58),
            38, White, FontStyles.Bold, TextAlignmentOptions.Left);

        CreateImage(root, "TimerPanel", "Battle/Battle_TimerPanel.png", TopRight,
            new Vector2(-42, -40), new Vector2(470, 142));
        CreateText(root, "TimerLabel", "TIME", TopRight, new Vector2(-95, -53), new Vector2(160, 34),
            20, Cyan, FontStyles.Bold);
        CreateText(root, "TimerValue", "01:40", TopRight, new Vector2(-85, -82), new Vector2(300, 62),
            42, White, FontStyles.Bold);

        Image fill = CreateImage(root, "SpecialGaugeFill", "Battle/Battle_SpecialGaugeFill.png", BottomCenter,
            new Vector2(0, 36), new Vector2(222, 222));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Bottom;
        fill.fillClockwise = true;
        fill.fillAmount = 0.65f;
        CreateImage(root, "SpecialGaugeFrame", "Battle/Battle_SpecialGaugeFrame.png", BottomCenter,
            new Vector2(0, 36), new Vector2(232, 232));
        CreateImage(root, "SpecialGaugeIcon", "Battle/Battle_SpecialGaugeIcon.png", BottomCenter,
            new Vector2(0, 36), new Vector2(112, 112));
        CreateImage(root, "SkillKeycap", "Battle/Battle_KeycapFrame.png", BottomCenter,
            new Vector2(0, 22), new Vector2(58, 58));
        CreateText(root, "SkillKey", "Q", BottomCenter, new Vector2(0, 31), new Vector2(42, 38), 22,
            White, FontStyles.Bold);
    }

    private static void BuildPause(Transform root)
    {
        CreateOverlay(root, 0.68f);
        CreateImage(root, "MenuPanel", "Pause/Pause_MenuPanel.png", Center,
            Vector2.zero, new Vector2(560, 760));
        CreateImage(root, "HeaderFrame", "Pause/Pause_HeaderFrame.png", Center,
            new Vector2(0, 285), new Vector2(420, 105));
        CreateText(root, "Header", "PAUSED", Center, new Vector2(0, 292), new Vector2(330, 70), 46,
            White, FontStyles.Bold);
        CreateButton(root, "ResumeButton", "Pause/Pause_ButtonPrimary.png", "RESUME", Center,
            new Vector2(0, 105), new Vector2(360, 104), 31, Cyan);
        CreateButton(root, "RetryButton", "Pause/Pause_ButtonSecondary.png", "RETRY", Center,
            new Vector2(0, -35), new Vector2(360, 104), 31, White);
        CreateButton(root, "QuitButton", "Pause/Pause_ButtonDanger.png", "QUIT TO START", Center,
            new Vector2(0, -175), new Vector2(360, 104), 29, Warning);
    }

    private static void BuildRetryConfirmation(Transform root)
    {
        CreateOverlay(root, 0.76f);
        CreateImage(root, "ModalPanel", "RetryConfirmation/RetryConfirmation_ModalPanel.png", Center,
            Vector2.zero, new Vector2(880, 410));
        CreateImage(root, "WarningIcon", "RetryConfirmation/RetryConfirmation_WarningIcon.png", Center,
            new Vector2(-315, 55), new Vector2(120, 120), true);
        CreateText(root, "Title", "RESTART ROUND?", Center, new Vector2(70, 98), new Vector2(560, 58), 38,
            Warning, FontStyles.Bold);
        CreateText(root, "Message", "Current score and battle progress will be reset.", Center,
            new Vector2(70, 25), new Vector2(570, 54), 24, White, FontStyles.Normal);
        CreateButton(root, "CancelButton", "RetryConfirmation/RetryConfirmation_CancelButton.png", "CANCEL", Center,
            new Vector2(-120, -105), new Vector2(250, 82), 25, White);
        CreateButton(root, "ConfirmButton", "RetryConfirmation/RetryConfirmation_ConfirmButton.png", "RESTART", Center,
            new Vector2(180, -105), new Vector2(250, 82), 25, Warning);
    }

    private static void BuildFinishing(Transform root)
    {
        CreateImage(root, "VignetteOverlay", "Finishing/Finishing_VignetteOverlay.png", StretchAnchor,
            Vector2.zero, Vector2.zero);
        CreateImage(root, "ScorePanel", "Finishing/Finishing_ScorePanel.png", TopLeft,
            new Vector2(42, -40), new Vector2(510, 122));
        CreateText(root, "ScoreLabel", "SCORE", TopLeft, new Vector2(80, -54), new Vector2(140, 34), 20,
            Cyan, FontStyles.Bold, TextAlignmentOptions.Left);
        CreateText(root, "ScoreValue", "000000", TopLeft, new Vector2(80, -84), new Vector2(390, 58), 38,
            White, FontStyles.Bold, TextAlignmentOptions.Left);
        CreateImage(root, "TimerPanel", "Finishing/Finishing_TimerPanel.png", TopRight,
            new Vector2(-42, -40), new Vector2(470, 142));
        CreateText(root, "TimerValue", "00:00", TopRight, new Vector2(-90, -66), new Vector2(300, 66), 44,
            Warning, FontStyles.Bold);
        CreateImage(root, "TimeUpFrame", "Finishing/Finishing_TimeUpFrame.png", Center,
            new Vector2(0, 55), new Vector2(700, 220));
        CreateText(root, "TimeUp", "TIME UP", Center, new Vector2(0, 62), new Vector2(560, 105), 74,
            White, FontStyles.Bold);
        CreateImage(root, "SlowMotionBadge", "Finishing/Finishing_SlowMotionBadge.png", Center,
            new Vector2(0, -105), new Vector2(390, 120));
        CreateText(root, "SlowMotion", "SLOW MOTION  x0.5", Center, new Vector2(0, -101), new Vector2(320, 55), 25,
            Cyan, FontStyles.Bold);
    }

    private static void BuildResult(Transform root)
    {
        CreateOverlay(root, 0.72f);
        CreateImage(root, "TitleFrame", "Result/Result_TitleFrame.png", TopCenter,
            new Vector2(0, -55), new Vector2(550, 130));
        CreateText(root, "Title", "RESULT", TopCenter, new Vector2(0, -76), new Vector2(430, 76), 52,
            White, FontStyles.Bold);

        CreateImage(root, "FinalScorePanel", "Result/Result_FinalScorePanel.png", TopCenter,
            new Vector2(0, -205), new Vector2(650, 170));
        CreateText(root, "FinalScoreLabel", "FINAL SCORE", TopCenter, new Vector2(0, -218), new Vector2(420, 38), 23,
            Cyan, FontStyles.Bold);
        CreateText(root, "FinalScoreValue", "000000", TopCenter, new Vector2(0, -264), new Vector2(520, 74), 52,
            White, FontStyles.Bold);
        CreateImage(root, "Separator", "Result/Result_SeparatorLine.png", Center,
            new Vector2(0, 75), new Vector2(920, 38));

        CreateStatTile(root, "Kills", "KILLS", "000", new Vector2(-330, -55));
        CreateStatTile(root, "MaxCombo", "MAX COMBO", "00", new Vector2(-110, -55));
        CreateStatTile(root, "HitsTaken", "HITS TAKEN", "00", new Vector2(110, -55));
        CreateStatTile(root, "SpecialUses", "SPECIAL USE", "00", new Vector2(330, -55));

        CreateButton(root, "RetryButton", "Result/Result_RetryButton.png", "RETRY", BottomCenter,
            new Vector2(0, 72), new Vector2(400, 112), 34, Cyan);
    }

    private static void CreateStatTile(Transform root, string name, string label, string value, Vector2 position)
    {
        Image tile = CreateImage(root, name + "Tile", "Result/Result_StatTile.png", Center,
            position, new Vector2(196, 188));
        CreateText(tile.transform, name + "Label", label, TopCenter, new Vector2(0, -34), new Vector2(170, 42), 18,
            Muted, FontStyles.Bold);
        CreateText(tile.transform, name + "Value", value, Center, new Vector2(0, -18), new Vector2(150, 72), 42,
            White, FontStyles.Bold);
    }

    private static void UpdateMainSceneInstances()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainScenePath)
        {
            Debug.LogWarning("[GameDemo UI] Prefabs built. Main scene was not active, so scene instances were not synchronized.");
            return;
        }

        GameObject canvasObject = GameObject.Find("UICanvas");
        if (canvasObject == null)
        {
            Debug.LogWarning("[GameDemo UI] Prefabs built, but UICanvas was not found in the active Main scene.");
            return;
        }

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        string[] ordered =
        {
            "Start", "Dialogue", "Countdown", "Battle", "Pause",
            "RetryConfirmation", "Finishing", "Result"
        };

        for (int i = 0; i < ordered.Length; i++)
        {
            Transform child = canvasObject.transform.Find(ordered[i]);
            if (child == null)
                continue;

            RectTransform rect = child as RectTransform;
            Stretch(rect);
            child.SetSiblingIndex(i);
            child.gameObject.SetActive(ordered[i] == "Start");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static Image CreateOverlay(Transform parent, float alpha)
    {
        return CreateSolidImage(parent, "DimmedOverlay", new Color(0f, 0.01f, 0.02f, alpha));
    }

    private static Image CreateSolidImage(Transform parent, string name, Color color)
    {
        GameObject gameObject = CreateUiObject(name, parent, typeof(Image));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        Stretch(rect);
        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image CreateImage(Transform parent, string name, string relativeSpritePath,
        Anchor anchor, Vector2 position, Vector2 size, bool preserveAspect = false)
    {
        GameObject gameObject = CreateUiObject(name, parent, typeof(Image));
        SetRect(gameObject.GetComponent<RectTransform>(), anchor, position, size);
        Image image = gameObject.GetComponent<Image>();
        image.sprite = LoadSprite(relativeSpritePath);
        image.preserveAspect = preserveAspect;
        image.raycastTarget = false;
        return image;
    }

    private static Button CreateButton(Transform parent, string name, string relativeSpritePath, string label,
        Anchor anchor, Vector2 position, Vector2 size, float fontSize, Color labelColor)
    {
        GameObject gameObject = CreateUiObject(name, parent, typeof(Image), typeof(Button));
        SetRect(gameObject.GetComponent<RectTransform>(), anchor, position, size);
        Image image = gameObject.GetComponent<Image>();
        image.sprite = LoadSprite(relativeSpritePath);
        image.raycastTarget = true;

        Button button = gameObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.75f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.45f, 0.85f, 0.92f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        TMP_Text text = CreateText(gameObject.transform, "Label", label, StretchAnchor, Vector2.zero, Vector2.zero,
            fontSize, labelColor, FontStyles.Bold);
        text.raycastTarget = false;
        return button;
    }

    private static TMP_Text CreateText(Transform parent, string name, string content, Anchor anchor,
        Vector2 position, Vector2 size, float fontSize, Color color, FontStyles style,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        GameObject gameObject = CreateUiObject(name, parent, typeof(TextMeshProUGUI));
        SetRect(gameObject.GetComponent<RectTransform>(), anchor, position, size);
        TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent, params Type[] components)
    {
        var allComponents = new List<Type> { typeof(RectTransform) };
        allComponents.AddRange(components);
        GameObject gameObject = new(name, allComponents.ToArray());
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static Sprite LoadSprite(string relativePath)
    {
        string path = SpriteRoot + relativePath;
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            throw new InvalidOperationException($"UI sprite not found or not imported as Sprite: {path}");
        return sprite;
    }

    private static void ClearChildren(Transform root)
    {
        while (root.childCount > 0)
            UnityEngine.Object.DestroyImmediate(root.GetChild(0).gameObject);
    }

    private static void Stretch(RectTransform rect)
    {
        if (rect == null)
            return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void SetRect(RectTransform rect, Anchor anchor, Vector2 position, Vector2 size)
    {
        if (anchor.Stretch)
        {
            Stretch(rect);
            return;
        }

        rect.anchorMin = anchor.Value;
        rect.anchorMax = anchor.Value;
        rect.pivot = anchor.Pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private readonly struct Anchor
    {
        public Anchor(Vector2 value, Vector2 pivot, bool stretch = false)
        {
            Value = value;
            Pivot = pivot;
            Stretch = stretch;
        }

        public Vector2 Value { get; }
        public Vector2 Pivot { get; }
        public bool Stretch { get; }
    }

    private static readonly Anchor StretchAnchor = new(Vector2.zero, new Vector2(0.5f, 0.5f), true);
    private static readonly Anchor Center = new(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    private static readonly Anchor TopCenter = new(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
    private static readonly Anchor BottomCenter = new(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
    private static readonly Anchor TopLeft = new(new Vector2(0f, 1f), new Vector2(0f, 1f));
    private static readonly Anchor TopRight = new(new Vector2(1f, 1f), new Vector2(1f, 1f));
    private static readonly Anchor BottomLeft = new(new Vector2(0f, 0f), new Vector2(0f, 0f));
    private static readonly Anchor BottomRight = new(new Vector2(1f, 0f), new Vector2(1f, 0f));
}
