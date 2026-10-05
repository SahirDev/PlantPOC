using UnityEngine;

public class CharacterViewModeEffects : MonoBehaviour
{
    private Player playerOwner;
    private bool ControlBlocked => playerOwner != null && playerOwner.IsControlBlocked;

    [Header("Required References")]
    [SerializeField]
    private CharacterViewStateMachine viewStateMachine;

    [SerializeField]
    private CharacterController characterController;

    [SerializeField]
    private CharacterCameraController cameraController;

    [Tooltip("The player root that will be moved.")]
    [SerializeField]
    private Transform playerRoot;

    [Header("Meshes Hidden In FPP And Fly Cam")]
    [SerializeField]
    private SkinnedMeshRenderer[] skinnedMeshRenderers;

    [Header("Ground Teleport Settings")]
    [Tooltip(
        "The first collider hit must be on one of these layers. " +
        "Otherwise, the player returns to the saved position."
    )]
    [SerializeField]
    private LayerMask teleportGroundLayers;

    [Tooltip("How far above the player the ray begins.")]
    [SerializeField, Min(0f)]
    private float rayStartHeight = 2f;

    [Tooltip("Maximum distance checked below the player.")]
    [SerializeField, Min(0.01f)]
    private float raycastDistance = 1000f;

    [Tooltip("Optional space between the player pivot and the ground.")]
    [SerializeField, Min(0f)]
    private float groundClearance = 0f;

    [Header("Debug")]
    [SerializeField]
    private bool drawDebugRay = true;

    [SerializeField]
    private Vector3 savedPosition;

    [SerializeField]
    private bool hasSavedPosition;

    private CharacterViewStateMachine.ViewMode previousMode;
    private bool[] originalRendererStates;
    private float originalHeight;
    private float originalRadius;
    private float originalStepOffset;
    private Vector3 originalCenter;
    private bool hasCachedControllerSettings;
    private bool flyColliderApplied;
    private bool hasStarted;
    private Animator[] flightAnimators;
    private bool[] flightAnimatorEnabledStates;
    private bool flightAnimationSuspended;

    private void Awake()
    {
        playerOwner = GetComponent<Player>();
        if (viewStateMachine == null)
            viewStateMachine = GetComponent<CharacterViewStateMachine>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (playerRoot == null)
            playerRoot = transform;

        if (cameraController == null)
            cameraController = GetComponent<CharacterCameraController>();

        // Cache once at startup, before any mode event can resize the controller.
        CacheOriginalControllerSettings();
        CacheOriginalRendererStates();
    }

    private void OnEnable()
    {
        if (viewStateMachine != null)
        {
            viewStateMachine.OnViewModeChanged += HandleViewModeChanged;

            if (hasStarted)
            {
                HandleViewModeChanged(viewStateMachine.CurrentMode);
                if (viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FlyCam)
                    ApplyFlyCollider();
            }
        }
    }

    private void Start()
    {
        if (viewStateMachine == null)
            return;

        hasStarted = true;
        HandleViewModeChanged(viewStateMachine.CurrentMode);
    }

    private void OnDisable()
    {
        if (viewStateMachine != null)
        {
            viewStateMachine.OnViewModeChanged -= HandleViewModeChanged;
        }

        RestoreOriginalControllerSettings();
        RestoreFlightAnimators();
        RestoreOriginalRendererStates();
    }

    private void HandleViewModeChanged(CharacterViewStateMachine.ViewMode newMode)
    {
        // Start synchronizes the final initial mode after all Awake calls finish.
        if (!hasStarted || ControlBlocked)
            return;

        bool enteringFlyCam = previousMode != CharacterViewStateMachine.ViewMode.FlyCam && newMode == CharacterViewStateMachine.ViewMode.FlyCam;
        bool leavingFlyCam = previousMode == CharacterViewStateMachine.ViewMode.FlyCam && newMode != CharacterViewStateMachine.ViewMode.FlyCam;

        if (enteringFlyCam)
        {
            SavePlayerPosition();
            ApplyFlyCollider();
        }

        if (leavingFlyCam)
        {
            if (newMode == CharacterViewStateMachine.ViewMode.FocusCam)
                RestoreOriginalControllerSettings();
            else
                MovePlayerToGroundOrSavedPosition();
        }
        // Focus exit restores the saved pose itself; do not ground-teleport it.

        if (newMode == CharacterViewStateMachine.ViewMode.FlyCam)
            SuspendFlightAnimators();
        else
            RestoreFlightAnimators();

        UpdateRendererVisibility(newMode);
        previousMode = newMode;
    }

    private void CacheOriginalControllerSettings()
    {
        if (characterController == null || hasCachedControllerSettings)
            return;

        originalHeight = characterController.height;
        originalRadius = characterController.radius;
        originalCenter = characterController.center;
        originalStepOffset = characterController.stepOffset;
        hasCachedControllerSettings = true;
    }

    private void ApplyFlyCollider()
    {
        if (characterController == null || !hasCachedControllerSettings)
            return;

        if (cameraController == null)
        {
            Debug.LogWarning("Assign a CharacterCameraController in CharacterViewModeEffects.", this);
            return;
        }

        bool wasEnabled = characterController.enabled;
        characterController.enabled = false;

        // SphereCast radius is in world units; controller dimensions are local.
        Vector3 scale = characterController.transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float verticalScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float radius = Mathf.Max(0.01f, cameraController.CameraCollisionRadius);

        // Avoid a step offset larger than the reduced capsule.
        characterController.stepOffset = 0f;
        characterController.radius = radius / horizontalScale;
        characterController.height = 2f * radius / verticalScale;

        if (cameraController.FlyCameraPosition != null)
        {
            characterController.center = characterController.transform.InverseTransformPoint(
                cameraController.FlyCameraPosition.position);
        }

        flyColliderApplied = true;
        characterController.enabled = wasEnabled;
    }

    private void RestoreOriginalControllerSettings()
    {
        if (characterController == null || !hasCachedControllerSettings || !flyColliderApplied)
            return;

        bool wasEnabled = characterController.enabled;
        characterController.enabled = false;
        characterController.height = originalHeight;
        characterController.radius = originalRadius;
        characterController.center = originalCenter;
        characterController.stepOffset = originalStepOffset;
        flyColliderApplied = false;
        characterController.enabled = wasEnabled;
    }

    public void SavePlayerPosition()
    {
        if (ControlBlocked) return;
        if (playerRoot == null)
            return;

        savedPosition = playerRoot.position;
        hasSavedPosition = true;
    }

    public bool MovePlayerToGroundOrSavedPosition()
    {
        if (ControlBlocked) return false;
        if (playerRoot == null)
            return false;

        Vector3 rayOrigin = playerRoot.position + Vector3.up * rayStartHeight;

        float totalRayDistance = rayStartHeight + raycastDistance;

        if (drawDebugRay)
        {
            Debug.DrawRay(rayOrigin,Vector3.down * totalRayDistance,Color.green,2f);
        }

        bool controllerWasEnabled = characterController != null && characterController.enabled;

        // Prevent the ray from hitting the CharacterController.
        if (controllerWasEnabled)
            characterController.enabled = false;

        // Restore while disabled, before the landing raycast and teleport.
        RestoreOriginalControllerSettings();

        // No layer filter is used here because the ray must stop
        // at the first collider, including an invalid collider.
        bool hitSomething = Physics.Raycast(rayOrigin,Vector3.down,out RaycastHit hit,totalRayDistance,~0,QueryTriggerInteraction.Ignore);
        bool hitAllowedGround = hitSomething && IsLayerAllowed(hit.collider.gameObject.layer);

        if (hitAllowedGround)
        {
            Vector3 targetPosition = playerRoot.position;

            targetPosition.y = hit.point.y + groundClearance;

            playerRoot.position = targetPosition;
        }
        else
        {
            ReturnToSavedPosition();

            if (hitSomething)
            {
                Debug.LogWarning($"{nameof(CharacterViewModeEffects)}: " + $"The first object hit, '{hit.collider.name}', " + "is not on an allowed ground layer. " + "The player was returned to the saved position.", hit.collider);
            }
            else
            {
                Debug.LogWarning($"{nameof(CharacterViewModeEffects)}: " + "No object was found below the player. " + "The player was returned to the saved position.",this);
            }
        }

        if (controllerWasEnabled) characterController.enabled = true;

        return hitAllowedGround;
    }

    public void ReturnToSavedPosition()
    {
        if (ControlBlocked) return;
        if (!hasSavedPosition || playerRoot == null)
            return;

        playerRoot.position = savedPosition;
    }

    internal Vector3 CaptureGroundReturnPosition() => hasSavedPosition ? savedPosition : playerRoot.position;
    internal void RestoreGroundReturnPosition(Vector3 position) { savedPosition = position; hasSavedPosition = true; }

    internal void ResetForScene(CharacterViewStateMachine.ViewMode mode)
    {
        RestoreOriginalControllerSettings();
        savedPosition = playerRoot.position;
        hasSavedPosition = true;
        previousMode = mode;
        if (mode == CharacterViewStateMachine.ViewMode.FlyCam)
        {
            ApplyFlyCollider();
            SuspendFlightAnimators();
        }
        else RestoreFlightAnimators();
        UpdateRendererVisibility(mode);
    }

    // Hidden character animations must not move the player/camera during flight.
    // Cache at entry, not Awake, so intentionally disabled Animators stay disabled.
    private void SuspendFlightAnimators()
    {
        if (flightAnimationSuspended) return;
        Transform root = playerRoot != null ? playerRoot : transform;
        flightAnimators = root.GetComponentsInChildren<Animator>(true);
        flightAnimatorEnabledStates = new bool[flightAnimators.Length];
        for (int i = 0; i < flightAnimators.Length; i++)
        {
            Animator item = flightAnimators[i];
            if (item == null) continue;
            flightAnimatorEnabledStates[i] = item.enabled;
            item.enabled = false;
        }
        flightAnimationSuspended = true;
    }

    private void RestoreFlightAnimators()
    {
        if (!flightAnimationSuspended) return;
        for (int i = 0; i < flightAnimators.Length; i++)
            if (flightAnimators[i] != null)
                flightAnimators[i].enabled = flightAnimatorEnabledStates[i];
        flightAnimators = null;
        flightAnimatorEnabledStates = null;
        flightAnimationSuspended = false;
    }

    private bool IsLayerAllowed(int objectLayer)
    {
        int objectLayerMask = 1 << objectLayer;

        return (teleportGroundLayers.value & objectLayerMask) != 0;
    }

    private void UpdateRendererVisibility(CharacterViewStateMachine.ViewMode mode)
    {
        bool hideCharacter = mode == CharacterViewStateMachine.ViewMode.FPP || mode == CharacterViewStateMachine.ViewMode.FlyCam || mode == CharacterViewStateMachine.ViewMode.FocusCam;

        if (skinnedMeshRenderers == null)
            return;

        for (int i = 0; i < skinnedMeshRenderers.Length; i++)
        {
            SkinnedMeshRenderer meshRenderer = skinnedMeshRenderers[i];

            if (meshRenderer == null)
                continue;

            if (hideCharacter)
            {
                meshRenderer.enabled = false;
            }
            else
            {
                bool originalState = originalRendererStates != null && i < originalRendererStates.Length ? originalRendererStates[i]: true;
                meshRenderer.enabled = originalState;
            }
        }
    }

    private void CacheOriginalRendererStates()
    {
        if (skinnedMeshRenderers == null)
        {
            originalRendererStates = new bool[0];
            return;
        }

        originalRendererStates =
            new bool[skinnedMeshRenderers.Length];

        for (int i = 0;
             i < skinnedMeshRenderers.Length;
             i++)
        {
            if (skinnedMeshRenderers[i] != null)
            {
                originalRendererStates[i] =
                    skinnedMeshRenderers[i].enabled;
            }
        }
    }

    private void RestoreOriginalRendererStates()
    {
        if (skinnedMeshRenderers == null ||
            originalRendererStates == null)
        {
            return;
        }

        int rendererCount = Mathf.Min(
            skinnedMeshRenderers.Length,
            originalRendererStates.Length
        );

        for (int i = 0; i < rendererCount; i++)
        {
            if (skinnedMeshRenderers[i] != null)
            {
                skinnedMeshRenderers[i].enabled =
                    originalRendererStates[i];
            }
        }
    }
}