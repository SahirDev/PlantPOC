using UnityEngine;

/// <summary>
/// The minimap of ONE scene: its picture + where that picture lies in the world.
/// Add one to every scene (Tools > Thermal Plant > 6 does it). No camera is used: MiniMapPanel shows
/// the picture as UI and moves the worker icon over it.
///
/// LINING IT UP (once per scene, in the Scene view):
///   1. Assign Map Image (a Sprite, e.g. from Assets/Texture/MiniMap).
///   2. Put this object on the floor. A see-through copy of the picture is shown flat on the floor
///      (editor only, never in the build).
///   3. Move / rotate (Y only) / scale this object until the walls in the picture sit on the real walls.
///      Keep X and Z scale equal to the picture's proportions (use the scale handle's centre = uniform).
/// That's all: the worker icon then follows the worker exactly.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class MiniMapArea : MonoBehaviour
{
    public static MiniMapArea Current { get; private set; }

    [Tooltip("The minimap picture of this scene (Texture Type: Sprite).")]
    [SerializeField] private Sprite mapImage;

    [Header("Lining up (Scene view only)")]
    [SerializeField] private bool showPreview = true;
    [SerializeField, Range(0.1f, 1f)] private float previewOpacity = 0.6f;
    [Tooltip("Preview height above this object, so it is not hidden inside the floor.")]
    [SerializeField] private float previewHeight = 0.05f;

    private SpriteRenderer preview;

    public Sprite MapImage => mapImage;

    /// <summary>Picture size in this object's local units (X = width, Y = height along local Z).</summary>
    public Vector2 ImageSize
    {
        get
        {
            if (mapImage == null) return new Vector2(10f, 10f);
            Vector2 size = mapImage.bounds.size;
            return new Vector2(Mathf.Max(0.0001f, size.x), Mathf.Max(0.0001f, size.y));
        }
    }

    /// <summary>Width / height of the picture.</summary>
    public float ImageAspect
    {
        get
        {
            Vector2 size = ImageSize;
            return size.x / size.y;
        }
    }

    /// <summary>Y rotation of the picture in the world (degrees). Picture "up" = this object's +Z.</summary>
    public float MapYaw => transform.eulerAngles.y;

    /// <summary>World position -> position on the picture, 0..1 (x to the right, y up). Outside = &lt;0 or &gt;1.</summary>
    public Vector2 WorldToMap(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector2 size = ImageSize;
        return new Vector2(local.x / size.x + 0.5f, local.z / size.y + 0.5f);
    }

    private void OnEnable()
    {
        if (Application.isPlaying) Current = this;
        else UpdatePreview();
    }

    private void OnDisable()
    {
        if (Current == this) Current = null;
        DestroyPreview();
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (!Application.isPlaying) UpdatePreview();
    }
#endif

    // ------------------------------------------------------------------ editor preview

    private void UpdatePreview()
    {
#if UNITY_EDITOR
        if (!showPreview || mapImage == null)
        {
            DestroyPreview();
            return;
        }

        if (preview == null)
        {
            // Not saved, not in the build, not in the Hierarchy: only a visual aid while lining up.
            var go = new GameObject("MiniMap Preview") { hideFlags = HideFlags.HideAndDontSave };
            preview = go.AddComponent<SpriteRenderer>();
            preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            preview.receiveShadows = false;
        }

        preview.sprite = mapImage;
        preview.color = new Color(1f, 1f, 1f, previewOpacity);

        // Not parented (so it never gets in the way of this object being deleted): follow by hand.
        Vector3 scale = transform.lossyScale;
        preview.transform.SetPositionAndRotation(
            transform.position + transform.up * previewHeight,
            transform.rotation * Quaternion.Euler(90f, 0f, 0f)); // flat, picture up = local +Z
        preview.transform.localScale = new Vector3(scale.x, scale.z, 1f);
#endif
    }

    private void DestroyPreview()
    {
        if (preview == null) return;

        if (Application.isPlaying) Destroy(preview.gameObject);
        else DestroyImmediate(preview.gameObject);
        preview = null;
    }

    private void OnDrawGizmos()
    {
        // Outline of the picture on the floor, also when the preview is off.
        Vector2 size = ImageSize;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.01f, size.y));

        // Arrow = picture "up".
        Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, size.y * 0.5f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
