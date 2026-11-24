using UnityEngine;

// 차지 중 바닥에 예상 공격 범위(상자 또는 부채꼴)를 표시한다.
// 크기·각도는 히트박스와 같은 계산(AttackHitData.GetScaledBox / GetSector)을 써서, 표시된 범위 = 실제로 맞는 범위.
// 바닥 판은 실행 시 자동으로 만들어진다 (에디터에서 따로 만들 필요 없음).
// 재질을 비워 두면 CombatVfxSettings의 바닥 표시 재질(REBOOT/Ground Indicator, 사이버펑크 네온)을 쓴다:
// 청록 테두리·눈금·격자·바깥으로 퍼지는 스캔선, 차지가 쌓이면 안쪽부터 차오르고 다 차면 마젠타로 깜빡인다.
// 메시는 매 프레임 같은 배열을 다시 채워 쓴다(차지 중 메모리 할당 없음).
public class ChargeRangePreview : MonoBehaviour, IPlayerPresentation
{
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int ArcId = Shader.PropertyToID("_Arc");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    // Rings of vertices from the player out: the centre, a little way out and the rim (UV v = share of the radius).
    private static readonly float[] Rings = { 0f, 0.35f, 1f };

    [SerializeField] private AttackHitData chargeData;                          // ChargeAttack 에셋
    [SerializeField] private Material material;                                 // 비우면 네온 바닥 표시 재질 사용
    [SerializeField] private Color startColor = new Color(1f, 0.85f, 0.2f, 0.25f); // 차지 시작 (1배) - 예전 단색 재질용
    [SerializeField] private Color maxColor = new Color(1f, 0.2f, 0.1f, 0.45f);    // 최대 배율 도달 - 예전 단색 재질용
    [SerializeField] private float groundHeight = 0.02f;                        // 바닥과 겹쳐 깜빡이지 않도록 살짝 띄움
    [SerializeField, Range(1f, 15f)] private float segmentAngle = 5f;           // 부채꼴을 몇 도 간격으로 나눠 그릴지

    private PlayerChargeTracker _charge;
    private GameObject _view;
    private Mesh _mesh;
    private Material _material;
    private bool _neon;
    private Vector3[] _vertices;
    private Vector2[] _uvs;
    private int[] _triangles;

    // Read by tests: the preview is up, and how far the charge has filled it (0..1).
    public bool IsShown => _view != null && _view.activeSelf;
    public bool UsesNeon => _neon;
    public float ShownCharge => _neon && _material != null ? _material.GetFloat(ChargeId) : 0f;

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
        Material source = material;
        if (source == null)
        {
            var vfx = Resources.Load<CombatVfxSettings>("CombatVfxSettings");
            if (vfx != null) source = vfx.groundIndicatorMaterial;
        }
        _material = source != null ? new Material(source) : new Material(Shader.Find("Sprites/Default"));
        _neon = _material.HasProperty(ChargeId);
        renderer.sharedMaterial = _material;

        int segments = Mathf.CeilToInt(360f / Mathf.Max(1f, segmentAngle)) + 1;
        _vertices = new Vector3[(segments + 1) * Rings.Length];
        _uvs = new Vector2[_vertices.Length];
        _triangles = new int[segments * (3 + 6 * (Rings.Length - 2))];
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
        if (_neon) _material.SetFloat(ChargeId, t);
        else _material.color = Color.Lerp(startColor, maxColor, t);
    }

    // 상자의 바닥 면 (가로 x, 앞뒤 z). UV: u = 가로, v = 앞쪽으로.
    private void BuildBox(float s)
    {
        chargeData.GetScaledBox(s, out Vector3 c, out Vector3 size);
        float x0 = c.x - size.x * 0.5f, x1 = c.x + size.x * 0.5f;
        float z0 = c.z - size.z * 0.5f, z1 = c.z + size.z * 0.5f;
        _vertices[0] = new Vector3(x0, 0f, z0);
        _vertices[1] = new Vector3(x0, 0f, z1);
        _vertices[2] = new Vector3(x1, 0f, z1);
        _vertices[3] = new Vector3(x1, 0f, z0);
        _uvs[0] = new Vector2(0f, 0f);
        _uvs[1] = new Vector2(0f, 1f);
        _uvs[2] = new Vector2(1f, 1f);
        _uvs[3] = new Vector2(1f, 0f);
        _triangles[0] = 0; _triangles[1] = 1; _triangles[2] = 2;
        _triangles[3] = 0; _triangles[4] = 2; _triangles[5] = 3;
        Apply(4, 6);
        if (_neon)
        {
            _material.SetFloat(ShapeId, 1f);
            _material.SetVector(SizeId, new Vector4(size.x, size.z, 0f, 0f));
        }
    }

    // 부채꼴 (정면 0°, 오른쪽 +). 중심에서 테두리까지 고리 몇 개로 나눠 UV가 정확하게 이어지게 한다.
    // UV: u = 호를 따라 0..1, v = 중심 0 → 테두리 1.
    private void BuildSector(float s)
    {
        chargeData.GetSector(s, out float radius, out float startAngle, out float endAngle);
        int segments = Mathf.Clamp(Mathf.CeilToInt((endAngle - startAngle) / segmentAngle), 2,
            _vertices.Length / Rings.Length - 1);
        int perRay = Rings.Length;
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float a = Mathf.Lerp(startAngle, endAngle, u) * Mathf.Deg2Rad;
            var ray = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
            for (int r = 0; r < perRay; r++)
            {
                _vertices[i * perRay + r] = ray * Rings[r];
                _uvs[i * perRay + r] = new Vector2(u, Rings[r]);
            }
        }
        int index = 0;
        for (int i = 0; i < segments; i++)
        {
            int a = i * perRay, b = (i + 1) * perRay;
            // The centre: one triangle per segment.
            _triangles[index++] = a; _triangles[index++] = a + 1; _triangles[index++] = b + 1;
            // Further out: a quad between each pair of rings.
            for (int r = 1; r < perRay - 1; r++)
            {
                _triangles[index++] = a + r; _triangles[index++] = a + r + 1; _triangles[index++] = b + r + 1;
                _triangles[index++] = a + r; _triangles[index++] = b + r + 1; _triangles[index++] = b + r;
            }
        }
        Apply((segments + 1) * perRay, index);
        if (_neon)
        {
            _material.SetFloat(ShapeId, 0f);
            _material.SetFloat(ArcId, endAngle - startAngle);
            _material.SetVector(SizeId, new Vector4(radius, radius, 0f, 0f));
        }
    }

    private void Apply(int vertexCount, int indexCount)
    {
        _mesh.Clear();
        _mesh.SetVertices(_vertices, 0, vertexCount);
        _mesh.SetUVs(0, _uvs, 0, vertexCount);
        _mesh.SetTriangles(_triangles, 0, indexCount, 0);
        _mesh.RecalculateBounds();
    }

    // ── 팀원 구조가 보내 주는 알림 (PlayerRuntime이 자동으로 찾아서 호출) ──
    public void ApplyState(GameState state) { }
    public void ResetPresentation()
    {
        if (_view != null) _view.SetActive(false);
    }
}
