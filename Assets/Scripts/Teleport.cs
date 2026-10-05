using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class Teleport : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject plantFloor;
    [SerializeField] private PlantIsometricCameraController cameraController;
    [SerializeField] private Player workerPlayer;

    [Header("Marker")]
    [SerializeField] private Sprite teleportMarkerSprite;
    [SerializeField] private Color markerColor = new Color(0f, 0.92f, 1f, 0.95f);
    [SerializeField] private float markerSize = 2.5f;
    [SerializeField] private float verticalOffset = 0.04f;
    [SerializeField] private float pulseSpeed = 3f;
    [SerializeField] private float pulseAmplitude = 0.12f;
    [SerializeField] private float minimumAlpha = 0.45f;
    [SerializeField] private bool rotateMarker = true;
    [SerializeField] private float rotationSpeed = 40f;

    [Header("Teleport")]
    [SerializeField] private float workerYOffset = 0.08f;
    [SerializeField] private float clickDragThreshold = 6f;

    private GameObject marker;
    private Transform markerTransform;
    private SpriteRenderer markerRenderer;

    private Transform plantFloorTransform;

    private Vector3 baseMarkerScale;
    private float markerSpinAngle;
    private bool rightMouseCandidate;
    private Vector2 rightMouseDownPosition;
    private Mouse mouse;

    private void Awake()
    {
        mouse = Mouse.current;
        ResolveReferences();
        CreateMarker();
    }

    private void OnEnable()
    {
        ResolveReferences();
        rightMouseCandidate = false;
        HideMarker();
    }

    private void OnDisable()
    {
        rightMouseCandidate = false;
        HideMarker();
    }

    private void OnDestroy()
    {
        if (marker != null) Destroy(marker);
    }

    private void Update()
    {
        if (cameraController == null || !cameraController.IsInIsometricMode)
        {
            HideMarker();
            rightMouseCandidate = false;
            return;
        }

        if (mouse == null) mouse = Mouse.current;

        if (cameraController.PointerOverUI)
        {
            HideMarker();
            rightMouseCandidate = false;
            return;
        }

        if (cameraController.TryGetPointerHit(out RaycastHit hit) && IsPlantFloor(hit.collider))
        {
            UpdateMarker(hit.point, hit.normal);
            HandleTeleportClick(cameraController.PointerPosition, hit.point);
            return;
        }

        HideMarker();

        if (IsRightMouseReleased()) rightMouseCandidate = false;
    }

    private void ResolveReferences()
    {
        if (cameraController == null) cameraController = FindAnyObjectByType<PlantIsometricCameraController>(FindObjectsInactive.Include);
        if (workerPlayer == null) workerPlayer = FindAnyObjectByType<Player>(FindObjectsInactive.Include);
        if (plantFloor == null) plantFloor = GameObject.Find("PlantFloor");

        if (plantFloor != null) plantFloorTransform = plantFloor.transform;
    }

    private bool IsPlantFloor(Collider hitCollider)
    {
        if (hitCollider == null || plantFloorTransform == null) return false;

        Transform hitTransform = hitCollider.transform;
        return hitTransform == plantFloorTransform || hitTransform.IsChildOf(plantFloorTransform);
    }

    private void HandleTeleportClick(Vector2 mousePosition, Vector3 hitPoint)
    {
        if (IsRightMousePressed())
        {
            rightMouseCandidate = true;
            rightMouseDownPosition = mousePosition;
            return;
        }

        if (!IsRightMouseReleased() || !rightMouseCandidate) return;

        rightMouseCandidate = false;

        float threshold = clickDragThreshold * clickDragThreshold;

        if ((rightMouseDownPosition - mousePosition).sqrMagnitude <= threshold) ExecuteTeleport(hitPoint);
    }

    public void ExecuteTeleport(Vector3 destination)
    {
        if (cameraController == null || !cameraController.IsInIsometricMode) return;

        HideMarker();

        if (workerPlayer == null) ResolveReferences();
        if (workerPlayer == null)
        {
            Debug.LogWarning("[Teleport] Worker not found.", this);
            return;
        }

        // The worker is switched off while in overview, so it is placed while being switched back on:
        // the camera controller turns the worker on at this position, always in TPP, and turns the
        // overview camera off.
        Vector3 workerPosition = new Vector3(destination.x, destination.y + workerYOffset, destination.z);
        cameraController.EnableWorkerModeAt(workerPosition);
    }

    private void CreateMarker()
    {
        if (marker != null) return;

        marker = new GameObject("TeleportMarker");
        markerTransform = marker.transform;
        markerTransform.SetParent(transform, false);

        markerRenderer = marker.AddComponent<SpriteRenderer>();
        markerRenderer.sprite = teleportMarkerSprite;
        markerRenderer.color = markerColor;
        markerRenderer.sortingOrder = 100;
        markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markerRenderer.receiveShadows = false;

        baseMarkerScale = Vector3.one * markerSize;
        markerTransform.localScale = baseMarkerScale;

        marker.SetActive(false);
    }

    private void UpdateMarker(Vector3 position, Vector3 normal)
    {
        if (marker == null) CreateMarker();
        if (markerRenderer == null) return;

        if (!marker.activeSelf) marker.SetActive(true);

        markerTransform.position = position + normal * verticalOffset;

        // The rotation is rebuilt from the surface normal every frame, so the spin must be accumulated
        // separately (a per-frame Rotate() on top would be overwritten next frame and never spin).
        if (rotateMarker) markerSpinAngle = Mathf.Repeat(markerSpinAngle + rotationSpeed * Time.deltaTime, 360f);

        Quaternion surfaceRotation = normal.sqrMagnitude > 0.001f ? Quaternion.FromToRotation(Vector3.forward, normal) : markerTransform.rotation;
        markerTransform.rotation = rotateMarker ? surfaceRotation * Quaternion.AngleAxis(markerSpinAngle, Vector3.forward) : surfaceRotation;

        float sine = Mathf.Sin(Time.time * pulseSpeed);
        float pulse = (sine + 1f) * 0.5f;

        markerTransform.localScale = baseMarkerScale * (1f + sine * pulseAmplitude);

        Color color = markerColor;
        color.a = Mathf.Lerp(minimumAlpha, markerColor.a, pulse);
        markerRenderer.color = color;
    }

    private void HideMarker()
    {
        if (marker != null && marker.activeSelf) marker.SetActive(false);
    }

    private bool IsRightMousePressed()
    {
        return mouse != null && mouse.rightButton.wasPressedThisFrame;
    }

    private bool IsRightMouseReleased()
    {
        return mouse != null && mouse.rightButton.wasReleasedThisFrame;
    }
}