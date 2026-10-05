using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class WaterSurfaceMesh : MonoBehaviour
{
    [Header("Reference Fluid")]
    [SerializeField] private Renderer fluidRenderer;

    [Header("Surface Resolution")]
    [SerializeField] private int xSegments = 80;
    [SerializeField] private int zSegments = 40;

    [Header("Fit")]
    [SerializeField, Range(0.8f, 1f)]
    private float widthPadding = 0.92f;

    [SerializeField, Range(0.8f, 1f)]
    private float lengthPadding = 0.92f;

    private Mesh waterMesh;

    private void Awake()
    {
        GenerateSurface();
    }

    private void GenerateSurface()
    {
        if (fluidRenderer == null)
        {
            Debug.LogError("Fluid Renderer is not assigned.");
            return;
        }

        MeshFilter fluidMeshFilter =
            fluidRenderer.GetComponent<MeshFilter>();

        if (fluidMeshFilter == null ||
            fluidMeshFilter.sharedMesh == null)
        {
            Debug.LogError(
                "Fluid object needs a MeshFilter with a mesh."
            );
            return;
        }

        // Get the actual dimensions of Cylinder (1).
        Bounds bounds = fluidMeshFilter.sharedMesh.bounds;

        float width = bounds.size.x * widthPadding;
        float length = bounds.size.z * lengthPadding;

        CreateGrid(width, length);
    }

    private void CreateGrid(float width, float length)
    {
        int vertexCount =
            (xSegments + 1) *
            (zSegments + 1);

        Vector3[] vertices =
            new Vector3[vertexCount];

        Vector2[] uv =
            new Vector2[vertexCount];

        int triangleCount =
            xSegments *
            zSegments *
            6;

        int[] triangles =
            new int[triangleCount];

        int index = 0;

        // -----------------------------
        // CREATE VERTICES
        // -----------------------------

        for (int z = 0; z <= zSegments; z++)
        {
            float z01 =
                (float)z / zSegments;

            float localZ =
                Mathf.Lerp(
                    -length * 0.5f,
                     length * 0.5f,
                     z01
                );

            for (int x = 0; x <= xSegments; x++)
            {
                float x01 =
                    (float)x / xSegments;

                float localX =
                    Mathf.Lerp(
                        -width * 0.5f,
                         width * 0.5f,
                         x01
                    );

                vertices[index] =
                    new Vector3(
                        localX,
                        0f,
                        localZ
                    );

                uv[index] =
                    new Vector2(
                        x01,
                        z01
                    );

                index++;
            }
        }

        // -----------------------------
        // CREATE TRIANGLES
        // -----------------------------

        int triangleIndex = 0;

        for (int z = 0; z < zSegments; z++)
        {
            for (int x = 0; x < xSegments; x++)
            {
                int current =
                    z * (xSegments + 1) + x;

                int nextRow =
                    current + xSegments + 1;

                triangles[triangleIndex++] = current;
                triangles[triangleIndex++] = nextRow;
                triangles[triangleIndex++] = current + 1;

                triangles[triangleIndex++] = current + 1;
                triangles[triangleIndex++] = nextRow;
                triangles[triangleIndex++] = nextRow + 1;
            }
        }

        waterMesh = new Mesh();
        waterMesh.name = "Generated_Water_Surface";

        waterMesh.vertices = vertices;
        waterMesh.uv = uv;
        waterMesh.triangles = triangles;

        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();

        MeshFilter meshFilter =
            GetComponent<MeshFilter>();

        meshFilter.sharedMesh = waterMesh;
    }

    private void OnDestroy()
    {
        if (waterMesh != null)
        {
            Destroy(waterMesh);
            waterMesh = null;
        }
    }
}