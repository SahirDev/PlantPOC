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
                // Debug.Log($"Nothing hit");
                if (HUDController.Instance != null)
                {
                    HUDController.Instance.HideClickContext();
                    HUDController.Instance.HideDescription();
                }
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

        UpdateCameraPosition();

        // Part names show on click only (PartInfoCard); hover detection is off unless switched on.
        if (showPartNameOnHover) GetMouseHoveredObject();
    }

    private void HandleHit(RaycastHit hit)
    {
        var selectedObject = hit.collider.gameObject;

        if (selectedObject == null || hit.collider == null)
        {
            if (HUDController.Instance != null)
            {
                HUDController.Instance.HideClickContext();
                HUDController.Instance.HideDescription();
            }
        }

        if (selectedObject.TryGetComponent<ExplodableViewNode>(out var part))
        {
            if (HUDController.Instance != null && Mouse.current != null)
            {
                HUDController.Instance.ShowClickContext(
                    Mouse.current.position.ReadValue(),
                    part
                );
            }

            // Debug.Log($"Selected Part: {selectedObject.name}");
            return;
        }

        var explodable = GetExplodable(selectedObject);

        if (explodable != null)
        {
            explodable.ToggleExplode(selectedObject.transform);
            Focus(GetObjectCenter(selectedObject));
        }

        // Show the clicked object's name too (parts without an ExplodableViewNode).
        if (HUDController.Instance != null && Mouse.current != null)
            HUDController.Instance.ShowClickContext(Mouse.current.position.ReadValue(), selectedObject);

        Debug.Log($"Hit: {selectedObject.name}");

        // var selectedObject = hit.collider.gameObject;
        // var explodable = GetExplodable(selectedObject);
        //
        // if (explodable != null)
        // {
        //     explodable.ToggleExplode(selectedObject.transform);
        //     Focus(GetObjectCenter(selectedObject));
        // }
        //
        // Debug.Log($"Hit: {selectedObject.name}");
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

        _distance -= ZoomInput.y * zoomSpeed * 0.01f;
        _distance = Mathf.Clamp(_distance, minZoomDistance, maxZoomDistance);
    }

    private void UpdateCameraPosition()
    {
        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        var offset = rotation * Vector3.back * _distance;

        targetCamera.transform.position = _pivot + offset;
        targetCamera.transform.rotation = rotation;
    }

    public void Focus(Vector3 position)
    {
        _pivot = position;
        UpdateCameraPosition();
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