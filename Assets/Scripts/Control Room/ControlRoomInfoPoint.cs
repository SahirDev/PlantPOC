using UnityEngine;

/// <summary>
/// Control room info point: when the worker walks into this trigger, React is told which info panel to show
/// (handleControlRoomInfoEntered { index, name, title }); walking out sends handleControlRoomInfoExited.
///
/// Setup: create an empty GameObject where the info should appear (e.g. "Info Point 1"), add this component
/// (a trigger BoxCollider is added automatically), set Info Index to 1, 2 or 3 and size the box.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ControlRoomInfoPoint : MonoBehaviour
{
    [Tooltip("Which info panel React shows: 1, 2 or 3.")]
    [SerializeField, Min(1)] private int infoIndex = 1;

    [Tooltip("Optional title sent to React (empty = GameObject name).")]
    [SerializeField] private string title = "";

    public int InfoIndex => infoIndex;

    private bool workerInside;

    private void Reset()
    {
        var box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(3f, 2.5f, 3f);
        box.center = new Vector3(0f, 1.25f, 0f);
    }

    private void Awake()
    {
        var box = GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (workerInside || !other.CompareTag("Player")) return;
        workerInside = true;
        CommunicationManager.HandleControlRoomInfoEntered_Extern(BuildPayload());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!workerInside || !other.CompareTag("Player")) return;
        workerInside = false;
        CommunicationManager.HandleControlRoomInfoExited_Extern(BuildPayload());
    }

    // Info point switched off / scene unloading while the worker is inside: tell React the panel can close.
    private void OnDisable()
    {
        if (!workerInside) return;
        workerInside = false;
        CommunicationManager.HandleControlRoomInfoExited_Extern(BuildPayload());
    }

    private ControlRoomInfoPayload BuildPayload() => new ControlRoomInfoPayload
    {
        index = infoIndex,
        name = gameObject.name,
        title = string.IsNullOrEmpty(title) ? gameObject.name : title
    };

    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
