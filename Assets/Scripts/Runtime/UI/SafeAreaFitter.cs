using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 화면 View의 내용을 Screen.safeArea 안에 맞춘다. View 루트의 기준점(anchor)만 안전 영역으로 옮기고(위치·크기 값은 그대로라
// View가 직접 흔드는 위치와 부딪히지 않는다), 화면 전체를 덮는 배경·어둡게 덮는 막(자식이 없는 전체 늘림 Graphic, 글자 제외)은
// 기준점을 바깥으로 넓혀 계속 화면 전체를 덮게 한다. 데스크톱처럼 안전 영역이 화면 전체이면 아무것도 움직이지 않는다.
// 화면 크기나 안전 영역(또는 실행 중에 생긴 자식)이 바뀐 프레임에만 다시 계산하며, 매 프레임 할당하지 않는다.
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaFitter : MonoBehaviour
{
    private const float Epsilon = 0.0001f;
    private static readonly Rect FullScreen = new Rect(0f, 0f, 1f, 1f);

    private static bool s_simulating;
    private static Rect s_simulatedSafeArea;
    private static Vector2 s_simulatedScreen;

    private readonly List<RectTransform> _backgrounds = new List<RectTransform>(4);
    private RectTransform _rect;
    private Rect _appliedArea;
    private int _appliedChildCount = -1;
    private bool _applied;

    // 화면 전체를 덮도록 넓혀 둔 직계 자식인지(어둡게 덮는 막, 번쩍임, 화면 전체 클릭 영역).
    public bool IsBackground(RectTransform child) => child != null && _backgrounds.Contains(child);

    // 테스트·해상도 점검용 Hook: 실제 화면 대신 이 화면 크기와 안전 영역(둘 다 픽셀)을 쓴다. 다음 갱신에 모든 Fitter가 다시 맞춘다.
    public static void Simulate(Rect safeArea, Vector2 screenSize)
    {
        s_simulating = true;
        s_simulatedSafeArea = safeArea;
        s_simulatedScreen = screenSize;
    }

    public static void StopSimulating() => s_simulating = false;

    // 지금 쓸 안전 영역을 화면 비율(0~1)로 돌려준다.
    public static Rect CurrentNormalizedSafeArea() => s_simulating
        ? Normalize(s_simulatedSafeArea, s_simulatedScreen)
        : Normalize(Screen.safeArea, new Vector2(Screen.width, Screen.height));

    // 픽셀 단위 안전 영역을 화면 비율로 바꾼다. 화면 밖으로 나간 부분은 잘라 내고, 쓸 수 없는 값이면 화면 전체를 쓴다.
    public static Rect Normalize(Rect safeArea, Vector2 screenSize)
    {
        if (screenSize.x <= 0f || screenSize.y <= 0f)
            return FullScreen;
        float xMin = Mathf.Clamp01(safeArea.xMin / screenSize.x);
        float yMin = Mathf.Clamp01(safeArea.yMin / screenSize.y);
        float xMax = Mathf.Clamp01(safeArea.xMax / screenSize.x);
        float yMax = Mathf.Clamp01(safeArea.yMax / screenSize.y);
        if (xMax - xMin < 0.1f || yMax - yMin < 0.1f)
            return FullScreen;
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    // 안전 영역에 맞춘 부모 안에서 자식이 화면 전체를 덮으려면 둘 기준점(부모 비율)을 이렇게 둔다. 안전 영역이 화면 전체면 (0,0)-(1,1).
    public static void FullScreenAnchors(Rect normalizedSafeArea, out Vector2 anchorMin, out Vector2 anchorMax)
    {
        Vector2 size = normalizedSafeArea.size;
        anchorMin = new Vector2(-normalizedSafeArea.xMin / size.x, -normalizedSafeArea.yMin / size.y);
        anchorMax = new Vector2((1f - normalizedSafeArea.xMin) / size.x, (1f - normalizedSafeArea.yMin) / size.y);
    }

    private void OnEnable() => Apply();

    private void LateUpdate()
    {
        if (!_applied || transform.childCount != _appliedChildCount || CurrentNormalizedSafeArea() != _appliedArea)
            Apply();
    }

    // 지금의 안전 영역으로 다시 맞춘다. 해상도 점검처럼 같은 프레임 안에서 결과가 필요할 때 직접 부를 수 있다.
    public void Apply()
    {
        if (_rect == null)
            _rect = (RectTransform)transform;
        Rect area = CurrentNormalizedSafeArea();
        CollectBackgrounds();
        SetAnchors(_rect, area.min, area.max);
        FullScreenAnchors(area, out Vector2 backgroundMin, out Vector2 backgroundMax);
        for (int i = 0; i < _backgrounds.Count; i++)
            SetAnchors(_backgrounds[i], backgroundMin, backgroundMax);
        _appliedArea = area;
        _appliedChildCount = transform.childCount;
        _applied = true;
    }

    // 처음 본 직계 자식 가운데 화면 전체를 덮는 배경을 기억한다(한 번 넓힌 뒤에는 기준점이 달라지므로 목록으로 기억한다).
    private void CollectBackgrounds()
    {
        for (int i = _backgrounds.Count - 1; i >= 0; i--)
            if (_backgrounds[i] == null || _backgrounds[i].parent != transform)
                _backgrounds.RemoveAt(i);
        for (int i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i) is RectTransform child && !_backgrounds.Contains(child) && IsFullScreenLeaf(child))
                _backgrounds.Add(child);
        }
    }

    // 부모 전체로 늘어나 있고(기준점 0~1, 여백 0) 자식이 없는 Graphic: 어둡게 덮는 막, 번쩍임, 화면 전체 클릭 영역 같은 배경이다.
    // 자식을 가진 전체 늘림 컨테이너는 내용으로 보고 안전 영역 안에 남긴다. 글자는 화면 전체로 늘려 둔 것(가운데 정렬 배너 등)이라도
    // 읽혀야 하는 내용이므로 배경으로 보지 않고 안전 영역 안에 둔다.
    private static bool IsFullScreenLeaf(RectTransform child) =>
        child.childCount == 0 && child.TryGetComponent(out Graphic graphic) && !IsText(graphic) &&
        Near(child.anchorMin, Vector2.zero) && Near(child.anchorMax, Vector2.one) &&
        Near(child.offsetMin, Vector2.zero, 0.5f) && Near(child.offsetMax, Vector2.zero, 0.5f);

    private static bool IsText(Graphic graphic) => graphic is TMP_Text || graphic is Text;

    // 값이 그대로면 건드리지 않아 레이아웃을 다시 계산하게 만들지 않는다.
    private static void SetAnchors(RectTransform target, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (target.anchorMin != anchorMin) target.anchorMin = anchorMin;
        if (target.anchorMax != anchorMax) target.anchorMax = anchorMax;
    }

    private static bool Near(Vector2 a, Vector2 b, float tolerance = Epsilon) =>
        Mathf.Abs(a.x - b.x) <= tolerance && Mathf.Abs(a.y - b.y) <= tolerance;
}
