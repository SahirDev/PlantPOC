using System;
using System.Collections;
using UnityEngine;
using static UnityEngine.ParticleSystem;

public class BoilerOperationInfo : MonoBehaviour
{
    public static BoilerOperationInfo instanced;
    public GameObject outerlayer;
    [SerializeField]
    private Material transparentOuterMaterial;
    public BoilerFluidController instance;
    [Tooltip("GameObject with BoilerDashboardController. Switched on by the info sequence (sends the dashboard to React).")]
    public GameObject uiobject;
    public GameObject particel;
    public Material[] originalPipeMaterials;
    public Material originalOuterMaterial;

    private MeshRenderer cachedOuterRenderer;

    private void Awake()
    {
        instanced = this;
        
        if (outerlayer != null)
        {
            cachedOuterRenderer = outerlayer.GetComponent<MeshRenderer>();
        }

        if (particel != null)
        {
            particel.SetActive(false);
        }
    }

    private void Start()
    {
        instanced = this;
    }

    private void OnDestroy()
    {
        if (instanced == this)
        {
            instanced = null;
        }
    }

    public void StartBoilerInfoSequence()
    {
        if (BoilerFluidController.instance != null)
        {
            BoilerFluidController.instance.SetInfoActive(true);
        }

        StartCoroutine(BoilerActionAfter3Seconds());
    }

    private IEnumerator BoilerActionAfter3Seconds()
    {
        var fluid = BoilerFluidController.instance != null ? BoilerFluidController.instance : instance;
        if (fluid != null && fluid.pipesToChangeAfterFire != null && fluid.pipeMaterialAfterFire != null)
        {
            for (int i = 0; i < fluid.pipesToChangeAfterFire.Length; i++)
            {
                if (fluid.pipesToChangeAfterFire[i] != null)
                {
                    fluid.pipesToChangeAfterFire[i].sharedMaterial = fluid.pipeMaterialAfterFire;
                }
            }
        }

        if (particel != null)
            particel.SetActive(true);

        if (uiobject != null)
            uiobject.SetActive(true);

        // Wait 3 seconds
        yield return new WaitForSeconds(3f);

        if (cachedOuterRenderer != null && transparentOuterMaterial != null)
        {
            cachedOuterRenderer.sharedMaterial = transparentOuterMaterial;
        }
    }

    public void ResetBoilerInfo()
    {
        // Stop any sequence still running.
        StopAllCoroutines();

        // Restore original pipe materials.
        BoilerFluidController fluid =
            BoilerFluidController.instance != null ? BoilerFluidController.instance : instance;

        if (fluid != null &&
            fluid.pipesToChangeAfterFire != null &&
            originalPipeMaterials != null)
        {
            for (int i = 0;
                 i < fluid.pipesToChangeAfterFire.Length &&
                 i < originalPipeMaterials.Length;
                 i++)
            {
                if (fluid.pipesToChangeAfterFire[i] != null)
                {
                    fluid.pipesToChangeAfterFire[i].sharedMaterial =
                        originalPipeMaterials[i];
                }
            }
        }

        // Restore outer-layer material.
        if (cachedOuterRenderer != null && originalOuterMaterial != null)
        {
            cachedOuterRenderer.sharedMaterial = originalOuterMaterial;
        }
        else if (outerlayer != null && originalOuterMaterial != null)
        {
            MeshRenderer outerRenderer = outerlayer.GetComponent<MeshRenderer>();
            if (outerRenderer != null)
            {
                outerRenderer.sharedMaterial = originalOuterMaterial;
            }
        }

        // Hide particle and UI.
        if (particel != null)
            particel.SetActive(false);

        if (uiobject != null)
            uiobject.SetActive(false);

        if (BoilerFluidController.instance != null)
            BoilerFluidController.instance.SetInfoActive(false);

        Debug.Log("Boiler info reset completed.");
    }

}
