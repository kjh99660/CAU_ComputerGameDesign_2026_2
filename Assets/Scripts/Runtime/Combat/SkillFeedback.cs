using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// What the skill (E, the dash along a line) shows, besides the player's own animation:
//   lane   - while the skill winds up, the stretch of floor the dash will cut: a neon lane as wide as the blow, filling
//            from the player to its far end as the wind-up runs out, with streaks of light rushing along it;
//   ghosts - hologram copies of the player left behind along the path the dash really took, as if the player had
//            blinked from one to the next. Each builds up from the feet and breaks up into slices as it goes;
//   cut    - the line the blade drew through the air from where the dash began to where it ended.
// The skill's adapter (PlayerSkill) calls these; one set is enough because the skill cannot start again before they
// have faded. They follow the round: they run in Battle and Finishing, hold still in Pause (they stay on screen) and
// are cleared by Retry and every other state. The time scale is never touched and nothing is allocated per frame.
// The lane and the cut use the neon ground-indicator material, the ghosts their own shader; without one of these
// that part is simply not drawn. Every size and time is a provisional value in CombatVfxSettings.
[DisallowMultipleComponent]
public sealed class SkillFeedback : MonoBehaviour, IRoundResettable
{
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int DashesId = Shader.PropertyToID("_Dashes");
    private static readonly int ClockId = Shader.PropertyToID("_Clock");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int RevealId = Shader.PropertyToID("_Reveal");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int BaseYId = Shader.PropertyToID("_BaseY");
    private static readonly int HeightId = Shader.PropertyToID("_Height");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private const float FloorLift = 0.06f;      // above the floor, so the lane does not flicker against it
    private const float RushLift = 0.04f;       // the rushing streaks lie just above the lane
    private const float AimFadeIn = 0.07f;      // seconds
    private const int MaxRush = 48;
    private const int CutPoints = 9;
    private const int MaxGhosts = 10;
    private const int MaxParts = 12;
    private const float GhostBuildSeconds = 0.07f;
    private const float GhostHeight = 1.9f;

    private CombatVfxSettings _settings;
    private bool _ownsSettings;
    private bool _advancing;
    private int _tick;
    private float _clock;

    // Lane.
    private Transform _laneRoot;
    private MeshRenderer _lane, _rush;
    private Material _laneMaterial, _rushMaterial;
    private Mesh _laneMesh, _rushMesh;
    private readonly Vector3[] _rushVertices = new Vector3[MaxRush * 4];
    private readonly Color32[] _rushColors = new Color32[MaxRush * 4];
    private int _rushCount;
    private float _aimElapsed, _aimSeconds, _aimLength;

    // Cut.
    private LineRenderer _cut;
    private Material _cutMaterial;
    private float _cutElapsed;

    // Ghosts: one captured pose (baked meshes and the player's rigid parts), drawn at several places.
    private Material _ghostMaterial;
    private MaterialPropertyBlock _ghostProperties;
    private SkinnedMeshRenderer[] _skinned = System.Array.Empty<SkinnedMeshRenderer>();
    private MeshRenderer[] _rigid = System.Array.Empty<MeshRenderer>();
    private MeshFilter[] _rigidFilters = System.Array.Empty<MeshFilter>();
    private Mesh[] _baked = System.Array.Empty<Mesh>();
    private readonly Mesh[] _partMesh = new Mesh[MaxParts];
    private readonly Matrix4x4[] _partMatrix = new Matrix4x4[MaxParts];
    private readonly int[] _partSubMeshes = new int[MaxParts];
    private int _partCount;
    private Vector3 _poseOrigin;
    private readonly Vector3[] _ghostOffset = new Vector3[MaxGhosts];
    private readonly float[] _ghostBorn = new float[MaxGhosts];
    private int _ghostCount;
    private Vector3 _lastGhostAt;

    // Read by tests.
    public bool AimShowing => _lane != null && _lane.enabled;
    public Vector3 AimFrom { get; private set; }
    public Vector3 AimTo { get; private set; }
    public float AimWidth { get; private set; }
    public float AimFill => _laneMaterial != null ? _laneMaterial.GetFloat(ChargeId) : 0f;
    public bool StreakShowing => _cut != null && _cut.enabled;
    public Vector3 StreakFrom { get; private set; }
    public Vector3 StreakTo { get; private set; }
    public int DashesPlayed { get; private set; }
    public bool DrawsGhosts => _ghostMaterial != null && _skinned.Length + _rigid.Length > 0;
    public int GhostsSpawned { get; private set; }
    public int GhostsAlive
    {
        get
        {
            int alive = 0;
            for (int i = 0; i < _ghostCount; i++)
                if (_clock - _ghostBorn[i] < GhostSeconds) alive++;
            return alive;
        }
    }
    public Vector3 GhostPosition(int index) => _poseOrigin + _ghostOffset[index];
    public float GhostSpacing => Mathf.Max(0.2f, _settings.skillGhostSpacing);

    private float GhostSeconds => Mathf.Max(0.05f, _settings.skillGhostSeconds);

    // player: the character whose copies the dash leaves (its renderers are read, never changed). Call before anything
    // adds helper renderers under the player.
    public void Configure(Material indicator, CombatVfxSettings settings, Transform player = null)
    {
        _settings = settings;
        if (_settings == null)
        {
            _settings = ScriptableObject.CreateInstance<CombatVfxSettings>();
            _ownsSettings = true;
        }
        enabled = false;
        if (indicator != null)
        {
            BuildLane(indicator);
            BuildCut(indicator);
        }
        if (player != null && _settings.skillGhostShader != null)
        {
            _ghostMaterial = new Material(_settings.skillGhostShader);
            _ghostProperties = new MaterialPropertyBlock();
            _skinned = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _rigid = player.GetComponentsInChildren<MeshRenderer>(true);
            _rigidFilters = new MeshFilter[_rigid.Length];
            for (int i = 0; i < _rigid.Length; i++)
                _rigidFilters[i] = _rigid[i].GetComponent<MeshFilter>();
            _baked = new Mesh[_skinned.Length];
            for (int i = 0; i < _baked.Length; i++)
            {
                _baked[i] = new Mesh { name = "SkillGhostPose" + i };
                _baked[i].MarkDynamic();
            }
        }
    }

    private void BuildLane(Material indicator)
    {
        var root = new GameObject("SkillLane");
        _laneRoot = root.transform;
        _laneRoot.SetParent(transform, false);

        // The lane: a unit strip (x across, z from the player to the far end) scaled to the real size.
        _laneMesh = new Mesh { name = "SkillLane" };
        _laneMesh.SetVertices(new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 1f), new Vector3(0.5f, 0f, 1f) });
        _laneMesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
        _laneMesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        _laneMesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(1f, 0.2f, 1f));
        _laneMaterial = new Material(indicator);
        _laneMaterial.SetFloat(ModeId, 0f);
        _laneMaterial.SetFloat(ShapeId, 1f);
        _lane = AddMesh("Lane", _laneRoot, _laneMesh, _laneMaterial);

        // The rush: short streaks of light that run along the lane. One mesh of quads in metres, refilled every frame.
        _rushMesh = new Mesh { name = "SkillRush" };
        _rushMesh.MarkDynamic();
        var uvs = new Vector2[MaxRush * 4];
        var triangles = new int[MaxRush * 6];
        for (int i = 0; i < MaxRush; i++)
        {
            uvs[i * 4] = new Vector2(0f, 0f);
            uvs[i * 4 + 1] = new Vector2(0f, 1f);
            uvs[i * 4 + 2] = new Vector2(1f, 0f);
            uvs[i * 4 + 3] = new Vector2(1f, 1f);
            int v = i * 4, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        _rushMesh.SetVertices(_rushVertices);
        _rushMesh.SetUVs(0, uvs);
        _rushMesh.SetColors(_rushColors);
        _rushMesh.SetTriangles(triangles, 0);
        _rushMesh.bounds = new Bounds(new Vector3(0f, 0f, 10f), new Vector3(20f, 1f, 40f));
        _rushMaterial = new Material(indicator);
        _rushMaterial.SetFloat(ModeId, 1f);
        _rushMaterial.SetFloat(DashesId, 0f);
        _rushMaterial.SetFloat(ClockId, 0f);
        _rushMaterial.SetFloat(ChargeId, 0.25f);
        var rushRoot = new GameObject("SkillRush").transform;
        rushRoot.SetParent(transform, false);
        _rush = AddMesh("Rush", rushRoot, _rushMesh, _rushMaterial);
    }

    private static MeshRenderer AddMesh(string objectName, Transform holder, Mesh mesh, Material material)
    {
        holder.gameObject.name = objectName;
        holder.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = holder.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false;
        return renderer;
    }

    private void BuildCut(Material indicator)
    {
        var cutObject = new GameObject("SkillCut", typeof(LineRenderer));
        cutObject.transform.SetParent(transform, false);
        _cut = cutObject.GetComponent<LineRenderer>();
        _cut.useWorldSpace = true;
        _cut.textureMode = LineTextureMode.Stretch;
        _cut.shadowCastingMode = ShadowCastingMode.Off;
        _cut.receiveShadows = false;
        _cut.positionCount = CutPoints;   // enough points for the width curve to show along the cut
        // One solid stroke that tapers to nothing at the end the dash started from.
        _cut.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0.35f));
        _cut.startColor = new Color(1f, 1f, 1f, 0f);
        _cut.endColor = Color.white;
        _cutMaterial = new Material(indicator);
        _cutMaterial.SetFloat(ModeId, 1f);
        _cutMaterial.SetFloat(DashesId, 0f);
        _cutMaterial.SetFloat(ClockId, 0f);
        _cut.sharedMaterial = _cutMaterial;
        _cut.enabled = false;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        _tick = notification.Tick;
        _advancing = notification.Current == GameState.Battle || notification.Current == GameState.Finishing;
        if (!_advancing && notification.Current != GameState.Paused) HideAll();
    }

    // While the skill winds up: the lane from the player to where the dash would end with nothing in its way.
    // width: how wide the blow along the dash is (0: the skill does not cut along its dash; a narrow lane then only
    // shows the way). seconds: the length of the wind-up, over which the lane fills.
    public void ShowAim(Vector3 origin, Vector3 direction, float length, float width, float seconds)
    {
        direction.y = 0f;
        if (_lane == null || !_advancing || length <= 0.1f || direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();
        AimWidth = width > 0.05f ? width : Mathf.Max(0.05f, _settings.skillAimWidth);
        _aimLength = length;
        _aimSeconds = Mathf.Max(0.02f, seconds);
        AimFrom = origin + Vector3.up * FloorLift;
        AimTo = AimFrom + direction * length;
        Quaternion facing = Quaternion.LookRotation(direction);
        _laneRoot.SetPositionAndRotation(AimFrom, facing);
        _laneRoot.localScale = new Vector3(AimWidth, 1f, length);
        _laneMaterial.SetVector(SizeId, new Vector4(AimWidth, length, 0f, 0f));
        _rush.transform.SetPositionAndRotation(AimFrom + Vector3.up * RushLift, facing);
        _rushCount = Mathf.Clamp(_settings.skillAimStreaks, 0, MaxRush);
        _aimElapsed = 0f;
        _lane.enabled = true;
        _rush.enabled = _rushCount > 0;
        Advance(0f);
        enabled = true;
    }

    public void HideAim()
    {
        if (_lane == null) return;
        _lane.enabled = false;
        _rush.enabled = false;
    }

    // The dash begins: the player's pose is kept (the player is about to be hidden) and the first copy stands where
    // the dash starts.
    public void CapturePose(Transform player)
    {
        _ghostCount = 0;
        _partCount = 0;
        if (_ghostMaterial == null || !_advancing || player == null) return;
        _poseOrigin = player.position;
        for (int i = 0; i < _skinned.Length && _partCount < MaxParts; i++)
        {
            SkinnedMeshRenderer source = _skinned[i];
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy || source.sharedMesh == null) continue;
            source.BakeMesh(_baked[i], true);
            Transform bone = source.transform;
            AddPart(_baked[i], Matrix4x4.TRS(bone.position, bone.rotation, Vector3.one));
        }
        for (int i = 0; i < _rigid.Length && _partCount < MaxParts; i++)
        {
            MeshRenderer source = _rigid[i];
            MeshFilter filter = _rigidFilters[i];
            if (source == null || filter == null || !source.enabled || !source.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
            AddPart(filter.sharedMesh, source.transform.localToWorldMatrix);
        }
        if (_partCount == 0) return;
        SpawnGhost(_poseOrigin);
        enabled = true;
    }

    private void AddPart(Mesh mesh, Matrix4x4 matrix)
    {
        _partMesh[_partCount] = mesh;
        _partMatrix[_partCount] = matrix;
        _partSubMeshes[_partCount] = mesh.subMeshCount;
        _partCount++;
    }

    // During the dash: another copy every skillGhostSpacing metres of the way really gone. The dash covers about a
    // metre a frame, so one frame can pass several of those places: each gets its copy, on the line between where the
    // last copy stands and where the player is now, and the copies stay evenly spaced whatever the frame rate.
    public void TrailTo(Vector3 position)
    {
        if (_partCount == 0 || !_advancing || _ghostCount >= MaxGhosts) return;
        Vector3 from = _lastGhostAt;
        Vector3 flat = position - from;
        flat.y = 0f;
        float gone = flat.magnitude;
        float spacing = GhostSpacing;
        for (float at = spacing; at <= gone + 0.0001f && _ghostCount < MaxGhosts; at += spacing)
            SpawnGhost(Vector3.Lerp(from, position, at / gone));
    }

    private void SpawnGhost(Vector3 position)
    {
        _ghostOffset[_ghostCount] = position - _poseOrigin;
        _ghostBorn[_ghostCount] = _clock;
        _ghostCount++;
        _lastGhostAt = position;
        GhostsSpawned++;
    }

    // The dash is over: the cut it left, from where it began to where it really ended.
    public void PlayDash(Vector3 from, Vector3 to)
    {
        HideAim();
        if (_cut == null || !_advancing || (to - from).sqrMagnitude < 0.01f) return;
        Vector3 lift = Vector3.up * _settings.skillStreakHeight;
        StreakFrom = from + lift;
        StreakTo = to + lift;
        for (int i = 0; i < CutPoints; i++)
            _cut.SetPosition(i, Vector3.Lerp(StreakFrom, StreakTo, i / (CutPoints - 1f)));
        _cutElapsed = 0f;
        _cut.enabled = true;
        DashesPlayed++;
        Advance(0f);
        enabled = true;
    }

    private void Update()
    {
        if (_advancing) Advance(Time.deltaTime);
        DrawGhosts();   // every frame, also while paused: the copies stay where they are
        if (!AimShowing && !StreakShowing && GhostsAlive == 0) enabled = false;
    }

    private void Advance(float deltaTime)
    {
        _clock += deltaTime;
        if (AimShowing)
        {
            _aimElapsed += deltaTime;
            float fade = Mathf.Clamp01(_aimElapsed / AimFadeIn);
            _laneMaterial.SetFloat(FadeId, fade);
            _laneMaterial.SetFloat(ChargeId, Mathf.Clamp01(_aimElapsed / _aimSeconds));
            _laneMaterial.SetFloat(ClockId, _clock);
            _rushMaterial.SetFloat(FadeId, fade);
            if (_rush.enabled) FillRush();
        }
        if (StreakShowing)
        {
            _cutElapsed += deltaTime;
            float progress = Mathf.Clamp01(_cutElapsed / Mathf.Max(0.05f, _settings.skillStreakSeconds));
            _cut.widthMultiplier = Mathf.Max(0.01f, _settings.skillStreakWidth) * (1f - progress * progress);
            _cutMaterial.SetFloat(ChargeId, 1f - progress);   // hot when it is cut, cooling as it closes
            _cutMaterial.SetFloat(FadeId, 1f - progress);
            if (progress >= 1f) _cut.enabled = false;
        }
    }

    // Streaks of light running from the player to the far end of the lane. Each keeps its place across the lane and
    // its own speed and length; where it is along the lane follows the clock, so it holds still in Pause.
    private void FillRush()
    {
        float half = Mathf.Max(0.05f, AimWidth * 0.5f - 0.12f);
        for (int i = 0; i < MaxRush; i++)
        {
            int v = i * 4;
            if (i >= _rushCount)
            {
                _rushVertices[v] = _rushVertices[v + 1] = _rushVertices[v + 2] = _rushVertices[v + 3] = Vector3.zero;
                continue;
            }
            float across = (Unit(i, 1) * 2f - 1f) * half;
            float length = Mathf.Lerp(0.45f, 1.3f, Unit(i, 2));
            float speed = Mathf.Lerp(14f, 30f, Unit(i, 3));
            float thickness = Mathf.Lerp(0.07f, 0.16f, Unit(i, 4));
            float head = Mathf.Repeat(Unit(i, 5) * _aimLength + _clock * speed, _aimLength + length);
            float tail = Mathf.Max(0f, head - length);
            head = Mathf.Min(head, _aimLength);
            _rushVertices[v] = new Vector3(across - thickness, 0f, tail);
            _rushVertices[v + 1] = new Vector3(across + thickness, 0f, tail);
            _rushVertices[v + 2] = new Vector3(across - thickness, 0f, head);
            _rushVertices[v + 3] = new Vector3(across + thickness, 0f, head);
            // Dim where it enters and leaves the lane, brightest in the middle.
            float where = _aimLength > 0f ? Mathf.Clamp01((head + tail) * 0.5f / _aimLength) : 0f;
            byte alpha = (byte)(255f * Mathf.Sin(where * Mathf.PI));
            var colour = new Color32(255, 255, 255, alpha);
            _rushColors[v] = _rushColors[v + 1] = _rushColors[v + 2] = _rushColors[v + 3] = colour;
        }
        _rushMesh.SetVertices(_rushVertices);
        _rushMesh.SetColors(_rushColors);
    }

    // A fixed number between 0 and 1 for streak "index" and one of its properties.
    private static float Unit(int index, int salt)
    {
        uint x = (uint)(index * 73856093) ^ (uint)(salt * 19349663);
        x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15;
        return (x & 0xFFFFFF) / 16777216f;
    }

    private void DrawGhosts()
    {
        if (_ghostMaterial == null || _partCount == 0) return;
        float life = GhostSeconds;
        for (int g = 0; g < _ghostCount; g++)
        {
            float age = _clock - _ghostBorn[g];
            if (age >= life) continue;
            float t = age / life;
            _ghostProperties.SetColor(ColorId, _settings.skillGhostColor);
            _ghostProperties.SetColor(EdgeColorId, _settings.skillGhostEdgeColor);
            _ghostProperties.SetFloat(RevealId, Mathf.Clamp01(age / GhostBuildSeconds));
            _ghostProperties.SetFloat(DissolveId, Mathf.Clamp01((t - 0.35f) / 0.65f));
            // The first copies stand nearest the camera (it trails behind the dash) and overlap the later ones seen
            // from there, so they are kept dimmer.
            float order = _ghostCount > 1 ? g / (_ghostCount - 1f) : 1f;
            _ghostProperties.SetFloat(FadeId, (1f - Mathf.Clamp01((t - 0.5f) / 0.5f)) * Mathf.Lerp(0.45f, 1f, order));
            _ghostProperties.SetFloat(BaseYId, _poseOrigin.y + _ghostOffset[g].y);
            _ghostProperties.SetFloat(HeightId, GhostHeight);
            _ghostProperties.SetFloat(ClockId, _clock);
            _ghostProperties.SetFloat(SeedId, g + 1f);
            Matrix4x4 move = Matrix4x4.Translate(_ghostOffset[g]);
            for (int p = 0; p < _partCount; p++)
            {
                Matrix4x4 matrix = move * _partMatrix[p];
                for (int sub = 0; sub < _partSubMeshes[p]; sub++)
                    Graphics.DrawMesh(_partMesh[p], matrix, _ghostMaterial, 0, null, sub, _ghostProperties,
                        ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
        }
    }

    public void StopProducing() => HideAll();
    public IEnumerator ResetRound(int tick) { _tick = tick; HideAll(); yield break; }

    private void HideAll()
    {
        HideAim();
        if (_cut != null) _cut.enabled = false;
        _ghostCount = 0;
        _partCount = 0;
        enabled = false;
    }

    private void OnDestroy()
    {
        if (_laneMaterial != null) Destroy(_laneMaterial);
        if (_rushMaterial != null) Destroy(_rushMaterial);
        if (_cutMaterial != null) Destroy(_cutMaterial);
        if (_ghostMaterial != null) Destroy(_ghostMaterial);
        if (_laneMesh != null) Destroy(_laneMesh);
        if (_rushMesh != null) Destroy(_rushMesh);
        foreach (Mesh mesh in _baked)
            if (mesh != null) Destroy(mesh);
        if (_ownsSettings && _settings != null) Destroy(_settings);
    }
}
