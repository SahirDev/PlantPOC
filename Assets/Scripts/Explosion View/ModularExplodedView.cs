using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class ModularExplodedView : MonoBehaviour
{
    [Header("Explosion")]
    [SerializeField]
    private float animationDuration = 1f;

    [SerializeField]
    private Transform explosionCameraPoint;

    [SerializeField]
    private float initialDistance = 30f;

    [SerializeField]
    private LayerMask ignoreLayer;

    public Transform ExplosionCameraPoint => explosionCameraPoint;
    public float InitialDistance => initialDistance;

    private readonly HashSet<Transform> exploded = new();
    private readonly Dictionary<Transform, Vector3> originalPositions = new();

    private readonly List<GameObject> hiddenObjects = new();
    private bool isIsolated;

    private GameObject hoveredObject;

    private void Awake()
    {
        CacheHierarchy(transform);

    }

    // private void Update()
    // {
    //     CheckHover();
    // }

    private void CacheHierarchy(Transform parent)
    {
        originalPositions[parent] = parent.localPosition;

        foreach (Transform child in parent)
            CacheHierarchy(child);
    }

    public void ToggleExplode(Transform target)
    {
        if (target == null) return;

        var group = GetExplodedParent(target);

        if (group != null)
        {
            Collapse(group);
            return;
        }

        group = GetExplodableGroup(target);

        if (group == null) return;

        if (exploded.Contains(group))
        {
            Collapse(group);
            return;
        }

        Explode(group);
    }

    private Transform GetExplodedParent(Transform selected)
    {
        var current = selected;

        while (current != null)
        {
            if (exploded.Contains(current))
                return current;

            current = current.parent;
        }

        return null;
    }

    private Transform GetExplodableGroup(Transform selected)
    {
        if (selected == null) return null;

        var current = selected;

        while (current != null)
        {
            if (current.TryGetComponent<ExplodableViewNode>(out var node))
                if (current == selected || node.ShouldChildExplode)
                    return current;

            current = current.parent;
        }

        return null;
    }

    private void Explode(Transform group)
    {
        if (group == null) return;

        if (!originalPositions.TryGetValue(group, out var originalPosition)) return;

        var node = group.GetComponent<ExplodableViewNode>();

        if (node == null) return;

        var worldOffset = group.TransformVector(node.ExplosionOffset);

        exploded.Add(group);

        var groupTargetPosition = originalPosition + worldOffset;

        StartCoroutine(AnimatePosition(group, group.localPosition, groupTargetPosition));

        if (node.ShouldChildExplode)
            ExplodeChildren(group, worldOffset, 2);
    }

    private void ExplodeChildren(Transform root, Vector3 worldOffset, int index)
    {
        foreach (Transform child in root)
        {
            if (!originalPositions.TryGetValue(child, out var originalPosition))
                continue;

            var localOffset = child.parent.InverseTransformVector(worldOffset);
            var targetPosition = originalPosition + localOffset * index;

            StartCoroutine(AnimatePosition(child, child.localPosition, targetPosition));

            index++;

            ExplodeChildren(child, worldOffset, index);
        }
    }

    public void ExplodeAll()
    {
        var nodes = GetComponentsInChildren<ExplodableViewNode>(true);

        foreach (var node in nodes)
        {
            if (node == null) continue;
            if (exploded.Contains(node.transform)) continue;

            var originalPosition = originalPositions[node.transform];
            var worldOffset = node.transform.TransformVector(node.ExplosionOffset);
            var targetPosition = originalPosition + worldOffset;

            exploded.Add(node.transform);

            StartCoroutine(AnimatePosition(node.transform, node.transform.localPosition, targetPosition));
        }
    }

    private void Collapse(Transform group)
    {
        if (group == null) return;

        CollapseHierarchy(group);

        exploded.Remove(group);
    }

    public void CollapseAll()
    {
        foreach (var group in exploded)
            CollapseHierarchy(group);

        exploded.Clear();
    }

    private void CollapseHierarchy(Transform parent)
    {
        if (originalPositions.TryGetValue(parent, out var originalPosition))
            StartCoroutine(AnimatePosition(parent, parent.localPosition, originalPosition));

        foreach (Transform child in parent)
            CollapseHierarchy(child);
    }

    private IEnumerator AnimatePosition(Transform target, Vector3 start, Vector3 end)
    {
        if (target == null) yield break;
        var elapsed = 0f;

        while (elapsed < animationDuration)
        {
            if (target == null) yield break;
            elapsed += Time.deltaTime;

            var t = animationDuration > 0f ? Mathf.SmoothStep(0f, 1f, elapsed / animationDuration) : 1f;

            target.localPosition = Vector3.Lerp(start, end, t);

            yield return null;
        }

        if (target != null)
        {
            target.localPosition = end;
            // (No camera re-centre here: it ran for every moving part and threw the camera around / out of the room.)
        }
    }

    public void IsolateExplosionView()
    {
        if (isIsolated) return;

        isIsolated = true;
        hiddenObjects.Clear();

        var root = gameObject.scene.GetRootGameObjects();

        foreach (var sceneObject in root)
            HideUnrelatedObjects(sceneObject);
    }

    private void HideUnrelatedObjects(GameObject obj)
    {
        if (obj == null || obj == gameObject || obj.transform.IsChildOf(transform) || transform.IsChildOf(obj.transform))
            return;

        if (obj.CompareTag("Player") || obj.name == "Worker" || obj.name == "SceneController" || obj.name == "HUDController")
            return;

        if (obj.GetComponent<SceneChangeCol>() != null || obj.GetComponent<PlayerSceneContext>() != null)
            return;

        if (obj == Camera.main || obj.GetComponentInChildren<Camera>(true) != null)
            return;

        if ((ignoreLayer.value & (1 << obj.layer)) != 0)
            return;

        if (obj.GetComponent<Canvas>() != null || obj.GetComponent<UIDocument>() != null)
            return;

        obj.SetActive(false);
        hiddenObjects.Add(obj);
    }

    public void RestoreExplosionView()
    {
        if (!isIsolated) return;

        foreach (var obj in hiddenObjects)
            if (obj != null)
                obj.SetActive(true);

        hiddenObjects.Clear();
        isIsolated = false;
    }

    public bool IsPartExploded(Transform target)
    {
        if (target == null)
            return false;

        var group = GetExplodedParent(target);

        if (group != null)
            return true;

        group = GetExplodableGroup(target);

        return group != null && exploded.Contains(group);
    }
}