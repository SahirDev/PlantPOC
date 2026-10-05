using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class BoxColliderHighlighter : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material outlineMaterial;
    [SerializeField] private Material fillMaterial;

    [Header("Outline")]
    [SerializeField] private float outlineWidth = 0.35f;

    [Header("Fill")]
    [SerializeField] private bool showFill = true;

    private BoxCollider currentCollider;
    private Transform currentTransform;

    private GameObject highlightRig;
    private Transform rigTransform;

    private LineRenderer lineRenderer;
    private Transform fillTransform;
    private MeshRenderer fillRenderer;

    private readonly Vector3[] linePoints = new Vector3[16];

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 lastScale;
    private Vector3 lastCenter;
    private Vector3 lastSize;

    public bool IsHighlighted => currentCollider != null;
    public BoxCollider CurrentCollider => currentCollider;

    private void Awake()
    {
        CreateRig();
    }

    private void LateUpdate()
    {
        if (currentCollider == null || currentTransform == null) return;

        if (currentTransform.position != lastPosition || currentTransform.rotation != lastRotation || currentTransform.lossyScale != lastScale || currentCollider.center != lastCenter || currentCollider.size != lastSize) UpdateHighlight();
    }

    private void OnDestroy()
    {
        if (highlightRig != null) Destroy(highlightRig);
    }

    public void Highlight(BoxCollider box)
    {
        if (box == null)
        {
            Clear();
            return;
        }

        currentCollider = box;
        currentTransform = box.transform;

        if (highlightRig == null) CreateRig();

        UpdateHighlight();
        highlightRig.SetActive(true);
    }

    public void Clear()
    {
        currentCollider = null;
        currentTransform = null;

        if (highlightRig != null) highlightRig.SetActive(false);
    }

    private void CreateRig()
    {
        if (highlightRig != null) return;

        highlightRig = new GameObject("BoxHighlightRig");
        rigTransform = highlightRig.transform;

        GameObject outlineObject = new GameObject("Outline");
        outlineObject.transform.SetParent(rigTransform, false);

        lineRenderer = outlineObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = false;
        lineRenderer.positionCount = 16;
        lineRenderer.startWidth = outlineWidth;
        lineRenderer.endWidth = outlineWidth;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.numCapVertices = 2;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.sharedMaterial = outlineMaterial;

        GameObject fillObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fillObject.name = "Fill";
        fillObject.transform.SetParent(rigTransform, false);

        Collider generatedCollider = fillObject.GetComponent<Collider>();
        if (generatedCollider != null) Destroy(generatedCollider);

        fillTransform = fillObject.transform;
        fillRenderer = fillObject.GetComponent<MeshRenderer>();
        fillRenderer.sharedMaterial = fillMaterial;
        fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;
        fillRenderer.enabled = showFill;

        highlightRig.SetActive(false);
    }

    private void UpdateHighlight()
    {
        if (currentCollider == null || currentTransform == null || rigTransform == null) return;

        Vector3 worldCenter = currentTransform.TransformPoint(currentCollider.center);
        Vector3 scale = currentTransform.lossyScale;

        Vector3 worldSize = new Vector3(Mathf.Abs(currentCollider.size.x * scale.x), Mathf.Abs(currentCollider.size.y * scale.y), Mathf.Abs(currentCollider.size.z * scale.z));

        rigTransform.SetPositionAndRotation(worldCenter, currentTransform.rotation);
        rigTransform.localScale = Vector3.one;

        UpdateLinePoints(worldSize);

        fillTransform.localPosition = Vector3.zero;
        fillTransform.localRotation = Quaternion.identity;
        fillTransform.localScale = worldSize;
        fillRenderer.enabled = showFill;

        lastPosition = currentTransform.position;
        lastRotation = currentTransform.rotation;
        lastScale = currentTransform.lossyScale;
        lastCenter = currentCollider.center;
        lastSize = currentCollider.size;
    }

    private void UpdateLinePoints(Vector3 size)
    {
        Vector3 h = size * 0.5f;

        Vector3 p0 = new Vector3(-h.x, -h.y, -h.z);
        Vector3 p1 = new Vector3(h.x, -h.y, -h.z);
        Vector3 p2 = new Vector3(h.x, -h.y, h.z);
        Vector3 p3 = new Vector3(-h.x, -h.y, h.z);
        Vector3 p4 = new Vector3(-h.x, h.y, -h.z);
        Vector3 p5 = new Vector3(h.x, h.y, -h.z);
        Vector3 p6 = new Vector3(h.x, h.y, h.z);
        Vector3 p7 = new Vector3(-h.x, h.y, h.z);

        linePoints[0] = p0; linePoints[1] = p1; linePoints[2] = p2; linePoints[3] = p3;
        linePoints[4] = p0; linePoints[5] = p4; linePoints[6] = p5; linePoints[7] = p1;
        linePoints[8] = p5; linePoints[9] = p6; linePoints[10] = p2; linePoints[11] = p6;
        linePoints[12] = p7; linePoints[13] = p3; linePoints[14] = p7; linePoints[15] = p4;

        lineRenderer.SetPositions(linePoints);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (lineRenderer != null)
        {
            lineRenderer.sharedMaterial = outlineMaterial;
            lineRenderer.startWidth = outlineWidth;
            lineRenderer.endWidth = outlineWidth;
        }

        if (fillRenderer != null)
        {
            fillRenderer.sharedMaterial = fillMaterial;
            fillRenderer.enabled = showFill;
        }
    }
#endif
}