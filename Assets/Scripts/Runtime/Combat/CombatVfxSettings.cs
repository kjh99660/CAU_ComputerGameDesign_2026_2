using UnityEngine;

[CreateAssetMenu(menuName = "Game Demo/Combat VFX Settings")]
public sealed class CombatVfxSettings : ScriptableObject
{
    public GameObject slashPrefab;
    public GameObject impactPrefab;
    [Header("Provisional placement and size (player local space)")]
    public Vector3 slashOffset = new Vector3(0f, 1f, 1.25f);
    public Vector3 slashEulerOffset;
    [Min(0.01f)] public float slashScale = 1f;
    public Vector3 impactOffset = new Vector3(0f, 1f, 0f);
    public Vector3 impactEulerOffset;
    [Min(0.01f)] public float impactScale = 1f;
    [Header("Pool limits / scaled-time safety timeout")]
    [Min(1)] public int maximumInstancesPerEffect = 128;
    [Min(0.1f)] public float maximumLifetime = 3f;

    // Particle burst of the gauge special. One burst at the centre and a ring of copies around it, all pooled.
    // Sizes are written for a 6 m special radius and follow the real radius.
    [Header("Special attack burst - provisional")]
    public GameObject specialBurstPrefab;
    public GameObject specialRadialPrefab;
    public Vector3 specialOffset = new Vector3(0f, 1f, 0f);
    [Min(0.01f)] public float specialBurstScale = 0.8f;
    [Range(0, 24)] public int specialRadialCount = 10;
    [Tooltip("Where the ring of copies sits, as a share of the special radius.")]
    [Range(0f, 1f)] public float specialRadialDistance = 0.5f;
    public Vector3 specialRadialEulerOffset;
    [Min(0.01f)] public float specialRadialScale = 1.7f;

    // Field-of-view pulse (degrees taken off, then released). 0 turns a pulse off.
    [Header("Camera zoom pulse - provisional")]
    [Range(0f, 10f)] public float swingZoomDegrees = 0.8f;
    [Range(0f, 10f)] public float hitZoomDegrees = 2.6f;
    [Range(0f, 20f)] public float specialZoomDegrees = 6f;
    [Min(0.01f)] public float zoomInSeconds = 0.05f;
    [Min(0.01f)] public float zoomOutSeconds = 0.24f;
    [Tooltip("How far the view swings back out past its resting value, as a share of the pulse.")]
    [Range(0f, 1f)] public float zoomOvershoot = 0.2f;

    // Outline that keeps the player readable inside the crowd. Width 0 turns it off.
    [Header("Player outline - provisional")]
    public Shader playerOutlineShader;
    public Color playerOutlineColor = Color.white;
    [Tooltip("Width in pixels on a 1080-pixel-high screen; it scales with the screen height.")]
    [Range(0f, 8f)] public float playerOutlinePixels = 2.5f;
    [Tooltip("Draw the outline even where an enemy or a wall stands in front of the player.")]
    public bool playerOutlineThroughOccluders = true;

    // Hit stop: real seconds the round holds still when a blow lands, by the weight of the blow. 0 turns one off.
    [Header("Hit stop - provisional")]
    [Range(0f, 0.3f)] public float hitStopLight = 0.05f;
    [Range(0f, 0.3f)] public float hitStopMedium = 0.06f;
    [Range(0f, 0.3f)] public float hitStopHeavy = 0.12f;
    [Range(0f, 0.3f)] public float hitStopSpecial = 0.15f;
    [Tooltip("Time scale while a hit stop holds (near 0 is a hard freeze).")]
    [Range(0.02f, 1f)] public float hitStopTimeScale = 0.05f;

    // Camera shake: every event adds "trauma" (0-1) and the shake is trauma squared. Light hits do not shake (they get
    // the hit stop and a small kick), heavy blows and the special do. Mostly rotation; it runs on real time, so it
    // keeps moving through a hit stop. Values follow published hit-feel practice (Genshin hitlag, trauma shake).
    [Header("Camera shake - provisional")]
    [Tooltip("Scales every camera shake and kick; 0 turns them off (for players who feel sick from shake).")]
    [Range(0f, 1f)] public float cameraShakeAmount = 1f;
    [Range(0f, 1f)] public float shakeLight = 0f;
    [Range(0f, 1f)] public float shakeMedium = 0.15f;
    [Range(0f, 1f)] public float shakeHeavy = 0.45f;
    [Range(0f, 1f)] public float shakeSpecial = 0.7f;
    [Range(0f, 1f)] public float shakePlayerHurt = 0.35f;
    [Tooltip("Metres the camera moves at full shake.")]
    [Range(0f, 1f)] public float shakeMaxOffset = 0.08f;
    [Tooltip("Degrees the camera turns at full shake (roll is half again as much).")]
    [Range(0f, 10f)] public float shakeMaxAngle = 3f;
    [Range(1f, 60f)] public float shakeFrequency = 20f;
    [Tooltip("Trauma lost per second.")]
    [Range(0.1f, 10f)] public float shakeDecay = 2.5f;

    // Camera kick: the view jolts a little toward the blow and springs back.
    [Header("Camera kick - provisional")]
    [Range(0f, 1f)] public float kickLight = 0.04f;
    [Range(0f, 1f)] public float kickHeavy = 0.12f;
    [Range(0f, 1f)] public float kickSpecial = 0.2f;
    [Range(0.02f, 1f)] public float kickReturnSeconds = 0.1f;

    [Header("Enemy hit flash - provisional")]
    [Range(0f, 1f)] public float hitFlashSeconds = 0.08f;
    [ColorUsage(false, true)] public Color hitFlashColor = new Color(1.8f, 1.6f, 1.3f, 1f);

    // Neon markings on the floor (REBOOT/Ground Indicator): the charge attack's reach and the special's shock ring.
    [Header("Ground indicators")]
    public Material groundIndicatorMaterial;

    // A hit on the player in the same language: its white outline flashes this colour, then blinks for as long as the
    // player cannot be hit again (CombatSettings.invulnerabilitySeconds).
    [Header("Player hurt - provisional")]
    [ColorUsage(false, true)] public Color hurtOutlineColor = new Color(1.6f, 0.2f, 0.32f, 1f);
    [Range(0f, 0.5f)] public float hurtSolidSeconds = 0.12f;
    [Range(1f, 30f)] public float hurtBlinkHz = 10f;

    // What a destroyed enemy leaves behind (EnemyRemainsRuntime): a body that is eaten away, or parts that fly apart.
    [Header("Enemy remains - provisional")]
    public Material remainsMaterial;
    public EnemyDebrisSet debrisSet;
    [Tooltip("The enemy model (its FBX). Pooled copies of it hold the last pose of fallen enemies.")]
    public GameObject remainsBodyModel;
    [Range(0f, 3f)] public float bodyLingerSeconds = 0.6f;
    [Range(0.1f, 4f)] public float bodyEatenSeconds = 0.9f;
    [Range(0, 64)] public int bodyPoolSize = 32;
    [Tooltip("Parts that can lie around at once (11 per broken enemy, see debrisSet); when they run out, a heavy kill leaves a body.")]
    [Range(0, 256)] public int piecePoolSize = 240;
    [Tooltip("Share of light and medium kills that also break apart; heavy blows and the special always do.")]
    [Range(0f, 1f)] public float lightKillBreakChance = 0.25f;
    // The parts come apart where the enemy stood: a small push away from the blow and out from the body's middle.
    [Tooltip("Push away from the blow (m/s).")]
    [Range(0f, 20f)] public float pieceSpeed = 1.4f;
    [Tooltip("Push out from the body's middle, per metre the part sits from it.")]
    [Range(0f, 6f)] public float pieceBurst = 1.2f;
    [Range(0f, 20f)] public float pieceUpSpeed = 2.2f;
    [Range(0f, 10f)] public float pieceSpread = 0.6f;
    [Range(0f, 2000f)] public float pieceSpinDegrees = 380f;
    [Range(1f, 60f)] public float pieceGravity = 22f;
    [Range(0f, 1f)] public float pieceBounce = 0.35f;
    [Range(0f, 1f)] public float pieceFriction = 0.55f;
    [Range(0.1f, 6f)] public float pieceLifeSeconds = 2.6f;
    [Range(0.1f, 3f)] public float pieceEatenSeconds = 1f;
    [Tooltip("The parts keep the enemy's light this long, dimming as they fly, so the break is easy to see.")]
    [Range(0.05f, 3f)] public float pieceDimSeconds = 0.6f;

    // What stays lit on the remains (parts and bodies): a share keeps a faint, uneven glow, another share crackles
    // with electricity (bright arcs over the surface in short bursts, its lights flickering with them); the rest go
    // dark. All of it fades as the remains are eaten.
    [Header("Enemy remains afterglow - provisional")]
    [Tooltip("Share of the living enemy's glow that a glowing or crackling part keeps.")]
    [Range(0f, 1f)] public float remainsAfterglow = 0.16f;
    [Range(0f, 1f)] public float remainsGlowShare = 0.45f;
    [Range(0f, 1f)] public float remainsCrackleShare = 0.3f;
    [Tooltip("Colour of the remains' own lights (its hue is what counts; a grey gives no light).")]
    [ColorUsage(false)] public Color remainsGlowHue = new Color(1f, 0.35f, 0.15f, 1f);
    [ColorUsage(false, true)] public Color crackleColor = new Color(0.55f, 1.35f, 4f, 1f);
    [Tooltip("How often a crackling part may start a burst of sparks (about half the chances are taken).")]
    [Range(0.2f, 10f)] public float crackleBurstsPerSecond = 2.5f;
    [Tooltip("Flashes per second within a burst; every flash draws new arcs.")]
    [Range(5f, 60f)] public float crackleFlickerHz = 30f;
}
