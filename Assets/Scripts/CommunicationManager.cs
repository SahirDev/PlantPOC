using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bridge between Unity and the React UI (react-unity-webgl). One persistent object for the whole session.
///
/// React -> Unity
///   sendMessage("CommunicationManager", "ChangeScene_Extern", "BoilerRoom");
///   Every public method ending in _Extern in the "React -> Unity" regions can be called this way.
///   Parameters are strings: "true"/"false", numbers like "42.5", names, or JSON.
///
/// Unity -> React
///   Game scripts call the static HandleXxx_Extern methods, e.g.
///       CommunicationManager.HandleEquipmentState_Extern(state);
///   which call the function of the same name in Assets/Plugins/WebGL/React.jslib
///   (window.dispatchReactUnityEvent("handleEquipmentState", json)).
///   React: addEventListener("handleEquipmentState", (json) => ...)
///   They are static so they work from anywhere, even while scenes load or the app closes.
///   Only sent in WebGL builds with the REACT_BUILD scripting define (Tools > Thermal Plant >
///   2. Apply WebGL Player Settings adds it). In the Editor they are logged / shown in
///   Tools > React Bridge Debugger.
///
/// The object is created automatically if no scene contains one. Its name must stay "CommunicationManager".
/// </summary>
[DefaultExecutionOrder(-1000)]
public class CommunicationManager : SingletonMono<CommunicationManager>
{
    /// <summary>React calls sendMessage on a GameObject with exactly this name.</summary>
    public const string GameObjectName = "CommunicationManager";

    [Header("Input")]
    [Tooltip("WebGL only. False (recommended) = Unity only gets keyboard input while the canvas has focus, so React text fields work.")]
    [SerializeField] private bool captureAllKeyboardInput = false;
    [Tooltip("WebGL only. Keeps the keys (WASD, Shift, Space, Z) with Unity inside the React page: focuses the canvas " +
             "at start and after clicks on React buttons / sliders. React text fields still get the keyboard while focused.")]
    [SerializeField] private bool autoFocusCanvas = true;

    [Header("Logging")]
    [Tooltip("Log every Unity -> React call to the console (Editor and browser). Noisy with live data.")]
    [SerializeField] private bool logOutgoing = false;
    [Tooltip("Log every React -> Unity call.")]
    [SerializeField] private bool logIncoming = false;

    protected override bool PersistAcrossScenes => true;

    /// <summary>Raised for every Unity -> React call: (jsFunctionName, data). Used by the Editor debugger.</summary>
    public static event Action<string, string> MessageSent;

    private static bool logOutgoingMessages;

    private Player worker;
    private bool lastCursorLocked;
    private bool trackWorker;
    private float trackInterval = 0.1f;
    private float nextTrackTime;

    // ===================================================================================== jslib functions

    [DllImport("__Internal")] private static extern void handleError(string data);
    [DllImport("__Internal")] private static extern void handleSceneLoadFailed(string data);
    [DllImport("__Internal")] private static extern void handleUnityReady(string data);
    [DllImport("__Internal")] private static extern void handleCursorLockChanged(string data);
    [DllImport("__Internal")] private static extern void handleWorkerTransform(string data);
    [DllImport("__Internal")] private static extern void handleSceneLoading(string data);
    [DllImport("__Internal")] private static extern void handleSceneDownloadProgress(string data);
    [DllImport("__Internal")] private static extern void handleSceneLoaded(string data);
    [DllImport("__Internal")] private static extern void handleScenePreloaded(string data);
    [DllImport("__Internal")] private static extern void handleSceneDownloadSize(string data);
    [DllImport("__Internal")] private static extern void handleSceneTriggerEntered(string data);
    [DllImport("__Internal")] private static extern void handleSceneTriggerExited(string data);
    [DllImport("__Internal")] private static extern void handleCameraModeChanged(string data);
    [DllImport("__Internal")] private static extern void handleOverviewObjectSelected(string data);
    [DllImport("__Internal")] private static extern void handleOverviewObjectDeselected();
    [DllImport("__Internal")] private static extern void handleEquipmentInRange(string data);
    [DllImport("__Internal")] private static extern void handleEquipmentOutOfRange(string data);
    [DllImport("__Internal")] private static extern void handleBoilerInRange(string data);
    [DllImport("__Internal")] private static extern void handleTurbineInRange(string data);
    [DllImport("__Internal")] private static extern void handleTurbineOutOfRange(string data);
    [DllImport("__Internal")] private static extern void handleBoilerOutOfRange(string data);
    [DllImport("__Internal")] private static extern void handleEquipmentState(string data);
    [DllImport("__Internal")] private static extern void handlePartHover(string data);
    [DllImport("__Internal")] private static extern void handlePartHoverEnd();
    [DllImport("__Internal")] private static extern void handlePartSelected(string data);
    [DllImport("__Internal")] private static extern void handlePartDeselected();
    [DllImport("__Internal")] private static extern void handlePartMaintenance(string data);
    [DllImport("__Internal")] private static extern void handleTurbineData(string data);
    [DllImport("__Internal")] private static extern void handleTurbineList(string data);
    [DllImport("__Internal")] private static extern void handleBoilerDashboardOpened(string data);
    [DllImport("__Internal")] private static extern void handleBoilerDashboard(string data);
    [DllImport("__Internal")] private static extern void handleBoilerDashboardClosed();
    [DllImport("__Internal")] private static extern void handleElectricalPanelOpened(string data);
    [DllImport("__Internal")] private static extern void handleElectricalValues(string data);
    [DllImport("__Internal")] private static extern void handleElectricalPanelClosed();
    [DllImport("__Internal")] private static extern void handleControlRoomInfoEntered(string data);
    [DllImport("__Internal")] private static extern void handleTimeOfDayChanged(string data);
    [DllImport("__Internal")] private static extern void handleSmokeLevelChanged(string data);
    [DllImport("__Internal")] private static extern void handleTourChanged(string data);
    [DllImport("__Internal")] private static extern void handleControlRoomInfoExited(string data);

    // KeyboardFocus.jslib
    [DllImport("__Internal")] private static extern void KeyboardFocus_Setup(bool autoFocus);
    [DllImport("__Internal")] private static extern void KeyboardFocus_Focus();
    [DllImport("__Internal")] private static extern void KeyboardFocus_SetAuto(bool autoFocus);

    // ===================================================================================== lifecycle

    // Guarantees the bridge exists whichever scene starts first.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (HasInstance) return;

        var bridge = new GameObject(GameObjectName);
        bridge.AddComponent<CommunicationManager>();

        // Scene loading is needed by ChangeScene_Extern; add it if no scene provides one.
        if (!SceneController.HasInstance) new GameObject("SceneController").AddComponent<SceneController>();
    }

    protected override void Awake()
    {
        base.Awake();
        if (IsDuplicate) return;

        gameObject.name = GameObjectName; // sendMessage finds the object by name
        logOutgoingMessages = logOutgoing;
        ApplyKeyboardCapture(captureAllKeyboardInput);
#if UNITY_WEBGL && !UNITY_EDITOR
        KeyboardFocus_Setup(autoFocusCanvas);
#endif

        SceneManager.sceneLoaded += HandleSceneLoaded;
        lastCursorLocked = Cursor.lockState == CursorLockMode.Locked;
    }

    protected override void Start()
    {
        base.Start();
        if (IsDuplicate) return;

        FindWorker();
        HandleUnityReady_Extern(BuildStatus());
    }

    protected override void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        base.OnDestroy();
    }

    private void OnValidate()
    {
        logOutgoingMessages = logOutgoing;
    }

    private void Update()
    {
        // Cursor lock can change from Unity (worker, focus mode) or the browser (Esc).
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (locked != lastCursorLocked)
        {
            lastCursorLocked = locked;
            HandleCursorLockChanged_Extern(locked);
        }

        if (trackWorker && Time.unscaledTime >= nextTrackTime)
        {
            nextTrackTime = Time.unscaledTime + trackInterval;
            SendWorkerTransform();
        }
    }

    // ===================================================================================== React -> Unity

    #region React -> Unity : App / input

    /// <summary>Health check. Answer: handleUnityReady.</summary>
    public void Ping_Extern()
    {
        LogIncoming(nameof(Ping_Extern), null);
        HandleUnityReady_Extern(BuildStatus());
    }

    /// <summary>"true"/"false". False = React text fields receive the keyboard.</summary>
    public void SetKeyboardCapture_Extern(string enabled)
    {
        LogIncoming(nameof(SetKeyboardCapture_Extern), enabled);
        captureAllKeyboardInput = ParseBool(enabled);
        ApplyKeyboardCapture(captureAllKeyboardInput);
    }

    /// <summary>Give the keyboard to Unity now (e.g. after closing a React panel / text field), so WASD works
    /// without clicking the 3D view.</summary>
    public void FocusUnity_Extern()
    {
        LogIncoming(nameof(FocusUnity_Extern), null);
#if UNITY_WEBGL && !UNITY_EDITOR
        KeyboardFocus_Focus();
#endif
    }

    /// <summary>"true" (default) / "false". True = after clicks on React buttons / sliders the keys go back to
    /// Unity automatically. False = Unity only gets keys after a click on the 3D view or FocusUnity_Extern.</summary>
    public void SetKeyboardAutoFocus_Extern(string enabled)
    {
        LogIncoming(nameof(SetKeyboardAutoFocus_Extern), enabled);
        autoFocusCanvas = ParseBool(enabled);
#if UNITY_WEBGL && !UNITY_EDITOR
        KeyboardFocus_SetAuto(autoFocusCanvas);
#endif
    }

    /// <summary>"true" on mouseenter / "false" on mouseleave of every React panel over the canvas.
    /// While true, Unity ignores clicks, scroll and hover under the panel.</summary>
    public void SetPointerOverUI_Extern(string value)
    {
        ExternalUIState.PointerOverUI = ParseBool(value);
    }

    /// <summary>"true"/"false". Unlock before showing a React panel in worker mode.
    /// Browsers may need one click on the canvas before a lock takes effect.</summary>
    public void SetCursorLocked_Extern(string locked)
    {
        LogIncoming(nameof(SetCursorLocked_Extern), locked);
        Player current = GetWorker();

        if (!ParseBool(locked))
        {
            if (current != null && current.isActiveAndEnabled && current.CameraController != null) current.CameraController.UnlockCursor();
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }

        if (current == null || !current.isActiveAndEnabled || current.IsControlSuspended || current.CameraController == null)
        {
            HandleError_Extern(nameof(SetCursorLocked_Extern), "The worker is not active (plant overview or explosion view).");
            return;
        }

        current.CameraController.LockCursor();
    }

    /// <summary>"true" / "false" / "true/0.2" (interval in seconds). Streams handleWorkerTransform.</summary>
    public void SetWorkerTracking_Extern(string input)
    {
        LogIncoming(nameof(SetWorkerTracking_Extern), input);
        string[] parts = (input ?? "").Split('/');
        trackWorker = ParseBool(parts[0]);
        trackInterval = parts.Length > 1 && TryParseFloat(parts[1], out float interval) ? Mathf.Max(0.02f, interval) : 0.1f;
        nextTrackTime = 0f;
    }

    /// <summary>Master volume "0".."1".</summary>
    public void SetVolume_Extern(string volume)
    {
        LogIncoming(nameof(SetVolume_Extern), volume);
        if (TryParseFloat(volume, out float value, nameof(SetVolume_Extern))) AudioListener.volume = Mathf.Clamp01(value);
    }

    /// <summary>"true" / "false". Shows / hides the Unity minimap panel (it also hides by itself in
    /// overview and explosion view, when there is no worker).</summary>
    public void SetMiniMapVisible_Extern(string visible)
    {
        LogIncoming(nameof(SetMiniMapVisible_Extern), visible);
        if (!Available<MiniMapPanel>(nameof(SetMiniMapVisible_Extern), "Minimap panel")) return;
        MiniMapPanel.Instance.SetVisible(ParseBool(visible));
    }

    #endregion

    #region React -> Unity : Scenes

    /// <summary>"Main_Scene" | "BoilerRoom" | "TurbineRoom" | "Control_Room".
    /// Events: handleSceneLoading, handleSceneDownloadProgress..., handleSceneLoaded (or handleSceneLoadFailed).</summary>
    public void ChangeScene_Extern(string sceneName)
    {
        LogIncoming(nameof(ChangeScene_Extern), sceneName);
        if (!Available<SceneController>(nameof(ChangeScene_Extern), "Scene loading")) return;

        if (SceneController.Instance.IsChangingScene)
        {
            HandleError_Extern(nameof(ChangeScene_Extern), "A scene is already loading.");
            return;
        }

        SceneController.Instance.ChangeScene(sceneName);
    }

    /// <summary>Scene name. Answer: handleSceneDownloadSize { name, exists, cached, sizeMB }.</summary>
    public void GetSceneDownloadSize_Extern(string sceneName)
    {
        LogIncoming(nameof(GetSceneDownloadSize_Extern), sceneName);
        if (Available<SceneController>(nameof(GetSceneDownloadSize_Extern), "Scene loading")) SceneController.Instance.RequestDownloadSize(sceneName);
    }

    /// <summary>Scene name. Downloads quietly without opening it. Done: handleScenePreloaded.</summary>
    public void PreloadScene_Extern(string sceneName)
    {
        LogIncoming(nameof(PreloadScene_Extern), sceneName);
        if (Available<SceneController>(nameof(PreloadScene_Extern), "Scene loading")) SceneController.Instance.Preload(sceneName);
    }

    #endregion

    #region React -> Unity : Main_Scene camera

    /// <summary>"overview" | "worker". Answer: handleCameraModeChanged.</summary>
    public void SetCameraMode_Extern(string mode)
    {
        LogIncoming(nameof(SetCameraMode_Extern), mode);
        if (!Available<PlantIsometricCameraController>(nameof(SetCameraMode_Extern), "The plant overview camera")) return;

        string value = (mode ?? "").Trim().ToLowerInvariant();
        if (value == CameraModeOverview) PlantIsometricCameraController.Instance.EnableIsometricMode();
        else if (value == CameraModeWorker) PlantIsometricCameraController.Instance.EnableWorkerMode();
        else HandleError_Extern(nameof(SetCameraMode_Extern), $"Unknown mode '{mode}'. Use \"overview\" or \"worker\".");
    }

    public void ToggleCameraMode_Extern()
    {
        LogIncoming(nameof(ToggleCameraMode_Extern), null);
        if (Available<PlantIsometricCameraController>(nameof(ToggleCameraMode_Extern), "The plant overview camera"))
            PlantIsometricCameraController.Instance.SwitchCamera();
    }

    /// <summary>Overview only: back to the default view.</summary>
    public void ResetOverviewView_Extern()
    {
        LogIncoming(nameof(ResetOverviewView_Extern), null);
        if (!Available<PlantIsometricCameraController>(nameof(ResetOverviewView_Extern), "The plant overview camera")) return;

        if (!PlantIsometricCameraController.Instance.IsInIsometricMode)
        {
            HandleError_Extern(nameof(ResetOverviewView_Extern), "Reset view only works in overview mode.");
            return;
        }

        PlantIsometricCameraController.Instance.ResetView();
    }

    /// <summary>Answer: handleCameraModeChanged with the current mode.</summary>
    public void GetCameraMode_Extern()
    {
        LogIncoming(nameof(GetCameraMode_Extern), null);
        if (Available<PlantIsometricCameraController>(nameof(GetCameraMode_Extern), "The plant overview camera"))
            HandleCameraModeChanged_Extern(PlantIsometricCameraController.Instance.IsInIsometricMode ? CameraModeOverview : CameraModeWorker);
    }

    #endregion

    #region React -> Unity : Equipment (turbine / boiler / control room)

    /// <summary>Answer: handleEquipmentState.</summary>
    public void GetEquipmentState_Extern()
    {
        LogIncoming(nameof(GetEquipmentState_Extern), null);
        if (Available<HUDController>(nameof(GetEquipmentState_Extern), "Equipment controls")) HUDController.Instance.SendState();
    }

    /// <summary>Toggle explosion view (enter / collapse).</summary>
    public void ToggleExplode_Extern()
    {
        LogIncoming(nameof(ToggleExplode_Extern), null);
        if (Available<HUDController>(nameof(ToggleExplode_Extern), "Equipment controls")) HUDController.Instance.ToggleExplode();
    }

    /// <summary>Toggle explode-all.</summary>
    public void ToggleExplodeAll_Extern()
    {
        LogIncoming(nameof(ToggleExplodeAll_Extern), null);
        if (Available<HUDController>(nameof(ToggleExplodeAll_Extern), "Equipment controls")) HUDController.Instance.ToggleExplodeAll();
    }

    /// <summary>Toggle operation (turbine: animated + exploded, boiler: fluid process).</summary>
    public void ToggleOperation_Extern()
    {
        LogIncoming(nameof(ToggleOperation_Extern), null);
        if (Available<HUDController>(nameof(ToggleOperation_Extern), "Equipment controls")) HUDController.Instance.ToggleOperation();
    }

    /// <summary>Boiler only: info sequence + opens the boiler dashboard.</summary>
    public void StartBoilerInfo_Extern()
    {
        LogIncoming(nameof(StartBoilerInfo_Extern), null);
        if (Available<HUDController>(nameof(StartBoilerInfo_Extern), "Equipment controls")) HUDController.Instance.StartBoilerInfo();
    }

    /// <summary>Leave any explosion view / turbine operation.</summary>
    public void CollapseEquipment_Extern()
    {
        LogIncoming(nameof(CollapseEquipment_Extern), null);
        if (Available<HUDController>(nameof(CollapseEquipment_Extern), "Equipment controls")) HUDController.Instance.CollapseAll();
    }

    /// <summary>Explode / collapse the part from the last handlePartSelected.</summary>
    public void TogglePartExplode_Extern()
    {
        LogIncoming(nameof(TogglePartExplode_Extern), null);
        if (Available<HUDController>(nameof(TogglePartExplode_Extern), "Equipment controls")) HUDController.Instance.TogglePartExplode();
    }

    /// <summary>Close the part menu.</summary>
    public void ClearPartSelection_Extern()
    {
        LogIncoming(nameof(ClearPartSelection_Extern), null);
        if (Available<HUDController>(nameof(ClearPartSelection_Extern), "Equipment controls")) HUDController.Instance.HideClickContext();
    }

    #endregion

    #region React -> Unity : Turbine (one function per button, acts on the turbine the worker is at)

    /// <summary>Turbine: explode all parts. State answer: handleEquipmentState.</summary>
    public void TurbineExplodeAll_Extern()
    {
        LogIncoming(nameof(TurbineExplodeAll_Extern), null);
        if (Available<HUDController>(nameof(TurbineExplodeAll_Extern), "Equipment controls")) HUDController.Instance.TurbineExplodeAll();
    }

    /// <summary>Turbine: collapse all parts (also stops operation).</summary>
    public void TurbineCollapseAll_Extern()
    {
        LogIncoming(nameof(TurbineCollapseAll_Extern), null);
        if (Available<HUDController>(nameof(TurbineCollapseAll_Extern), "Equipment controls")) HUDController.Instance.TurbineCollapseAll();
    }

    /// <summary>Turbine: start operation (exploded + spinning + steam). Live values: handleTurbineData.</summary>
    public void TurbineStartOperation_Extern()
    {
        LogIncoming(nameof(TurbineStartOperation_Extern), null);
        if (Available<HUDController>(nameof(TurbineStartOperation_Extern), "Equipment controls")) HUDController.Instance.TurbineStartOperation();
    }

    /// <summary>Turbine: stop operation.</summary>
    public void TurbineStopOperation_Extern()
    {
        LogIncoming(nameof(TurbineStopOperation_Extern), null);
        if (Available<HUDController>(nameof(TurbineStopOperation_Extern), "Equipment controls")) HUDController.Instance.TurbineStopOperation();
    }

    #endregion

    #region React -> Unity : Turbines

    /// <summary>Answer: handleTurbineList { turbines: [...] }.</summary>
    public void GetAllTurbineData_Extern()
    {
        LogIncoming(nameof(GetAllTurbineData_Extern), null);
        HandleTurbineList_Extern(new TurbineListPayload { turbines = TurbineData.GetAllPayloads() });
    }

    /// <summary>Turbine id (e.g. "1"). Answer: handleTurbineData.</summary>
    public void GetTurbineData_Extern(string id)
    {
        LogIncoming(nameof(GetTurbineData_Extern), id);
        TurbineDataPayload data = TurbineData.GetPayload(id);
        if (data != null) HandleTurbineData_Extern(data);
        else HandleError_Extern(nameof(GetTurbineData_Extern), $"No turbine with id '{id}' in this scene.");
    }

    #endregion

    #region React -> Unity : Boiler (one function per button)

    /// <summary>Boiler: explode all parts. State answer: handleEquipmentState.</summary>
    public void BoilerExplodeAll_Extern()
    {
        LogIncoming(nameof(BoilerExplodeAll_Extern), null);
        if (Available<HUDController>(nameof(BoilerExplodeAll_Extern), "Equipment controls")) HUDController.Instance.BoilerExplodeAll();
    }

    /// <summary>Boiler: collapse all parts, back to the worker.</summary>
    public void BoilerCollapseAll_Extern()
    {
        LogIncoming(nameof(BoilerCollapseAll_Extern), null);
        if (Available<HUDController>(nameof(BoilerCollapseAll_Extern), "Equipment controls")) HUDController.Instance.BoilerCollapseAll();
    }

    /// <summary>Boiler: start the operation animation.</summary>
    public void BoilerStartOperation_Extern()
    {
        LogIncoming(nameof(BoilerStartOperation_Extern), null);
        if (Available<HUDController>(nameof(BoilerStartOperation_Extern), "Equipment controls")) HUDController.Instance.BoilerStartOperation();
    }

    /// <summary>Boiler: stop the operation animation.</summary>
    public void BoilerStopOperation_Extern()
    {
        LogIncoming(nameof(BoilerStopOperation_Extern), null);
        if (Available<HUDController>(nameof(BoilerStopOperation_Extern), "Equipment controls")) HUDController.Instance.BoilerStopOperation();
    }

    /// <summary>Boiler: info panel + particle effects. Also: handleBoilerDashboardOpened.</summary>
    public void BoilerShowInfo_Extern()
    {
        LogIncoming(nameof(BoilerShowInfo_Extern), null);
        if (Available<HUDController>(nameof(BoilerShowInfo_Extern), "Equipment controls")) HUDController.Instance.BoilerShowInfo();
    }

    /// <summary>Boiler: close the info panel and particle effects.</summary>
    public void BoilerHideInfo_Extern()
    {
        LogIncoming(nameof(BoilerHideInfo_Extern), null);
        if (Available<HUDController>(nameof(BoilerHideInfo_Extern), "Equipment controls")) HUDController.Instance.BoilerHideInfo();
    }

    /// <summary>Boiler burner power slider "0".."100". Works any time in the boiler room.</summary>
    public void SetBoilerBurnerPower_Extern(string value)
    {
        LogIncoming(nameof(SetBoilerBurnerPower_Extern), value);
        if (Available<HUDController>(nameof(SetBoilerBurnerPower_Extern), "Equipment controls") &&
            TryParseFloat(value, out float power, nameof(SetBoilerBurnerPower_Extern)))
            HUDController.Instance.SetBoilerBurnerPower(power);
    }

    #endregion

    #region React -> Unity : Boiler dashboard

    /// <summary>"0".."100".</summary>
    public void SetBurnerPower_Extern(string value)
    {
        LogIncoming(nameof(SetBurnerPower_Extern), value);
        if (BoilerDashboardOpen(nameof(SetBurnerPower_Extern)) && TryParseFloat(value, out float power, nameof(SetBurnerPower_Extern)))
            BoilerDashboardController.Instance.SetBurnerPower(power);
    }

    /// <summary>"0".."100".</summary>
    public void SetValveOpening_Extern(string value)
    {
        LogIncoming(nameof(SetValveOpening_Extern), value);
        if (BoilerDashboardOpen(nameof(SetValveOpening_Extern)) && TryParseFloat(value, out float opening, nameof(SetValveOpening_Extern)))
            BoilerDashboardController.Instance.SetValveOpening(opening);
    }

    /// <summary>Restart after the cycle completed.</summary>
    public void RestartBoiler_Extern()
    {
        LogIncoming(nameof(RestartBoiler_Extern), null);
        if (BoilerDashboardOpen(nameof(RestartBoiler_Extern))) BoilerDashboardController.Instance.RestartBoiler();
    }

    /// <summary>Close the dashboard (also ends the boiler info sequence).</summary>
    public void CloseBoilerDashboard_Extern()
    {
        LogIncoming(nameof(CloseBoilerDashboard_Extern), null);
        if (BoilerDashboardOpen(nameof(CloseBoilerDashboard_Extern))) BoilerDashboardController.Instance.CloseDashboard();
    }

    /// <summary>Answer: handleBoilerDashboard.</summary>
    public void GetBoilerDashboard_Extern()
    {
        LogIncoming(nameof(GetBoilerDashboard_Extern), null);
        if (BoilerDashboardOpen(nameof(GetBoilerDashboard_Extern))) BoilerDashboardController.Instance.SendDashboard();
    }

    #endregion

    #region React -> Unity : Main_Scene visuals

    /// <summary>"day" | "evening" | "night". Main_Scene only. Answer: handleTimeOfDayChanged.</summary>
    public void SetTimeOfDay_Extern(string value)
    {
        LogIncoming(nameof(SetTimeOfDay_Extern), value);
        if (DayNightController.Instance == null)
        {
            HandleError_Extern(nameof(SetTimeOfDay_Extern), "Day / night is only available in Main_Scene.");
            return;
        }

        if (!DayNightController.Instance.SetTime(value))
            HandleError_Extern(nameof(SetTimeOfDay_Extern), $"Unknown time '{value}'. Use \"day\", \"evening\" or \"night\".");
    }

    /// <summary>Chimney smoke slider "0".."1" (default 0.2). 0 = no smoke, 0.2 white, 0.4 dark gray, 0.6 black,
    /// 0.8 yellow-brown, 1 blue-gray. Main_Scene only. Answer: handleSmokeLevelChanged.</summary>
    public void SetSmokeLevel_Extern(string value)
    {
        LogIncoming(nameof(SetSmokeLevel_Extern), value);
        if (SmokeColorController.Instance == null)
        {
            HandleError_Extern(nameof(SetSmokeLevel_Extern), "The smoke slider is only available in Main_Scene.");
            return;
        }

        if (TryParseFloat(value, out float level, nameof(SetSmokeLevel_Extern))) SmokeColorController.Instance.SetLevel(level);
    }

    /// <summary>Answer: handleSmokeLevelChanged with the current level (e.g. to set the React slider).</summary>
    public void GetSmokeLevel_Extern()
    {
        LogIncoming(nameof(GetSmokeLevel_Extern), null);
        if (SmokeColorController.Instance == null)
        {
            HandleError_Extern(nameof(GetSmokeLevel_Extern), "The smoke slider is only available in Main_Scene.");
            return;
        }

        HandleSmokeLevelChanged_Extern(SmokeColorController.Instance.BuildStatus());
    }

    /// <summary>Master volume of all sounds (boiling water, turbines, alarms): "0".."1" (or "0".."100").</summary>
    public void SetSoundVolume_Extern(string value)
    {
        LogIncoming(nameof(SetSoundVolume_Extern), value);
        if (TryParseFloat(value, out float volume, nameof(SetSoundVolume_Extern))) PlantSounds.SetMasterVolume(volume);
    }

    #endregion

    #region React -> Unity : Control room guided tour

    /// <summary>Starts the voltage-control + safety tour (Control_Room). Answer: handleTourChanged.</summary>
    public void StartControlRoomTour_Extern()
    {
        LogIncoming(nameof(StartControlRoomTour_Extern), null);
        if (TourAvailable(nameof(StartControlRoomTour_Extern))) ControlRoomTour.Instance.StartTour();
    }

    /// <summary>Next step (only when the current one is done; on the last screen: closes the tour).</summary>
    public void TourNext_Extern()
    {
        LogIncoming(nameof(TourNext_Extern), null);
        if (TourAvailable(nameof(TourNext_Extern))) ControlRoomTour.Instance.Next();
    }

    public void TourBack_Extern()
    {
        LogIncoming(nameof(TourBack_Extern), null);
        if (TourAvailable(nameof(TourBack_Extern))) ControlRoomTour.Instance.Back();
    }

    public void StopControlRoomTour_Extern()
    {
        LogIncoming(nameof(StopControlRoomTour_Extern), null);
        if (TourAvailable(nameof(StopControlRoomTour_Extern))) ControlRoomTour.Instance.StopTour();
    }

    /// <summary>Answer: handleTourChanged with the current state.</summary>
    public void GetTourState_Extern()
    {
        LogIncoming(nameof(GetTourState_Extern), null);
        if (TourAvailable(nameof(GetTourState_Extern))) HandleTourChanged_Extern(ControlRoomTour.Instance.BuildState());
    }

    /// <summary>"true" (default): Unity shows the start button + step panel. "false": React draws the tour.</summary>
    public void SetTourUnityUI_Extern(string value)
    {
        LogIncoming(nameof(SetTourUnityUI_Extern), value);
        if (TourAvailable(nameof(SetTourUnityUI_Extern)))
            ControlRoomTour.Instance.SetUnityUI(!string.Equals(value, "false", System.StringComparison.OrdinalIgnoreCase) && value != "0");
    }

    private static bool TourAvailable(string command)
    {
        if (ControlRoomTour.Instance != null) return true;
        HandleError_Extern(command, "The guided tour is only available in the control room.");
        return false;
    }

    #endregion

    #region React -> Unity : Control room

    /// <summary>Control room slider "0".."100". Works anywhere in the control room; from 80 (Warning Threshold
    /// on ElectricalPanelInfo) the warning lights blink. Answer: handleElectricalValues (alarmLevel).</summary>
    public void SetControlRoomSlider_Extern(string value)
    {
        LogIncoming(nameof(SetControlRoomSlider_Extern), value);
        if (Available<HUDController>(nameof(SetControlRoomSlider_Extern), "Equipment controls") &&
            TryParseFloat(value, out float slider, nameof(SetControlRoomSlider_Extern)))
            HUDController.Instance.SetControlRoomSlider(slider);
    }

    #endregion

    #region React -> Unity : Electrical panel

    /// <summary>"0".."100" – only while the worker is at the panel.</summary>
    public void SetGeneratorValue_Extern(string value)
    {
        LogIncoming(nameof(SetGeneratorValue_Extern), value);
        if (Available<HUDController>(nameof(SetGeneratorValue_Extern), "Equipment controls") &&
            TryParseFloat(value, out float generator, nameof(SetGeneratorValue_Extern)))
            HUDController.Instance.SetGeneratorValue(generator);
    }

    #endregion

    // ===================================================================================== Unity -> React

    #region Unity -> React : Errors

    /// <summary>A React request could not be done → React: <c>handleError</c> { command, message }.</summary>
    public static void HandleError_Extern(string command, string message)
    {
        Debug.LogWarning($"[Bridge] {command}: {message}");
        string json = ToJson(new BridgeErrorPayload { command = command, message = message });
        Log(nameof(handleError), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleError(json);
#endif
    }

    /// <summary>A scene could not be downloaded/loaded → React: <c>handleSceneLoadFailed</c> { command = scene, message }.</summary>
    public static void HandleSceneLoadFailed_Extern(string sceneName, string message)
    {
        string json = ToJson(new BridgeErrorPayload { command = sceneName, message = message });
        Log(nameof(handleSceneLoadFailed), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSceneLoadFailed(json);
#endif
    }

    #endregion

    #region Unity -> React : App

    /// <summary>Unity started (sent once). Also the answer to Ping_Extern. → React: <c>handleUnityReady</c></summary>
    public static void HandleUnityReady_Extern(UnityStatusPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleUnityReady), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleUnityReady(json);
#endif
    }

    /// <summary>"true"/"false" – cursor locked (worker look mode) or free. Also fires when the user presses Esc. → React: <c>handleCursorLockChanged</c></summary>
    public static void HandleCursorLockChanged_Extern(bool value)
    {
        string data = value ? "true" : "false";
        Log(nameof(handleCursorLockChanged), data);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleCursorLockChanged(data);
#endif
    }

    /// <summary>Worker position/heading, while tracking is on (SetWorkerTracking_Extern). For a React minimap. → React: <c>handleWorkerTransform</c></summary>
    public static void HandleWorkerTransform_Extern(WorkerTransformPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleWorkerTransform), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleWorkerTransform(json);
#endif
    }

    #endregion

    #region Unity -> React : Scenes

    /// <summary>Scene name – a scene change started. → React: <c>handleSceneLoading</c></summary>
    public static void HandleSceneLoading_Extern(string data)
    {
        data ??= string.Empty;
        Log(nameof(handleSceneLoading), data);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSceneLoading(data);
#endif
    }

    /// <summary>Download progress of a scene that is not cached yet. → React: <c>handleSceneDownloadProgress</c></summary>
    public static void HandleSceneDownloadProgress_Extern(SceneDownloadProgressPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleSceneDownloadProgress), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSceneDownloadProgress(json);
#endif
    }

    /// <summary>A scene finished loading. → React: <c>handleSceneLoaded</c></summary>
    public static void HandleSceneLoaded_Extern(SceneInfoPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleSceneLoaded), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSceneLoaded(json);
#endif
    }

    /// <summary>Scene name – finished downloading in the background. → React: <c>handleScenePreloaded</c></summary>
    public static void HandleScenePreloaded_Extern(string data)
    {
        data ??= string.Empty;
        Log(nameof(handleScenePreloaded), data);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleScenePreloaded(data);
#endif
    }

    /// <summary>Answer to GetSceneDownloadSize_Extern. → React: <c>handleSceneDownloadSize</c></summary>
    public static void HandleSceneDownloadSize_Extern(SceneDownloadSizePayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleSceneDownloadSize), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSceneDownloadSize(json);
#endif
    }

    /// <summary>Worker stands at a door to another room: show "Go to …" (button calls ChangeScene_Extern). → React: <c>handleSceneTriggerEntered</c></summary>
    public static void HandleSceneTriggerEntered_Extern(SceneTriggerPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleSceneTriggerEntered), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSceneTriggerEntered(json);
#endif
    }

    /// <summary>Worker left the door. → React: <c>handleSceneTriggerExited</c></summary>
    public static void HandleSceneTriggerExited_Extern(SceneTriggerPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleSceneTriggerExited), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSceneTriggerExited(json);
#endif
    }

    #endregion

    #region Unity -> React : Camera

    /// <summary>"overview" or "worker". → React: <c>handleCameraModeChanged</c></summary>
    public static void HandleCameraModeChanged_Extern(string data)
    {
        data ??= string.Empty;
        Log(nameof(handleCameraModeChanged), data);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleCameraModeChanged(data);
#endif
    }

    /// <summary>Building clicked in plant overview. → React: <c>handleOverviewObjectSelected</c></summary>
    public static void HandleOverviewObjectSelected_Extern(OverviewSelectionPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleOverviewObjectSelected), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleOverviewObjectSelected(json);
#endif
    }

    /// <summary>Overview selection cleared. → React: <c>handleOverviewObjectDeselected</c></summary>
    public static void HandleOverviewObjectDeselected_Extern()
    {
        Log(nameof(handleOverviewObjectDeselected), null);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleOverviewObjectDeselected();
#endif
    }

    #endregion

    #region Unity -> React : Equipment

    /// <summary>Smoke level changed (also sent when Main_Scene starts). → React: <c>handleSmokeLevelChanged</c>
    /// { level, status, indication, color }</summary>
    public static void HandleSmokeLevelChanged_Extern(SmokeStatusPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleSmokeLevelChanged), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleSmokeLevelChanged(json);
#endif
    }

    /// <summary>Control room tour changed (start, step, live progress, done, stop). → React: <c>handleTourChanged</c></summary>
    public static void HandleTourChanged_Extern(TourStatePayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleTourChanged), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleTourChanged(json);
#endif
    }

    /// <summary>Time of day changed (React button, N key or start). → React: <c>handleTimeOfDayChanged</c> "day" | "evening" | "night"</summary>
    public static void HandleTimeOfDayChanged_Extern(string value)
    {
        Log(nameof(handleTimeOfDayChanged), value);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleTimeOfDayChanged(value);
#endif
    }


    /// <summary>Worker walked into control room info point 1/2/3: show that info panel. → React: <c>handleControlRoomInfoEntered</c> { index, name, title }</summary>
    public static void HandleControlRoomInfoEntered_Extern(ControlRoomInfoPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleControlRoomInfoEntered), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleControlRoomInfoEntered(json);
#endif
    }

    /// <summary>Worker left the info point: hide that info panel. → React: <c>handleControlRoomInfoExited</c> { index, name, title }</summary>
    public static void HandleControlRoomInfoExited_Extern(ControlRoomInfoPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleControlRoomInfoExited), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleControlRoomInfoExited(json);
#endif
    }

    /// <summary>Worker reached a turbine / boiler / control room. → React: <c>handleEquipmentInRange</c></summary>
    public static void HandleEquipmentInRange_Extern(EquipmentInfoPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleEquipmentInRange), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleEquipmentInRange(json);
#endif
    }

    /// <summary>Worker is near turbine 1-4 (show that turbine's buttons). → React: <c>handleTurbineInRange</c>
    /// { id: "1".."4", name: "Turbine-1", type, operating, steamPressure, temperature, ... }</summary>
    public static void HandleTurbineInRange_Extern(TurbineDataPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleTurbineInRange), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleTurbineInRange(json);
#endif
    }

    /// <summary>Worker walked away from that turbine. → React: <c>handleTurbineOutOfRange</c> (same data)</summary>
    public static void HandleTurbineOutOfRange_Extern(TurbineDataPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleTurbineOutOfRange), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleTurbineOutOfRange(json);
#endif
    }

    /// <summary>Worker is near the boiler (show the boiler buttons). → React: <c>handleBoilerInRange</c> { type, name }</summary>
    public static void HandleBoilerInRange_Extern(EquipmentInfoPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleBoilerInRange), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleBoilerInRange(json);
#endif
    }

    /// <summary>Worker walked away from the boiler (hide the boiler buttons). → React: <c>handleBoilerOutOfRange</c></summary>
    public static void HandleBoilerOutOfRange_Extern(EquipmentInfoPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleBoilerOutOfRange), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleBoilerOutOfRange(json);
#endif
    }

    /// <summary>Worker walked away. → React: <c>handleEquipmentOutOfRange</c></summary>
    public static void HandleEquipmentOutOfRange_Extern(EquipmentInfoPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleEquipmentOutOfRange), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleEquipmentOutOfRange(json);
#endif
    }

    /// <summary>Full button state. Enable/disable React buttons from the can* flags. → React: <c>handleEquipmentState</c></summary>
    public static void HandleEquipmentState_Extern(EquipmentStatePayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleEquipmentState), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleEquipmentState(json);
#endif
    }

    /// <summary>Part name under the mouse (explosion view). → React: <c>handlePartHover</c></summary>
    public static void HandlePartHover_Extern(string data)
    {
        data ??= string.Empty;
        Log(nameof(handlePartHover), data);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handlePartHover(data);
#endif
    }

    /// <summary>Mouse left the part. → React: <c>handlePartHoverEnd</c></summary>
    public static void HandlePartHoverEnd_Extern()
    {
        Log(nameof(handlePartHoverEnd), null);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handlePartHoverEnd();
#endif
    }

    /// <summary>Part clicked: show its menu at x/y (0..1 of canvas, top-left). → React: <c>handlePartSelected</c></summary>
    public static void HandlePartSelected_Extern(PartSelectedPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handlePartSelected), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handlePartSelected(json);
#endif
    }

    /// <summary>Maintenance sheet of the clicked part (explosion view; Unity shows it bottom-right too).
    /// → React: <c>handlePartMaintenance</c> { partName, partId, equipment, lastMaintenanceDate, ... }</summary>
    public static void HandlePartMaintenance_Extern(MaintenanceInfo data)
    {
        string json = ToJson(data);
        Log(nameof(handlePartMaintenance), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handlePartMaintenance(json);
#endif
    }

    /// <summary>Close the part menu. → React: <c>handlePartDeselected</c></summary>
    public static void HandlePartDeselected_Extern()
    {
        Log(nameof(handlePartDeselected), null);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handlePartDeselected();
#endif
    }

    #endregion

    #region Unity -> React : Turbine

    /// <summary>Live values of one turbine (every few seconds while the worker is there or it operates). → React: <c>handleTurbineData</c></summary>
    public static void HandleTurbineData_Extern(TurbineDataPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleTurbineData), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleTurbineData(json);
#endif
    }

    /// <summary>Answer to GetAllTurbineData_Extern. → React: <c>handleTurbineList</c></summary>
    public static void HandleTurbineList_Extern(TurbineListPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleTurbineList), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleTurbineList(json);
#endif
    }

    #endregion

    #region Unity -> React : Boiler

    /// <summary>Boiler dashboard opened (after StartBoilerInfo_Extern). → React: <c>handleBoilerDashboardOpened</c></summary>
    public static void HandleBoilerDashboardOpened_Extern(BoilerDashboardPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleBoilerDashboardOpened), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleBoilerDashboardOpened(json);
#endif
    }

    /// <summary>Live boiler values (max 5x per second, only on change). → React: <c>handleBoilerDashboard</c></summary>
    public static void HandleBoilerDashboard_Extern(BoilerDashboardPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleBoilerDashboard), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleBoilerDashboard(json);
#endif
    }

    /// <summary>Boiler dashboard closed. → React: <c>handleBoilerDashboardClosed</c></summary>
    public static void HandleBoilerDashboardClosed_Extern()
    {
        Log(nameof(handleBoilerDashboardClosed), null);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleBoilerDashboardClosed();
#endif
    }

    #endregion

    #region Unity -> React : Electrical

    /// <summary>Worker at the electrical panel; value = current generator slider value (0-100). → React: <c>handleElectricalPanelOpened</c></summary>
    public static void HandleElectricalPanelOpened_Extern(float value)
    {
        string data = value.ToString("0.###", CultureInfo.InvariantCulture);
        Log(nameof(handleElectricalPanelOpened), data);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleElectricalPanelOpened(data);
#endif
    }

    /// <summary>Panel values after every change. → React: <c>handleElectricalValues</c></summary>
    public static void HandleElectricalValues_Extern(ElectricalValuesPayload data)
    {
        string json = ToJson(data);
        Log(nameof(handleElectricalValues), json);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleElectricalValues(json);
#endif
    }

    /// <summary>Worker left the panel. → React: <c>handleElectricalPanelClosed</c></summary>
    public static void HandleElectricalPanelClosed_Extern()
    {
        Log(nameof(handleElectricalPanelClosed), null);
#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD
        handleElectricalPanelClosed();
#endif
    }

    #endregion

    // ===================================================================================== helpers

    public const string CameraModeOverview = "overview";
    public const string CameraModeWorker = "worker";

    private static string ToJson(object data)
    {
        return data == null ? "{}" : JsonUtility.ToJson(data);
    }

    private static void Log(string jsFunction, string data)
    {
        if (logOutgoingMessages) Debug.Log($"[Unity -> React] {jsFunction}({data})");
        MessageSent?.Invoke(jsFunction, data);
    }

    private void LogIncoming(string method, string data)
    {
        if (logIncoming) Debug.Log($"[React -> Unity] {method}({data})");
    }

    private static bool Available<TSingleton>(string command, string what) where TSingleton : SingletonMono<TSingleton>
    {
        if (SingletonMono<TSingleton>.HasInstance) return true;

        HandleError_Extern(command, $"{what} is not available in scene '{SceneManager.GetActiveScene().name}'.");
        return false;
    }

    private static bool BoilerDashboardOpen(string command)
    {
        if (BoilerDashboardController.HasInstance && BoilerDashboardController.Instance.isActiveAndEnabled) return true;

        HandleError_Extern(command, "The boiler dashboard is not open (call StartBoilerInfo_Extern at the boiler first).");
        return false;
    }

    private static bool ParseBool(string value)
    {
        string text = (value ?? "").Trim();
        return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseFloat(string value, out float result, string command = null)
    {
        if (float.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return true;

        if (command != null) HandleError_Extern(command, $"'{value}' is not a number.");
        return false;
    }

    private static void ApplyKeyboardCapture(bool capture)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLInput.captureAllKeyboardInput = capture;
#endif
    }

    private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;

        FindWorker();
        HandleSceneLoaded_Extern(new SceneInfoPayload { name = scene.name, buildIndex = scene.buildIndex });
    }

    // Each scene has its own worker; cached once per scene (it may be switched off later).
    private void FindWorker()
    {
        worker = FindAnyObjectByType<Player>(FindObjectsInactive.Include);
    }

    private Player GetWorker()
    {
        if (worker == null) FindWorker();
        return worker;
    }

    private void SendWorkerTransform()
    {
        Player current = GetWorker();
        var payload = new WorkerTransformPayload { scene = SceneManager.GetActiveScene().name };

        if (current != null)
        {
            Vector3 position = current.transform.position;
            payload.active = current.isActiveAndEnabled && !current.IsControlSuspended;
            payload.x = position.x;
            payload.y = position.y;
            payload.z = position.z;
            payload.heading = current.transform.eulerAngles.y;
        }

        HandleWorkerTransform_Extern(payload);
    }

    private static UnityStatusPayload BuildStatus()
    {
        UnityEngine.SceneManagement.Scene active = SceneManager.GetActiveScene();
        return new UnityStatusPayload
        {
            scene = active.name,
            buildIndex = active.buildIndex,
            unityVersion = Application.unityVersion,
            appVersion = Application.version,
            platform = Application.platform.ToString()
        };
    }
}
