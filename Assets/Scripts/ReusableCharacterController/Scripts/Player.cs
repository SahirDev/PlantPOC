using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

// Start runs after the existing controllers initialize their camera offsets and effects.
[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterInputReader), typeof(CharacterMovementController), typeof(CharacterCameraController))]
[RequireComponent(typeof(CharacterViewStateMachine), typeof(CharacterViewModeEffects), typeof(CharacterFocusController))]
public sealed class Player : MonoBehaviour
{
    [SerializeField]
    private PlayerReference playerReference;

    [Tooltip("Optional extra custom behaviours to disable during suspension. Do not add Player, PersistentPlayer, or the six character controllers.")]
    [SerializeField]
    private Behaviour[] additionalBehavioursToSuspend;

    [SerializeField]
    private UnityEvent onControlSuspended = new();

    [SerializeField]
    private UnityEvent onControlResumed = new();

    public CharacterInputReader InputReader { get; private set; }
    public CharacterMovementController MovementController { get; private set; }
    public CharacterCameraController CameraController { get; private set; }
    public CharacterFocusController FocusController { get; private set; }
    public CharacterViewStateMachine ViewStateMachine { get; private set; }
    public CharacterViewModeEffects ViewModeEffects { get; private set; }
    public PlayerReference Reference => playerReference;
    public bool IsControlSuspended { get; private set; }
    public bool IsControlBlocked => IsControlSuspended || changingControl || scenePending || loadingScene || Time.frameCount <= blockThroughFrame
        || (ExternalInputBlock != null && ExternalInputBlock());

    /// <summary>Extra block from outside this assembly (set by UITextInput: true while typing in a Unity text field).</summary>
    public static System.Func<bool> ExternalInputBlock;
    public Scene CurrentScene => gameplayScene;
    public UnityEvent OnControlSuspended => onControlSuspended;
    public UnityEvent OnControlResumed => onControlResumed;

    private Scene gameplayScene;
    private bool started, changingControl, loadingScene;
    private int blockThroughFrame = -1;
    private SuspensionState suspendedState;
    private readonly Dictionary<string, VisitState> visits = new();
    private Scene pendingScene;
    private bool scenePending;
    private bool savedBeforeLoad;

    private void Awake()
    {
        EnsureControllers();
    }

    private void EnsureControllers()
    {
        if (InputReader == null) InputReader = GetComponent<CharacterInputReader>();
        if (MovementController == null) MovementController = GetComponent<CharacterMovementController>();
        if (CameraController == null) CameraController = GetComponent<CharacterCameraController>();
        if (FocusController == null) FocusController = GetComponent<CharacterFocusController>();
        if (ViewStateMachine == null) ViewStateMachine = GetComponent<CharacterViewStateMachine>();
        if (ViewModeEffects == null) ViewModeEffects = GetComponent<CharacterViewModeEffects>();
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        if (PersistentPlayer.Instance == null || PersistentPlayer.Instance.gameObject != gameObject) return;
        if (CameraController.PlayerCamera == null || !CameraController.PlayerCamera.transform.IsChildOf(transform))
        {
            Debug.LogError("Assign a Camera inside the Player prefab to CharacterCameraController.", this);
            SuspendCharacterControl();
            return;
        }

        started = true;
        ApplyScene(SceneManager.GetActiveScene());
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (playerReference != null) playerReference.Unregister(this);
    }

    public void SuspendCharacterControl()
    {
        if (IsControlSuspended || changingControl) return;
        EnsureControllers();
        changingControl = true;
        try
        {
            if (MovementController != null) MovementController.CancelLadderClimbing();
            suspendedState = CaptureSuspension();
            IsControlSuspended = true;
            SetPrivateInputSuspended(true);
            FocusController.PrepareSuspension();
            DisablePresence(suspendedState);
            // Release once. While suspended none of the package scripts writes cursor state.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        finally
        {
            changingControl = false;
        }

        onControlSuspended.Invoke();
    }

    public void ResumeCharacterControl()
    {
        if (!IsControlSuspended || changingControl || loadingScene || scenePending) return;
        EnsureControllers();
        changingControl = true;
        try
        {
            RestoreSuspension(suspendedState);
            suspendedState = null;
            IsControlSuspended = false;
            if (FocusController.HasFocusSession &&
                (FocusController.FocusTarget == null || !FocusController.FocusTarget.gameObject.activeInHierarchy))
            {
                var fallback = FocusController.ReturnMode;
                FocusController.EndFocusForScene();
                ViewStateMachine.SetSceneMode(fallback);
                ViewModeEffects.ResetForScene(ViewStateMachine.CurrentMode);
                CameraController.ResetForScene(transform.eulerAngles.y);
            }

            SetPrivateInputSuspended(false);
            blockThroughFrame = Time.frameCount; // Do not consume the UI click that resumed us.
            FocusController.SuppressPointerThisFrame();
        }
        finally
        {
            changingControl = false;
        }

        onControlResumed.Invoke();
    }

    // ---------------------------------------------------------------------------------------
    // Plant overview
    // While the plant overview camera is active the worker is completely "off": hidden, no
    // collisions, its camera / audio listener / animator disabled, input paused (this reuses the
    // existing suspension system). The GameObject itself stays active so scene loading, the
    // persistent player and return positions keep working.
    // ---------------------------------------------------------------------------------------

    public void SetPlantOverviewMode(bool active)
    {
        if (active) EnterPlantOverview();
        else ExitPlantOverview(null);
    }

    /// <summary>Switches the worker off for the plant overview. Returns true if the worker is now off.</summary>
    public bool EnterPlantOverview()
    {
        EnsureControllers();
        if (!IsControlSuspended) SuspendCharacterControl();
        return IsControlSuspended;
    }

    /// <summary>
    /// Switches the worker back on. With a teleport position the worker is placed there and always
    /// starts in TPP with the camera snapped behind it. Without one (e.g. the C key) the worker
    /// resumes exactly where and how it was. Returns false if the worker could not be resumed
    /// (e.g. during a scene load); callers should then stay in overview.
    /// </summary>
    public bool ExitPlantOverview(Vector3? teleportPosition)
    {
        EnsureControllers();
        if (IsControlSuspended) ResumeCharacterControl();
        if (IsControlSuspended) return false;

        if (teleportPosition.HasValue) PlaceWorkerInTPP(teleportPosition.Value);

        // Not LockCursor(): it is ignored on the frame control resumes. This internal call is not.
        CameraController.RestoreCursorState(true, CursorLockMode.Locked, false);
        return true;
    }

    private void PlaceWorkerInTPP(Vector3 position)
    {
        MovementController.CancelLadderClimbing();

        // End any object inspection first, otherwise focus mode would pull the worker back to it.
        FocusController.EndFocusForScene();

        ViewStateMachine.SetSceneMode(CharacterViewStateMachine.ViewMode.TPP);

        var controller = GetComponent<CharacterController>();
        var controllerEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        transform.position = position;
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = controllerEnabled;

        // Same reset used when entering a scene: normal capsule, character visible, animators on,
        // and the TPP camera snapped behind the worker (no swoop from the old location).
        ViewModeEffects.ResetForScene(CharacterViewStateMachine.ViewMode.TPP);
        CameraController.ResetForScene(transform.eulerAngles.y);
    }

    private void SetPrivateInputSuspended(bool suspended)
    {
        InputReader.SetInputSuspended(suspended || !InputReader.enabled);
        ViewStateMachine.SetInputSuspended(suspended || !ViewStateMachine.enabled);
        CameraController.SetInputSuspended(suspended || !CameraController.enabled);
    }

    // Call this before an externally managed scene load as well. It releases old scene effects
    // while the target objects still exist, and remembers that scene's return pose.
    public void PrepareForSceneChange()
    {
        if (!started || savedBeforeLoad) return;
        SaveCurrentVisit();
        savedBeforeLoad = true;
        if (!IsControlSuspended) SuspendCharacterControl();
        changingControl = true;
        try
        {
            // Temporarily restore component states only; input/update remains blocked.
            RestoreSuspension(suspendedState);
            IsControlSuspended = false;
            FocusController.EndFocusForScene();
            ViewStateMachine.SetSceneMode(ViewStateMachine.CurrentMode == CharacterViewStateMachine.ViewMode.FocusCam
                ? FocusController.ReturnMode
                : ViewStateMachine.CurrentMode);
            ViewModeEffects.ResetForScene(ViewStateMachine.CurrentMode);
            MovementController.CancelLadderClimbing();
            suspendedState = CaptureSuspension();
            IsControlSuspended = true;
            DisablePresence(suspendedState);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        finally
        {
            changingControl = false;
        }
    }

    // Convenience scene loader for overworld/room travel. Scenes must be in the build scene list.
    public void LoadScene(string sceneNameOrPath)
    {
        if (!started || loadingScene || string.IsNullOrWhiteSpace(sceneNameOrPath)) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneNameOrPath))
        {
            Debug.LogError("Scene is not available in the build scene list: " + sceneNameOrPath, this);
            return;
        }

        StartCoroutine(LoadSceneRoutine(sceneNameOrPath));
    }

    // Convenience scene loader using the scene's Build Settings index.
    public void LoadScene(int sceneIndex)
    {
        if (!started || loadingScene) return;
        if (sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings ||
            !Application.CanStreamedLevelBeLoaded(sceneIndex))
        {
            Debug.LogError("Scene build index is not available in the build scene list: " + sceneIndex, this);
            return;
        }

        StartCoroutine(LoadSceneRoutine(sceneIndex));
    }

    private IEnumerator LoadSceneRoutine(string sceneNameOrPath)
    {
        loadingScene = true;
        PrepareForSceneChange();
        AsyncOperation operation = null;
        try
        {
            operation = SceneManager.LoadSceneAsync(sceneNameOrPath, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        if (operation == null)
        {
            loadingScene = false;
            savedBeforeLoad = false;
            ResumeCharacterControl();
            yield break;
        }

        yield return operation;
        loadingScene = false;
    }

    private IEnumerator LoadSceneRoutine(int sceneIndex)
    {
        loadingScene = true;
        PrepareForSceneChange();
        AsyncOperation operation = null;
        try
        {
            operation = SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }

        if (operation == null)
        {
            loadingScene = false;
            savedBeforeLoad = false;
            ResumeCharacterControl();
            yield break;
        }

        yield return operation;
        loadingScene = false;
    }

    private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (!started) return;
        pendingScene = newScene;
        scenePending = true;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!started) return;
        // Additive helper scenes must not take control unless made active explicitly.
        if (mode == LoadSceneMode.Single || scene == SceneManager.GetActiveScene())
        {
            pendingScene = scene;
            scenePending = true;
        }

        ApplyScene(scene);
    }

    private void LateUpdate()
    {
        if (!started || !scenePending || loadingScene) return;
        scenePending = false;
        // ApplyScene(pendingScene);
    }

    private static string SceneKey(Scene scene)
    {
        return string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
    }

    private void SaveCurrentVisit()
    {
        if (!gameplayScene.IsValid()) return;
        var position = transform.position;
        var rotation = transform.rotation;
        var mode = ViewStateMachine.CurrentMode;
        var angles = CameraController.CaptureAngles();
        var camera = CameraController.PlayerCamera;
        var cameraPosition = camera.transform.position;
        var cameraRotation = camera.transform.rotation;
        var focus = FocusController.HasFocusSession;
        if (focus)
        {
            FocusController.GetReturnPose(out position, out rotation);
            mode = FocusController.ReturnMode;
        }

        visits[SceneKey(gameplayScene)] = new VisitState
        {
            position = position, rotation = rotation, mode = mode, angles = angles,
            cameraPosition = cameraPosition, cameraRotation = cameraRotation,
            restoreCameraPose = !focus,
            groundReturnPosition = ViewModeEffects.CaptureGroundReturnPosition()
        };
    }

    private void ApplyScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        if (gameplayScene.IsValid() && gameplayScene.handle == scene.handle) return;
        if (!savedBeforeLoad) SaveCurrentVisit();
        changingControl = true;
        var wasSuspended = IsControlSuspended;
        try
        {
            if (IsControlSuspended) RestoreSuspension(suspendedState);
            suspendedState = null;
            IsControlSuspended = false;
            FocusController.EndFocusForScene();
            gameplayScene = scene;
            var context = FindContext(scene);
            var settings = context != null ? context.Settings : null;
            var spawn = context != null ? context.SpawnPoint : null;
            var restore = settings == null || settings.restoreLastVisit;
            VisitState visit = null;
            if (restore) visits.TryGetValue(SceneKey(scene), out visit);
            var mode = visit != null ? visit.mode : settings != null ? settings.entryMode : ViewStateMachine.InitialMode;
            ViewStateMachine.SetSceneMode(mode);
            var controller = GetComponent<CharacterController>();
            var controllerEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            if (visit != null) transform.SetPositionAndRotation(visit.position, visit.rotation);
            else if (spawn != null) transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            else if (started) Debug.LogWarning("No PlayerSceneContext found. Keeping the player's current pose for " + scene.name, this);
            Physics.SyncTransforms();
            if (controller != null) controller.enabled = controllerEnabled;
            MovementController.ResetForScene(context != null ? context.MovementBoundsCenter : null,
                context != null ? context.MovementBoundsSize : Vector3.zero);
            ViewModeEffects.ResetForScene(ViewStateMachine.CurrentMode);
            if (visit != null) ViewModeEffects.RestoreGroundReturnPosition(visit.groundReturnPosition);
            CameraController.ResetForScene(transform.eulerAngles.y);
            if (visit != null && visit.restoreCameraPose)
            {
                CameraController.RestoreAngles(visit.angles);
                CameraController.PlayerCamera.transform.SetPositionAndRotation(visit.cameraPosition, visit.cameraRotation);
            }

            FocusController.ClickToFocusEnabled = settings != null && settings.allowClickToFocus; // off unless a scene asks for it
            var lockCursor = settings == null || settings.lockCursorOnEntry;
            CameraController.RestoreCursorState(lockCursor, lockCursor ? CursorLockMode.Locked : CursorLockMode.None, !lockCursor);
            var suspend = settings != null && settings.startSuspended;
            if (suspend)
            {
                suspendedState = CaptureSuspension();
                IsControlSuspended = true;
                FocusController.PrepareSuspension();
                DisablePresence(suspendedState);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            SetPrivateInputSuspended(suspend);
            blockThroughFrame = Time.frameCount;
            FocusController.SuppressPointerThisFrame();
            savedBeforeLoad = false;
            // Release reentrancy guard before external ready events.
            changingControl = false;
            if (suspend && !wasSuspended) onControlSuspended.Invoke();
            if (!suspend && wasSuspended) onControlResumed.Invoke();
            if (playerReference != null) playerReference.Register(this);
            context?.NotifyReady();
        }
        finally
        {
            changingControl = false;
        }
    }

    private PlayerSceneContext FindContext(Scene scene)
    {
        PlayerSceneContext result = null;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var candidate in root.GetComponentsInChildren<PlayerSceneContext>(true))
            if (candidate.isActiveAndEnabled)
            {
                if (result != null) Debug.LogWarning("Multiple PlayerSceneContexts in " + scene.name + "; using the first.", candidate);
                else result = candidate;
            }

        return result;
    }

    public void ForgetSceneReturnPositions()
    {
        visits.Clear();
    }

    private sealed class VisitState
    {
        public Vector3 position, cameraPosition, groundReturnPosition;
        public Quaternion rotation, cameraRotation;
        public Vector2 angles;
        public CharacterViewStateMachine.ViewMode mode;
        public bool restoreCameraPose;
    }

    private sealed class SuspensionState
    {
        public readonly List<TransformState> transforms = new();
        public readonly List<EnabledState<Renderer>> renderers = new();
        public readonly List<EnabledState<Collider>> colliders = new();
        public readonly List<EnabledState<Behaviour>> behaviours = new();
        public readonly List<AnimatorState> animators = new();
        public readonly List<BodyState> bodies = new();
        public readonly List<AudioSource> playingAudio = new();
        public readonly List<ParticleSystem> playingParticles = new();
        public bool cursorLocked, cursorVisible;
        public CursorLockMode cursorLockState;
    }

    private struct EnabledState<T> where T : UnityEngine.Object
    {
        public T component;
        public bool enabled;

        public EnabledState(T component, bool enabled)
        {
            this.component = component;
            this.enabled = enabled;
        }
    }

    private struct TransformState
    {
        public Transform transform;
        public Vector3 position, scale;
        public Quaternion rotation;
    }

    private struct AnimatorState
    {
        public Animator animator;
        public bool keepState;
    }

    private static bool GetKeepAnimatorState(Animator animator)
    {
#if UNITY_6000_0_OR_NEWER
        return animator.keepAnimatorStateOnDisable;
#else
        return animator.keepAnimatorControllerStateOnDisable;
#endif
    }

    private static void SetKeepAnimatorState(Animator animator, bool value)
    {
#if UNITY_6000_0_OR_NEWER
        animator.keepAnimatorStateOnDisable = value;
#else
        animator.keepAnimatorControllerStateOnDisable = value;
#endif
    }

    private static Vector3 GetBodyVelocity(Rigidbody body)
    {
#if UNITY_6000_0_OR_NEWER
        return body.linearVelocity;
#else
        return body.velocity;
#endif
    }

    private static void SetBodyVelocity(Rigidbody body, Vector3 velocity)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = velocity;
#else
        body.velocity = velocity;
#endif
    }

    private struct BodyState
    {
        public Rigidbody body;
        public bool kinematic, collisions;
        public Vector3 velocity, angularVelocity;
    }

    private SuspensionState CaptureSuspension()
    {
        var state = new SuspensionState
        {
            cursorLocked = CameraController.IsCursorLocked,
            cursorVisible = Cursor.visible,
            cursorLockState = Cursor.lockState
        };
        foreach (var item in GetComponentsInChildren<Transform>(true))
            state.transforms.Add(new TransformState { transform = item, position = item.localPosition, rotation = item.localRotation, scale = item.localScale });
        foreach (var item in GetComponentsInChildren<Renderer>(true))
            state.renderers.Add(new EnabledState<Renderer>(item, item.enabled));
        foreach (var item in GetComponentsInChildren<Collider>(true))
            state.colliders.Add(new EnabledState<Collider>(item, item.enabled));
        // CharacterController derives from Collider and is included above.
        foreach (var item in GetComponentsInChildren<Behaviour>(true))
            if (item is Camera || item is AudioListener || item is Animator || item is Light)
                state.behaviours.Add(new EnabledState<Behaviour>(item, item.enabled));
        if (additionalBehavioursToSuspend != null)
            foreach (var item in additionalBehavioursToSuspend)
                if (item != null && !(item is Player) && !(item is PersistentPlayer) &&
                    !(item is CharacterInputReader) && !(item is CharacterViewStateMachine) &&
                    !(item is CharacterMovementController) && !(item is CharacterCameraController) &&
                    !(item is CharacterFocusController) && !(item is CharacterViewModeEffects) &&
                    !state.behaviours.Exists(entry => entry.component == item))
                    state.behaviours.Add(new EnabledState<Behaviour>(item, item.enabled));
        foreach (var item in GetComponentsInChildren<Animator>(true))
            state.animators.Add(new AnimatorState { animator = item, keepState = GetKeepAnimatorState(item) });
        foreach (var item in GetComponentsInChildren<Rigidbody>(true))
            state.bodies.Add(new BodyState
            {
                body = item, kinematic = item.isKinematic,
                collisions = item.detectCollisions, velocity = GetBodyVelocity(item), angularVelocity = item.angularVelocity
            });
        foreach (var item in GetComponentsInChildren<AudioSource>(true))
            if (item.isPlaying)
                state.playingAudio.Add(item);
        foreach (var item in GetComponentsInChildren<ParticleSystem>(true))
            if (item.isPlaying)
                state.playingParticles.Add(item);
        return state;
    }

    private static void DisablePresence(SuspensionState state)
    {
        if (state == null) return;
        foreach (var entry in state.renderers)
            if (entry.component != null)
                entry.component.enabled = false;
        foreach (var entry in state.colliders)
            if (entry.component != null)
                entry.component.enabled = false;
        foreach (var entry in state.animators)
            if (entry.animator != null)
                SetKeepAnimatorState(entry.animator, true);
        foreach (var entry in state.behaviours)
            if (entry.component != null)
                entry.component.enabled = false;
        foreach (var entry in state.bodies)
            if (entry.body != null)
            {
                entry.body.detectCollisions = false;
                entry.body.isKinematic = true;
            }

        foreach (var item in state.playingAudio)
            if (item != null)
                item.Pause();
        foreach (var item in state.playingParticles)
            if (item != null)
                item.Pause(false);
    }

    private void RestoreSuspension(SuspensionState state)
    {
        if (state == null) return;
        foreach (var entry in state.transforms)
            if (entry.transform != null)
            {
                entry.transform.localPosition = entry.position;
                entry.transform.localRotation = entry.rotation;
                entry.transform.localScale = entry.scale;
            }

        Physics.SyncTransforms();
        foreach (var entry in state.bodies)
            if (entry.body != null)
            {
                entry.body.isKinematic = entry.kinematic;
                entry.body.detectCollisions = entry.collisions;
                if (!entry.kinematic)
                {
                    SetBodyVelocity(entry.body, entry.velocity);
                    entry.body.angularVelocity = entry.angularVelocity;
                }
            }

        foreach (var entry in state.colliders)
            if (entry.component != null)
                entry.component.enabled = entry.enabled;
        foreach (var entry in state.renderers)
            if (entry.component != null)
                entry.component.enabled = entry.enabled;
        foreach (var entry in state.behaviours)
            if (entry.component != null)
                entry.component.enabled = entry.enabled;
        foreach (var entry in state.animators)
            if (entry.animator != null)
                SetKeepAnimatorState(entry.animator, entry.keepState);
        foreach (var item in state.playingAudio)
            if (item != null)
                item.UnPause();
        foreach (var item in state.playingParticles)
            if (item != null)
                item.Play(false);
        CameraController.RestoreCursorState(state.cursorLocked, state.cursorLockState, state.cursorVisible);
    }
}