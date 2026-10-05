using UnityEngine;

/// <summary>
/// Name + description shown in the Unity info card when this object is clicked in the
/// Main_Scene plant overview (isometric camera).
///
/// Put it on the same GameObject as the BoxCollider tagged "Highlight" (or on a parent of it).
/// Leave Display Name empty to use the GameObject's name.
/// </summary>
[DisallowMultipleComponent]
public class ObjectInfo : MonoBehaviour
{
    public enum PanelSide
    {
        Auto,   // right of the object, left if there is no room
        Right,
        Left
    }

    [Tooltip("Title of the info card. Empty = GameObject name.")]
    [SerializeField] private string displayName;

    [TextArea(3, 10)]
    [SerializeField] private string description;

    [Tooltip("Which side of the object the card opens on.")]
    [SerializeField] private PanelSide side = PanelSide.Auto;

    [Tooltip("Extra offset of the card in screen pixels (x = away from the object, y = up).")]
    [SerializeField] private Vector2 screenOffset;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public string Description => description ?? string.Empty;
    public PanelSide Side => side;
    public Vector2 ScreenOffset => screenOffset;

    /// <summary>ObjectInfo of a collider: on the collider's GameObject first, then its parents.</summary>
    public static ObjectInfo For(Component collider)
    {
        if (collider == null) return null;
        ObjectInfo info = collider.GetComponent<ObjectInfo>();
        return info != null ? info : collider.GetComponentInParent<ObjectInfo>();
    }
}
