using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Boiler dashboard simulation (temperature, water, pressure) for the React UI.
/// The simulation is unchanged; instead of UI Toolkit labels it sends the values to React.
///
/// Opened when its GameObject is switched on (BoilerOperationInfo does that in the info sequence).
///   Sends:  handleBoilerDashboardOpened, handleBoilerDashboard (throttled), handleBoilerDashboardClosed
///   React:  SetBurnerPower_Extern, SetValveOpening_Extern, RestartBoiler_Extern,
///           CloseBoilerDashboard_Extern, GetBoilerDashboard_Extern  (all in CommunicationManager)
/// Access from code: BoilerDashboardController.Instance (only meaningful while the dashboard is open).
/// </summary>
public class BoilerDashboardController : SingletonMono<BoilerDashboardController>
{
    [Header("Boiler Settings")]
    [SerializeField] private float temperature = 25f;
    [SerializeField] private float waterLevel = 100f;
    [SerializeField] private float steamPressure = 0f;

    [SerializeField] private float maxTemperature = 150f;
    [SerializeField] private float maxPressure = 10f;
    [SerializeField] private float minimumWaterLevel = 15f;

    [Header("Simulation Speeds")]
    [SerializeField] private float heatingSpeed = 8f;
    [SerializeField] private float coolingSpeed = 2f;
    [SerializeField] private float waterConsumption = 0.5f;
    [SerializeField] private float pressureBuildSpeed = 0.08f;

    [Header("Initial Controls")]
    [Range(0f, 100f)]
    [SerializeField] private float initialBurnerPower = 45f;

    [Range(0f, 100f)]
    [SerializeField] private float initialValveOpening = 0f;

    [Header("Boiler Fluid Link")]
    [SerializeField] private BoilerFluidController fluidController;

    [Header("React")]
    [Tooltip("Seconds between \"boiler.dashboard\" updates while values change (0.2 = 5 per second).")]
    [SerializeField, Min(0.05f)] private float sendInterval = 0.2f;

    [Header("Events")]
    public UnityEvent onBoilerStart;
    public UnityEvent onBoilerStop;
    public UnityEvent onBoilerReset;

    public UnityEvent<float> onTemperatureChanged;
    public UnityEvent<float> onWaterLevelChanged;
    public UnityEvent<float> onPressureChanged;
    public UnityEvent<float> onBurnerPowerChanged;
    public UnityEvent<float> onValveOpeningChanged;

    [Tooltip("GameObject switched off when React closes the dashboard (usually this one).")]
    public GameObject uiObject;

    // Simulation state
    private float burnerPower;
    private float valveOpening;
    private float targetWaterLevel = 70f;

    private bool isRunning;
    private bool cycleCompleted;
    private string statusText = "STOPPED";
    private string footerMessage = "";

    private bool dirty;
    private float nextSendTime;

    private void OnEnable()
    {
        ResolveFluidController();

        ResetBoilerValues();
        StartBoiler();

        CommunicationManager.HandleBoilerDashboardOpened_Extern(BuildPayload());
        dirty = false;
        nextSendTime = Time.unscaledTime + sendInterval;
    }

    private void OnDisable()
    {
        CommunicationManager.HandleBoilerDashboardClosed_Extern();
    }

    private void Update()
    {
        // Smoothly adjust water level towards the target set by burner power
        if (Mathf.Abs(waterLevel - targetWaterLevel) > 0.05f)
        {
            waterLevel = Mathf.MoveTowards(waterLevel, targetWaterLevel, 30f * Time.deltaTime);
            dirty = true;
            onWaterLevelChanged?.Invoke(waterLevel);
            if (fluidController != null) fluidController.SetWaterLevel(waterLevel / 100f);
        }

        if (isRunning) SimulateBoiler(Time.deltaTime);

        // Throttled send: at most one message per sendInterval, only when something changed.
        if (dirty && Time.unscaledTime >= nextSendTime) SendDashboard();
    }

    // --------------------------------------------------
    // SIMULATION (unchanged)
    // --------------------------------------------------

    private void SimulateBoiler(float deltaTime)
    {
        if (cycleCompleted)
            return;

        float burnerMultiplier = burnerPower / 100f;

        float heating = burnerMultiplier * heatingSpeed;
        float cooling = coolingSpeed;

        temperature += (heating - cooling) * deltaTime;
        temperature = Mathf.Clamp(temperature, 0f, maxTemperature);

        float consumptionRate = burnerMultiplier * waterConsumption;
        waterLevel -= consumptionRate * deltaTime;
        waterLevel = Mathf.Clamp(waterLevel, 0f, 100f);

        float temperatureRatio = Mathf.InverseLerp(25f, maxTemperature, temperature);
        float pressureBuild = temperatureRatio * burnerMultiplier * pressureBuildSpeed;
        float pressureRelease = (valveOpening / 100f) * pressureBuildSpeed * 2f;

        steamPressure += (pressureBuild - pressureRelease) * deltaTime;
        steamPressure = Mathf.Clamp(steamPressure, 0f, maxPressure);

        dirty = true;

        if (fluidController != null)
            fluidController.SetWaterLevel(waterLevel / 100f);

        onTemperatureChanged?.Invoke(temperature);
        onWaterLevelChanged?.Invoke(waterLevel);
        onPressureChanged?.Invoke(steamPressure);

        if (temperature >= maxTemperature)
        {
            CompleteBoilerCycle("Maximum temperature reached.");
            return;
        }

        if (waterLevel <= minimumWaterLevel)
        {
            CompleteBoilerCycle("Water level is too low.");
        }
    }

    // --------------------------------------------------
    // START / STOP
    // --------------------------------------------------

    private void StartBoiler()
    {
        if (cycleCompleted)
            return;

        isRunning = true;
        SetStatus("RUNNING", "Boiler simulation running.");
        onBoilerStart?.Invoke();
    }

    private void StopBoiler()
    {
        if (!isRunning)
            return;

        isRunning = false;
        SetStatus("STOPPED", "Boiler simulation stopped.");
        onBoilerStop?.Invoke();
    }

    private void CompleteBoilerCycle(string message)
    {
        if (cycleCompleted)
            return;

        cycleCompleted = true;
        StopBoiler();
        SetStatus("COMPLETED", message);
        SendDashboard(); // important state change: send right away
    }

    // --------------------------------------------------
    // RESTART / CLOSE
    // --------------------------------------------------

    public void RestartBoiler()
    {
        ResetBoilerValues();
        onBoilerReset?.Invoke();
        StartBoiler();
        SendDashboard();
    }

    public void CloseDashboard()
    {
        // Same as the old close button.
        if (BoilerOperationInfo.instanced != null)
            BoilerOperationInfo.instanced.ResetBoilerInfo();

        if (uiObject != null)
            uiObject.SetActive(false);

        if (HUDController.Instance != null)
            HUDController.Instance.UpdateBoilerButtonStates();
    }

    private void ResetBoilerValues()
    {
        temperature = 25f;
        steamPressure = 0f;

        burnerPower = Mathf.Clamp(initialBurnerPower, 0f, 100f);
        valveOpening = Mathf.Clamp(initialValveOpening, 0f, 100f);

        targetWaterLevel = Mathf.Lerp(100f, 15f, burnerPower / 100f);
        waterLevel = targetWaterLevel;

        cycleCompleted = false;
        dirty = true;

        ResolveFluidController();

        if (fluidController != null)
        {
            fluidController.SetBurnerPower(burnerPower / 100f);
            fluidController.SetWaterLevelImmediate(waterLevel / 100f);
        }
    }

    // --------------------------------------------------
    // CONTROLS (were sliders; React calls them through CommunicationManager)
    // --------------------------------------------------

    public void SetBurnerPower(float value)
    {
        burnerPower = Mathf.Clamp(value, 0f, 100f);
        float normalizedPower = burnerPower / 100f;

        ResolveFluidController();
        if (fluidController != null) fluidController.SetBurnerPower(normalizedPower);

        // Target water level in burner decreases as burner power increases
        targetWaterLevel = Mathf.Lerp(100f, 15f, normalizedPower);

        dirty = true;
        onBurnerPowerChanged?.Invoke(burnerPower);
    }

    public void SetValveOpening(float value)
    {
        valveOpening = Mathf.Clamp(value, 0f, 100f);
        dirty = true;
        onValveOpeningChanged?.Invoke(valveOpening);
    }


    // --------------------------------------------------
    // REACT OUTPUT
    // --------------------------------------------------

    private void SetStatus(string status, string message)
    {
        statusText = status;
        footerMessage = message;
        dirty = true;
    }

    private BoilerDashboardPayload BuildPayload()
    {
        return new BoilerDashboardPayload
        {
            temperature = temperature,
            maxTemperature = maxTemperature,
            waterLevel = waterLevel,
            steamPressure = steamPressure,
            maxPressure = maxPressure,
            burnerPower = burnerPower,
            valveOpening = valveOpening,
            status = statusText,
            message = footerMessage,
            lowWater = waterLevel < 35f,
            completed = cycleCompleted
        };
    }

    /// <summary>Sends the current values to React (handleBoilerDashboard).</summary>
    public void SendDashboard()
    {
        dirty = false;
        nextSendTime = Time.unscaledTime + sendInterval;
        CommunicationManager.HandleBoilerDashboard_Extern(BuildPayload());
    }

    private void ResolveFluidController()
    {
        if (fluidController != null) return;

        fluidController = BoilerFluidController.instance != null
            ? BoilerFluidController.instance
            : FindAnyObjectByType<BoilerFluidController>(FindObjectsInactive.Include);
    }
}
