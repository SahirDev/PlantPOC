using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Picture of one part on a clean background (for the maintenance report). The part is rendered alone by a
/// temporary camera that looks at it from the same direction as the user's camera; nothing else of the scene,
/// no HUD. Runs only when asked (Export): one extra render + JPG encode, no per-frame cost.
/// </summary>
public static class PartSnapshot
{
    private const int SnapshotLayer = 31; // unused layer, the part sits on it only during the render

    public static readonly Color Background = new Color(0.93f, 0.94f, 0.96f, 1f);

    /// <summary>JPG bytes of the part, or null when it has nothing visible.</summary>
    public static byte[] CaptureJpg(Transform part, Camera view, int width = 1024, int height = 768, int quality = 88)
    {
        if (part == null) return null;

        var renderers = new List<Renderer>();
        foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
            if (r.enabled && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer))
                renderers.Add(r);
        if (renderers.Count == 0) return null;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++) bounds.Encapsulate(renderers[i].bounds);
        float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);

        if (view == null) view = Camera.main;
        Quaternion rotation = view != null ? view.transform.rotation : Quaternion.LookRotation(new Vector3(-1f, -0.6f, 1f));

        // Put only the part on the snapshot layer (restored right after, same frame).
        var layers = new Dictionary<GameObject, int>();
        foreach (Renderer r in renderers)
        {
            if (layers.ContainsKey(r.gameObject)) continue;
            layers.Add(r.gameObject, r.gameObject.layer);
            r.gameObject.layer = SnapshotLayer;
        }

        var cameraObject = new GameObject("PartSnapshotCamera") { hideFlags = HideFlags.HideAndDontSave };
        var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 4 };
        RenderTexture target = RenderTexture.GetTemporary(descriptor);
        RenderTexture previousActive = RenderTexture.active;
        Texture2D picture = null;
        Camera camera = null;

        try
        {
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.useOcclusionCulling = false; // baked room occlusion must not hide the part
            camera.cullingMask = 1 << SnapshotLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.fieldOfView = 30f;
            camera.aspect = (float)width / height;

            // Fit the bounding sphere in the narrower field of view, with a little margin.
            float vertical = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float horizontal = Mathf.Atan(Mathf.Tan(vertical) * camera.aspect);
            float distance = radius / Mathf.Sin(Mathf.Min(vertical, horizontal)) * 1.08f;

            camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 1.5f);
            camera.farClipPlane = distance + radius * 1.5f;
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply(false);
            return ImageConversion.EncodeToJPG(picture, quality);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[PartSnapshot] Could not capture the part: " + e.Message);
            return null;
        }
        finally
        {
            foreach (KeyValuePair<GameObject, int> entry in layers)
                if (entry.Key != null) entry.Key.layer = entry.Value;

            RenderTexture.active = previousActive;
            if (camera != null) camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            if (picture != null) Object.Destroy(picture);
            Object.Destroy(cameraObject);
        }
    }
}
