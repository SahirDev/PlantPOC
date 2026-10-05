using System;
using System.Collections;
using UnityEngine;

public class ExplodableViewNode : MonoBehaviour
{
    [Header("Explosion")]
    [SerializeField]
    private Vector3 explosionOffset = new(10f, 0f, 0f);

    [SerializeField]
    private bool shouldChildExplode = false;

    [Header("Parts")]
    [SerializeField]
    private string partName;

    [SerializeField]
    [Multiline]
    private string description;

    private bool isHovered = false;

    public Vector3 ExplosionOffset => explosionOffset;
    public bool ShouldChildExplode => shouldChildExplode;

    public string Description => description;
    public string PartName => string.IsNullOrEmpty(partName) ? gameObject.name : partName;

    public void OnHoverEnter()
    {
        if (!isHovered)
        {
            isHovered = true;

            // Debug.Log($"Hovered on {(string.IsNullOrEmpty(partName) ? gameObject.name : partName)}");
            if (HUDController.HasInstance) HUDController.Instance.ShowPartName(PartName);
        }
    }

    public void OnHoverExit()
    {
        if (!isHovered)
            return;

        isHovered = false;

        if (HUDController.HasInstance) HUDController.Instance.HidePartName();
    }
}