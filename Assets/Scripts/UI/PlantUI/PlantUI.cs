using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The one in-Unity UI canvas (Bootstrap scene, kept for the whole session - DontDestroyOnLoad). It only turns
/// its panels on / off for the current room and mode, nothing is created or destroyed while playing:
///   - Equipment info panel (top-left): Boiler / Turbine / Electrical, shown while the worker is at that equipment.
///   - Navigation bar (bottom): Home, Power Plant Area, Boiler Room, Turbine Room, Control Room.
///   - Toolbar (right): TPP / FPP / Fly (worker mode only), overview / worker (plant area), hide UI.
///   - Day / Night button (plant area only).
///   - Maintenance sheet: HUDController creates it inside <see cref="SheetParent"/> (same canvas).
/// Built by Tools > Thermal Plant > 17. Create Plant UI (Bootstrap); edit the layout / colours freely, keep the
/// references on the components.
/// </summary>
[DefaultExecutionOrder(-200)] // before HUDController, which puts the maintenance sheet in here
[DisallowMultipleComponent]
public class PlantUI : MonoBehaviour
{
    public static PlantUI Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private EquipmentInfoPanel boilerPanel;
    [SerializeField] private EquipmentInfoPanel turbinePanel;
    [SerializeField] private EquipmentInfoPanel electricalPanel;
    [SerializeField] private PlantNavBar navBar;
    [SerializeField] private PlantToolbar toolbar;
    [Tooltip("The maintenance sheet is placed here (explosion view).")]
    [SerializeField] private RectTransform sheetParent;

    [Header("Behaviour")]
    [Tooltip("Electrical panel also shows within this distance (m) of an electrical panel, not only in its trigger.")]
    [SerializeField, Min(0f)] private float electricalPanelDistance = 3.5f;
    [SerializeField, Min(0.05f)] private float checkInterval = 0.2f;

    private bool hidden;
    private float nextCheck;
    private Transform worker;
    private CharacterViewStateMachine views;
    private PlantIsometricCameraController overviewCamera;
    private float nextOverviewSearch, nextViewsSearch;
    private readonly List<ElectricalPanelInfo> panelsInScene = new List<ElectricalPanelInfo>();

    public RectTransform SheetParent => sheetParent;
    public bool IsHidden => hidden;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent != null) transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        worker = null;
        views = null;
        overviewCamera = null;
        nextOverviewSearch = nextViewsSearch = 0f;
        panelsInScene.Clear();
        nextCheck = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + checkInterval;
        Refresh();
    }

    // ------------------------------------------------------------------ public

    /// <summary>Hide / show all panels (toolbar stays, to bring them back).</summary>
    public void ToggleHidden()
    {
        hidden = !hidden;
        Refresh();
    }

    /// <summary>Worker present and in control (not overview, not explosion view).</summary>
    public bool WorkerActive
    {
        get
        {
            if (HUDController.HasInstance && HUDController.Instance.IsExplodedView) return false;
            PlantIsometricCameraController overview = OverviewCamera;
            if (overview != null && overview.IsInIsometricMode) return false;
            CharacterViewStateMachine v = Views();
            return v != null && v.isActiveAndEnabled;
        }
    }

    /// <summary>The plant overview camera (Power Plant Area only), searched at most every 2 s.</summary>
    public PlantIsometricCameraController OverviewCamera
    {
        get
        {
            if (overviewCamera == null && Time.unscaledTime >= nextOverviewSearch)
            {
                nextOverviewSearch = Time.unscaledTime + 2f;
                overviewCamera = FindAnyObjectByType<PlantIsometricCameraController>();
            }
            return overviewCamera;
        }
    }

    public CharacterViewStateMachine Views()
    {
        if (views != null && views.isActiveAndEnabled) return views;
        if (Time.unscaledTime < nextViewsSearch) return null;
        nextViewsSearch = Time.unscaledTime + 1f;
        views = FindAnyObjectByType<CharacterViewStateMachine>();
        worker = views != null ? views.transform : null;
        return views;
    }

    /// <summary>A UI prefab with its own Canvas placed inside this canvas: fills it, keeps its own draw order.</summary>
    public static void FitNestedCanvas(GameObject instance, int sortingOrder)
    {
        var rect = instance.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
        if (instance.TryGetComponent(out Canvas canvas))
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
        }
        if (instance.TryGetComponent(out UnityEngine.UI.CanvasScaler scaler)) scaler.enabled = false; // the root canvas scales
    }

    // ------------------------------------------------------------------ visibility

    private void Refresh()
    {
        HUDController hud = HUDController.HasInstance ? HUDController.Instance : null;
        bool workerActive = WorkerActive;
        bool exploded = hud != null && hud.IsExplodedView;
        EquipmentType type = hud != null ? hud.CurrentEquipmentType : EquipmentType.None;
        GameObject equipment = hud != null ? hud.CurrentEquipmentObject : null;

        bool showPanels = !hidden && !exploded;
        Show(boilerPanel, showPanels && type == EquipmentType.Boiler, equipment);
        Show(turbinePanel, showPanels && type == EquipmentType.Turbine, equipment);

        ElectricalPanelInfo electrical = showPanels && workerActive ? NearElectricalPanel(hud) : null;
        Show(electricalPanel, electrical != null, electrical != null ? electrical.gameObject : null);

        if (navBar != null && navBar.gameObject.activeSelf == hidden) navBar.gameObject.SetActive(!hidden);
        if (toolbar != null) toolbar.Refresh(workerActive, hidden);
    }

    private static void Show(EquipmentInfoPanel panel, bool show, GameObject source)
    {
        if (panel != null) panel.Show(show, source);
    }

    // The panel whose trigger the worker is in, else the nearest one within electricalPanelDistance.
    private ElectricalPanelInfo NearElectricalPanel(HUDController hud)
    {
        if (hud != null && hud.CurrentElectricalPanel != null && hud.CurrentElectricalPanel.isActiveAndEnabled) return hud.CurrentElectricalPanel;
        if (worker == null) return null;

        if (panelsInScene.Count == 0)
            panelsInScene.AddRange(FindObjectsByType<ElectricalPanelInfo>(FindObjectsSortMode.None));

        ElectricalPanelInfo best = null;
        float bestDistance = electricalPanelDistance * electricalPanelDistance;
        foreach (ElectricalPanelInfo p in panelsInScene)
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            Vector3 d = p.transform.position - worker.position;
            d.y = 0f;
            if (d.sqrMagnitude <= bestDistance) { bestDistance = d.sqrMagnitude; best = p; }
        }
        return best;
    }
}
