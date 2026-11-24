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
    // 줄이 다 보였을 때만 깜빡이는 넘김 표시(unscaled time).
    [SerializeField] private Graphic nextIndicator;
    [SerializeField, Min(0.1f)] private float nextIndicatorBlinkHz = 1.2f;
    // 화자 강조: 말하는 쪽은 원래 밝기·크기, 듣는 쪽은 어둡고 조금 작고 낮게. 전환은 unscaled time으로 부드럽게 한다.
    [SerializeField, Min(0.01f)] private float speakerTransitionSeconds = 0.15f;
    [SerializeField] private Color listenerTint = new Color(0.42f, 0.47f, 0.52f, 1f);
    [SerializeField, Range(0.8f, 1f)] private float listenerScale = 0.96f;
    [SerializeField] private float listenerOffsetY = -12f;
    // 타이핑 속도(초당 글자 수). 임시값이며 플레이 테스트로 조정한다. 0이면 줄 전체를 바로 보여 준다.
    [SerializeField, Min(0f)] private float charactersPerSecond = 40f;
    // 대화가 열린 직후 이 시간 동안은 진행 입력을 무시한다. 전투 끝까지 누르던 공격 클릭이나 키가
    // 첫 대사를 바로 넘기지 않게 하기 위한 임시값이다. Skip 버튼에는 적용하지 않는다.
    [SerializeField, Min(0f)] private float inputGraceSeconds = 0.35f;
    private TMP_FontAsset dialogueFont;
    private bool _fontAcquired;
    private float _leftEmphasis = 1f, _rightEmphasis, _leftTarget = 1f, _rightTarget;
    private bool _snapEmphasis = true;
    private bool _portraitBasesCaptured;
    private Vector2 _leftBase, _rightBase;
    private bool _nextShown;
    private float _blinkTime;

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
        if (nextIndicator == null) nextIndicator = FindNamed<Graphic>("NextIndicator");
        _nextShown = nextIndicator != null && nextIndicator.enabled;
        // 강조 연출이 위치를 옮기므로 처음 한 번만 기준 위치를 기억한다.
        if (!_portraitBasesCaptured && (leftPortrait != null || rightPortrait != null))
        {
            if (leftPortrait != null) _leftBase = leftPortrait.rectTransform.anchoredPosition;
            if (rightPortrait != null) _rightBase = rightPortrait.rectTransform.anchoredPosition;
            _portraitBasesCaptured = true;
        }
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
                TurnNavigationOff(continueButton);
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
        TurnNavigationOff(advanceArea);
    }

    private void OnDestroy()
    {
        // 폰트는 전투 중 무전 자막과 함께 쓴다. 마지막으로 돌려주는 쪽이 지운다(UiDialogueFont).
        if (_fontAcquired) UiDialogueFont.Release();
        _fontAcquired = false;
        dialogueFont = null;
    }

    private void ApplyDialogueFont()
    {
        if (dialogueFont == null)
        {
            // UI 테마의 한글 서체(IBM Plex Sans KR)를 우선 쓰고, 없으면 기존 나눔고딕으로 대체한다.
            dialogueFont = UiDialogueFont.Acquire();
            _fontAcquired = dialogueFont != null;
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
        SetSpeaker(model.IsLeftSpeaker);
    }

    // 대화 묶음이 새로 시작될 때(도입·마무리 진입, Retry 후) 첫 화자 강조는 전환 없이 바로 적용한다.
    public void BeginSequence() => _snapEmphasis = true;

    // 화자 강조 전환을 unscaled time으로 진행한다. DialoguePresenter.Update가 대화 상태에서 매 프레임 부른다.
    public void Animate(float unscaledDeltaTime)
    {
        if (_leftEmphasis == _leftTarget && _rightEmphasis == _rightTarget) return;
        float step = unscaledDeltaTime / Mathf.Max(0.01f, speakerTransitionSeconds);
        _leftEmphasis = Mathf.MoveTowards(_leftEmphasis, _leftTarget, step);
        _rightEmphasis = Mathf.MoveTowards(_rightEmphasis, _rightTarget, step);
        ApplyEmphasis();
    }

    // 타이핑: 문자열은 그대로 두고 TMP가 그리는 글자 수만 바꾼다.
    public void SetVisibleCharacters(int count)
    {
        if (bodyText != null) bodyText.maxVisibleCharacters = count;
    }

    // 줄이 다 보였을 때만 넘김 표시를 켜고 unscaled time으로 깜빡인다. 켜고 끌 때는 Graphic만 끄므로 레이아웃을 다시 계산하지 않는다.
    public void UpdateNextIndicator(bool visible, float unscaledDeltaTime)
    {
        if (nextIndicator == null) return;
        if (!visible)
        {
            if (!_nextShown) return;
            _nextShown = false;
            nextIndicator.enabled = false;
            return;
        }
        if (!_nextShown)
        {
            _nextShown = true;
            _blinkTime = 0f;
            nextIndicator.enabled = true;
        }
        _blinkTime += unscaledDeltaTime;
        float wave = 0.5f + 0.5f * Mathf.Cos(_blinkTime * nextIndicatorBlinkHz * 2f * Mathf.PI);
        nextIndicator.canvasRenderer.SetAlpha(Mathf.Lerp(0.3f, 1f, wave));
    }

    public override void ResetView()
    {
        SetText(speakerName, string.Empty);
        SetText(bodyText, string.Empty);
        SetVisibleCharacters(int.MaxValue);
        UpdateNextIndicator(false, 0f);
        _snapEmphasis = true;
    }

    private void SetSpeaker(bool leftIsSpeaking)
    {
        _leftTarget = leftIsSpeaking ? 1f : 0f;
        _rightTarget = leftIsSpeaking ? 0f : 1f;
        if (!_snapEmphasis) return;
        _snapEmphasis = false;
        _leftEmphasis = _leftTarget;
        _rightEmphasis = _rightTarget;
        ApplyEmphasis();
    }

    private void ApplyEmphasis()
    {
        ApplyPortrait(leftPortrait, _leftEmphasis, _leftBase);
        ApplyPortrait(rightPortrait, _rightEmphasis, _rightBase);
    }

    // emphasis 1 = 말하는 쪽(원래 밝기·크기·위치), 0 = 듣는 쪽(어둡게, 작게, 낮게).
    private void ApplyPortrait(Image portrait, float emphasis, Vector2 basePosition)
    {
        if (portrait == null) return;
        portrait.color = Color.Lerp(listenerTint, Color.white, emphasis);
        float scale = Mathf.Lerp(listenerScale, 1f, emphasis);
        RectTransform rect = portrait.rectTransform;
        rect.localScale = new Vector3(scale, scale, 1f);
        rect.anchoredPosition = basePosition + new Vector2(0f, Mathf.Lerp(listenerOffsetY, 0f, emphasis));
    }

    private void HandleSkipClicked() => SkipClicked?.Invoke();
    private void HandleContinueClicked() => ContinueClicked?.Invoke();
}
