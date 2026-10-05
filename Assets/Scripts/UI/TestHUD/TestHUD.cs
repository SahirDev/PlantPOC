#if TEST_HUD
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// TEST UI for the "Project-With-UI-" branch (only compiled with the TEST_HUD scripting define).
/// A stand-in for the React UI, so features and performance can be tested in the editor and in a
/// plain WebGL build. It talks to Unity exactly like React does:
///   - calls CommunicationManager.Instance.Xxx_Extern(...)
///   - listens to every handleXxx event through CommunicationManager.MessageSent
/// so whatever works here works for the React team too.
///
/// Layout (like the Figma): left = room panel (parameters, operation, slider), bottom = room buttons,
/// above it = Explode / Collapse near equipment, right = FPP / TPP / Fly (+ Overview in Main_Scene).
/// The minimap (MiniMapPanel) is separate and untouched. Tab frees / locks the mouse.
/// Built entirely in code: nothing to set up in any scene.
/// </summary>
public class TestHUD : MonoBehaviour
{
    // ------------------------------------------------------------------ auto start

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<TestHUD>() != null) return;
        var go = new GameObject("TestHUD");
        DontDestroyOnLoad(go);
        go.AddComponent<TestHUD>();
    }

    // ------------------------------------------------------------------ look (Figma colours)

    private static readonly Color PanelColor = new Color32(19, 24, 44, 235);
    private static readonly Color RowLine = new Color32(255, 255, 255, 18);
    private static readonly Color ButtonColor = new Color32(36, 44, 74, 255);
    private static readonly Color ActiveColor = new Color32(47, 91, 216, 255);
    private static readonly Color StartColor = new Color32(31, 107, 79, 255);
    private static readonly Color StopColor = new Color32(150, 45, 45, 255);
    private static readonly Color TextColor = Color.white;
    private static readonly Color MutedColor = new Color32(160, 170, 195, 255);
    private static readonly Color RunningColor = new Color32(70, 220, 160, 255);
    private static readonly Color[] ValueColors =
    {
        new Color32(255, 120, 110, 255), new Color32(110, 180, 255, 255), new Color32(190, 140, 255, 255),
        new Color32(255, 200, 90, 255), new Color32(90, 220, 200, 255), new Color32(255, 140, 90, 255),
        new Color32(200, 210, 230, 255)
    };

    private static readonly string[] SceneIds = { "Main_Scene", "BoilerRoom", "TurbineRoom", "Control_Room" };
    private static readonly string[] SceneTitles = { "Power Plant Area", "Boiler Room", "Turbine Room", "Control Room" };

    private const int MaxRows = 7;

    // ------------------------------------------------------------------ UI references

    private Font font;
    private Sprite rounded;

    private Text title, status, operationHint, sliderLabel, sliderValue, hint;
    private GameObject operationSection, sliderSection;
    private Button operationButton;
    private Text operationButtonText;
    private Image operationButtonImage;
    private Slider slider;
    private readonly Text[] rowLabels = new Text[MaxRows];
    private readonly Text[] rowValues = new Text[MaxRows];
    private readonly GameObject[] rows = new GameObject[MaxRows];

    private readonly Button[] sceneButtons = new Button[4];
    private readonly Image[] sceneButtonImages = new Image[4];

    private GameObject equipmentBar;
    private Button explodeButton, collapseButton;
    private GameObject doorPrompt;
    private Text doorText;

    private readonly Dictionary<string, Image> viewButtons = new Dictionary<string, Image>();
    private GameObject overviewButton;

    private GameObject loading;
    private Text loadingText;
    private RectTransform loadingBar;

    private Text toast;
    private float toastUntil;
    private Text fpsText;
    private float fpsTime;
    private int fpsFrames;

    // ------------------------------------------------------------------ state from Unity events

    private string scene = "";
    private string viewMode = "";
    private bool overview;
    private bool cursorLocked;

    private EquipmentStatePayload equipment = new EquipmentStatePayload();
    private BoilerDashboardPayload boiler;
    private bool boilerOpen;
    private readonly Dictionary<string, TurbineDataPayload> turbines = new Dictionary<string, TurbineDataPayload>();
    private string currentTurbine;
    private ElectricalValuesPayload electrical;
    private bool panelOpen;
    private SceneTriggerPayload door;
    private string selectedBuilding;

    private float nextTurbinePoll;
    private bool sliderFromCode;

    private static CommunicationManager Bridge => CommunicationManager.Instance;

    // ================================================================== lifecycle

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        rounded = CreateRoundedSprite();
        BuildUI();
        CommunicationManager.MessageSent += OnUnityMessage;
    }

    private void OnDestroy()
    {
        CommunicationManager.MessageSent -= OnUnityMessage;
    }

    private void Start()
    {
        EnsureEventSystem();
        scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene == "Bootstrap") scene = "";
        RefreshAll();
    }

    private void Update()
    {
        // Tab = free / lock the mouse (needed to click the UI while walking).
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame && Bridge != null)
            Bridge.SetCursorLocked_Extern(cursorLocked ? "false" : "true");

        // Unity cameras ignore clicks / scroll while the mouse is over this UI (React does the same).
        EventSystem events = EventSystem.current;
        ExternalUIState.PointerOverUI = events != null && !cursorLocked && events.IsPointerOverGameObject();

        // Turbine room: keep values fresh even when the worker is not at a turbine.
        if (scene == "TurbineRoom" && Time.unscaledTime >= nextTurbinePoll && Bridge != null)
        {
            nextTurbinePoll = Time.unscaledTime + 2f;
            Bridge.GetAllTurbineData_Extern();
        }

        if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);

        // FPS (for performance testing)
        fpsFrames++;
        float elapsed = Time.unscaledTime - fpsTime;
        if (elapsed >= 0.5f)
        {
            float fps = fpsFrames / elapsed;
            fpsText.text = string.Format(CultureInfo.InvariantCulture, "{0:0} FPS  {1:0.0} ms", fps, 1000f / Mathf.Max(1f, fps));
            fpsFrames = 0;
            fpsTime = Time.unscaledTime;
        }
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem (TestHUD)", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(go);
    }

    // ================================================================== Unity -> "React" events

    private void OnUnityMessage(string function, string data)
    {
        switch (function)
        {
            case "handleSceneLoading":
                ShowLoading(data, 0f);
                break;
            case "handleSceneDownloadProgress":
                var progress = JsonUtility.FromJson<SceneDownloadProgressPayload>(data);
                if (!progress.background) ShowLoading(progress.name, progress.progress);
                break;
            case "handleSceneLoaded":
                OnSceneLoaded(JsonUtility.FromJson<SceneInfoPayload>(data).name);
                break;
            case "handleSceneLoadFailed":
                loading.SetActive(false);
                ShowToast("Scene failed: " + JsonUtility.FromJson<BridgeErrorPayload>(data).message);
                break;
            case "handleError":
                var error = JsonUtility.FromJson<BridgeErrorPayload>(data);
                ShowToast(error.command + ": " + error.message);
                break;
            case "handleCursorLockChanged":
                cursorLocked = data == "true";
                RefreshHint();
                break;
            case "handleCameraModeChanged":
                overview = data == CommunicationManager.CameraModeOverview;
                RefreshViewButtons();
                RefreshPanel();
                break;
            case "handleViewModeChanged":
                viewMode = data;
                RefreshViewButtons();
                break;
            case "handleOverviewObjectSelected":
                selectedBuilding = JsonUtility.FromJson<OverviewSelectionPayload>(data).name;
                RefreshPanel();
                break;
            case "handleOverviewObjectDeselected":
                selectedBuilding = null;
                RefreshPanel();
                break;
            case "handleEquipmentState":
                equipment = JsonUtility.FromJson<EquipmentStatePayload>(data);
                RefreshEquipment();
                RefreshPanel();
                break;
            case "handleEquipmentOutOfRange":
                equipment = new EquipmentStatePayload();
                RefreshEquipment();
                RefreshPanel();
                break;
            case "handleBoilerDashboardOpened":
            case "handleBoilerDashboard":
                boiler = JsonUtility.FromJson<BoilerDashboardPayload>(data);
                boilerOpen = true;
                RefreshPanel();
                break;
            case "handleBoilerDashboardClosed":
                boilerOpen = false;
                RefreshPanel();
                break;
            case "handleTurbineData":
                var turbine = JsonUtility.FromJson<TurbineDataPayload>(data);
                turbines[turbine.id] = turbine;
                if (equipment.inRange && equipment.type == "Turbine" || turbine.operating || currentTurbine == null) currentTurbine = turbine.id;
                RefreshPanel();
                break;
            case "handleTurbineList":
                var list = JsonUtility.FromJson<TurbineListPayload>(data);
                if (list.turbines != null)
                    foreach (TurbineDataPayload t in list.turbines) turbines[t.id] = t;
                if (currentTurbine == null && list.turbines != null && list.turbines.Length > 0) currentTurbine = list.turbines[0].id;
                RefreshPanel();
                break;
            case "handleElectricalPanelOpened":
                panelOpen = true;
                if (float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out float generator)) SetSlider(generator);
                RefreshPanel();
                break;
            case "handleElectricalValues":
                electrical = JsonUtility.FromJson<ElectricalValuesPayload>(data);
                RefreshPanel();
                break;
            case "handleElectricalPanelClosed":
                panelOpen = false;
                RefreshPanel();
                break;
            case "handleSceneTriggerEntered":
                door = JsonUtility.FromJson<SceneTriggerPayload>(data);
                RefreshDoor();
                break;
            case "handleSceneTriggerExited":
                door = null;
                RefreshDoor();
                break;
        }
    }

    private void OnSceneLoaded(string name)
    {
        scene = name;
        loading.SetActive(false);

        equipment = new EquipmentStatePayload();
        boiler = null;
        boilerOpen = false;
        turbines.Clear();
        currentTurbine = null;
        electrical = null;
        panelOpen = false;
        door = null;
        selectedBuilding = null;
        overview = false;

        // Slider start values per room.
        if (scene == "BoilerRoom") SetSlider(45f);
        else if (scene == "TurbineRoom") SetSlider(TurbineData.SteamPressurePercent >= 0f ? TurbineData.SteamPressurePercent : 50f);
        else if (scene == "Control_Room") SetSlider(0f);

        RefreshAll();

        if (Bridge == null) return;
        Bridge.GetViewMode_Extern();
        if (scene == "Main_Scene") Bridge.GetCameraMode_Extern();
        if (scene == "TurbineRoom") Bridge.GetAllTurbineData_Extern();
    }

    // ================================================================== refresh

    private void RefreshAll()
    {
        RefreshSceneButtons();
        RefreshViewButtons();
        RefreshEquipment();
        RefreshDoor();
        RefreshPanel();
        RefreshHint();
    }

    private void RefreshSceneButtons()
    {
        for (int i = 0; i < SceneIds.Length; i++)
            sceneButtonImages[i].color = SceneIds[i] == scene ? ActiveColor : new Color(0, 0, 0, 0);
    }

    private void RefreshViewButtons()
    {
        bool hasWorker = scene != "";
        foreach (KeyValuePair<string, Image> pair in viewButtons)
        {
            bool active = pair.Key == "overview" ? overview : !overview && pair.Key == viewMode;
            pair.Value.color = active ? ActiveColor : ButtonColor;
        }

        overviewButton.SetActive(scene == "Main_Scene");
        viewButtons["fpp"].transform.parent.gameObject.SetActive(hasWorker);
    }

    private void RefreshEquipment()
    {
        bool show = equipment.inRange && (equipment.type == "Boiler" || equipment.type == "Turbine");
        equipmentBar.SetActive(show);
        if (!show) return;

        explodeButton.interactable = equipment.canExplode && !equipment.exploded;
        collapseButton.interactable = equipment.exploded || equipment.operating;
    }

    private void RefreshDoor()
    {
        doorPrompt.SetActive(door != null);
        if (door != null) doorText.text = string.IsNullOrEmpty(door.text) ? "Go to " + Title(door.targetScene) : door.text;
    }

    private void RefreshHint()
    {
        hint.text = cursorLocked ? "Tab: free the mouse to use the panels" : "Tab: back to looking around";
    }

    private void RefreshPanel()
    {
        title.text = Title(scene);
        switch (scene)
        {
            case "BoilerRoom": BoilerPanel(); break;
            case "TurbineRoom": TurbinePanel(); break;
            case "Control_Room": ControlRoomPanel(); break;
            default: MainPanel(); break;
        }
    }

    private void MainPanel()
    {
        SetStatus(overview ? "Overview" : "Worker", RunningColor);
        operationSection.SetActive(false);
        sliderSection.SetActive(false);

        int r = 0;
        SetRow(r++, "Camera", overview ? "Plant overview" : "Worker (" + ViewName(viewMode) + ")");
        SetRow(r++, "Selected", string.IsNullOrEmpty(selectedBuilding) ? "-" : selectedBuilding);
        SetRow(r++, "Teleport", "Right-click the ground");
        SetRow(r++, "Switch camera", "C key");
        HideRowsFrom(r);
    }

    private void BoilerPanel()
    {
        bool atBoiler = equipment.inRange && equipment.type == "Boiler";
        string state = boilerOpen && boiler != null ? Capitalize(boiler.status) : "Stopped";
        SetStatus(state, boilerOpen ? RunningColor : MutedColor);

        operationSection.SetActive(true);
        operationHint.text = boilerOpen ? "Simulation running – drag Burner Power"
            : atBoiler ? "Start or stop the boiler simulation" : "Walk up to the boiler to start it";

        if (boilerOpen && boiler != null && boiler.completed) SetOperationButton("Restart", StartColor, true);
        else if (boilerOpen) SetOperationButton("Stop Operation", StopColor, true);
        else SetOperationButton("Start Operation", StartColor, atBoiler && equipment.canInfo);

        int r = 0;
        if (boiler != null)
        {
            SetRow(r++, "Temperature", Format(boiler.temperature, "0.0", " °C"));
            SetRow(r++, "Water Level", Format(boiler.waterLevel, "0.0", " %"));
            SetRow(r++, "Steam Pressure", Format(boiler.steamPressure, "0.00", " bar"));
            SetRow(r++, "Burner Output", Format(boiler.burnerPower, "0", " %"));
            SetRow(r++, "Steam Valve", Format(boiler.valveOpening, "0", " %"));
            if (!string.IsNullOrEmpty(boiler.message)) SetRow(r++, "Message", boiler.message);
        }
        else
        {
            SetRow(r++, "Parameters", "Start the operation to see live values");
        }
        HideRowsFrom(r);

        ShowSlider("Burner Power", boilerOpen);
    }

    private void TurbinePanel()
    {
        turbines.TryGetValue(currentTurbine ?? "", out TurbineDataPayload t);
        bool atTurbine = equipment.inRange && equipment.type == "Turbine";
        bool operating = equipment.operating && atTurbine || t != null && t.operating;
        SetStatus(operating ? "Operating" : "Idle", operating ? RunningColor : MutedColor);

        operationSection.SetActive(true);
        operationHint.text = atTurbine ? "Start or stop " + (string.IsNullOrEmpty(equipment.name) ? "the turbine" : equipment.name)
            : "Walk up to a turbine to run it";
        if (atTurbine && equipment.operating) SetOperationButton("Stop Operation", StopColor, true);
        else SetOperationButton("Start Operation", StartColor, atTurbine && equipment.canOperate);

        int r = 0;
        if (t != null)
        {
            SetRow(r++, "Turbine", string.IsNullOrEmpty(t.name) ? t.id : t.name);
            SetRow(r++, "Steam Pressure", Format(t.steamPressure, "0.00", " bar"));
            SetRow(r++, "Temperature", Format(t.temperature, "0.0", " °C"));
            SetRow(r++, "Speed", Format(t.rpm, "0", " rpm"));
            SetRow(r++, "Steam Flow", Format(t.steamMassFlowRate, "0.00", " kg/s"));
            SetRow(r++, "Vibration", Format(t.vibration, "0.00", " mm/s"));
        }
        else
        {
            SetRow(r++, "Parameters", "Waiting for turbine data…");
        }
        HideRowsFrom(r);

        ShowSlider("Steam Pressure", true);
    }

    private void ControlRoomPanel()
    {
        SetStatus(panelOpen ? "Panel open" : "Idle", panelOpen ? RunningColor : MutedColor);
        operationSection.SetActive(true);
        operationHint.text = panelOpen ? "Electrical panel – drag Voltage" : "Walk to the electrical panel";
        operationButton.gameObject.SetActive(false);

        int r = 0;
        if (electrical != null)
        {
            SetRow(r++, "Generator", Format(electrical.generatorKV, "0.00", " kV"));
            SetRow(r++, "Grid", Format(electrical.gridKV, "0.00", " kV"));
            SetRow(r++, "Loading", Format(electrical.loading, "0.0", " %"));
            SetRow(r++, "Oil Temperature", Format(electrical.oilTemp, "0.0", " °C"));
            SetRow(r++, "Winding Temperature", Format(electrical.windingTemp, "0.0", " °C"));
            SetRow(r++, "Cooling Fan", electrical.coolingFan ? "On" : "Off");
            SetRow(r++, "Alarm", Capitalize(electrical.alarmLevel));
        }
        else
        {
            SetRow(r++, "Parameters", "Open the panel to see live values");
        }
        HideRowsFrom(r);

        ShowSlider("Voltage", panelOpen);
    }

    // ================================================================== actions

    private void OnOperationClicked()
    {
        if (Bridge == null) return;

        if (scene == "BoilerRoom")
        {
            if (boilerOpen && boiler != null && boiler.completed) Bridge.RestartBoiler_Extern();
            else if (boilerOpen) Bridge.CloseBoilerDashboard_Extern();
            else Bridge.StartBoilerInfo_Extern();
        }
        else if (scene == "TurbineRoom")
        {
            Bridge.ToggleOperation_Extern();
        }
    }

    private void OnSliderChanged(float value)
    {
        sliderValue.text = Mathf.RoundToInt(value) + " %";
        if (sliderFromCode || Bridge == null) return;

        string text = value.ToString("0.#", CultureInfo.InvariantCulture);
        if (scene == "BoilerRoom") Bridge.SetBurnerPower_Extern(text);
        else if (scene == "TurbineRoom") Bridge.SetSteamPressure_Extern(text);
        else if (scene == "Control_Room") Bridge.SetGeneratorValue_Extern(text);
    }

    private void ChangeScene(int index)
    {
        if (Bridge == null || SceneIds[index] == scene) return;
        Bridge.ChangeScene_Extern(SceneIds[index]);
    }

    private void SetView(string mode)
    {
        if (Bridge == null) return;
        if (mode == "overview") Bridge.SetCameraMode_Extern(CommunicationManager.CameraModeOverview);
        else Bridge.SetViewMode_Extern(mode);
    }

    // ================================================================== helpers (state -> UI)

    private void SetStatus(string text, Color color)
    {
        status.text = "\u2022 " + text;
        status.color = color;
    }

    private void SetOperationButton(string label, Color color, bool interactable)
    {
        operationButton.gameObject.SetActive(true);
        operationButtonText.text = label;
        operationButtonImage.color = color;
        operationButton.interactable = interactable;
    }

    private void SetRow(int index, string label, string value)
    {
        if (index >= MaxRows) return;
        rows[index].SetActive(true);
        rowLabels[index].text = label;
        rowValues[index].text = value;
        rowValues[index].color = ValueColors[index % ValueColors.Length];
    }

    private void HideRowsFrom(int index)
    {
        for (int i = index; i < MaxRows; i++) rows[i].SetActive(false);
    }

    private void ShowSlider(string label, bool interactable)
    {
        sliderSection.SetActive(true);
        sliderLabel.text = label;
        slider.interactable = interactable;
    }

    private void SetSlider(float value)
    {
        sliderFromCode = true;
        slider.value = Mathf.Clamp(value, 0f, 100f);
        sliderValue.text = Mathf.RoundToInt(slider.value) + " %";
        sliderFromCode = false;
    }

    private void ShowLoading(string sceneName, float progress)
    {
        loading.SetActive(true);
        loadingText.text = "Loading " + Title(sceneName) + "…  " + Mathf.RoundToInt(progress * 100f) + " %";
        loadingBar.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }

    private void ShowToast(string message)
    {
        toast.text = message;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 4f;
    }

    private static string Title(string sceneName)
    {
        int index = System.Array.IndexOf(SceneIds, sceneName);
        return index >= 0 ? SceneTitles[index] : sceneName;
    }

    private static string ViewName(string mode)
    {
        switch (mode)
        {
            case "fpp": return "first person";
            case "tpp": return "third person";
            case "fly": return "fly camera";
            default: return mode;
        }
    }

    private static string Format(float value, string format, string unit)
    {
        return value.ToString(format, CultureInfo.InvariantCulture) + unit;
    }

    private static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "-";
        text = text.ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    // ================================================================== build UI

    private void BuildUI()
    {
        var canvasObject = new GameObject("TestHUD Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15; // minimap (20) stays on top
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)canvasObject.transform;

        BuildLeftPanel(root);
        BuildBottomBar(root);
        BuildEquipmentBar(root);
        BuildDoorPrompt(root);
        BuildViewToolbar(root);
        BuildOverlays(root);
    }

    private void BuildLeftPanel(RectTransform root)
    {
        RectTransform panel = Box("RoomPanel", root, PanelColor).rectTransform;
        Place(panel, new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(380f, 0f));
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Title row
        RectTransform header = Row(panel, "Header", 32f);
        title = Label(header, "Title", "", 24, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft);
        Stretch(title.rectTransform, 0f, 0.62f);
        status = Label(header, "Status", "", 15, FontStyle.Bold, RunningColor, TextAnchor.MiddleRight);
        Stretch(status.rectTransform, 0.62f, 1f);

        // Operation
        operationSection = new GameObject("Operation", typeof(RectTransform));
        operationSection.transform.SetParent(panel, false);
        VerticalLayoutGroup opLayout = operationSection.AddComponent<VerticalLayoutGroup>();
        opLayout.spacing = 8f;
        opLayout.childControlWidth = true;
        opLayout.childControlHeight = true;
        opLayout.childForceExpandHeight = false;
        RectTransform opTitle = Row((RectTransform)operationSection.transform, "OperationTitle", 22f);
        Label(opTitle, "Label", "Operation", 17, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft);
        RectTransform opHint = Row((RectTransform)operationSection.transform, "OperationHint", 20f);
        operationHint = Label(opHint, "Hint", "", 14, FontStyle.Normal, MutedColor, TextAnchor.MiddleLeft);
        operationButton = MakeButton((RectTransform)operationSection.transform, "OperationButton", "Start Operation", 44f, StartColor, OnOperationClicked);
        operationButtonImage = operationButton.GetComponent<Image>();
        operationButtonText = operationButton.GetComponentInChildren<Text>();

        // Parameters
        RectTransform paramTitle = Row(panel, "ParametersTitle", 26f);
        Label(paramTitle, "Label", "Parameters", 17, FontStyle.Bold, MutedColor, TextAnchor.MiddleLeft);

        for (int i = 0; i < MaxRows; i++)
        {
            RectTransform row = Row(panel, "Row" + i, 34f);
            Image line = Box("Line", row, RowLine);
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 1f);
            line.sprite = null;
            rowLabels[i] = Label(row, "Label", "", 16, FontStyle.Normal, TextColor, TextAnchor.MiddleLeft);
            Stretch(rowLabels[i].rectTransform, 0f, 0.55f);
            rowValues[i] = Label(row, "Value", "", 16, FontStyle.Bold, ValueColors[i], TextAnchor.MiddleRight);
            Stretch(rowValues[i].rectTransform, 0.4f, 1f);
            rows[i] = row.gameObject;
        }

        // Slider
        sliderSection = new GameObject("SliderSection", typeof(RectTransform));
        sliderSection.transform.SetParent(panel, false);
        VerticalLayoutGroup sLayout = sliderSection.AddComponent<VerticalLayoutGroup>();
        sLayout.spacing = 6f;
        sLayout.childControlWidth = true;
        sLayout.childControlHeight = true;
        sLayout.childForceExpandHeight = false;
        RectTransform sHeader = Row((RectTransform)sliderSection.transform, "SliderHeader", 24f);
        sliderLabel = Label(sHeader, "Label", "", 17, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft);
        Stretch(sliderLabel.rectTransform, 0f, 0.7f);
        sliderValue = Label(sHeader, "Value", "", 17, FontStyle.Bold, TextColor, TextAnchor.MiddleRight);
        Stretch(sliderValue.rectTransform, 0.6f, 1f);
        slider = MakeSlider((RectTransform)sliderSection.transform);
        slider.onValueChanged.AddListener(OnSliderChanged);

        // Hint
        RectTransform hintRow = Row(panel, "Hint", 20f);
        hint = Label(hintRow, "Hint", "", 13, FontStyle.Italic, MutedColor, TextAnchor.MiddleLeft);
    }

    private void BuildBottomBar(RectTransform root)
    {
        RectTransform bar = Box("SceneBar", root, PanelColor).rectTransform;
        Place(bar, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(860f, 64f));
        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 9, 9);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;

        for (int i = 0; i < SceneIds.Length; i++)
        {
            int index = i;
            Button button = MakeButton(bar, SceneIds[i], SceneTitles[i], 46f, new Color(0, 0, 0, 0), () => ChangeScene(index));
            sceneButtons[i] = button;
            sceneButtonImages[i] = button.GetComponent<Image>();
        }
    }

    private void BuildEquipmentBar(RectTransform root)
    {
        RectTransform bar = Box("EquipmentBar", root, PanelColor).rectTransform;
        Place(bar, new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(380f, 60f));
        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;

        explodeButton = MakeButton(bar, "Explode", "Explode", 44f, ActiveColor, () => { if (Bridge != null) Bridge.ToggleExplode_Extern(); });
        collapseButton = MakeButton(bar, "Collapse", "Collapse", 44f, ButtonColor, () => { if (Bridge != null) Bridge.CollapseEquipment_Extern(); });
        equipmentBar = bar.gameObject;
    }

    private void BuildDoorPrompt(RectTransform root)
    {
        RectTransform prompt = Box("DoorPrompt", root, PanelColor).rectTransform;
        Place(prompt, new Vector2(0.5f, 0f), new Vector2(0f, 172f), new Vector2(520f, 56f));
        doorText = Label(prompt, "Text", "", 17, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft);
        Stretch(doorText.rectTransform, 0.04f, 0.7f);
        Button enter = MakeButton(prompt, "Enter", "Enter  (Y)", 40f, ActiveColor, () =>
        {
            if (Bridge != null && door != null) Bridge.ChangeScene_Extern(door.targetScene);
        });
        RectTransform enterRect = (RectTransform)enter.transform;
        enterRect.anchorMin = new Vector2(0.72f, 0.5f);
        enterRect.anchorMax = new Vector2(0.97f, 0.5f);
        enterRect.sizeDelta = new Vector2(0f, 40f);
        enterRect.anchoredPosition = Vector2.zero;
        doorPrompt = prompt.gameObject;
    }

    private void BuildViewToolbar(RectTransform root)
    {
        RectTransform bar = Box("ViewToolbar", root, PanelColor).rectTransform;
        Place(bar, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(96f, 0f));
        VerticalLayoutGroup layout = bar.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        bar.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        AddViewButton(bar, "fpp", "1st\nPerson");
        AddViewButton(bar, "tpp", "3rd\nPerson");
        AddViewButton(bar, "fly", "Fly\nCam");
        overviewButton = AddViewButton(bar, "overview", "Plant\nView");

        // FPS readout under the toolbar
        fpsText = Label(root, "FPS", "", 14, FontStyle.Bold, MutedColor, TextAnchor.UpperRight);
        Place(fpsText.rectTransform, new Vector2(1f, 1f), new Vector2(-130f, -24f), new Vector2(220f, 24f));
    }

    private GameObject AddViewButton(RectTransform bar, string mode, string label)
    {
        Button button = MakeButton(bar, mode, label, 64f, ButtonColor, () => SetView(mode));
        button.GetComponentInChildren<Text>().fontSize = 14;
        viewButtons[mode] = button.GetComponent<Image>();
        return button.gameObject;
    }

    private void BuildOverlays(RectTransform root)
    {
        // Loading
        Image cover = Box("Loading", root, new Color(0.03f, 0.04f, 0.08f, 0.92f));
        RectTransform coverRect = cover.rectTransform;
        coverRect.anchorMin = Vector2.zero;
        coverRect.anchorMax = Vector2.one;
        coverRect.sizeDelta = Vector2.zero;
        cover.sprite = null;
        loadingText = Label(coverRect, "Text", "", 22, FontStyle.Bold, TextColor, TextAnchor.MiddleCenter);
        Place(loadingText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(700f, 40f));
        Image track = Box("Track", coverRect, ButtonColor);
        Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(500f, 10f));
        Image fill = Box("Fill", track.rectTransform, ActiveColor);
        loadingBar = fill.rectTransform;
        loadingBar.anchorMin = Vector2.zero;
        loadingBar.anchorMax = new Vector2(0f, 1f);
        loadingBar.sizeDelta = Vector2.zero;
        loading = cover.gameObject;
        loading.SetActive(false);

        // Error toast
        toast = Label(root, "Toast", "", 16, FontStyle.Bold, new Color32(255, 170, 160, 255), TextAnchor.MiddleCenter);
        Place(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 30f));
        toast.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ widgets

    private Image Box(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = rounded;
        image.type = Image.Type.Sliced;
        image.color = color;
        return image;
    }

    private RectTransform Row(RectTransform parent, string name, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        LayoutElement element = go.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        return (RectTransform)go.transform;
    }

    private Text Label(Transform parent, string name, string text, int size, FontStyle style, Color color, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text label = go.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = anchor;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        return label;
    }

    private Button MakeButton(RectTransform parent, string name, string label, float height, Color color, UnityEngine.Events.UnityAction onClick)
    {
        Image image = Box(name, parent, color);
        LayoutElement element = image.gameObject.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        Label(image.transform, "Label", label, 16, FontStyle.Bold, TextColor, TextAnchor.MiddleCenter);
        return button;
    }

    private Slider MakeSlider(RectTransform parent)
    {
        RectTransform root = Row(parent, "Slider", 26f);
        Slider s = root.gameObject.AddComponent<Slider>();

        Image background = Box("Background", root, ButtonColor);
        background.rectTransform.anchorMin = new Vector2(0f, 0.35f);
        background.rectTransform.anchorMax = new Vector2(1f, 0.65f);
        background.rectTransform.sizeDelta = Vector2.zero;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root, false);
        RectTransform fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.anchorMin = new Vector2(0f, 0.35f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.65f);
        fillAreaRect.offsetMin = new Vector2(5f, 0f);
        fillAreaRect.offsetMax = new Vector2(-12f, 0f);
        Image fill = Box("Fill", fillAreaRect, new Color32(240, 110, 50, 255));
        fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(root, false);
        RectTransform handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10f, 0f);
        handleAreaRect.offsetMax = new Vector2(-10f, 0f);
        Image handle = Box("Handle", handleAreaRect, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(20f, 0f);

        s.fillRect = fill.rectTransform;
        s.handleRect = handle.rectTransform;
        s.targetGraphic = handle;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0f;
        s.maxValue = 100f;
        return s;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, float fromX, float toX)
    {
        rect.anchorMin = new Vector2(fromX, 0f);
        rect.anchorMax = new Vector2(toX, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>White rounded rectangle (radius 12 px), 9-sliced, made once in code.</summary>
    private static Sprite CreateRoundedSprite()
    {
        const int size = 48, radius = 12;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "TestHUD Rounded", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
            float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            byte alpha = (byte)(Mathf.Clamp01(radius - distance + 0.5f) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
    }
}
#endif
