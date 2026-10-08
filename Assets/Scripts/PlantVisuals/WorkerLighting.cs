using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the worker readable in the baked indoor rooms (Boiler / Turbine / Control room).
///
/// Why it was dark: those rooms are lit by baked lights, which only light walls / floors. A moving character gets
/// its light from light probes - the rooms have none where the worker walks (Control_Room: none at all), so the
/// worker got almost nothing.
///
/// What this does (no re-bake, no extra realtime light): every few frames it reads the light at the worker's
/// position (probes / room ambient), lifts it to at least <see cref="minimumBrightness"/> and adds a soft fill from
/// the camera's side - only as much as is missing, so in bright scenes (Main_Scene by day) nothing changes.
/// The result is given to the worker's meshes as their own ambient light (per-renderer spherical harmonics).
///
/// Installs itself on the worker. Tweak on the component (Play mode) or the defaults below.
/// </summary>
public class WorkerLighting : MonoBehaviour
{
    [Tooltip("Lowest ambient brightness the worker gets (0 = off, 0.5 = medium grey light).")]
    [SerializeField, Range(0f, 1.5f)] private float minimumBrightness = 0.55f;
    [Tooltip("Soft light from the camera side, added only when the room is darker than the minimum.")]
    [SerializeField, Range(0f, 2f)] private float fillIntensity = 0.7f;
    [SerializeField] private Color fillColor = new Color(1f, 0.96f, 0.9f);
    [Tooltip("Seconds between updates (the light follows the worker smoothly).")]
    [SerializeField, Min(0.02f)] private float updateInterval = 0.1f;

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly SphericalHarmonicsL2[] shArray = new SphericalHarmonicsL2[1];
    private readonly Vector3[] upDirection = { Vector3.up };
    private readonly Color[] evaluated = new Color[1];
    private MaterialPropertyBlock block;
    private float nextUpdate;
    private Camera playerCamera;

    // ------------------------------------------------------------------ self-install

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AddToWorkers();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AddToWorkers();

    private static void AddToWorkers()
    {
        foreach (Player player in FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (player.GetComponent<WorkerLighting>() == null) player.gameObject.AddComponent<WorkerLighting>();
    }

    // ------------------------------------------------------------------ lifecycle

    private void OnEnable()
    {
        block ??= new MaterialPropertyBlock();
        CollectRenderers();
        nextUpdate = 0f;
    }

    private void OnDisable()
    {
        // Back to normal probe lighting.
        foreach (Renderer r in renderers)
            if (r != null) r.lightProbeUsage = LightProbeUsage.BlendProbes;
    }

    private void CollectRenderers()
    {
        renderers.Clear();
        playerCamera = GetComponentInChildren<Camera>(true);
        Transform cameraTransform = playerCamera != null ? playerCamera.transform : null;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (cameraTransform != null && r.transform.IsChildOf(cameraTransform)) continue;
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer && r.gameObject.layer == gameObject.layer)) continue;
            renderers.Add(r);
        }
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextUpdate || renderers.Count == 0) return;
        nextUpdate = Time.unscaledTime + updateInterval;
        Apply();
    }

    // ------------------------------------------------------------------ lighting

    private void Apply()
    {
        Vector3 position = transform.position + Vector3.up * 1f;

        // Light here: interpolated probes if the scene has some, otherwise the room's ambient.
        SphericalHarmonicsL2 sh;
        if (LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0)
            LightProbes.GetInterpolatedProbe(position, renderers[0], out sh);
        else
            sh = RenderSettings.ambientProbe;

        sh.Evaluate(upDirection, evaluated);
        float brightness = evaluated[0].grayscale;
        float missing = Mathf.Max(0f, minimumBrightness - brightness);

        if (missing > 0f)
        {
            sh.AddAmbientLight(new Color(missing, missing, missing) * 0.8f);

            // Soft key from the camera side (slightly above), scaled by how dark it is.
            Vector3 toCamera = playerCamera != null ? playerCamera.transform.position - position : -transform.forward;
            toCamera.y = Mathf.Max(toCamera.y, 0f) + toCamera.magnitude * 0.35f;
            float darkness = minimumBrightness > 0f ? Mathf.Clamp01(missing / minimumBrightness) : 0f;
            if (toCamera.sqrMagnitude > 0.0001f && fillIntensity > 0f)
                sh.AddDirectionalLight(toCamera.normalized, fillColor, fillIntensity * darkness);
        }

        shArray[0] = sh;
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (r.lightProbeUsage != LightProbeUsage.CustomProvided) r.lightProbeUsage = LightProbeUsage.CustomProvided;
            r.GetPropertyBlock(block);
            block.CopySHCoefficientArraysFrom(shArray);
            r.SetPropertyBlock(block);
        }
    }
}
