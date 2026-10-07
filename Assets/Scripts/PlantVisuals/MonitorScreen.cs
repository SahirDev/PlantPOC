using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Live parameters on a monitor's screen (BoilerRoom: boiler values, TurbineRoom: each monitor shows the
/// nearest turbine). A small world-space canvas is laid flat on the screen mesh, so the text is sharp and
/// glows like a display (unlit). Values refresh every few seconds (default 5) - no per-frame cost.
///
/// Installs itself: in a scene with a boiler or turbines, every object named "Display" / "Screen" that is
/// (inside) a "Monitor" gets one. Or add it by hand to the screen object and pick the source / turbine.
/// If the panel shows on the back of the screen, tick Flip Side.
/// </summary>
public class MonitorScreen : MonoBehaviour
{
    public enum Source { Auto, Boiler, Turbine }

    [SerializeField] private Source source = Source.Auto;
    [Tooltip("Turbine shown (Source = Turbine). Empty = the nearest one.")]
    [SerializeField] private TurbineData turbine;
    [SerializeField, Min(1f)] private float refreshSeconds = 5f;
    [Tooltip("Panel on the other face of the screen mesh.")]
    [SerializeField] private bool flipSide;
    [Tooltip("Free border around the panel, as a fraction of the screen size.")]
    [SerializeField, Range(0f, 0.2f)] private float margin = 0.03f;

    // ------------------------------------------------------------------ look (as the React panel)
    private static readonly Color PanelTop = new Color(0.10f, 0.12f, 0.20f, 1f);
    private static readonly Color PanelBottom = new Color(0.17f, 0.08f, 0.16f, 1f);
    private static readonly Color TitleColor = new Color(0.86f, 0.89f, 0.95f, 1f);
    private static readonly Color LabelColor = new Color(0.86f, 0.89f, 0.95f, 1f);
    private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color MutedColor = new Color(0.62f, 0.67f, 0.78f, 1f);

    private static readonly Color Red = new Color(1f, 0.45f, 0.42f);
    private static readonly Color Blue = new Color(0.33f, 0.68f, 1f);
    private static readonly Color Purple = new Color(0.78f, 0.52f, 1f);
    private static readonly Color Amber = new Color(1f, 0.74f, 0.25f);
    private static readonly Color Cyan = new Color(0.30f, 0.92f, 0.95f);
    private static readonly Color Rust = new Color(0.93f, 0.40f, 0.33f);
    private static readonly Color Green = new Color(0.35f, 0.88f, 0.50f);

    private struct Row
    {
        public string label;
        public Color color;
    }

    private static readonly Row[] BoilerRows =
    {
        new Row { label = "Temperature", color = Red },
        new Row { label = "Water Level", color = Blue },
        new Row { label = "Steam Pressure", color = Purple },
        new Row { label = "Burner Output", color = Amber },
        new Row { label = "Steam Valve", color = Cyan },
        new Row { label = "Winding Temperature", color = Rust },
    };

    private static readonly Row[] TurbineRows =
    {
        new Row { label = "Status", color = Green },
        new Row { label = "Temperature", color = Red },
        new Row { label = "Steam Pressure", color = Purple },
        new Row { label = "Speed", color = Amber },
        new Row { label = "Steam Flow", color = Blue },
        new Row { label = "Vibration", color = Cyan },
    };

    private TMP_Text title, footer;
    private TMP_Text[] values;
    private Image statusDot;
    private float nextRefresh;
    private bool built;

    // ------------------------------------------------------------------ self-install

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) InstallIn(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallIn(scene);

    private static void InstallIn(Scene scene)
    {
        if (!scene.isLoaded) return;

        bool hasTurbines = false, hasBoiler = false;
        var screens = new List<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<TurbineData>(true) != null) hasTurbines = true;
            if (root.GetComponentInChildren<BoilerFluidController>(true) != null) hasBoiler = true;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (IsScreenName(r.name) && InsideMonitor(r.transform) && r.GetComponent<MonitorScreen>() == null)
                    screens.Add(r.transform);
        }

        if (!hasTurbines && !hasBoiler) return; // only the boiler / turbine rooms
        foreach (Transform screen in screens)
            screen.gameObject.AddComponent<MonitorScreen>().source = hasTurbines ? Source.Turbine : Source.Boiler;
    }

    private static bool IsScreenName(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("display") || n.Contains("screen");
    }

    private static bool InsideMonitor(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name.ToLowerInvariant().Contains("monitor")) return true;
        return false;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Start()
    {
        if (source == Source.Auto)
            source = FindAnyObjectByType<TurbineData>() != null ? Source.Turbine : Source.Boiler;
        if (source == Source.Turbine && turbine == null) turbine = NearestTurbine();

        built = Build();
        Refresh();
    }

    private void Update()
    {
        if (!built || Time.time < nextRefresh) return;
        Refresh();
    }

    private TurbineData NearestTurbine()
    {
        TurbineData best = null;
        float bestDistance = float.MaxValue;
        foreach (TurbineData t in FindObjectsByType<TurbineData>(FindObjectsSortMode.None))
        {
            if (t.gameObject.scene != gameObject.scene) continue;
            float d = (t.transform.position - transform.position).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = t; }
        }
        return best;
    }

    // ------------------------------------------------------------------ values

    private void Refresh()
    {
        nextRefresh = Time.time + refreshSeconds;
        if (!built) return;

        if (source == Source.Turbine) ShowTurbine();
        else ShowBoiler();

        footer.text = "Updated " + System.DateTime.Now.ToString("HH:mm:ss");
    }

    private void ShowBoiler()
    {
        BoilerMonitorValues.Values v = BoilerMonitorValues.Current();
        title.text = "Boiler Parameters";
        values[0].text = $"{v.temperature:F1} °C";
        values[1].text = $"{v.waterLevel:F1} %";
        values[2].text = $"{v.pressure:F2} bar";
        values[3].text = $"{v.burner:F0} %";
        values[4].text = $"{v.valve:F0} %";
        values[5].text = $"{v.winding:F0} °C";
        statusDot.color = v.operating ? Green : MutedColor;
    }

    private void ShowTurbine()
    {
        if (turbine == null)
        {
            title.text = "Turbine Parameters";
            for (int i = 0; i < values.Length; i++) values[i].text = "-";
            statusDot.color = MutedColor;
            return;
        }

        TurbineDataPayload p = turbine.ToPayload();
        title.text = (string.IsNullOrEmpty(p.name) ? turbine.name : p.name) + " Parameters";
        values[0].text = p.operating ? "Running" : "Standby";
        values[0].color = p.operating ? Green : Amber;
        values[1].text = $"{p.temperature:F1} °C";
        values[2].text = $"{p.steamPressure:F1} bar";
        values[3].text = $"{p.rpm:F0} rpm";
        values[4].text = $"{p.steamMassFlowRate:F1} kg/s";
        values[5].text = $"{p.vibration:F1} µm";
        statusDot.color = p.operating ? Green : Amber;
    }

    // ------------------------------------------------------------------ panel on the screen mesh

    private bool Build()
    {
        if (!TryGetComponent(out Renderer screenRenderer)) return false;

        // Screen plane in the screen's local space: thinnest axis = normal, the axis closest to world up = height.
        Bounds local = screenRenderer.localBounds;
        Vector3 size = local.size;
        int normal = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        int a = (normal + 1) % 3, b = (normal + 2) % 3;
        float upA = Mathf.Abs(Vector3.Dot(transform.TransformDirection(Axis(a)).normalized, Vector3.up));
        float upB = Mathf.Abs(Vector3.Dot(transform.TransformDirection(Axis(b)).normalized, Vector3.up));
        int heightAxis = upA >= upB ? a : b;
        int widthAxis = heightAxis == a ? b : a;

        Vector3 up = Axis(heightAxis) * Mathf.Sign(Vector3.Dot(transform.TransformDirection(Axis(heightAxis)), Vector3.up) + 1e-6f);

        // Front = away from the monitor body (the screen sits on its front face).
        float front = 1f;
        Renderer body = transform.parent != null ? transform.parent.GetComponentInParent<Renderer>() : null;
        if (body != null && body != screenRenderer)
        {
            Vector3 normalWorld = transform.TransformDirection(Axis(normal));
            float side = Vector3.Dot(screenRenderer.bounds.center - body.bounds.center, normalWorld);
            if (Mathf.Abs(side) > 1e-4f) front = Mathf.Sign(side);
        }
        if (flipSide) front = -front;

        // World length of one local unit along each axis (screen may be scaled per axis).
        float worldWidthUnit = transform.TransformVector(Axis(widthAxis)).magnitude;
        float worldHeightUnit = transform.TransformVector(Axis(heightAxis)).magnitude;
        float worldNormalUnit = transform.TransformVector(Axis(normal)).magnitude;
        float worldWidth = size[widthAxis] * worldWidthUnit * (1f - 2f * margin);
        float worldHeight = size[heightAxis] * worldHeightUnit * (1f - 2f * margin);
        if (worldWidth <= 0.0001f || worldHeight <= 0.0001f) return false;

        const float canvasWidth = 600f;
        float metresPerUnit = worldWidth / canvasWidth;
        float canvasHeight = worldHeight / metresPerUnit;

        var canvasObject = new GameObject("MonitorPanel", typeof(RectTransform));
        canvasObject.layer = gameObject.layer;
        var rect = (RectTransform)canvasObject.transform;
        rect.SetParent(transform, false);
        rect.localRotation = Quaternion.LookRotation(-Axis(normal) * front, up); // canvas faces the viewer
        // Axis-aligned with the screen: per-axis scale undoes the screen's own scale -> square pixels.
        Vector3 scale = Vector3.one;
        scale.x = metresPerUnit / worldWidthUnit;
        scale.y = metresPerUnit / worldHeightUnit;
        scale.z = metresPerUnit / worldNormalUnit;
        rect.localScale = scale;
        rect.localPosition = local.center + Axis(normal) * front * (size[normal] * 0.5f + 0.003f / worldNormalUnit);
        rect.sizeDelta = new Vector2(canvasWidth, canvasHeight);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

        BuildPanel(rect, source == Source.Turbine ? TurbineRows : BoilerRows, canvasHeight);
        return true;
    }

    private static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    private void BuildPanel(RectTransform root, Row[] rows, float height)
    {
        // Background: two-tone (top / bottom) for the gradient look.
        Fill(NewImage("Bottom", root, PanelBottom), 0f, 0f, 1f, 1f);
        Fill(NewImage("Top", root, new Color(PanelTop.r, PanelTop.g, PanelTop.b, 0.75f)), 0f, 0.35f, 1f, 1f);

        float titleShare = Mathf.Clamp(70f / height, 0.12f, 0.2f);
        float footerShare = Mathf.Clamp(34f / height, 0.06f, 0.1f);
        float rowsTop = 1f - titleShare, rowsBottom = footerShare;
        float rowHeight = (rowsTop - rowsBottom) / rows.Length;
        float rowPixels = rowHeight * height;
        float textSize = Mathf.Clamp(rowPixels * 0.42f, 14f, 34f);

        title = NewText("Title", root, "Parameters", textSize * 1.1f, TitleColor, TextAlignmentOptions.MidlineLeft);
        Fill(title.rectTransform, 0.06f, rowsTop, 0.85f, 1f);

        statusDot = NewImage("Status", root, MutedColor);
        float dot = Mathf.Min(titleShare * height * 0.28f, 16f);
        statusDot.rectTransform.anchorMin = statusDot.rectTransform.anchorMax = new Vector2(0.93f, rowsTop + titleShare * 0.5f);
        statusDot.rectTransform.sizeDelta = new Vector2(dot, dot);

        values = new TMP_Text[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            float top = rowsTop - i * rowHeight, bottom = top - rowHeight;

            Image line = NewImage("Line", root, LineColor);
            Fill(line.rectTransform, 0.04f, top, 0.96f, top);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f);

            Image marker = NewImage("Marker", root, rows[i].color);
            Fill(marker.rectTransform, 0.05f, bottom + rowHeight * 0.28f, 0.062f, top - rowHeight * 0.28f);

            TMP_Text label = NewText("Label", root, rows[i].label, textSize, LabelColor, TextAlignmentOptions.MidlineLeft);
            Fill(label.rectTransform, 0.1f, bottom, 0.66f, top);

            values[i] = NewText("Value", root, "-", textSize, rows[i].color, TextAlignmentOptions.MidlineRight);
            values[i].fontStyle = FontStyles.Bold;
            Fill(values[i].rectTransform, 0.6f, bottom, 0.95f, top);
        }

        footer = NewText("Footer", root, "", textSize * 0.6f, MutedColor, TextAlignmentOptions.MidlineRight);
        Fill(footer.rectTransform, 0.05f, 0f, 0.95f, footerShare);
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Fill(Graphic graphic, float xMin, float yMin, float xMax, float yMax) => Fill(graphic.rectTransform, xMin, yMin, xMax, yMax);

    private static void Fill(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
    {
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}

/// <summary>
/// Boiler values for the monitor: the dashboard's simulation while it is open, otherwise a light model from the
/// boiler itself (water level, burner power, operating) so the monitor always shows plausible live values.
/// </summary>
public static class BoilerMonitorValues
{
    public struct Values
    {
        public float temperature, waterLevel, pressure, burner, valve, winding;
        public bool operating;
    }

    private static float temperature = 25.8f;
    private static int lastFrame = -1;
    private static Values last;

    public static Values Current()
    {
        if (lastFrame == Time.frameCount) return last; // several monitors, one calculation
        lastFrame = Time.frameCount;

        BoilerFluidController fluid = BoilerFluidController.instance;
        var v = new Values
        {
            operating = fluid != null && fluid.IsOperating,
            burner = fluid != null ? fluid.BurnerPower * 100f : 45f,
            waterLevel = fluid != null ? fluid.CurrentWaterLevel * 100f : 100f,
        };

        BoilerDashboardController dashboard = BoilerDashboardController.Instance; // only while it is open
        if (dashboard != null && dashboard.isActiveAndEnabled)
        {
            BoilerDashboardPayload p = dashboard.CurrentValues;
            v.temperature = temperature = p.temperature;
            v.waterLevel = p.waterLevel;
            v.pressure = p.steamPressure;
            v.burner = p.burnerPower;
            v.valve = p.valveOpening;
            v.operating = true;
        }
        else
        {
            // Heats towards a burner-dependent temperature while operating, cools to ambient otherwise.
            float target = v.operating ? 25f + v.burner * 1.15f : 25.8f;
            temperature = Mathf.Lerp(temperature, target, 0.35f) + Random.Range(-0.3f, 0.3f);
            v.temperature = temperature;
            v.pressure = Mathf.Clamp((temperature - 100f) / 5f, 0f, 10f);
            v.valve = 0f;
        }

        v.winding = 60f + v.burner * 0.09f + Random.Range(-1.5f, 1.5f);
        last = v;
        return v;
    }
}
