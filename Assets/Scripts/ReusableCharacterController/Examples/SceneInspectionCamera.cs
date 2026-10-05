using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Scene-owned test camera. The complete camera GameObject is controlled by
/// CameraSwitchButton. This script only handles its own orbit behaviour.
/// Independent orbit camera: no character-package references or handoff events.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class SceneInspectionCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform focusTarget;
    [SerializeField] private Vector3 targetOffset;

    [Header("Orbit: hold right mouse button")]
    [SerializeField] private float initialYaw = 0f;
    [SerializeField, Range(-85f, 85f)] private float initialPitch = 20f;
    [SerializeField, Min(0f)] private float orbitSensitivity = 0.2f;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -80f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 80f;

    [Header("Zoom: mouse wheel")]
    [SerializeField, Min(0.01f)] private float initialDistance = 5f;
    [SerializeField, Min(0.01f)] private float minimumDistance = 0.5f;
    [SerializeField, Min(0.01f)] private float maximumDistance = 30f;
    [SerializeField, Min(0f)] private float zoomSensitivity = 0.0015f;

    public bool IsInspecting { get; private set; }
    public bool CanEnable => isActiveAndEnabled && focusTarget != null && focusTarget.gameObject.activeInHierarchy;

    private Camera inspectionCamera;
    private bool dragging;
    private float yaw, pitch, distance;
    private int activationFrame;

    private void Awake()
    {
        inspectionCamera = GetComponent<Camera>();
        ResetOrbit();
    }

    public void EnableCamera()
    {
        if (IsInspecting) return;
        if (!CanEnable)
        {
            Debug.LogWarning("Enable this script and assign an active Focus Target before enabling the camera.", this);
            return;
        }
        IsInspecting = true;
        dragging = false;
        activationFrame = Time.frameCount;
        UpdatePose();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void DisableCamera() { StopLocalInspection(); }

    // Compatibility with buttons configured for the earlier test script.
    public void StartInspection() { EnableCamera(); }
    public void EndInspection() { DisableCamera(); }

    public void ToggleInspection()
    {
        if (IsInspecting) EndInspection(); else StartInspection();
    }

    // Optional third UI button to reset this camera's view.
    public void ResetOrbit()
    {
        yaw = initialYaw;
        pitch = Mathf.Clamp(initialPitch, minimumPitch, maximumPitch);
        distance = Mathf.Clamp(initialDistance, MinimumDistance, MaximumDistance);
        if (IsInspecting && focusTarget != null) UpdatePose();
    }

    private void LateUpdate()
    {
        if (!IsInspecting) return;
        if (focusTarget == null || !focusTarget.gameObject.activeInHierarchy)
        {
            EndInspection();
            return;
        }

        Mouse mouse = Mouse.current;
        if (Time.frameCount != activationFrame && Application.isFocused && mouse != null)
        {
            // For this uGUI-button test, UI input must not also orbit/zoom the camera.
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUI || !inspectionCamera.pixelRect.Contains(mouse.position.ReadValue()))
                dragging = false;
            else
            {
                if (mouse.rightButton.wasPressedThisFrame) dragging = true;
                if (!mouse.rightButton.isPressed) dragging = false;
                if (dragging)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * orbitSensitivity;
                    pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, minimumPitch, maximumPitch);
                }
                distance = Mathf.Clamp(distance * Mathf.Exp(-mouse.scroll.ReadValue().y * zoomSensitivity),
                    MinimumDistance, MaximumDistance);
            }
        }
        else dragging = false;
        UpdatePose();
    }

    private float MinimumDistance => Mathf.Max(0.01f, minimumDistance);
    private float MaximumDistance => Mathf.Max(MinimumDistance, maximumDistance);

    private void UpdatePose()
    {
        Vector3 center = focusTarget.TransformPoint(targetOffset);
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);
    }

    private void StopLocalInspection()
    {
        IsInspecting = false;
        dragging = false;
    }

    private void OnDisable()
    {
        // Local cleanup only. This camera never enables or resumes another controller.
        StopLocalInspection();
    }
}
