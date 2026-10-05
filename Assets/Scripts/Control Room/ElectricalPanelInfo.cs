using UnityEngine;

public class ElectricalPanelInfo : MonoBehaviour
{
    [Header("Indicator Lights")]
    public Renderer greenLight;

    public Renderer redLight;

    [Header("Scene Lighting")]
    public GameObject sceneLighting;

    [Header("Warning Audio")]
    public AudioSource warningAudioSource;

    public AudioClip warningSound;

    [Header("Emission Settings")]
    public Color greenEmissionColor = Color.green;

    public Color redEmissionColor = Color.red;
    public float emissionIntensity = 5f;

    [Header("Warning Light")]
    public GameObject Warninglight;

    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    private Material greenMaterial;
    private Material redMaterial;

    private bool warningSoundPlaying = false;

    private float generatorValue;

    [SerializeField]
    private Animator animator;

    public float GeneratorValue => generatorValue;


    private void Start()
    {
        if (Warninglight != null)
            Warninglight.SetActive(false);

        // -----------------------------
        // MATERIAL INITIALIZATION
        // -----------------------------

        if (greenLight != null)
            greenMaterial = greenLight.material;

        if (redLight != null)
            redMaterial = redLight.material;

        // -----------------------------
        // WARNING AUDIO INITIALIZATION
        // -----------------------------

        if (warningAudioSource != null)
        {
            warningAudioSource.loop = true;
            warningAudioSource.Stop();
        }

        // -----------------------------
        // INITIAL VALUE
        // -----------------------------

        generatorValue = 0f;

        UpdateValues(generatorValue);
    }


    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        if (HUDController.Instance == null)
            return;

        HUDController.Instance.SetElectricalPanel(this);
    }


    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        if (HUDController.Instance == null)
            return;

        HUDController.Instance.ResetElectricalPanel(this);
    }


    // =====================================================
    // CALLED BY HUD CONTROLLER
    // =====================================================

    public void SetGeneratorValue(float value)
    {
        generatorValue = value;

        UpdateValues(generatorValue);
    }


    public void RefreshUI()
    {
        UpdateValues(generatorValue);
    }

    private float RandomizeValue(float value, float min, float max)
    {
        return Mathf.Clamp(
            value + Random.Range(-10f, 10f),
            min,
            max
        );
    }


    // =====================================================
    // PANEL VALUES
    // =====================================================

    private void UpdateValues(float value)
    {
        //var generatorKV =
        //    Mathf.Lerp(70f, 89f, value / 100f);

        //var gridKV = 100f;

        //var loading =
        //    Mathf.Lerp(20f, 82f, value / 100f);

        //var oilTemp =s
        //    Mathf.Lerp(45f, 67f, value / 100f);

        //var windingTemp =
        //    Mathf.Lerp(40f, 80f, value / 100f);

        //var coolingFan = loading >= 50f;

        var generatorKV = Mathf.Lerp(0f, 100f, value / 100f);

        if (value < 80f)
        {
            generatorKV = RandomizeValue(Mathf.Lerp(0f, 80f, value / 100f), 60f, 100f);
        }


        var gridKV = 100f;

        var loading =
            RandomizeValue(Mathf.Lerp(20f, 82f, value / 100f), 10f, 92f);

        var oilTemp =
            RandomizeValue(Mathf.Lerp(45f, 67f, value / 100f), 35f, 77f);

        var windingTemp =
            RandomizeValue(Mathf.Lerp(40f, 80f, value / 100f), 30f, 90f);

        var coolingFan = loading >= 50f;



        // -----------------------------
        // UPDATE UI TOOLKIT
        // -----------------------------

        if (HUDController.Instance != null)
            HUDController.Instance.UpdateElectricalPanelValues(
                value,
                generatorKV,
                gridKV,
                loading,
                oilTemp,
                windingTemp,
                coolingFan
            );


        // -----------------------------
        // LIGHT LOGIC
        // -----------------------------

        if (value < 85f)
        {
            // GREEN STATE

            SetGreenLight(true);
            SetRedLight(false);

            if (sceneLighting != null)
                sceneLighting.SetActive(true);

            StopWarningAudio();

            if (Warninglight != null)
                Warninglight.SetActive(false);
        }
        else if (value < 90f)
        {
            // RED STATE

            SetGreenLight(false);
            SetRedLight(true);

            if (sceneLighting != null)
                sceneLighting.SetActive(true);

            StopWarningAudio();

            if (Warninglight != null)
                Warninglight.SetActive(false);
        }
        else
        {
            // WARNING STATE (>= 90%)
            SetGreenLight(false);
            SetRedLight(true);

            if (sceneLighting != null)
                sceneLighting.SetActive(true);

            StartWarningAudio();

            if (Warninglight != null)
            {
                Warninglight.SetActive(true);
                if (animator != null)
                    animator.Play("FlickerLight");
            }
        }
    }


    // =====================================================
    // WARNING AUDIO
    // =====================================================

    private void StartWarningAudio()
    {
        if (warningAudioSource == null ||
            warningSound == null)
            return;

        if (!warningSoundPlaying)
        {
            warningAudioSource.clip = warningSound;
            warningAudioSource.loop = true;
            warningAudioSource.Play();

            warningSoundPlaying = true;
        }
    }


    private void StopWarningAudio()
    {
        if (warningAudioSource == null)
            return;

        if (warningSoundPlaying)
        {
            warningAudioSource.Stop();
            warningSoundPlaying = false;
        }
    }


    // =====================================================
    // GREEN LIGHT
    // =====================================================

    private void SetGreenLight(bool state)
    {
        if (greenMaterial == null)
            return;

        if (state)
        {
            greenMaterial.EnableKeyword(EmissionKeyword);
            greenMaterial.SetColor(EmissionColorProp, greenEmissionColor * emissionIntensity);
        }
        else
        {
            greenMaterial.SetColor(EmissionColorProp, Color.black);
            greenMaterial.DisableKeyword(EmissionKeyword);
        }
    }


    // =====================================================
    // RED LIGHT
    // =====================================================

    private void SetRedLight(bool state)
    {
        if (redMaterial == null)
            return;

        if (state)
        {
            redMaterial.EnableKeyword(EmissionKeyword);
            redMaterial.SetColor(EmissionColorProp, redEmissionColor * emissionIntensity);
        }
        else
        {
            redMaterial.SetColor(EmissionColorProp, Color.black);
            redMaterial.DisableKeyword(EmissionKeyword);
        }
    }


    private void OnDestroy()
    {
        StopWarningAudio();

        if (HUDController.Instance != null)
            HUDController.Instance.ResetElectricalPanel(this);

        if (greenMaterial != null)
        {
            Destroy(greenMaterial);
            greenMaterial = null;
        }

        if (redMaterial != null)
        {
            Destroy(redMaterial);
            redMaterial = null;
        }
    }
}