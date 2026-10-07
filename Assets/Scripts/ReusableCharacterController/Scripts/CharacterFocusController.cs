using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterViewStateMachine), typeof(CharacterCameraController))]
public class CharacterFocusController : MonoBehaviour
{
    private Player playerOwner;
    private bool ControlBlocked => playerOwner != null && playerOwner.IsControlBlocked;

    [Header("Focus")]
    [Tooltip("Off: clicking equipment never switches the worker to the orbit camera (explosion view only).")]
    [SerializeField] private bool clickToFocusEnabled = false;

    [Header("References")]
    [SerializeField] private CharacterViewStateMachine viewStateMachine;
    [SerializeField] private CharacterCameraController cameraController;
    [SerializeField] private CharacterController characterController;

    [Header("Pointer Picking")]
    [SerializeField, Min(0.01f)] private float pickDistance = 1000f;
    [SerializeField] private string interactableLayerName = "Interactable";
    [SerializeField] private QueryTriggerInteraction pickTriggers = QueryTriggerInteraction.Collide;
    [SerializeField] private bool drawPickingRay;

    [Header("Pointer Events")]
    [SerializeField] private UnityEvent<GameObject> onHoveredObjectChanged = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onObjectClicked = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onHoverEntered = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onHoverExited = new UnityEvent<GameObject>();

    [Header("Focus Events")]
    [SerializeField] private UnityEvent<GameObject> onFocusEntered = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onFocusExited = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject, GameObject> onFocusTargetChanged = new UnityEvent<GameObject, GameObject>();

    [Header("Focus Orbit")]
    [SerializeField, Min(0f)] private float orbitSensitivity = 0.2f;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -85f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 85f;
    [SerializeField, Min(0f)] private float orbitSmoothTime = 0.08f;
    [SerializeField, Min(0.01f)] private float minimumZoomDistance = 0.5f;
    [SerializeField, Min(0.01f)] private float maximumZoomDistance = 30f;
    [SerializeField, Min(0f)] private float zoomSensitivity = 0.0015f;
    [SerializeField, Min(0f)] private float zoomSmoothTime = 0.1f;
    [SerializeField, Min(1f)] private float framingPadding = 1.2f;

    [Header("Additional Camera Rig Colliders")]
    [SerializeField] private Collider[] additionalCameraColliders;

    public bool ClickToFocusEnabled { get => clickToFocusEnabled; set => clickToFocusEnabled = value; }
    public CharacterViewStateMachine.ViewMode ReturnMode => returnMode;
    public Transform FocusTarget { get; private set; }
    public GameObject FocusedObject => FocusTarget != null ? FocusTarget.gameObject : null;
    public GameObject HoveredObject { get; private set; }
    public RaycastHit LastPointerHit { get; private set; }
    public bool HasPointerHit { get; private set; }
    public bool HasFocusSession { get; private set; }

    public UnityEvent<GameObject> OnHoveredObjectChanged => onHoveredObjectChanged;
    public UnityEvent<GameObject> OnObjectClicked => onObjectClicked;
    public UnityEvent<GameObject> OnHoverEntered => onHoverEntered;
    public UnityEvent<GameObject> OnHoverExited => onHoverExited;
    public UnityEvent<GameObject> OnFocusEntered => onFocusEntered;
    public UnityEvent<GameObject> OnFocusExited => onFocusExited;
    public UnityEvent<GameObject, GameObject> OnFocusTargetChanged => onFocusTargetChanged;

    private CharacterViewStateMachine.ViewMode returnMode;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>(8);
    private readonly List<Collider> disabledColliders = new List<Collider>(8);
    private readonly List<bool> originalColliderStates = new List<bool>(8);

    private PointerEventData pointerData;
    private EventSystem pointerEventSystem;
    private UIDocument[] uiDocuments;
    private Mouse mouse;
    private int interactableLayer;

    private Vector3 savedPlayerPosition;
    private Quaternion savedPlayerRotation;
    private Vector3 savedCameraPosition;
    private Quaternion savedCameraRotation;
    private Vector3 cameraOffsetFromPlayer;
    private Vector3 localFocusCenter;

    private bool savedCursorLocked;
    private bool orbitDragging;
    private bool focusEntryNotified;
    private bool restoringFocus;

    private float targetYaw;
    private float targetPitch;
    private float targetDistance;
    private float orbitYaw;
    private float orbitPitch;
    private float orbitDistance;
    private float yawVelocity;
    private float pitchVelocity;
    private float distanceVelocity;

    private int suppressPointerFrame = -1;
    private int hoverRevision;

    [Header("Performance (WebGL)")]
    [Tooltip("While the cursor is free and the mouse + camera are still, the hover UI check and raycast are only repeated at this interval (seconds). 0 = every frame (old behaviour). Clicks always get a fresh raycast.")]
    [SerializeField, Min(0f)] private float idlePointerRefreshInterval = 0.1f;

    private bool pickCacheValid;
    private Vector2 lastPickPosition;
    private Vector3 lastPickCameraPosition;
    private Quaternion lastPickCameraRotation;
    private float nextForcedPickTime;
    private bool lastPickExternalUI;

    private float MinimumDistance => Mathf.Max(0.01f, minimumZoomDistance);
    private float MaximumDistance => Mathf.Max(MinimumDistance, maximumZoomDistance);

    private void Awake()
    {
        playerOwner = GetComponent<Player>();
        mouse = Mouse.current;
        interactableLayer = LayerMask.NameToLayer(interactableLayerName);
        uiDocuments = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (viewStateMachine != null) viewStateMachine.OnViewModeChanging += HandleModeChanging;
    }

    private void OnDisable()
    {
        ExitFocus();
        if (viewStateMachine != null) viewStateMachine.OnViewModeChanging -= HandleModeChanging;
        ClearPointerHit();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        interactableLayer = LayerMask.NameToLayer(interactableLayerName);
    }
#endif

    private void ResolveReferences()
    {
        if (viewStateMachine == null) viewStateMachine = GetComponent<CharacterViewStateMachine>();
        if (cameraController == null) cameraController = GetComponent<CharacterCameraController>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
    }

    public void RefreshUIDocuments()
    {
        uiDocuments = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    public void Focus(GameObject target)
    {
        if (target != null) Focus(target.transform);
    }

    public void Focus(Transform target)
    {
        if (target != null) Focus(target, CalculateBounds(target));
    }

    public void Focus(Transform target, Bounds worldBounds)
    {
        if (ControlBlocked || restoringFocus || !isActiveAndEnabled || target == null || !target.gameObject.activeInHierarchy || cameraController == null || viewStateMachine == null) return;

        Transform player = cameraController.CharacterRoot;
        Camera cam = cameraController.PlayerCamera;

        if (player == null || cam == null || target == player || target.IsChildOf(player) || player.IsChildOf(target))
        {
            Debug.LogWarning("Focus needs a camera, player root and target outside the player hierarchy.", this);
            return;
        }

        GameObject previousTarget = FocusedObject;
        bool enteringFocus = !HasFocusSession;

        if (!HasFocusSession)
        {
            returnMode = viewStateMachine.CurrentMode;
            savedPlayerPosition = player.position;
            savedPlayerRotation = player.rotation;
            savedCameraPosition = cam.transform.position;
            savedCameraRotation = cam.transform.rotation;
            savedCursorLocked = cameraController.IsCursorLocked;
            cameraOffsetFromPlayer = Quaternion.Inverse(player.rotation) * (cam.transform.position - player.position);
            CacheAndDisableColliders(cam);
            HasFocusSession = true;
        }

        FocusTarget = target;
        localFocusCenter = target.InverseTransformPoint(worldBounds.center);

        Vector3 offset = cam.transform.position - worldBounds.center;
        if (offset.sqrMagnitude < 0.0001f) offset = -cam.transform.forward;

        Quaternion lookRotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);

        targetYaw = lookRotation.eulerAngles.y;
        targetPitch = Mathf.Clamp(Mathf.DeltaAngle(0f, lookRotation.eulerAngles.x), minimumPitch, maximumPitch);
        orbitYaw = targetYaw;
        orbitPitch = targetPitch;
        orbitDistance = Mathf.Clamp(offset.magnitude, MinimumDistance, MaximumDistance);

        float halfVerticalFov = cam.fieldOfView * Mathf.Deg2Rad * 0.5f;
        float halfHorizontalFov = Mathf.Atan(Mathf.Tan(halfVerticalFov) * cam.aspect);
        float limitingHalfFov = Mathf.Max(0.01f, Mathf.Min(halfVerticalFov, halfHorizontalFov));

        targetDistance = Mathf.Clamp(worldBounds.extents.magnitude * framingPadding / Mathf.Sin(limitingHalfFov), MinimumDistance, MaximumDistance);

        yawVelocity = 0f;
        pitchVelocity = 0f;
        distanceVelocity = 0f;
        orbitDragging = false;

        cameraController.UnlockCursor();
        viewStateMachine.SwitchToMode(CharacterViewStateMachine.ViewMode.FocusCam);

        if (viewStateMachine.CurrentMode != CharacterViewStateMachine.ViewMode.FocusCam)
        {
            RestoreFocusSession();
            return;
        }

        if (!HasFocusSession || FocusTarget != target) return;

        if (enteringFocus)
        {
            focusEntryNotified = true;
            onFocusEntered.Invoke(target.gameObject);
        }
        else if (previousTarget != target.gameObject)
        {
            onFocusTargetChanged.Invoke(previousTarget, target.gameObject);
        }
    }

    public void ExitFocus()
    {
        if (ControlBlocked || !HasFocusSession) return;

        if (viewStateMachine != null) viewStateMachine.SwitchToMode(returnMode == CharacterViewStateMachine.ViewMode.FocusCam ? viewStateMachine.InitialMode : returnMode);
        if (HasFocusSession) RestoreFocusSession();
    }

    private void HandleModeChanging(CharacterViewStateMachine.ViewMode oldMode, CharacterViewStateMachine.ViewMode newMode)
    {
        if (HasFocusSession && newMode != CharacterViewStateMachine.ViewMode.FocusCam) RestoreFocusSession();
    }

    private void RestoreFocusSession()
    {
        if (!HasFocusSession || restoringFocus) return;

        GameObject previousTarget = FocusedObject;
        bool notifyExit = focusEntryNotified;

        focusEntryNotified = false;
        restoringFocus = true;

        try
        {
            Transform player = cameraController != null ? cameraController.CharacterRoot : null;
            Camera cam = cameraController != null ? cameraController.PlayerCamera : null;

            if (player != null) player.SetPositionAndRotation(savedPlayerPosition, savedPlayerRotation);
            if (cam != null) cam.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);

            Physics.SyncTransforms();

            for (int i = 0; i < disabledColliders.Count; i++)
                if (disabledColliders[i] != null) disabledColliders[i].enabled = originalColliderStates[i];

            disabledColliders.Clear();
            originalColliderStates.Clear();

            HasFocusSession = false;
            FocusTarget = null;
            orbitDragging = false;
            suppressPointerFrame = Time.frameCount;

            if (cameraController != null) cameraController.RestoreCursorState(savedCursorLocked, savedCursorLocked ? CursorLockMode.Locked : CursorLockMode.None, !savedCursorLocked);

            ClearPointerHit();

            if (notifyExit) onFocusExited.Invoke(previousTarget);
        }
        finally
        {
            restoringFocus = false;
        }
    }

    private void CacheAndDisableColliders(Camera cam)
    {
        disabledColliders.Clear();
        originalColliderStates.Clear();

        DisableCollider(characterController);

        Collider[] cameraColliders = cam.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < cameraColliders.Length; i++) DisableCollider(cameraColliders[i]);

        if (additionalCameraColliders == null) return;

        for (int i = 0; i < additionalCameraColliders.Length; i++) DisableCollider(additionalCameraColliders[i]);
    }

    private void DisableCollider(Collider collider)
    {
        if (collider == null || disabledColliders.Contains(collider)) return;

        disabledColliders.Add(collider);
        originalColliderStates.Add(collider.enabled);
        collider.enabled = false;
    }

    public void TickPointerInput()
    {
        if (ControlBlocked) return;

        if (HasFocusSession && (FocusTarget == null || !FocusTarget.gameObject.activeInHierarchy))
        {
            ExitFocus();
            return;
        }

        if (mouse == null) mouse = Mouse.current;

        if (!Application.isFocused || mouse == null || cameraController == null || cameraController.PlayerCamera == null || cameraController.IsCursorLocked || !UnityEngine.Cursor.visible || suppressPointerFrame == Time.frameCount)
        {
            orbitDragging = false;
            ClearPointerHit();
            return;
        }

        Camera cam = cameraController.PlayerCamera;
        Vector2 position = mouse.position.ReadValue();

        // Nothing changed since the last pick (same mouse position, same camera pose, no click):
        // keep the current hover result instead of re-running the UI check + raycast.
        // Focus orbit/zoom is never cached.
        if (!HasFocusSession && CanReusePointerPick(position, cam)) return;

        if (!cam.pixelRect.Contains(position) || IsPointerOverUI(position))
        {
            orbitDragging = false;
            ClearPointerHit();
            StorePointerPick(position, cam);
            return;
        }

        if (HasFocusSession)
        {
            if (mouse.rightButton.wasPressedThisFrame) orbitDragging = true;
            if (!mouse.rightButton.isPressed) orbitDragging = false;

            if (orbitDragging)
            {
                Vector2 delta = mouse.delta.ReadValue();
                targetYaw += delta.x * orbitSensitivity;
                targetPitch = Mathf.Clamp(targetPitch - delta.y * orbitSensitivity, minimumPitch, maximumPitch);
            }

            float scroll = mouse.scroll.ReadValue().y;

            if (Mathf.Abs(scroll) > 0.01f) targetDistance = Mathf.Clamp(targetDistance * Mathf.Exp(-scroll * zoomSensitivity), MinimumDistance, MaximumDistance);

            if (orbitDragging)
            {
                ClearPointerHit();
                return;
            }
        }

        Ray ray = cam.ScreenPointToRay(position);
        bool found = Physics.Raycast(ray, out RaycastHit hit, pickDistance, ~0, pickTriggers);

        HasPointerHit = found;
        LastPointerHit = found ? hit : default;

        GameObject hitObject = found && hit.collider != null ? hit.collider.gameObject : null;

        // Store before notifying hover listeners: if a listener clears the hover, the cache is invalidated again.
        StorePointerPick(position, cam);
        SetHoveredObject(hitObject);

        if (drawPickingRay) Debug.DrawLine(ray.origin, found ? hit.point : ray.GetPoint(pickDistance), found ? Color.green : Color.yellow);

        if (ControlBlocked || !isActiveAndEnabled || suppressPointerFrame == Time.frameCount || HoveredObject != hitObject) return;
        if (hitObject == null || !mouse.leftButton.wasPressedThisFrame || !hitObject.activeInHierarchy) return;

        onObjectClicked.Invoke(hitObject);

        if (clickToFocusEnabled && interactableLayer >= 0 && hitObject.layer == interactableLayer) Focus(hitObject);
    }

    public void UpdateFocusCamera()
    {
        if (ControlBlocked || !HasFocusSession || FocusTarget == null || cameraController == null) return;

        float dt = Time.unscaledDeltaTime;

        orbitYaw = SmoothAngle(orbitYaw, targetYaw, ref yawVelocity, orbitSmoothTime, dt);
        orbitPitch = SmoothAngle(orbitPitch, targetPitch, ref pitchVelocity, orbitSmoothTime, dt);
        orbitDistance = zoomSmoothTime > 0f ? Mathf.SmoothDamp(orbitDistance, targetDistance, ref distanceVelocity, zoomSmoothTime, Mathf.Infinity, dt) : targetDistance;
        orbitDistance = Mathf.Clamp(orbitDistance, MinimumDistance, MaximumDistance);

        Quaternion rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 pivot = FocusTarget.TransformPoint(localFocusCenter);
        Vector3 position = pivot - rotation * Vector3.forward * orbitDistance;

        Transform player = cameraController.CharacterRoot;
        Camera cam = cameraController.PlayerCamera;

        if (player == null || cam == null) return;

        player.rotation = Quaternion.Euler(0f, orbitYaw, 0f);
        player.position = position - player.rotation * cameraOffsetFromPlayer;

        cam.transform.SetPositionAndRotation(position, rotation);
    }

    private float SmoothAngle(float current, float target, ref float velocity, float smoothTime, float dt)
    {
        return smoothTime > 0f ? Mathf.SmoothDampAngle(current, target, ref velocity, smoothTime, Mathf.Infinity, dt) : target;
    }

    public bool IsPointerOverUI(Vector2 screenPosition)
    {
        // React panels on top of the canvas (reported by React via "input.pointerOverUI").
        if (ExternalUIState.PointerOverUI) return true;

        EventSystem events = EventSystem.current;

        if (events != null)
        {
            if (pointerData == null || pointerEventSystem != events)
            {
                pointerEventSystem = events;
                pointerData = new PointerEventData(events);
            }

            pointerData.Reset();
            pointerData.position = screenPosition;
            uiHits.Clear();
            events.RaycastAll(pointerData, uiHits);

            for (int i = 0; i < uiHits.Count; i++)
                if (uiHits[i].module is GraphicRaycaster || uiHits[i].module is PanelRaycaster) return true;
        }

        if (uiDocuments == null) return false;

        Vector2 topLeftPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

        for (int i = 0; i < uiDocuments.Length; i++)
        {
            UIDocument document = uiDocuments[i];

            if (document == null || !document.isActiveAndEnabled) continue;

            VisualElement root = document.rootVisualElement;

            if (root == null || root.panel == null) continue;

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, topLeftPosition);
            VisualElement picked = root.panel.Pick(panelPosition);

            if (picked != null && picked != root.panel.visualTree) return true;
        }

        return false;
    }

    internal void PrepareSuspension()
    {
        orbitDragging = false;
        ClearHover();
    }

    internal void SuppressPointerThisFrame()
    {
        suppressPointerFrame = Time.frameCount;
    }

    internal void GetReturnPose(out Vector3 position, out Quaternion rotation)
    {
        position = savedPlayerPosition;
        rotation = savedPlayerRotation;
    }

    internal void EndFocusForScene()
    {
        RestoreFocusSession();
    }

    public void ClearHover()
    {
        suppressPointerFrame = Time.frameCount;
        ClearPointerHit();
    }

    private bool CanReusePointerPick(Vector2 position, Camera cam)
    {
        if (!pickCacheValid || idlePointerRefreshInterval <= 0f) return false;
        if (Time.unscaledTime >= nextForcedPickTime) return false;
        if (position != lastPickPosition) return false;
        if (ExternalUIState.PointerOverUI != lastPickExternalUI) return false;

        Transform camTransform = cam.transform;
        if (camTransform.position != lastPickCameraPosition || camTransform.rotation != lastPickCameraRotation) return false;

        if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) return false;

        return true;
    }

    private void StorePointerPick(Vector2 position, Camera cam)
    {
        Transform camTransform = cam.transform;
        pickCacheValid = true;
        lastPickExternalUI = ExternalUIState.PointerOverUI;
        lastPickPosition = position;
        lastPickCameraPosition = camTransform.position;
        lastPickCameraRotation = camTransform.rotation;
        nextForcedPickTime = Time.unscaledTime + idlePointerRefreshInterval;
    }

    private void ClearPointerHit()
    {
        pickCacheValid = false;
        HasPointerHit = false;
        LastPointerHit = default;
        SetHoveredObject(null);
    }

    private void SetHoveredObject(GameObject target)
    {
        if (ReferenceEquals(HoveredObject, target)) return;

        GameObject previousTarget = HoveredObject;

        HoveredObject = target;

        int revision = ++hoverRevision;

        if (!ReferenceEquals(previousTarget, null)) onHoverExited.Invoke(previousTarget);
        if (revision != hoverRevision) return;

        if (target != null) onHoverEntered.Invoke(target);
        if (revision != hoverRevision) return;

        onHoveredObjectChanged.Invoke(target);
    }

    private static Bounds CalculateBounds(Transform target)
    {
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        bool found = false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];

            if (!renderer.enabled) continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }

        if (!found)
        {
            Collider[] colliders = target.GetComponentsInChildren<Collider>();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];

                if (!collider.enabled) continue;

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else bounds.Encapsulate(collider.bounds);
            }
        }

        if (!found) bounds = new Bounds(target.position, Vector3.one);

        return bounds;
    }
}