using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DialogueView : UIScreenView
{
    [SerializeField] private Button skipButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button advanceArea;
    [SerializeField] private TMP_Text speakerName;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Image leftPortrait;
    [SerializeField] private Image rightPortrait;
    [SerializeField, Range(0f, 1f)] private float inactiveSpeakerAlpha = 0.45f;
    // 타이핑 속도(초당 글자 수). 임시값이며 플레이 테스트로 조정한다. 0이면 줄 전체를 바로 보여 준다.
    [SerializeField, Min(0f)] private float charactersPerSecond = 40f;
    // 대화가 열린 직후 이 시간 동안은 진행 입력을 무시한다. 전투 끝까지 누르던 공격 클릭이나 키가
    // 첫 대사를 바로 넘기지 않게 하기 위한 임시값이다. Skip 버튼에는 적용하지 않는다.
    [SerializeField, Min(0f)] private float inputGraceSeconds = 0.35f;
    private TMP_FontAsset dialogueFont;

    public event Action SkipClicked;
    public event Action ContinueClicked;

    public float CharactersPerSecond => charactersPerSecond;
    public float InputGraceSeconds => inputGraceSeconds;

    public override void AutoBind()
    {
        base.AutoBind();
        if (skipButton == null) skipButton = FindNamed<Button>("SkipButton");
        if (continueButton == null) continueButton = FindNamed<Button>("ContinuePrompt");
        if (advanceArea == null) advanceArea = FindNamed<Button>("AdvanceArea");
        if (speakerName == null) speakerName = FindNamed<TMP_Text>("SpeakerName");
        if (bodyText == null) bodyText = FindNamed<TMP_Text>("BodyText");
        if (leftPortrait == null) leftPortrait = FindNamed<Image>("LeftPortrait");
        if (rightPortrait == null) rightPortrait = FindNamed<Image>("RightPortrait");
    }

    private void OnEnable()
    {
        AutoBind();
        ApplyDialogueFont();
        if (continueButton == null)
        {
            var box = FindNamed<Image>("DialogueBox");
            if (box != null)
            {
                continueButton = box.GetComponent<Button>();
                if (continueButton == null) continueButton = box.gameObject.AddComponent<Button>();
                continueButton.transition = Selectable.Transition.None;
                box.raycastTarget = true;
            }
        }
        EnsureAdvanceArea();
        // 글자 위를 눌러도 아래의 대사 상자·전체 화면 영역이 클릭을 받도록 글자는 클릭을 가로채지 않는다.
        if (speakerName != null) speakerName.raycastTarget = false;
        if (bodyText != null) bodyText.raycastTarget = false;
        if (skipButton != null) skipButton.onClick.AddListener(HandleSkipClicked);
        if (continueButton != null) continueButton.onClick.AddListener(HandleContinueClicked);
        if (advanceArea != null) advanceArea.onClick.AddListener(HandleContinueClicked);
    }

    private void OnDisable()
    {
        if (skipButton != null) skipButton.onClick.RemoveListener(HandleSkipClicked);
        if (continueButton != null) continueButton.onClick.RemoveListener(HandleContinueClicked);
        if (advanceArea != null) advanceArea.onClick.RemoveListener(HandleContinueClicked);
    }

    // 화면 아무 곳을 눌러도 넘어가도록 다른 대화 요소 뒤에 투명한 전체 화면 버튼을 둔다.
    // Skip 버튼은 이 영역보다 위에 있어 따로 동작한다.
    private void EnsureAdvanceArea()
    {
        if (advanceArea != null) return;
        var area = new GameObject("AdvanceArea", typeof(RectTransform), typeof(Image), typeof(Button));
        area.layer = gameObject.layer;
        var rect = (RectTransform)area.transform;
        rect.SetParent(transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = area.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
        advanceArea = area.GetComponent<Button>();
        advanceArea.transition = Selectable.Transition.None;
    }

    private void OnDestroy()
    {
        if (dialogueFont == null) return;
        if (dialogueFont.material != null) Destroy(dialogueFont.material);
        foreach (var atlas in dialogueFont.atlasTextures)
            if (atlas != null) Destroy(atlas);
        Destroy(dialogueFont);
    }

    private void ApplyDialogueFont()
    {
        if (dialogueFont == null)
        {
            // UI 테마의 한글 서체(IBM Plex Sans KR)를 우선 쓰고, 없으면 기존 나눔고딕으로 대체한다.
            var source = Resources.Load<Font>("Fonts/IBMPlexSansKR-Regular");
            if (source == null) source = Resources.Load<Font>("Fonts/NanumGothic-Regular");
            if (source != null) dialogueFont = TMP_FontAsset.CreateFontAsset(source);
            if (dialogueFont == null)
            {
                Debug.LogError("대화용 한글 폰트를 불러오지 못했습니다.", this);
                return;
            }
        }

        if (speakerName != null) speakerName.font = dialogueFont;
        if (bodyText != null) bodyText.font = dialogueFont;
    }

    public void Render(DialogueUiModel model)
    {
        SetText(speakerName, model.Speaker);
        SetText(bodyText, model.Body);
        SetPortraitEmphasis(model.IsLeftSpeaker);
    }

    // 타이핑: 문자열은 그대로 두고 TMP가 그리는 글자 수만 바꾼다.
    public void SetVisibleCharacters(int count)
    {
        if (bodyText != null) bodyText.maxVisibleCharacters = count;
    }

    public override void ResetView()
    {
        SetText(speakerName, string.Empty);
        SetText(bodyText, string.Empty);
        SetVisibleCharacters(int.MaxValue);
    }

    private void SetPortraitEmphasis(bool leftIsSpeaking)
    {
        if (leftPortrait != null) leftPortrait.color = WithAlpha(leftPortrait.color, leftIsSpeaking ? 1f : inactiveSpeakerAlpha);
        if (rightPortrait != null) rightPortrait.color = WithAlpha(rightPortrait.color, leftIsSpeaking ? inactiveSpeakerAlpha : 1f);
    }

    private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
    private void HandleSkipClicked() => SkipClicked?.Invoke();
    private void HandleContinueClicked() => ContinueClicked?.Invoke();
}
