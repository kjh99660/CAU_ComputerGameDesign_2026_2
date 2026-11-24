using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Authors deterministic skeletal poses against the imported bind/rest hierarchy.
// Clips use Transform curves, so they do not need retargeting or modify the source Avatar.
public static class GameDemoHumanoidEnemyBuilder
{
    public const string ModelPath = "Assets/MeshyImports/EnemyHumanoid/EmenyHumanoid.fbx";
    public const string PrefabPath = "Assets/Prefabs/Enemy/EnemyPrototype.prefab";
    public const string Folder = "Assets/Animations/Enemy/Humanoid";
    // Attack2 and Attack3 are looks for the same attack: same length and the same contact moment as Attack.
    // The game picks one at random per attack (EnemyAgent); rules and timing do not depend on which one plays.
    private static readonly string[] States = { "Idle", "Chase", "Walk", "Attack", "Attack2", "Attack3", "Knockback", "Dying" };
    private static EnemyGait.Rig _rig;
    private static EnemyGait _idleGait, _runGait, _walkGait;

    [MenuItem("Tools/Game Demo Enemy/Build Humanoid Enemy")]
    public static void BuildAll()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Animations/Enemy", "Humanoid");
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null) throw new InvalidOperationException("Rigged enemy model is missing.");
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            // Keep the root EnemyAgent, body, collider, scale and their existing file IDs.
            Transform visual = root.transform.Find("Visual");
            foreach (Transform child in visual.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.scene);
            model.transform.SetParent(visual, false);
            // Unpack only this newly-created instance. Keep the original FBX/prefab untouched.
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "EnemyHumanoidModel";
            foreach (Animator nested in model.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(nested);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null || skin.bones.Length < 20) throw new InvalidOperationException("Model does not have a skinned skeleton.");
            // Fit the new model to the existing collision volume, including the root's configured scale.
            Bounds bounds = skin.bounds;
            float height = root.GetComponent<BoxCollider>().size.y;
            float factor = height * root.transform.lossyScale.y / bounds.size.y;
            model.transform.localScale *= factor;
            bounds = skin.bounds;
            Vector3 floorCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.transform.position += root.transform.position - floorCenter;
            // Renderer bounds are padded, so the line above leaves the mesh hovering. Sit the real soles on the root plane.
            model.transform.position += Vector3.down * LowestVertexHeight(root.transform);

            AnimatorController controller = BuildClipsAndController(root.transform, model.transform);
            Animator animator = root.GetComponent<Animator>();
            animator.avatar = null; // Exact bone paths drive the skin directly; the FBX retains its Humanoid Avatar.
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        ValidateAll();
        Debug.Log("[Humanoid Enemy] Built five skeletal clips and connected the gameplay prefab.");
    }

    // Height of the lowest skinned vertex above the gameplay root, in world metres, for the pose the rig is in.
    private static float LowestVertexHeight(Transform root)
    {
        SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null || skin.sharedMesh == null) return 0f;
        Mesh mesh = skin.sharedMesh;
        Transform[] bones = skin.bones;
        Matrix4x4[] bind = mesh.bindposes;
        BoneWeight[] weights = mesh.boneWeights;
        Vector3[] vertices = mesh.vertices;
        float lowest = float.MaxValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            int bone = weights[i].boneIndex0;
            lowest = Mathf.Min(lowest, bones[bone].localToWorldMatrix.MultiplyPoint3x4(bind[bone].MultiplyPoint3x4(vertices[i])).y);
        }
        return lowest - root.position.y;
    }

    // The prefab was fitted to padded renderer bounds, which left the model hovering above the ground.
    // Moves only the model's local height so the soles rest on the root plane; nothing else in the prefab changes.
    [MenuItem("Tools/Game Demo Enemy/Ground Humanoid Enemy Model")]
    public static void GroundModel()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform model = root.transform.Find("Visual/EnemyHumanoidModel");
            if (model == null) throw new InvalidOperationException("Run Build Humanoid Enemy first: the prefab has no rigged model.");
            float gap = LowestVertexHeight(root.transform);
            if (Mathf.Abs(gap) > 0.002f)
            {
                model.position += Vector3.down * gap;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[Humanoid Enemy] Model lowered by {gap:F3} m so its soles rest on the root plane.");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // Regenerates the clips and the controller in place (same GUIDs). The prefab only gets its model height corrected.
    [MenuItem("Tools/Game Demo Enemy/Rebuild Humanoid Enemy Clips")]
    public static void RebuildClips()
    {
        GroundModel();
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform model = root.transform.Find("Visual/EnemyHumanoidModel");
            if (model == null) throw new InvalidOperationException("Run Build Humanoid Enemy first: the prefab has no rigged model.");
            BuildClipsAndController(root.transform, model);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        ValidateAll();
        Debug.Log("[Humanoid Enemy] Rebuilt clips. " + GaitReport());
    }

    private static AnimatorController BuildClipsAndController(Transform root, Transform model)
    {
        Transform[] bones = model.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("mixamorig:", StringComparison.Ordinal)).ToArray();
        var settings = AssetDatabase.LoadAssetAtPath<EnemySettings>("Assets/Prefabs/Enemy/EnemySettings.asset");
        ConfigureGaits(root, bones, settings);
        var durations = new Dictionary<string, float>
        {
            { "Idle", IdleCycle }, { "Chase", RunCycle }, { "Walk", WalkCycle }, { "Attack", settings.AttackDuration },
            { "Attack2", settings.AttackDuration }, { "Attack3", settings.AttackDuration },
            { "Knockback", settings.KnockbackFallbackDuration }, { "Dying", Mathf.Max(0.05f, settings.DeathDuration) }
        };
        var clips = new Dictionary<string, AnimationClip>();
        foreach (string state in States)
            clips.Add(state, BuildClip(state, durations[state], state == "Idle" || state == "Chase" || state == "Walk", root, model, bones));
        return BuildController(clips);
    }

    // Provisional motion styling: cycle lengths and the shape of each gait. Speeds come from EnemySettings so the
    // runtime plays each clip at "real speed / authored speed".
    private const float IdleCycle = 2f;
    private const float RunCycle = 0.6f;
    private const float WalkCycle = 0.8f;

    private static void ConfigureGaits(Transform root, Transform[] bones, EnemySettings settings)
    {
        Transform Bone(string name) => bones.First(b => b.name == "mixamorig:" + name);
        Vector3 Local(Transform bone) => root.InverseTransformPoint(bone.position) * root.lossyScale.y;   // root-space metres
        EnemyGait.Leg Measure(string side)
        {
            Vector3 hip = Local(Bone(side + "UpLeg")), knee = Local(Bone(side + "Leg")), ankle = Local(Bone(side + "Foot"));
            return new EnemyGait.Leg
            {
                Hip = hip,
                Thigh = Vector3.Distance(hip, knee),
                Shin = Vector3.Distance(knee, ankle),
                RestThigh = Mathf.Atan2(knee.z - hip.z, hip.y - knee.y),
                RestShin = Mathf.Atan2(ankle.z - knee.z, knee.y - ankle.y),
                AnkleHeight = ankle.y
            };
        }
        MeasureSole(root, bones, Local(Bone("LeftFoot")), out float soleTip, out float heelBack);
        _rig = new EnemyGait.Rig
        {
            Left = Measure("Left"),
            Right = Measure("Right"),
            HipsCenter = Local(Bone("Hips")),
            SoleForward = soleTip * 0.75f,   // the ball of the foot sits behind the tip of the sole
            SoleTip = soleTip,
            HeelBack = heelBack
        };
        float leg = _rig.LegLength;

        // Ready stance: knees soft, weight shifting, arms hanging ready at the sides. Not an A-pose statue.
        _idleGait = new EnemyGait(_rig, new EnemyGait.Style
        {
            Speed = 0f, Cycle = IdleCycle, Duty = 0.5f, Stance = leg * 0.07f, Crouch = leg * 0.05f, Bob = leg * 0.006f, Sway = leg * 0.022f,
            PelvisYaw = 2f, PelvisRoll = 1.2f, Lean = 7f, ArmIn = 19f, ArmSwing = 2f, ElbowBend = 42f, ElbowSwing = 4f
        });
        // Run: a flight phase between steps, hips lowest under the supporting leg, arms pumping.
        _runGait = new EnemyGait(_rig, new EnemyGait.Style
        {
            Speed = settings.RunClipSpeed, Cycle = RunCycle, Duty = Mathf.Clamp(leg * 0.80f / (settings.RunClipSpeed * RunCycle), 0.2f, 0.45f),
            ContactZ = leg * 0.27f, Crouch = leg * 0.10f, Bob = -leg * 0.03f, Sway = leg * 0.012f, SwingHeight = leg * 0.30f,
            SwingToesUp = 8f, ToeOffAngle = 20f, PelvisYaw = 7f, PelvisRoll = 3f, Lean = 14f, ArmIn = 21f, ArmSwing = 34f, ElbowBend = 68f, ElbowSwing = 22f
        });
        // Walk: always one foot down, hips highest over the supporting leg, the rear foot rolling onto its toes.
        _walkGait = new EnemyGait(_rig, new EnemyGait.Style
        {
            Speed = settings.WalkClipSpeed, Cycle = WalkCycle, Duty = Mathf.Clamp(leg * 0.74f / (settings.WalkClipSpeed * WalkCycle), 0.52f, 0.68f),
            ContactZ = leg * 0.36f, Crouch = leg * 0.07f, Bob = leg * 0.018f, Sway = leg * 0.03f, SwingHeight = leg * 0.12f,
            SwingToesUp = 6f, ToeOffAngle = 16f, PelvisYaw = 5f, PelvisRoll = 3.5f, Lean = 5f, ArmIn = 19f, ArmSwing = 18f, ElbowBend = 24f, ElbowSwing = 8f
        });
    }

    // Outline of the sole from the skinned vertices that follow the left foot and sit on the ground at rest:
    // how far the sole reaches ahead of and behind the ankle. The boots are much longer than the foot bones.
    private static void MeasureSole(Transform root, Transform[] bones, Vector3 ankle, out float tip, out float heel)
    {
        tip = 0.08f;
        heel = 0.04f;
        SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null || skin.sharedMesh == null) return;
        Mesh mesh = skin.sharedMesh;
        Transform[] skinBones = skin.bones;
        Matrix4x4[] bind = mesh.bindposes;
        BoneWeight[] weights = mesh.boneWeights;
        Vector3[] vertices = mesh.vertices;
        var foot = new List<Vector3>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int bone = weights[i].boneIndex0;
            string name = skinBones[bone].name;
            if (name != "mixamorig:LeftFoot" && name != "mixamorig:LeftToeBase" && name != "mixamorig:LeftToe_End") continue;
            Vector3 world = skinBones[bone].localToWorldMatrix.MultiplyPoint3x4(bind[bone].MultiplyPoint3x4(vertices[i]));
            foot.Add(root.InverseTransformPoint(world) * root.lossyScale.y);
        }
        if (foot.Count == 0) return;
        float ground = foot.Min(v => v.y);
        float front = float.MinValue, back = float.MaxValue;
        foreach (Vector3 v in foot)
        {
            if (v.y > ground + 0.05f) continue;   // only the part that touches the ground
            front = Mathf.Max(front, v.z);
            back = Mathf.Min(back, v.z);
        }
        if (front > float.MinValue)
        {
            tip = Mathf.Max(0.02f, front - ankle.z);
            heel = Mathf.Max(0.01f, ankle.z - back);
        }
    }

    public static EnemyGait.Rig Rig => _rig;

    public static string GaitReport()
    {
        if (_runGait == null) return "(gaits not built in this session)";
        return $"rig L thigh={_rig.Left.Thigh:F3} shin={_rig.Left.Shin:F3} ankle={_rig.Left.AnkleHeight:F3} | R thigh={_rig.Right.Thigh:F3} shin={_rig.Right.Shin:F3} ankle={_rig.Right.AnkleHeight:F3} | soleTip={_rig.SoleTip:F3} heelBack={_rig.HeelBack:F3} ballPivot={_rig.SoleForward:F3} hips={_rig.HipsCenter.y:F3} | " +
               $"run reach={_runGait.WorstReach:F3} liftOffZ={_runGait.LiftOffZ:F3} | walk reach={_walkGait.WorstReach:F3} liftOffZ={_walkGait.LiftOffZ:F3} | idle reach={_idleGait.WorstReach:F3}";
    }

    private static EnemyGait GaitFor(string state) => state == "Chase" ? _runGait : state == "Walk" ? _walkGait : _idleGait;

    private static AnimationClip BuildClip(string state, float duration, bool loop, Transform root, Transform model, Transform[] bones)
    {
        string path = Folder + "/" + state + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = state }; AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = 30f;
        int steps = Mathf.Max(8, Mathf.CeilToInt(duration * 30));
        foreach (Transform bone in bones)
        {
            string boneName = bone.name.Substring("mixamorig:".Length);
            string bonePath = AnimationUtility.CalculateTransformPath(bone, root);
            Quaternion rest = bone.localRotation;
            Quaternion axes = Quaternion.Inverse(bone.rotation) * model.rotation;
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            for (int frame = 0; frame <= steps; frame++)
            {
                float t = (float)frame / steps;
                Quaternion q = rest * axes * Pose(state, boneName, t) * Quaternion.Inverse(axes);
                curves[0].AddKey(t * duration, q.x); curves[1].AddKey(t * duration, q.y);
                curves[2].AddKey(t * duration, q.z); curves[3].AddKey(t * duration, q.w);
            }
            string[] components = { "x", "y", "z", "w" };
            for (int j = 0; j < 4; j++) clip.SetCurve(bonePath, typeof(Transform), "m_LocalRotation." + components[j], curves[j]);
            // Every clip also restores all bone positions, preventing pool/state pose leakage.
            Vector3 pos = bone.localPosition;
            for (int j = 0; j < 3; j++)
            {
                var positionCurve = AnimationCurve.Constant(0f, duration, pos[j]);
                if (boneName == "Hips")
                {
                    // The pelvis rides on the gait (crouch, bob, sway). Dying starts from the ready stance and drops.
                    positionCurve = new AnimationCurve();
                    for (int frame = 0; frame <= steps; frame++)
                    {
                        float t = (float)frame / steps;
                        Vector3 offset = HipsOffset(state, t, root);
                        positionCurve.AddKey(t * duration, pos[j] + bone.parent.InverseTransformVector(root.TransformDirection(offset))[j]);
                    }
                }
                clip.SetCurve(bonePath, typeof(Transform), "m_LocalPosition." + components[j], positionCurve);
            }
        }
        var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
        clipSettings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
        clip.EnsureQuaternionContinuity();
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // Pelvis offset in root space (metres).
    private static Vector3 HipsOffset(string state, float t, Transform root)
    {
        if (state == "Idle" || state == "Chase" || state == "Walk")
            return GaitFor(state).HipsOffset(t);
        Vector3 ready = _idleGait.HipsOffset(0f);
        if (state == "Dying")
        {
            float drop = root.GetComponent<BoxCollider>().size.y * root.lossyScale.y * 0.35f;
            return ready + Vector3.down * (drop * Mathf.SmoothStep(0f, 1f, t));
        }
        return ready;
    }

    // Provisional motion styling. Rotations are in model axes, converted to each bone's rest frame above.
    // Idle, Chase (run) and Walk come from EnemyGait; the one-shot states start from the ready stance so that
    // blending in and out of them does not pop.
    private static Quaternion Pose(string state, string bone, float t)
    {
        bool locomotion = state == "Idle" || state == "Chase" || state == "Walk";
        EnemyGait gait = GaitFor(state);
        float phase = locomotion ? t : 0f;
        Quaternion basePose = GaitPose(gait, bone, phase);
        if (locomotion)
        {
            if (state == "Idle")
            {
                // Breathing and a slow look-around on top of the stance.
                float breath = Mathf.Sin(t * Mathf.PI * 2f);
                if (bone == "Spine1") return basePose * Quaternion.Euler(breath * 1.6f, 0f, 0f);
                if (bone == "Head") return basePose * Quaternion.Euler(0f, Mathf.Sin(t * Mathf.PI * 2f + 0.8f) * 5f, 0f);
            }
            return basePose;
        }

        Vector3 extra = Vector3.zero;
        if (state == "Attack")
        {
            // Windup, contact exactly at the existing 50% hit check, recovery.
            float punch = t < 0.25f ? -0.25f * t / 0.25f : t < 0.5f ? Mathf.Lerp(-0.25f, 1f, (t - 0.25f) / 0.25f) : 1f - (t - 0.5f) / 0.5f;
            if (bone == "RightArm") extra = new Vector3(-85f * punch, 0f, -12f * Mathf.Max(0f, punch));
            if (bone == "RightForeArm") extra = new Vector3(38f * Mathf.Max(0f, punch), 0f, 0f);   // the bent guard arm straightens into the strike
            if (bone == "Spine") extra = new Vector3(10f * punch, 0f, 0f);
            if (bone == "Spine1") extra = new Vector3(0f, -22f * punch, 0f);
            if (bone == "LeftArm") extra = new Vector3(-12f * Mathf.Max(0f, punch), 0f, 0f);
        }
        else if (state == "Attack2")
        {
            // Left hook: the body winds to the left, then swings round to the right carrying the raised left arm
            // across. Contact at the same 50% as Attack.
            float swing = t < 0.25f ? -0.35f * t / 0.25f : t < 0.5f ? Mathf.Lerp(-0.35f, 1f, (t - 0.25f) / 0.25f) : 1f - (t - 0.5f) / 0.5f;
            float raised = Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 0.25f, (1f - t) / 0.4f));   // arm up through the swing
            // The arm starts out to the left and the turning body brings it round, so at contact it points at the target.
            if (bone == "LeftArm") extra = new Vector3(-70f * raised - 12f * Mathf.Max(0f, swing), raised * (-38f + 26f * Mathf.Max(0f, swing)), 10f * raised);
            if (bone == "LeftForeArm") extra = new Vector3(20f * Mathf.Max(0f, swing), 0f, 0f);
            if (bone == "Spine") extra = new Vector3(8f * Mathf.Max(0f, swing), 10f * swing, 0f);
            if (bone == "Spine1") extra = new Vector3(0f, 22f * swing, 0f);
            if (bone == "Head") extra = new Vector3(0f, -14f * swing, 0f);   // keeps looking at the target
            if (bone == "RightArm") extra = new Vector3(10f * Mathf.Max(0f, swing), 0f, -8f * raised);
        }
        else if (state == "Attack3")
        {
            // Overhead smash: both arms go up while the body leans back, then come down together.
            // Contact at the same 50% as Attack.
            float lift = t < 0.3f ? Mathf.SmoothStep(0f, 1f, t / 0.3f) : t < 0.5f ? 1f - Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.2f) : 0f;
            float held = Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 0.18f, (1f - t) / 0.45f));    // arms forward before and after the blow
            float down = t < 0.3f ? 0f : t < 0.5f ? Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.2f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.5f) / 0.5f);
            float arm = -(58f * held + 96f * lift);
            if (bone == "RightArm") extra = new Vector3(arm, 0f, -14f * held);
            if (bone == "LeftArm") extra = new Vector3(arm, 0f, 14f * held);
            if (bone == "RightForeArm" || bone == "LeftForeArm") extra = new Vector3(26f * down - 10f * lift, 0f, 0f);
            if (bone == "Spine") extra = new Vector3(20f * down - 10f * lift, 0f, 0f);
            if (bone == "Spine1") extra = new Vector3(12f * down - 8f * lift, 0f, 0f);
            if (bone == "Head") extra = new Vector3(-10f * down + 6f * lift, 0f, 0f);
        }
        else if (state == "Knockback")
        {
            float recoil = Mathf.Sin(Mathf.Min(1f, t * 4f) * Mathf.PI * 0.5f);
            if (bone == "Spine") extra = new Vector3(-30f * recoil, 0f, 0f);
            if (bone == "Head") extra = new Vector3(-12f * recoil, 0f, 0f);
            if (bone == "LeftArm") extra = new Vector3(0f, 0f, -40f * recoil);
            if (bone == "RightArm") extra = new Vector3(0f, 0f, 40f * recoil);
            if (bone == "LeftUpLeg" || bone == "RightUpLeg") extra = new Vector3(-20f * recoil, 0f, 0f);
            if (bone == "LeftLeg" || bone == "RightLeg") extra = new Vector3(30f * recoil, 0f, 0f);
        }
        else if (state == "Dying")
        {
            float collapse = Mathf.SmoothStep(0f, 1f, t);
            if (bone == "Hips") extra = new Vector3(0f, 0f, 85f * collapse);
            if (bone == "Spine") extra = new Vector3(-30f + 41f * collapse, 0f, 0f);
            if (bone == "Head") extra = new Vector3(-12f + 32f * collapse, 0f, 0f);
            if (bone == "LeftUpLeg" || bone == "RightUpLeg") extra = new Vector3(-20f - 20f * collapse, 0f, 0f);
            if (bone == "LeftLeg" || bone == "RightLeg") extra = new Vector3(30f + 25f * collapse, 0f, 0f);
        }
        return Quaternion.Euler(extra) * basePose;
    }

    private static Quaternion GaitPose(EnemyGait gait, string bone, float phase)
    {
        switch (bone)
        {
            case "Hips": return gait.PelvisRotation(phase);
            case "Spine": return gait.Spine(phase, 0.6f, 0.55f);
            case "Spine1": return gait.Spine(phase, 0.9f, 0.3f);
            case "Spine2": return gait.Spine(phase, 0f, 0.15f);
            case "Head": return gait.Head(phase);
            case "LeftArm": return gait.UpperArm(phase, true);
            case "RightArm": return gait.UpperArm(phase, false);
            case "LeftForeArm": return gait.ForeArm(phase, true);
            case "RightForeArm": return gait.ForeArm(phase, false);
        }
        bool left = bone.StartsWith("Left", StringComparison.Ordinal);
        if (bone == "LeftUpLeg" || bone == "RightUpLeg" || bone == "LeftLeg" || bone == "RightLeg" || bone == "LeftFoot" || bone == "RightFoot")
        {
            gait.SolveLeg(phase, left, out Quaternion thigh, out Quaternion knee, out Quaternion foot);
            if (bone.EndsWith("UpLeg", StringComparison.Ordinal)) return thigh;
            return bone.EndsWith("Foot", StringComparison.Ordinal) ? foot : knee;
        }
        return Quaternion.identity;
    }

    private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        string path = Folder + "/HumanoidEnemy.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var machine = controller.layers[0].stateMachine;
        foreach (string name in States)
        {
            AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = clips[name]; state.writeDefaultValues = true;
            if (name == "Idle") machine.defaultState = state;
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }

    [MenuItem("Tools/Game Demo Enemy/Validate Humanoid Enemy")]
    public static void ValidateAll()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null || skin.bones.Length < 20 || skin.sharedMesh.bindposes.Length == 0)
            throw new InvalidOperationException("Enemy has no valid skinned skeleton.");
        if (prefab.GetComponentsInChildren<Animator>(true).Length != 1 || prefab.GetComponent<Animator>().runtimeAnimatorController == null)
            throw new InvalidOperationException("Enemy must have one root Animator with a controller.");
        foreach (string name in States)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + name + ".anim");
            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length < 100) throw new InvalidOperationException(name + " has no skeletal motion.");
            foreach (var binding in bindings)
                if (prefab.transform.Find(binding.path) == null) throw new InvalidOperationException("Unresolved animation bone: " + binding.path);
        }
        Debug.Log("[Humanoid Enemy] Skin, single Animator and all skeletal curve paths validated.");
    }

    // Contact sheet for reviewing the generated motions without changing the open Scene.
    public static void BuildAndPreview() { BuildAll(); ExportPreview(); }

    [MenuItem("Tools/Game Demo Enemy/Export Humanoid Pose Preview")]
    public static void ExportPreview()
    {
        var sheet = new Texture2D(320 * States.Length, 800, TextureFormat.RGB24, false);
        // Where in each clip the picture is taken: a quarter into the loops, the contact of the one-shots, the end of Dying.
        float[] samples = States.Select(state => state == "Idle" || state == "Chase" || state == "Walk" ? 0.25f : state == "Dying" ? 1f : 0.5f).ToArray();
        try
        {
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < States.Length; i++)
            {
                var preview = new PreviewRenderUtility();
                var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                try
                {
                    instance.GetComponent<Animator>().enabled = false;
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + States[i] + ".anim");
                    clip.SampleAnimation(instance, clip.length * samples[i]);
                    preview.AddSingleGO(instance);
                    preview.camera.orthographic = true;
                    preview.camera.orthographicSize = 1.45f;
                    preview.camera.nearClipPlane = 0.01f;
                    preview.camera.farClipPlane = 50f;
                    preview.camera.transform.position = row == 0 ? new Vector3(3f, 1.7f, 7f) : new Vector3(7f, 1.7f, 0f);
                    preview.camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
                    preview.camera.clearFlags = CameraClearFlags.Color;
                    preview.camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
                    preview.lights[0].intensity = 1.3f;
                    preview.lights[0].transform.rotation = Quaternion.Euler(35f, -30f, 0f);
                    preview.lights[1].intensity = 0.8f;
                    preview.ambientColor = Color.gray;
                    preview.BeginStaticPreview(new Rect(0f, 0f, 320f, 400f));
                    preview.Render(true);
                    Texture2D image = preview.EndStaticPreview();
                    sheet.SetPixels(i * 320, (1 - row) * 400, 320, 400, image.GetPixels());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                finally { preview.Cleanup(); }
            }
            sheet.Apply();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllBytes("Logs/humanoid-enemy-poses.png", sheet.EncodeToPNG());
            Debug.Log("[Humanoid Enemy] Preview exported: " + string.Join(", ", States) + "; front and side rows.");
        }
        finally { UnityEngine.Object.DestroyImmediate(sheet); }
    }
}
