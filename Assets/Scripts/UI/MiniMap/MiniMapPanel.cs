using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The minimap UI. Lives in the Bootstrap scene and stays for the whole session.
///
/// NO camera: it shows the current scene's picture (MiniMapArea.MapImage) as a UI image and moves a
/// worker icon over it. Cost per frame = moving one icon.
///   - Zoom 1 shows the whole picture; + / - zooms in around the worker.
///   - Expand makes the panel bigger.
///   - Hidden while there is no active worker (overview, explosion view), in scenes without a
///     MiniMapArea, and when React says so (SetMiniMapVisible_Extern).
///
/// Built by Tools > Thermal Plant > 5. Create MiniMap Panel – restyle it freely, keep the references.
/// </summary>
public class MiniMapPanel : SingletonMono<MiniMapPanel>
{
    protected override bool PersistAcrossScenes => true;

    [Header("References")]
    [Tooltip("The panel that is shown / hidden and resized when expanded.")]
    [SerializeField] private RectTransform panel;
    [Tooltip("Masked area the map is shown in.")]
    [SerializeField] private RectTransform mapFrame;
    [Tooltip("Image that shows the scene's picture (child of Map Frame).")]
    [SerializeField] private Image mapImage;
    [Tooltip("Worker icon (child of Map Image). Its picture should point UP.")]
    [SerializeField] private RectTransform workerIcon;
    [SerializeField] private Button zoomInButton;
    [SerializeField] private Button zoomOutButton;
    [SerializeField] private Button expandButton;
    [Tooltip("Optional: turns so it always points to world north (+Z).")]
    [SerializeField] private RectTransform northIcon;
    [Tooltip("The EventSystem for the minimap buttons (kept across scenes; other EventSystems are switched off).")]
    [SerializeField] private EventSystem eventSystem;

    [Header("Behaviour")]
    [SerializeField] private Vector2 expandedSize = new Vector2(720f, 520f);
    [SerializeField, Min(1.05f)] private float zoomStep = 1.5f;
    [SerializeField, Min(1f)] private float maxZoom = 4f;
    [Tooltip("Turn the worker icon with the worker's heading.")]
    [SerializeField] private bool rotateWorkerIcon = true;

    private Vector2 normalSize;
    private bool visibleByReact = true;
    private bool expanded;
    private float zoom = 1f;

    private MiniMapArea area;
    private Player worker;
    private float nextWorkerSearch;

    public bool Expanded => expanded;

    protected override void OnSingletonAwake()
    {
        if (panel != null) normalSize = panel.sizeDelta;

        if (zoomInButton != null) zoomInButton.onClick.AddListener(ZoomIn);
        if (zoomOutButton != null) zoomOutButton.onClick.AddListener(ZoomOut);
        if (expandButton != null) expandButton.onClick.AddListener(ToggleExpanded);

        if (mapImage != null)
        {
            mapImage.preserveAspect = false; // we size it ourselves
            RectTransform rect = mapImage.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        }

        if (workerIcon != null)
        {
            workerIcon.anchorMin = workerIcon.anchorMax = new Vector2(0.5f, 0.5f);
            foreach (Graphic graphic in workerIcon.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }

        if (eventSystem != null && eventSystem.transform.parent == null) DontDestroyOnLoad(eventSystem.gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        SetPanelActive(false);
    }

    protected override void OnSingletonDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ------------------------------------------------------------------ public (React / buttons)

    /// <summary>React can hide the minimap (e.g. while a big panel is open).</summary>
    public void SetVisible(bool visible) => visibleByReact = visible;

    public void ZoomIn() => zoom = Mathf.Clamp(zoom * zoomStep, 1f, maxZoom);
    public void ZoomOut() => zoom = Mathf.Clamp(zoom / zoomStep, 1f, maxZoom);
    public void ToggleExpanded() => SetExpanded(!expanded);

    public void SetExpanded(bool value)
    {
        expanded = value;
        if (panel != null) panel.sizeDelta = expanded ? expandedSize : normalSize;
    }

    // ------------------------------------------------------------------ update

    private void LateUpdate()
    {
        if (area == null) area = MiniMapArea.Current;
        Player current = GetWorker();

        bool show = visibleByReact && area != null && area.MapImage != null
                    && current != null && current.gameObject.activeInHierarchy && !current.IsControlSuspended;

        SetPanelActive(show);
        if (!show) return;

        UpdateMap(current.transform);
    }

    private void UpdateMap(Transform workerTransform)
    {
        if (mapImage == null || mapFrame == null) return;

        if (mapImage.sprite != area.MapImage) mapImage.sprite = area.MapImage;

        // Picture fitted inside the frame (keeps its proportions), times zoom.
        Vector2 frame = mapFrame.rect.size;
        float aspect = area.ImageAspect;
        Vector2 fitted = frame.x / frame.y > aspect ? new Vector2(frame.y * aspect, frame.y) : new Vector2(frame.x, frame.x / aspect);
        Vector2 size = fitted * zoom;
        RectTransform mapRect = mapImage.rectTransform;
        mapRect.sizeDelta = size;

        // Worker on the picture (0..1), clamped to the picture's edge.
        Vector2 uv = area.WorldToMap(workerTransform.position);
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);
        Vector2 iconPosition = new Vector2((uv.x - 0.5f) * size.x, (uv.y - 0.5f) * size.y);

        // Zoomed in: keep the worker in the middle, but never show past the picture's edge.
        Vector2 offset = -iconPosition;
        Vector2 maxOffset = new Vector2(Mathf.Max(0f, (size.x - frame.x) * 0.5f), Mathf.Max(0f, (size.y - frame.y) * 0.5f));
        offset.x = Mathf.Clamp(offset.x, -maxOffset.x, maxOffset.x);
        offset.y = Mathf.Clamp(offset.y, -maxOffset.y, maxOffset.y);
        mapRect.anchoredPosition = offset;

        if (workerIcon != null)
        {
            workerIcon.anchoredPosition = iconPosition;

            // Heading relative to the picture's "up" (UI rotation is counter-clockwise).
            float angle = rotateWorkerIcon ? area.MapYaw - workerTransform.eulerAngles.y : 0f;
            workerIcon.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        if (northIcon != null) northIcon.localEulerAngles = new Vector3(0f, 0f, area.MapYaw);
    }

    private Player GetWorker()
    {
        if (worker != null) return worker;
        if (Time.unscaledTime < nextWorkerSearch) return null;

        nextWorkerSearch = Time.unscaledTime + 1f;
        worker = FindAnyObjectByType<Player>(FindObjectsInactive.Include);
        return worker;
    }

    private void SetPanelActive(bool active)
    {
        GameObject target = panel != null ? panel.gameObject : null;
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // New scene: its own picture and its own worker.
        area = null;
        worker = null;
        nextWorkerSearch = 0f;
        zoom = 1f;
        SetExpanded(false);

        if (eventSystem == null) return;

        // Scenes may still contain their own EventSystem: only ours stays on.
        foreach (EventSystem other in FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (other == eventSystem) continue;
            foreach (BaseInputModule module in other.GetComponents<BaseInputModule>()) module.enabled = false;
            other.enabled = false;
        }
    }
}
