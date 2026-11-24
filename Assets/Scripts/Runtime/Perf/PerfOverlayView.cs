using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 성능 오버레이의 화면. 처음 켤 때 작은 Canvas 하나와 TMP 글자 하나를 실행 중에 만든다(Prefab과 게임 UI는 건드리지 않는다).
// 게임 UI보다 위에 그리지만 GraphicRaycaster가 없고 모든 Graphic의 raycastTarget이 꺼져 있어 클릭과 입력을 받지 않는다.
// 글꼴은 TMP 기본 글꼴(LiberationSans SDF)이라 글자는 ASCII만 쓴다.
public sealed class PerfOverlayView
{
    // 게임 UI Canvas(정렬 순서 0)보다 위. Canvas 정렬 순서의 최댓값은 32767이다.
    public const int SortingOrder = 32000;
    private const float FontSize = 19f;
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    // 1920x1080 기준으로 왼쪽 20~530px. 가운데의 Start 버튼, 제목, 결과 카드(540px부터)와 겹치지 않는 폭이다.
    private static readonly Vector2 PanelSize = new Vector2(510f, 318f);
    private static readonly Color PanelColor = new Color(0.02f, 0.03f, 0.05f, 0.8f);

    private GameObject _root;
    private Canvas _canvas;
    private TextMeshProUGUI _text;

    public bool IsVisible => _root != null && _root.activeSelf;
    public Canvas Canvas => _canvas;
    public TMP_Text Text => _text;
    public GameObject Root => _root;

    public void SetVisible(bool visible)
    {
        if (visible && _root == null)
            Build();
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    public void SetText(char[] chars, int length)
    {
        if (_text != null)
            _text.SetCharArray(chars, 0, length);
    }

    public void Destroy()
    {
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        _canvas = null;
        _text = null;
    }

    private void Build()
    {
        _root = new GameObject("PerfOverlay (runtime)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        _canvas = _root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = SortingOrder;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var group = _root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        // 왼쪽 가운데: 좌상단 Pause·Score, 우상단 Timer, 하단 가운데 게이지를 가리지 않는 자리.
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        var panelRect = (RectTransform)panel.transform;
        panelRect.SetParent(_root.transform, false);
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(20f, 0f);
        panelRect.sizeDelta = PanelSize;
        var image = panel.GetComponent<Image>();
        image.color = PanelColor;
        image.raycastTarget = false;

        var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = (RectTransform)label.transform;
        labelRect.SetParent(panelRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 12f);
        labelRect.offsetMax = new Vector2(-16f, -12f);
        _text = label.GetComponent<TextMeshProUGUI>();
        _text.raycastTarget = false;
        _text.richText = true;
        _text.fontSize = FontSize;
        _text.color = Color.white;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.overflowMode = TextOverflowModes.Overflow;
    }
}
