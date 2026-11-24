using UnityEngine;
using UnityEngine.UI;

// TransitionLayer: 화면 전환 때 전체 화면을 검게 덮었다가 걷는 최상위 UI 레이어(unscaled time).
// 순수 연출이다. 상태는 GameStateManager가 바꾸고 여기서는 그 결과를 읽기만 하므로 요청과 상태 흐름을 늦추거나 막지 않는다.
// 거의 불투명한 동안에만 포인터 입력을 막고(입력 우선순위 Transition > Dialogue/Modal > State Overlay > HUD),
// 페이드가 끝나거나 다른 상태 변경에 끊기면 언제나 투명하고 입력을 막지 않는다. 투명한 동안에는 레이어를 꺼 둔다.
// 상태는 다른 시스템처럼 알림으로 받는다: UiLayer.OnGameStateChanged가 성공한 상태 변경을 ApplyState로 넘기고,
// Reset의 시작은 RoundResetCoordinator가 부르는 UIRoot.StopProducing이 BeginReset으로 알린다. 시작 상태만
// Configure에서 한 번 읽는다. LateUpdate는 진행 중인 페이드를 걷기만 한다.
[DisallowMultipleComponent]
public sealed class ScreenTransition : MonoBehaviour
{
    // 모두 임시값(provisional)이며 플레이 테스트로 조정한다.
    [Header("Transition fade - provisional")]
    [SerializeField, Min(0f)] private float readyFadeSeconds = 0.35f;
    [SerializeField, Min(0f)] private float resultFadeSeconds = 0.2f;
    [SerializeField, Range(0.05f, 1f)] private float blockInputFromOpacity = 0.5f;
    [SerializeField] private Color fadeColor = Color.black;
    // 다른 모든 UI 위에 그리고 클릭을 먼저 받도록 하는 Canvas 정렬 순서.
    [SerializeField] private int sortingOrder = 1000;

    private const float MaxFadeStepSeconds = 0.05f;

    private readonly ScreenFade _fade = new ScreenFade();
    private bool _configured;
    private GameState _current = GameState.Initializing;
    private int _tick;
    private int _changedFrame = -1;
    private GameObject _layer;
    private Canvas _canvas;
    private CanvasGroup _group;
    private Image _image;
    private float _shownOpacity = -1f;
    private bool _shownBlocking;

    public float Opacity => _fade.Opacity;
    public bool BlocksInput => _fade.BlocksInput(blockInputFromOpacity);
    public bool IsCovering => _fade.IsCovering;
    public bool IsFading => _fade.IsFading;
    public float ReadyFadeSeconds => readyFadeSeconds;
    public float ResultFadeSeconds => resultFadeSeconds;
    public RectTransform Layer => _layer != null ? (RectTransform)_layer.transform : null;

    // UIRoot가 시작 상태를 한 번 넘긴다(그 뒤로는 알림만 받는다). 다시 불러도 레이어를 새로 만들지 않는다.
    public void Configure(GameStateManager stateManager)
    {
        EnsureLayer();
        if (stateManager != null)
        {
            _current = stateManager.CurrentState;
            _tick = stateManager.Tick;
            _fade.Observe(_current, _tick, stateManager.IsResetting, readyFadeSeconds, resultFadeSeconds);
        }
        _configured = true;
        Present();
    }

    // 성공한 상태 변경(UiLayer.OnGameStateChanged가 넘긴다). Reset이 끝나 Ready가 오면 검은 화면에서 걷힌다.
    public void ApplyState(GameStateChangedEvent notification)
    {
        if (!_configured || notification.Tick < _tick)
            return;
        _current = notification.Current;
        _tick = notification.Tick;
        _fade.Observe(_current, _tick, false, readyFadeSeconds, resultFadeSeconds);
        // 막 시작한 페이드는 이 프레임에 걷지 않는다: 새 Ready·Result의 첫 프레임은 완전히 검게 그려진다.
        _changedFrame = Time.frameCount;
        Present();
    }

    // Reset이 시작됐다(UIRoot.StopProducing). Ready가 다시 올 때까지 검게 덮어 둔다.
    public void BeginReset()
    {
        if (!_configured)
            return;
        _fade.Observe(_current, _tick, true, readyFadeSeconds, resultFadeSeconds);
        Present();
    }

    // 진행 중인 페이드만 걷는다. 상태 변경과 Reset 시작은 위의 두 알림으로 들어온다.
    private void LateUpdate()
    {
        if (!_configured || _layer == null || Time.frameCount == _changedFrame)
            return;
        // Reset 직후처럼 한 프레임이 길어져도 페이드가 한 번에 건너뛰지 않게 한 프레임에 걷는 시간을 제한한다.
        _fade.Advance(Mathf.Min(Time.unscaledDeltaTime, MaxFadeStepSeconds));
        Present();
    }

    // 값이 바뀐 프레임에만 레이어를 건드린다.
    private void Present()
    {
        if (_layer == null)
            return;
        float opacity = _fade.Opacity;
        bool blocking = BlocksInput;
        if (opacity == _shownOpacity && blocking == _shownBlocking)
            return;
        _shownOpacity = opacity;
        _shownBlocking = blocking;
        bool visible = opacity > 0f;
        if (_layer.activeSelf != visible)
            _layer.SetActive(visible);
        _group.alpha = opacity;
        _group.blocksRaycasts = blocking;
        if (_canvas.sortingOrder != sortingOrder)
            _canvas.sortingOrder = sortingOrder;
        if (_image.color != fadeColor)
            _image.color = fadeColor;
    }

    // 화면 전체를 덮는 검은 이미지. 자체 Canvas로 다른 UI보다 위에 그리고, 자체 Raycaster로 다른 UI보다 먼저 클릭을 받는다.
    // Safe Area와 무관하게 Canvas 전체를 덮는다.
    private void EnsureLayer()
    {
        if (_layer != null)
            return;
        _layer = new GameObject("TransitionLayer", typeof(RectTransform));
        _layer.layer = gameObject.layer;
        var rect = (RectTransform)_layer.transform;
        rect.SetParent(transform, false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _canvas = _layer.AddComponent<Canvas>();
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = sortingOrder;
        _layer.AddComponent<GraphicRaycaster>();
        _group = _layer.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _image = _layer.AddComponent<Image>();
        _image.color = fadeColor;
        _image.raycastTarget = true;
        _shownOpacity = -1f;
    }
}
