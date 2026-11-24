using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CountdownView : UIScreenView
{
    // 숫자와 START가 나타나는 순간을 알린다. 효과음이 이 순간에 맞춰 소리를 낸다.
    public event Action<int> NumberShown;
    public event Action StartShown;

    [SerializeField] private TMP_Text numberText;
    [SerializeField] private TMP_Text readyText;
    [SerializeField] private Graphic numberFrame;
    [SerializeField, Min(0.05f)] private float stepDuration = 0.8f;
    [SerializeField, Min(0.05f)] private float startDuration = 0.55f;
    // 연출 값은 모두 임시값이며 unscaled time으로 재생한다. 플레이 테스트로 조정한다.
    [Header("Number animation - provisional")]
    [SerializeField, Min(1f)] private float numberPopScale = 1.55f;
    [SerializeField, Min(0f)] private float numberPopSeconds = 0.22f;
    [SerializeField, Min(0f)] private float numberFadeOutSeconds = 0.22f;
    [SerializeField, Range(0f, 1f)] private float numberEndAlpha = 0.2f;
    [Header("START animation - provisional")]
    [SerializeField, Min(1f)] private float startPopScale = 1.5f;
    [SerializeField, Min(0f)] private float startPopSeconds = 0.2f;
    [SerializeField, Min(0f)] private float startFlashSeconds = 0.35f;
    // 화면 전체에 청록색을 잠깐 덮는 세기. 0이면 번쩍임을 쓰지 않는다.
    // Linear 색공간에서는 어두운 화면 위의 밝은 색이 수치보다 훨씬 강하게 보여 낮게 둔다(0.2는 화면 전체가 청록으로 덮였다).
    [SerializeField, Range(0f, 0.3f)] private float startFlashOpacity = 0.06f;

    private const float NumberFadeInSeconds = 0.08f;
    private const float NumberEndScale = 0.9f;
    private const float FramePulseScale = 1.06f;
    private const float FrameBurstScale = 1.35f;

    private Image _flash;
    private Vector3 _numberRestScale = Vector3.one;
    private Vector3 _readyRestScale = Vector3.one;
    private Vector3 _frameRestScale = Vector3.one;
    private bool _restCached;

    public float StepDuration => stepDuration;
    public float StartDuration => startDuration;

    public override void AutoBind()
    {
        base.AutoBind();
        if (numberText == null) numberText = FindNamed<TMP_Text>("Number");
        if (readyText == null) readyText = FindNamed<TMP_Text>("ReadyText");
        if (numberFrame == null) numberFrame = FindNamed<Graphic>("NumberFrame");
    }

    private void OnEnable()
    {
        AutoBind();
        EnsureFlash();
    }

    public void ShowNumber(int number)
    {
        SetText(numberText, number.ToString());
        SetText(readyText, "READY");
        AnimateNumber(0f);
        NumberShown?.Invoke(number);
    }

    public void ShowStart()
    {
        SetText(numberText, string.Empty);
        SetText(readyText, "START");
        AnimateStart(0f);
        StartShown?.Invoke();
    }

    // 숫자 한 단계가 시작된 뒤 지난 시간으로 자세를 정한다.
    // 숫자는 크게 나타나 제자리로 줄어들고, 단계가 끝날 즈음 옅어지며 조금 더 작아진다.
    public void AnimateNumber(float elapsed)
    {
        CacheRest();
        float tail = UiMotion.Tail(elapsed, stepDuration, numberFadeOutSeconds);
        float scale = UiMotion.Pop(elapsed, numberPopSeconds, numberPopScale) * UiMotion.Lerp(1f, NumberEndScale, tail);
        float alpha = UiMotion.Clamp01(elapsed / NumberFadeInSeconds) * UiMotion.Lerp(1f, numberEndAlpha, tail);
        SetPose(numberText, _numberRestScale, scale, alpha);
        SetPose(numberFrame, _frameRestScale, UiMotion.Pop(elapsed, numberPopSeconds, FramePulseScale), 1f);
        SetPose(readyText, _readyRestScale, 1f, 1f);
        SetFlash(0f);
    }

    // START가 나온 뒤 지난 시간으로 자세를 정한다.
    // 글자가 튀어나오고, 육각 틀은 커지며 사라지고, 화면에 청록색이 잠깐 번쩍인다.
    public void AnimateStart(float elapsed)
    {
        CacheRest();
        float burst = startFlashSeconds <= 0f ? 1f : UiMotion.EaseOutCubic(elapsed / startFlashSeconds);
        SetPose(numberText, _numberRestScale, 1f, 0f);
        SetPose(numberFrame, _frameRestScale, UiMotion.Lerp(1f, FrameBurstScale, burst), 1f - burst);
        SetPose(readyText, _readyRestScale, UiMotion.Pop(elapsed, startPopSeconds, startPopScale), 1f);
        SetFlash(startFlashOpacity * UiMotion.Decay(elapsed, startFlashSeconds));
    }

    public override void ResetView()
    {
        SetText(numberText, "3");
        SetText(readyText, "READY");
        CacheRest();
        SetPose(numberText, _numberRestScale, 1f, 1f);
        SetPose(numberFrame, _frameRestScale, 1f, 1f);
        SetPose(readyText, _readyRestScale, 1f, 1f);
        SetFlash(0f);
    }

    private void CacheRest()
    {
        if (_restCached) return;
        AutoBind();
        if (numberText != null) _numberRestScale = numberText.rectTransform.localScale;
        if (readyText != null) _readyRestScale = readyText.rectTransform.localScale;
        if (numberFrame != null) _frameRestScale = numberFrame.rectTransform.localScale;
        _restCached = true;
    }

    // 크기는 Transform으로, 불투명도는 CanvasRenderer로 바꿔 글자 메시를 다시 만들지 않는다.
    // 굵은 글자는 TMP가 자식 오브젝트(TMP_SubMeshUI)로 따로 그리는데, 부모 CanvasRenderer의 알파는 거기에 전달되지 않는다.
    // 자식 하위 메시의 알파도 함께 맞추지 않으면 글자가 바뀐 프레임의 낮은 알파에 굳어 숫자가 거의 보이지 않는다.
    private static void SetPose(Graphic target, Vector3 restScale, float scale, float alpha)
    {
        if (target == null) return;
        target.rectTransform.localScale = restScale * scale;
        target.canvasRenderer.SetAlpha(alpha);
        if (!(target is TMP_Text)) return;
        Transform root = target.transform;
        for (int i = 0; i < root.childCount; i++)
            if (root.GetChild(i).TryGetComponent(out TMP_SubMeshUI subMesh))
                subMesh.canvasRenderer.SetAlpha(alpha);
    }

    private void SetFlash(float alpha)
    {
        if (_flash == null) return;
        bool visible = alpha > 0.001f;
        if (_flash.enabled != visible) _flash.enabled = visible;
        if (visible) _flash.canvasRenderer.SetAlpha(alpha);
    }

    // 카운트다운 요소 뒤에 깔리는 전체 화면 번쩍임. 입력을 막지 않는다.
    private void EnsureFlash()
    {
        if (_flash != null) return;
        var go = new GameObject("StartFlash", typeof(RectTransform), typeof(Image));
        go.layer = gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _flash = go.GetComponent<Image>();
        _flash.color = UiPalette.Accent;
        _flash.raycastTarget = false;
        _flash.enabled = false;
    }
}
