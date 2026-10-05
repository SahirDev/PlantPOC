using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Optional player component. Ladders need only a trigger BoxCollider tagged Ladder.
/// Local +Y is bottom-to-top; local +Z is the outward-facing climbing side.
/// The movement controller remains the only caller that moves the capsule.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterMovementController), typeof(CharacterController))]
public sealed class CharacterLadderController : MonoBehaviour
{
    [Header("Detection")]
    [SerializeField] private string ladderTag = "Ladder";
    [SerializeField] private LayerMask ladderLayers = ~0;
    [SerializeField, Min(0.01f)] private float detectionDistance = 0.6f;
    [SerializeField, Range(-1f, 1f)] private float minimumApproachDot = 0.2f;
    [SerializeField, Min(0f)] private float faceGap = 0.08f;

    [Header("Climbing")]
    [SerializeField, Min(0.01f)] private float climbSpeed = 2f;
    [SerializeField, Min(0.01f)] private float alignmentSpeed = 3f;
    [SerializeField, Min(0.01f)] private float alignmentTimeout = 3f;
    [Tooltip("Optional UNANIMATED child pivot containing the mesh/skeleton, not the camera. Never assign the Player root.")]
    [SerializeField] private Transform visualPivot;
    [Tooltip("Use this to correct a model that does not face local +Z.")]
    [SerializeField] private Vector3 visualRotationOffset;
    [SerializeField, Min(0.01f)] private float alignmentDegreesPerSecond = 360f;

    [Header("Top Exit - No Landing Surface Required")]
    [Tooltip("Extra horizontal distance beyond the far top edge of the ladder, in world units.")]
    [SerializeField, Min(0f)] private float topForwardOffset = 0.4f;
    [FormerlySerializedAs("topClearance")]
    [Tooltip("Raises the bottom of the scaled CharacterController above the ladder's highest point, in world units.")]
    [SerializeField, Min(0f)] private float topExitHeight = 0.2f;
    [SerializeField, Min(0.01f)] private float exitSpeed = 2f;

    [Header("Vault Animation")]
    [Tooltip("Optional chest or shoulder bone. Leaving this unassigned keeps the original ladder animation behavior.")]
    [SerializeField] private Transform vaultReference;
    [Tooltip("Detection offset along the reference bone's local Y, including its transform scale. Does not move the bone.")]
    [SerializeField] private float localYOffset;

    [Header("Optional Animator Parameter")]
    [Tooltip("Optional Float for the climbing state Speed Multiplier. W/input up = +1, no input = 0, S/input down = -1. Independent of velocity. Empty disables it.")]
    [SerializeField] private string climbSpeedParameter = "ClimbSpeed";

    [Header("Runtime")]
    [SerializeField] private string ladderStatus = "Not climbing";
    public string Status => ladderStatus;
    public bool IsClimbing => phase != Phase.None;

    private enum Phase { None, Aligning, Climbing, Exiting }
    private Phase phase;
    private CharacterMovementController motor;
    private CharacterController capsule;
    private Animator animator;
    private CharacterViewStateMachine viewModes;
    private BoxCollider ladder;
    private BoxCollider lastLadder;
    private float retryAfter;
    private float nextDetection;
    private float alignmentElapsed;
    private float exitStalledTime;
    private Vector3 bottom, normal, up;
    private float length;
    private Vector3 alignTarget;
    private readonly Vector3[] exitTargets = new Vector3[2];
    private int exitIndex;
    private Quaternion savedVisualRotation;
    private Quaternion visualWorldRotation;
    private bool hasVisualSnapshot;
    private Vector3 motorPosition;
    private Quaternion motorRotation;
    private Vector3 animatorLocalPosition;
    private Quaternion animatorLocalRotation;
    private bool hasMotorPose;
    private int climbSpeedHash;
    private bool hasClimbSpeedParameter;
    private static readonly int IsVaultingHash = Animator.StringToHash("isVaulting");
    private bool hasVaultingParameter;
    private bool vaultLatched;
    private Player playerOwner;

    private void Awake()
    {
        capsule = GetComponent<CharacterController>();
        motor = GetComponent<CharacterMovementController>();
    }

    private void OnEnable()
    {
        playerOwner = GetComponent<Player>();
        if (playerOwner != null) playerOwner.OnControlSuspended.AddListener(CancelClimb);
        viewModes = GetComponent<CharacterViewStateMachine>();
        if (viewModes != null) viewModes.OnViewModeChanging += OnViewModeChanging;
    }

    private void OnDisable()
    {
        if (playerOwner != null) playerOwner.OnControlSuspended.RemoveListener(CancelClimb);
        if (viewModes != null) viewModes.OnViewModeChanging -= OnViewModeChanging;
        CancelClimb();
    }

    private void OnViewModeChanging(CharacterViewStateMachine.ViewMode oldMode,
        CharacterViewStateMachine.ViewMode newMode)
    {
        if (newMode == CharacterViewStateMachine.ViewMode.FlyCam ||
            newMode == CharacterViewStateMachine.ViewMode.FocusCam) CancelClimb();
    }

    internal void Configure(CharacterMovementController movement, Animator targetAnimator)
    {
        motor = movement;
        capsule = GetComponent<CharacterController>();
        animator = targetAnimator;
        hasClimbSpeedParameter = false;
        hasVaultingParameter = false;
        if (animator == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.nameHash == IsVaultingHash && parameter.type == AnimatorControllerParameterType.Bool)
                hasVaultingParameter = true;
        SetVaulting(false);
        if (!string.IsNullOrEmpty(climbSpeedParameter))
        {
            climbSpeedHash = Animator.StringToHash(climbSpeedParameter);
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.nameHash == climbSpeedHash && parameter.type == AnimatorControllerParameterType.Float)
                    hasClimbSpeedParameter = true;
        }
        // This relay leaves the Apply Root Motion setting untouched. Ground frames
        // use the Animator's normal root motion; climbing frames use the motor pose.
        CharacterLadderAnimatorRelay relay = animator.GetComponent<CharacterLadderAnimatorRelay>();
        if (relay == null) relay = animator.gameObject.AddComponent<CharacterLadderAnimatorRelay>();
        relay.Configure(this, animator);
    }

    internal bool Tick(Vector2 input, Vector3 approachDirection, bool inputAllowed, float deltaTime)
    {
        if (!isActiveAndEnabled) return false;
        if (!IsClimbing)
        {
            if (Time.time >= retryAfter && input.y <= 0.1f) lastLadder = null;
            if (!inputAllowed || input.y <= 0.1f || deltaTime <= 0f || Time.time < nextDetection)
                return false;
            nextDetection = Time.time + 0.1f;
            if (!TryEnter(approachDirection)) return false;
        }

        if (ladder == null || !ladder.enabled || !ladder.gameObject.activeInHierarchy || !RefreshGeometry(ladder))
        {
            CancelClimb();
            ladderStatus = "Ladder removed or invalid";
            return true;
        }

        // Write before Animator evaluation. Attachment selects the state; input
        // alone selects forward, paused or reverse playback, even when blocked.
        SetClimbPlaybackInput(inputAllowed && deltaTime > 0f ? input.y : 0f);
        if (!inputAllowed || deltaTime <= 0f)
        {
            ladderStatus = "Climbing paused: cursor released or game paused";
            CaptureMotorPose();
            return true;
        }

        // Reversing deliberately ends the latch; a moving animated bone does not.
        if (input.y < -0.1f) SetVaulting(false);
        UpdateVaultDetection(input.y);

        if (phase == Phase.Aligning)
        {
            alignmentElapsed += deltaTime;
            MoveTowards(alignTarget, alignmentSpeed, deltaTime);
            ladderStatus = "Aligning with ladder";
            if (Vector3.Distance(transform.position, alignTarget) < 0.035f)
                phase = Phase.Climbing;
            else if (alignmentElapsed > alignmentTimeout)
            {
                CancelClimb();
                ladderStatus = "Ladder alignment blocked";
            }
        }
        else if (phase == Phase.Exiting)
        {
            // S cancels an exit attempt and returns to the ladder's climbing path.
            if (input.y < -0.1f)
            {
                phase = Phase.Aligning;
                alignTarget = RootAtFeet(bottom + up * Mathf.Clamp(CurrentDistance(), 0f, length));
                alignmentElapsed = 0f;
            }
            else TickExit(deltaTime);
        }
        else
        {
            float distance = Mathf.Clamp(CurrentDistance(), 0f, length);
            if (input.y > 0.1f && distance >= length - 0.035f)
            {
                PrepareTopExit();
                phase = Phase.Exiting;
                exitIndex = 0;
                exitStalledTime = 0f;
                TickExit(deltaTime);
            }
            else if (input.y < -0.1f && distance <= 0.035f)
            {
                CancelClimb();
                ladderStatus = "Left ladder at bottom";
            }
            else
            {
                float nextDistance = Mathf.Clamp(distance + input.y * climbSpeed * deltaTime, 0f, length);
                Vector3 target = RootAtFeet(bottom + up * nextDistance);
                motor.MoveForLadder(target - transform.position);
                ladderStatus = Mathf.Abs(input.y) > 0.1f ? "Climbing" : "Holding ladder";
            }
        }
        if (IsClimbing)
        {
            // Include this frame's collision-aware climb displacement in detection.
            UpdateVaultDetection(input.y);
            CaptureMotorPose();
        }
        return true;
    }

    private bool TryEnter(Vector3 approachDirection)
    {
        GetCapsule(transform.position, out Vector3 lower, out Vector3 upper, out float radius);
        Collider[] nearby = Physics.OverlapCapsule(lower, upper, radius + detectionDistance,
            ladderLayers, QueryTriggerInteraction.Collide);
        BoxCollider best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Collider candidate in nearby)
        {
            BoxCollider box = candidate as BoxCollider;
            if (box == null || !box.isTrigger || IsSelf(box) || box.tag != ladderTag || box == lastLadder)
                continue;
            if (!RefreshGeometry(box)) continue;
            Vector3 feet = FeetPosition();
            float along = Vector3.Dot(feet - bottom, up);
            if (along < -detectionDistance || along > length + detectionDistance) continue;
            Vector3 local = box.transform.InverseTransformPoint(feet);
            // Only the configured +Z face is climbable.
            if (local.z < box.center.z) continue;
            Vector3 towardFace = Vector3.ProjectOnPlane(-normal, Vector3.up);
            if (towardFace.sqrMagnitude > 0.001f &&
                Vector3.Dot(approachDirection.normalized, towardFace.normalized) < minimumApproachDot)
                continue;
            Vector3 attach = RootAtFeet(bottom + up * Mathf.Clamp(along, 0f, length));
            float distance = Vector3.Distance(transform.position, attach);
            if (distance > detectionDistance + radius || distance >= bestDistance) continue;
            bestDistance = distance;
            best = box;
        }
        if (best == null) return false;
        ladder = best;
        RefreshGeometry(ladder);
        alignTarget = RootAtFeet(bottom + up * Mathf.Clamp(CurrentDistance(), 0f, length));
        phase = Phase.Aligning;
        alignmentElapsed = 0f;
        if (visualPivot != null && visualPivot != transform && visualPivot.IsChildOf(transform))
        {
            savedVisualRotation = visualPivot.localRotation;
            visualWorldRotation = visualPivot.rotation;
            hasVisualSnapshot = true;
        }
        SetVaulting(false);
        motor.SetClimbingAnimation(true);
        CaptureMotorPose();
        return true;
    }

    private bool RefreshGeometry(BoxCollider box)
    {
        Vector3 half = box.size * 0.5f;
        if (half.x <= 0f || half.y <= 0f || half.z <= 0f) return false;
        Vector3 a = box.transform.TransformPoint(box.center + new Vector3(0f, -half.y, half.z));
        Vector3 b = box.transform.TransformPoint(box.center + new Vector3(0f, half.y, half.z));
        length = Vector3.Distance(a, b);
        if (length < 0.001f) return false;
        up = (b - a) / length;
        normal = box.transform.forward;
        float standOff = WorldRadius() + faceGap;
        bottom = a + normal * standOff;
        return true;
    }

    private float CurrentDistance() => Vector3.Dot(FeetPosition() - bottom, up);

    private void PrepareTopExit()
    {
        // All exit geometry comes from this ladder and the player's scaled capsule.
        // There is deliberately no ground ray, landing layer or platform requirement.
        Vector3 feet = FeetPosition();
        float raisedY = Mathf.Max(feet.y, ladder.bounds.max.y) + Mathf.Max(0f, topExitHeight);
        Vector3 raisedFeet = new Vector3(feet.x, raisedY, feet.z);

        Vector3 forward = Vector3.ProjectOnPlane(-normal, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(up, Vector3.up);
        forward.Normalize();

        // Cross the actual box thickness, not a guessed distance to external mesh.
        Vector3 half = ladder.size * 0.5f;
        Vector3 farTopEdge = ladder.transform.TransformPoint(
            ladder.center + new Vector3(0f, half.y, -half.z));
        Vector3 overFeet = farTopEdge + forward *
            (WorldRadius() + faceGap + Mathf.Max(0f, topForwardOffset));
        overFeet.y = raisedY;

        // Store feet positions: convert to the root using the current capsule offset
        // each tick, so rotation/scale do not confuse the root with the player's feet.
        exitTargets[0] = raisedFeet;
        exitTargets[1] = overFeet;
    }

    private void TickExit(float deltaTime)
    {
        Vector3 target = RootAtFeet(exitTargets[exitIndex]);
        float beforeDistance = Vector3.Distance(transform.position, target);
        MoveTowards(target, exitSpeed, deltaTime);
        float afterDistance = Vector3.Distance(transform.position, target);
        if (afterDistance < 0.01f)
        {
            exitIndex++;
            exitStalledTime = 0f;
            if (exitIndex == exitTargets.Length)
            {
                CancelClimb();
                ladderStatus = "Top exit complete: normal movement and gravity resumed";
            }
        }
        else
        {
            // Move still respects solid collisions and movement bounds. If those
            // prevent progress, release instead of leaving the player attached forever.
            float minimumProgress = Mathf.Min(0.001f, exitSpeed * deltaTime * 0.05f);
            exitStalledTime = beforeDistance - afterDistance <= minimumProgress
                ? exitStalledTime + deltaTime : 0f;
            if (exitStalledTime >= 0.75f)
            {
                CancelClimb();
                ladderStatus = "Exit movement obstructed: released ladder, normal gravity resumed";
            }
            else ladderStatus = exitIndex == 0 ? "Raising feet above ladder" : "Moving over ladder";
        }
    }

    private void MoveTowards(Vector3 target, float speed, float deltaTime)
    {
        Vector3 next = Vector3.MoveTowards(transform.position, target, speed * deltaTime);
        motor.MoveForLadder(next - transform.position);
    }

    private bool IsSelf(Collider item) => item.transform == transform || item.transform.IsChildOf(transform);
    private float WorldRadius() => capsule.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
    private float WorldHalfHeight() => Mathf.Max(WorldRadius(), capsule.height * Mathf.Abs(transform.lossyScale.y) * 0.5f);
    private Vector3 CenterOffset() => transform.TransformVector(capsule.center);
    private Vector3 FeetPosition() => transform.position + CenterOffset() - Vector3.up * WorldHalfHeight();
    private Vector3 RootAtFeet(Vector3 feet) => feet - CenterOffset() + Vector3.up * WorldHalfHeight();

    private void GetCapsule(Vector3 root, out Vector3 lower, out Vector3 upper, out float radius)
    {
        radius = WorldRadius();
        Vector3 center = root + CenterOffset();
        float segment = WorldHalfHeight() - radius;
        lower = center - Vector3.up * segment;
        upper = center + Vector3.up * segment;
    }

    // Called after the motor has applied collision-aware movement and bounds.
    internal void CaptureMotorPose()
    {
        if (!IsClimbing) return;
        motorPosition = transform.position;
        motorRotation = transform.rotation;
        if (animator != null && animator.transform != transform)
        {
            animatorLocalPosition = animator.transform.localPosition;
            animatorLocalRotation = animator.transform.localRotation;
        }
        hasMotorPose = true;
    }

    internal void RestoreMotorPoseAfterAnimation(bool restoreRotation = true)
    {
        if (!IsClimbing || !hasMotorPose) return;
        // Restore only the pose already reached through CharacterController.Move.
        // This prevents animation root/transform curves from adding a second move.
        if ((transform.position - motorPosition).sqrMagnitude > 0.00000001f)
            transform.position = motorPosition;
        if (restoreRotation && Quaternion.Angle(transform.rotation, motorRotation) > 0.001f)
            transform.rotation = motorRotation;
        if (animator != null && animator.transform != transform)
        {
            animator.transform.localPosition = animatorLocalPosition;
            animator.transform.localRotation = animatorLocalRotation;
        }
    }

    internal void FinishFrame()
    {
        if (!IsClimbing) return;
        // CameraController runs its look update before this LateUpdate. Preserve
        // that camera-facing root rotation while restoring any animation displacement.
        RestoreMotorPoseAfterAnimation(false);
        if (hasVisualSnapshot && visualPivot != null)
        {
            Quaternion target = Quaternion.LookRotation(-normal, up) * Quaternion.Euler(visualRotationOffset);
            visualWorldRotation = Quaternion.RotateTowards(visualWorldRotation, target,
                alignmentDegreesPerSecond * Time.deltaTime);
            visualPivot.rotation = visualWorldRotation;
        }
    }

    private void SetClimbPlaybackInput(float verticalInput)
    {
        if (!hasClimbSpeedParameter || animator == null) return;
        float playbackSpeed = verticalInput > 0.1f ? 1f : verticalInput < -0.1f ? -1f : 0f;
        animator.SetFloat(climbSpeedHash, playbackSpeed);
    }

    private Vector3 VaultDetectionPoint() =>
        vaultReference.TransformPoint(new Vector3(0f, localYOffset, 0f));

    private void UpdateVaultDetection(float verticalInput)
    {
        if (vaultLatched || vaultReference == null || ladder == null ||
            verticalInput <= 0.1f || (phase != Phase.Climbing && phase != Phase.Exiting)) return;

        // Compare in collider local space: +Y is the climbing axis. This uses the
        // actual top plane, not the world-axis-aligned bounds, even under scale.
        Vector3 localPoint = ladder.transform.InverseTransformPoint(VaultDetectionPoint());
        float localTop = ladder.center.y + ladder.size.y * 0.5f;
        if (localPoint.y >= localTop) SetVaulting(true);
    }

    private void SetVaulting(bool vaulting)
    {
        vaultLatched = vaulting;
        if (animator != null && hasVaultingParameter)
            animator.SetBool(IsVaultingHash, vaulting);
    }

    private void OnDrawGizmos()
    {
        if (vaultReference == null) return;
        Vector3 point = VaultDetectionPoint();
        Gizmos.color = vaultLatched ? Color.green : Color.yellow;
        Gizmos.DrawLine(vaultReference.position, point);
        Gizmos.DrawWireSphere(point, 0.05f);
    }

    public void CancelClimb()
    {
        // Clear parameters even on repeated cancellation or while already detached.
        SetVaulting(false);
        if (motor != null) motor.SetClimbingAnimation(false);
        if (!IsClimbing) return;
        lastLadder = ladder;
        retryAfter = Time.time + 0.5f;
        phase = Phase.None;
        ladder = null;
        hasMotorPose = false;
        if (hasVisualSnapshot && visualPivot != null) visualPivot.localRotation = savedVisualRotation;
        hasVisualSnapshot = false;
        SetClimbPlaybackInput(0f);
        ladderStatus = "Not climbing";
    }
}
