using UnityEngine;

/// <summary>
/// Optional test bridge. All character-package integration is kept here,
/// separate from the independent SceneInspectionCamera.
/// Attach to an always-active scene UI object, not the persistent player.
/// The referenced camera GameObject is activated/deactivated as a complete
/// unit, so custom scripts on that camera rig do not need package-specific
/// suspension checks.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraSwitchButton : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerReference playerReference;
    [Tooltip("The complete custom-camera rig to activate and deactivate. Do not assign the Player camera.")]
    [SerializeField] private GameObject sceneCameraObject;
    [Tooltip("The SceneInspectionCamera component on the assigned rig or one of its children.")]
    [SerializeField] private SceneInspectionCamera sceneCamera;

    private Player controlledPlayer;
    private bool usingSceneCamera;

    private void Awake()
    {
        if (sceneCameraObject == null || sceneCameraObject == gameObject)
            return;

        // The bridge is the owner of the complete camera-rig lifecycle.
        // This also guarantees the rig starts inactive if it was left active
        // while preparing the example scene.
        sceneCameraObject.SetActive(false);
    }

    // Assign this method to your camera-switch Button's On Click event.
    public void ToggleCamera()
    {
        if (usingSceneCamera) DisableSceneCamera();
        else EnableSceneCamera();
    }

    public void EnableSceneCamera()
    {
        if (usingSceneCamera) return;
        if (playerReference == null || !playerReference.HasPlayer)
        {
            Debug.LogWarning("Assign the shared PlayerReference asset and wait for the player to initialize.", this);
            return;
        }
        if (sceneCameraObject == null || sceneCamera == null)
        {
            Debug.LogWarning("Assign both the custom Scene Camera Root and its SceneInspectionCamera component.", this);
            return;
        }
        if (!IsSceneCameraReferenceValid())
        {
            Debug.LogWarning("SceneInspectionCamera must be on the assigned Scene Camera Root or one of its children. Do not assign the Player camera.", this);
            return;
        }
        if (transform == sceneCameraObject.transform || transform.IsChildOf(sceneCameraObject.transform))
        {
            Debug.LogError("CameraSwitchButton must be outside the assigned Scene Camera Root so it remains active while the rig is disabled.", this);
            return;
        }
        Player player = playerReference.Current;
        if (player.CurrentScene != gameObject.scene || sceneCameraObject.scene != gameObject.scene)
        {
            Debug.LogWarning("The button and scene camera must belong to the current gameplay scene.", this);
            return;
        }

        bool cameraObjectWasActive = sceneCameraObject.activeSelf;
        if (!cameraObjectWasActive)
            sceneCameraObject.SetActive(true);

        if (!sceneCamera.CanEnable)
        {
            if (!cameraObjectWasActive)
                sceneCameraObject.SetActive(false);
            Debug.LogWarning("The SceneInspectionCamera must have an active Focus Target.", this);
            return;
        }

        bool wasSuspended = player.IsControlSuspended;
        playerReference.SuspendCharacterControl();
        if (!player.IsControlSuspended)
        {
            if (!cameraObjectWasActive)
                sceneCameraObject.SetActive(false);
            return;
        }

        sceneCamera.EnableCamera();
        if (!sceneCamera.IsInspecting)
        {
            if (!wasSuspended) playerReference.ResumeCharacterControl();
            if (!cameraObjectWasActive)
                sceneCameraObject.SetActive(false);
            return;
        }
        controlledPlayer = player;
        usingSceneCamera = true;
        controlledPlayer.OnControlResumed.AddListener(HandleExternalResume);
    }

    public void DisableSceneCamera()
    {
        if (!usingSceneCamera) return;
        StopSceneCameraObject();
        if (controlledPlayer != null && playerReference != null &&
            playerReference.Current == controlledPlayer && controlledPlayer.CurrentScene == gameObject.scene)
        {
            playerReference.ResumeCharacterControl();
            // Resume can be deferred while a scene transition is in progress.
            if (controlledPlayer != null && controlledPlayer.IsControlSuspended)
            {
                if (sceneCameraObject != null && sceneCamera != null)
                {
                    sceneCameraObject.SetActive(true);
                    if (sceneCamera.CanEnable) sceneCamera.EnableCamera();
                }
                return;
            }
        }
        ClearOwnership();
    }

    private void LateUpdate()
    {
        if (!usingSceneCamera) return;
        if (controlledPlayer == null || controlledPlayer.CurrentScene != gameObject.scene ||
            !controlledPlayer.IsControlSuspended)
        {
            HandleExternalResume();
            return;
        }
        // Recover if the orbit target was removed or the camera was disabled during inspection.
        if (sceneCamera == null || !sceneCamera.IsInspecting) DisableSceneCamera();
    }

    private void HandleExternalResume()
    {
        StopSceneCameraObject();
        ClearOwnership();
    }

    private void StopSceneCameraObject()
    {
        if (sceneCamera != null)
            sceneCamera.DisableCamera();
        if (sceneCameraObject != null)
            sceneCameraObject.SetActive(false);
    }

    private bool IsSceneCameraReferenceValid()
    {
        Transform cameraTransform = sceneCamera.transform;
        Transform rootTransform = sceneCameraObject.transform;
        return cameraTransform == rootTransform || cameraTransform.IsChildOf(rootTransform);
    }

    private void ClearOwnership()
    {
        if (controlledPlayer != null)
            controlledPlayer.OnControlResumed.RemoveListener(HandleExternalResume);
        controlledPlayer = null;
        usingSceneCamera = false;
    }

    private void OnDisable()
    {
        // Scene unloading must not resume the player into the old scene.
        // For a normal handoff, call DisableSceneCamera before disabling this bridge.
        if (usingSceneCamera) StopSceneCameraObject();
        ClearOwnership();
    }
}
