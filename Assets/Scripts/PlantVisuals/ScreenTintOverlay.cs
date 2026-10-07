using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Darkens / tints the whole view with ONE quad in front of whichever camera is rendering
/// (overview or worker), using the PlantPOC/ScreenMultiply shader. This is how night works even though
/// Main_Scene's daylight is baked into lightmaps: no second lightmap set, no post-processing.
///
/// Several features can tint at the same time through named channels (e.g. "time" from DayNightController);
/// the final tint is their product. All white = the quad is switched off
/// (zero cost in daylight).
/// </summary>
public class ScreenTintOverlay : MonoBehaviour
{
    public static ScreenTintOverlay Instance { get; private set; }

    [Tooltip("Material with the PlantPOC/ScreenMultiply shader (darkens everything outside the building light volumes).")]
    [SerializeField] private Material multiplyMaterial;
    [Tooltip("Optional: ScreenMultiply material for the pixels INSIDE the building light volumes (stencil != 0).")]
    [SerializeField] private Material insideMultiplyMaterial;
    [Tooltip("How much lighter the inside of the building boxes stays (0 = as dark as outside, 1 = not darkened).")]
    [SerializeField, Range(0f, 1f)] private float insideLight = 0.6f;
    [Tooltip("Seconds to fade between tints.")]
    [SerializeField, Min(0f)] private float fadeSeconds = 1.5f;

    private class Channel { public Color current = Color.white; public Color target = Color.white; }

    private readonly Dictionary<string, Channel> channels = new Dictionary<string, Channel>();
    private MeshRenderer quadRenderer;
    private Material runtimeMaterial;
    private Material insideRuntimeMaterial;
    private Transform quad;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        Instance = this;

        var go = new GameObject("TintQuad");
        go.transform.SetParent(transform, false);
        quad = go.transform;
        go.AddComponent<MeshFilter>().sharedMesh = BuildQuad();
        quadRenderer = go.AddComponent<MeshRenderer>();
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.lightProbeUsage = LightProbeUsage.Off;
        quadRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        quadRenderer.allowOcclusionWhenDynamic = false;

        if (multiplyMaterial != null)
        {
            runtimeMaterial = new Material(multiplyMaterial);
            if (insideMultiplyMaterial != null)
            {
                // Same quad drawn twice: outside pixels with the full tint, inside pixels with the lighter one.
                insideRuntimeMaterial = new Material(insideMultiplyMaterial);
                quadRenderer.sharedMaterials = new[] { runtimeMaterial, insideRuntimeMaterial };
            }
            else
            {
                quadRenderer.sharedMaterial = runtimeMaterial;
            }
        }

        quadRenderer.enabled = false;
    }

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += PlaceInFrontOf;
    private void OnDisable() => RenderPipelineManager.beginCameraRendering -= PlaceInFrontOf;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
        if (insideRuntimeMaterial != null) Destroy(insideRuntimeMaterial);
    }

    public float InsideLight
    {
        get => insideLight;
        set { insideLight = Mathf.Clamp01(value); Apply(); }
    }

    /// <summary>Sets the target tint of a channel (white = no tint). Fades unless instant.</summary>
    public void SetTint(string channel, Color tint, bool instant = false)
    {
        if (!channels.TryGetValue(channel, out Channel c))
        {
            c = new Channel();
            channels[channel] = c;
        }

        c.target = tint;
        if (instant) c.current = tint;
        Apply();
    }

    private void Update()
    {
        float step = fadeSeconds <= 0f ? 1f : Time.unscaledDeltaTime / fadeSeconds;
        bool changed = false;

        foreach (Channel c in channels.Values)
        {
            if (c.current == c.target) continue;
            c.current = MoveTowards(c.current, c.target, step);
            changed = true;
        }

        if (changed) Apply();
    }

    private void Apply()
    {
        Color total = Color.white;
        foreach (Channel c in channels.Values) total *= c.current;
        total.a = 1f;

        bool active = total.r < 0.999f || total.g < 0.999f || total.b < 0.999f;
        if (quadRenderer != null) quadRenderer.enabled = active && runtimeMaterial != null;
        if (runtimeMaterial != null) runtimeMaterial.SetColor(ColorId, total);
        if (insideRuntimeMaterial != null)
        {
            Color inside = Color.Lerp(total, Color.white, insideLight);
            inside.a = 1f;
            insideRuntimeMaterial.SetColor(ColorId, inside);
        }
    }

    private static Color MoveTowards(Color from, Color to, float step)
    {
        return new Color(Mathf.MoveTowards(from.r, to.r, step), Mathf.MoveTowards(from.g, to.g, step),
            Mathf.MoveTowards(from.b, to.b, step), Mathf.MoveTowards(from.a, to.a, step));
    }

    // Just before each camera renders: put the quad right in front of it, covering the whole view.
    private void PlaceInFrontOf(ScriptableRenderContext context, Camera cam)
    {
        if (quadRenderer == null || !quadRenderer.enabled) return;
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;

        Transform t = cam.transform;
        float distance = cam.nearClipPlane + 0.05f;
        float height = cam.orthographic ? cam.orthographicSize * 2f : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = height * Mathf.Max(cam.aspect, 0.01f);

        quad.SetPositionAndRotation(t.position + t.forward * distance, t.rotation);
        quad.localScale = new Vector3(width * 1.2f, height * 1.2f, 1f);
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "TintQuad" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // never culled
        return mesh;
    }
}
