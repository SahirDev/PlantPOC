using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class CharacterCameraController : MonoBehaviour
{
    private Player playerOwner;
    private bool ControlBlocked => playerOwner != null && playerOwner.IsControlBlocked;

    public enum CursorInputMode
    {
        Toggle, HoldToShow

    }

    /// <summary>
    /// DragToLook (default, best for web + React UI): the cursor is always visible and free, WASD always
    /// moves, hold the right mouse button and drag to look around, left click selects.
    /// LockedCursor (old behaviour): right click toggles a locked cursor; move/look only while locked.
    /// </summary>
    public enum LookMode
    {
        DragToLook, LockedCursor
    }

    [Header("Required References")]
    [SerializeField] private CharacterInputReader inputReader;
    [SerializeField] private CharacterViewStateMachine viewStateMachine;
    [SerializeField] private Transform characterRoot;
    [SerializeField] private Camera playerCamera;

    [Header("Camera Positions")]
    [SerializeField] private Transform fppPosition;
    [SerializeField] private Transform tppPosition;

    [Header("Look Settings")]
    [SerializeField, Min(0f)] private float lookSensitivity = 0.1f;
    [SerializeField, Min(0f)] private float lookSmoothTime = 0.04f;
    [SerializeField, Min(0f)] private float mouseDeadZone = 0.01f;
    [SerializeField] private bool invertVerticalLook;

    [Header("TPP Pitch Limits")]
    [SerializeField] private float tppMinimumPitch = -40f;
    [SerializeField] private float tppMaximumPitch = 70f;

    [Header("FPP Pitch Limits")]
    [SerializeField] private float fppMinimumPitch = -80f;
    [SerializeField] private float fppMaximumPitch = 80f;

    [Header("Fly Cam Pitch Limits")]
    [SerializeField] private float flyMinimumPitch = -89f;
    [SerializeField] private float flyMaximumPitch = 89f;

    [Header("TPP Camera Collision")]
    [Tooltip("Only colliders on these layers block the camera.")]
    [SerializeField] private LayerMask cameraCollisionLayers = ~0;

    [Tooltip("Radius of the SphereCast.")]
    [SerializeField, Min(0.01f)]
    private float cameraCollisionRadius = 0.25f;

    [Tooltip("SphereCast origin height above the character root.")]
    [SerializeField]
    private float cameraCollisionPivotHeight = 1.5f;

    [Tooltip("Extra space between the camera and an obstacle.")]
    [SerializeField, Min(0f)]
    private float cameraCollisionPadding = 0.05f;

    [Tooltip("Near clip plane of the worker camera (all modes). Must be well below the collision radius, otherwise " +
             "the camera stops at the wall but still shows through it (the old 0.3 did).")]
    [SerializeField, Range(0.01f, 0.3f)]
    private float nearClipPlane = 0.06f;

    [SerializeField]
    private bool drawCameraCollisionGizmos = true;

    [Tooltip("TPP: when a wall / equipment pushes the camera this close to the worker's head, the worker's body is " +
             "hidden (its shadow stays), so the camera never shows the inside of the character. 0 = off.")]
    [SerializeField, Min(0f)]
    private float hideCharacterDistance = 1.1f;

    [Header("Camera Transition")]
    [SerializeField, Min(0f)]
    private float transitionDuration = 0.25f;

    [Header("Cursor")]
    [Tooltip("Drag To Look: cursor always visible, hold right mouse button + drag to look, WASD always moves.\n" +
             "Locked Cursor: the old mode - right click toggles a locked cursor, move/look only while locked.")]
    [SerializeField]
    private LookMode lookMode = LookMode.DragToLook;

    [Tooltip("Locked Cursor mode only: the action that toggles / holds the cursor.")]
    [SerializeField]
    private InputActionReference cursorAction;

    [Tooltip("Toggle switches visibility on each press. " + "Hold To Show displays the cursor only while held.")]
    [SerializeField]
    private CursorInputMode cursorInputMode = CursorInputMode.Toggle;

    [SerializeField]
    private bool lockCursorOnStart = true;

    private CharacterFocusController focusController;
    private CharacterViewStateMachine.ViewMode previousMode;

    private Vector3 transitionStartPosition;
    private Quaternion transitionStartRotation;
    private float transitionElapsed;
    private bool isTransitioning;

    private Vector3 tppOrbitOffset;
    private Quaternion tppRotationOffset;

    private Vector2 smoothedLookInput;
    private Vector2 lookSmoothVelocity;

    private InputAction activeCursorAction;
    private bool enabledCursorActionHere;

    private bool isCursorLocked;
    private bool lookDragging;
    private float crouchDrop;
    private CharacterMovementController movementController;
    private float yaw;
    private float pitch;

    private Vector3 lastCollisionOrigin;
    private Vector3 lastDesiredTPPPosition;
    private Vector3 lastResolvedTPPPosition;
    private bool lastSphereCastHit;

    private readonly RaycastHit[] backHitBuffer = new RaycastHit[8];
    private Renderer[] characterRenderers;
    private UnityEngine.Rendering.ShadowCastingMode[] characterShadowModes;
    private bool characterHidden;

    public Camera PlayerCamera => playerCamera;
    public Transform CharacterRoot => characterRoot;
    public bool IsCursorLocked => isCursorLocked;
    public bool IsDragToLook => lookMode == LookMode.DragToLook;
    /// <summary>True while WASD may move the worker: always in Drag To Look, only when locked otherwise.</summary>
    public bool MovementAllowed => lookMode == LookMode.DragToLook || isCursorLocked;
    /// <summary>True while the right mouse button drag is turning the camera.</summary>
    public bool IsLookDragging => lookDragging;

    public float CameraCollisionRadius => cameraCollisionRadius;
    public Transform FlyCameraPosition => fppPosition;

    private void Awake()
    {
        playerOwner = GetComponent<Player>();
        if (inputReader == null) inputReader = GetComponent<CharacterInputReader>();

        if (viewStateMachine == null)
        {
            viewStateMachine = GetComponent<CharacterViewStateMachine>();
        }

        if (characterRoot == null) characterRoot = transform;

        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
        if (playerCamera != null) playerCamera.nearClipPlane = nearClipPlane;
        focusController = GetComponent<CharacterFocusController>();
        movementController = GetComponent<CharacterMovementController>();
    }

    private void OnEnable()
    {
        activeCursorAction = cursorAction != null ? cursorAction.action.Clone() : null;

        enabledCursorActionHere = false;

        if (activeCursorAction == null)
        {
            Debug.LogWarning
            (
                "Assign a Cursor Action in CharacterCameraController.", this
            );
            return;
        }

        enabledCursorActionHere = !activeCursorAction.enabled;

        if (enabledCursorActionHere) activeCursorAction.Enable();
    }

    private void Update()
    {
        if (ControlBlocked)
        {
            lookDragging = false;
            return;
        }
        // Inspection keeps the pointer visible for orbit dragging and exit UI.
        if (viewStateMachine != null && viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FocusCam)
        {
            lookDragging = false;
            return;
        }

        if (lookMode == LookMode.DragToLook)
        {
            UpdateDragToLook();
            return;
        }

        if (!Application.isFocused || activeCursorAction == null || !activeCursorAction.enabled)
        {
            return;
        }

        // Poll the button state so UI/RightClick can remain Pass Through.
        if (cursorInputMode == CursorInputMode.Toggle)
        {
            if (activeCursorAction.WasPressedThisFrame())
                ToggleCursor();
        }
        else
        {
            bool shouldLock = !activeCursorAction.IsPressed();
            CursorLockMode desiredLockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;

            if (isCursorLocked != shouldLock || Cursor.lockState != desiredLockState || Cursor.visible != !shouldLock)
            {
                SetCursorLocked(shouldLock);
            }
        }
    }

    /// <summary>Cursor stays free and visible; a right button press that starts on the 3D view (not on a
    /// React panel or Unity UI) turns the camera until the button is released.</summary>
    private void UpdateDragToLook()
    {
        if (isCursorLocked || Cursor.lockState != CursorLockMode.None || !Cursor.visible) SetCursorLocked(false);

        Mouse mouse = Mouse.current;
        if (mouse == null || !Application.isFocused)
        {
            lookDragging = false;
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame && !IsPointerOverUI()) lookDragging = true;
        if (!mouse.rightButton.isPressed) lookDragging = false;
    }

    private static bool IsPointerOverUI()
    {
        if (ExternalUIState.PointerOverUI) return true; // React panel (SetPointerOverUI_Extern)
        EventSystem eventSystem = EventSystem.current;
        return eventSystem != null && eventSystem.IsPointerOverGameObject(); // Unity UI, e.g. the minimap
    }

    private void Start()
    {
        if (viewStateMachine == null || viewStateMachine.CurrentMode != CharacterViewStateMachine.ViewMode.FocusCam)
        {
            SetCursorLocked(lookMode == LookMode.LockedCursor && (cursorInputMode == CursorInputMode.HoldToShow || lockCursorOnStart));
        }

        if (viewStateMachine == null || characterRoot == null || playerCamera == null)
        {
            return;
        }

        yaw = characterRoot.eulerAngles.y;
        pitch = 0f;

        CacheTPPCameraOffset();

        previousMode = viewStateMachine.CurrentMode;

        ClampPitchForCurrentMode();
        if (previousMode != CharacterViewStateMachine.ViewMode.FocusCam)
            SnapToCurrentPosition();
    }

    private void LateUpdate()
    {
        if (ControlBlocked || viewStateMachine == null || characterRoot == null || playerCamera == null) return;

        // Worker camera is not rendering (e.g. plant overview camera is active): skip the TPP SphereCast,
        // the hover UI check and the hover raycast entirely. The camera pose is fully recomputed from
        // yaw/pitch/character position on the first frame it is enabled again, so nothing is lost.
        if (!playerCamera.isActiveAndEnabled) return;

        if (focusController != null && focusController.isActiveAndEnabled) focusController.TickPointerInput();
        if (ControlBlocked) return;

        if (viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FocusCam)
        {
            previousMode = viewStateMachine.CurrentMode;
            isTransitioning = false;
            ResetLookSmoothing();

            if (focusController != null && focusController.isActiveAndEnabled) focusController.UpdateFocusCamera();
            return;
        }

        if (previousMode != viewStateMachine.CurrentMode)
        {
            previousMode = viewStateMachine.CurrentMode;
            ClampPitchForCurrentMode();
            BeginTransition();
        }

        bool looking = lookMode == LookMode.DragToLook ? lookDragging : isCursorLocked;
        if (looking && inputReader != null) HandleLookInput();
        else ResetLookSmoothing();

        // Crouching (Z) lowers the camera smoothly; fly camera never crouches.
        float targetDrop = movementController != null && viewStateMachine.CurrentMode != CharacterViewStateMachine.ViewMode.FlyCam
            ? movementController.CrouchCameraDrop : 0f;
        crouchDrop = Mathf.MoveTowards(crouchDrop, targetDrop, Time.deltaTime * 3f);

        UpdateCameraPosition();
    }

    //private void LateUpdate()
    //{
    //    if (ControlBlocked) return;
    //    if (viewStateMachine == null || characterRoot == null || playerCamera == null)
    //        return;

    //    if (focusController == null)
    //        focusController = GetComponent<CharacterFocusController>();

    //    if (focusController != null && focusController.isActiveAndEnabled)
    //        focusController.TickPointerInput();

    //    if (ControlBlocked) return; // Pointer listeners may have suspended the player.

    //    if (viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FocusCam)
    //    {
    //        previousMode = viewStateMachine.CurrentMode;
    //        isTransitioning = false;
    //        ResetLookSmoothing();
    //        if (focusController != null && focusController.isActiveAndEnabled)
    //            focusController.UpdateFocusCamera();
    //        return; // No TPP SphereCast or normal look while inspecting.
    //    }

    //    if (previousMode != viewStateMachine.CurrentMode)
    //    {
    //        previousMode = viewStateMachine.CurrentMode;

    //        ClampPitchForCurrentMode();
    //        BeginTransition();
    //    }

    //    if (isCursorLocked && inputReader != null)
    //    {
    //        HandleLookInput();
    //    }
    //    else
    //    {
    //        ResetLookSmoothing();
    //    }

    //    UpdateCameraPosition();
    //}

    private void CacheTPPCameraOffset()
    {
        if (tppPosition == null)
            return;

        Quaternion initialYawRotation = Quaternion.Euler(0f, yaw, 0f);

        tppOrbitOffset = Quaternion.Inverse(initialYawRotation) * (tppPosition.position - characterRoot.position);

        tppRotationOffset = Quaternion.Inverse(initialYawRotation) * tppPosition.rotation;
    }

    private void HandleLookInput()
    {
        Vector2 rawLookInput = inputReader.LookInput;

        if (Mathf.Abs(rawLookInput.x) < mouseDeadZone)
            rawLookInput.x = 0f;

        if (Mathf.Abs(rawLookInput.y) < mouseDeadZone)
            rawLookInput.y = 0f;

        if (lookSmoothTime > 0f)
        {
            smoothedLookInput = Vector2.SmoothDamp(smoothedLookInput,rawLookInput,ref lookSmoothVelocity,lookSmoothTime,Mathf.Infinity,Time.unscaledDeltaTime);
        }
        else
        {
            smoothedLookInput = rawLookInput;
        }

        yaw += smoothedLookInput.x * lookSensitivity;

        float verticalDirection = invertVerticalLook ? 1f : -1f;

        pitch += smoothedLookInput.y * lookSensitivity * verticalDirection;

        ClampPitchForCurrentMode();

        if (viewStateMachine.CurrentMode != CharacterViewStateMachine.ViewMode.TPP)
        {
            characterRoot.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    private void ClampPitchForCurrentMode()
    {
        GetCurrentPitchLimits
        (
            out float minimumPitch,
            out float maximumPitch
        );

        if (minimumPitch > maximumPitch)
        {
            float temporaryValue = minimumPitch;
            minimumPitch = maximumPitch;
            maximumPitch = temporaryValue;
        }

        pitch = Mathf.Clamp(pitch,minimumPitch,maximumPitch);
    }

    private void GetCurrentPitchLimits(out float minimumPitch,out float maximumPitch)
    {
        switch (viewStateMachine.CurrentMode)
        {
            case CharacterViewStateMachine.ViewMode.TPP:
                minimumPitch = tppMinimumPitch;
                maximumPitch = tppMaximumPitch;
                break;

            case CharacterViewStateMachine.ViewMode.FPP:
                minimumPitch = fppMinimumPitch;
                maximumPitch = fppMaximumPitch;
                break;

            case CharacterViewStateMachine.ViewMode.FlyCam:
                minimumPitch = flyMinimumPitch;
                maximumPitch = flyMaximumPitch;
                break;

            default:
                minimumPitch = -80f;
                maximumPitch = 80f;
                break;
        }
    }

    private Vector3 ResolveTPPCameraCollision(Vector3 desiredCameraPosition)
    {
        Vector3 sphereCastOrigin = characterRoot.position + Vector3.up * cameraCollisionPivotHeight;

        Vector3 castDirection = desiredCameraPosition - sphereCastOrigin;

        float desiredDistance = castDirection.magnitude;

        lastCollisionOrigin = sphereCastOrigin;
        lastDesiredTPPPosition = desiredCameraPosition;
        lastResolvedTPPPosition = desiredCameraPosition;
        lastSphereCastHit = false;

        if (desiredDistance <= Mathf.Epsilon)
            return desiredCameraPosition;

        castDirection /= desiredDistance;

        float safeDistance = desiredDistance;
        bool hitObstacle = false;

        if (Physics.SphereCast(sphereCastOrigin,cameraCollisionRadius,castDirection,out RaycastHit hit,desiredDistance,cameraCollisionLayers,QueryTriggerInteraction.Ignore))
        {
            safeDistance = hit.distance - cameraCollisionPadding;
            hitObstacle = true;
        }

        // One-sided walls (mesh colliders) are invisible to the cast above when seen from their back side:
        // check from the camera towards the worker as well and keep the obstacle nearest to the worker.
        int backHits = Physics.RaycastNonAlloc(desiredCameraPosition,-castDirection,backHitBuffer,desiredDistance,cameraCollisionLayers,QueryTriggerInteraction.Ignore);
        for (int i = 0; i < backHits; i++)
        {
            Collider c = backHitBuffer[i].collider;
            if (c == null || c.transform.IsChildOf(characterRoot)) continue; // the worker's own colliders

            float distanceFromWorker = desiredDistance - backHitBuffer[i].distance - cameraCollisionRadius - cameraCollisionPadding;
            if (distanceFromWorker < safeDistance)
            {
                safeDistance = distanceFromWorker;
                hitObstacle = true;
            }
        }

        if (!hitObstacle)
            return desiredCameraPosition;

        safeDistance = Mathf.Clamp(safeDistance,0f,desiredDistance);

        Vector3 resolvedPosition = sphereCastOrigin + castDirection * safeDistance;

        lastSphereCastHit = true;
        lastResolvedTPPPosition = resolvedPosition;

        return resolvedPosition;
    }

    // Body meshes -> shadow only (still casts its shadow, not drawn). The FPP hiding (renderer.enabled) is separate.
    private void SetCharacterHidden(bool hidden)
    {
        if (hidden == characterHidden) return;
        characterHidden = hidden;

        if (characterRenderers == null) CacheCharacterRenderers();

        for (int i = 0; i < characterRenderers.Length; i++)
        {
            if (characterRenderers[i] == null) continue;
            characterRenderers[i].shadowCastingMode = hidden ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : characterShadowModes[i];
        }
    }

    private void CacheCharacterRenderers()
    {
        var list = new System.Collections.Generic.List<Renderer>();
        Transform cameraTransform = playerCamera != null ? playerCamera.transform : null;
        int bodyLayer = characterRoot.gameObject.layer;

        foreach (Renderer r in characterRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (cameraTransform != null && r.transform.IsChildOf(cameraTransform)) continue;
            // Skinned body / clothes, and meshes on the worker's own layer (helmet, tools). Not minimap icons etc.
            if (r is SkinnedMeshRenderer || (r is MeshRenderer && r.gameObject.layer == bodyLayer)) list.Add(r);
        }

        characterRenderers = list.ToArray();
        characterShadowModes = new UnityEngine.Rendering.ShadowCastingMode[characterRenderers.Length];
        for (int i = 0; i < characterRenderers.Length; i++) characterShadowModes[i] = characterRenderers[i].shadowCastingMode;
    }

    private void ResetLookSmoothing()
    {
        smoothedLookInput = Vector2.zero;
        lookSmoothVelocity = Vector2.zero;
    }

    private void BeginTransition()
    {
        transitionStartPosition = playerCamera.transform.position;
        transitionStartRotation = playerCamera.transform.rotation;

        transitionElapsed = 0f;
        isTransitioning = true;
    }

    private void UpdateCameraPosition()
    {
        if (!TryGetTargetCameraPose(out Vector3 targetPosition,out Quaternion targetRotation))
        {
            return;
        }

        if (!isTransitioning || transitionDuration <= 0f)
        {
            playerCamera.transform.SetPositionAndRotation(targetPosition,targetRotation);

            isTransitioning = false;
            return;
        }

        transitionElapsed += Time.deltaTime;

        float transitionProgress = Mathf.Clamp01(transitionElapsed / transitionDuration);

        float smoothedProgress = Mathf.SmoothStep(0f,1f,transitionProgress);

        Vector3 cameraPosition = Vector3.Lerp(transitionStartPosition,targetPosition,smoothedProgress);

        Quaternion cameraRotation = Quaternion.Slerp(transitionStartRotation,targetRotation,smoothedProgress);

        playerCamera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);

        if (transitionProgress >= 1f)
            isTransitioning = false;
    }

    private bool TryGetTargetCameraPose(out Vector3 targetPosition,out Quaternion targetRotation)
    {
        targetPosition = Vector3.zero;
        targetRotation = Quaternion.identity;

        if (viewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.TPP)
        {
            if (tppPosition == null)
                return false;

            Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);

            Vector3 desiredPosition = characterRoot.position + yawRotation * tppOrbitOffset + Vector3.down * crouchDrop;

            targetPosition = ResolveTPPCameraCollision(desiredPosition);
            targetRotation = yawRotation * tppRotationOffset * Quaternion.Euler(pitch, 0f, 0f);

            // Hysteresis (+0.15 m) so the body does not flicker at the edge.
            float headDistance = Vector3.Distance(targetPosition, lastCollisionOrigin);
            SetCharacterHidden(hideCharacterDistance > 0f && headDistance < hideCharacterDistance + (characterHidden ? 0.15f : 0f));

            return true;
        }

        SetCharacterHidden(false);

        if (fppPosition == null)
            return false;

        targetPosition = fppPosition.position + Vector3.down * crouchDrop;
        targetRotation = fppPosition.rotation * Quaternion.Euler(pitch, 0f, 0f);

        return true;
    }

    private void SnapToCurrentPosition()
    {
        if (!TryGetTargetCameraPose(out Vector3 targetPosition,out Quaternion targetRotation))
        {
            return;
        }

        playerCamera.transform.SetPositionAndRotation(targetPosition,targetRotation);
    }

    internal void SetInputSuspended(bool suspended)
    {
        if (activeCursorAction == null) return;
        if (suspended) activeCursorAction.Disable(); else activeCursorAction.Enable();
    }

    internal void ResetForScene(float heading)
    {
        yaw = heading;
        pitch = 0f;
        isTransitioning = false;
        lookDragging = false;
        crouchDrop = 0f;
        previousMode = viewStateMachine.CurrentMode;
        ResetLookSmoothing();
        ClampPitchForCurrentMode();
        SnapToCurrentPosition();
    }

    internal Vector2 CaptureAngles() => new Vector2(yaw, pitch);
    internal void RestoreAngles(Vector2 angles)
    {
        yaw = angles.x;
        pitch = angles.y;
        previousMode = viewStateMachine.CurrentMode;
        isTransitioning = false;
        ResetLookSmoothing();
    }

    internal void RestoreCursorState(bool locked, CursorLockMode lockState, bool visible)
    {
        if (lookMode == LookMode.DragToLook)
        {
            // The cursor is never locked or hidden in Drag To Look.
            locked = false;
            lockState = CursorLockMode.None;
            visible = true;
        }

        isCursorLocked = locked;
        Cursor.lockState = lockState;
        Cursor.visible = visible;
    }

    public void ToggleCursor()
    {
        SetCursorLocked(!isCursorLocked);
    }

    public void LockCursor()
    {
        SetCursorLocked(true);
    }

    public void UnlockCursor()
    {
        SetCursorLocked(false);
    }

    private void SetCursorLocked(bool locked)
    {
        if (ControlBlocked) return;
        if (lookMode == LookMode.DragToLook) locked = false; // cursor always visible
        isCursorLocked = locked;

        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;

        ResetLookSmoothing();
    }

    private void OnDisable()
    {
        if (enabledCursorActionHere && activeCursorAction != null)
            activeCursorAction.Disable();

        activeCursorAction?.Dispose();
        activeCursorAction = null;
        enabledCursorActionHere = false;

        SetCursorLocked(false);
        SetCharacterHidden(false);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawCameraCollisionGizmos)
            return;

        if (Application.isPlaying)
        {
            Gizmos.color = lastSphereCastHit ? Color.red : Color.green;
            Gizmos.DrawLine(lastCollisionOrigin,lastDesiredTPPPosition);
            Gizmos.DrawWireSphere(lastResolvedTPPPosition,cameraCollisionRadius);

            return;
        }

        if (characterRoot == null)
            characterRoot = transform;

        if (tppPosition == null)
            return;

        Vector3 previewOrigin = characterRoot.position + Vector3.up * cameraCollisionPivotHeight;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(previewOrigin,tppPosition.position);
        Gizmos.DrawWireSphere(tppPosition.position,cameraCollisionRadius);
    }
}