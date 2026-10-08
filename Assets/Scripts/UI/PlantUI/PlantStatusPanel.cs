using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// "Thermal Power Plant Status Panel" (Power Plant Area, opened with the (i) button beside Day / Night).
/// Observation only - no sliders, no buttons that change the plant. Tabs:
///   Boiler Room (2) · Turbine Room (4) · Electrical Info Panel (3) · Condenser (2) · Chimney (smoke cases).
/// Values come from <see cref="PlantStatusSimulation"/> and change every 5 s with the rest of the plant
/// (PlantDataClock). The chimney follows the real smoke level of the plant (SmokeColorController, 0 / 0.2 / 0.4 /
/// 0.6 / 0.8 / 1.0 cases).
/// Built by Tools > Thermal Plant > 18. Update Plant UI - restyle freely, keep the references.
/// </summary>
public class PlantStatusPanel : MonoBehaviour
{
    public enum Page { Boiler, Turbine, Electrical, Condenser, Chimney }

    [Serializable]
    public class Tab
    {
        public Button button;
        public Image highlight;
        public TMP_Text label;
        public Image icon;
        public Image countBackground;
        public GameObject page;
    }

    [Header("Tabs (Boiler, Turbine, Electrical, Condenser, Chimney)")]
    [SerializeField] private Tab[] tabs = new Tab[0];
    [SerializeField] private Color tabActive = new Color(0.17f, 0.32f, 0.85f, 1f);
    [SerializeField] private Color tabText = new Color(0.86f, 0.89f, 0.95f, 1f);
    [SerializeField] private Color countIdle = new Color(1f, 1f, 1f, 0.14f);

    [Header("Cards")]
    [SerializeField] private PlantStatusCard[] boilers = new PlantStatusCard[0];
    [SerializeField] private PlantStatusCard[] turbines = new PlantStatusCard[0];
    [SerializeField] private PlantStatusCard[] electricalPanels = new PlantStatusCard[0];
    [SerializeField] private PlantStatusCard[] condensers = new PlantStatusCard[0];
    [SerializeField] private PlantStatusCard chimney;

    [Header("Chimney smoke cases (0, 0.2, 0.4, 0.6, 0.8, 1.0)")]
    [SerializeField] private Image[] caseFrames = new Image[0];
    [SerializeField] private Image[] caseSwatches = new Image[0];
    [SerializeField] private TMP_Text smokeIndication;
    [SerializeField] private Color caseFrameIdle = new Color(1f, 1f, 1f, 0.05f);

    [Header("Header")]
    [SerializeField] private TMP_Text dateText;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button backdropButton;

    [Header("Status colours")]
    [SerializeField] private Color running = new Color(0.3f, 0.9f, 0.92f);
    [SerializeField] private Color high = new Color(1f, 0.74f, 0.25f);
    [SerializeField] private Color alarm = new Color(1f, 0.38f, 0.32f);
    [SerializeField] private Color good = new Color(0.35f, 0.88f, 0.5f);

    private Page page = Page.Turbine;
    private int shownTick = int.MinValue;
    private float shownSmoke = -1f;
    private float nextClock;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public bool IsOpen => gameObject.activeSelf;

    // ------------------------------------------------------------------ open / close

    private void Awake()
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            if (tabs[i]?.button != null) tabs[i].button.onClick.AddListener(() => ShowPage((Page)index));
        }
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backdropButton != null) backdropButton.onClick.AddListener(Close);
        ShowPage(page);
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        shownTick = int.MinValue;
        nextClock = 0f;
        Refresh();
    }

    public void Close()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    public void ShowPage(Page value)
    {
        page = value;
        for (int i = 0; i < tabs.Length; i++)
        {
            Tab tab = tabs[i];
            if (tab == null) continue;
            bool active = i == (int)value;
            if (tab.page != null && tab.page.activeSelf != active) tab.page.SetActive(active);
            if (tab.highlight != null) tab.highlight.color = active ? tabActive : new Color(tabActive.r, tabActive.g, tabActive.b, 0f);
            if (tab.label != null) tab.label.color = active ? Color.white : tabText;
            if (tab.icon != null) tab.icon.color = active ? Color.white : tabText;
            if (tab.countBackground != null) tab.countBackground.color = active ? new Color(1f, 1f, 1f, 0.25f) : countIdle;
        }
    }

    // ------------------------------------------------------------------ update

    private void Update()
    {
        if (Time.unscaledTime >= nextClock)
        {
            nextClock = Time.unscaledTime + 1f;
            DateTime now = DateTime.Now;
            if (dateText != null) dateText.text = now.ToString("ddd, dd MMM yyyy", Invariant);
            if (timeText != null) timeText.text = now.ToString("HH:mm:ss", Invariant);
        }

        float smoke = SmokeLevel();
        if (PlantDataClock.Tick != shownTick || !Mathf.Approximately(smoke, shownSmoke)) Refresh();
    }

    private void Refresh()
    {
        PlantStatusSimulation.Step();
        shownTick = PlantDataClock.Tick;
        shownSmoke = SmokeLevel();

        for (int i = 0; i < boilers.Length; i++) FillBoiler(boilers[i], i);
        for (int i = 0; i < turbines.Length; i++) FillTurbine(turbines[i], i);
        for (int i = 0; i < electricalPanels.Length; i++) FillElectrical(electricalPanels[i], i);
        for (int i = 0; i < condensers.Length; i++) FillCondenser(condensers[i], i);
        FillChimney(shownSmoke);
    }

    private static float SmokeLevel() =>
        SmokeColorController.Instance != null ? SmokeColorController.Instance.Level : SmokeColorController.DefaultLevel;

    // ------------------------------------------------------------------ cards

    private void FillBoiler(PlantStatusCard card, int i)
    {
        if (card == null) return;
        PlantStatusSimulation.BoilerReading r = PlantStatusSimulation.Boiler(i, shownSmoke);
        card.SetTitle($"Boiler {i + 1:00}");
        SetLoadStatus(card, r.load);
        card.SetValue(0, $"{N(r.furnace, "N0")} °C");
        card.SetValue(1, $"{N(r.waterLevel, "F1")} %");
        card.SetValue(2, $"{N(r.drumPressure, "F1")} bar");
        card.SetValue(3, $"{N(r.steamFlow, "N0")} t/h");
        card.SetValue(4, $"{N(r.load, "F0")} %");
        card.SetValue(5, $"{N(r.feedwater, "F0")} °C");
        card.SetValue(6, $"{N(r.flueGas, "F0")} °C");
        card.SetValue(7, $"{N(r.oxygen, "F1")} %");
        card.SetBar(r.load / 100f, $"{N(r.load, "F0")} %");
    }

    private void FillTurbine(PlantStatusCard card, int i)
    {
        if (card == null) return;
        PlantStatusSimulation.TurbineReading r = PlantStatusSimulation.Turbine(i);
        card.SetTitle($"Turbine {i + 1:00}");
        SetLoadStatus(card, r.load);
        card.SetValue(0, $"{N(r.rpm, "N0")} rpm");
        card.SetValue(1, $"{N(r.inletPressure, "F1")} bar");
        card.SetValue(2, $"{N(r.bearingTemp, "F0")} °C");
        card.SetValue(3, $"{N(r.vibration, "F1")} mm/s");
        card.SetValue(4, $"{N(r.outputMW, "F0")} MW");
        card.SetValue(5, $"{N(r.lubeOil, "F1")} bar");
        card.SetValue(6, $"{N(r.exhaustTemp, "F0")} °C");
        card.SetValue(7, $"{N(r.efficiency, "F1")} %");
        card.SetBar(r.load / 100f, $"{N(r.load, "F0")} %");
    }

    private void FillElectrical(PlantStatusCard card, int i)
    {
        if (card == null) return;
        PlantStatusSimulation.ElectricalReading r = PlantStatusSimulation.Electrical(i);
        card.SetTitle($"Electrical Panel {i + 1:00}");
        SetLoadStatus(card, r.load);
        card.SetValue(0, $"{N(r.generatorKV, "F0")} kV");
        card.SetValue(1, $"{N(r.gridKV, "F0")} kV");
        card.SetValue(2, $"{N(r.current, "N0")} A");
        card.SetValue(3, $"{N(r.frequency, "F1")} Hz");
        card.SetValue(4, $"{N(r.load, "F0")} %");
        card.SetValue(5, $"{N(r.oilTemp, "F0")} °C");
        card.SetValue(6, $"{N(r.windingTemp, "F0")} °C");
        card.SetValue(7, N(r.powerFactor, "F2"));
        card.SetValue(8, "Closed");
        card.SetValueColor(8, good);
        card.SetBar(r.load / 100f, $"{N(r.load, "F0")} %");
    }

    private void FillCondenser(PlantStatusCard card, int i)
    {
        if (card == null) return;
        PlantStatusSimulation.CondenserReading r = PlantStatusSimulation.Condenser(i);
        card.SetTitle($"Condenser {i + 1:00}");
        SetLoadStatus(card, r.load);
        card.SetValue(0, $"{N(r.vacuum, "F1")} kPa");
        card.SetValue(1, $"{N(r.hotwell, "F0")} %");
        card.SetValue(2, $"{N(r.cwInlet, "F1")} °C");
        card.SetValue(3, $"{N(r.cwOutlet, "F1")} °C");
        card.SetValue(4, $"{N(r.steamIn, "N0")} t/h");
        card.SetValue(5, $"{N(r.condensateTemp, "F1")} °C");
        card.SetValue(6, $"{N(r.cwFlow, "N0")} m³/h");
        card.SetValue(7, $"{N(r.cleanliness, "F1")} %");
        card.SetBar(r.load / 100f, $"{N(r.load, "F0")} %");
    }

    private void FillChimney(float smoke)
    {
        PlantStatusSimulation.ChimneyReading r = PlantStatusSimulation.Chimney(smoke);
        int stage = Mathf.Clamp(Mathf.RoundToInt(smoke * 5f), 0, 5);

        if (chimney != null)
        {
            chimney.SetTitle("Chimney (Stack)");
            Color c = stage <= 1 ? running : stage == 2 ? high : alarm;
            chimney.SetStatus(stage == 0 ? "No Smoke" : stage == 1 ? "Normal" : stage == 2 ? "Watch" : "Alarm", c);
            chimney.SetValue(0, N(smoke, "F2"));
            chimney.SetValue(1, $"{N(r.opacity, "F0")} %");
            chimney.SetValue(2, $"{N(r.stackTemp, "F0")} °C");
            chimney.SetValue(3, $"{N(r.velocity, "F1")} m/s");
            chimney.SetValue(4, $"{N(r.particulate, "F0")} mg/Nm³");
            chimney.SetValue(5, $"{N(r.sox, "F0")} mg/Nm³");
            chimney.SetValue(6, $"{N(r.nox, "F0")} mg/Nm³");
            chimney.SetValue(7, $"{N(r.co, "F0")} ppm");
            chimney.SetBar(smoke, N(smoke, "F2"));
        }

        for (int i = 0; i < caseFrames.Length; i++)
            if (caseFrames[i] != null) caseFrames[i].color = i == stage ? tabActive : caseFrameIdle;
        for (int i = 0; i < caseSwatches.Length; i++)
            if (caseSwatches[i] != null)
                caseSwatches[i].color = i == 0 ? new Color(1f, 1f, 1f, 0.12f) : SmokeColorController.ColorAt(i * 0.2f);

        if (smokeIndication != null)
            smokeIndication.text = SmokeColorController.Instance != null
                ? SmokeColorController.Instance.BuildStatus().indication
                : PlantStatusSimulation.Indication(stage);
    }

    private void SetLoadStatus(PlantStatusCard card, float load) =>
        card.SetStatus(load >= 90f ? "High Load" : "Running", load >= 90f ? high : running);

    private static string N(float value, string format) => value.ToString(format, Invariant);
}

/// <summary>
/// Mock live data for the Plant Status panel: each unit has a load that drifts a little every 5 s
/// (PlantDataClock); all values follow their unit's load with small measurement noise.
/// </summary>
public static class PlantStatusSimulation
{
    private static readonly float[] boilerLoad = { 78f, 82f };
    private static readonly float[] turbineLoad = { 78f, 76f, 80f, 72f };
    private static readonly float[] electricalLoad = { 72f, 68f, 60f };
    private static readonly float[] condenserLoad = { 75f, 70f };
    private static int tick = int.MinValue;

    public struct BoilerReading { public float load, furnace, waterLevel, drumPressure, steamFlow, feedwater, flueGas, oxygen; }
    public struct TurbineReading { public float load, rpm, inletPressure, bearingTemp, vibration, outputMW, lubeOil, exhaustTemp, efficiency; }
    public struct ElectricalReading { public float load, generatorKV, gridKV, current, frequency, oilTemp, windingTemp, powerFactor; }
    public struct CondenserReading { public float load, vacuum, hotwell, cwInlet, cwOutlet, steamIn, condensateTemp, cwFlow, cleanliness; }
    public struct ChimneyReading { public float opacity, stackTemp, velocity, particulate, sox, nox, co; }

    /// <summary>New loads once per 5 s tick (several callers per tick are fine).</summary>
    public static void Step()
    {
        int t = PlantDataClock.Tick;
        if (t == tick) return;
        tick = t;
        Drift(boilerLoad);
        Drift(turbineLoad);
        Drift(electricalLoad);
        Drift(condenserLoad);
    }

    private static void Drift(float[] loads)
    {
        for (int i = 0; i < loads.Length; i++) loads[i] = Mathf.Clamp(loads[i] + UnityEngine.Random.Range(-1.5f, 1.5f), 55f, 92f);
    }

    private static float Noise(float amount) => UnityEngine.Random.Range(-amount, amount);
    private static float Load(float[] loads, int i) => loads[Mathf.Clamp(i, 0, loads.Length - 1)];

    /// <param name="smoke">Plant smoke level 0..1: more smoke = less oxygen, hotter flue gas.</param>
    public static BoilerReading Boiler(int i, float smoke)
    {
        float l = Load(boilerLoad, i);
        return new BoilerReading
        {
            load = l,
            furnace = 700f + l * 4.5f + Noise(6f),
            waterLevel = 95f - l * 0.16f + Noise(0.4f),
            drumPressure = 120f + l * 0.54f + Noise(0.5f),
            steamFlow = l * 6.2f + Noise(3f),
            feedwater = 140f + l * 0.59f + Noise(0.8f),
            flueGas = 170f + l * 0.84f + smoke * 25f + Noise(1.5f),
            oxygen = Mathf.Max(1f, 4.4f - l * 0.015f - smoke * 1.2f + Noise(0.08f)),
        };
    }

    public static TurbineReading Turbine(int i)
    {
        float l = Load(turbineLoad, i);
        return new TurbineReading
        {
            load = l,
            rpm = 3000f + Mathf.Round(Noise(2.4f)),
            inletPressure = 12f + l * 0.06f + Noise(0.15f),
            bearingTemp = 40f + l * 0.36f + Noise(0.8f),
            vibration = 1f + l * 0.015f + Noise(0.08f),
            outputMW = l * 2.75f + Noise(2f),
            lubeOil = 3.6f + l * 0.008f + Noise(0.05f),
            exhaustTemp = 70f + l * 0.55f + Noise(1f),
            efficiency = 88f + l * 0.055f + Noise(0.15f),
        };
    }

    public static ElectricalReading Electrical(int i)
    {
        float l = Load(electricalLoad, i);
        return new ElectricalReading
        {
            load = l,
            generatorKV = 70f + Noise(0.4f),
            gridKV = 110f + Noise(0.4f),
            current = l * 11.8f + Noise(8f),
            frequency = 50f + Noise(0.04f),
            oilTemp = 20f + l * 0.36f + Noise(0.6f),
            windingTemp = 30f + l * 0.58f + Noise(0.8f),
            powerFactor = Mathf.Min(0.99f, 0.93f + l * 0.0004f + Noise(0.005f)),
        };
    }

    public static CondenserReading Condenser(int i)
    {
        float l = Load(condenserLoad, i);
        float cwIn = 28f + Noise(0.3f);
        return new CondenserReading
        {
            load = l,
            vacuum = -(93.5f - l * 0.03f) + Noise(0.2f),
            hotwell = 52f + Noise(1.5f),
            cwInlet = cwIn,
            cwOutlet = cwIn + l * 0.12f + Noise(0.2f),
            steamIn = l * 5.2f + Noise(3f),
            condensateTemp = 38f + l * 0.06f + Noise(0.3f),
            cwFlow = 32000f + l * 120f + Noise(150f),
            cleanliness = 88f - l * 0.03f + Noise(0.3f),
        };
    }

    // Emission per smoke case 0 / 0.2 / 0.4 / 0.6 / 0.8 / 1.0 (blended in between).
    private static readonly float[] Opacity = { 0f, 8f, 25f, 55f, 40f, 45f };
    private static readonly float[] StackTemp = { 130f, 140f, 150f, 168f, 175f, 182f };
    private static readonly float[] Particulate = { 5f, 18f, 45f, 95f, 40f, 60f };
    private static readonly float[] Sox = { 80f, 140f, 180f, 220f, 520f, 340f };
    private static readonly float[] Nox = { 120f, 160f, 210f, 260f, 480f, 300f };
    private static readonly float[] Co = { 10f, 25f, 60f, 180f, 70f, 120f };

    private static readonly string[] Indications =
    {
        "No smoke - emission stopped",
        "Normal operation / steam-heavy exhaust",
        "Combustion efficiency degrading / elevated particulate load",
        "Severe incomplete combustion / high soot condition",
        "Possible NOx/SOx-related abnormal-emission warning",
        "Possible oil/fuel contamination or abnormal combustion",
    };

    public static string Indication(int stage) => Indications[Mathf.Clamp(stage, 0, Indications.Length - 1)];

    public static ChimneyReading Chimney(float smoke)
    {
        smoke = Mathf.Clamp01(smoke);
        return new ChimneyReading
        {
            opacity = Blend(Opacity, smoke) + (smoke > 0.01f ? Noise(1f) : 0f),
            stackTemp = Blend(StackTemp, smoke) + Noise(1f),
            velocity = smoke > 0.001f ? 16f + smoke * 4f + Noise(0.3f) : 0f,
            particulate = Blend(Particulate, smoke) + Noise(1.5f),
            sox = Blend(Sox, smoke) + Noise(4f),
            nox = Blend(Nox, smoke) + Noise(4f),
            co = Blend(Co, smoke) + Noise(2f),
        };
    }

    private static float Blend(float[] stops, float smoke)
    {
        float x = smoke * (stops.Length - 1);
        int a = Mathf.Clamp(Mathf.FloorToInt(x), 0, stops.Length - 1);
        int b = Mathf.Min(a + 1, stops.Length - 1);
        return Mathf.Max(0f, Mathf.Lerp(stops[a], stops[b], x - a));
    }
}
