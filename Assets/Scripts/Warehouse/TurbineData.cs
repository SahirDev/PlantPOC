using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One turbine: mock live data + operation effects.
/// React (through CommunicationManager):
///   - handleTurbineData every 5 s (PlantDataClock) while the worker is at this turbine or it is operating.
///   - GetAllTurbineData_Extern / GetTurbineData_Extern(id) ask for the values at any time.
/// The old UI Toolkit monitor is gone; React draws it from this data.
/// </summary>
public class TurbineData : MonoBehaviour
{
    [SerializeField]
    private TurbineScriptableObject data;

    [Header("Mock Data Simulation")]
    [Space]
#pragma warning disable 0414 // kept for old scenes: readings now follow PlantDataClock (5 s, shared)
    [SerializeField]
    private float UpdateRate = 5;
#pragma warning restore 0414

    [Space]
    [SerializeField]
    private Vector2 TemperatureRange;

    [SerializeField]
    private Vector2 PressureRange;

    [SerializeField]
    private Vector2 RPMRange;

    [SerializeField]
    private Vector2 FlowRateRange;

    [SerializeField]
    private Vector2 VibrationRange;

    [Header("VFX")]
    [SerializeField]
    private ParticleSystem[] effects;

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
    private static readonly List<TurbineData> activeTurbines = new List<TurbineData>();

    private SpinObjects cachedSpinObject;
    private bool workerInRange;
    private bool operating;
    private int lastTick = int.MinValue;

    /// <summary>Goes up with every new reading - the monitors and the Plant UI redraw when it changes.</summary>
    public int Version { get; private set; }

    public bool IsOperating => operating;

    /// <summary>Steam admission / load 0..1 (Unity UI slider): values sit at this point of their ranges
    /// (0 = bottom, 1 = top) with small live variation; stopped turbines run down to the bottom.</summary>
    public float Load => load;
    private float load = 0.5f;

    public void SetLoad(float value)
    {
        load = Mathf.Clamp01(value > 1f ? value / 100f : value);
        SimulateData(); // the panel sees the change straight away
    }

    // Value at the current load in a range, with +-4 % of the range as live variation.
    private float AtLoad(Vector2 range)
    {
        float level = operating ? load : load * 0.25f;
        float spread = (range.y - range.x) * 0.04f;
        return Mathf.Clamp(Mathf.Lerp(range.x, range.y, level) + Random.Range(-spread, spread), Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    private void Awake()
    {
        cachedSpinObject = GetComponentInChildren<SpinObjects>();
    }

    private void OnEnable()
    {
        activeTurbines.Add(this);
        lastTick = int.MinValue;
    }

    private void OnDisable()
    {
        activeTurbines.Remove(this);
    }

    // A new reading every 5 s, at the same moment for all turbines, monitors and the Plant UI (PlantDataClock).
    private void Update()
    {
        int tick = PlantDataClock.Tick;
        if (tick == lastTick) return;
        lastTick = tick;
        SimulateData();
    }

    private void Start()
    {
        if (HUDController.Instance != null)
        {
            HUDController.Instance.HideEquipmentButtons();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        workerInRange = true;
        if (HUDController.Instance != null)
        {
            HUDController.Instance.SetInRangeOfEquipment(EquipmentType.Turbine, gameObject);
        }

        SendData(); // React gets values immediately, not after up to 5 seconds
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        workerInRange = false;
        if (HUDController.Instance != null)
        {
            HUDController.Instance.ResetInRangeOfEquipment(EquipmentType.Turbine, gameObject);
        }
    }

    public void SimulateData()
    {
        if (data == null) return;
        Version++;

        // More load (steam admission) -> higher temperature, pressure, speed, flow and vibration.
        float temp = AtLoad(TemperatureRange);
        data.Temperature = temp.ToString("F2", Invariant);
        data.TemperatureF = (temp * 1.8f + 32f).ToString("F2", Invariant);

        data.SteamPressure = AtLoad(PressureRange).ToString("F2", Invariant);
        data.RPM = AtLoad(RPMRange).ToString("F2", Invariant);
        data.SteamMassFlowRate = AtLoad(FlowRateRange).ToString("F2", Invariant);
        data.Vibration = AtLoad(VibrationRange).ToString("F2", Invariant);

        // Only stream while someone can see it; React can always ask with "turbine.get(All)".
        if (workerInRange || operating) SendData();
    }

    public void StartOperation()
    {
        operating = true;

        if (effects != null)
        {
            foreach (var effect in effects)
            {
                if (effect != null)
                {
                    effect.gameObject.SetActive(true);
                    effect.Play();
                }
            }
        }

        if (cachedSpinObject == null) cachedSpinObject = GetComponentInChildren<SpinObjects>();
        if (cachedSpinObject != null) cachedSpinObject.shouldSpin = true;

        SimulateData(); // running values at once (monitors + Plant UI)
        SendData();
    }

    public void StopOperation()
    {
        operating = false;

        if (effects != null)
        {
            foreach (var effect in effects)
            {
                if (effect != null)
                {
                    effect.Stop();
                    effect.gameObject.SetActive(false);
                }
            }
        }

        if (cachedSpinObject == null) cachedSpinObject = GetComponentInChildren<SpinObjects>();
        if (cachedSpinObject != null) cachedSpinObject.shouldSpin = false;

        SimulateData();
        SendData();
    }

    // ------------------------------------------------------------------ React

    public TurbineDataPayload ToPayload()
    {
        var payload = new TurbineDataPayload { operating = operating, name = gameObject.name, id = gameObject.name };
        if (data == null) return payload;

        payload.id = data.TurbineID;
        payload.name = data.TurbineDisplayName;
        payload.type = data.TurbineType;
        payload.steamPressure = ParseValue(data.SteamPressure);
        payload.temperature = ParseValue(data.Temperature);
        payload.temperatureF = ParseValue(data.TemperatureF);
        payload.vibration = ParseValue(data.Vibration);
        payload.rpm = ParseValue(data.RPM);
        payload.steamMassFlowRate = ParseValue(data.SteamMassFlowRate);
        return payload;
    }

    private void SendData()
    {
        CommunicationManager.HandleTurbineData_Extern(ToPayload());
    }

    private static float ParseValue(string value)
    {
        return float.TryParse(value, System.Globalization.NumberStyles.Float, Invariant, out float result) ? result : 0f;
    }

    /// <summary>Values of every turbine in the current scene.</summary>
    public static TurbineDataPayload[] GetAllPayloads()
    {
        var list = new TurbineDataPayload[activeTurbines.Count];
        for (int i = 0; i < activeTurbines.Count; i++) list[i] = activeTurbines[i].ToPayload();
        return list;
    }

    /// <summary>Values of one turbine by id (or GameObject name); null if not in this scene.</summary>
    public static TurbineDataPayload GetPayload(string id)
    {
        foreach (TurbineData turbine in activeTurbines)
        {
            TurbineDataPayload payload = turbine.ToPayload();
            if (payload.id == id || turbine.gameObject.name == id) return payload;
        }

        return null;
    }
}
