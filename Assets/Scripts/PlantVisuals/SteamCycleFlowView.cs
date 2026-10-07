using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// "How a thermal plant works" view for Main_Scene:
///   - dims the whole plant (ScreenTintOverlay channel "flow"),
///   - shows the steam-cycle flow lines (SplineParticleFlow with the AdditiveGlow "on top" material, so they
///     are visible through the buildings): water -> boiler -> steam -> turbine -> condenser -> back, + power out,
///   - shows a label at each station that follows the camera.
/// Cost: off = nothing; on = one tint quad, 4-5 line meshes and a few UI labels.
///
/// React: SetFlowView_Extern("true" | "false") -> handleFlowViewChanged.
/// Testing in Unity: press F, or use the context menu (⋮) of this component.
/// Created by Tools > Thermal Plant > 13. Create Steam-Cycle Flow View (Main_Scene).
/// </summary>
public class SteamCycleFlowView : MonoBehaviour
{
    [Serializable]
    public class Station
    {
        public string title = "Boiler";
        public string subtitle = "Water is heated into steam";
        public Transform anchor;
        public Color color = Color.white;
    }

    public static SteamCycleFlowView Instance { get; private set; }

    [SerializeField] private bool startVisible = false;
    [Tooltip("Parent of the flow lines (switched on / off).")]
    [SerializeField] private GameObject flowsRoot;
    [Tooltip("How dark the plant gets in the flow view (multiplied over the view).")]
    [SerializeField] private Color dimTint = new Color(0.32f, 0.36f, 0.45f, 1f);
    [SerializeField] private Station[] stations = new Station[0];
    [Tooltip("Key that toggles the flow view (Unity Editor and build). None = off.")]
    [SerializeField] private Key toggleKey = Key.F;

    public bool IsVisible { get; private set; }

    private Canvas labelCanvas;
    private readonly List<RectTransform> labelRects = new List<RectTransform>();

    private void Awake()
    {
        Instance = this;
        BuildLabels();
    }

    private void Start()
    {
        SetVisible(startVisible, true);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (labelCanvas != null) Destroy(labelCanvas.gameObject);
    }

    private void Update()
    {
        if (toggleKey != Key.None && Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            SetVisible(!IsVisible);
    }

    public void SetVisible(bool visible, bool instant = false)
    {
        IsVisible = visible;

        if (flowsRoot != null) flowsRoot.SetActive(visible);
        if (labelCanvas != null) labelCanvas.gameObject.SetActive(visible);
        if (ScreenTintOverlay.Instance != null) ScreenTintOverlay.Instance.SetTint("flow", visible ? dimTint : Color.white, instant);

        CommunicationManager.HandleFlowViewChanged_Extern(visible);
    }

    [ContextMenu("Show flow view")] private void ContextShow() => SetVisible(true);
    [ContextMenu("Hide flow view")] private void ContextHide() => SetVisible(false);

    // ------------------------------------------------------------------ labels

    private void LateUpdate()
    {
        if (!IsVisible || labelCanvas == null) return;

        Camera cam = ActiveCamera();
        if (cam == null) return;

        for (int i = 0; i < stations.Length && i < labelRects.Count; i++)
        {
            RectTransform rect = labelRects[i];
            Transform anchor = stations[i].anchor;
            if (anchor == null) { rect.gameObject.SetActive(false); continue; }

            Vector3 screen = cam.WorldToScreenPoint(anchor.position);
            bool inFront = screen.z > 0f;
            if (rect.gameObject.activeSelf != inFront) rect.gameObject.SetActive(inFront);
            if (inFront) rect.position = new Vector3(screen.x, screen.y, 0f);
        }
    }

    // The camera drawing on top (overview, or the worker camera).
    private static Camera ActiveCamera()
    {
        Camera best = null;
        foreach (Camera cam in Camera.allCameras)
            if (cam.isActiveAndEnabled && cam.targetTexture == null && (best == null || cam.depth > best.depth)) best = cam;
        return best;
    }

    private void BuildLabels()
    {
        var canvasGo = new GameObject("FlowView Labels", typeof(RectTransform));
        labelCanvas = canvasGo.AddComponent<Canvas>();
        labelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        labelCanvas.sortingOrder = 5;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        foreach (Station station in stations)
        {
            var label = new GameObject(station.title, typeof(RectTransform));
            label.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(240f, 56f);
            rect.pivot = new Vector2(0.5f, 0f);

            var background = label.AddComponent<Image>();
            background.color = new Color(0.04f, 0.06f, 0.12f, 0.82f);
            background.raycastTarget = false;

            AddText(rect, station.title.ToUpperInvariant(), font, 18, FontStyle.Bold, station.color, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
            AddText(rect, station.subtitle, font, 13, FontStyle.Normal, new Color(0.85f, 0.88f, 0.95f), new Vector2(0f, 0f), new Vector2(1f, 0.5f));

            labelRects.Add(rect);
        }

        canvasGo.SetActive(false);
    }

    private static void AddText(RectTransform parent, string value, Font font, int size, FontStyle style, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(8f, 0f);
        rect.offsetMax = new Vector2(-8f, 0f);

        var text = go.AddComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
    }
}
