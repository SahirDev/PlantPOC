using UnityEngine;

public class WaterSurfaceController : MonoBehaviour
{
    [SerializeField] private Renderer fluidRenderer;
    [SerializeField] private Transform waterSurface;

    private MaterialPropertyBlock block;
    private static readonly int FillLevelProp = Shader.PropertyToID("_FillLevel");

    private float bottomY;
    private float topY;

    private void Awake()
    {
        block = new MaterialPropertyBlock();

        if (fluidRenderer == null || waterSurface == null)
        {
            Debug.LogError("Assign Fluid Renderer and Water Surface.");
            enabled = false;
            return;
        }

        MeshFilter meshFilter = fluidRenderer.GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("Fluid needs a MeshFilter with a mesh.");
            enabled = false;
            return;
        }

        Bounds bounds = meshFilter.sharedMesh.bounds;

        bottomY = bounds.min.y;
        topY = bounds.max.y;

        // Start surface at bottom.
        SetSurfaceHeight(0f);
    }

    private void EnsureInitialized()
    {
        if (block == null)
            block = new MaterialPropertyBlock();

        if (fluidRenderer != null && Mathf.Approximately(bottomY, topY))
        {
            MeshFilter meshFilter = fluidRenderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Bounds bounds = meshFilter.sharedMesh.bounds;
                bottomY = bounds.min.y;
                topY = bounds.max.y;
            }
        }
    }

    public void SetFillLevel(float fillLevel)
    {
        EnsureInitialized();

        if (fluidRenderer == null)
            return;

        fillLevel = Mathf.Clamp01(fillLevel);

        // Tell the water shader the current fill.
        fluidRenderer.GetPropertyBlock(block);
        block.SetFloat(FillLevelProp, fillLevel);
        fluidRenderer.SetPropertyBlock(block);

        // Move only the water surface.
        if (waterSurface != null)
        {
            float y = Mathf.Lerp(
                bottomY,
                topY,
                fillLevel
            );

            SetSurfaceHeight(y);
        }
    }

    private void SetSurfaceHeight(float y)
    {
        Vector3 position = waterSurface.localPosition;

        position.y = y;

        waterSurface.localPosition = position;
    }
}