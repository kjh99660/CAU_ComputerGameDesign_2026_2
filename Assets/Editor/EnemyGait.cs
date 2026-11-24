using UnityEngine;

// Editor-only gait synthesis for the humanoid enemy clips.
// The feet are placed first: planted on the ground during stance and moving back at exactly the clip's reference
// speed. The leg joints are then solved from the feet (two-bone IK in the leg's plane), so a clip played at
// "real speed / reference speed" cannot slide by construction. Hips, spine, head and arms are layered on top.
// Angles returned here are model-space deltas (x = pitch, y = yaw, z = roll) from the rest pose.
public sealed class EnemyGait
{
    // Measured from the rig at rest, in world units with the gameplay root at the origin.
    // Each leg has its own numbers: the auto-generated rig is not perfectly symmetric.
    public struct Leg
    {
        public Vector3 Hip;            // hip joint
        public float Thigh;            // hip joint -> knee
        public float Shin;             // knee -> ankle
        public float RestThigh;        // forward angle of the thigh from straight down at rest (radians)
        public float RestShin;         // forward angle of the shin from straight down at rest (radians)
        public float AnkleHeight;      // ankle above the ground at rest
    }

    public struct Rig
    {
        public Leg Left, Right;
        public Vector3 HipsCenter;     // pelvis bone
        public float SoleForward;      // ball of the foot ahead of the ankle (the point the foot rolls over at lift-off)
        public float SoleTip;          // front end of the sole ahead of the ankle
        public float HeelBack;         // back end of the sole behind the ankle
        public float LegLength => Left.Thigh + Left.Shin;
    }

    public struct Style
    {
        public float Speed;            // ground speed the clip is authored for (m/s); 0 = standing
        public float Cycle;            // seconds for two steps
        public float Duty;             // fraction of the cycle each foot is on the ground
        public float ContactZ;         // ankle ahead of its hip joint at touch-down (m)
        public float Crouch;           // how much lower than rest the hips ride (m)
        public float Bob;              // vertical hip amplitude (m). > 0: highest at mid-stance (walk). < 0: lowest at mid-stance (run)
        public float Sway;             // lateral hip amplitude toward the stance leg (m)
        public float SwingHeight;      // ankle lift at the top of the swing (m)
        public float SwingToesUp;      // toes-up angle carried through the swing for ground clearance (degrees)
        public float ToeOffAngle;      // how far the foot rolls onto its toes before lift-off (degrees, positive)
        public float PelvisYaw;        // degrees
        public float PelvisRoll;       // degrees
        public float Lean;             // forward torso lean (degrees)
        public float ArmIn;            // brings the A-pose arms toward the body (degrees)
        public float ArmSwing;         // degrees
        public float ElbowBend;        // degrees
        public float ElbowSwing;       // extra elbow bend while the arm swings forward (degrees)
        public float Stance;           // standing only: half the fore/aft foot offset (m), left foot forward
    }

    private readonly Rig _rig;
    private readonly Style _style;

    // Largest distance the solver was asked to reach, as a fraction of the straight leg. Above 1 the foot target was
    // out of reach and had to be pulled in, which shows up as sliding; the builder reports it.
    public float WorstReach { get; private set; }

    public EnemyGait(Rig rig, Style style)
    {
        _rig = rig;
        _style = style;
    }

    public float LiftOffZ => _style.ContactZ - _style.Speed * _style.Duty * _style.Cycle;

    // ---------------------------------------------------------------- pelvis
    public Vector3 HipsOffset(float phase)
    {
        float midStance = _style.Duty * 0.5f;
        float bob = _style.Bob * Mathf.Cos(4f * Mathf.PI * (phase - midStance));
        float sway = -_style.Sway * Mathf.Cos(2f * Mathf.PI * (phase - midStance));   // toward the left (-X) at left mid-stance
        return new Vector3(sway, bob - _style.Crouch, 0f);
    }

    public Quaternion PelvisRotation(float phase)
    {
        float midStance = _style.Duty * 0.5f;
        float yaw = _style.PelvisYaw * Mathf.Cos(2f * Mathf.PI * phase);                   // the hip of the leading leg goes forward
        float roll = -_style.PelvisRoll * Mathf.Cos(2f * Mathf.PI * (phase - midStance));  // the swing side drops
        return Quaternion.Euler(0f, yaw, roll);
    }

    // ---------------------------------------------------------------- legs
    public void SolveLeg(float phase, bool left, out Quaternion thigh, out Quaternion knee, out Quaternion foot)
    {
        Leg leg = left ? _rig.Left : _rig.Right;
        float legPhase = Mathf.Repeat(left ? phase : phase + 0.5f, 1f);
        FootTarget(legPhase, left, leg.AnkleHeight, out float footZ, out float footY, out float toesUp);

        Quaternion pelvis = PelvisRotation(phase);
        Vector3 joint = _rig.HipsCenter + HipsOffset(phase) + pelvis * (leg.Hip - _rig.HipsCenter);

        // The ankle keeps its rest side offset, so the hips can sway over a foot that stays where it was planted.
        Vector3 ankle = new Vector3(leg.Hip.x, footY, leg.Hip.z + footZ);
        Vector3 reach = ankle - joint;
        float distance = reach.magnitude;
        float longest = (leg.Thigh + leg.Shin) * 0.997f;
        WorstReach = Mathf.Max(WorstReach, distance / (leg.Thigh + leg.Shin));
        if (distance > longest)
        {
            reach *= longest / distance;
            distance = longest;
        }

        // Leg plane: tilted sideways by 'side' so the ankle lands under its planted spot.
        float down = Mathf.Sqrt(reach.x * reach.x + reach.y * reach.y);
        float side = Mathf.Asin(Mathf.Clamp(reach.x / Mathf.Max(0.0001f, down), -1f, 1f));
        float toward = Mathf.Atan2(reach.z, down);
        float atHip = Mathf.Acos(Mathf.Clamp((leg.Thigh * leg.Thigh + distance * distance - leg.Shin * leg.Shin) / (2f * leg.Thigh * distance), -1f, 1f));
        float atKnee = Mathf.Acos(Mathf.Clamp((leg.Thigh * leg.Thigh + leg.Shin * leg.Shin - distance * distance) / (2f * leg.Thigh * leg.Shin), -1f, 1f));
        float thighForward = toward + atHip;                     // knee points forward
        float shinForward = thighForward - (Mathf.PI - atKnee);

        // Pitch about +X is positive when a hanging limb swings back, so forward angles are negated.
        float thighPitch = -(thighForward - leg.RestThigh) * Mathf.Rad2Deg;
        float kneePitch = ((thighForward - shinForward) - (leg.RestThigh - leg.RestShin)) * Mathf.Rad2Deg;
        float footPitch = (shinForward - leg.RestShin) * Mathf.Rad2Deg - toesUp;

        // The thigh undoes the pelvis turn so the leg stays in its own plane and the foot keeps pointing ahead.
        thigh = Quaternion.Inverse(pelvis) * Quaternion.Euler(0f, 0f, side * Mathf.Rad2Deg) * Quaternion.Euler(thighPitch, 0f, 0f);
        knee = Quaternion.Euler(kneePitch, 0f, 0f);
        foot = Quaternion.Euler(footPitch, 0f, -side * Mathf.Rad2Deg);
    }

    // Ankle position in the leg's fore/aft plane (z ahead of the rest hip joint, y above the ground) and foot pitch.
    private void FootTarget(float legPhase, bool left, float ankleHeight, out float z, out float y, out float toesUp)
    {
        if (_style.Speed <= 0f)
        {
            // Standing: both feet planted, one a little ahead of the other.
            z = left ? _style.Stance : -_style.Stance;
            y = ankleHeight;
            toesUp = 0f;
            return;
        }

        // contact / liftOff are where the ankle would be with the foot flat, i.e. a fixed spot of the sole on the ground.
        float contact = _style.ContactZ;
        float liftOff = LiftOffZ;
        if (legPhase < _style.Duty)
        {
            float u = legPhase / _style.Duty;
            float planted = Mathf.Lerp(contact, liftOff, u);   // straight line in time = constant ground speed = no slide
            float roll = _style.ToeOffAngle * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, u));
            RollOverToes(planted, roll, ankleHeight, out z, out y);
            toesUp = -roll;
            return;
        }

        float s = (legPhase - _style.Duty) / (1f - _style.Duty);
        RollOverToes(liftOff, _style.ToeOffAngle, ankleHeight, out float startZ, out float startY);
        // Leaves the ground and meets it again at stance speed, so the foot does not jerk forward or brake on contact.
        float tangent = (liftOff - contact) / _style.Duty * (1f - _style.Duty);
        float s2 = s * s, s3 = s2 * s;
        z = (2f * s3 - 3f * s2 + 1f) * startZ + (s3 - 2f * s2 + s) * tangent + (-2f * s3 + 3f * s2) * contact + (s3 - s2) * tangent;
        float arc = Mathf.Sin(Mathf.PI * Mathf.Pow(s, 0.85f));
        y = Mathf.Lerp(startY, ankleHeight, Mathf.SmoothStep(0f, 1f, s)) + _style.SwingHeight * arc * arc;
        // Toes come up for clearance, then the foot flattens to land on the whole sole.
        float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.45f, s));
        float flatten = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, s));
        toesUp = Mathf.Lerp(-_style.ToeOffAngle, _style.SwingToesUp, lift) * (1f - flatten);
    }

    // Ankle position while the foot rolls onto its toes by rollDegrees around the ball of the foot, which stays
    // planted. Soles are thick on this model, so ignoring this would drag the visible sole across the ground.
    private void RollOverToes(float flatAnkleZ, float rollDegrees, float ankleHeight, out float z, out float y)
    {
        float angle = rollDegrees * Mathf.Deg2Rad;
        z = flatAnkleZ + _rig.SoleForward * (1f - Mathf.Cos(angle)) + ankleHeight * Mathf.Sin(angle);
        y = ankleHeight * Mathf.Cos(angle) + _rig.SoleForward * Mathf.Sin(angle);
    }

    // ---------------------------------------------------------------- upper body
    // counterTurn (0..1) is how much of the pelvis turn this spine bone undoes, so the chest swings against the hips.
    public Quaternion Spine(float phase, float counterTurn, float leanShare)
    {
        Vector3 pelvis = Euler(PelvisRotation(phase));
        return Quaternion.Euler(_style.Lean * leanShare, -pelvis.y * counterTurn, -pelvis.z * counterTurn);
    }

    public Quaternion Head(float phase)
    {
        // Keeps the eyes level and forward over a leaning, turning chest.
        Vector3 pelvis = Euler(PelvisRotation(phase));
        return Quaternion.Euler(-_style.Lean * 0.75f, pelvis.y * 0.55f, 0f);
    }

    public Quaternion UpperArm(float phase, bool left)
    {
        // The arm swings against the leg of the same side: back (positive pitch) while that leg is forward.
        float swing = _style.ArmSwing * Mathf.Cos(2f * Mathf.PI * phase) * (left ? 1f : -1f);
        return Quaternion.Euler(swing, 0f, left ? _style.ArmIn : -_style.ArmIn);
    }

    public Quaternion ForeArm(float phase, bool left)
    {
        float forward = Mathf.Max(0f, -Mathf.Cos(2f * Mathf.PI * phase) * (left ? 1f : -1f));
        return Quaternion.Euler(-(_style.ElbowBend + _style.ElbowSwing * forward), 0f, 0f);
    }

    private static Vector3 Euler(Quaternion rotation)
    {
        Vector3 e = rotation.eulerAngles;
        return new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
    }
}
