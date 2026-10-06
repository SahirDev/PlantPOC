using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Thermal Plant, lighting:
///   10. Prepare Lighting In All Scenes – makes baked lights actually show:
///         - fixed geometry gets Static "Contribute GI" (only that flag: nothing becomes batching-static,
///           so nothing is frozen in place)
///         - everything that moves (explodable equipment, turbines, boiler fluid, worker, particles,
///           animated objects) stays dynamic and is lit by an automatic Light Probe Group instead
///         - models without lightmap UVs get "Generate Lightmap UVs"
///         - a switched-off "Lighting" object that holds baked lights is switched back on
///   11. Bake Lighting In All Scenes – Generate Lighting for every scene, one after another, and save.
/// Baked lights only light objects marked Contribute GI (through lightmaps) or through light probes;
/// that is why lights looked "on" but had no effect.
/// </summary>
public static partial class ThermalPlantTools
{
    private const float ProbeSpacing = 2.5f;
    private const int MaxProbesPerScene = 4000;

    /// <summary>Components whose object (and everything under it) moves at runtime: never static.</summary>
    private static readonly Type[] MovingRoots =
    {
        typeof(ModularExplodedView), typeof(ExplodableViewNode), typeof(TurbineData), typeof(SpinObjects),
        typeof(BoilerFluidController), typeof(WaterSurfaceMesh), typeof(SplineParticleFlow),
        typeof(Animator), typeof(Animation), typeof(Rigidbody), typeof(CharacterMovementController), typeof(Player),
        typeof(ParticleSystem), typeof(Canvas), typeof(MiniMapArea)
    };

    [MenuItem(MenuRoot + "10. Prepare Lighting In All Scenes", priority = 10)]
    private static void PrepareLightingInAllScenes()
    {
        if (!EditorUtility.DisplayDialog("Prepare lighting",
                "For Main_Scene, BoilerRoom, TurbineRoom and Control_Room:\n\n" +
                "- fixed geometry -> Static: Contribute GI (gets the baked light)\n" +
                "- moving things (explodable equipment, turbines, boiler fluid, worker, particles, animated objects) " +
                "stay dynamic and get a Light Probe Group\n" +
                "- models without lightmap UVs -> Generate Lightmap UVs\n" +
                "- a switched-off 'Lighting' object with baked lights is switched back on\n\n" +
                "Each scene is saved. Then run 11. Bake Lighting In All Scenes. Continue?", "Continue", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder();
        var modelsToFix = new HashSet<string>();

        foreach (string sceneName in DownloadableScenes)
        {
            string path = FindScenePath(sceneName);
            if (path == null) continue;

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            log.AppendLine($"== {sceneName}");
            PrepareLighting(scene, log, modelsToFix);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // Lightmap UVs for static meshes that have none (FBX/OBJ models; glTF models already make them).
        if (modelsToFix.Count > 0)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string modelPath in modelsToFix)
                {
                    if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer) || importer.generateSecondaryUV) continue;
                    importer.generateSecondaryUV = true;
                    importer.SaveAndReimport();
                    log.AppendLine($"+ Generate Lightmap UVs: {modelPath}");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        Report("Prepare lighting", log, "Next: 11. Bake Lighting In All Scenes (takes a while). " +
                                        "If a room is too bright afterwards, lower the spot lights' Intensity (3-5) and bake again.");
    }

    private static void PrepareLighting(Scene scene, StringBuilder log, HashSet<string> modelsToFix)
    {
        // 1. Switched-off "Lighting" objects that hold baked / mixed lights.
        foreach (Transform t in FindAllInScene<Transform>(scene))
        {
            if (t.gameObject.activeSelf || t.name != "Lighting") continue;
            bool hasBakedLights = false;
            foreach (Light light in t.GetComponentsInChildren<Light>(true))
                if (light.lightmapBakeType != LightmapBakeType.Realtime) hasBakedLights = true;
            if (!hasBakedLights) continue;

            t.gameObject.SetActive(true);
            log.AppendLine($"  + switched on '{GetPath(t.gameObject)}' (baked lights)");
        }

        // 2. Static Contribute GI on fixed geometry; moving things stay dynamic.
        int madeStatic = 0, keptDynamic = 0;
        var movingBounds = new List<Bounds>();

        foreach (MeshRenderer renderer in FindAllInScene<MeshRenderer>(scene))
        {
            GameObject go = renderer.gameObject;

            if (IsMoving(go))
            {
                keptDynamic++;
                if (go.activeInHierarchy) movingBounds.Add(renderer.bounds);
                SetContributeGI(go, false);
                ClearBatchingStatic(go); // it must be able to move
                continue;
            }

            if (SetContributeGI(go, true)) madeStatic++;

            MeshFilter filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && !filter.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1))
            {
                string modelPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (!string.IsNullOrEmpty(modelPath) && AssetImporter.GetAtPath(modelPath) is ModelImporter) modelsToFix.Add(modelPath);
            }
        }

        log.AppendLine($"  + {madeStatic} objects set to Contribute GI, {keptDynamic} moving objects kept dynamic");

        // 3. Light probes around the moving things (only if the scene has none yet).
        bool hasProbes = false;
        foreach (LightProbeGroup _ in FindAllInScene<LightProbeGroup>(scene)) { hasProbes = true; break; }

        if (hasProbes)
        {
            log.AppendLine("  = Light Probe Group already there - kept");
        }
        else if (movingBounds.Count > 0)
        {
            int count = CreateLightProbes(scene, movingBounds);
            log.AppendLine($"  + Light Probe Group with {count} probes around the moving equipment");
        }

        // 4. Lights: report what will (not) bake.
        int baked = 0, realtime = 0, off = 0;
        foreach (Light light in FindAllInScene<Light>(scene))
        {
            if (!light.isActiveAndEnabled) { off++; continue; }
            if (light.lightmapBakeType == LightmapBakeType.Realtime) realtime++; else baked++;
        }
        log.AppendLine($"  = lights: {baked} baked/mixed, {realtime} realtime, {off} switched off");
    }

    private static bool IsMoving(GameObject go)
    {
        if (go.GetComponent<SkinnedMeshRenderer>() != null) return true;

        foreach (Type type in MovingRoots)
            if (go.GetComponentInParent(type, true) != null) return true;

        return false;
    }

    /// <summary>Sets / clears only the Contribute GI static flag. Returns true when it was changed to on.</summary>
    private static bool SetContributeGI(GameObject go, bool on)
    {
        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
        bool has = (flags & StaticEditorFlags.ContributeGI) != 0;
        if (has == on) return false;

        flags = on ? flags | StaticEditorFlags.ContributeGI : flags & ~StaticEditorFlags.ContributeGI;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
        return on;
    }

    private static void ClearBatchingStatic(GameObject go)
    {
        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
        if ((flags & StaticEditorFlags.BatchingStatic) != 0)
            GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.BatchingStatic);
    }

    /// <summary>A grid of probes (every ProbeSpacing m, 3 heights) over the area of the moving objects.</summary>
    private static int CreateLightProbes(Scene scene, List<Bounds> moving)
    {
        Bounds area = moving[0];
        foreach (Bounds b in moving) area.Encapsulate(b);
        area.Expand(new Vector3(2f, 1f, 2f));

        float spacing = ProbeSpacing;
        int nx, nz;
        while (true)
        {
            nx = Mathf.Max(2, Mathf.CeilToInt(area.size.x / spacing) + 1);
            nz = Mathf.Max(2, Mathf.CeilToInt(area.size.z / spacing) + 1);
            if (nx * nz * 3 <= MaxProbesPerScene) break;
            spacing *= 1.5f;
        }

        float[] heights = { area.min.y + 0.5f, area.center.y, area.max.y - 0.5f };
        var positions = new List<Vector3>(nx * nz * 3);
        for (int ix = 0; ix < nx; ix++)
        for (int iz = 0; iz < nz; iz++)
        foreach (float y in heights)
        {
            Vector3 p = new Vector3(area.min.x + area.size.x * ix / (nx - 1), y, area.min.z + area.size.z * iz / (nz - 1));
            // Only near moving things: skip far corners of a big area.
            foreach (Bounds b in moving)
            {
                Bounds near = b;
                near.Expand(spacing * 2f);
                if (near.Contains(p)) { positions.Add(p); break; }
            }
        }

        var go = new GameObject("Light Probes (auto)");
        SceneManager.MoveGameObjectToScene(go, scene);
        LightProbeGroup group = go.AddComponent<LightProbeGroup>();
        group.probePositions = positions.ToArray();
        return positions.Count;
    }

    [MenuItem(MenuRoot + "11. Bake Lighting In All Scenes", priority = 11)]
    private static void BakeLightingInAllScenes()
    {
        if (!EditorUtility.DisplayDialog("Bake lighting",
                "Generate Lighting for Main_Scene, BoilerRoom, TurbineRoom and Control_Room, one after another, and save each.\n\n" +
                "This can take several minutes per scene; Unity is busy meanwhile. Continue?", "Bake", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder();
        foreach (string sceneName in DownloadableScenes)
        {
            string path = FindScenePath(sceneName);
            if (path == null) continue;

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            float start = (float)EditorApplication.timeSinceStartup;
            bool ok = Lightmapping.Bake();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"{(ok ? "+" : "!")} {sceneName}: {(ok ? "baked" : "bake failed / cancelled")} in {EditorApplication.timeSinceStartup - start:0}s, {LightmapSettings.lightmaps.Length} lightmaps");
        }

        Report("Bake lighting", log, "Check each room in Play mode. Too bright? Lower the light Intensity and bake that scene again.");
    }
}
