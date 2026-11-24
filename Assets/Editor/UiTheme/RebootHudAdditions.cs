using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 전투 HUD와 화면 프리팹에 더한 요소: 콤보 표시, 전투 중 무전 자막, 게이지 옆 키 표시(E·Q), 결과 화면의 최고 점수,
// 시작 화면의 두 줄 조작 안내, 일시정지 메뉴 양옆의 설정 칸(감도·음량)과 조작 안내 칸, 대사 상자 뒤의 어두운 채움. 프리팹 전체를 다시 만들지 않고 이 요소들만 더하거나 다시 맞춘다
// (전투 HUD의 피격 글리치·흐림 값처럼 생성 뒤에 조절한 값은 그대로 둔다).
// - Ensure* 메서드는 여러 번 실행해도 같다. 같은 이름의 요소가 있으면 찾아서 배치와 모양만 다시 맞춘다.
// - GameDemoUiPrefabBuilder가 같은 메서드를 불러, 전체 다시 만들기에서도 같은 요소가 생긴다.
// - 글꼴과 글자 재질은 같은 프리팹의 기존 글자에서 가져온다(테마 적용 전이면 기본 글꼴, 후면 Chakra Petch).
// - 색은 팔레트(UiPalette)를 쓰고, 각 표시 컴포넌트가 실행 중에도 팔레트로 다시 칠한다. 색과 크기는 모두 임시값이다.
// 실행: -executeMethod RebootHudAdditions.ApplyBatch
public static class RebootHudAdditions
{
    private const string PrefabRoot = "Assets/Prefabs/UI/";
    private const string DialogueDataPath = "Assets/Resources/DialogueSequence.asset";
    private const string RadioPanelSpritePath = "Assets/Sprites/UI_Reboot/Dialogue_TextBox.png";
    private const string LeftPortraitPath = "Assets/Sprites/UI/Dialogue/Dialogue_LeftCharacterPortrait.png";
    private const string RightPortraitPath = "Assets/Sprites/UI/Dialogue/Dialogue_RightCharacterPortrait.png";

    // 실제 입력 설정(InputSystem_Actions, PlayerRuntime, PlayerSkill, UiLayer)과 같은 키다. 키를 바꾸면 이 문구도 바꾼다.
    // 이동 WASD, 카메라 마우스, 공격 왼쪽 버튼(Attack1 → Attack2 → 세 번째 입력을 누르고 있으면 차지), 회피 Left Shift(Sprint),
    // 스킬 돌진 Q(Skill1), 특수 공격 E(Interact, 게이지 100%일 때), 일시정지 ESC.
    public const string ControlHintText =
        "WASD  MOVE     MOUSE  CAMERA     LMB  ATTACK     SHIFT  DODGE\n" +
        "HOLD 3RD LMB  CHARGE     Q  SKILL DASH     E  SPECIAL AT 100%     ESC  PAUSE";

    private static readonly Color PanelFill = new(0.039f, 0.047f, 0.063f, 0.66f);

    // ------------------------------------------------------------------ batch

    public static void ApplyBatch()
    {
        try
        {
            Apply();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static void Apply()
    {
        ApplyToPrefab("Battle", root => EnsureBattle(root.transform));
        ApplyToPrefab("Finishing", root => EnsureFinishing(root.transform));
        ApplyToPrefab("Start", root => EnsureStart(root.transform));
        ApplyToPrefab("Result", root => EnsureResult(root.transform));
        ApplyToPrefab("Pause", root => EnsurePause(root.transform));
        ApplyToPrefab("Dialogue", root => EnsureDialogue(root.transform));
        EnsureDialogueData();
        AssetDatabase.SaveAssets();
        Debug.Log("[RebootHUD] Combo, radio, key badges, control hint, best score, pause options and dialogue backing applied.");
    }

    private static void ApplyToPrefab(string prefabName, Action<GameObject> ensure)
    {
        string path = PrefabRoot + prefabName + ".prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ensure(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------ battle

    public static void EnsureBattle(Transform root)
    {
        TMP_Text style = FindText(root, "ScoreValue");
        HudComboCounter combo = EnsureComboCounter(root, style);
        HudRadioSubtitle radio = EnsureRadioSubtitle(root, style);
        // 원형 게이지(BattleView가 실행 중에 화면 아래 가운데, 아래에서 35px 띄운 170px로 만든다)의 양옆.
        HudKeyBadge skill = EnsureKeyBadge(root, "SkillKeyBadge", "Q", "SKILL", new Vector2(-150f, 120f), style, true);
        HudKeyBadge special = EnsureKeyBadge(root, "SpecialKeyBadge", "E", "SPECIAL", new Vector2(150f, 120f), style, false);

        var view = root.GetComponent<BattleView>();
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("comboCounter").objectReferenceValue = combo;
        so.FindProperty("radioSubtitle").objectReferenceValue = radio;
        so.FindProperty("specialKeyBadge").objectReferenceValue = special;
        so.FindProperty("skillKeyBadge").objectReferenceValue = skill;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void EnsureFinishing(Transform root)
    {
        HudComboCounter combo = EnsureComboCounter(root, FindText(root, "ScoreValue"));
        var view = root.GetComponent<FinishingView>();
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("comboCounter").objectReferenceValue = combo;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 콤보: 화면 오른쪽 가운데보다 조금 위. 오른쪽 위 시간 패널(아래 끝 182px)과 아래 가운데 게이지와 겹치지 않는다.
    private static HudComboCounter EnsureComboCounter(Transform root, TMP_Text style)
    {
        RectTransform rect = EnsureRect(root, "ComboCounter", typeof(CanvasGroup), typeof(HudComboCounter));
        Place(rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-56f, 150f), new Vector2(380f, 190f));
        PassThrough(rect.GetComponent<CanvasGroup>());

        TMP_Text count = EnsureText(rect, "ComboCount", style, "12", 100f, UiPalette.Ink, TextAlignmentOptions.MidlineRight);
        Place(count.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, -58f), new Vector2(380f, 116f));
        // 라벨은 콤보 점수 보너스가 켜져 있으면 "x1.25  COMBO"처럼 배율도 보여 준다(HudComboCounter).
        TMP_Text label = EnsureText(rect, "ComboLabel", style, "COMBO", 28f, UiPalette.Accent, TextAlignmentOptions.MidlineRight);
        Place(label.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -114f), new Vector2(360f, 36f));
        label.characterSpacing = 8f;

        Image track = EnsureImage(rect, "ComboTimeTrack", null, UiPalette.WithAlpha(UiPalette.Muted, 0.35f));
        Place(track.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, -164f), new Vector2(300f, 6f));
        Image fill = EnsureImage(rect, "ComboTimeFill", null, UiPalette.Accent);
        Place(fill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(0f, -164f), new Vector2(300f, 6f));

        var combo = rect.GetComponent<HudComboCounter>();
        combo.AutoBind();
        rect.gameObject.SetActive(false);
        return combo;
    }

    // 무전 자막: 왼쪽 위 점수 패널 아래. 오른쪽 위의 10초 경고 타이머, 오른쪽 콤보, 아래 가운데 게이지·키 표시와
    // 화면 비율이 달라도 겹치지 않는다. 클릭을 받지 않는다.
    private static HudRadioSubtitle EnsureRadioSubtitle(Transform root, TMP_Text style)
    {
        RectTransform rect = EnsureRect(root, "RadioSubtitle", typeof(CanvasGroup), typeof(HudRadioSubtitle));
        Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -196f), new Vector2(760f, 124f));
        PassThrough(rect.GetComponent<CanvasGroup>());

        Image panel = EnsureImage(rect, "RadioPanel", LoadSprite(RadioPanelSpritePath), Color.white);
        Stretch(panel.rectTransform);

        // 초상화는 대화 화면의 원본 그림에서 얼굴 부분만 잘라 보여 준다(RectMask2D).
        RectTransform mask = EnsureRect(rect, "RadioPortraitMask", typeof(RectMask2D));
        Place(mask, Vector2.zero, Vector2.zero, new Vector2(22f, 16f), new Vector2(92f, 92f));
        Image maskBack = EnsureImage(mask, "RadioPortraitBack", null, PanelFill);
        Stretch(maskBack.rectTransform);
        Image left = EnsureImage(mask, "RadioPortraitLeft", LoadSprite(LeftPortraitPath), Color.white);
        // 노아(1024x1536): 얼굴 가운데가 그림의 (530, 440) 근처다. 0.28배로 줄여 얼굴이 가운데에 오게 둔다.
        Place(left.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-5f, -91.8f), new Vector2(286.7f, 430.1f));
        Image right = EnsureImage(mask, "RadioPortraitRight", LoadSprite(RightPortraitPath), Color.white);
        // K-7(1024x1536): 얼굴 가운데가 그림의 (420, 430) 근처다.
        Place(right.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(25.8f, -94.6f), new Vector2(286.7f, 430.1f));
        right.gameObject.SetActive(false);
        Image edge = EnsureImage(rect, "RadioPortraitEdge", null, UiPalette.Accent);
        Place(edge.rectTransform, Vector2.zero, Vector2.zero, new Vector2(22f, 16f), new Vector2(4f, 92f));

        TMP_Text speaker = EnsureText(rect, "RadioSpeaker", style, string.Empty, 26f, UiPalette.Accent, TextAlignmentOptions.BottomLeft);
        Place(speaker.rectTransform, Vector2.zero, Vector2.zero, new Vector2(134f, 72f), new Vector2(300f, 36f));
        TMP_Text channel = EnsureText(rect, "RadioChannel", style, "COMM LINK", 18f, UiPalette.Muted, TextAlignmentOptions.BottomRight);
        Place(channel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 76f), new Vector2(240f, 28f));
        channel.characterSpacing = 6f;
        TMP_Text body = EnsureText(rect, "RadioText", style, string.Empty, 30f, UiPalette.Ink, TextAlignmentOptions.TopLeft);
        Place(body.rectTransform, Vector2.zero, Vector2.zero, new Vector2(134f, 14f), new Vector2(600f, 56f));
        body.fontStyle = FontStyles.Normal;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.enableAutoSizing = true;
        body.fontSizeMin = 22f;
        body.fontSizeMax = 30f;

        var radio = rect.GetComponent<HudRadioSubtitle>();
        radio.AutoBind();
        rect.gameObject.SetActive(false);
        return radio;
    }

    private static HudKeyBadge EnsureKeyBadge(Transform root, string name, string key, string label, Vector2 position,
        TMP_Text style, bool withCooldown)
    {
        RectTransform rect = EnsureRect(root, name, typeof(CanvasGroup), typeof(HudKeyBadge));
        Place(rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), position, new Vector2(60f, 60f));
        PassThrough(rect.GetComponent<CanvasGroup>());

        Image frame = EnsureImage(rect, "KeyFrame", null, PanelFill);
        Stretch(frame.rectTransform);
        // 테마 패널과 같은 구성: 왼쪽 굵은 선과 아래 가는 선.
        Image edge = EnsureImage(rect, "KeyEdge", null, UiPalette.Muted);
        Place(edge.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(4f, 60f));
        Image baseLine = EnsureImage(rect, "KeyBaseLine", null, UiPalette.WithAlpha(UiPalette.Muted, 0.55f));
        Place(baseLine.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(60f, 2f));
        if (withCooldown)
        {
            // 재사용 대기용 막(지금은 쓰이지 않는다). 아래에서부터 남은 대기 비율만큼 덮는다.
            Image cooldown = EnsureImage(rect, "CooldownFill", null, new Color(0f, 0f, 0f, 0.6f));
            Place(cooldown.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(60f, 60f));
            cooldown.rectTransform.localScale = new Vector3(1f, 0f, 1f);
            cooldown.gameObject.SetActive(false);
        }
        TMP_Text keyText = EnsureText(rect, "Key", style, key, 32f, UiPalette.Ink, TextAlignmentOptions.Center);
        Stretch(keyText.rectTransform);
        TMP_Text labelText = EnsureText(rect, "KeyLabel", style, label, 20f, UiPalette.Muted, TextAlignmentOptions.Top);
        Place(labelText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(116f, 26f));
        labelText.characterSpacing = 3f;

        var badge = rect.GetComponent<HudKeyBadge>();
        badge.AutoBind();
        return badge;
    }

    // ------------------------------------------------------------------ start

    // 시작 화면의 조작 안내를 실제 키 전부로 채운다. 두 줄이라 안내 칸을 넓히고 높인다(1280x720에서도 읽히게).
    public static void EnsureStart(Transform root)
    {
        RectTransform panel = root.Find("ControlHintPanel") as RectTransform;
        if (panel != null)
            Place(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1180f, 150f));
        TMP_Text hint = FindText(root, "ControlHint");
        if (hint == null) return;
        hint.text = ControlHintText;
        Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(1100f, 96f));
        hint.alignment = TextAlignmentOptions.Center;
        StyleControlHint(hint);
        EnsureStartBestScore(root);
    }

    // 제목 칸(아래 끝 y=60)과 START 버튼(위 끝 y=-43) 사이에 최고 점수 한 줄. 기록이 없으면 StartView가 숨긴다.
    // 결과 화면의 BEST 줄과 같은 배치다(라벨은 가운데의 왼쪽, 숫자는 오른쪽).
    private static void EnsureStartBestScore(Transform root)
    {
        TMP_Text style = FindText(root, "Subtitle");
        if (style == null) style = FindText(root, "ControlHint");
        RectTransform row = EnsureRect(root, "BestScoreRow");
        Place(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(520f, 36f));
        TMP_Text label = EnsureText(row, "BestScoreLabel", style, "BEST", 22f, UiPalette.Muted, TextAlignmentOptions.MidlineRight);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-38f, 0f), new Vector2(160f, 36f));
        label.characterSpacing = 6f;
        TMP_Text value = EnsureText(row, "BestScoreValue", style, "000000", 32f, UiPalette.Ink, TextAlignmentOptions.MidlineLeft);
        Place(value.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-22f, 0f), new Vector2(220f, 36f));
        row.gameObject.SetActive(false);

        var view = root.GetComponent<StartView>();
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("bestScoreRow").objectReferenceValue = row.gameObject;
        so.FindProperty("bestScoreValue").objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 조작 안내의 글자 크기 규칙. 테마 빌더(RebootUiThemeBuilder)도 같은 값을 쓴다.
    public static void StyleControlHint(TMP_Text hint)
    {
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 18f;
        hint.fontSizeMax = 26f;
        hint.lineSpacing = 14f;
    }

    // ------------------------------------------------------------------ pause

    // 일시정지 메뉴의 조작 안내. 시작 화면의 안내(ControlHintText)와 같은 키다. 키를 바꾸면 둘 다 바꾼다.
    public const string PauseControlKeys = "WASD\nMOUSE\nLMB\nHOLD 3RD LMB\nSHIFT\nQ\nE\nESC";
    public const string PauseControlActions = "MOVE\nCAMERA\nATTACK\nCHARGE\nDODGE\nSKILL DASH\nSPECIAL AT 100%\nRESUME";

    // 일시정지 메뉴 양옆의 두 칸. 가운데 메뉴(MenuPanel 520px)와 겹치지 않게 ±500px에 두고, 위쪽 끝을 RESUME 버튼에 맞춘다.
    // - 왼쪽 OptionsPanel: 플레이어 설정 두 줄(마우스 감도, 음량). 한 번 누르면 한 단계(GameOptions). PauseView가 버튼과
    //   값 글자를 이름으로도 찾지만 여기서 직접 연결해 둔다.
    // - 오른쪽 ControlsPanel: 조작 안내.
    // 재시작 확인창이 뜨면 PauseView가 DimmedOverlay를 뺀 나머지를 숨기므로 두 칸도 같이 숨는다.
    public static void EnsurePause(Transform root)
    {
        TMP_Text style = FindText(root, "Header");
        if (style == null) style = FindText(root, "Label");

        RectTransform options = EnsureRect(root, "OptionsPanel");
        Place(options, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(-500f, 157f), new Vector2(400f, 196f));
        EnsurePanelDressing(options, style, "OPTIONS");
        TMP_Text lookValue = EnsureOptionRow(options, style, "Look", "SENSITIVITY", -92f, out Button lookDown, out Button lookUp);
        TMP_Text volumeValue = EnsureOptionRow(options, style, "Volume", "VOLUME", -150f, out Button volumeDown, out Button volumeUp);

        RectTransform controls = EnsureRect(root, "ControlsPanel");
        Place(controls, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(500f, 157f), new Vector2(400f, 330f));
        EnsurePanelDressing(controls, style, "CONTROLS");
        TMP_Text keys = EnsureText(controls, "ControlKeys", style, PauseControlKeys, 19f, UiPalette.Ink, TextAlignmentOptions.TopLeft);
        Place(keys.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -58f), new Vector2(160f, 256f));
        TMP_Text actions = EnsureText(controls, "ControlActions", style, PauseControlActions, 19f, UiPalette.Muted, TextAlignmentOptions.TopLeft);
        Place(actions.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(196f, -58f), new Vector2(190f, 256f));
        keys.lineSpacing = actions.lineSpacing = 22f;
        keys.characterSpacing = actions.characterSpacing = 2f;

        var view = root.GetComponent<PauseView>();
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("lookDownButton").objectReferenceValue = lookDown;
        so.FindProperty("lookUpButton").objectReferenceValue = lookUp;
        so.FindProperty("volumeDownButton").objectReferenceValue = volumeDown;
        so.FindProperty("volumeUpButton").objectReferenceValue = volumeUp;
        so.FindProperty("lookValue").objectReferenceValue = lookValue;
        so.FindProperty("volumeValue").objectReferenceValue = volumeValue;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 옆 칸의 바탕(전투 HUD 무전 자막과 같은 어두운 채움), 왼쪽 강조선, 제목.
    private static void EnsurePanelDressing(RectTransform panel, TMP_Text style, string title)
    {
        Image fill = EnsureImage(panel, "PanelFill", null, PanelFill);
        Stretch(fill.rectTransform);
        Image edge = EnsureImage(panel, "PanelEdge", null, UiPalette.Accent);
        edge.rectTransform.anchorMin = new Vector2(0f, 0f);
        edge.rectTransform.anchorMax = new Vector2(0f, 1f);
        edge.rectTransform.pivot = new Vector2(0f, 0.5f);
        edge.rectTransform.anchoredPosition = Vector2.zero;
        edge.rectTransform.sizeDelta = new Vector2(3f, 0f);
        TMP_Text text = EnsureText(panel, "PanelTitle", style, title, 20f, UiPalette.Muted, TextAlignmentOptions.TopLeft);
        Place(text.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -16f), new Vector2(300f, 28f));
        text.characterSpacing = 8f;
    }

    // 설정 한 줄: 이름, 내림 버튼, 값, 올림 버튼. y는 칸의 위쪽 끝에서 줄 가운데까지.
    private static TMP_Text EnsureOptionRow(RectTransform panel, TMP_Text style, string id, string label, float y,
        out Button down, out Button up)
    {
        TMP_Text name = EnsureText(panel, id + "Label", style, label, 20f, UiPalette.Ink, TextAlignmentOptions.MidlineLeft);
        Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(26f, y), new Vector2(170f, 44f));
        name.characterSpacing = 3f;
        down = EnsureStepButton(panel, id + "DownButton", "-", style, new Vector2(222f, y));
        TMP_Text value = EnsureText(panel, id + "Value", style, "100%", 24f, UiPalette.Accent, TextAlignmentOptions.Center);
        Place(value.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(288f, y), new Vector2(84f, 44f));
        up = EnsureStepButton(panel, id + "UpButton", "+", style, new Vector2(354f, y));
        return value;
    }

    // 한 단계 버튼. 바탕 색 위에 상태 색(보통, 가리킴, 누름, 꺼짐)을 곱한다. 키보드 이동은 쓰지 않는다(마우스로 누른다).
    private static Button EnsureStepButton(RectTransform panel, string name, string glyph, TMP_Text style, Vector2 position)
    {
        RectTransform rect = EnsureRect(panel, name, typeof(Image), typeof(Button));
        Place(rect, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), position, new Vector2(46f, 46f));
        var image = rect.GetComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = UiPalette.WithAlpha(UiPalette.Accent, 0.2f);
        image.raycastTarget = true;
        var button = rect.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        colors.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        colors.pressedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.15f);
        colors.colorMultiplier = 2f;
        colors.fadeDuration = 0.06f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        TMP_Text text = EnsureText(rect, "Glyph", style, glyph, 32f, UiPalette.Ink, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    // ------------------------------------------------------------------ dialogue

    // 대사 상자 뒤의 어두운 채움. 상자 그림의 채움만으로는 밝은 배경(로봇 무리, 길의 선) 앞에서 글자가 묻힌다
    // (선형 색 공간에서는 어두운 반투명이 눈에 보이는 것보다 덜 가린다). 상자 그림의 보이는 부분 안쪽에 맞춘다.
    public static void EnsureDialogue(Transform root)
    {
        Transform box = root.Find("DialogueBox");
        if (box == null) return;
        Image fill = EnsureImage(box, "BoxFill", null, new Color(PanelFill.r, PanelFill.g, PanelFill.b, 0.62f));
        RectTransform rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(8f, 14f);
        rect.offsetMax = new Vector2(-14f, -14f);
        rect.localScale = Vector3.one;
        rect.SetAsFirstSibling();
    }

    // ------------------------------------------------------------------ result

    // 최종 점수 아래의 최고 점수 줄과, 최종 점수 오른쪽의 NEW BEST 표시.
    public static void EnsureResult(Transform root)
    {
        TMP_Text style = FindText(root, "FinalScoreValue");
        RectTransform row = EnsureRect(root, "BestScoreRow");
        Place(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -362f), new Vector2(520f, 32f));  // in the gap under the final score: clear of the centre-anchored tiles even at 21:9 with a safe-area inset (UiResolutionSceneTests)
        TMP_Text label = EnsureText(row, "BestScoreLabel", style, "BEST", 22f, UiPalette.Muted, TextAlignmentOptions.MidlineRight);
        Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-38f, 0f), new Vector2(160f, 32f));
        label.characterSpacing = 6f;
        TMP_Text value = EnsureText(row, "BestScoreValue", style, "000000", 32f, UiPalette.Ink, TextAlignmentOptions.MidlineLeft);
        Place(value.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-22f, 0f), new Vector2(220f, 32f));

        // 최종 점수 숫자 오른쪽. 가운데를 기준으로 튀어나오므로 가장 커졌을 때도(ResultView의 newBestPopScale) 숫자에 닿지 않는다.
        Transform scorePanel = root.Find("FinalScorePanel");
        Transform markParent = scorePanel != null ? scorePanel : root;
        RectTransform mark = EnsureRect(markParent, "NewBestMark");
        Place(mark, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-104f, -10f), new Vector2(150f, 40f));
        Image markFill = EnsureImage(mark, "NewBestFill", null, UiPalette.WithAlpha(UiPalette.Accent, 0.16f));
        Stretch(markFill.rectTransform);
        Image markEdge = EnsureImage(mark, "NewBestEdge", null, UiPalette.Accent);
        Place(markEdge.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(4f, 40f));
        TMP_Text markText = EnsureText(mark, "NewBestText", style, "NEW BEST", 21f, UiPalette.Accent, TextAlignmentOptions.Center);
        Stretch(markText.rectTransform);
        markText.characterSpacing = 3f;
        mark.gameObject.SetActive(false);

        var view = root.GetComponent<ResultView>();
        if (view == null) return;
        var so = new SerializedObject(view);
        so.FindProperty("bestScoreLabel").objectReferenceValue = label;
        so.FindProperty("bestScoreValue").objectReferenceValue = value;
        so.FindProperty("newBestMark").objectReferenceValue = mark;
        // 1.25배를 넘으면 튀어나오는 순간 NEW BEST가 최종 점수 숫자에 닿는다.
        so.FindProperty("newBestPopScale").floatValue = 1.25f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ dialogue data

    // 대사 에셋(DialogueSequence.asset)의 문장은 에셋이 기준이다: 팀이 정한 도입·마무리 대사, 10초 무전, 생성 경고 세 줄,
    // 성과 반응 마무리 변형과 그 기준값. 이 빌더는 그 값들을 덮어쓰지 않는다. 전에는 여기서 무전 두 줄을 다시 쓰고 마무리
    // 변형을 비웠는데, 그대로 두면 Apply를 돌릴 때마다 정해 둔 대사가 지워진다. 무전 줄이 하나도 없는 새 에셋에만
    // 기본 한 줄(10초)을 넣는다.
    public static void EnsureDialogueData()
    {
        var data = AssetDatabase.LoadAssetAtPath<DialogueSequenceData>(DialogueDataPath);
        if (data == null) throw new InvalidOperationException("Missing " + DialogueDataPath);
        if (data.battleRadio != null && data.battleRadio.Length > 0)
            return;
        var radioLines = new[]
        {
            new BattleRadioLine { remainingSeconds = 10f, speaker = "노아", text = "10초 남았어. 버텨.", leftSpeaker = true }
        };
        var so = new SerializedObject(data);
        SerializedProperty radio = so.FindProperty("battleRadio");
        radio.arraySize = radioLines.Length;
        for (int i = 0; i < radioLines.Length; i++)
            SetRadioLine(radio.GetArrayElementAtIndex(i), radioLines[i]);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
    }

    private static void SetRadioLine(SerializedProperty line, BattleRadioLine value)
    {
        line.FindPropertyRelative("remainingSeconds").floatValue = value.remainingSeconds;
        line.FindPropertyRelative("speaker").stringValue = value.speaker;
        line.FindPropertyRelative("text").stringValue = value.text;
        line.FindPropertyRelative("leftSpeaker").boolValue = value.leftSpeaker;
    }

    // ------------------------------------------------------------------ helpers

    private static RectTransform EnsureRect(Transform parent, string name, params Type[] components)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            foreach (Type component in components)
                if (go.GetComponent(component) == null) go.AddComponent(component);
        }
        else
        {
            var types = new List<Type> { typeof(RectTransform) };
            types.AddRange(components);
            go = new GameObject(name, types.ToArray());
            go.transform.SetParent(parent, false);
        }
        go.layer = LayerMask.NameToLayer("UI");
        return (RectTransform)go.transform;
    }

    private static Image EnsureImage(Transform parent, string name, Sprite sprite, Color color)
    {
        RectTransform rect = EnsureRect(parent, name, typeof(Image));
        var image = rect.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text EnsureText(Transform parent, string name, TMP_Text style, string content, float size, Color color,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = EnsureRect(parent, name, typeof(TextMeshProUGUI));
        var text = rect.GetComponent<TextMeshProUGUI>();
        if (style != null)
        {
            text.font = style.font;
            text.fontSharedMaterial = style.fontSharedMaterial;
        }
        text.text = content;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.enableAutoSizing = false;
        text.raycastTarget = false;
        return text;
    }

    private static TMP_Text FindText(Transform root, string name)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            if (text.name == name) return text;
        return null;
    }

    private static Sprite LoadSprite(string path) =>
        AssetDatabase.LoadAssetAtPath<Sprite>(path) ?? throw new InvalidOperationException("Sprite missing: " + path);

    // HUD 요소는 입력을 받지 않는다. 아래에 있는 버튼이나 게임 입력을 막지 않게 한다.
    private static void PassThrough(CanvasGroup group)
    {
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;
        group.ignoreParentGroups = false;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
