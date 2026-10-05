using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple and lightweight LineRenderer flow effect.
/// - Controls: Speed, Density (tiling), Size (width), Texture, and Color.
/// - Spline Points: Uses child Transforms (Start, Bends, End) so you can
///   click and move/rotate them directly in the Scene View using Unity's native Move (W) and Rotate (E) tools!
/// - WebGL Optimized: Single LineRenderer, 1 Draw Call, 0 particle overhead.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
[SelectionBase]
public class SplineParticleFlow : MonoBehaviour
{
    [Header("Texture & Appearance")]
    [Tooltip("The texture that will flow along the line (electric, steam, arrows, etc.)")]
    [SerializeField] private Texture2D flowTexture;
    [Tooltip("Color and glow tint for the line")]
    [SerializeField] private Color color = new Color(0.2f, 0.95f, 1f, 1f);
    [Tooltip("Size / Thickness of the line")]
    [SerializeField, Min(0.01f)] private float size = 0.35f;

    [Header("Motion & Density")]
    [Tooltip("Speed at which the texture moves from start to end")]
    [SerializeField] private float speed = 2.0f;
    [Tooltip("Density: How many times the texture repeats along the line")]
    [SerializeField, Min(0.01f)] private float density = 1.0f;
    [Tooltip("Reverse the flow direction")]
    [SerializeField] private bool reverse = false;

    [Header("Curve Smoothing")]
    [Tooltip("Smooths the line between points. Set to 0 or 1 for straight segments between points.")]
    [SerializeField, Range(1, 30)] private int curveSmoothness = 16;

    [Header("Point Transforms")]
    [Tooltip("The list of point transforms in order (Start -> Bends -> End). If empty, child objects are used automatically.")]
    [SerializeField] private List<Transform> points = new List<Transform>();

    // Internal references
    private LineRenderer lineRenderer;
    private Material runtimeMaterial;
    private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    // Public properties for easy script access
    public float Speed { get => speed; set => speed = value; }
    public float Density { get => density; set => density = Mathf.Max(0.1f, value); }
    public float Size { get => size; set => size = Mathf.Max(0.01f, value); }
    public Texture2D FlowTexture { get => flowTexture; set { flowTexture = value; UpdateMaterialProperties(); } }
    public Color Color { get => color; set { color = value; UpdateMaterialProperties(); } }
    public bool Reverse { get => reverse; set => reverse = value; }
    public int PointCount => points.Count;
    public List<Transform> Points => points;
    public LineRenderer LineRendererComponent => lineRenderer;

    private void Awake()
    {
        InitializeLine();
    }

    private void OnEnable()
    {
        InitializeLine();
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying) Destroy(runtimeMaterial);
            else DestroyImmediate(runtimeMaterial);
        }
    }

    public void InitializeLine()
    {
        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.textureMode = LineTextureMode.Tile;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.numCapVertices = 4;

        EnsureMaterial();
        EnsurePoints();
        UpdateLineGeometry();
        UpdateMaterialProperties();
    }

    private void EnsureMaterial()
    {
        if (runtimeMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Texture");

            runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "SplineFlow_RuntimeMat";
            runtimeMaterial.SetFloat("_Surface", 1); // Transparent
            runtimeMaterial.SetFloat("_Blend", 1);   // Additive
            runtimeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            runtimeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            runtimeMaterial.SetInt("_ZWrite", 0);
            runtimeMaterial.renderQueue = 3000;

            if (flowTexture == null)
            {
#if UNITY_EDITOR
                flowTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Material/SplineFlow/Tex_ElectricFlow.png");
#endif
            }
        }

        if (lineRenderer != null && lineRenderer.sharedMaterial != runtimeMaterial)
        {
            lineRenderer.material = runtimeMaterial;
        }
    }

    public void EnsurePoints()
    {
        // Remove any destroyed / null points
        points.RemoveAll(p => p == null);

        // If no points assigned, find child transforms
        if (points.Count == 0)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("Point") || child.name.StartsWith("Start") || child.name.StartsWith("End") || child.name.StartsWith("Bend"))
                {
                    points.Add(child);
                }
            }
        }

        // If still empty, create default Start, Bend, End points
        if (points.Count < 2)
        {
            CreateDefaultPoints();
        }
    }

    private void CreateDefaultPoints()
    {
        // Clear any old point children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
            if (c.name.StartsWith("Point") || c.name.StartsWith("Start") || c.name.StartsWith("End") || c.name.StartsWith("Bend"))
            {
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
        }
        points.Clear();

        var p0 = CreatePointObject("Start (0)", new Vector3(0f, 0f, 0f));
        var p1 = CreatePointObject("Bend (1)", new Vector3(4f, 1.5f, 1f));
        var p2 = CreatePointObject("End (2)", new Vector3(8f, 0f, 3f));

        points.Add(p0);
        points.Add(p1);
        points.Add(p2);
    }

    private Transform CreatePointObject(string pointName, Vector3 localPosition)
    {
        var go = new GameObject(pointName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        return go.transform;
    }

    private void Update()
    {
        // 1. Update line geometry to follow points
        UpdateLineGeometry();

        // 2. Animate UV texture flow
        AnimateTextureFlow();
    }

    private void AnimateTextureFlow()
    {
        if (runtimeMaterial == null) return;

        float time = Application.isPlaying ? Time.time : (float)DateTime.Now.TimeOfDay.TotalSeconds;
        float dir = reverse ? 1f : -1f;
        float offset = (time * speed) * dir;

        runtimeMaterial.mainTextureOffset = new Vector2(offset % 1f, 0f);
    }

    public void UpdateMaterialProperties()
    {
        EnsureMaterial();
        if (runtimeMaterial == null) return;

        if (flowTexture != null)
        {
            runtimeMaterial.mainTexture = flowTexture;
            if (runtimeMaterial.HasProperty(BaseMapID)) runtimeMaterial.SetTexture(BaseMapID, flowTexture);
        }

        runtimeMaterial.mainTextureScale = new Vector2(Mathf.Max(0.1f, density), 1f);

        if (runtimeMaterial.HasProperty(BaseColorID)) runtimeMaterial.SetColor(BaseColorID, color);
        runtimeMaterial.color = color;
    }

    public void UpdateLineGeometry()
    {
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null) return;

        // Clean nulls
        points.RemoveAll(p => p == null);
        if (points.Count < 2) return;

        lineRenderer.startWidth = size;
        lineRenderer.endWidth = size;

        // If smoothness is 1, just connect points directly
        if (curveSmoothness <= 1)
        {
            lineRenderer.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                lineRenderer.SetPosition(i, points[i].position);
            }
            return;
        }

        // Generate smooth Catmull-Rom curve through all points
        int segmentCount = points.Count - 1;
        int totalPositions = segmentCount * curveSmoothness + 1;
        lineRenderer.positionCount = totalPositions;

        int posIndex = 0;
        for (int seg = 0; seg < segmentCount; seg++)
        {
            Vector3 p0 = seg > 0 ? points[seg - 1].position : 2f * points[0].position - points[1].position;
            Vector3 p1 = points[seg].position;
            Vector3 p2 = points[seg + 1].position;
            Vector3 p3 = (seg + 2 < points.Count) ? points[seg + 2].position : 2f * points[points.Count - 1].position - points[points.Count - 2].position;

            // Also factor point rotations to bend the curve gracefully
            Vector3 forward1 = points[seg].forward * Vector3.Distance(p1, p2) * 0.3f;
            Vector3 forward2 = points[seg + 1].forward * Vector3.Distance(p1, p2) * 0.3f;

            for (int step = 0; step < curveSmoothness; step++)
            {
                float t = (float)step / curveSmoothness;
                Vector3 catmull = CatmullRom(p0, p1, p2, p3, t);
                Vector3 hermite = CubicHermite(p1, forward1, p2, forward2, t);
                Vector3 finalPos = Vector3.Lerp(catmull, hermite, 0.45f);

                lineRenderer.SetPosition(posIndex++, finalPos);
            }
        }

        // Set final point
        lineRenderer.SetPosition(posIndex, points[points.Count - 1].position);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private static Vector3 CubicHermite(Vector3 p0, Vector3 t0, Vector3 p1, Vector3 t1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return h00 * p0 + h10 * t0 + h01 * p1 + h11 * t1;
    }

    #region Point Management Helpers

    public Transform AddPointInBetween(int afterIndex = -1)
    {
        EnsurePoints();
        if (points.Count < 2) return null;

        int insertIndex;
        Vector3 newPos;
        Quaternion newRot;

        if (afterIndex >= 0 && afterIndex < points.Count - 1)
        {
            insertIndex = afterIndex + 1;
            newPos = (points[afterIndex].position + points[afterIndex + 1].position) * 0.5f;
            newRot = Quaternion.Slerp(points[afterIndex].rotation, points[afterIndex + 1].rotation, 0.5f);
        }
        else
        {
            // Insert in the middle
            insertIndex = Mathf.Max(1, points.Count / 2);
            newPos = (points[insertIndex - 1].position + points[insertIndex].position) * 0.5f;
            newRot = Quaternion.Slerp(points[insertIndex - 1].rotation, points[insertIndex].rotation, 0.5f);
        }

        var newPoint = CreatePointObject($"Bend ({insertIndex})", transform.InverseTransformPoint(newPos));
        newPoint.rotation = newRot;
        points.Insert(insertIndex, newPoint);

        RenamePoints();
        UpdateLineGeometry();
        return newPoint;
    }

    public Transform AddPointAtEnd()
    {
        EnsurePoints();
        int n = points.Count;

        Vector3 newPos;
        Quaternion newRot = Quaternion.identity;

        if (n >= 2)
        {
            Vector3 dir = (points[n - 1].position - points[n - 2].position).normalized;
            float dist = Vector3.Distance(points[n - 1].position, points[n - 2].position);
            newPos = points[n - 1].position + dir * Mathf.Max(2f, dist);
            newRot = points[n - 1].rotation;
        }
        else if (n == 1)
        {
            newPos = points[0].position + transform.forward * 3f;
        }
        else
        {
            newPos = transform.position;
        }

        var newPoint = CreatePointObject($"Point ({n})", transform.InverseTransformPoint(newPos));
        newPoint.rotation = newRot;
        points.Add(newPoint);

        RenamePoints();
        UpdateLineGeometry();
        return newPoint;
    }

    public void RemovePoint(int index)
    {
        if (points.Count <= 2) return;
        if (index < 0 || index >= points.Count) return;

        var t = points[index];
        points.RemoveAt(index);

        if (t != null)
        {
            if (Application.isPlaying) Destroy(t.gameObject);
            else DestroyImmediate(t.gameObject);
        }

        RenamePoints();
        UpdateLineGeometry();
    }

    public void RenamePoints()
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] == null) continue;
            if (i == 0) points[i].name = "Start (0)";
            else if (i == points.Count - 1) points[i].name = $"End ({i})";
            else points[i].name = $"Bend ({i})";
        }
    }

    #endregion

    #region Presets

    public void SetElectricPreset()
    {
        speed = 4.0f;
        density = 5.0f;
        size = 0.25f;
        color = new Color(0.2f, 0.95f, 1f, 1f) * 1.5f;

#if UNITY_EDITOR
        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Material/SplineFlow/Tex_ElectricFlow.png");
        if (tex != null) flowTexture = tex;
#endif
        UpdateMaterialProperties();
    }

    public void SetSteamPreset()
    {
        speed = 1.2f;
        density = 3.0f;
        size = 0.65f;
        color = new Color(0.92f, 0.96f, 1f, 0.70f);

#if UNITY_EDITOR
        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Material/SplineFlow/Tex_SteamFlow.png");
        if (tex != null) flowTexture = tex;
#endif
        UpdateMaterialProperties();
    }

    public void SetEnergyArrowsPreset()
    {
        speed = 2.5f;
        density = 4.0f;
        size = 0.35f;
        color = new Color(0.2f, 1f, 0.85f, 1f);

#if UNITY_EDITOR
        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Material/SplineFlow/Tex_ArrowFlow.png");
        if (tex != null) flowTexture = tex;
#endif
        UpdateMaterialProperties();
    }

    #endregion

    private void OnDrawGizmos()
    {
        if (points == null || points.Count == 0) return;

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] == null) continue;

            if (i == 0)
            {
                Gizmos.color = Color.green; // Start
                Gizmos.DrawWireSphere(points[i].position, size * 0.8f);
            }
            else if (i == points.Count - 1)
            {
                Gizmos.color = Color.red; // End
                Gizmos.DrawWireSphere(points[i].position, size * 0.8f);
            }
            else
            {
                Gizmos.color = Color.yellow; // In-between bend
                Gizmos.DrawWireSphere(points[i].position, size * 0.6f);
            }

            // Direction ray
            Gizmos.color = new Color(1f, 1f, 0f, 0.6f);
            Gizmos.DrawRay(points[i].position, points[i].forward * (size * 1.5f));
        }
    }
}
