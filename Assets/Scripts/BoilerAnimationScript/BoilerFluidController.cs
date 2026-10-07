using System.Collections;
using UnityEngine;


// =============================================================
// FLOW AXIS
// =============================================================

public enum FlowAxis
{
    X = 0,
    Y = 1,
    Z = 2
}


// =============================================================
// FLOW DIRECTION
// =============================================================

public enum FlowDirection
{
    Positive = 1,
    Negative = -1
}


// =============================================================
// FILL PARTICLE TRIGGER
// =============================================================

[System.Serializable]
public class FillParticleTrigger
{
    [Tooltip("Particle effect to control.")]
    public ParticleSystem particle;

    [Range(0f, 100f)]
    [Tooltip("Particle starts when fill reaches this percentage.")]
    public float startPercent = 50f;

    [Range(0f, 100f)]
    [Tooltip("Particle stops when fill reaches this percentage.")]
    public float stopPercent = 100f;
}

// =============================================================
// RANDOM SPLASH EFFECT
// =============================================================

[System.Serializable]
public class RandomSplashEffect
{
    [Tooltip("Splash particle effect at its fixed scene position.")]
    public ParticleSystem particle;

    [Min(0f)]
    [Tooltip("Minimum time before this splash plays again.")]
    public float minInterval = 1f;

    [Min(0f)]
    [Tooltip("Maximum time before this splash plays again.")]
    public float maxInterval = 2f;
}
// =============================================================
// PIPE FLOW STAGE
// =============================================================

[System.Serializable]
public class PipeFlowStage
{
    [Header("Pipe")]

    [Tooltip("Metal pipe parent/renderer.")]
    public Renderer pipeParent;

    [Tooltip("Separate water mesh using WaterFluidShader.")]
    public Renderer waterObject;


    [Header("Flow Direction")]

    [Tooltip("Axis of the water object's LOCAL mesh along which filling occurs.")]
    public FlowAxis flowAxis = FlowAxis.X;

    [Tooltip("Direction of flow along the selected local axis.")]
    public FlowDirection flowDirection = FlowDirection.Positive;


    [Header("Optional Fill Particles")]

    [Tooltip("Optional particles that can be controlled by this pipe's fill percentage.")]
    public FillParticleTrigger[] fillParticles;


    // Runtime-only value.
    [System.NonSerialized]
    public Material originalMaterial;

    [System.NonSerialized]
    public float cachedBottom;

    [System.NonSerialized]
    public float cachedTop;

    [System.NonSerialized]
    public bool boundsCached;
}


// =============================================================
// BOILER FLUID CONTROLLER
// =============================================================

public class BoilerFluidController : MonoBehaviour
{
    // =========================================================
    // OUTER PART
    // =========================================================
    public static BoilerFluidController instance;

    [Header("Outer Part")]

    [SerializeField]
    private MeshRenderer outerPartRenderer;

    [SerializeField]
    private Material originalOuterMaterial;

    [SerializeField]
    private Material transparentOuterMaterial;


    // =========================================================
    // PIPE FLOW
    // =========================================================

    [Header("Pipe Flow Stages")]

    [SerializeField]
    [Tooltip("Pipe order is important. Element 0 flows first, then Element 1, then Element 2...")]
    private PipeFlowStage[] pipeStages;

    [SerializeField, Min(0.01f)]
    [Tooltip("Speed at which water travels through each pipe.")]
    private float pipeFillSpeed = 0.3f;


    // =========================================================
    // FINAL CYLINDER
    // =========================================================

    [Header("Final Cylinder")]

    [SerializeField]
    private Renderer finalCylinder;

    // =========================================================
    // OBJECT TO MAKE TRANSPARENT AFTER FINAL CYLINDER IS FULL
    // =========================================================

    [Header("Object After Final Cylinder Full")]

    [SerializeField]
    [Tooltip("Object whose material should become transparent after the final cylinder reaches 100%.")]
    private MeshRenderer objectToMakeTransparent;

    [SerializeField]
    [Tooltip("Original material of the target object.")]
    private Material objectOriginalMaterial;

    [SerializeField]
    [Tooltip("Transparent material to apply after the final cylinder reaches 100%.")]
    private Material objectTransparentMaterial;
    // =========================================================
    // FINAL CYLINDER PARTICLES
    // =========================================================

    [Header("Final Cylinder Fill Particle Effects")]

    [SerializeField]
    private FillParticleTrigger[] finalCylinderEffects;


    // =========================================================
    // FINAL CYLINDER WAVE
    // =========================================================

    [Header("Final Cylinder Wave")]

    [SerializeField, Min(0f)]
    [Tooltip("Delay after final cylinder reaches 100% before the wave starts.")]
    private float finalCylinderWaveStartDelay = 1f;

    [SerializeField, Min(0.01f)]
    [Tooltip("Time for the wave strength to smoothly reach its material strength.")]
    private float waveRampDuration = 1.5f;


    // =========================================================
    // FINAL CYLINDER SMOKE
    // =========================================================

    [Header("Final Cylinder Smoke")]

    [SerializeField]
    private ParticleSystem[] smokeEffects;

    [SerializeField, Min(0f)]
    [Tooltip("Delay after wave starts before smoke starts.")]
    private float smokeDelay = 2f;


    // =========================================================
    // FINAL CYLINDER SPLASH
    // =========================================================

    [Header("Final Cylinder Splash")]

    //[SerializeField]
    //private ParticleSystem[] splashEffects;

    [SerializeField]
    private RandomSplashEffect[] splashEffects;

    [SerializeField, Min(0f)]
    [Tooltip("Delay after smoke starts before splash starts.")]
    private float splashDelay = 2f;


    // =========================================================
    // FINAL CYLINDER FILL
    // =========================================================

    [Header("Final Cylinder Fill")]

    [SerializeField, Min(0.01f)]
    private float finalCylinderFillSpeed = 0.3f;


    // =========================================================
    // FIRE
    // =========================================================

    [Header("Fire Effect")]

    [SerializeField]
    private ParticleSystem fireEffect;

    [SerializeField, Min(0f)]
    private float fireDelayAfterFull = 3f;


    // =========================================================
    // BURNER POWER (0-1) & SIMULATION SPEED CONTROLS
    // =========================================================

    [Header("Burner Power & Speed Controls (0 to 1)")]

    [Range(0f, 1f)]
    [SerializeField]
    [Tooltip("Normalized Burner Power (0 to 1). Increases particle speeds and decreases burner water level.")]
    private float burnerPower = 0.45f;

    [SerializeField]
    [Tooltip("Particle simulation speed when burner power is at 0.")]
    private float minParticleSpeed = 0.3f;

    [SerializeField]
    [Tooltip("Particle simulation speed when burner power is at 1.")]
    private float maxParticleSpeed = 2.5f;

    [Header("Burner Water Level Link")]

    [SerializeField]
    [Tooltip("If true, changing burner power automatically decreases/increases the water level.")]
    private bool autoLinkWaterLevel = true;

    [Range(0f, 1f)]
    [SerializeField]
    [Tooltip("Water level when burner power is 1.0 (maximum).")]
    private float minWaterLevelOnMaxBurner = 0.15f;

    [Range(0f, 1f)]
    [SerializeField]
    [Tooltip("Water level when burner power is 0.0 (minimum).")]
    private float maxWaterLevelOnMinBurner = 1.0f;

    [SerializeField, Min(0.01f)]
    [Tooltip("Fastest the water level may move (fill per second, 1 = full tank).")]
    private float waterChangeSpeed = 0.35f;

    [SerializeField, Min(0.05f)]
    [Tooltip("About how many seconds the water takes to settle at a new level: it starts slowly, glides and " +
             "eases in (natural), instead of moving at one constant speed.")]
    private float waterSettleTime = 3f;

    [Header("Low Water Warning (< 35%)")]
    [SerializeField]
    private float lowWaterWarningThreshold = 0.35f;

    [SerializeField]
    private BoilerWarningIndicator warningIndicator;

    [SerializeField]
    [Tooltip("Optional WaterSurfaceController to synchronize water level.")]
    private WaterSurfaceController waterSurfaceController;

    [SerializeField]
    [Tooltip("Optional additional particles root (e.g. ParticleEffects parent).")]
    private GameObject additionalParticlesRoot;

    private readonly System.Collections.Generic.List<ParticleSystem> cachedAllParticles = new System.Collections.Generic.List<ParticleSystem>();
    private readonly System.Collections.Generic.Dictionary<MeshRenderer, Material> pipeOriginalMaterials = new System.Collections.Generic.Dictionary<MeshRenderer, Material>();
    private float lastBurnerPower = -1f;

    private float currentWaterLevel = 0.70f;
    private float targetWaterLevel = 0.70f;
    private float waterLevelVelocity;
    private bool isInfoActive = false;
    private bool lastWarningState = false;

    public bool IsInfoActive => isInfoActive;
    public bool IsOperating => started;
    public bool IsActiveSession => started || isInfoActive;
    public float CurrentWaterLevel => currentWaterLevel;
    public float TargetWaterLevel => targetWaterLevel;


    // =========================================================
    // SHADER PROPERTY IDS
    // =========================================================

    private static readonly int FillPropertyId = Shader.PropertyToID("_FillLevel");
    private static readonly int BottomPropertyId = Shader.PropertyToID("_BottomY");
    private static readonly int TopPropertyId = Shader.PropertyToID("_TopY");
    private static readonly int FlowAxisPropertyId = Shader.PropertyToID("_FlowAxis");
    private static readonly int FlowDirectionPropertyId = Shader.PropertyToID("_FlowDirection");
    private static readonly int WaveTimePropertyId = Shader.PropertyToID("_WaveTime");
    private static readonly int WaveStrengthPropertyId = Shader.PropertyToID("_WaveStrength");

    private float finalCylinderBottom;
    private float finalCylinderTop;
    private bool finalCylinderBoundsCached = false;


    // =========================================================
    // PROPERTY BLOCKS
    // =========================================================

    private MaterialPropertyBlock[] pipeBlocks;

    private MaterialPropertyBlock finalCylinderBlock;


    // =========================================================
    // PIPE STATE
    // =========================================================

    private int currentPipeIndex = -1;

    private float currentPipeFill = 0f;


    // =========================================================
    // FINAL CYLINDER STATE
    // =========================================================

    private float finalCylinderFill = 0f;

    private float finalCylinderMaxWaveStrength = 0f;

    private float finalCylinderWaveTime = 0f;

    private float finalCylinderWaveRamp = 0f;


    // =========================================================
    // WAVE STATE
    // =========================================================

    private bool waveWaiting = false;

    private bool waveStarted = false;

    private float waveDelayTimer = 0f;


    // =========================================================
    // EFFECT STATE
    // =========================================================

    private bool postFullSequenceStarted = false;


    // =========================================================
    // FIRE STATE
    // =========================================================

    private float fireTimer = 0f;

    private bool fireStarted = false;

    [Header("Pipe Material Change After Fire")]

    [SerializeField]
    [Tooltip("Pipe renderers whose materials should change after fire starts.")]
    public MeshRenderer[] pipesToChangeAfterFire;

    [SerializeField]
    [Tooltip("Material to apply to all selected pipes.")]
    public Material pipeMaterialAfterFire;

    [SerializeField, Min(0f)]
    [Tooltip("Delay after fire starts before changing pipe materials.")]
    private float pipeMaterialChangeDelayAfterFire = 2f;

    // =========================================================
    // GENERAL STATE
    // =========================================================

    private bool started = false;

    private bool pipesFinished = false;

    private bool finalCylinderFinished = false;


    // =========================================================
    // AWAKE
    // =========================================================

    private void Awake()
    {
        instance = this;
        // =====================================================
        // SAVE TARGET OBJECT ORIGINAL MATERIAL
        // =====================================================

        if (objectToMakeTransparent != null)
        {
            if (objectOriginalMaterial == null)
            {
                objectOriginalMaterial =
                    objectToMakeTransparent.sharedMaterial;
            }
        }
        // =====================================================
        // CREATE PROPERTY BLOCKS
        // =====================================================

        if (pipeStages != null)
        {
            pipeBlocks =
                new MaterialPropertyBlock[
                    pipeStages.Length
                ];
        }
        else
        {
            pipeBlocks =
                new MaterialPropertyBlock[0];
        }


        for (int i = 0; i < pipeBlocks.Length; i++)
        {
            pipeBlocks[i] =
                new MaterialPropertyBlock();
        }


        finalCylinderBlock =
            new MaterialPropertyBlock();


        // =====================================================
        // RESET STATE
        // =====================================================

        currentPipeIndex = -1;

        currentPipeFill = 0f;

        finalCylinderFill = 0f;

        started = false;

        pipesFinished = false;

        finalCylinderFinished = false;

        waveWaiting = false;

        waveStarted = false;

        waveDelayTimer = 0f;

        postFullSequenceStarted = false;

        finalCylinderWaveTime = 0f;

        finalCylinderWaveRamp = 0f;

        fireTimer = 0f;

        fireStarted = false;


        // =====================================================
        // OUTER PART
        // =====================================================

        if (outerPartRenderer != null)
        {
            if (originalOuterMaterial != null)
            {
                outerPartRenderer.sharedMaterial =
                    originalOuterMaterial;
            }
        }


        // =====================================================
        // GET EACH PIPE'S ORIGINAL MATERIAL
        // =====================================================

        if (pipeStages != null)
        {
            for (int i = 0; i < pipeStages.Length; i++)
            {
                PipeFlowStage stage =
                    pipeStages[i];

                if (stage == null)
                {
                    continue;
                }


                if (stage.pipeParent != null)
                {
                    stage.originalMaterial =
                        stage.pipeParent.sharedMaterial;
                }
            }
        }


        // =====================================================
        // INITIALIZE ALL PIPES
        // =====================================================

        if (pipeStages != null)
        {
            for (int i = 0; i < pipeStages.Length; i++)
            {
                InitializePipe(i);
            }
        }


        // =====================================================
        // INITIALIZE FINAL CYLINDER
        // =====================================================

        InitializeFinalCylinder();


        // =====================================================
        // STOP FINAL PARTICLES
        // =====================================================

        StopParticleEffects(
            finalCylinderEffects
        );

        StopParticleArray(
            smokeEffects
        );

        /*  StopParticleArray(
              splashEffects
          );*/

        StopRandomSplashArray(
        splashEffects
        );

        StopParticle(
            fireEffect
        );

        // =====================================================
        // BURNER & WATER INITIALIZATION
        // =====================================================

        if (waterSurfaceController == null)
        {
            waterSurfaceController = FindAnyObjectByType<WaterSurfaceController>(FindObjectsInactive.Include);
        }

        if (additionalParticlesRoot == null && transform.parent != null)
        {
            // Anywhere under the boiler (it sits under Boiler Chamber, not directly under Boiler Parent).
            // Not found = it stays switched off and the steam / smoke / splash never show.
            var pe = transform.parent.Find("ParticleEffects");
            if (pe == null)
            {
                foreach (Transform t in transform.parent.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "ParticleEffects") { pe = t; break; }
                }
            }

            if (pe != null)
            {
                additionalParticlesRoot = pe.gameObject;
            }
        }

        if (warningIndicator == null)
        {
            warningIndicator = FindAnyObjectByType<BoilerWarningIndicator>(FindObjectsInactive.Include);
        }

        float initialTarget = Mathf.Lerp(maxWaterLevelOnMinBurner, minWaterLevelOnMaxBurner, burnerPower);
        targetWaterLevel = initialTarget;
        currentWaterLevel = initialTarget;
        finalCylinderFill = initialTarget;

        RefreshParticleCache();
        UpdateBurnerPowerEffects();

        // Ensure water and particles are disabled by default until operation or info UI is active
        UpdateWaterAndParticleVisibility();
    }


    // =========================================================
    // BURNER POWER & SPEED PUBLIC API
    // =========================================================

    /// <summary>All warning lamps in the scene blink together (not only the assigned one).</summary>
    private void SetWarningLamps(bool active)
    {
        BoilerWarningIndicator.SetAllWarnings(active);

        // A lamp on an object that was inactive at load has not registered yet: switch it directly.
        if (warningIndicator != null && warningIndicator.IsWarningActive != active)
            warningIndicator.SetWarning(active, BoilerWarningIndicator.Count == 0);
    }

    public float BurnerPower
    {
        get => burnerPower;
        set => SetBurnerPower(value);
    }

    public void SetBurnerPower(float power01)
    {
        burnerPower = Mathf.Clamp01(power01);
        lastBurnerPower = burnerPower;
        UpdateBurnerPowerEffects();
    }

    public void SetWaterLevel(float fill01)
    {
        targetWaterLevel = Mathf.Clamp01(fill01);
    }

    public void SetWaterLevelImmediate(float fill01)
    {
        targetWaterLevel = Mathf.Clamp01(fill01);
        currentWaterLevel = targetWaterLevel;
        waterLevelVelocity = 0f;
        finalCylinderFill = currentWaterLevel;

        if (IsActiveSession)
        {
            if (finalCylinder != null && finalCylinder.enabled)
            {
                SetFinalCylinderFill(currentWaterLevel);
            }
            if (waterSurfaceController != null && waterSurfaceController.gameObject.activeInHierarchy)
            {
                waterSurfaceController.SetFillLevel(currentWaterLevel);
            }
        }
    }

    public void SetInfoActive(bool active)
    {
        isInfoActive = active;
        UpdateWaterAndParticleVisibility();
    }

    public void UpdateWaterAndParticleVisibility()
    {
        bool active = IsActiveSession;

        if (finalCylinder != null)
        {
            finalCylinder.enabled = active;
            if (active)
            {
                SetFinalCylinderFill(currentWaterLevel);
            }
        }

        if (pipeStages != null)
        {
            for (int i = 0; i < pipeStages.Length; i++)
            {
                if (pipeStages[i] != null && pipeStages[i].waterObject != null)
                {
                    if (!active)
                    {
                        pipeStages[i].waterObject.enabled = false;
                    }
                    else if (isInfoActive)
                    {
                        pipeStages[i].waterObject.enabled = true;
                    }
                }
            }
        }

        if (waterSurfaceController != null)
        {
            waterSurfaceController.gameObject.SetActive(active);
            if (active)
            {
                waterSurfaceController.SetFillLevel(currentWaterLevel);
            }
        }

        if (additionalParticlesRoot != null)
        {
            additionalParticlesRoot.SetActive(active);
        }

        if (!active)
        {
            lastWarningState = false;
            if (fireEffect != null)
            {
                fireEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            SetWarningLamps(false);
        }
    }

    public void RefreshParticleCache()
    {
        cachedAllParticles.Clear();

        if (additionalParticlesRoot != null)
        {
            var pss = additionalParticlesRoot.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < pss.Length; i++)
            {
                if (pss[i] != null && !cachedAllParticles.Contains(pss[i]))
                {
                    cachedAllParticles.Add(pss[i]);
                }
            }
        }

        if (transform.parent != null)
        {
            var pss = transform.parent.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < pss.Length; i++)
            {
                if (pss[i] != null && !cachedAllParticles.Contains(pss[i]))
                {
                    cachedAllParticles.Add(pss[i]);
                }
            }
        }
        else
        {
            var pss = transform.root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < pss.Length; i++)
            {
                if (pss[i] != null && !cachedAllParticles.Contains(pss[i]))
                {
                    cachedAllParticles.Add(pss[i]);
                }
            }
        }
    }

    private void UpdateBurnerPowerEffects()
    {
        float targetSpeed = Mathf.Lerp(minParticleSpeed, maxParticleSpeed, burnerPower);

        // 1. Scale speed for all burner & boiler particle systems
        ApplyParticleSpeed(fireEffect, targetSpeed);

        if (smokeEffects != null)
        {
            for (int i = 0; i < smokeEffects.Length; i++)
                ApplyParticleSpeed(smokeEffects[i], targetSpeed);
        }

        if (splashEffects != null)
        {
            for (int i = 0; i < splashEffects.Length; i++)
            {
                if (splashEffects[i] != null)
                    ApplyParticleSpeed(splashEffects[i].particle, targetSpeed);
            }
        }

        if (finalCylinderEffects != null)
        {
            for (int i = 0; i < finalCylinderEffects.Length; i++)
            {
                if (finalCylinderEffects[i] != null)
                    ApplyParticleSpeed(finalCylinderEffects[i].particle, targetSpeed);
            }
        }

        for (int i = 0; i < cachedAllParticles.Count; i++)
        {
            ApplyParticleSpeed(cachedAllParticles[i], targetSpeed);
        }

        // 2. Target water level decreases as burner power increases
        if (autoLinkWaterLevel)
        {
            float targetWater = Mathf.Lerp(maxWaterLevelOnMinBurner, minWaterLevelOnMaxBurner, burnerPower);
            SetWaterLevel(targetWater);
        }
    }

    private static void ApplyParticleSpeed(ParticleSystem ps, float speed)
    {
        if (ps != null)
        {
            var main = ps.main;
            main.simulationSpeed = speed;
        }
    }

    private void OnValidate()
    {
        burnerPower = Mathf.Clamp01(burnerPower);
        if (Application.isPlaying)
        {
            UpdateBurnerPowerEffects();
        }
    }


    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        // Check for runtime changes to burner power slider
        if (!Mathf.Approximately(burnerPower, lastBurnerPower))
        {
            lastBurnerPower = burnerPower;
            UpdateBurnerPowerEffects();
        }

        // Smoothly and continuously adjust currentWaterLevel towards targetWaterLevel
        if (Mathf.Abs(currentWaterLevel - targetWaterLevel) > 0.0005f || Mathf.Abs(waterLevelVelocity) > 0.0005f)
        {
            // Eased (SmoothDamp): slow start, glide, slow settle - capped at waterChangeSpeed.
            currentWaterLevel = Mathf.SmoothDamp(
                currentWaterLevel,
                targetWaterLevel,
                ref waterLevelVelocity,
                waterSettleTime,
                waterChangeSpeed,
                Time.deltaTime
            );
            if (Mathf.Abs(currentWaterLevel - targetWaterLevel) <= 0.0005f && Mathf.Abs(waterLevelVelocity) <= 0.0005f)
            {
                currentWaterLevel = targetWaterLevel;
                waterLevelVelocity = 0f;
            }
            finalCylinderFill = currentWaterLevel;

            if (IsActiveSession)
            {
                if (finalCylinder != null && finalCylinder.enabled)
                {
                    SetFinalCylinderFill(currentWaterLevel);
                }

                if (waterSurfaceController != null && waterSurfaceController.gameObject.activeInHierarchy)
                {
                    waterSurfaceController.SetFillLevel(currentWaterLevel);
                }
            }
        }

        // Low water level warning (< 35%)
        bool shouldWarn = IsActiveSession && (currentWaterLevel < lowWaterWarningThreshold);
        if (shouldWarn != lastWarningState)
        {
            lastWarningState = shouldWarn;
            SetWarningLamps(shouldWarn);
        }

        // (Space is the worker jump key; the process is started from React / the dev panel.)
        if (!started)
        {
            return;
        }


        // =====================================================
        // PIPE FLOW
        // =====================================================

        if (!pipesFinished)
        {
            UpdatePipeFlow();
        }
        else
        {
            // =================================================
            // FINAL CYLINDER
            // =================================================

            if (!finalCylinderFinished)
            {
                UpdateFinalCylinderFill();
            }
            else
            {
                UpdateFinalCylinderWaveSequence();

                UpdateFireDelay();
            }
        }
    }


    // =========================================================
    // START PROCESS
    // =========================================================

    public void StartProcess()
    {
        started = true;

        // =====================================================
        // RESET TARGET OBJECT TO ORIGINAL MATERIAL
        // =====================================================

        if (objectToMakeTransparent != null &&
            objectOriginalMaterial != null)
        {
            objectToMakeTransparent.sharedMaterial =
                objectOriginalMaterial;
        }
        // =====================================================
        // OUTER PART → TRANSPARENT
        // =====================================================

        if (outerPartRenderer != null &&
            transparentOuterMaterial != null)
        {
            outerPartRenderer.sharedMaterial =
                transparentOuterMaterial;
        }


        // =====================================================
        // RESET PIPES
        // =====================================================

        currentPipeIndex = -1;

        currentPipeFill = 0f;

        pipesFinished = false;


        if (pipeStages != null)
        {
            for (int i = 0; i < pipeStages.Length; i++)
            {
                InitializePipe(i);
            }
        }

        if (finalCylinder != null)
        {
            finalCylinder.enabled = false;
        }

        finalCylinderFill = 0f;
        finalCylinderFinished = false;

        SetFinalCylinderFill(0f);
        SetFinalCylinderWave(0f, 0f);

        // =====================================================
        // RESET FINAL CYLINDER
        // =====================================================

        finalCylinderFill = 0f;

        finalCylinderFinished = false;

        finalCylinderWaveTime = 0f;

        finalCylinderWaveRamp = 0f;

        waveWaiting = false;

        waveStarted = false;

        waveDelayTimer = 0f;

        postFullSequenceStarted = false;


        SetFinalCylinderFill(0f);

        SetFinalCylinderWave(
            0f,
            0f
        );


        StopParticleEffects(
            finalCylinderEffects
        );

        StopParticleArray(
            smokeEffects
        );

        /* StopParticleArray(
             splashEffects
         );*/
        StopRandomSplashArray(
        splashEffects
         );


        // =====================================================
        // RESET FIRE
        // =====================================================

        fireTimer = 0f;

        fireStarted = false;

        StopParticle(
            fireEffect
        );


        // =====================================================
        // START FIRST PIPE
        // =====================================================

        if (pipeStages != null &&
            pipeStages.Length > 0)
        {
            StartPipe(0);
        }
        else
        {
            // No pipes configured.
            // Start final cylinder directly.
            pipesFinished = true;
            StartFinalCylinder();
        }
    }

    // =========================================================
    // STOP RANDOM SPLASH ARRAY
    // =========================================================

    private void StopRandomSplashArray(
        RandomSplashEffect[] effects
    )
    {
        if (effects == null)
        {
            return;
        }

        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i] == null ||
                effects[i].particle == null)
            {
                continue;
            }

            effects[i].particle.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }
    }

    // =========================================================
    // INITIALIZE ONE PIPE
    // =========================================================

    private void InitializePipe(int index)
    {
        if (pipeStages == null ||
            index < 0 ||
            index >= pipeStages.Length)
        {
            return;
        }


        PipeFlowStage stage =
            pipeStages[index];


        if (stage == null)
        {
            return;
        }

        // Cache mesh bounds once
        if (!stage.boundsCached && stage.waterObject != null)
        {
            MeshFilter mf = stage.waterObject.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds bounds = mf.sharedMesh.bounds;
                float bottom, top;
                switch (stage.flowAxis)
                {
                    case FlowAxis.X:
                        bottom = bounds.min.x;
                        top = bounds.max.x;
                        break;
                    case FlowAxis.Y:
                        bottom = bounds.min.y;
                        top = bounds.max.y;
                        break;
                    case FlowAxis.Z:
                        bottom = bounds.min.z;
                        top = bounds.max.z;
                        break;
                    default:
                        bottom = bounds.min.y;
                        top = bounds.max.y;
                        break;
                }
                if (Mathf.Approximately(bottom, top))
                {
                    top = bottom + 0.01f;
                }
                stage.cachedBottom = bottom;
                stage.cachedTop = top;
                stage.boundsCached = true;
            }
        }

        MaterialPropertyBlock block =
            pipeBlocks[index];


        // =====================================================
        // PIPE PARENT → ORIGINAL
        // =====================================================

        if (stage.pipeParent != null)
        {
            if (stage.originalMaterial != null)
            {
                stage.pipeParent.sharedMaterial =
                    stage.originalMaterial;
            }
        }


        // =====================================================
        // WATER = 0%
        // =====================================================

        if (stage.waterObject != null)
        {
            stage.waterObject.enabled = false;


            SetWaterFlowProperties(
                stage,
                block,
                0f
            );
        }


        // =====================================================
        // STOP PIPE PARTICLES
        // =====================================================

        StopParticleEffects(
            stage.fillParticles
        );
    }


    // =========================================================
    // START PIPE
    // =========================================================

    private void StartPipe(int index)
    {
        if (pipeStages == null)
        {
            return;
        }


        if (index < 0 ||
            index >= pipeStages.Length)
        {
            FinishPipes();
            return;
        }


        PipeFlowStage stage =
            pipeStages[index];


        if (stage == null ||
            stage.waterObject == null)
        {
            // Skip invalid pipe stage safely.
            StartPipe(index + 1);
            return;
        }


        currentPipeIndex = index;

        currentPipeFill = 0f;


        // =====================================================
        // PARENT → TRANSPARENT
        // =====================================================

        if (stage.pipeParent != null &&
            transparentOuterMaterial != null)
        {
            stage.pipeParent.sharedMaterial =
                transparentOuterMaterial;
        }


        // =====================================================
        // SET WATER FLOW SETTINGS
        // =====================================================

        SetWaterFlowProperties(
            stage,
            pipeBlocks[index],
            0f
        );


        // =====================================================
        // START WATER
        // =====================================================

        stage.waterObject.enabled = true;


        // =====================================================
        // STOP PARTICLES
        // =====================================================

        StopParticleEffects(
            stage.fillParticles
        );
    }


    // =========================================================
    // UPDATE PIPE FLOW
    // =========================================================

    private void UpdatePipeFlow()
    {
        // Final cylinder must remain OFF while pipes are flowing.
        if (finalCylinder != null)
        {
            finalCylinder.enabled = false;
        }

        if (currentPipeIndex < 0)
        {
            StartPipe(0);
            return;
        }


        if (currentPipeIndex >= pipeStages.Length)
        {
            FinishPipes();
            return;
        }


        PipeFlowStage stage =
            pipeStages[currentPipeIndex];


        if (stage == null ||
            stage.waterObject == null)
        {
            StartPipe(currentPipeIndex + 1);
            return;
        }


        // =====================================================
        // INCREASE WATER FILL
        // =====================================================

        currentPipeFill =
            Mathf.MoveTowards(
                currentPipeFill,
                1f,
                pipeFillSpeed *
                Time.deltaTime
            );


        // =====================================================
        // UPDATE WATER
        // =====================================================

        SetWaterFlowProperties(
            stage,
            pipeBlocks[currentPipeIndex],
            currentPipeFill
        );


        // =====================================================
        // OPTIONAL PIPE PARTICLES
        // =====================================================

        UpdateParticleEffects(
            stage.fillParticles,
            currentPipeFill * 100f
        );


        // =====================================================
        // PIPE COMPLETE
        // =====================================================

        if (currentPipeFill >= 1f)
        {
            currentPipeFill = 1f;


            SetWaterFlowProperties(
                stage,
                pipeBlocks[currentPipeIndex],
                1f
            );


            // -------------------------------------------------
            // STOP OPTIONAL FILL PARTICLES
            // -------------------------------------------------

            StopParticleEffects(
                stage.fillParticles
            );


            // -------------------------------------------------
            // PIPE PARENT → ORIGINAL
            // -------------------------------------------------

           /* if (stage.pipeParent != null &&
                stage.originalMaterial != null)
            {
                stage.pipeParent.sharedMaterial =
                    stage.originalMaterial;
            }*/


            // -------------------------------------------------
            // MOVE TO NEXT PIPE
            // -------------------------------------------------

            int nextPipe =
                currentPipeIndex + 1;


            if (nextPipe < pipeStages.Length)
            {
                StartPipe(nextPipe);
            }
            else
            {
                FinishPipes();
            }
        }
    }


    // =========================================================
    // FINISH PIPE SYSTEM
    // =========================================================

    private void FinishPipes()
    {
        pipesFinished = true;


        // =====================================================
        // START FINAL CYLINDER
        // =====================================================

        StartFinalCylinder();
    }


    // =========================================================
    // START FINAL CYLINDER
    // =========================================================

    private void StartFinalCylinder()
    {
        if (finalCylinder == null)
        {
            finalCylinderFinished = true;
            return;
        }


        finalCylinder.enabled = true;


        finalCylinderFill = 0f;

        finalCylinderFinished = false;


        SetFinalCylinderFill(0f);


        SetFinalCylinderWave(
            0f,
            0f
        );


        UpdateParticleEffects(
            finalCylinderEffects,
            0f
        );
    }


    // =========================================================
    // FINAL CYLINDER FILL
    // =========================================================

    private void UpdateFinalCylinderFill()
    {
        finalCylinderFill =
            Mathf.MoveTowards(
                finalCylinderFill,
                1f,
                finalCylinderFillSpeed *
                Time.deltaTime
            );


        // =====================================================
        // FILL
        // =====================================================

        SetFinalCylinderFill(
            finalCylinderFill
        );


        // =====================================================
        // NO WAVES WHILE FILLING
        // =====================================================

        SetFinalCylinderWave(
            0f,
            0f
        );


        // =====================================================
        // PERCENTAGE PARTICLES
        // =====================================================

        UpdateParticleEffects(
            finalCylinderEffects,
            finalCylinderFill * 100f
        );


        // =====================================================
        // 100%
        // =====================================================

        if (finalCylinderFill >= 1f)
        {
            finalCylinderFill = 1f;


            SetFinalCylinderFill(1f);

            MakeObjectTransparent();

            StopParticleEffects(
                finalCylinderEffects
            );


            finalCylinderFinished = true;


            // =================================================
            // START WAVE DELAY
            // =================================================

            waveWaiting = true;

            waveStarted = false;

            waveDelayTimer = 0f;


            // =================================================
            // RESET POST-FULL SEQUENCE
            // =================================================

            postFullSequenceStarted = false;


            // =================================================
            // FIRE
            // =================================================

            fireTimer = 0f;

            fireStarted = false;
        }
    }


    // =========================================================
    // FINAL CYLINDER WAVE SEQUENCE
    // =========================================================

    private void UpdateFinalCylinderWaveSequence()
    {
        // =====================================================
        // WAIT AFTER 100%
        // =====================================================

        if (waveWaiting)
        {
            waveDelayTimer +=
                Time.deltaTime;


            // Water stays still.
            SetFinalCylinderWave(
                0f,
                0f
            );


            if (waveDelayTimer >=
                finalCylinderWaveStartDelay)
            {
                waveWaiting = false;


                StartFinalCylinderWaves();


                if (!postFullSequenceStarted)
                {
                    postFullSequenceStarted = true;


                    StartCoroutine(
                        FinalCylinderSmokeSplashSequence()
                    );
                }
            }


            return;
        }


        // =====================================================
        // WAVES
        // =====================================================

        if (waveStarted)
        {
            UpdateFinalCylinderWaves();
        }
    }


    // =========================================================
    // START FINAL CYLINDER WAVES
    // =========================================================

    private void StartFinalCylinderWaves()
    {
        if (waveStarted)
        {
            return;
        }


        waveStarted = true;


        finalCylinderWaveTime = 0f;

        finalCylinderWaveRamp = 0f;


        SetFinalCylinderWave(
            0f,
            0f
        );
    }


    // =========================================================
    // UPDATE FINAL CYLINDER WAVES
    // =========================================================

    private void UpdateFinalCylinderWaves()
    {
        if (!waveStarted ||
            finalCylinder == null)
        {
            return;
        }


        // =====================================================
        // WAVE TIME
        // =====================================================

        finalCylinderWaveTime +=
            Time.deltaTime;


        // =====================================================
        // SMOOTH STRENGTH
        // =====================================================

        float safeDuration =
            Mathf.Max(
                0.01f,
                waveRampDuration
            );


        finalCylinderWaveRamp =
            Mathf.MoveTowards(
                finalCylinderWaveRamp,
                1f,
                Time.deltaTime /
                safeDuration
            );


        float strength =
            Mathf.Lerp(
                0f,
                finalCylinderMaxWaveStrength,
                finalCylinderWaveRamp
            );


        // =====================================================
        // APPLY WAVE
        // =====================================================

        SetFinalCylinderWave(
            finalCylinderWaveTime,
            strength
        );
    }


    // =========================================================
    // FINAL CYLINDER
    // SMOKE → SPLASH
    // =========================================================

    private IEnumerator FinalCylinderSmokeSplashSequence()
    {
        // =====================================================
        // WAIT AFTER WAVE START
        // =====================================================

        if (smokeDelay > 0f)
        {
            yield return new WaitForSeconds(
                smokeDelay
            );
        }


        // =====================================================
        // START SMOKE
        // =====================================================

        PlayLoopingParticleArray(
            smokeEffects
        );


        // =====================================================
        // WAIT AFTER SMOKE
        // =====================================================

        if (splashDelay > 0f)
        {
            yield return new WaitForSeconds(
                splashDelay
            );
        }


        // =====================================================
        // START SPLASH
        // =====================================================

        /* PlayParticleArray(
             splashEffects
         );*/

        StartRandomSplashEffects();
    }


    // =========================================================
    // UPDATE PARTICLE EFFECTS
    // =========================================================

    private void UpdateParticleEffects(
        FillParticleTrigger[] effects,
        float fillPercent
    )
    {
        if (effects == null)
        {
            return;
        }


        fillPercent =
            Mathf.Clamp(
                fillPercent,
                0f,
                100f
            );


        foreach (FillParticleTrigger effect in effects)
        {
            if (effect == null ||
                effect.particle == null)
            {
                continue;
            }


            float start =
                Mathf.Clamp(
                    effect.startPercent,
                    0f,
                    100f
                );


            float stop =
                Mathf.Clamp(
                    effect.stopPercent,
                    start,
                    100f
                );


            if (fillPercent >= start &&
                fillPercent < stop)
            {
                ParticleSystem.MainModule main =
                    effect.particle.main;


                main.loop = true;


                if (!effect.particle.isPlaying)
                {
                    effect.particle.Play();
                }
            }


            if (fillPercent >= stop)
            {
                if (effect.particle.isPlaying)
                {
                    effect.particle.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear
                    );
                }
            }
        }
    }


    // =========================================================
    // STOP FILL PARTICLES
    // =========================================================

    private void StopParticleEffects(
        FillParticleTrigger[] effects
    )
    {
        if (effects == null)
        {
            return;
        }


        foreach (FillParticleTrigger effect in effects)
        {
            if (effect == null ||
                effect.particle == null)
            {
                continue;
            }


            effect.particle.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }
    }


    // =========================================================
    // PLAY LOOPING PARTICLE
    // =========================================================

    private void PlayLoopingParticle(
        ParticleSystem particle
    )
    {
        if (particle == null)
        {
            return;
        }


        EnsureActiveForPlay(particle);

        ParticleSystem.MainModule main =
            particle.main;


        main.loop = true;


        if (!particle.isPlaying)
        {
            particle.Play();
        }
    }


    // =========================================================
    // PLAY LOOPING ARRAY
    // =========================================================

    /// <summary>A particle system on a switched-off object does not play: switch on its parents under the
    /// boiler (e.g. ParticleEffects) first.</summary>
    private void EnsureActiveForPlay(ParticleSystem particle)
    {
        if (particle == null || particle.gameObject.activeInHierarchy) return;

        Transform stopAt = transform.parent != null ? transform.parent : transform.root;
        for (Transform t = particle.transform; t != null && t != stopAt; t = t.parent)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
        }
    }

    private void PlayLoopingParticleArray(
        ParticleSystem[] particles
    )
    {
        if (particles == null)
        {
            return;
        }


        for (int i = 0;
             i < particles.Length;
             i++)
        {
            if (particles[i] == null)
            {
                continue;
            }


            PlayLoopingParticle(
                particles[i]
            );
        }
    }


    // =========================================================
    // PLAY PARTICLE
    // =========================================================

    private void PlayParticle(
        ParticleSystem particle
    )
    {
        if (particle == null)
        {
            return;
        }


        particle.Play();
    }


    // =========================================================
    // PLAY ARRAY
    // =========================================================

    private void PlayParticleArray(
        ParticleSystem[] particles
    )
    {
        if (particles == null)
        {
            return;
        }


        for (int i = 0;
             i < particles.Length;
             i++)
        {
            if (particles[i] == null)
            {
                continue;
            }


            PlayParticle(
                particles[i]
            );
        }
    }


    // =========================================================
    // STOP PARTICLE
    // =========================================================

    private void StopParticle(
        ParticleSystem particle
    )
    {
        if (particle == null)
        {
            return;
        }


        particle.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );
    }


    // =========================================================
    // STOP PARTICLE ARRAY
    // =========================================================

    private void StopParticleArray(
        ParticleSystem[] particles
    )
    {
        if (particles == null)
        {
            return;
        }


        for (int i = 0;
             i < particles.Length;
             i++)
        {
            StopParticle(
                particles[i]
            );
        }
    }


    // =========================================================
    // SET PIPE WATER PROPERTIES
    // =========================================================

    private void SetWaterFlowProperties(
        PipeFlowStage stage,
        MaterialPropertyBlock block,
        float fill
    )
    {
        if (stage == null || stage.waterObject == null)
        {
            return;
        }

        stage.waterObject.GetPropertyBlock(
            block
        );

        block.SetFloat(
            FillPropertyId,
            Mathf.Clamp01(fill)
        );

        block.SetFloat(
            BottomPropertyId,
            stage.cachedBottom
        );

        block.SetFloat(
            TopPropertyId,
            stage.cachedTop
        );

        block.SetFloat(
            FlowAxisPropertyId,
            (float)stage.flowAxis
        );

        block.SetFloat(
            FlowDirectionPropertyId,
            (float)stage.flowDirection
        );

        // Pipe water never waves.
        block.SetFloat(
            WaveTimePropertyId,
            0f
        );

        block.SetFloat(
            WaveStrengthPropertyId,
            0f
        );

        stage.waterObject.SetPropertyBlock(
            block
        );
    }


    // =========================================================
    // SET FINAL CYLINDER FILL
    // =========================================================

    private void EnsureFinalCylinderInitialized()
    {
        if (finalCylinderBlock == null)
        {
            finalCylinderBlock = new MaterialPropertyBlock();
        }

        if (!finalCylinderBoundsCached && finalCylinder != null)
        {
            MeshFilter meshFilter =
                finalCylinder.GetComponent<MeshFilter>();

            if (meshFilter != null &&
                meshFilter.sharedMesh != null)
            {
                Bounds bounds =
                    meshFilter.sharedMesh.bounds;

                finalCylinderBottom =
                    bounds.min.y;

                finalCylinderTop =
                    bounds.max.y;

                if (Mathf.Approximately(
                        finalCylinderBottom,
                        finalCylinderTop))
                {
                    finalCylinderTop =
                        finalCylinderBottom +
                        0.01f;
                }

                finalCylinderBoundsCached = true;
            }
        }
    }

    private void SetFinalCylinderFill(
        float fill
    )
    {
        if (finalCylinder == null)
        {
            return;
        }

        EnsureFinalCylinderInitialized();

        finalCylinder.GetPropertyBlock(
            finalCylinderBlock
        );

        // Cylinder uses Y axis.
        finalCylinderBlock.SetFloat(
            FlowAxisPropertyId,
            (float)FlowAxis.Y
        );

        finalCylinderBlock.SetFloat(
            FlowDirectionPropertyId,
            (float)FlowDirection.Positive
        );

        finalCylinderBlock.SetFloat(
            FillPropertyId,
            Mathf.Clamp01(fill)
        );

        finalCylinderBlock.SetFloat(
            BottomPropertyId,
            finalCylinderBottom
        );

        finalCylinderBlock.SetFloat(
            TopPropertyId,
            finalCylinderTop
        );

        finalCylinder.SetPropertyBlock(
            finalCylinderBlock
        );
    }


    // =========================================================
    // SET FINAL CYLINDER WAVE
    // =========================================================

    private void SetFinalCylinderWave(
        float waveTime,
        float waveStrength
    )
    {
        if (finalCylinder == null)
        {
            return;
        }

        EnsureFinalCylinderInitialized();

        finalCylinder.GetPropertyBlock(
            finalCylinderBlock
        );

        finalCylinderBlock.SetFloat(
            WaveTimePropertyId,
            waveTime
        );

        finalCylinderBlock.SetFloat(
            WaveStrengthPropertyId,
            waveStrength
        );

        finalCylinder.SetPropertyBlock(
            finalCylinderBlock
        );
    }


    // =========================================================
    // INITIALIZE FINAL CYLINDER
    // =========================================================

    private void InitializeFinalCylinder()
    {
        if (finalCylinder == null)
        {
            return;
        }

        if (!finalCylinderBoundsCached)
        {
            MeshFilter meshFilter =
                finalCylinder.GetComponent<MeshFilter>();

            if (meshFilter != null &&
                meshFilter.sharedMesh != null)
            {
                Bounds bounds =
                    meshFilter.sharedMesh.bounds;

                finalCylinderBottom =
                    bounds.min.y;

                finalCylinderTop =
                    bounds.max.y;

                if (Mathf.Approximately(
                        finalCylinderBottom,
                        finalCylinderTop))
                {
                    finalCylinderTop =
                        finalCylinderBottom +
                        0.01f;
                }

                finalCylinderBoundsCached = true;
            }
        }

        finalCylinder.enabled = false;

        finalCylinderMaxWaveStrength =
            GetMaterialWaveStrength(
                finalCylinder
            );

        SetFinalCylinderFill(
            0f
        );

        SetFinalCylinderWave(
            0f,
            0f
        );
    }


    // =========================================================
    // GET MATERIAL WAVE STRENGTH
    // =========================================================

    private float GetMaterialWaveStrength(
        Renderer targetRenderer
    )
    {
        if (targetRenderer == null)
        {
            return 0.05f;
        }

        Material material =
            targetRenderer.sharedMaterial;

        if (material == null ||
            !material.HasProperty(
                WaveStrengthPropertyId))
        {
            return 0.05f;
        }

        return material.GetFloat(
            WaveStrengthPropertyId
        );
    }

    /*private void UpdateFireDelay()
    {
        if (fireStarted)
        {
            return;
        }

        fireTimer += Time.deltaTime;

        if (fireTimer >= fireDelayAfterFull)
        {
            fireStarted = true;

            if (fireEffect != null)
            {
                ParticleSystem.MainModule main =
                    fireEffect.main;

                main.loop = true;

                if (!fireEffect.isPlaying)
                {
                    fireEffect.Play();
                }
            }
        }
    }*/

    private void UpdateFireDelay()
    {
        if (fireStarted)
        {
            return;
        }

        fireTimer += Time.deltaTime;

        if (fireTimer >= fireDelayAfterFull)
        {
            fireStarted = true;

            // =====================================================
            // START FIRE
            // =====================================================

            if (fireEffect != null)
            {
                ParticleSystem.MainModule main =
                    fireEffect.main;

                main.loop = true;

                if (!fireEffect.isPlaying)
                {
                    fireEffect.Play();
                }
            }

            // =====================================================
            // START PIPE MATERIAL CHANGE DELAY
            // =====================================================

            StartCoroutine(
                ChangePipeMaterialsAfterFire()
            );
        }
    }
    // =========================================================
    // MAKE OBJECT TRANSPARENT
    // =========================================================

    private void MakeObjectTransparent()
    {
        if (objectToMakeTransparent == null)
        {
            return;
        }

        if (objectTransparentMaterial == null)
        {
            Debug.LogWarning(
                "Object Transparent Material is not assigned."
            );

            return;
        }

        objectToMakeTransparent.sharedMaterial =
            objectTransparentMaterial;
    }

    private IEnumerator ChangePipeMaterialsAfterFire()
    {
        if (pipeMaterialChangeDelayAfterFire > 0f)
        {
            yield return new WaitForSeconds(
                pipeMaterialChangeDelayAfterFire
            );
        }

        if (pipesToChangeAfterFire == null ||
            pipeMaterialAfterFire == null)
        {
            yield break;
        }

        for (int i = 0; i < pipesToChangeAfterFire.Length; i++)
        {
            if (pipesToChangeAfterFire[i] == null)
            {
                continue;
            }

            pipesToChangeAfterFire[i].sharedMaterial =
                pipeMaterialAfterFire;
        }
    }

    // =========================================================
    // START RANDOM SPLASH EFFECTS
    // =========================================================

    private void StartRandomSplashEffects()
    {
        if (splashEffects == null)
        {
            return;
        }

        for (int i = 0; i < splashEffects.Length; i++)
        {
            if (splashEffects[i] == null ||
                splashEffects[i].particle == null)
            {
                continue;
            }

            StartCoroutine(
                RunRandomSplash(
                    splashEffects[i]
                )
            );
        }
    }


    // =========================================================
    // RUN ONE RANDOM SPLASH
    // =========================================================

    private IEnumerator RunRandomSplash(
        RandomSplashEffect splash
    )
    {
        if (splash == null ||
            splash.particle == null)
        {
            yield break;
        }


        float min =
            Mathf.Max(
                0f,
                splash.minInterval
            );

        float max =
            Mathf.Max(
                min,
                splash.maxInterval
            );


        // =====================================================
        // FIRST PLAY
        // =====================================================

        EnsureActiveForPlay(splash.particle);
        splash.particle.Play();


        // =====================================================
        // CONTINUE FOREVER
        // =====================================================

        while (true)
        {
            float nextInterval =
                Random.Range(
                    min,
                    max
                );


            yield return new WaitForSeconds(
                nextInterval
            );


            if (splash.particle == null)
            {
                yield break;
            }


            EnsureActiveForPlay(splash.particle);
        splash.particle.Play();
        }
    }

    // =========================================================
    // RESET / END ENTIRE PROCESS
    // =========================================================

    public void ResetProcess()
    {
        // =====================================================
        // STOP ALL RUNNING COROUTINES
        // =====================================================

        StopAllCoroutines();

        // =====================================================
        // STOP PROCESS
        // =====================================================

        started = false;

        // =====================================================
        // RESET PIPE STATE
        // =====================================================

        currentPipeIndex = -1;
        currentPipeFill = 0f;
        pipesFinished = false;

        // =====================================================
        // RESET FINAL CYLINDER STATE
        // =====================================================

        finalCylinderFill = 0f;
        finalCylinderFinished = false;

        finalCylinderWaveTime = 0f;
        finalCylinderWaveRamp = 0f;
        finalCylinderMaxWaveStrength =
            GetMaterialWaveStrength(finalCylinder);

        // =====================================================
        // RESET WAVE STATE
        // =====================================================

        waveWaiting = false;
        waveStarted = false;
        waveDelayTimer = 0f;

        // =====================================================
        // RESET POST-FULL SEQUENCE
        // =====================================================

        postFullSequenceStarted = false;

        // =====================================================
        // RESET FIRE STATE
        // =====================================================

        fireTimer = 0f;
        fireStarted = false;

        // =====================================================
        // RESTORE OUTER PART MATERIAL
        // =====================================================

        if (outerPartRenderer != null &&
            originalOuterMaterial != null)
        {
            outerPartRenderer.sharedMaterial =
                originalOuterMaterial;
        }

        // =====================================================
        // RESTORE TARGET OBJECT MATERIAL
        // =====================================================

        if (objectToMakeTransparent != null &&
            objectOriginalMaterial != null)
        {
            objectToMakeTransparent.sharedMaterial =
                objectOriginalMaterial;
        }

        // =====================================================
        // RESET ALL PIPES
        // =====================================================

        if (pipeStages != null)
        {
            for (int i = 0; i < pipeStages.Length; i++)
            {
                PipeFlowStage stage = pipeStages[i];

                if (stage == null)
                {
                    continue;
                }

                // Restore original pipe material.
                if (stage.pipeParent != null &&
                    stage.originalMaterial != null)
                {
                    stage.pipeParent.sharedMaterial =
                        stage.originalMaterial;
                }

                // Reset water fill.
                if (stage.waterObject != null &&
                    pipeBlocks != null &&
                    i < pipeBlocks.Length &&
                    pipeBlocks[i] != null)
                {
                    stage.waterObject.enabled = false;

                    SetWaterFlowProperties(
                        stage,
                        pipeBlocks[i],
                        0f
                    );
                }

                // Stop pipe particles.
                StopParticleEffects(
                    stage.fillParticles
                );
            }
        }

        // =====================================================
        // RESET FINAL CYLINDER
        // =====================================================

        if (finalCylinder != null)
        {
            finalCylinder.enabled = false;

            SetFinalCylinderFill(0f);

            SetFinalCylinderWave(
                0f,
                0f
            );
        }

        // =====================================================
        // STOP FINAL CYLINDER PARTICLES
        // =====================================================

        StopParticleEffects(
            finalCylinderEffects
        );

        // =====================================================
        // STOP SMOKE
        // =====================================================

        StopParticleArray(
            smokeEffects
        );

        // =====================================================
        // STOP SPLASH
        // =====================================================

        StopRandomSplashArray(
            splashEffects
        );

        // =====================================================
        // STOP FIRE
        // =====================================================

        StopParticle(
            fireEffect
        );

        // =====================================================
        // RESTORE PIPES CHANGED AFTER FIRE
        // =====================================================

        if (pipesToChangeAfterFire != null)
        {
            for (int i = 0;
                 i < pipesToChangeAfterFire.Length;
                 i++)
            {
                MeshRenderer pipe =
                    pipesToChangeAfterFire[i];

                if (pipe == null)
                {
                    continue;
                }

                if (pipeOriginalMaterials.TryGetValue(pipe, out Material origMat) && origMat != null)
                {
                    pipe.sharedMaterial = origMat;
                    continue;
                }

                // Find the matching PipeFlowStage
                // and restore its original material.
                if (pipeStages != null)
                {
                    for (int j = 0;
                         j < pipeStages.Length;
                         j++)
                    {
                        PipeFlowStage stage =
                            pipeStages[j];

                        if (stage == null)
                        {
                            continue;
                        }

                        if (stage.pipeParent == pipe &&
                            stage.originalMaterial != null)
                        {
                            pipe.sharedMaterial =
                                stage.originalMaterial;
                            pipeOriginalMaterials[pipe] = stage.originalMaterial;

                            break;
                        }
                    }
                }
            }
        }

        // =====================================================
        // FINAL UI / SHADER STATE
        // =====================================================

        SetFinalCylinderFill(0f);

        SetFinalCylinderWave(
            0f,
            0f
        );

        // =====================================================
        // READY FOR NEXT START
        // =====================================================

        currentPipeIndex = -1;
        currentPipeFill = 0f;

        UpdateWaterAndParticleVisibility();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
            "[BoilerFluidController] Entire boiler process reset."
        );
#endif
    }
}