using UnityEngine;

// Read velocity after CharacterMovementController's LateUpdate (order 100).
[DefaultExecutionOrder(200)]
[RequireComponent(typeof(Camera))]
public class MiniMap : MonoBehaviour
{
    [Header("Player Reference")]
    [SerializeField] private CharacterMovementController movementController;

    [Header("Orthographic Sizes")]
    [SerializeField, Min(0.01f)] private float stationarySize = 5f;
    [SerializeField, Min(0.01f)] private float walkSize = 8f;
    [SerializeField, Min(0.01f)] private float runSize = 12f;

    [Header("Speed Thresholds (Units Per Second)")]
    [SerializeField, Min(0.01f)] private float walkSpeedThreshold = 1f;
    [SerializeField, Min(0.01f)] private float runSpeedThreshold = 4f;

    [Header("Performance (WebGL)")]
    [Tooltip("Render the minimap only every N frames. 1 = every frame (old behaviour), 2 = half the cost, etc. " +
             "The render texture keeps showing the last image in between, so it still looks continuous.")]
    [SerializeField, Range(1, 10)] private int renderEveryNFrames = 2;

    private const float ZoomSmoothTime = 0.25f;
    private float zoomVelocity;
    private Camera minimapCamera;
    private bool cameraEnabledByDefault = true;

    private void Awake()
    {
        minimapCamera = GetComponent<Camera>();
        minimapCamera.orthographic = true;
        cameraEnabledByDefault = minimapCamera.enabled;
    }

    private void OnEnable()
    {
        zoomVelocity = 0f;

        // Render immediately when the minimap becomes visible again.
        if (minimapCamera != null) minimapCamera.enabled = cameraEnabledByDefault;
    }

    private void OnDisable()
    {
        zoomVelocity = 0f;

        // Disabling this component (e.g. in plant overview) also stops the camera from rendering.
        if (minimapCamera != null) minimapCamera.enabled = false;
    }

    private void LateUpdate()
    {
        if (Time.deltaTime <= 0f || minimapCamera == null) return;
        float speed = movementController != null
            ? movementController.Velocity.magnitude : 0f;

        float walkThreshold = Mathf.Max(0.01f, walkSpeedThreshold);
        float runThreshold = Mathf.Max(walkThreshold + 0.01f, runSpeedThreshold);
        float targetSize = speed >= runThreshold ? runSize
            : speed >= walkThreshold ? walkSize
            : stationarySize;

        minimapCamera.orthographicSize = Mathf.SmoothDamp(
            minimapCamera.orthographicSize, Mathf.Max(0.01f, targetSize),
            ref zoomVelocity, ZoomSmoothTime, Mathf.Infinity, Time.deltaTime);

        // Frame skipping: the camera is only enabled on the frames it should render.
        if (cameraEnabledByDefault && renderEveryNFrames > 1)
            minimapCamera.enabled = Time.frameCount % renderEveryNFrames == 0;
    }

    private void OnValidate()
    {
        stationarySize = Mathf.Max(0.01f, stationarySize);
        walkSize = Mathf.Max(0.01f, walkSize);
        runSize = Mathf.Max(0.01f, runSize);
        walkSpeedThreshold = Mathf.Max(0.01f, walkSpeedThreshold);
        runSpeedThreshold = Mathf.Max(walkSpeedThreshold + 0.01f, runSpeedThreshold);
        renderEveryNFrames = Mathf.Max(1, renderEveryNFrames);
    }
}
