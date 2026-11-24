using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Neon rings on the floor where announced enemies are about to appear. They use the floor-marking shader
// REBOOT/Ground Indicator (the charge reach's look: rim, tick marks, range ring, scan line) as a full circle tinted as a
// threat: the floor fills from the centre to the rim and the colour heats from the warning colour to the hot colour as
// the moment comes, so each ring counts down to its enemy. Each ring has its own material instance and its own
// animation clock, which only moves when the countdown does, so a paused game shows still rings.
// Rings are pooled under one parent and made on first need: showing, updating and hiding allocate nothing.
public sealed class EnemySpawnWarnings
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int HotColorId = Shader.PropertyToID("_HotColor");
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int ArcId = Shader.PropertyToID("_Arc");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    // Rings of vertices from the centre out (UV v = share of the radius), as the charge reach's mesh.
    private static readonly float[] RingShares = { 0f, 0.35f, 1f };
    private const int Segments = 48;
    private const float FloorLift = 0.04f;     // above the floor, so the marking does not flicker into it
    private const float FadeInShare = 0.12f;   // share of the countdown over which a ring fades in

    private sealed class Ring
    {
        public Transform Transform;
        public MeshRenderer Renderer;
        public Material Material;
        public bool Shown;
    }

    private readonly Transform _root;
    private readonly Material _source;
    private readonly List<Ring> _rings = new List<Ring>(16);
    private readonly Stack<int> _free = new Stack<int>(16);
    private Mesh _disc;
    private int _shown;

    // source: a REBOOT/Ground Indicator material (CombatVfxSettings.groundIndicatorMaterial). Without one, nothing is
    // drawn and Show returns -1; the spawns themselves still wait for their warning time.
    public EnemySpawnWarnings(Transform parent, Material source)
    {
        _source = source;
        var root = new GameObject("SpawnWarnings");
        _root = root.transform;
        _root.SetParent(parent, false);
    }

    public int ShownCount => _shown;
    public bool CanDraw => _source != null;

    // Shows a ring flat on the floor around floorPosition and returns its marker for Update and Hide.
    public int Show(Vector3 floorPosition, float radius, Color warning, Color hot)
    {
        if (_source == null)
            return -1;
        int marker = _free.Count > 0 ? _free.Pop() : Create();
        Ring ring = _rings[marker];
        float size = Mathf.Max(0.1f, radius);
        ring.Transform.SetPositionAndRotation(floorPosition + Vector3.up * FloorLift, Quaternion.identity);
        ring.Transform.localScale = new Vector3(size, 1f, size);
        ring.Material.SetColor(ColorId, warning);
        ring.Material.SetColor(HotColorId, hot);
        ring.Material.SetVector(SizeId, new Vector4(size, size, 0f, 0f));
        SetProgress(ring, 0f, 0f);
        ring.Renderer.enabled = true;
        ring.Shown = true;
        _shown++;
        return marker;
    }

    // progress: 0 when announced, 1 when the enemy appears. clockSeconds: how long the ring has been counting down.
    public void Update(int marker, float progress, float clockSeconds)
    {
        if (!IsShown(marker))
            return;
        SetProgress(_rings[marker], progress, clockSeconds);
    }

    public void Hide(int marker)
    {
        if (!IsShown(marker))
            return;
        Ring ring = _rings[marker];
        ring.Renderer.enabled = false;
        ring.Shown = false;
        _shown--;
        _free.Push(marker);
    }

    public void HideAll()
    {
        for (int i = 0; i < _rings.Count; i++)
            Hide(i);
    }

    public bool IsShown(int marker) => marker >= 0 && marker < _rings.Count && _rings[marker].Shown;

    // Read by tests and tuning tools.
    public Vector3 PositionOf(int marker) => IsShown(marker) ? _rings[marker].Transform.position : Vector3.zero;
    public float ChargeOf(int marker) => IsShown(marker) ? _rings[marker].Material.GetFloat(ChargeId) : 0f;
    public Material MaterialOf(int marker) => marker >= 0 && marker < _rings.Count ? _rings[marker].Material : null;

    public void Dispose()
    {
        for (int i = 0; i < _rings.Count; i++)
            Release(_rings[i].Material);
        _rings.Clear();
        _free.Clear();
        _shown = 0;
        Release(_disc);
        _disc = null;
        if (_root != null)
            Release(_root.gameObject);
    }

    private static void SetProgress(Ring ring, float progress, float clockSeconds)
    {
        float shown = Mathf.Clamp01(progress);
        ring.Material.SetFloat(ChargeId, shown);
        ring.Material.SetFloat(FadeId, Mathf.Clamp01(shown / FadeInShare));
        ring.Material.SetFloat(ClockId, Mathf.Max(0f, clockSeconds));
    }

    private int Create()
    {
        _disc ??= BuildDisc();
        var ringObject = new GameObject("SpawnWarning " + _rings.Count);
        ringObject.transform.SetParent(_root, false);
        ringObject.AddComponent<MeshFilter>().sharedMesh = _disc;
        MeshRenderer renderer = ringObject.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var material = new Material(_source) { name = "SpawnWarning" };
        material.SetFloat(ModeId, 0f);      // area marking
        material.SetFloat(ShapeId, 0f);     // sector...
        material.SetFloat(ArcId, 360f);     // ...all the way round
        renderer.sharedMaterial = material;
        renderer.enabled = false;
        _rings.Add(new Ring { Transform = ringObject.transform, Renderer = renderer, Material = material });
        return _rings.Count - 1;
    }

    // A flat disc of radius 1 with polar UVs: u round the circle, v from the centre (0) to the rim (1).
    private static Mesh BuildDisc()
    {
        int perRay = RingShares.Length;
        var vertices = new Vector3[(Segments + 1) * perRay];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[Segments * (3 + 6 * (perRay - 2))];
        for (int i = 0; i <= Segments; i++)
        {
            float u = i / (float)Segments;
            float angle = u * Mathf.PI * 2f;
            var ray = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            for (int r = 0; r < perRay; r++)
            {
                vertices[i * perRay + r] = ray * RingShares[r];
                uvs[i * perRay + r] = new Vector2(u, RingShares[r]);
            }
        }
        int index = 0;
        for (int i = 0; i < Segments; i++)
        {
            int a = i * perRay, b = (i + 1) * perRay;
            triangles[index++] = a; triangles[index++] = a + 1; triangles[index++] = b + 1;
            for (int r = 1; r < perRay - 1; r++)
            {
                triangles[index++] = a + r; triangles[index++] = a + r + 1; triangles[index++] = b + r + 1;
                triangles[index++] = a + r; triangles[index++] = b + r + 1; triangles[index++] = b + r;
            }
        }
        var mesh = new Mesh { name = "SpawnWarningDisc" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Release(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }
}
