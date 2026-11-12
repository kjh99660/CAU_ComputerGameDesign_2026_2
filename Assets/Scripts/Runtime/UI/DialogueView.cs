using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DialogueView : UIScreenView
{
    [SerializeField] private Button skipButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private TMP_Text speakerName;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Image leftPortrait;
    [SerializeField] private Image rightPortrait;
    [SerializeField, Range(0f, 1f)] private float inactiveSpeakerAlpha = 0.45f;
    private TMP_FontAsset dialogueFont;

    public event Action SkipClicked;
    public event Action ContinueClicked;

    public override void AutoBind()
    {
        base.AutoBind();
        if (skipButton == null) skipButton = FindNamed<Button>("SkipButton");
        if (continueButton == null) continueButton = FindNamed<Button>("ContinuePrompt");
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
        if (skipButton != null) skipButton.onClick.AddListener(HandleSkipClicked);
        if (continueButton != null) continueButton.onClick.AddListener(HandleContinueClicked);
    }

    private void OnDisable()
    {
        if (skipButton != null) skipButton.onClick.RemoveListener(HandleSkipClicked);
        if (continueButton != null) continueButton.onClick.RemoveListener(HandleContinueClicked);
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
        PlayDialogueTextAnimation();
    }

    public override void ResetView()
    {
        SetText(speakerName, string.Empty);
        SetText(bodyText, string.Empty);
        StopDialogueAnimations();
    }

    private void SetPortraitEmphasis(bool leftIsSpeaking)
    {
        if (leftPortrait != null) leftPortrait.color = WithAlpha(leftPortrait.color, leftIsSpeaking ? 1f : inactiveSpeakerAlpha);
        if (rightPortrait != null) rightPortrait.color = WithAlpha(rightPortrait.color, leftIsSpeaking ? inactiveSpeakerAlpha : 1f);
    }

    private void PlayDialogueTextAnimation()
    {
        //TOOD : unscaled time 기반 타이핑 및 화자 전환 연출을 연결한다.
    }

    private void StopDialogueAnimations()
    {
        //TOOD : 진행 중인 Tween, Coroutine과 지연 Callback을 취소한다.
    }

    private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }
    private void HandleSkipClicked() => SkipClicked?.Invoke();
    private void HandleContinueClicked() => ContinueClicked?.Invoke();
}
