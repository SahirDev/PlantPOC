using UnityEngine;

/// <summary>
/// Turns a room built as ONE BoxCollider around the whole room (e.g. TurbineRoom "Warehouse") into real walls.
/// From inside a solid box nothing is blocked - the worker, the fly camera and the camera checks all pass
/// through. At start this replaces it with six thin slabs on the box faces (4 walls, floor, roof), keeping the
/// inside space, layer and physics material. Put it on the object with that BoxCollider.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BoxRoomWalls : MonoBehaviour
{
    [Tooltip("Wall thickness in metres (outside the box faces, so the room inside keeps its size).")]
    [SerializeField, Min(0.05f)] private float thickness = 1f;
    [SerializeField] private bool floor = true;
    [SerializeField] private bool roof = true;

    private void Awake()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null || !box.enabled || box.isTrigger) return;

        Vector3 scale = transform.lossyScale;
        Vector3 local = new Vector3(
            thickness / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            thickness / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            thickness / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        Vector3 c = box.center, s = box.size;

        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == 1 && !floor && !roof) continue;
            for (int side = -1; side <= 1; side += 2)
            {
                if (axis == 1 && side < 0 && !floor) continue;
                if (axis == 1 && side > 0 && !roof) continue;

                Vector3 size = s + 2f * local; // overlap at the corners
                size[axis] = local[axis];
                Vector3 center = c;
                center[axis] += side * (s[axis] + local[axis]) * 0.5f;

                var slab = gameObject.AddComponent<BoxCollider>();
                slab.center = center;
                slab.size = size;
                slab.sharedMaterial = box.sharedMaterial;
            }
        }

        box.enabled = false;
    }
}
