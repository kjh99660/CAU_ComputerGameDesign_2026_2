using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 중 무전 자막(05_STORY_AND_SCRIPT 4.6). 화자와 한 줄을 몇 초 동안 화면 왼쪽 위(점수 아래)에 띄운다.
// 클릭을 가로채지 않고(모든 그래픽의 raycastTarget이 꺼져 있다), 연출은 BattleView.UpdateHud가 unscaled time으로
// 진행하므로 Pause 동안에는 그대로 멈춰 있다. 한글은 대화 화면과 같은 TMP 폰트(UiDialogueFont)로 그린다.
// 생성 이벤트 경고(warning)는 같은 자리에 경고 모양으로 띄운다: 채널 표시가 WARNING, 화자·채널·테두리가 경고색.
public sealed class HudRadioSubtitle : MonoBehaviour
{
    private const string WarningChannel = "WARNING";

    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Graphic leftPortrait;
    [SerializeField] private Graphic rightPortrait;
    [SerializeField] private RectTransform slideTarget;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text channelText;
    [SerializeField] private Graphic edge;
    [Header("Battle radio - provisional")]
    [SerializeField, Min(0.5f)] private float holdSeconds = 3.5f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.2f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.4f;
    [SerializeField] private float slideInPixels = -28f;

    private float _elapsed = -1f;
    private Vector2 _restPosition;
    private bool _restCaptured;
    private string _channelDefault;
    private Color _channelColor;
    private Color _edgeColor;

    public bool IsShowing => _elapsed >= 0f;
    public bool ShowsWarning { get; private set; }
    public string ChannelShown => channelText != null ? channelText.text : string.Empty;
    public float Elapsed => _elapsed;
    public float TotalSeconds => fadeInSeconds + holdSeconds + fadeOutSeconds;
    public float Alpha => group != null ? group.alpha : 1f;
    public string SpeakerShown => speakerText != null ? speakerText.text : string.Empty;
    public string TextShown => bodyText != null ? bodyText.text : string.Empty;
    public TMP_FontAsset Font => bodyText != null ? bodyText.font : null;

    public void AutoBind()
    {
        if (speakerText == null) speakerText = Find<TMP_Text>("RadioSpeaker");
        if (bodyText == null) bodyText = Find<TMP_Text>("RadioText");
        if (leftPortrait == null) leftPortrait = Find<Graphic>("RadioPortraitLeft");
        if (rightPortrait == null) rightPortrait = Find<Graphic>("RadioPortraitRight");
        if (slideTarget == null) slideTarget = transform as RectTransform;
        if (group == null) group = GetComponent<CanvasGroup>();
        if (channelText == null) channelText = Find<TMP_Text>("RadioChannel");
        if (edge == null) edge = Find<Graphic>("RadioPortraitEdge");
        if (!_restCaptured && slideTarget != null)
        {
            _restPosition = slideTarget.anchoredPosition;
            _restCaptured = true;
            // 평소 무전의 채널 글자와 색(프리팹 값). 경고 뒤에 다시 이 모양으로 돌아온다.
            _channelDefault = channelText != null ? channelText.text : "COMM LINK";
            _channelColor = channelText != null ? channelText.color : UiPalette.Muted;
            _edgeColor = edge != null ? edge.color : UiPalette.Accent;
        }
    }

    // 한글 대사용 폰트. 화자 이름과 대사에 같이 쓴다.
    public void SetFont(TMP_FontAsset font)
    {
        AutoBind();
        if (font == null) return;
        if (speakerText != null && speakerText.font != font) speakerText.font = font;
        if (bodyText != null && bodyText.font != font) bodyText.font = font;
    }

    public void Show(string speaker, string text, bool leftSpeaker, bool warning = false)
    {
        AutoBind();
        ShowsWarning = warning;
        if (speakerText != null)
        {
            speakerText.text = speaker ?? string.Empty;
            speakerText.color = warning ? UiPalette.Alert : UiPalette.Accent;
        }
        if (channelText != null)
        {
            channelText.text = warning ? WarningChannel : _channelDefault;
            channelText.color = warning ? UiPalette.Alert : _channelColor;
        }
        if (edge != null) edge.color = warning ? UiPalette.Alert : _edgeColor;
        if (bodyText != null)
        {
            bodyText.text = text ?? string.Empty;
            bodyText.color = UiPalette.Ink;
        }
        if (leftPortrait != null) leftPortrait.gameObject.SetActive(leftSpeaker);
        if (rightPortrait != null) rightPortrait.gameObject.SetActive(!leftSpeaker);
        _elapsed = 0f;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        Apply();
    }

    public void Animate(float unscaledDeltaTime)
    {
        if (_elapsed < 0f) return;
        _elapsed += unscaledDeltaTime;
        if (_elapsed >= TotalSeconds)
        {
            Clear();
            return;
        }
        Apply();
    }

    // Retry, 라운드 시작, 표시 시간이 끝났을 때.
    public void Clear()
    {
        AutoBind();
        _elapsed = -1f;
        ShowsWarning = false;
        if (slideTarget != null) slideTarget.anchoredPosition = _restPosition;
        if (group != null) group.alpha = 1f;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    // 왼쪽에서 살짝 밀려 들어오며 나타나고, 정한 시간 동안 머문 뒤 사라진다.
    private void Apply()
    {
        float alpha;
        float slide = 0f;
        if (_elapsed < fadeInSeconds)
        {
            float t = fadeInSeconds > 0f ? _elapsed / fadeInSeconds : 1f;
            alpha = UiMotion.EaseOutCubic(t);
            slide = slideInPixels * (1f - UiMotion.EaseOutCubic(t));
        }
        else if (_elapsed < fadeInSeconds + holdSeconds)
            alpha = 1f;
        else
            alpha = UiMotion.Decay(_elapsed - fadeInSeconds - holdSeconds, fadeOutSeconds);
        if (group != null) group.alpha = alpha;
        if (slideTarget != null) slideTarget.anchoredPosition = _restPosition + new Vector2(slide, 0f);
    }

    private T Find<T>(string objectName) where T : Component
    {
        foreach (T candidate in GetComponentsInChildren<T>(true))
            if (candidate.name == objectName) return candidate;
        return null;
    }
}
