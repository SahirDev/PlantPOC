using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.UIElements;

// Start must run after Player.Start (order 500), which sets the worker up for the scene.
// Otherwise the player's scene setup would switch the worker back on right after we switched it off.
[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public class PlantIsometricCameraController : SingletonMono<PlantIsometricCameraController>
{
    // One per Main_Scene; a stray duplicate only loses this component, never the camera object.
    protected override bool DestroyDuplicateGameObject => false;

    [Header("Cameras")]
    [SerializeField] private Camera isometricCamera;
    [SerializeField] private Camera workerCamera;
    [SerializeField] private Player workerPlayer;
    [SerializeField] private bool startInIsometricView = true;
    [SerializeField] private Key switchKey = Key.C;

    [Header("References")]
    [SerializeField] private BoxColliderHighlighter highlighter;
    [SerializeField] private PlantCameraUI plantCameraUI;
    [SerializeField] private Teleport teleport;
    [SerializeField] private GameObject minimap;

    [Header("Default View")]
    [SerializeField] private Transform defaultPivot;
    [SerializeField] private float initialPitch = 35.264f;
    [SerializeField] private float initialYaw = 45f;
    [SerializeField] private float initialDistance = 120f;

    [Header("Rotate")]
    [SerializeField] private float rotateSensitivity = 0.25f;
    [SerializeField] private float minPitch = 10f;
    [SerializeField] private float maxPitch = 85f;
    [SerializeField] private float rotateSmoothTime = 0.08f;

    [Header("Pan")]
    [SerializeField] private float panSensitivity = 0.35f;
    [SerializeField] private float panSmoothTime = 0.12f;

    [Header("Zoom")]
    [SerializeField] private float zoomSensitivity = 35f;
    [SerializeField] private float minDistance = 15f;
    [SerializeField] private float maxDistance = 180f;
    [SerializeField] private float zoomSmoothTime = 0.12f;

    [Header("Pivot Bounds")]
    [SerializeField] private bool usePivotBounds = true;
    [SerializeField] private Vector3 minPivotBounds = new Vector3(-150f, 1f, -150f);
    [SerializeField] private Vector3 maxPivotBounds = new Vector3(150f, 150f, 150f);

    [Header("Camera Position Bounds")]
    [SerializeField] private bool useCameraBounds = true;
    [SerializeField] private Vector3 minCameraBounds = new Vector3(-150f, 1f, -150f);
    [SerializeField] private Vector3 maxCameraBounds = new Vector3(150f, 150f, 150f);

    [Header("Interaction Raycast")]
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField] private float rayDistance = 2500f;

    [Header("Performance (WebGL)")]
    [Tooltip("While the mouse and camera are both still, the UI check + physics raycast under the cursor is only repeated at this interval (seconds) instead of every frame. 0 = every frame (old behaviour).")]
    [SerializeField, Min(0f)] private float idlePointerRefreshInterval = 0.1f;

    [Header("Events")]
    public UnityEvent<GameObject> onObjectSelected = new UnityEvent<GameObject>();
    public UnityEvent onObjectDeselected = new UnityEvent();
    public UnityEvent<bool> onCameraModeChanged = new UnityEvent<bool>();

    public bool IsInIsometricMode { get; private set; }
    public Camera IsometricCamera => isometricCamera;
    public BoxCollider SelectedCollider => selectedCollider;
    public Vector3 CurrentPivot => currentPivot;

    public Vector2 PointerPosition
    {
        get
        {
            RefreshPointerState();
            return pointerPosition;
        }
    }

    public bool PointerOverUI
    {
        get
        {
            RefreshPointerState();
            return pointerOverUI;
        }
    }

    private BoxCollider selectedCollider;
    private Transform camTransform;
    private AudioListener isometricListener;
    private Mouse mouse;

    private Vector3 currentPivot, targetPivot, pivotVelocity;
    private float currentYaw, targetYaw, yawVelocity;
    private float currentPitch, targetPitch, pitchVelocity;
    private float currentDistance, targetDistance, distanceVelocity;

    private bool rotating, panning, leftPressed;
    private Vector2 leftDownPosition;

    private int pointerFrame = -1;
    private Vector2 pointerPosition;
    private bool pointerOverUI;
    private bool pointerHitValid;
    private RaycastHit pointerHit;

    private PointerEventData pointerEventData;
    private UIDocument[] uiDocuments;
    private readonly List<RaycastResult> uiResults = new List<RaycastResult>(8);

    // Pointer pick cache: skip UI + physics raycasts when nothing that affects them has changed.
    private Keyboard keyboard;
    private bool pickCacheValid;
    private Vector2 lastPickPointer;
    private Vector3 lastPickCameraPosition;
    private Quaternion lastPickCameraRotation;
    private float nextForcedPickTime;
    private bool lastPickExternalUI;

    // True once the smoothed camera has reached its target; no transform writes until input changes the target.
    private bool cameraSettled;

    private Vector3 DefaultPivot => defaultPivot != null ? defaultPivot.position : Vector3.zero;

    protected override void Awake()
    {
        base.Awake();
        if (IsDuplicate) return;

        mouse = Mouse.current;
        keyboard = Keyboard.current;

        if (isometricCamera == null) isometricCamera = GetComponent<Camera>();
        if (isometricCamera != null) camTransform = isometricCamera.transform;
        if (highlighter == null) highlighter = GetComponentInChildren<BoxColliderHighlighter>(true);

        uiDocuments = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        SetDefaultValues();
    }

    protected override void Start()
    {
        base.Start();
        if (IsDuplicate) return;

        SetIsometricMode(startInIsometricView);
        ResetView();
    }

    private void Update()
    {
        if (keyboard == null) keyboard = Keyboard.current;
        if (keyboard != null && keyboard[switchKey].wasPressedThisFrame) SwitchCamera();
        if (!IsInIsometricMode) return;

        RefreshPointerState();
        HandleNavigation();
        HandleSelection();
        UpdateCamera();
    }

    public void SwitchCamera()
    {
        SetIsometricMode(!IsInIsometricMode);
    }

    public void SetIsometricMode(bool overview)
    {
        SetIsometricMode(overview, null);
    }

    /// <summary>Leaves the overview and switches the worker on at the given position, always in TPP (used by Teleport).</summary>
    public void EnableWorkerModeAt(Vector3 workerPosition)
    {
        SetIsometricMode(false, workerPosition);
    }

    // Exactly one of the two is ever on:
    //   overview -> plant overview camera ON,  worker completely OFF (hidden, no camera, no minimap, no input)
    //   worker   -> worker ON,                 plant overview camera OFF (camera + audio listener)
    private void SetIsometricMode(bool overview, Vector3? workerTeleportPosition)
    {
        ResolveLiveWorker();

        if (overview)
        {
            // Worker off (the minimap panel hides by itself while the worker is suspended).
            if (workerPlayer != null) workerPlayer.EnterPlantOverview();
            if (workerCamera != null) workerCamera.enabled = false;

            // Overview camera on.
            SetIsometricCameraActive(true);
        }
        else
        {
            // Worker on first; if that is refused (e.g. mid scene-load) stay in overview so the screen never goes black.
            if (workerPlayer != null && !workerPlayer.ExitPlantOverview(workerTeleportPosition)) return;

            if (workerCamera != null) workerCamera.enabled = true;

            // Overview camera off.
            SetIsometricCameraActive(false);
        }

        IsInIsometricMode = overview;

        if (plantCameraUI != null) plantCameraUI.SetCameraMode(overview);
        if (teleport != null) teleport.gameObject.SetActive(overview);
        if (minimap != null) minimap.SetActive(!overview);

        if (!overview) Deselect();

        UnityEngine.Cursor.lockState = overview ? CursorLockMode.None : UnityEngine.Cursor.lockState;
        if (overview) UnityEngine.Cursor.visible = true;

        pointerFrame = -1;
        pickCacheValid = false;
        cameraSettled = false;
        onCameraModeChanged.Invoke(overview);
        CommunicationManager.HandleCameraModeChanged_Extern(overview ? CommunicationManager.CameraModeOverview : CommunicationManager.CameraModeWorker);
    }

    // The worker is a persistent player (DontDestroyOnLoad). When Main_Scene is loaded again, the scene's own
    // Worker copy is deactivated and destroyed, and the Inspector references would point at that dead copy.
    // Always switch to the live persistent worker and its camera.
    private void ResolveLiveWorker()
    {
        PersistentPlayer persistent = PersistentPlayer.Instance;
        if (persistent == null) return;

        Player livePlayer = persistent.GetComponent<Player>();
        if (livePlayer == null || livePlayer == workerPlayer) return;

        workerPlayer = livePlayer;
        if (livePlayer.CameraController != null && livePlayer.CameraController.PlayerCamera != null)
            workerCamera = livePlayer.CameraController.PlayerCamera;
    }

    private void SetIsometricCameraActive(bool active)
    {
        if (isometricCamera == null) return;

        isometricCamera.enabled = active;

        // The worker's audio listener is switched off with the worker, so the overview camera
        // carries the listener while it is active (avoids "no audio listener" / "2 audio listeners").
        if (isometricListener == null) isometricListener = isometricCamera.GetComponent<AudioListener>();
        if (isometricListener != null) isometricListener.enabled = active;
    }

    public void EnableIsometricMode()
    {
        SetIsometricMode(true);
    }

    public void EnableWorkerMode()
    {
        SetIsometricMode(false);
    }

    public void ResetView()
    {
        Deselect();

        targetPivot = DefaultPivot;
        targetYaw = initialYaw;
        targetPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        targetDistance = Mathf.Clamp(initialDistance, minDistance, maxDistance);

        currentPivot = targetPivot;
        currentYaw = targetYaw;
        currentPitch = targetPitch;
        currentDistance = targetDistance;

        pivotVelocity = Vector3.zero;
        yawVelocity = 0f;
        pitchVelocity = 0f;
        distanceVelocity = 0f;

        ClampPivot();
        UpdateCameraImmediate();
    }

    private void SetDefaultValues()
    {
        currentPivot = targetPivot = DefaultPivot;
        currentYaw = targetYaw = initialYaw;
        currentPitch = targetPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        currentDistance = targetDistance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
    }

    private void RefreshPointerState()
    {
        if (pointerFrame == Time.frameCount) return;

        pointerFrame = Time.frameCount;

        if (mouse == null) mouse = Mouse.current;
        if (mouse == null || isometricCamera == null || !isometricCamera.enabled)
        {
            pointerHitValid = false;
            pointerOverUI = false;
            pickCacheValid = false;
            return;
        }

        Vector2 position = mouse.position.ReadValue();

        // Reuse last frame's result when the mouse, camera and buttons are all unchanged.
        // A forced refresh every idlePointerRefreshInterval still catches UI opening / objects moving under the cursor.
        if (CanReusePointerPick(position))
        {
            pointerPosition = position;
            return;
        }

        pointerPosition = position;
        pointerHitValid = false;
        pointerOverUI = CheckPointerOverUI(pointerPosition);

        if (!pointerOverUI)
        {
            Ray ray = isometricCamera.ScreenPointToRay(pointerPosition);
            pointerHitValid = Physics.Raycast(ray, out pointerHit, rayDistance, interactionLayers, QueryTriggerInteraction.Ignore);
        }

        pickCacheValid = true;
        lastPickExternalUI = ExternalUIState.PointerOverUI;
        lastPickPointer = position;
        lastPickCameraPosition = camTransform != null ? camTransform.position : Vector3.zero;
        lastPickCameraRotation = camTransform != null ? camTransform.rotation : Quaternion.identity;
        nextForcedPickTime = Time.unscaledTime + idlePointerRefreshInterval;
    }

    private bool CanReusePointerPick(Vector2 position)
    {
        if (!pickCacheValid || idlePointerRefreshInterval <= 0f || camTransform == null) return false;
        if (Time.unscaledTime >= nextForcedPickTime) return false;
        if (position != lastPickPointer) return false;
        if (ExternalUIState.PointerOverUI != lastPickExternalUI) return false;
        if (camTransform.position != lastPickCameraPosition || camTransform.rotation != lastPickCameraRotation) return false;

        // Any click / scroll gets a fresh pick so selection, teleport and zoom always see current data.
        if (mouse.leftButton.wasPressedThisFrame || mouse.leftButton.wasReleasedThisFrame) return false;
        if (mouse.rightButton.wasPressedThisFrame || mouse.rightButton.wasReleasedThisFrame) return false;
        if (mouse.middleButton.wasPressedThisFrame || mouse.middleButton.wasReleasedThisFrame) return false;
        if (mouse.scroll.ReadValue().y != 0f) return false;

        return true;
    }

    public bool TryGetPointerHit(out RaycastHit hit)
    {
        RefreshPointerState();
        hit = pointerHit;
        return pointerHitValid;
    }

    private void HandleNavigation()
    {
        if (mouse == null || camTransform == null) return;

        Vector2 mouseDelta = mouse.delta.ReadValue();

        if (mouse.rightButton.wasPressedThisFrame && !pointerOverUI) rotating = true;
        if (mouse.rightButton.wasReleasedThisFrame) rotating = false;

        if (rotating)
        {
            targetYaw += mouseDelta.x * rotateSensitivity;
            targetPitch = Mathf.Clamp(targetPitch - mouseDelta.y * rotateSensitivity, minPitch, maxPitch);
        }

        if (mouse.middleButton.wasPressedThisFrame && !pointerOverUI) panning = true;
        if (mouse.middleButton.wasReleasedThisFrame) panning = false;

        if (panning)
        {
            float scale = targetDistance / 100f * panSensitivity;
            targetPivot += (-camTransform.right * mouseDelta.x - camTransform.up * mouseDelta.y) * scale;
            ClampPivot();
        }

        float scroll = mouse.scroll.ReadValue().y;

        if (Mathf.Abs(scroll) > 0.01f && !pointerOverUI)
        {
            float zoomAmount = targetDistance * 0.001f * zoomSensitivity;
            targetDistance = Mathf.Clamp(targetDistance - scroll * zoomAmount, minDistance, maxDistance);
        }
    }

    private void HandleSelection()
    {
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame && !pointerOverUI)
        {
            leftPressed = true;
            leftDownPosition = pointerPosition;
        }

        if (!mouse.leftButton.wasReleasedThisFrame || !leftPressed) return;

        leftPressed = false;

        float threshold = 6f * 6f;
        if ((leftDownPosition - pointerPosition).sqrMagnitude <= threshold) SelectFromPointer();
    }

    private void SelectFromPointer()
    {
        if (TryGetHighlightTarget(out BoxCollider box))
        {
            SelectBoxCollider(box);
            return;
        }

        Deselect();
    }

    private bool TryGetHighlightTarget(out BoxCollider box)
    {
        box = null;

        if (!TryGetPointerHit(out RaycastHit hit) || hit.collider == null) return false;

        box = hit.collider as BoxCollider;
        if (box == null) box = hit.collider.GetComponentInParent<BoxCollider>();

        if (box != null && box.CompareTag("Highlight")) return true;

        box = null;
        return false;
    }

    public void SelectBoxCollider(BoxCollider box)
    {
        if (box == null || !box.CompareTag("Highlight"))
        {
            Deselect();
            return;
        }

        selectedCollider = box;
        SetPivot(box.bounds.center);

        if (highlighter != null) highlighter.Highlight(box);
        if (plantCameraUI != null) plantCameraUI.ShowSelection(box);

        onObjectSelected.Invoke(box.gameObject);

        Vector3 size = box.bounds.size;
        ObjectInfo info = ObjectInfo.For(box);
        CommunicationManager.HandleOverviewObjectSelected_Extern(new OverviewSelectionPayload
        {
            name = info != null ? info.DisplayName : box.gameObject.name,
            objectName = box.gameObject.name,
            description = info != null ? info.Description : string.Empty,
            sizeX = Mathf.Abs(size.x),
            sizeY = Mathf.Abs(size.y),
            sizeZ = Mathf.Abs(size.z)
        });
    }

    public void Deselect()
    {
        bool hadSelection = selectedCollider != null;

        selectedCollider = null;
        ResetPivot();

        if (highlighter != null) highlighter.Clear();
        if (plantCameraUI != null) plantCameraUI.HideSelection();
        if (hadSelection)
        {
            onObjectDeselected.Invoke();
            CommunicationManager.HandleOverviewObjectDeselected_Extern();
        }
    }

    public void SetPivot(Vector3 position)
    {
        targetPivot = position;
        ClampPivot();
    }

    public void ResetPivot()
    {
        targetPivot = DefaultPivot;
        ClampPivot();
    }

    private void ClampPivot()
    {
        if (!usePivotBounds) return;

        targetPivot.x = Mathf.Clamp(targetPivot.x, minPivotBounds.x, maxPivotBounds.x);
        targetPivot.y = Mathf.Clamp(targetPivot.y, minPivotBounds.y, maxPivotBounds.y);
        targetPivot.z = Mathf.Clamp(targetPivot.z, minPivotBounds.z, maxPivotBounds.z);
    }

    private Vector3 ClampCameraPosition(Vector3 position)
    {
        if (!useCameraBounds) return position;

        position.x = Mathf.Clamp(position.x, minCameraBounds.x, maxCameraBounds.x);
        position.y = Mathf.Clamp(position.y, minCameraBounds.y, maxCameraBounds.y);
        position.z = Mathf.Clamp(position.z, minCameraBounds.z, maxCameraBounds.z);

        return position;
    }

    private void UpdateCamera()
    {
        if (camTransform == null) return;

        // Camera already resting on its target and no input changed it: nothing to do this frame.
        if (cameraSettled && currentYaw == targetYaw && currentPitch == targetPitch && currentDistance == targetDistance && currentPivot == targetPivot) return;

        float dt = Time.unscaledDeltaTime;

        currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, rotateSmoothTime, Mathf.Infinity, dt);
        currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref pitchVelocity, rotateSmoothTime, Mathf.Infinity, dt);
        currentPivot = Vector3.SmoothDamp(currentPivot, targetPivot, ref pivotVelocity, panSmoothTime, Mathf.Infinity, dt);
        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref distanceVelocity, zoomSmoothTime, Mathf.Infinity, dt);

        // SmoothDamp only approaches its target asymptotically; snap once the remaining difference is invisible
        // so the camera truly stops moving (this also lets the pointer pick cache work while idle).
        cameraSettled = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw)) < 0.001f
            && Mathf.Abs(currentPitch - targetPitch) < 0.001f
            && Mathf.Abs(currentDistance - targetDistance) < 0.001f
            && (currentPivot - targetPivot).sqrMagnitude < 0.000001f;

        if (cameraSettled)
        {
            currentYaw = targetYaw;
            currentPitch = targetPitch;
            currentDistance = targetDistance;
            currentPivot = targetPivot;
            yawVelocity = pitchVelocity = distanceVelocity = 0f;
            pivotVelocity = Vector3.zero;
        }

        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 position = currentPivot - rotation * Vector3.forward * currentDistance;

        position = ClampCameraPosition(position);

        camTransform.SetPositionAndRotation(position, rotation);
    }

    private void UpdateCameraImmediate()
    {
        if (camTransform == null) return;

        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 position = currentPivot - rotation * Vector3.forward * currentDistance;

        position = ClampCameraPosition(position);

        camTransform.SetPositionAndRotation(position, rotation);
    }

    private bool CheckPointerOverUI(Vector2 screenPosition)
    {
        // React panels on top of the canvas (reported by React via "input.pointerOverUI").
        if (ExternalUIState.PointerOverUI) return true;

        EventSystem eventSystem = EventSystem.current;

        if (eventSystem != null)
        {
            if (pointerEventData == null) pointerEventData = new PointerEventData(eventSystem);

            pointerEventData.position = screenPosition;
            uiResults.Clear();
            eventSystem.RaycastAll(pointerEventData, uiResults);

            for (int i = 0; i < uiResults.Count; i++)
                if (uiResults[i].module is GraphicRaycaster) return true;
        }

        if (uiDocuments == null) return false;

        Vector2 topLeft = new Vector2(screenPosition.x, Screen.height - screenPosition.y);

        for (int i = 0; i < uiDocuments.Length; i++)
        {
            UIDocument document = uiDocuments[i];

            if (document == null || !document.isActiveAndEnabled || document.rootVisualElement?.panel == null) continue;

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(document.rootVisualElement.panel, topLeft);
            VisualElement picked = document.rootVisualElement.panel.Pick(panelPosition);

            if (picked != null && picked != document.rootVisualElement.panel.visualTree) return true;
        }

        return false;
    }
}