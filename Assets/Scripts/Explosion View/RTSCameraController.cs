using UnityEngine;
using UnityEngine.InputSystem;

public class RTSCameraController : MonoBehaviour
{
    [Tooltip("Off: the part name only shows when a part is clicked. On: also while hovering.")]
    [SerializeField] private bool showPartNameOnHover = false;

    [Header("References")]
    [SerializeField]
    private Camera targetCamera;

    [Header("Orbit")]
    [SerializeField]
    private float orbitSpeed = 0.2f;

    [SerializeField]
    private float minPitch = -80f;

    [SerializeField]
    private float maxPitch = 80f;

    [Header("Pan")]
    [SerializeField]
    private float panSpeed = 0.1f;

    [Header("Zoom")]
    [SerializeField]
    private float zoomSpeed = 40f;

    [SerializeField]
    private float minZoomDistance = 10f;

    [SerializeField]
    private float maxZoomDistance = 60f;

    [Header("Stay Inside The Room")]
    [Tooltip("Walls / floor / roof the camera must not pass. Nothing by default: the room is hidden in the " +
             "explosion view, and stopping at invisible walls made the distance jump.")]
    [SerializeField]
    private LayerMask roomLayers = 0;

    [Tooltip("Space kept between the camera and a wall.")]
    [SerializeField, Min(0.05f)]
    private float wallMargin = 0.5f;

    [Tooltip("The camera never gets closer to the orbit centre than this (even if a wall is closer).")]
    [SerializeField, Min(0.1f)]
    private float minCameraDistance = 2f;

    [Header("Smoothing")]
    [Tooltip("Seconds the camera trails the mouse (orbit / pan / zoom). 0 = raw, jumpy input.")]
    [SerializeField, Range(0f, 0.3f)]
    private float smoothTime = 0.08f;

    [Header("Click To Focus (explosion view only)")]
    [Tooltip("Clicking a part glides the orbit camera to it and frames it (the normal room never does this).")]
    [SerializeField]
    private bool focusOnClick = true;

    [Tooltip("Seconds for the glide to the clicked part.")]
    [SerializeField, Min(0f)]
    private float focusSeconds = 0.6f;

    [Tooltip("Space around the part when framed (1 = tight).")]
    [SerializeField, Min(1f)]
    private float focusPadding = 1.4f;

    [Tooltip("Closest the camera gets to a small focused part (m). Zooming in can go this close after a focus.")]
    [SerializeField, Min(0.5f)]
    private float focusMinDistance = 2.5f;

    [Header("Raycast")]
    [SerializeField]
    private LayerMask interactableLayers;

    [SerializeField]
    private float maxInteractDistance = 1000f;

    [Header("Input")]
    [SerializeField]
    private InputActionReference orbitAction;

    [SerializeField]
    private InputActionReference panAction;

    [SerializeField]
    private InputActionReference zoomAction;

    [SerializeField]
    private InputActionReference mouseInteractAction;

    private float _distance;
    private float _pitch;
    private Vector3 _pivot;
    private float _yaw;
    private bool _initialized;
    private bool _skipNextCameraUpdate;

    public bool IsOrbiting => orbitAction != null && orbitAction.action.ReadValue<float>() > 0f;
    public bool IsPanning => panAction != null && panAction.action.ReadValue<float>() > 0f;
    public Vector2 ZoomInput => zoomAction != null ? zoomAction.action.ReadValue<Vector2>() : Vector2.zero;
    public bool IsMouseInteracting => mouseInteractAction != null && mouseInteractAction.action.ReadValue<float>() > 0f;

    public Camera Camera => targetCamera;

    private ExplodableViewNode currentHoveredPart;

    // A room built as one big box collider (e.g. the turbine warehouse): rays from inside do not hit it,
    // so the camera is kept inside its bounds instead.
    private bool hasRoomBox;
    private Bounds roomBox;

    // Shown camera values: ease towards the target values (_yaw, _pitch, _distance, _pivot).
    private float curYaw, curPitch, curDistance;
    private Vector3 curPivot;
    private float yawVelocity, pitchVelocity, distanceVelocity;
    private Vector3 pivotVelocity;
    private ModularExplodedView explodedViewRef;

    // Glide to a clicked part.
    private bool focusing, focusedOnPart;
    private float focusTime;
    private Vector3 focusFromPivot, focusToPivot;
    private float focusFromDistance, focusToDistance;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (mouseInteractAction != null)
            mouseInteractAction.action.performed += HandleInteract;
    }

    private void OnEnable()
    {
        EnableAction(panAction);
        EnableAction(orbitAction);
        EnableAction(zoomAction);
        EnableAction(mouseInteractAction);
    }

    private void OnDisable()
    {
        DisableAction(panAction);
        DisableAction(orbitAction);
        DisableAction(zoomAction);
        DisableAction(mouseInteractAction);

        if (mouseInteractAction != null)
            mouseInteractAction.action.performed -= HandleInteract;
    }

    private void OnDestroy()
    {
        DisableAction(panAction);
        DisableAction(orbitAction);
        DisableAction(zoomAction);
        DisableAction(mouseInteractAction);

        if (mouseInteractAction != null)
            mouseInteractAction.action.performed -= HandleInteract;
    }

    public void Initialize(ModularExplodedView explodedView)
    {
        if (explodedView == null) return;

        var cameraPoint = explodedView.ExplosionCameraPoint;

        if (cameraPoint == null)
        {
            Debug.LogWarning("[RTS-Camera] Explosion camera point not assigned");
            return;
        }

        targetCamera.transform.SetPositionAndRotation(cameraPoint.position, cameraPoint.rotation);

        var angles = cameraPoint.eulerAngles;

        _yaw = angles.y;
        _pitch = angles.x > 180f ? angles.x - 360f : angles.x;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        _distance = explodedView.InitialDistance;

        _pivot = GetObjectCenter(explodedView.gameObject);
        explodedViewRef = explodedView;
        FindRoomBox();
        SnapSmoothing();

        _initialized = true;
        _skipNextCameraUpdate = true;
    }

    private void HandleInteract(InputAction.CallbackContext obj)
    {
        if (targetCamera == null) return;
        if (Mouse.current == null) return;

        var ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (IsPointerOverUI())
        {
            // Debug.Log($"Pointer on UI");
            // HUDController.Instance.HideClickContext();
            // HUDController.Instance.HideDescription();
        }
        else
        {
            // Debug.Log($"Pointer not on UI");

            if (Physics.Raycast(ray, out var hit, maxInteractDistance, interactableLayers, QueryTriggerInteraction.Ignore))
            {
                Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.green);
                HandleHit(hit);
            }
            else
            {
                // Empty space: close the part info and go back to the whole equipment.
                if (HUDController.Instance != null)
                {
                    HUDController.Instance.HideClickContext();
                    HUDController.Instance.HideDescription();
                }
                if (focusOnClick) FrameAll();
            }
        }
    }

    // React panels (reported by React) and Unity UI on top (e.g. the maintenance sheet): clicks / scroll there
    // must not select parts or zoom the camera.
    private bool IsPointerOverUI()
    {
        if (ExternalUIState.PointerOverUI) return true;
        UnityEngine.EventSystems.EventSystem eventSystem = UnityEngine.EventSystems.EventSystem.current;
        return eventSystem != null && eventSystem.IsPointerOverGameObject();
    }

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (_initialized)
            return;

        var angles = targetCamera.transform.eulerAngles;

        _yaw = angles.y;
        _pitch = angles.x;

        if (_pitch > 180f)
            _pitch -= 360f;

        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        _distance = Mathf.Clamp(10f, minZoomDistance, maxZoomDistance);
        _pivot = targetCamera.transform.position + targetCamera.transform.forward * _distance;
        FindRoomBox();
        SnapSmoothing();

        UpdateCameraPosition();
    }

    private void Update()
    {
        if (_skipNextCameraUpdate)
        {
            _skipNextCameraUpdate = false;
            return;
        }

        HandleOrbit();
        HandlePan();
        HandleZoom();
        UpdateFocusGlide();

        UpdateCameraPosition();

        // Part names show on click only (PartInfoCard); hover detection is off unless switched on.
        if (showPartNameOnHover) GetMouseHoveredObject();
    }

    // A click selects the part (maintenance sheet) and, in this explosion view, focuses the camera on it.
    // It never explodes anything (that is done by the buttons). Most boiler / turbine meshes are children of
    // a part, so the part is looked up in the parents.
    private void HandleHit(RaycastHit hit)
    {
        if (hit.collider == null || HUDController.Instance == null || Mouse.current == null) return;

        GameObject selectedObject = hit.collider.gameObject;
        ExplodableViewNode part = selectedObject.GetComponentInParent<ExplodableViewNode>();
        Vector2 pointer = Mouse.current.position.ReadValue();

        if (part != null) HUDController.Instance.ShowClickContext(pointer, part);
        else HUDController.Instance.ShowClickContext(pointer, selectedObject); // shows its name only

        if (focusOnClick) FocusOn(part != null ? part.gameObject : selectedObject);
    }

    /// <summary>Glides the orbit camera to the object and frames it (keeps the current viewing angle).</summary>
    public void FocusOn(GameObject target)
    {
        if (target == null || !TryGetBounds(target, out Bounds bounds)) return;
        FocusOnBounds(bounds, focusMinDistance);
        focusedOnPart = true;
    }

    /// <summary>Back to the whole equipment (all parts, as they are now - exploded or not): orbit around its
    /// centre, everything in view.</summary>
    public void FrameAll()
    {
        if (explodedViewRef == null || !TryGetBounds(explodedViewRef.gameObject, out Bounds bounds)) return;
        FocusOnBounds(bounds, minZoomDistance);
        focusedOnPart = false;
    }

    private static bool TryGetBounds(GameObject target, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private void FocusOnBounds(Bounds bounds, float minimumDistance)
    {
        if (targetCamera == null) return;

        float halfVertical = targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float halfHorizontal = Mathf.Atan(Mathf.Tan(halfVertical) * targetCamera.aspect);
        float distance = bounds.extents.magnitude * focusPadding / Mathf.Sin(Mathf.Max(0.05f, Mathf.Min(halfVertical, halfHorizontal)));

        focusFromPivot = _pivot;
        focusFromDistance = _distance;
        focusToPivot = bounds.center;
        focusToDistance = Mathf.Clamp(distance, minimumDistance, Mathf.Max(maxZoomDistance, minimumDistance));
        focusTime = 0f;
        focusing = true;
    }

    private void UpdateFocusGlide()
    {
        if (!focusing) return;
        if (IsPanning) { focusing = false; return; } // the user takes over

        focusTime += Time.unscaledDeltaTime;
        float t = focusSeconds > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(focusTime / focusSeconds)) : 1f;
        _pivot = Vector3.Lerp(focusFromPivot, focusToPivot, t);
        _distance = Mathf.Lerp(focusFromDistance, focusToDistance, t);
        if (t >= 1f) focusing = false;
    }

    private ModularExplodedView GetExplodable(GameObject obj)
    {
        var explodable = obj.GetComponent<ModularExplodedView>();

        if (explodable != null)
            return explodable;

        explodable = obj.GetComponentInParent<ModularExplodedView>();

        if (explodable != null)
            return explodable;

        Debug.LogWarning($"No explodable {obj}");
        return null;
    }

    private void HandleOrbit()
    {
        if (!IsOrbiting || Mouse.current == null) return;

        var mouseDelta = Mouse.current.delta.ReadValue();

        _yaw += mouseDelta.x * orbitSpeed;
        _pitch -= mouseDelta.y * orbitSpeed;

        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
    }

    private void HandlePan()
    {
        if (!IsPanning || Mouse.current == null) return;

        var mouseDelta = Mouse.current.delta.ReadValue();

        var right = targetCamera.transform.right;
        var up = targetCamera.transform.up;

        _pivot -= right * mouseDelta.x * panSpeed;
        _pivot -= up * mouseDelta.y * panSpeed;
    }

    private void HandleZoom()
    {
        if (IsPointerOverUI()) return; // scrolling a React panel must not zoom

        float zoom = ZoomInput.y;
        if (Mathf.Abs(zoom) < 0.001f) return;

        focusing = false; // the user takes over
        _distance -= zoom * zoomSpeed * 0.01f;
        // After a part focus the camera may stay closer than the normal minimum.
        _distance = Mathf.Clamp(_distance, focusedOnPart ? Mathf.Min(minZoomDistance, focusMinDistance) : minZoomDistance, maxZoomDistance);
    }

    private void UpdateCameraPosition()
    {
        float dt = Time.unscaledDeltaTime;
        if (smoothTime > 0f && dt > 0f)
        {
            curYaw = Mathf.SmoothDampAngle(curYaw, _yaw, ref yawVelocity, smoothTime, Mathf.Infinity, dt);
            curPitch = Mathf.SmoothDamp(curPitch, _pitch, ref pitchVelocity, smoothTime, Mathf.Infinity, dt);
            curDistance = Mathf.SmoothDamp(curDistance, _distance, ref distanceVelocity, smoothTime, Mathf.Infinity, dt);
            curPivot = Vector3.SmoothDamp(curPivot, _pivot, ref pivotVelocity, smoothTime, Mathf.Infinity, dt);
        }
        else SnapSmoothing();

        var rotation = Quaternion.Euler(curPitch, curYaw, 0f);
        var direction = rotation * Vector3.back;

        targetCamera.transform.position = curPivot + direction * RoomLimitedDistance(curPivot, direction, curDistance);
        targetCamera.transform.rotation = rotation;
    }

    private void SnapSmoothing()
    {
        curYaw = _yaw;
        curPitch = _pitch;
        curDistance = _distance;
        curPivot = _pivot;
        yawVelocity = pitchVelocity = distanceVelocity = 0f;
        pivotVelocity = Vector3.zero;
    }

    // Shortens the orbit distance so the camera stops in front of walls / the room box instead of leaving the room.
    private float RoomLimitedDistance(Vector3 pivot, Vector3 direction, float distance)
    {
        if (roomLayers.value == 0) return distance;
        float limit = distance;

        if (Physics.SphereCast(pivot, wallMargin, direction, out RaycastHit hit, distance, roomLayers, QueryTriggerInteraction.Ignore))
            limit = Mathf.Min(limit, hit.distance);

        if (hasRoomBox && roomBox.Contains(pivot))
        {
            for (int axis = 0; axis < 3; axis++)
            {
                if (direction[axis] > 0.0001f) limit = Mathf.Min(limit, (roomBox.max[axis] - pivot[axis]) / direction[axis]);
                else if (direction[axis] < -0.0001f) limit = Mathf.Min(limit, (roomBox.min[axis] - pivot[axis]) / direction[axis]);
            }
        }

        return Mathf.Max(limit, Mathf.Min(minCameraDistance, distance));
    }

    private void FindRoomBox()
    {
        hasRoomBox = false;
        if (roomLayers.value == 0) return;

        float largest = 0f;
        foreach (Collider c in Physics.OverlapSphere(_pivot, 0.05f, roomLayers, QueryTriggerInteraction.Ignore))
        {
            if (!(c is BoxCollider) || !c.bounds.Contains(_pivot)) continue;
            float size = c.bounds.size.x * c.bounds.size.z;
            if (size <= largest || c.bounds.size.x < 5f || c.bounds.size.z < 5f) continue; // a room, not a box on the floor
            largest = size;
            roomBox = c.bounds;
            hasRoomBox = true;
        }

        if (hasRoomBox) roomBox.Expand(-2f * wallMargin);
    }

    public void Focus(Vector3 position)
    {
        _pivot = position;
    }

    private static void EnableAction(InputActionReference actionReference)
    {
        if (actionReference != null)
            actionReference.action.Enable();
    }

    private static void DisableAction(InputActionReference actionReference)
    {
        if (actionReference != null)
            actionReference.action.Disable();
    }

    private Vector3 GetObjectCenter(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
            return obj.transform.position;

        var bounds = renderers[0].bounds;

        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds.center;
    }

    public void GetMouseHoveredObject()
    {
        if (targetCamera == null || Mouse.current == null)
            return;

        if (IsPointerOverUI())
        {
            if (currentHoveredPart != null) currentHoveredPart.OnHoverExit();
            currentHoveredPart = null;
            return;
        }

        var ray = targetCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue()
        );

        Debug.DrawLine(
            ray.origin,
            ray.origin + ray.direction * 100f,
            Color.green
        );

        ExplodableViewNode newHoveredPart = null;

        if (Physics.Raycast(
                ray,
                out var hit,
                maxInteractDistance,
                interactableLayers,
                QueryTriggerInteraction.Ignore))
        {
            Debug.DrawLine(ray.origin, hit.point, Color.red);

            hit.collider.gameObject.TryGetComponent(
                out newHoveredPart
            );
        }

        // Mouse moved from one part to another
        if (newHoveredPart != currentHoveredPart)
        {
            // Exit previous part
            if (currentHoveredPart != null) currentHoveredPart.OnHoverExit();

            // Enter new part
            if (newHoveredPart != null) newHoveredPart.OnHoverEnter();

            currentHoveredPart = newHoveredPart;
        }
    }
}