using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Equipment interaction logic (turbines, boiler, control room) for the React UI.
/// It used to drive the UI Toolkit HUD; now it holds no UI at all:
///   - React buttons call commands ("equipment.explode", "equipment.operation", ...)
///   - Unity tells React what to show with events ("equipment.state", "part.selected", ...)
/// The class name and public methods are unchanged, so triggers, turbines, the electrical panel
/// and the explosion view scripts keep calling it exactly as before.
///
/// Persistent singleton (one for the whole session). Put it on its own GameObject in the first
/// scene (or keep the existing HUDController prefab) and assign the explosion camera prefab.
/// </summary>
public class HUDController : SingletonMono<HUDController>
{
    private enum ActiveAction { None, Explode, ExplodeAll, Operation }

    [Header("Explosion View")]
    [Tooltip("Camera prefab with RTSCameraController, spawned for explosion view / turbine operation.")]
    [SerializeField] private GameObject explosionViewCameraPrefab;
    [Tooltip("Background picture behind the equipment in explosion view (stretched to the screen).")]
    [SerializeField] private Texture explosionBackgroundImage;
    [Tooltip("Background colour of the explosion view camera (used where there is no picture).")]
    [SerializeField] private Color explosionBackground = new Color(0.02f, 0.03f, 0.1f, 1f);
    [Tooltip("On: the explode button (ToggleExplode_Extern) explodes every part at once (Explode All).")]
    [SerializeField] private bool explodeMeansExplodeAll = true;

    [Header("Maintenance Sheet")]
    [Tooltip("Assets/Prefabs/UI/MaintenanceSheet.prefab - created once at start (hidden) and kept for the whole session. " +
             "Empty = the default layout is built in code.")]
    [SerializeField] private GameObject maintenanceSheetPrefab;

    protected override bool PersistAcrossScenes => true;

    private GameObject currentContextObject;
    private EquipmentType currentType = EquipmentType.None;
    private bool contextInRange;

    private ActiveAction activeAction = ActiveAction.None;
    private bool isOperating;

    private ExplodableViewNode selectedPart;
    private GameObject explosionViewCameraInstance;
    private ElectricalPanelInfo currentElectricalPanel;
    private CharacterMovementController cachedPlayer;
    private Coroutine collapseRoutine;
    private bool isCollapsing; // parts still sliding back: worker stays off, no new actions
    private float reactBurnerPower = -1f; // last value from SetBoilerBurnerPower_Extern (0..100), -1 = none

    private bool IsExploded => explosionViewCameraInstance != null;

    // Read-only state (Editor test panel, debugging).
    public EquipmentType CurrentEquipmentType => currentContextObject != null ? currentType : EquipmentType.None;
    public string CurrentEquipmentName => currentContextObject != null ? currentContextObject.name : "";
    /// <summary>Equipment the worker is at (boiler / turbine object), or null.</summary>
    public GameObject CurrentEquipmentObject => currentContextObject;
    /// <summary>Electrical panel the worker stands at (its trigger), or null.</summary>
    public ElectricalPanelInfo CurrentElectricalPanel => currentElectricalPanel;
    /// <summary>Last electrical values sent (generator kV, grid kV, loading, temperatures) - for the Unity UI.</summary>
    public ElectricalValuesPayload LastElectricalValues { get; private set; }
    public bool IsExplodedView => IsExploded;
    /// <summary>Parts are still sliding back after Collapse (worker comes back when done).</summary>
    public bool IsCollapsing => isCollapsing;
    /// <summary>The worker is at a boiler / turbine that has an explosion view and nothing else is running.</summary>
    public bool HasExplodedView => GetExplodedView() != null;
    public bool CanExplodeAll => activeAction == ActiveAction.None && !IsExploded && IsAllowed(ActiveAction.ExplodeAll) && GetExplodedView() != null;
    public bool IsOperatingEquipment => isOperating;
    public bool IsBoilerInfoOpen => IsBoilerInfoActive;

    // ================================================================== lifecycle

    protected override void OnSingletonAwake()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GetMaintenanceSheet(); // once, at start: no hitch on the first part click, no cost while hidden
    }

    protected override void OnSingletonDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    // Everything scene-specific was destroyed with the old scene: start clean.
    private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;

        currentContextObject = null;
        currentType = EquipmentType.None;
        contextInRange = false;
        activeAction = ActiveAction.None;
        isOperating = false;
        selectedPart = null;
        explosionViewCameraInstance = null;
        currentElectricalPanel = null;
        cachedPlayer = null;
        reactBurnerPower = -1f;

        SendState();
    }

    // ================================================================== called by scene scripts (unchanged API)

    public void SetInRangeOfEquipment(EquipmentType type, GameObject ctxObject)
    {
        if (type == EquipmentType.None) return;

        // Don't switch equipment in the middle of an action on another one.
        if (HasActiveSession && currentContextObject != null && ctxObject != currentContextObject) return;

        currentType = type;
        if (ctxObject != null) currentContextObject = ctxObject;
        contextInRange = true;

        var payload = new EquipmentInfoPayload
        {
            type = type.ToString(),
            name = currentContextObject != null ? currentContextObject.name : ""
        };
        CommunicationManager.HandleEquipmentInRange_Extern(payload);
        if (type == EquipmentType.Boiler) CommunicationManager.HandleBoilerInRange_Extern(payload);
        if (type == EquipmentType.Turbine) CommunicationManager.HandleTurbineInRange_Extern(GetTurbinePayload());
        SendState();
    }

    /// <summary>Preferred: only clears the context if the worker left THIS object (two turbines can be close together).</summary>
    public void ResetInRangeOfEquipment(EquipmentType type, GameObject ctxObject)
    {
        if (ctxObject != null && currentContextObject != null && ctxObject != currentContextObject) return;
        ResetInRangeOfEquipment(type);
    }

    public void ResetInRangeOfEquipment(EquipmentType type)
    {
        if (type != currentType) return;

        contextInRange = false;
        var payload = new EquipmentInfoPayload
        {
            type = type.ToString(),
            name = currentContextObject != null ? currentContextObject.name : ""
        };
        CommunicationManager.HandleEquipmentOutOfRange_Extern(payload);
        if (type == EquipmentType.Boiler) CommunicationManager.HandleBoilerOutOfRange_Extern(payload);
        if (type == EquipmentType.Turbine) CommunicationManager.HandleTurbineOutOfRange_Extern(GetTurbinePayload());

        // Keep the context while something is running so it can still be stopped.
        if (!HasActiveSession)
        {
            currentContextObject = null;
            currentType = EquipmentType.None;
        }

        SendState();
    }

    public void UpdateBoilerButtonStates() => SendState();
    public void UpdateEquipmentButtonStates() => SendState();

    public void ShowPartName(string partName)
    {
        CommunicationManager.HandlePartHover_Extern(partName);
    }

    public void HidePartName()
    {
        CommunicationManager.HandlePartHoverEnd_Extern();
    }

    /// <summary>A part was clicked in explosion view: React shows its context menu at x/y.</summary>
    public void ShowClickContext(Vector2 screenPosition, ExplodableViewNode part)
    {
        if (isOperating || isCollapsing || part == null) return;

        selectedPart = part;
        var explodedView = part.GetComponentInParent<ModularExplodedView>();

        // Explosion view: everything (incl. name + description) is in the maintenance sheet bottom-right,
        // no floating card beside the part. Outside the explosion view: the card as before.
        if (IsExploded)
        {
            if (partCard != null) partCard.Hide();
            MaintenanceInfo maintenance = MaintenanceData.For(part);
            GetMaintenanceSheet().Show(maintenance, part.transform, GetExplosionCamera()); // part + view: report picture
            CommunicationManager.HandlePartMaintenance_Extern(maintenance);
        }
        else
        {
            GetPartCard().Show(part.transform, part.PartName, part.Description, GetExplosionCamera());
        }

        CommunicationManager.HandlePartSelected_Extern(new PartSelectedPayload
        {
            name = part.PartName,
            description = part.Description,
            exploded = explodedView != null && explodedView.IsPartExploded(part.transform),
            x = Screen.width > 0 ? screenPosition.x / Screen.width : 0f,
            y = Screen.height > 0 ? 1f - screenPosition.y / Screen.height : 0f
        });
    }

    /// <summary>Any object was clicked in explosion view. Uses its ExplodableViewNode (on it or a parent) when
    /// there is one; otherwise React still gets the object's name (no part explode for it).</summary>
    public void ShowClickContext(Vector2 screenPosition, GameObject clicked)
    {
        if (isOperating || clicked == null) return;

        ExplodableViewNode node = clicked.GetComponentInParent<ExplodableViewNode>();
        if (node != null)
        {
            ShowClickContext(screenPosition, node);
            return;
        }

        selectedPart = null;
        plainPartShown = true;
        GetPartCard().Show(clicked.transform, clicked.name, string.Empty, GetExplosionCamera());
        if (maintenanceSheet != null) maintenanceSheet.Hide(); // not a part: no maintenance data
        CommunicationManager.HandlePartSelected_Extern(new PartSelectedPayload
        {
            name = clicked.name,
            description = string.Empty,
            exploded = false,
            x = Screen.width > 0 ? screenPosition.x / Screen.width : 0f,
            y = Screen.height > 0 ? 1f - screenPosition.y / Screen.height : 0f
        });
    }

    private bool plainPartShown;
    private PartInfoCard partCard;
    private MaintenanceSheetPanel maintenanceSheet;

    /// <summary>Bottom-right maintenance sheet: from the Maintenance Sheet Prefab, else built in code.
    /// Child of this persistent HUD, so it is created once and kept across all scenes.</summary>
    private MaintenanceSheetPanel GetMaintenanceSheet()
    {
        if (maintenanceSheet != null) return maintenanceSheet;

        if (maintenanceSheetPrefab != null)
        {
            // One canvas for the whole UI: inside the Plant UI canvas when it exists (nested canvas, own batch).
            Transform parent = PlantUI.Instance != null && PlantUI.Instance.SheetParent != null ? PlantUI.Instance.SheetParent : transform;
            GameObject instance = Instantiate(maintenanceSheetPrefab, parent);
            if (parent != transform) PlantUI.FitNestedCanvas(instance, 30);
            instance.name = "MaintenanceSheet";
            maintenanceSheet = instance.GetComponent<MaintenanceSheetPanel>();
            if (maintenanceSheet == null)
            {
                Debug.LogWarning("[HUD-Controller] Maintenance Sheet Prefab has no MaintenanceSheetPanel - using the default layout.", this);
                Destroy(instance);
            }
        }

        if (maintenanceSheet == null) maintenanceSheet = MaintenanceSheetPanel.CreateDefault(transform);
        return maintenanceSheet;
    }

    /// <summary>Unity card (same look as the Main_Scene object card) beside the clicked part.</summary>
    private PartInfoCard GetPartCard()
    {
        if (partCard == null) partCard = PartInfoCard.Create(transform);
        return partCard;
    }

    /// <summary>The background picture as a screen-filling image at the far end of the explosion camera
    /// (it belongs to the camera, so it is removed with it).</summary>
    private void AddBackgroundImage(Camera cam)
    {
        if (explosionBackgroundImage == null) return;

        var go = new GameObject("Explosion Background", typeof(RectTransform));
        go.transform.SetParent(cam.transform, false);
        go.layer = FirstRenderedLayer(cam);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 1f, cam.farClipPlane * 0.95f);
        canvas.sortingOrder = -1000;

        var imageObject = new GameObject("Image", typeof(RectTransform));
        imageObject.layer = go.layer;
        imageObject.transform.SetParent(go.transform, false);
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        var image = imageObject.AddComponent<UnityEngine.UI.RawImage>();
        image.texture = explosionBackgroundImage;
        image.raycastTarget = false;
    }

    private static int FirstRenderedLayer(Camera cam)
    {
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0 && (cam.cullingMask & (1 << ui)) != 0) return ui;
        for (int i = 0; i < 32; i++)
            if ((cam.cullingMask & (1 << i)) != 0) return i;
        return 0;
    }

    private Camera GetExplosionCamera()
    {
        return explosionViewCameraInstance != null ? explosionViewCameraInstance.GetComponentInChildren<Camera>() : Camera.main;
    }

    public void HideClickContext()
    {
        if (partCard != null) partCard.Hide();
        if (maintenanceSheet != null) maintenanceSheet.Hide();
        if (selectedPart == null && !plainPartShown) return;

        selectedPart = null;
        plainPartShown = false;
        CommunicationManager.HandlePartDeselected_Extern();
    }

    /// <summary>Kept for existing callers; the description is part of "part.selected" now.</summary>
    public void HideDescription() { }

    public void CloseAllPopups()
    {
        HideClickContext();
    }

    public void HideEquipmentButtons()
    {
        HideClickContext();
    }

    // ---------------------------------------------------------------- electrical panel

    public void SetElectricalPanel(ElectricalPanelInfo electricalPanel)
    {
        if (electricalPanel == null) return;

        currentElectricalPanel = electricalPanel;
        CommunicationManager.HandleElectricalPanelOpened_Extern(electricalPanel.GeneratorValue);
        electricalPanel.RefreshUI(); // sends "electrical.values"
    }

    public void ResetElectricalPanel(ElectricalPanelInfo electricalPanel)
    {
        if (currentElectricalPanel != electricalPanel) return;

        currentElectricalPanel = null;
        CommunicationManager.HandleElectricalPanelClosed_Extern();
    }

    public void UpdateElectricalPanelValues(float generatorValue, float generatorKV, float gridKV, float loading,
        float oilTemp, float windingTemp, bool coolingFan, string alarmLevel = null)
    {
        LastElectricalValues = new ElectricalValuesPayload
        {
            generatorValue = generatorValue,
            generatorKV = generatorKV,
            gridKV = gridKV,
            loading = loading,
            oilTemp = oilTemp,
            windingTemp = windingTemp,
            coolingFan = coolingFan,
            alarmLevel = alarmLevel ?? (generatorValue >= 80f ? "warning" : generatorValue >= 75f ? "high" : "normal")
        };
        CommunicationManager.HandleElectricalValues_Extern(LastElectricalValues);
    }

    /// <summary>Control room slider 0..100 from React. Works anywhere in the control room (not only at the
    /// panel). From the panel's Warning Threshold (default 80) the warning lights blink.</summary>
    public void SetControlRoomSlider(float value)
    {
        ElectricalPanelInfo[] panels = FindObjectsByType<ElectricalPanelInfo>(FindObjectsSortMode.None);
        if (panels.Length == 0)
        {
            Fail(nameof(SetControlRoomSlider), "No electrical panel in this scene (open the Control_Room first).");
            return;
        }

        float clamped = Mathf.Clamp(value, 0f, 100f);
        foreach (ElectricalPanelInfo panel in panels) panel.SetGeneratorValue(clamped);
    }

    // ================================================================== called by React (through CommunicationManager)

    /// <summary>Toggle explosion view of the current equipment (enter / collapse).</summary>
    public void ToggleExplode()
    {
        // The explode button explodes every part (and collapses them all again).
        if (explodeMeansExplodeAll)
        {
            ToggleExplodeAll();
            return;
        }

        if (!CanDo(ActiveAction.Explode, nameof(ToggleExplode))) return;

        if (activeAction == ActiveAction.Explode)
        {
            Collapse();
        }
        else if (!EnterExplosionMode())
        {
            Fail(nameof(ToggleExplode), "This equipment has no explosion view.");
            return;
        }
        else
        {
            activeAction = ActiveAction.Explode;
        }

        SendState();
    }

    /// <summary>Toggle explode-all of the current equipment.</summary>
    public void ToggleExplodeAll()
    {
        if (!CanDo(ActiveAction.ExplodeAll, nameof(ToggleExplodeAll))) return;

        if (activeAction == ActiveAction.ExplodeAll)
        {
            Collapse();
        }
        else
        {
            ModularExplodedView explodedView = GetExplodedView();
            if (explodedView == null || !EnterExplosionMode())
            {
                Fail(nameof(ToggleExplodeAll), "This equipment has no explosion view.");
                return;
            }

            activeAction = ActiveAction.ExplodeAll;
            explodedView.ExplodeAll();
        }

        SendState();
    }

    /// <summary>Toggle operation (turbine: animated + exploded; boiler: fluid process).</summary>
    public void ToggleOperation()
    {
        if (!CanDo(ActiveAction.Operation, nameof(ToggleOperation))) return;

        if (currentType == EquipmentType.Boiler)
        {
            BoilerFluidController boiler = GetBoilerController();
            if (boiler == null)
            {
                Fail(nameof(ToggleOperation), "BoilerFluidController was not found.");
                return;
            }

            if (isOperating)
            {
                isOperating = false;
                activeAction = ActiveAction.None;
                boiler.ResetProcess();
            }
            else
            {
                isOperating = true;
                activeAction = ActiveAction.Operation;
                boiler.StartProcess();
            }
        }
        else
        {
            // Turbine (and other exploded equipment): operation = explosion view + animation.
            if (isOperating)
            {
                if (currentContextObject.TryGetComponent<TurbineData>(out var runningTurbine)) runningTurbine.StopOperation();
                isOperating = false;
                Collapse();
            }
            else
            {
                ModularExplodedView explodedView = GetExplodedView();
                if (explodedView == null || !EnterExplosionMode())
                {
                    Fail(nameof(ToggleOperation), "This equipment has no explosion view.");
                    return;
                }

                if (currentContextObject.TryGetComponent<TurbineData>(out var turbine)) turbine.StartOperation();
                explodedView.ExplodeAll();
                isOperating = true;
                activeAction = ActiveAction.Operation;
            }
        }

        SendState();
    }

    /// <summary>Boiler only: info sequence, which also opens the boiler dashboard.</summary>
    public void StartBoilerInfo()
    {
        if (currentType != EquipmentType.Boiler || currentContextObject == null)
        {
            Fail(nameof(StartBoilerInfo), "Info is only available at the boiler.");
            return;
        }

        if (activeAction != ActiveAction.None || IsBoilerInfoActive)
        {
            Fail(nameof(StartBoilerInfo), "Finish the current action first.");
            return;
        }

        if (BoilerOperationInfo.instanced == null)
        {
            Fail(nameof(StartBoilerInfo), "BoilerOperationInfo was not found in the scene.");
            return;
        }

        // Opens the boiler dashboard too (its GameObject is switched on by this sequence).
        BoilerOperationInfo.instanced.StartBoilerInfoSequence();
        SendState();
    }

    /// <summary>Leave any explosion view / turbine operation.</summary>
    public void CollapseAll()
    {
        if (isOperating && currentType != EquipmentType.Boiler && currentContextObject != null &&
            currentContextObject.TryGetComponent<TurbineData>(out var turbine))
        {
            turbine.StopOperation();
            isOperating = false;
        }

        if (IsExploded) Collapse();
        SendState();
    }

    /// <summary>Explode / collapse the part from the last part selection.</summary>
    public void TogglePartExplode()
    {
        if (selectedPart == null)
        {
            Fail(nameof(TogglePartExplode), "No part is selected.");
            return;
        }

        var explodedView = selectedPart.GetComponentInParent<ModularExplodedView>();
        if (explodedView == null)
        {
            Fail(nameof(TogglePartExplode), $"No ModularExplodedView found for {selectedPart.gameObject.name}.");
            return;
        }

        explodedView.ToggleExplode(selectedPart.transform);
        HideClickContext();
    }

    // ================================================================== boiler (one function per React button)

    /// <summary>Boiler: explode every part (does nothing if already exploded).</summary>
    public void BoilerExplodeAll()
    {
        if (!AtBoiler(nameof(BoilerExplodeAll))) return;
        if (activeAction == ActiveAction.ExplodeAll) { SendState(); return; }
        ToggleExplodeAll();
    }

    /// <summary>Boiler: collapse all parts and go back to the worker (does nothing if not exploded).</summary>
    public void BoilerCollapseAll()
    {
        if (!AtBoiler(nameof(BoilerCollapseAll))) return;
        if (IsExploded) Collapse();
        SendState();
    }

    /// <summary>Boiler: start the operation animation (fluid process).</summary>
    public void BoilerStartOperation()
    {
        if (!AtBoiler(nameof(BoilerStartOperation))) return;
        if (isOperating) { SendState(); return; }
        ToggleOperation();
        ApplyReactBurnerPower();
    }

    /// <summary>Boiler: stop the operation animation and reset the process.</summary>
    public void BoilerStopOperation()
    {
        if (!AtBoiler(nameof(BoilerStopOperation))) return;
        if (!isOperating) { SendState(); return; }
        ToggleOperation();
    }

    /// <summary>Boiler: info panel with particle effects (same as StartBoilerInfo).</summary>
    public void BoilerShowInfo()
    {
        if (!AtBoiler(nameof(BoilerShowInfo))) return;
        if (IsBoilerInfoActive) { SendState(); return; }
        StartBoilerInfo();
        ApplyReactBurnerPower(); // the dashboard resets the burner to its default when it opens
    }

    /// <summary>Boiler: close the info panel and its particle effects.</summary>
    public void BoilerHideInfo()
    {
        if (!AtBoiler(nameof(BoilerHideInfo))) return;

        if (BoilerDashboardController.HasInstance && BoilerDashboardController.Instance.isActiveAndEnabled)
            BoilerDashboardController.Instance.CloseDashboard();
        else if (BoilerOperationInfo.instanced != null)
            BoilerOperationInfo.instanced.ResetBoilerInfo();

        SendState();
    }

    /// <summary>Boiler burner power 0..100 from the React slider. Works at any time in the boiler room
    /// (operation, info panel or idle) and is kept when the info panel opens.</summary>
    public void SetBoilerBurnerPower(float value)
    {
        reactBurnerPower = Mathf.Clamp(value, 0f, 100f);
        if (!ApplyReactBurnerPower())
            Fail(nameof(SetBoilerBurnerPower), "No boiler in this scene.");
    }

    private bool ApplyReactBurnerPower()
    {
        if (reactBurnerPower < 0f) return true;

        if (BoilerDashboardController.HasInstance)
        {
            BoilerDashboardController.Instance.SetBurnerPower(reactBurnerPower); // also updates the dashboard values
            return true;
        }

        BoilerFluidController fluid = GetBoilerController();
        if (fluid == null) return false;
        fluid.SetBurnerPower(reactBurnerPower / 100f);
        return true;
    }

    // ================================================================== turbine (one function per React button)

    /// <summary>Turbine the worker is at: explode all parts (does nothing if already exploded).</summary>
    public void TurbineExplodeAll()
    {
        if (!AtEquipment(EquipmentType.Turbine, nameof(TurbineExplodeAll))) return;
        if (activeAction == ActiveAction.ExplodeAll) { SendState(); return; }
        ToggleExplodeAll();
    }

    /// <summary>Turbine: collapse all parts (also stops operation), back to the worker.</summary>
    public void TurbineCollapseAll()
    {
        if (!AtEquipment(EquipmentType.Turbine, nameof(TurbineCollapseAll))) return;
        CollapseAll();
    }

    /// <summary>Turbine: start operation (explosion view + spinning + steam effects).</summary>
    public void TurbineStartOperation()
    {
        if (!AtEquipment(EquipmentType.Turbine, nameof(TurbineStartOperation))) return;
        if (isOperating) { SendState(); return; }
        ToggleOperation();
    }

    /// <summary>Turbine: stop operation, back to the worker.</summary>
    public void TurbineStopOperation()
    {
        if (!AtEquipment(EquipmentType.Turbine, nameof(TurbineStopOperation))) return;
        if (!isOperating) { SendState(); return; }
        ToggleOperation();
    }

    private TurbineDataPayload GetTurbinePayload()
    {
        if (currentContextObject != null && currentContextObject.TryGetComponent<TurbineData>(out var turbine))
            return turbine.ToPayload();
        return new TurbineDataPayload { id = currentContextObject != null ? currentContextObject.name : "", name = currentContextObject != null ? currentContextObject.name : "" };
    }

    private bool AtEquipment(EquipmentType type, string command)
    {
        if (currentType == type && currentContextObject != null) return true;
        Fail(command, $"The worker is not at a {type}.");
        return false;
    }

    private bool AtBoiler(string command)
    {
        if (currentType == EquipmentType.Boiler && currentContextObject != null) return true;
        Fail(command, "The worker is not at the boiler.");
        return false;
    }

    /// <summary>Electrical panel generator value 0..100 (only while the worker is at the panel).</summary>
    public void SetGeneratorValue(float value)
    {
        if (currentElectricalPanel == null)
        {
            Fail(nameof(SetGeneratorValue), "The worker is not at the electrical panel.");
            return;
        }

        currentElectricalPanel.SetGeneratorValue(Mathf.Clamp(value, 0f, 100f));
    }

    private static void Fail(string command, string message)
    {
        CommunicationManager.HandleError_Extern(command, message);
    }

    // ================================================================== rules

    private bool HasActiveSession => activeAction != ActiveAction.None || isOperating || IsBoilerInfoActive;

    private bool IsBoilerInfoActive =>
        currentType == EquipmentType.Boiler && BoilerFluidController.instance != null && BoilerFluidController.instance.IsInfoActive;

    // Same rules as the old HUD buttons: while one action runs, only that action's button
    // (to stop it) is available; nothing works while boiler info is open.
    private bool IsAllowed(ActiveAction action)
    {
        if (currentContextObject == null) return false;
        if (currentType != EquipmentType.Turbine && currentType != EquipmentType.Boiler) return false;
        if (IsBoilerInfoActive || isCollapsing) return false;
        return activeAction == ActiveAction.None || activeAction == action;
    }

    private bool CanDo(ActiveAction action, string command)
    {
        if (currentContextObject == null)
        {
            Fail(command, "The worker is not at any equipment.");
            return false;
        }

        if (!IsAllowed(action))
        {
            Fail(command, "Not available right now (another action is running).");
            return false;
        }

        return true;
    }

    private EquipmentStatePayload BuildState()
    {
        bool infoActive = IsBoilerInfoActive;

        return new EquipmentStatePayload
        {
            inRange = contextInRange,
            type = currentType.ToString(),
            name = currentContextObject != null ? currentContextObject.name : "",
            activeAction = infoActive ? "info" : ActionName(activeAction),
            exploded = IsExploded,
            operating = isOperating,
            infoActive = infoActive,
            canExplode = IsAllowed(explodeMeansExplodeAll ? ActiveAction.ExplodeAll : ActiveAction.Explode),
            canExplodeAll = IsAllowed(ActiveAction.ExplodeAll),
            canOperate = IsAllowed(ActiveAction.Operation),
            canInfo = currentType == EquipmentType.Boiler && currentContextObject != null && activeAction == ActiveAction.None && !infoActive
        };
    }

    /// <summary>Sends the full equipment state to React (handleEquipmentState).</summary>
    public void SendState()
    {
        CommunicationManager.HandleEquipmentState_Extern(BuildState());
    }

    private static string ActionName(ActiveAction action)
    {
        switch (action)
        {
            case ActiveAction.Explode: return "explode";
            case ActiveAction.ExplodeAll: return "explodeAll";
            case ActiveAction.Operation: return "operation";
            default: return "none";
        }
    }

    // ================================================================== explosion view

    private ModularExplodedView GetExplodedView()
    {
        return currentContextObject != null ? currentContextObject.GetComponent<ModularExplodedView>() : null;
    }

    private bool EnterExplosionMode()
    {
        if (IsExploded) return false;

        ModularExplodedView explodedView = GetExplodedView();
        if (explodedView == null)
        {
            Debug.LogWarning("[HUD-Controller] No exploded view found");
            return false;
        }

        if (explosionViewCameraPrefab == null)
        {
            Debug.LogWarning("[HUD-Controller] Explosion View Camera Prefab is not assigned.", this);
            return false;
        }

        explodedView.IsolateExplosionView();
        explosionViewCameraInstance = Instantiate(explosionViewCameraPrefab);

        // Grey background instead of the (white) sky behind the isolated equipment.
        Camera explosionCamera = explosionViewCameraInstance.GetComponentInChildren<Camera>();
        if (explosionCamera != null)
        {
            // The rooms have baked occlusion data: with the room hidden, its (invisible) walls still culled the
            // exploded parts at some angles -> parts popping in / out. No occlusion culling for this camera.
            explosionCamera.useOcclusionCulling = false;
            explosionCamera.clearFlags = CameraClearFlags.SolidColor;
            explosionCamera.backgroundColor = explosionBackground;
            AddBackgroundImage(explosionCamera);
        }

        var rtsCamera = explosionViewCameraInstance.GetComponent<RTSCameraController>();
        if (rtsCamera == null)
        {
            Debug.LogWarning("[HUD-Controller] RTS camera prefab has no RTSCameraController");
            explodedView.RestoreExplosionView();
            Destroy(explosionViewCameraInstance);
            explosionViewCameraInstance = null;
            return false;
        }

        // Only one camera at a time: the worker (and its camera) is switched off during explosion view.
        CharacterMovementController player = GetPlayer();
        if (player != null) player.gameObject.SetActive(false);

        rtsCamera.Initialize(explodedView);
        return true;
    }

    // The parts slide back first (explosion camera still on); the worker comes back only when they are home.
    // Switching the worker on at once put it among moving colliders, which pushed / dragged it along.
    private void Collapse()
    {
        ModularExplodedView explodedView = GetExplodedView();
        float wait = 0f;
        if (explodedView != null)
        {
            explodedView.CollapseAll();
            wait = explodedView.AnimationDuration;
        }

        activeAction = ActiveAction.None;
        HideClickContext();

        if (collapseRoutine != null) StopCoroutine(collapseRoutine);
        collapseRoutine = null;

        if (wait > 0f && isActiveAndEnabled) collapseRoutine = StartCoroutine(FinishCollapseLater(explodedView, wait));
        else FinishCollapse(explodedView);
    }

    private IEnumerator FinishCollapseLater(ModularExplodedView explodedView, float wait)
    {
        isCollapsing = true;
        yield return new WaitForSeconds(wait + 0.05f);
        collapseRoutine = null;
        FinishCollapse(explodedView);
        SendState(); // exploded = false now
    }

    private void FinishCollapse(ModularExplodedView explodedView)
    {
        isCollapsing = false;
        if (explodedView != null) explodedView.RestoreExplosionView();

        if (explosionViewCameraInstance != null)
        {
            Destroy(explosionViewCameraInstance);
            explosionViewCameraInstance = null;
        }

        CharacterMovementController player = GetPlayer();
        if (player != null) player.gameObject.SetActive(true);
    }

    private CharacterMovementController GetPlayer()
    {
        if (cachedPlayer == null)
            cachedPlayer = FindAnyObjectByType<CharacterMovementController>(FindObjectsInactive.Include);
        return cachedPlayer;
    }

    private BoilerFluidController GetBoilerController()
    {
        if (currentContextObject != null)
        {
            var boiler = currentContextObject.GetComponent<BoilerFluidController>();
            if (boiler == null) boiler = currentContextObject.GetComponentInParent<BoilerFluidController>();
            if (boiler == null) boiler = currentContextObject.GetComponentInChildren<BoilerFluidController>();
            if (boiler != null) return boiler;
        }

        return BoilerFluidController.instance;
    }
}
