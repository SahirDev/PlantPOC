using UnityEngine;

// Process movement after the camera controller updates the cursor state.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(CharacterController))]
public class CharacterMovementController : MonoBehaviour
{
    private Player playerOwner;
    private bool ControlBlocked => playerOwner != null && playerOwner.IsControlBlocked;

    [Header("Required References")]
    [SerializeField] private CharacterInputReader inputReader;
    [SerializeField] private CharacterViewStateMachine viewStateMachine;
    [SerializeField] private Animator animator;

    [Tooltip("Usually the Main Camera.")]
    [SerializeField] private Transform movementReference;

    [Header("Ground Movement")]
    [Tooltip("Walking movement speed in world units per second.")]
    [SerializeField, Min(0f)] private float walkingSpeed = 3f;
    [Tooltip("Running movement speed in world units per second while Run Action is held.")]
    [SerializeField, Min(0f)] private float runningSpeed = 6f;
    [SerializeField, Min(0f)] private float rotationSpeed = 12f;
    [SerializeField] private float gravity = -20f;

    [Tooltip("How quickly ground movement gains speed, in units per second squared.")]
    [SerializeField, Min(0.01f)] private float acceleration = 6f;
    [Tooltip("How quickly ground movement slows down, in units per second squared.")]
    [SerializeField, Min(0.01f)] private float deceleration = 10f;

    [Header("Jump / Crouch (walking)")]
    [Tooltip("Jump height in metres (Space). 0 = no jumping.")]
    [SerializeField, Min(0f)] private float jumpHeight = 1f;
    [Tooltip("Capsule height while crouching (hold Z), as a part of the standing height.")]
    [SerializeField, Range(0.3f, 1f)] private float crouchHeightFactor = 0.6f;
    [Tooltip("Speed while crouching, as a part of the walking speed.")]
    [SerializeField, Range(0.1f, 1f)] private float crouchSpeedFactor = 0.5f;

    [Header("Movement Diagnostics (Runtime)")]
    [SerializeField] private float measuredHorizontalSpeed;
    [SerializeField] private string movementStatus;
    public float HorizontalSpeed => measuredHorizontalSpeed;

    /// <summary>
    /// Measured world-space movement velocity in units per second, including Y.
    /// Updated in LateUpdate after movement, bounds and ladder pose restoration.
    /// Covers walking, falling, flight and ladders; excludes focus-camera placement.
    /// Read after this component's LateUpdate for the current frame's sample.
    /// Other reads return the most recently completed sample.
    /// </summary>
    public Vector3 Velocity => isActiveAndEnabled && !ControlBlocked && Time.deltaTime > 0f
        ? measuredVelocity : Vector3.zero;

    [Header("Flying")]
    [SerializeField, Min(0f)] private float flyingSpeed = 6f;

    [Header("Universal Movement Bounds")]
    [SerializeField] private bool useMovementBounds = true;

    [Tooltip("Assign an empty GameObject positioned at the center " + "of the movement boundary.")]
    [SerializeField] private Transform movementBoundsCenter;

    [Tooltip("Moves the bounds vertically relative to the center object.")]
    [SerializeField] private float movementBoundsYOffset = 0f;

    [Tooltip("Total width, height and depth of the boundary.")]
    [SerializeField]
    private Vector3 movementBoundsSize = new Vector3(200f, 100f, 200f);

    [Tooltip("Keeps the entire CharacterController capsule inside " + "the boundary.")]
    [SerializeField] private bool includeCharacterControllerSize = true;

    [Header("Bounds Debug")]
    [SerializeField] private bool drawBoundsGizmo = true;

    [SerializeField]
    private Color boundsGizmoColor = new Color(0f, 1f, 1f, 0.8f);

    private CharacterCameraController cameraController;
    // Drag To Look: always. Locked Cursor mode: only while the cursor is locked.
    private bool MovementInputAllowed => cameraController == null || cameraController.MovementAllowed;

    private float standingHeight;
    private Vector3 standingCenter;
    private bool isCrouching;
    private bool hasJumpingParameter;
    private bool hasCrouchingParameter;

    public bool IsCrouching => isCrouching;
    /// <summary>How far the camera goes down while crouching (metres).</summary>
    public float CrouchCameraDrop => isCrouching ? standingHeight * (1f - crouchHeightFactor) : 0f;

    private CharacterLadderController ladderController;
    private CharacterController characterController;
    private float verticalVelocity;
    private bool climbingAnimation;
    private Vector3 horizontalVelocity;
    private Vector3 frameStartPosition;
    private bool measureGroundMovement;
    private bool measureVelocity;
    private Vector3 measuredVelocity;

    private static readonly int WorkerSpeedHash = Animator.StringToHash("WorkerSpeed");
    private static readonly int IsClimbingHash = Animator.StringToHash("isClimbing");
    // Optional: add these Bool parameters (+ jump / crouch clips) to the Worker animator and they are driven.
    private static readonly int IsJumpingHash = Animator.StringToHash("isJumping");
    private static readonly int IsCrouchingHash = Animator.StringToHash("isCrouching");

    private void Awake()
    {
        playerOwner = GetComponent<Player>();
        cameraController = GetComponent<CharacterCameraController>();
        characterController = GetComponent<CharacterController>();
        // Acceleration starts with very small steps that must not be discarded.
        characterController.minMoveDistance = 0f;

        if (inputReader == null)
            inputReader = GetComponent<CharacterInputReader>();

        if (viewStateMachine == null)
        {
            viewStateMachine = GetComponent<CharacterViewStateMachine>();
        }

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        ladderController = GetComponent<CharacterLadderController>();
        if (ladderController != null) ladderController.Configure(this, animator);

        standingHeight = characterController.height;
        standingCenter = characterController.center;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Bool) continue;
                if (parameter.nameHash == IsJumpingHash) hasJumpingParameter = true;
                if (parameter.nameHash == IsCrouchingHash) hasCrouchingParameter = true;
            }
        }
    }

    private void OnEnable()
    {
        ResetMeasuredVelocity();
        ResetAnimation();
    }

    private void OnDisable()
    {
        ResetMeasuredVelocity();
        CancelLadderClimbing();
        ResetAnimation();
    }

    private void Start()
    {
        if (ControlBlocked) return;
        ConstrainCurrentPositionToBounds();
    }

    private void Update()
    {
        frameStartPosition = transform.position;
        measureVelocity = false;
        measureGroundMovement = false;
        if (ControlBlocked)
        {
            CancelLadderClimbing();
            movementStatus = "Player control is blocked or suspended";
            ResetAnimation();
            return;
        }
        if (viewStateMachine != null && viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FocusCam)
        {
            verticalVelocity = 0f;
            CancelLadderClimbing();
            movementStatus = "Focus mode owns movement";
            ResetAnimation();
            return; // Focus owns the player pose, including movement bounds.
        }

        if (characterController == null || !characterController.enabled || inputReader == null || !inputReader.isActiveAndEnabled)
        {
            CancelLadderClimbing();
            movementStatus = "Missing or disabled CharacterController / CharacterInputReader";
            ResetAnimation();
            return;
        }

        measureVelocity = true;
        if (viewStateMachine != null && viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FlyCam)
        {
            CancelLadderClimbing();
            SetCrouching(false); // in fly mode Z means "down"
            HandleFlying();
        }
        else
        {
            Vector2 input = MovementInputAllowed ? inputReader.MoveInput : Vector2.zero;
            bool ladderHandled = ladderController != null && ladderController.Tick(input,
                GetFlatCameraRelativeDirection(input), MovementInputAllowed, Time.deltaTime);
            if (ladderHandled)
            {
                SetCrouching(false);
                horizontalVelocity = Vector3.zero;
                verticalVelocity = 0f;
                movementStatus = ladderController.Status;
            }
            else HandleWalking();
        }

        ConstrainCurrentPositionToBounds();
        if (ladderController != null) ladderController.CaptureMotorPose();
    }

    internal void ResetForScene(Transform boundsCenter, Vector3 boundsSize)
    {
        ResetMeasuredVelocity();
        CancelLadderClimbing();
        verticalVelocity = 0f;
        ForceStand();
        movementBoundsCenter = boundsCenter;
        if (boundsCenter != null) movementBoundsSize = boundsSize;
        ResetAnimation();
    }

    private void HandleWalking()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            movementStatus = "Game time is paused";
            ResetAnimation();
            return;
        }

        measureGroundMovement = true;
        Vector2 input = MovementInputAllowed ? inputReader.MoveInput : Vector2.zero;
        Vector3 direction = GetFlatCameraRelativeDirection(input);

        // Hold Z = crouch (slower, lower). Stays crouched under a low ceiling until there is room.
        SetCrouching(MovementInputAllowed && inputReader.CrouchHeld);

        float targetSpeed = isCrouching ? walkingSpeed * crouchSpeedFactor
            : inputReader.RunHeld ? runningSpeed : walkingSpeed;
        Vector3 targetVelocity = direction * targetSpeed;

        // Releasing navigation control must stop motion immediately; gravity still runs.
        if (!MovementInputAllowed)
            horizontalVelocity = Vector3.zero;
        else
        {
            float rate = targetVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude
                ? acceleration : deceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity,
                Mathf.Max(0.01f, rate) * deltaTime);
        }

        if (horizontalVelocity.sqrMagnitude > 0.000001f)
            RotateTowards(horizontalVelocity);

        bool grounded = characterController.isGrounded;
        if (grounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        // Space = jump (only from the ground, not while crouching).
        if (MovementInputAllowed && grounded && !isCrouching && jumpHeight > 0f && gravity < 0f && inputReader.JumpPressed)
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);

        verticalVelocity += gravity * deltaTime;
        MoveWithinBounds((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);

        movementStatus = !MovementInputAllowed ? "Cursor is unlocked: navigation blocked"
            : input.sqrMagnitude < 0.000001f ? "No move input: idle or slowing down"
            : targetSpeed <= 0f ? "Movement speed is zero in the Inspector"
            : "Movement requested";

        if (hasJumpingParameter) animator.SetBool(IsJumpingHash, !characterController.isGrounded && verticalVelocity > 0f);
    }

    private void SetCrouching(bool crouch)
    {
        if (crouch == isCrouching) return;
        if (!crouch && !HasRoomToStand()) return;

        isCrouching = crouch;
        ApplyCapsuleHeight(crouch ? standingHeight * crouchHeightFactor : standingHeight);
    }

    private void ForceStand()
    {
        isCrouching = false;
        ApplyCapsuleHeight(standingHeight);
    }

    /// <summary>Changes the capsule height while keeping its bottom (the feet) in place.</summary>
    private void ApplyCapsuleHeight(float height)
    {
        if (characterController == null || standingHeight <= 0f) return;
        characterController.height = height;
        characterController.center = standingCenter - Vector3.up * ((standingHeight - height) * 0.5f);
        if (hasCrouchingParameter) animator.SetBool(IsCrouchingHash, isCrouching);
    }

    private bool HasRoomToStand()
    {
        float missing = standingHeight - characterController.height;
        if (missing <= 0.001f) return true;

        float radius = characterController.radius * 0.95f;
        Vector3 top = transform.TransformPoint(characterController.center + Vector3.up * (characterController.height * 0.5f - characterController.radius));
        return !Physics.SphereCast(top, radius, Vector3.up, out _, missing, ~0, QueryTriggerInteraction.Ignore);
    }

    private void LateUpdate()
    {
        if (ladderController != null) ladderController.FinishFrame();
        measuredVelocity = measureVelocity && !ControlBlocked && Time.deltaTime > 0f
            ? (transform.position - frameStartPosition) / Time.deltaTime
            : Vector3.zero;
        measureVelocity = false;
        // Measure this GameObject's final displacement, including bounds and animation
        // evaluation, rather than substituting input or a requested walk/run speed.
        if (!measureGroundMovement || ControlBlocked || Time.deltaTime <= 0f)
        {
            measuredHorizontalSpeed = 0f;
        }
        else
        {
            Vector3 displacement = transform.position - frameStartPosition;
            displacement.y = 0f; // Gravity and ladder motion do not drive locomotion.
            measuredHorizontalSpeed = displacement.magnitude / Time.deltaTime;
            if (movementStatus == "Movement requested")
                movementStatus = measuredHorizontalSpeed > 0.001f ? "Moving"
                    : "Requested movement produced no displacement: inspect collisions, bounds and transform writers";
        }

        SetWorkerSpeed(climbingAnimation ? 0f : measuredHorizontalSpeed);
    }

    private void ResetMeasuredVelocity()
    {
        measuredVelocity = Vector3.zero;
        measureVelocity = false;
    }

    private void HandleFlying()
    {
        movementStatus = "Fly mode";
        verticalVelocity = 0f;

        // Block horizontal and vertical flight together while the cursor is free.
        if (!MovementInputAllowed)
        {
            ResetAnimation();
            return;
        }

        Vector2 input = inputReader.MoveInput;
        Vector3 forward = movementReference != null ? movementReference.forward : transform.forward;
        Vector3 right = movementReference != null ? movementReference.right : transform.right;

        float verticalInput = 0f;

        if (inputReader.FlyUpHeld) verticalInput += 1f;   // Q or Space
        if (inputReader.FlyDownHeld) verticalInput -= 1f; // E or Z

        Vector3 movement = forward * input.y + right * input.x + Vector3.up * verticalInput;

        if (movement.sqrMagnitude > 1f) movement.Normalize();

        Vector3 flatDirection = Vector3.ProjectOnPlane(movement,Vector3.up);

        if (flatDirection.sqrMagnitude > 0.001f) RotateTowards(flatDirection);

        MoveWithinBounds(movement * flyingSpeed * Time.deltaTime);
        ResetAnimation();
    }

    public void CancelLadderClimbing()
    {
        if (ladderController != null && ladderController.IsClimbing)
        {
            ladderController.CancelClimb();
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }
    }

    internal void MoveForLadder(Vector3 movementDelta)
    {
        if (characterController != null && characterController.enabled && !ControlBlocked)
            MoveWithinBounds(movementDelta);
    }

    internal bool IsLadderPositionWithinBounds(Vector3 position)
    {
        return (ClampPositionToBounds(position) - position).sqrMagnitude < 0.000001f;
    }

    private void MoveWithinBounds(Vector3 movementDelta)
    {
        Vector3 currentPosition = transform.position;
        Vector3 requestedPosition = currentPosition + movementDelta;
        Vector3 allowedPosition = ClampPositionToBounds(requestedPosition);
        Vector3 allowedMovement = allowedPosition - currentPosition;

        characterController.Move(allowedMovement);
    }

    private Vector3 ClampPositionToBounds(Vector3 requestedPosition)
    {
        if (!useMovementBounds ||movementBoundsCenter == null)
        {
            return requestedPosition;
        }

        Bounds worldBounds = GetMovementBounds();

        Vector3 minimumPosition = worldBounds.min;
        Vector3 maximumPosition = worldBounds.max;

        if (includeCharacterControllerSize && characterController != null)
        {
            Vector3 lossyScale = transform.lossyScale;

            float horizontalScale = Mathf.Max(Mathf.Abs(lossyScale.x),Mathf.Abs(lossyScale.z));
            float verticalScale = Mathf.Abs(lossyScale.y);
            float radius = characterController.radius * horizontalScale;
            float halfHeight = Mathf.Max(characterController.height * verticalScale * 0.5f, radius);

            Vector3 scaledControllerCenter = Vector3.Scale(characterController.center,lossyScale);

            minimumPosition.x += radius - scaledControllerCenter.x;
            maximumPosition.x -= radius + scaledControllerCenter.x;
            minimumPosition.z += radius - scaledControllerCenter.z;
            maximumPosition.z -= radius + scaledControllerCenter.z;
            minimumPosition.y += halfHeight - scaledControllerCenter.y;
            maximumPosition.y -= halfHeight + scaledControllerCenter.y;
        }

        FixInvalidRange(ref minimumPosition.x,ref maximumPosition.x);
        FixInvalidRange(ref minimumPosition.y,ref maximumPosition.y);
        FixInvalidRange(ref minimumPosition.z,ref maximumPosition.z);

        requestedPosition.x = Mathf.Clamp(requestedPosition.x,minimumPosition.x,maximumPosition.x);
        requestedPosition.y = Mathf.Clamp(requestedPosition.y,minimumPosition.y,maximumPosition.y);
        requestedPosition.z = Mathf.Clamp(requestedPosition.z,minimumPosition.z,maximumPosition.z);

        return requestedPosition;
    }

    private void ConstrainCurrentPositionToBounds()
    {
        if (viewStateMachine != null && viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FocusCam)
            return;

        if (!useMovementBounds || movementBoundsCenter == null || characterController == null || !characterController.enabled)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 correctedPosition = ClampPositionToBounds(currentPosition);

        if ((correctedPosition - currentPosition).sqrMagnitude < 0.000001f)
        {
            return;
        }

        bool controllerWasEnabled =characterController.enabled;

        if (controllerWasEnabled) characterController.enabled = false;

        transform.position = correctedPosition;

        if (controllerWasEnabled) characterController.enabled = true;
    }

    private Bounds GetMovementBounds()
    {
        Vector3 validSize = new Vector3(Mathf.Abs(movementBoundsSize.x),Mathf.Abs(movementBoundsSize.y),Mathf.Abs(movementBoundsSize.z));
        Vector3 centerPosition = movementBoundsCenter != null ? movementBoundsCenter.position : transform.position;

        centerPosition.y += movementBoundsYOffset;

        return new Bounds(centerPosition,validSize);
    }

    private static void FixInvalidRange(ref float minimum,ref float maximum)
    {
        if (minimum <= maximum)
            return;

        float middle =(minimum + maximum) * 0.5f;

        minimum = middle;
        maximum = middle;
    }

    private Vector3 GetFlatCameraRelativeDirection(Vector2 input)
    {
        Vector3 forward = movementReference != null ? movementReference.forward : transform.forward;
        Vector3 right = movementReference != null ? movementReference.right : transform.right;

        forward.y = 0f;
        right.y = 0f;

        // Keep forward movement valid even if the reference looks straight up/down.
        if (forward.sqrMagnitude < 0.000001f)
        {
            right.Normalize();
            forward = Vector3.Cross(right, Vector3.up);
        }
        forward.Normalize();
        right = Vector3.Cross(Vector3.up, forward);

        Vector3 direction = forward * input.y + right * input.x;

        if (direction.sqrMagnitude > 1f) direction.Normalize();

        return direction;
    }

    private void RotateTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction,Vector3.up);

        transform.rotation = Quaternion.Slerp(transform.rotation,targetRotation,rotationSpeed * Time.deltaTime);
    }

    private void SetWorkerSpeed(float speed)
    {
        if (animator != null)
            animator.SetFloat(WorkerSpeedHash, speed);
    }

    // Changes Animator parameters only; ladder traversal is handled by CharacterLadderController.
    public void SetClimbingAnimation(bool climbing)
    {
        climbingAnimation = climbing;
        if (animator == null) return;
        animator.SetBool(IsClimbingHash, climbing);
        if (climbing) SetWorkerSpeed(0f);
    }

    private void ResetAnimation()
    {
        horizontalVelocity = Vector3.zero;
        measuredHorizontalSpeed = 0f;
        measureGroundMovement = false;
        SetWorkerSpeed(0f);
        SetClimbingAnimation(false);
    }

    private void OnDrawGizmos()
    {
        if (!drawBoundsGizmo || movementBoundsCenter == null)
        {
            return;
        }

        Vector3 validSize = new Vector3(Mathf.Abs(movementBoundsSize.x),Mathf.Abs(movementBoundsSize.y),Mathf.Abs(movementBoundsSize.z));
        Vector3 gizmoCenter = movementBoundsCenter.position;

        gizmoCenter.y += movementBoundsYOffset;

        Gizmos.color = boundsGizmoColor;
        Gizmos.DrawWireCube(gizmoCenter,validSize);
    }
}