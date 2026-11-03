using UnityEngine;

// 차지 중 바닥에 예상 공격 범위(상자 또는 부채꼴)를 표시한다.
// 크기·각도는 히트박스와 같은 계산(AttackHitData.GetScaledBox / GetSector)을 써서, 표시된 범위 = 실제로 맞는 범위.
// 바닥 판은 실행 시 자동으로 만들어진다 (에디터에서 따로 만들 필요 없음).
public class ChargeRangePreview : MonoBehaviour, IPlayerPresentation
{
    [SerializeField] private AttackHitData chargeData;                          // ChargeAttack 에셋
    [SerializeField] private Material material;                                 // 비우면 기본 반투명 재질 사용
    [SerializeField] private Color startColor = new Color(1f, 0.85f, 0.2f, 0.25f); // 차지 시작 (1배)
    [SerializeField] private Color maxColor = new Color(1f, 0.2f, 0.1f, 0.45f);    // 최대 배율 도달
    [SerializeField] private float groundHeight = 0.02f;                        // 바닥과 겹쳐 깜빡이지 않도록 살짝 띄움
    [SerializeField, Range(1f, 15f)] private float segmentAngle = 5f;           // 부채꼴을 몇 도 간격으로 나눠 그릴지

    private PlayerChargeTracker _charge;
    private GameObject _view;
    private Mesh _mesh;
    private Material _material;

    private void Awake()
    {
        _charge = GetComponent<PlayerChargeTracker>();

        // 바닥 판 만들기. 플레이어 크기(Scale)의 영향을 받지 않도록 자식으로 두지 않는다.
        _view = new GameObject("ChargeRangePreview (auto)");
        _mesh = new Mesh { name = "ChargeRangePreview" };
        _mesh.MarkDynamic();
        _view.AddComponent<MeshFilter>().sharedMesh = _mesh;
        MeshRenderer renderer = _view.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        _material = material != null ? new Material(material) : new Material(Shader.Find("Sprites/Default"));
        renderer.sharedMaterial = _material;
        _view.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_view != null) Destroy(_view);
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
    }

    // 캐릭터 이동/회전과 차지 시간 갱신이 끝난 뒤 위치를 맞춘다.
    private void LateUpdate()
    {
        bool show = chargeData != null && _charge != null && _charge.IsCharging;
        if (_view.activeSelf != show) _view.SetActive(show);
        if (!show) return;

        float s = chargeData.GetChargeScale(_charge.ChargeTime);

        // 판은 플레이어 발밑에, 플레이어가 보는 방향(수평)으로 둔다. 모양은 메쉬로 그린다.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        _view.transform.SetPositionAndRotation(
            transform.position + Vector3.up * groundHeight,
            Quaternion.LookRotation(forward.normalized, Vector3.up));

        if (chargeData.shape == AttackHitData.HitShape.Sector) BuildSector(s);
        else BuildBox(s);

        float max = chargeData.GetMaxChargeScale();
        float t = max > 1f ? Mathf.InverseLerp(1f, max, s) : 1f;
        _material.color = Color.Lerp(startColor, maxColor, t);
    }

    // 상자의 바닥 면 (가로 x, 앞뒤 z)
    private void BuildBox(float s)
    {
        chargeData.GetScaledBox(s, out Vector3 c, out Vector3 size);
        float x0 = c.x - size.x * 0.5f, x1 = c.x + size.x * 0.5f;
        float z0 = c.z - size.z * 0.5f, z1 = c.z + size.z * 0.5f;

        _mesh.Clear();
        _mesh.vertices = new[]
        {
            new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1),
            new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0)
        };
        _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _mesh.RecalculateBounds();
    }

    // 부채꼴: 중심점 + 호 위의 점들을 잇는 삼각형 부채 (정면 0°, 오른쪽 +)
    private void BuildSector(float s)
    {
        chargeData.GetSector(s, out float radius, out float startAngle, out float endAngle);
        int segments = Mathf.Max(2, Mathf.CeilToInt((endAngle - startAngle) / segmentAngle));

        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Lerp(startAngle, endAngle, i / (float)segments) * Mathf.Deg2Rad;
            vertices[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
        }
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        _mesh.Clear();
        _mesh.vertices = vertices;
        _mesh.triangles = triangles;
        _mesh.RecalculateBounds();
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──
    public void ApplyState(GameState state) { }
    public void ResetPresentation()
    {
        if (_view != null) _view.SetActive(false);
    }
}
