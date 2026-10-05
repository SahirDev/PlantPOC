using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// One turbine: mock live data + operation effects.
/// React (through CommunicationManager):
///   - handleTurbineData every UpdateRate seconds while the worker is at this turbine or it is operating.
///   - GetAllTurbineData_Extern / GetTurbineData_Extern(id) ask for the values at any time.
/// The old UI Toolkit monitor is gone; React draws it from this data.
/// </summary>
public class TurbineData : MonoBehaviour
{
    [SerializeField]
    private TurbineScriptableObject data;

    [Header("Mock Data Simulation")]
    [Space]
    [SerializeField]
    private float UpdateRate = 3;

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

    /// <summary>Steam pressure setpoint 0..1 for all turbines (-1 = free random values, the old behaviour).</summary>
    private static float steamPressureSetpoint = -1f;

    private SpinObjects cachedSpinObject;
    private bool workerInRange;
    private bool operating;

    private void Awake()
    {
        cachedSpinObject = GetComponentInChildren<SpinObjects>();
    }

    private void OnEnable()
    {
        activeTurbines.Add(this);
        InvokeRepeating(nameof(SimulateData), 1f, Mathf.Max(0.1f, UpdateRate));
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(SimulateData));
        activeTurbines.Remove(this);
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

        SendData(); // React gets values immediately, not after up to UpdateRate seconds
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

        float temp = Sample(TemperatureRange);
        data.Temperature = temp.ToString("F2", Invariant);
        data.TemperatureF = (temp * 1.8f + 32f).ToString("F2", Invariant);

        data.SteamPressure = Sample(PressureRange).ToString("F2", Invariant);
        data.RPM = Sample(RPMRange).ToString("F2", Invariant);
        data.SteamMassFlowRate = Sample(FlowRateRange).ToString("F2", Invariant);
        data.Vibration = Sample(VibrationRange).ToString("F2", Invariant);

        // Only stream while someone can see it; React can always ask with "turbine.get(All)".
        if (workerInRange || operating) SendData();
    }

    // Random value in the range, or - with a steam pressure setpoint - the setpoint's point of the range
    // with a little noise (pressure drives RPM, flow, temperature and vibration).
    private static float Sample(Vector2 range)
    {
        if (steamPressureSetpoint < 0f) return Random.Range(range.x, range.y);

        float noise = (range.y - range.x) * 0.02f;
        return Mathf.Lerp(range.x, range.y, steamPressureSetpoint) + Random.Range(-noise, noise);
    }

    /// <summary>Steam pressure slider 0..100 (React: SetSteamPressure_Extern). Applies to every turbine and
    /// updates their values right away.</summary>
    public static void SetSteamPressurePercent(float percent)
    {
        steamPressureSetpoint = Mathf.Clamp01(percent / 100f);
        foreach (TurbineData turbine in activeTurbines) turbine.SimulateData();
    }

    /// <summary>Current steam pressure setpoint 0..100, or -1 when not set.</summary>
    public static float SteamPressurePercent => steamPressureSetpoint < 0f ? -1f : steamPressureSetpoint * 100f;

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
