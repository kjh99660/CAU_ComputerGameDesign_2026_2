using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 HUD(와 종료 화면)의 콤보 표시. Presenter가 넘긴 ComboUiModel만 그린다(전투 객체를 읽지 않는다).
// 콤보가 minimumShown 이상일 때만 보이고, 숫자가 오를 때마다 튀어나오며, 남은 시간 막대가 줄어든다.
// 콤보가 끊기면(시간 초과, 피격) 빠르게 사라지고, 라운드가 끝나면(종료 연출 뒤, Retry) 지워진다. 특수 공격은 이어 오던
// 콤보를 끊는다. 그 처치가 콤보로 세어지면(CombatSettings.comboCountsSpecialKills, 기본값) 두 명 이상일 때 새 콤보가
// 바로 보이고, 세어지지 않으면 사라진다. 연출은 모두 unscaled time이다.
// 화면이 꺼져 있는 동안 받은 값은 연출 없이 그대로 맞춘다(종료 화면이 켜질 때 다시 튀지 않게).
// 콤보 점수 보너스가 켜져 있고 1배보다 크면 라벨이 "x1.25  COMBO"처럼 배율을 함께 보여 준다.
public sealed class HudComboCounter : MonoBehaviour
{
    [SerializeField] private TMP_Text countText;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private RectTransform timeFill;
    [SerializeField] private Graphic timeTrack;
    [SerializeField] private CanvasGroup group;
    [Header("Combo HUD - provisional")]
    [SerializeField, Min(2)] private int minimumShown = 2;
    [SerializeField, Min(0f)] private float popSeconds = 0.18f;
    [SerializeField, Min(1f)] private float popScale = 1.35f;
    [SerializeField, Min(0f)] private float fadeSeconds = 0.25f;
    // 남은 시간이 이 비율 아래로 내려가면 막대를 경고색으로 바꾼다. 0이면 쓰지 않는다.
    [SerializeField, Range(0f, 1f)] private float lowTimeFraction = 0.3f;

    private int _shownCount;             // 지금 보이는 콤보. 0이면 숨었거나 사라지는 중이다.
    private int _shownMultiplier = -1;   // 1/100 단위(100 = 보너스 없음). 바뀐 프레임에만 글자를 다시 만든다.
    private float _popElapsed = -1f;
    private float _fadeElapsed = -1f;
    private bool _lowTime;
    private bool _styled;
    private Vector3 _countBaseScale = Vector3.one;
    private Graphic _fillGraphic;

    // 화면에 실제로 그려지는 중이면 true(사라지는 중 포함).
    public bool IsShowing => gameObject.activeInHierarchy;
    public bool IsFading => _fadeElapsed >= 0f;
    public int ShownCount => _shownCount;
    public float Alpha => group != null ? group.alpha : 1f;
    public float TimeFillNormalized => timeFill != null ? timeFill.localScale.x : 0f;
    public Color TimeFillColor => _fillGraphic != null ? _fillGraphic.color : Color.clear;
    public string CountShown => countText != null ? countText.text : string.Empty;
    public string LabelShown => labelText != null ? labelText.text : string.Empty;
    public float CountScale => countText != null ? countText.rectTransform.localScale.x / Mathf.Max(0.0001f, _countBaseScale.x) : 1f;

    public void AutoBind()
    {
        if (countText == null) countText = Find<TMP_Text>("ComboCount");
        if (labelText == null) labelText = Find<TMP_Text>("ComboLabel");
        if (timeFill == null) timeFill = Find<RectTransform>("ComboTimeFill");
        if (timeTrack == null) timeTrack = Find<Graphic>("ComboTimeTrack");
        if (group == null) group = GetComponent<CanvasGroup>();
    }

    // animate: 이 표시가 들어 있는 화면이 보이는 중이면 true. 꺼진 화면은 값만 맞춘다.
    public void Render(in ComboUiModel model, bool animate)
    {
        EnsureStyle();
        if (model.Count >= minimumShown)
        {
            bool rose = model.Count > _shownCount;
            if (model.Count != _shownCount)
            {
                _shownCount = model.Count;
                if (countText != null) countText.SetText("{0}", _shownCount);
            }
            _fadeElapsed = -1f;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (group != null && group.alpha < 1f) group.alpha = 1f;
            if (animate)
            {
                if (rose && popSeconds > 0f)
                {
                    _popElapsed = 0f;
                    ApplyPop();
                }
            }
            else if (_popElapsed >= 0f)
            {
                _popElapsed = -1f;
                ApplyPop();
            }
            RenderTime(model.TimeLeftNormalized);
            RenderMultiplier(model.ScoreMultiplier);
            return;
        }

        // 콤보가 끝났다. 숫자는 사라지는 동안 그대로 두고, 그 사이 새 콤보가 생기면 다시 튀어나온다.
        if (_shownCount == 0) return;
        _shownCount = 0;
        if (animate && fadeSeconds > 0f && gameObject.activeSelf)
        {
            _fadeElapsed = 0f;
            RenderTime(0f);
        }
        else
            Clear();
    }

    public void Animate(float unscaledDeltaTime)
    {
        if (_popElapsed >= 0f)
        {
            _popElapsed += unscaledDeltaTime;
            if (_popElapsed >= popSeconds) _popElapsed = -1f;
            ApplyPop();
        }
        if (_fadeElapsed >= 0f)
        {
            _fadeElapsed += unscaledDeltaTime;
            if (_fadeElapsed >= fadeSeconds)
            {
                Clear();
                return;
            }
            if (group != null) group.alpha = UiMotion.Decay(_fadeElapsed, fadeSeconds);
        }
    }

    // Retry와 라운드 시작, 사라짐의 끝: 연출 없이 바로 숨긴다.
    public void Clear()
    {
        EnsureStyle();
        _shownCount = 0;
        _popElapsed = -1f;
        _fadeElapsed = -1f;
        ApplyPop();
        if (group != null) group.alpha = 1f;
        RenderTime(1f);
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void RenderTime(float normalized)
    {
        if (timeFill == null) return;
        float value = UiMotion.Clamp01(normalized);
        Vector3 scale = timeFill.localScale;
        if (scale.x != value)
        {
            scale.x = value;
            timeFill.localScale = scale;
        }
        bool low = lowTimeFraction > 0f && value < lowTimeFraction;
        if (low == _lowTime) return;
        _lowTime = low;
        if (_fillGraphic != null) _fillGraphic.color = low ? UiPalette.Alert : UiPalette.Accent;
    }

    private void RenderMultiplier(float multiplier)
    {
        if (labelText == null) return;
        int hundredths = Mathf.Max(100, Mathf.RoundToInt(multiplier * 100f));
        if (hundredths == _shownMultiplier) return;
        _shownMultiplier = hundredths;
        if (hundredths > 100) labelText.SetText("x{0:2}  COMBO", hundredths / 100f);
        else labelText.SetText("COMBO");
    }

    private void ApplyPop()
    {
        if (countText == null) return;
        float scale = _popElapsed >= 0f ? UiMotion.Pop(_popElapsed, popSeconds, popScale) : 1f;
        countText.rectTransform.localScale = _countBaseScale * scale;
    }

    // 색은 팔레트에서 가져온다(테마 빌더가 프리팹 색을 다시 칠해도 실행 중 모습은 같다).
    private void EnsureStyle()
    {
        if (_styled) return;
        _styled = true;
        AutoBind();
        if (countText != null)
        {
            _countBaseScale = countText.rectTransform.localScale;
            countText.color = UiPalette.Ink;
        }
        if (labelText != null) labelText.color = UiPalette.Accent;
        if (timeTrack != null) timeTrack.color = UiPalette.WithAlpha(UiPalette.Muted, 0.35f);
        if (timeFill != null) _fillGraphic = timeFill.GetComponent<Graphic>();
        if (_fillGraphic != null) _fillGraphic.color = UiPalette.Accent;
        _lowTime = false;
    }

    private T Find<T>(string objectName) where T : Component
    {
        foreach (T candidate in GetComponentsInChildren<T>(true))
            if (candidate.name == objectName) return candidate;
        return null;
    }
}
