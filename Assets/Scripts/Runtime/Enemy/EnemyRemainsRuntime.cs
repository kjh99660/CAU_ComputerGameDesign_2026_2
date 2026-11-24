using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// What a destroyed enemy leaves behind.
//   - A normal kill leaves the body where it fell. After a moment its lights dim and bites open in it and eat it
//     away (REBOOT/Enemy Remains).
//   - A heavy blow (charged attack, skill, the special, and a share of normal kills) breaks the robot into its parts
//     instead: head, chest, arms and legs come apart where it stood, pushed a little away from the blow, tumble,
//     bounce on the floor and are eaten away too. They keep the enemy's light while they fly, then dim.
//   - Some remains keep a faint glow and some crackle with electric sparks (RemainsLight), so the bodies and parts
//     on the floor stay easy to see until they are eaten.
// The enemy itself is untouched: it still plays its knockback and death and goes back to the pool as before.
// A body is a pooled copy of the enemy model whose bones take the enemy's last pose (no mesh read-back, so it also
// works in builds where the model is not CPU-readable); broken parts hide the enemy until it is pooled.
// Bodies and parts come from fixed pools made at start-up. When a pool runs out the oldest one is finished early;
// a heavy kill with no free parts leaves a body instead. Parts move by a small ballistic simulation (gravity and
// bounces on the floor found by a raycast), so they never push enemies or the player and need no physics settings.
// Everything moves only in Battle and Finishing on game time (slower in a hit stop and in the Finishing slow
// motion), holds still while paused, and Reset clears it all.
[DisallowMultipleComponent]
public sealed class EnemyRemainsRuntime : MonoBehaviour, IRoundResettable
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int LightsOutId = Shader.PropertyToID("_LightsOut");
    private static readonly int SparkId = Shader.PropertyToID("_Spark");
    private static readonly int SparkColorId = Shader.PropertyToID("_SparkColor");
    private static readonly int GlowHueId = Shader.PropertyToID("_GlowHue");
    private const int MaxProbesPerFrame = 24;

    private readonly List<Body> _bodies = new List<Body>();
    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly Dictionary<EnemyAgent, EnemyParts> _parts = new Dictionary<EnemyAgent, EnemyParts>();
    private readonly HashSet<EnemyAgent> _broken = new HashSet<EnemyAgent>();
    private CombatVfxSettings _settings;
    private EnemyRuntime _enemies;
    private Animator _player;
    private SpecialHitWindow _special;
    private MaterialPropertyBlock _block;
    private SfxRandom _random = new SfxRandom(0x9E3779B9u);
    private int _tick;
    private bool _advancing;
    private int _environmentMask;
    private int _probes;

    private sealed class EnemyParts
    {
        public Renderer[] Renderers;
        public SkinnedMeshRenderer Skin;
        public Transform[] SkinBones;
        public Transform[] PartBones;
        public Vector3 LastHit;
    }

    private sealed class Body
    {
        public GameObject Root;
        public SkinnedMeshRenderer Skin;
        public Transform[] Bones;
        public bool Active;
        public float Age;
        public float Started;
        public Color Tint;
        public Vector4 Seed;
        public RemainsLight.Kind Light;
        // The body topples over its feet, away from the blow that killed it.
        public Vector3 Pivot;
        public Quaternion Upright;
        public Vector3 FallAxis;
        public float FallAngle;
        public float FallSpeed;
        public float FallTarget;
    }

    private sealed class Piece
    {
        public GameObject Root;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public bool Active;
        public float Age;
        public float Started;
        public Vector3 Velocity;
        public Vector3 SpinAxis;
        public float SpinSpeed;
        public float Ground;
        public float Radius;
        public bool Resting;
        public Vector3 Probed;
        public Color Tint;
        public Vector4 Seed;
        public RemainsLight.Kind Light;
        public float Shown;
        public bool Sparking;
    }

    // Read by tests and tuning tools.
    public int ActiveBodies => CountActive(_bodies);
    public int ActivePieces => CountActive(_pieces);
    public int HiddenEnemies => _broken.Count;
    public int BodyPoolSize => _bodies.Count;
    public int PiecePoolSize => _pieces.Count;
    public int PartsPerEnemy => _settings != null && _settings.debrisSet != null ? _settings.debrisSet.parts.Length : 0;
    // Where the parts of the latest break are, for tests (a copy is not made; read only right after a break).
    public bool TryGetPiece(int index, out Vector3 position, out bool resting)
    {
        position = default;
        resting = false;
        if (index < 0 || index >= _pieces.Count || !_pieces[index].Active) return false;
        position = _pieces[index].Root.transform.position;
        resting = _pieces[index].Resting;
        return true;
    }

    // How a part is lit this frame, for tests: the share of the living enemy's light it shows and whether it sparks.
    public bool TryGetPieceLight(int index, out RemainsLight.Kind kind, out float light, out bool sparking)
    {
        kind = RemainsLight.Kind.Dark;
        light = 0f;
        sparking = false;
        if (index < 0 || index >= _pieces.Count || !_pieces[index].Active) return false;
        kind = _pieces[index].Light;
        light = _pieces[index].Shown;
        sparking = _pieces[index].Sparking;
        return true;
    }

    public void Configure(CombatVfxSettings settings, EnemyRuntime enemies, CombatRoundRuntime combat, Animator player)
    {
        _settings = settings;
        _enemies = enemies;
        _player = player;
        _special = new SpecialHitWindow(combat != null ? combat.Stats : null);
        _block = new MaterialPropertyBlock();
        _environmentMask = ~LayerMask.GetMask("Enemy", "Player", "UI", "Ignore Raycast");
        CreatePools();
        if (_enemies != null)
        {
            _enemies.EnemyStruck += OnStruck;
            _enemies.EnemyDied += OnDied;
        }
        enabled = false;
    }

    public void ApplyState(GameStateChangedEvent notification)
    {
        if (notification.Tick < _tick) return;
        _tick = notification.Tick;
        _advancing = notification.Current == GameState.Battle || notification.Current == GameState.Finishing;
        enabled = _advancing && (ActiveBodies > 0 || ActivePieces > 0);
    }

    // Break the enemy apart now, or let it leave a body when its death has played out.
    private void OnStruck(EnemyAgent enemy, Vector3 direction, int tick)
    {
        if (!_advancing || tick != _tick || enemy == null || _settings == null) return;
        PartsOf(enemy).LastHit = direction;
        EnemyDebrisSet set = _settings.debrisSet;
        if (set == null || set.parts.Length == 0 || _settings.remainsMaterial == null || _broken.Contains(enemy)) return;
        bool heavy = _special.IsSpecialHit || CombatHitWeights.FromPlayer(_player) == CombatHitWeight.Heavy;
        if (!heavy && _random.Value() >= _settings.lightKillBreakChance) return;
        if (FreePieces() < set.parts.Length) return;
        EnemyParts parts = PartsOf(enemy);
        if (parts.PartBones == null) return;
        Break(enemy, parts, set, direction);
    }

    private void OnDied(EnemyAgent enemy)
    {
        if (enemy == null) return;
        if (_broken.Remove(enemy))
        {
            // Its parts are already flying; make it visible again for when the pool hands it out next.
            Show(PartsOf(enemy), true);
            return;
        }
        if (!_advancing || _settings == null || _settings.remainsMaterial == null || _bodies.Count == 0) return;
        EnemyParts parts = PartsOf(enemy);
        if (parts.Skin == null || parts.SkinBones == null || !parts.Skin.gameObject.activeInHierarchy) return;
        LeaveBody(parts);
    }

    private void LeaveBody(EnemyParts parts)
    {
        Body body = TakeBody();
        Transform source = parts.Skin.transform.parent != null ? parts.Skin.transform.parent : parts.Skin.transform;
        body.Root.transform.SetPositionAndRotation(source.position, source.rotation);
        body.Root.transform.localScale = source.lossyScale;
        body.Skin.transform.SetLocalPositionAndRotation(parts.Skin.transform.localPosition, parts.Skin.transform.localRotation);
        int count = Mathf.Min(parts.SkinBones.Length, body.Bones.Length);
        for (int i = 0; i < count; i++)
        {
            Transform from = parts.SkinBones[i], to = body.Bones[i];
            if (from == null || to == null) continue;
            to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
            to.localScale = from.localScale;
        }
        Vector3 fall = parts.LastHit;
        fall.y = 0f;
        if (fall.sqrMagnitude < 0.0001f) fall = -source.forward;
        fall.y = 0f;
        body.Pivot = source.position;
        body.Upright = source.rotation;
        body.FallAxis = fall.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, fall.normalized) : Vector3.right;
        body.FallAngle = 0f;
        body.FallSpeed = 0f;
        body.FallTarget = 80f + 10f * _random.Value();
        body.Tint = TintOf(parts.Skin);
        body.Seed = NewSeed();
        body.Light = PickLight();
        body.Age = 0f;
        body.Started = Time.time;
        body.Active = true;
        body.Root.SetActive(true);
        Write(body.Skin, body.Tint, body.Seed, 0f, 0f, false, 0);
        enabled = _advancing;
    }

    private void Break(EnemyAgent enemy, EnemyParts parts, EnemyDebrisSet set, Vector3 direction)
    {
        Color tint = TintOf(parts.Skin);
        Vector3 centre = enemy.transform.position + Vector3.up;
        Vector3 away = direction;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = enemy.transform.forward;
        away.Normalize();
        for (int i = 0; i < set.parts.Length; i++)
        {
            EnemyDebrisSet.Part part = set.parts[i];
            Transform bone = parts.PartBones[i];
            if (part.mesh == null || bone == null) continue;
            Piece piece = TakePiece();
            piece.Filter.sharedMesh = part.mesh;
            piece.Renderer.sharedMaterial = _settings.remainsMaterial;
            float scale = bone.lossyScale.x / Mathf.Max(0.0001f, part.bakedScale);
            piece.Root.transform.SetPositionAndRotation(bone.position, bone.rotation);
            piece.Root.transform.localScale = new Vector3(scale, scale, scale);
            // A small push away from the blow and out from the body's middle, a little hop and a little scatter: the
            // robot comes apart where it stood rather than flying across the arena.
            Vector3 outward = bone.position - centre;
            outward.y = Mathf.Max(0f, outward.y);
            var scatter = new Vector3(_random.Value() * 2f - 1f, _random.Value(), _random.Value() * 2f - 1f) * _settings.pieceSpread;
            piece.Velocity = away * (_settings.pieceSpeed * (0.6f + 0.8f * _random.Value())) + outward * _settings.pieceBurst +
                             Vector3.up * (_settings.pieceUpSpeed * (0.5f + _random.Value())) + scatter;
            piece.SpinAxis = new Vector3(_random.Value() * 2f - 1f, _random.Value() * 2f - 1f, _random.Value() * 2f - 1f);
            if (piece.SpinAxis.sqrMagnitude < 0.01f) piece.SpinAxis = Vector3.right;
            piece.SpinAxis.Normalize();
            piece.SpinSpeed = _settings.pieceSpinDegrees * (0.5f + _random.Value());
            piece.Radius = Mathf.Max(0.03f, part.radius * scale);
            piece.Ground = FloorBelow(bone.position, enemy.transform.position.y);
            piece.Probed = bone.position;
            piece.Resting = false;
            piece.Tint = tint;
            piece.Seed = NewSeed();
            piece.Light = PickLight();
            piece.Shown = 1f;
            piece.Sparking = false;
            piece.Age = 0f;
            piece.Started = Time.time;
            piece.Active = true;
            piece.Root.SetActive(true);
            Write(piece.Renderer, piece.Tint, piece.Seed, 0f, 1f, false, 0);
        }
        Show(parts, false);
        _broken.Add(enemy);
        enabled = _advancing;
    }

    private void LateUpdate()
    {
        if (!_advancing || _settings == null) return;
        float deltaTime = Time.deltaTime;
        _probes = 0;
        bool any = false;
        for (int i = 0; i < _bodies.Count; i++)
        {
            Body body = _bodies[i];
            if (!body.Active) continue;
            body.Age += deltaTime;
            float eaten = Mathf.Clamp01((body.Age - _settings.bodyLingerSeconds) / Mathf.Max(0.01f, _settings.bodyEatenSeconds));
            if (eaten >= 1f)
            {
                Release(body);
                continue;
            }
            Topple(body, deltaTime);
            bool sparking = Sparking(body.Light, body.Age, body.Seed.x) && eaten < 0.8f;
            float light = RemainsLight.Light(body.Light, body.Age, 0f, _settings.bodyLingerSeconds, _settings.remainsAfterglow,
                                             eaten, body.Seed.y, sparking);
            Write(body.Skin, body.Tint, body.Seed, eaten, light, sparking, Flash(body.Age, body.Seed.x));
            any = true;
        }
        for (int i = 0; i < _pieces.Count; i++)
        {
            Piece piece = _pieces[i];
            if (!piece.Active) continue;
            piece.Age += deltaTime;
            Move(piece, deltaTime);
            float eaten = Mathf.Clamp01((piece.Age - _settings.pieceLifeSeconds) / Mathf.Max(0.01f, _settings.pieceEatenSeconds));
            if (eaten >= 1f)
            {
                Release(piece);
                continue;
            }
            piece.Sparking = Sparking(piece.Light, piece.Age, piece.Seed.x) && eaten < 0.8f;
            piece.Shown = RemainsLight.Light(piece.Light, piece.Age, 1f, _settings.pieceDimSeconds, _settings.remainsAfterglow,
                                             eaten, piece.Seed.y, piece.Sparking);
            Write(piece.Renderer, piece.Tint, piece.Seed, eaten, piece.Shown, piece.Sparking, Flash(piece.Age, piece.Seed.x));
            any = true;
        }
        if (!any) enabled = false;
    }

    private RemainsLight.Kind PickLight() =>
        RemainsLight.Pick(_random.Value(), _settings.remainsCrackleShare, _settings.remainsGlowShare);

    private bool Sparking(RemainsLight.Kind kind, float age, float seed) =>
        RemainsLight.Sparking(kind, age, seed, _settings.crackleBurstsPerSecond, _settings.crackleFlickerHz);

    private int Flash(float age, float seed) => RemainsLight.Flash(age, seed, _settings.crackleFlickerHz);

    // Falls like a toppling post: slow at first, faster the further it leans, one small bounce on the floor.
    private static void Topple(Body body, float deltaTime)
    {
        if (body.FallSpeed == 0f && body.FallAngle >= body.FallTarget) return;
        body.FallSpeed += (300f + 1500f * Mathf.Sin(body.FallAngle * Mathf.Deg2Rad)) * deltaTime;
        body.FallAngle += body.FallSpeed * deltaTime;
        if (body.FallAngle >= body.FallTarget)
        {
            body.FallAngle = body.FallTarget;
            body.FallSpeed = body.FallSpeed > 120f ? -body.FallSpeed * 0.2f : 0f;
        }
        body.Root.transform.SetPositionAndRotation(body.Pivot, Quaternion.AngleAxis(body.FallAngle, body.FallAxis) * body.Upright);
    }

    private void Move(Piece piece, float deltaTime)
    {
        Transform transform = piece.Root.transform;
        Vector3 position = transform.position;
        if (!piece.Resting)
        {
            piece.Velocity.y -= _settings.pieceGravity * deltaTime;
            position += piece.Velocity * deltaTime;
            Vector3 travelled = position - piece.Probed;
            travelled.y = 0f;
            if (travelled.sqrMagnitude > 1f && _probes < MaxProbesPerFrame)
            {
                piece.Ground = FloorBelow(position, piece.Ground);
                piece.Probed = position;
            }
            float floor = piece.Ground + piece.Radius;
            if (position.y < floor)
            {
                position.y = floor;
                if (piece.Velocity.y < 0f)
                {
                    // Bounce: lose most of the fall, keep some of the slide, spin slower.
                    piece.Velocity.y = -piece.Velocity.y * _settings.pieceBounce;
                    piece.Velocity.x *= _settings.pieceFriction;
                    piece.Velocity.z *= _settings.pieceFriction;
                    piece.SpinSpeed *= 0.6f;
                    if (piece.Velocity.y < 1.2f)
                    {
                        piece.Velocity.y = 0f;
                        piece.Resting = true;
                    }
                }
            }
        }
        else
        {
            float slow = Mathf.Exp(-8f * deltaTime);
            piece.Velocity.x *= slow;
            piece.Velocity.z *= slow;
            piece.SpinSpeed *= slow;
            position += piece.Velocity * deltaTime;
        }
        Quaternion rotation = transform.rotation;
        if (piece.SpinSpeed > 1f) rotation = Quaternion.AngleAxis(piece.SpinSpeed * deltaTime, piece.SpinAxis) * rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    // The floor under a point (the level, not characters); the fallback when nothing is found.
    private float FloorBelow(Vector3 point, float fallback)
    {
        _probes++;
        if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 40f, _environmentMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return fallback;
    }

    // light: the share of the living enemy's light shown, in the remains' glow colour (a body keeps the dark tint its
    // enemy died with, so its light cannot come from the tint). While it sparks, flash picks the arcs drawn (a new set
    // every flash); the seed's spare w carries it so the bite pattern (x, y, z) stays put.
    private void Write(Renderer renderer, Color tint, Vector4 seed, float eaten, float light, bool sparking, int flash)
    {
        renderer.GetPropertyBlock(_block);
        _block.SetColor(BaseColorId, tint);
        _block.SetColor(GlowHueId, _settings.remainsGlowHue);
        _block.SetFloat(DissolveId, eaten);
        _block.SetFloat(LightsOutId, 1f - light);
        _block.SetFloat(SparkId, sparking ? 1f - eaten : 0f);
        _block.SetColor(SparkColorId, _settings.crackleColor);
        seed.w = flash % 997;
        _block.SetVector(SeedId, seed);
        renderer.SetPropertyBlock(_block);
    }

    private Color TintOf(SkinnedMeshRenderer skin)
    {
        if (skin == null) return Color.white;
        skin.GetPropertyBlock(_block);
        Color tint = _block.GetColor(BaseColorId);
        if (tint.maxColorComponent <= 0f && skin.sharedMaterial != null && skin.sharedMaterial.HasProperty(BaseColorId))
            tint = skin.sharedMaterial.GetColor(BaseColorId);
        return tint.maxColorComponent > 0f ? tint : Color.white;
    }

    private Vector4 NewSeed() => new Vector4(_random.Value() * 97f, _random.Value() * 97f, _random.Value() * 97f, 0f);

    // Looked up once per pooled enemy (the pool reuses the same few hundred objects).
    private EnemyParts PartsOf(EnemyAgent enemy)
    {
        if (_parts.TryGetValue(enemy, out EnemyParts parts)) return parts;
        parts = new EnemyParts
        {
            Renderers = enemy.GetComponentsInChildren<Renderer>(true),
            Skin = enemy.GetComponentInChildren<SkinnedMeshRenderer>(true)
        };
        if (parts.Skin != null) parts.SkinBones = parts.Skin.bones;
        EnemyDebrisSet set = _settings != null ? _settings.debrisSet : null;
        if (set != null && set.parts.Length > 0 && parts.SkinBones != null)
        {
            var bones = new Transform[set.parts.Length];
            bool complete = true;
            for (int i = 0; i < set.parts.Length; i++)
            {
                for (int b = 0; b < parts.SkinBones.Length && bones[i] == null; b++)
                    if (parts.SkinBones[b] != null && parts.SkinBones[b].name == set.parts[i].bone) bones[i] = parts.SkinBones[b];
                complete &= bones[i] != null;
            }
            parts.PartBones = complete ? bones : null;
        }
        _parts.Add(enemy, parts);
        return parts;
    }

    private static void Show(EnemyParts parts, bool visible)
    {
        if (parts?.Renderers == null) return;
        for (int i = 0; i < parts.Renderers.Length; i++)
            if (parts.Renderers[i] != null) parts.Renderers[i].forceRenderingOff = !visible;
    }

    private void CreatePools()
    {
        if (_settings == null || _settings.remainsMaterial == null) return;
        GameObject model = _settings.remainsBodyModel;
        for (int i = 0; model != null && i < _settings.bodyPoolSize; i++)
        {
            GameObject root = Instantiate(model, transform);
            root.name = "Enemy Body " + i;
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin == null)
            {
                Destroy(root);
                break;
            }
            var materials = new Material[Mathf.Max(1, skin.sharedMaterials.Length)];
            for (int m = 0; m < materials.Length; m++) materials[m] = _settings.remainsMaterial;
            skin.sharedMaterials = materials;
            // The pose differs from the model's bind pose; keep the body from being culled while it lies on the floor.
            Bounds bounds = skin.localBounds;
            bounds.Expand(2f);
            skin.localBounds = bounds;
            root.SetActive(false);
            _bodies.Add(new Body { Root = root, Skin = skin, Bones = skin.bones });
        }
        for (int i = 0; i < _settings.piecePoolSize; i++)
        {
            var root = new GameObject("Enemy Part " + i);
            root.transform.SetParent(transform, false);
            var piece = new Piece { Root = root, Filter = root.AddComponent<MeshFilter>(), Renderer = root.AddComponent<MeshRenderer>() };
            root.SetActive(false);
            _pieces.Add(piece);
        }
    }

    private Body TakeBody()
    {
        Body oldest = null;
        foreach (Body body in _bodies)
        {
            if (!body.Active) return body;
            if (oldest == null || body.Started < oldest.Started) oldest = body;
        }
        Release(oldest);
        return oldest;
    }

    private Piece TakePiece()
    {
        Piece oldest = null;
        foreach (Piece piece in _pieces)
        {
            if (!piece.Active) return piece;
            if (oldest == null || piece.Started < oldest.Started) oldest = piece;
        }
        Release(oldest);
        return oldest;
    }

    private int FreePieces()
    {
        int free = 0;
        foreach (Piece piece in _pieces)
            if (!piece.Active) free++;
        return free;
    }

    private static int CountActive(List<Body> list)
    {
        int count = 0;
        foreach (Body body in list)
            if (body.Active) count++;
        return count;
    }

    private static int CountActive(List<Piece> list)
    {
        int count = 0;
        foreach (Piece piece in list)
            if (piece.Active) count++;
        return count;
    }

    private static void Release(Body body)
    {
        body.Active = false;
        body.Root.SetActive(false);
    }

    private static void Release(Piece piece)
    {
        piece.Active = false;
        piece.Root.SetActive(false);
    }

    public void StopProducing() { }

    public IEnumerator ResetRound(int tick)
    {
        _tick = tick;
        foreach (Body body in _bodies) Release(body);
        foreach (Piece piece in _pieces) Release(piece);
        foreach (EnemyAgent enemy in _broken)
            if (enemy != null) Show(PartsOf(enemy), true);
        _broken.Clear();
        enabled = false;
        yield break;
    }

    private void OnDestroy()
    {
        if (_enemies != null)
        {
            _enemies.EnemyStruck -= OnStruck;
            _enemies.EnemyDied -= OnDied;
        }
        _special?.Dispose();
    }
}
