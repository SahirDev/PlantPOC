using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Thermal Plant > 12. Create Day-Night + Building Lights (Main_Scene)
///
/// Builds the night lighting for every building of the plant overview (each ObjectInfo with its BoxCollider,
/// the same boxes used for selection - exact position, size and angle):
///   - buildings: street lamps at the corners, warm floodlight glow on every wall and a soft light on the roof
///   - parking: lamps around the edge
///   - chimneys: uplight glow around the bottom of the chimney + a light pool at its base
/// Plus a lighter night tint ("moonlight") so unlit parts stay readable.
/// Everything goes under PlantVisuals/Building Lights (replaced on every run). No real lights, no lightmaps.
/// Also removes the old steam-cycle flow view if it is still in the scene.
/// </summary>
public static partial class ThermalPlantTools
{
    private const string VisualsFolder = "Assets/Settings/PlantVisuals";
    private const string MultiplyShaderPath = "Assets/Shaders/PlantVisuals/ScreenMultiply.shader";
    private const string GlowShaderPath = "Assets/Shaders/PlantVisuals/AdditiveGlow.shader";
    private const string LampPrefabPath = "Assets/Prefabs/StreetLamp.prefab";
    private const string VisualsRootName = "PlantVisuals";
    private const string LightsRootName = "Building Lights";

    private enum LightKind { Building, Area, Chimney }

    // Small buildings (largest side below this) get wall / roof glow but no corner lamps.
    private const float MinLampBuildingSize = 8f;

    [MenuItem(MenuRoot + "12. Create Day-Night + Building Lights (Main_Scene)", priority = 12)]
    private static void CreateDayNightAndLights()
    {
        if (!EditorUtility.DisplayDialog("Day / Night + building lights",
                "In Main_Scene this creates / rebuilds:\n\n" +
                "- PlantVisuals with Screen Tint Overlay + Day Night Controller (N key: Day / Evening / Night)\n" +
                "- lights for every overview building (ObjectInfo + box collider): lamps, glow on the walls, " +
                "a soft light on the roof; parking lamps; glow at the bottom of the chimneys\n" +
                "- removes the old steam-cycle flow view if it is there\n\n" +
                "No real lights and no extra lightmaps (cheap on WebGL). The scene is saved. Continue?", "Create", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = OpenMainScene();
        if (!scene.IsValid()) return;

        var log = new StringBuilder();
        Materials m = EnsureVisualAssets(log);
        if (m == null) return;

        GameObject lampPrefab = EnsureLampPrefab(m, log);
        GameObject root = EnsureVisualsRoot(scene, m);
        RemoveOldVisuals(root, log);

        var lightsRoot = new GameObject(LightsRootName).transform;
        lightsRoot.SetParent(root.transform, false);

        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        int lamps = 0, washes = 0;

        var seen = new HashSet<GameObject>();
        foreach (ObjectInfo info in FindAllInScene<ObjectInfo>(scene))
        {
            if (info == null || !info.gameObject.activeInHierarchy || !seen.Add(info.gameObject)) continue;

            string label = info.DisplayName;
            BoxCollider box = info.GetComponent<BoxCollider>();
            Bounds local;
            Matrix4x4 toWorld;
            Quaternion rotation;
            bool ok = box != null
                ? TryGetBoxBounds(box, out local, out toWorld, out rotation)
                : TryGetOrientedBounds(info.gameObject, out local, out toWorld, out rotation);
            if (!ok)
            {
                log.AppendLine($"! {label}: no box collider / mesh - skipped");
                continue;
            }

            string key = (label + " " + info.gameObject.name).ToLowerInvariant();
            LightKind kind = key.Contains("chimney") ? LightKind.Chimney : key.Contains("parking") ? LightKind.Area : LightKind.Building;

            var group = new GameObject(label).transform;
            group.SetParent(lightsRoot, false);
            int l0 = lamps, w0 = washes;

            switch (kind)
            {
                case LightKind.Building:
                    if (Mathf.Max(local.size.x, local.size.z) >= MinLampBuildingSize)
                        lamps += PlaceCornerLamps(lampPrefab, scene, group, local, toWorld, 2.5f, false);
                    washes += PlaceWallWashes(group, quad, m.wash, local, toWorld, rotation);
                    washes += PlaceRoofLight(group, quad, m.roof, local, toWorld, rotation);
                    break;
                case LightKind.Area:
                    lamps += PlaceCornerLamps(lampPrefab, scene, group, local, toWorld, 1.5f, true);
                    break;
                case LightKind.Chimney:
                    washes += PlaceChimneyUplights(group, quad, m.wash, m.pool, local, toWorld);
                    break;
            }

            log.AppendLine($"+ {label} ({kind}): {lamps - l0} lamps, {washes - w0} glows");
        }

        var dayNight = root.GetComponent<DayNightController>();
        if (dayNight == null) dayNight = root.AddComponent<DayNightController>();
        var so = new SerializedObject(dayNight);
        so.FindProperty("lampsRoot").objectReferenceValue = lightsRoot;
        so.FindProperty("bulbMaterial").objectReferenceValue = m.bulb;
        so.FindProperty("poolMaterial").objectReferenceValue = m.pool;
        so.FindProperty("washMaterial").objectReferenceValue = m.wash;
        so.FindProperty("roofMaterial").objectReferenceValue = m.roof;
        so.FindProperty("nightTint").colorValue = new Color(0.27f, 0.31f, 0.47f, 1f); // a little "moonlight"
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Report("Day / Night + building lights", log,
            "Press Play, press N for Evening / Night. Move or delete any lamp / glow under PlantVisuals/Building Lights; " +
            "colours and brightness are on Day Night Controller. React: SetTimeOfDay_Extern(\"day\" | \"evening\" | \"night\").");
    }

    // ================================================================== placing lights

    // Lamps at the 4 corners (and edge middles for areas), turned towards the middle of the building.
    private static int PlaceCornerLamps(GameObject prefab, Scene scene, Transform parent, Bounds local, Matrix4x4 toWorld,
        float margin, bool edgeMiddles)
    {
        var spots = new List<Vector3>
        {
            new Vector3(local.min.x - margin, local.min.y, local.min.z - margin), new Vector3(local.max.x + margin, local.min.y, local.min.z - margin),
            new Vector3(local.min.x - margin, local.min.y, local.max.z + margin), new Vector3(local.max.x + margin, local.min.y, local.max.z + margin)
        };
        if (edgeMiddles)
        {
            spots.Add(new Vector3(local.center.x, local.min.y, local.min.z - margin));
            spots.Add(new Vector3(local.center.x, local.min.y, local.max.z + margin));
            spots.Add(new Vector3(local.min.x - margin, local.min.y, local.center.z));
            spots.Add(new Vector3(local.max.x + margin, local.min.y, local.center.z));
        }

        Vector3 center = toWorld.MultiplyPoint3x4(local.center);
        int count = 0;
        foreach (Vector3 spot in spots)
        {
            Vector3 p = toWorld.MultiplyPoint3x4(spot);
            p.y = GroundHeight(p, center, toWorld.MultiplyPoint3x4(local.min).y);

            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            lamp.transform.SetParent(parent, true);
            Vector3 toCenter = center - p; toCenter.y = 0f;
            lamp.transform.SetPositionAndRotation(p, toCenter.sqrMagnitude > 0.01f
                ? Quaternion.FromToRotation(Vector3.right, toCenter.normalized) : Quaternion.identity);
            lamp.name = $"Lamp {count + 1}";

            Transform body = lamp.transform.Find("Body");
            if (body != null) GameObjectUtility.SetStaticEditorFlags(body.gameObject, StaticEditorFlags.BatchingStatic);
            count++;
        }
        return count;
    }

    // A warm glow on each of the 4 walls, from the ground up (like floodlights shining on the building).
    private static int PlaceWallWashes(Transform parent, Mesh quad, Material wash, Bounds local, Matrix4x4 toWorld, Quaternion rotation)
    {
        float height = Mathf.Clamp(local.size.y * 0.75f, 3f, 14f);
        float y = local.min.y + height * 0.5f;
        const float offset = 0.25f;

        var walls = new[]
        {
            (pos: new Vector3(local.center.x, y, local.max.z + offset), width: local.size.x, normal: Vector3.forward),
            (pos: new Vector3(local.center.x, y, local.min.z - offset), width: local.size.x, normal: Vector3.back),
            (pos: new Vector3(local.max.x + offset, y, local.center.z), width: local.size.z, normal: Vector3.right),
            (pos: new Vector3(local.min.x - offset, y, local.center.z), width: local.size.z, normal: Vector3.left)
        };

        int count = 0;
        foreach (var wall in walls)
        {
            Vector3 worldNormal = rotation * wall.normal;
            CreateGlowQuad(parent, $"Wall Glow {count + 1}", quad, wash, toWorld.MultiplyPoint3x4(wall.pos),
                Quaternion.LookRotation(-worldNormal, Vector3.up), new Vector3(wall.width * 0.95f, height, 1f));
            count++;
        }
        return count;
    }

    // Soft light on the roof (seen from the overview camera), a little above it so it never flickers.
    private static int PlaceRoofLight(Transform parent, Mesh quad, Material roof, Bounds local, Matrix4x4 toWorld, Quaternion rotation)
    {
        Vector3 top = toWorld.MultiplyPoint3x4(new Vector3(local.center.x, local.max.y + 0.15f, local.center.z));
        CreateGlowQuad(parent, "Roof Light", quad, roof, top, rotation * Quaternion.Euler(90f, 0f, 0f),
            new Vector3(local.size.x, local.size.z, 1f));
        return 1;
    }

    // Glow around the bottom of the chimney (6 panels round it) + a light pool on the ground.
    private static int PlaceChimneyUplights(Transform parent, Mesh quad, Material wash, Material pool, Bounds local, Matrix4x4 toWorld)
    {
        float radius = Mathf.Min(local.extents.x, local.extents.z);
        float height = Mathf.Clamp(local.size.y * 0.18f, 4f, 18f);
        Vector3 baseCenter = toWorld.MultiplyPoint3x4(new Vector3(local.center.x, local.min.y, local.center.z));
        const int panels = 6;
        float panelWidth = 2f * Mathf.PI * radius / panels * 1.05f;

        for (int i = 0; i < panels; i++)
        {
            float angle = i * Mathf.PI * 2f / panels;
            var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 pos = baseCenter + outward * (radius + 0.3f) + Vector3.up * (height * 0.5f);
            CreateGlowQuad(parent, $"Chimney Glow {i + 1}", quad, wash, pos, Quaternion.LookRotation(-outward, Vector3.up),
                new Vector3(panelWidth, height, 1f));
        }

        float poolSize = radius * 2f + 10f;
        CreateGlowQuad(parent, "Base Light Pool", quad, pool, baseCenter + Vector3.up * 0.08f, Quaternion.Euler(90f, 0f, 0f),
            new Vector3(poolSize, poolSize, 1f));
        return panels + 1;
    }

    private static void CreateGlowQuad(Transform parent, string name, Mesh quad, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // ================================================================== cleanup

    private static void RemoveOldVisuals(GameObject root, StringBuilder log)
    {
        foreach (string child in new[] { LightsRootName, "Street Lamps", "Steam Cycle Flows", "Flow Labels" })
        {
            Transform t = root.transform.Find(child);
            if (t == null) continue;
            Object.DestroyImmediate(t.gameObject);
            if (child != LightsRootName) log.AppendLine($"- removed old '{child}'");
        }

        // The flow view script was deleted: its component is now a missing script on PlantVisuals.
        int missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
        if (missing > 0) log.AppendLine($"- removed {missing} missing script(s) from PlantVisuals (old flow view)");

        if (AssetDatabase.LoadAssetAtPath<Material>($"{VisualsFolder}/FlowOnTop.mat") != null)
        {
            AssetDatabase.DeleteAsset($"{VisualsFolder}/FlowOnTop.mat");
            log.AppendLine($"- removed {VisualsFolder}/FlowOnTop.mat");
        }
    }

    // ================================================================== assets

    private class Materials
    {
        public Material tint, bulb, pool, pole, wash, roof;
    }

    private static Scene OpenMainScene()
    {
        string path = FindScenePath("Main_Scene");
        if (path == null)
        {
            EditorUtility.DisplayDialog("Main_Scene not found", "Could not find Main_Scene.unity.", "OK");
            return default;
        }

        Scene active = SceneManager.GetActiveScene();
        return active.path == path ? active : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }

    private static Materials EnsureVisualAssets(StringBuilder log)
    {
        var multiply = AssetDatabase.LoadAssetAtPath<Shader>(MultiplyShaderPath);
        var glow = AssetDatabase.LoadAssetAtPath<Shader>(GlowShaderPath);
        if (multiply == null || glow == null)
        {
            EditorUtility.DisplayDialog("Shaders missing", $"Expected {MultiplyShaderPath} and {GlowShaderPath}.", "OK");
            return null;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder(VisualsFolder)) AssetDatabase.CreateFolder("Assets/Settings", "PlantVisuals");

        Texture2D poolTexture = EnsureGlowTexture("LightPool.png", log, (u, v) =>
        {
            float dx = u * 2f - 1f, dy = v * 2f - 1f;
            return Mathf.Pow(1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
        });

        // Bright at the bottom, fading up; soft at the left / right edges.
        Texture2D washTexture = EnsureGlowTexture("WallWash.png", log, (u, v) =>
        {
            float vertical = Mathf.Pow(1f - v, 1.6f);
            float edges = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 6f);
            return vertical * edges;
        });

        // Soft rectangle: even in the middle, fading at all edges.
        Texture2D roofTexture = EnsureGlowTexture("RoofGlow.png", log, (u, v) =>
            Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 5f) * Mathf.SmoothStep(0f, 1f, Mathf.Min(v, 1f - v) * 5f));

        var m = new Materials
        {
            tint = EnsureMaterial("ScreenTint", multiply, log, mat => mat.SetColor("_Color", Color.white)),
            bulb = EnsureMaterial("LampBulb", glow, log, mat =>
            {
                mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                mat.SetFloat("_Intensity", 2f);
                mat.renderQueue = (int)RenderQueue.Transparent + 600;
            }),
            pool = EnsureMaterial("LampLightPool", glow, log, mat =>
            {
                mat.SetTexture("_MainTex", poolTexture);
                mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                mat.renderQueue = (int)RenderQueue.Transparent + 600;
            }),
            wash = EnsureMaterial("WallWash", glow, log, mat =>
            {
                mat.SetTexture("_MainTex", washTexture);
                mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                mat.renderQueue = (int)RenderQueue.Transparent + 600;
            }),
            roof = EnsureMaterial("RoofGlow", glow, log, mat =>
            {
                mat.SetTexture("_MainTex", roofTexture);
                mat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
                mat.renderQueue = (int)RenderQueue.Transparent + 600;
            }),
            pole = EnsureMaterial("LampPole", Shader.Find("Universal Render Pipeline/Lit"), log, mat =>
            {
                mat.SetColor("_BaseColor", new Color(0.22f, 0.23f, 0.25f));
                mat.SetFloat("_Smoothness", 0.25f);
                mat.SetFloat("_Metallic", 0.3f);
            })
        };

        foreach (Material mat in new[] { m.bulb, m.pool, m.pole, m.wash, m.roof }) if (mat != null) mat.enableInstancing = true;
        AssetDatabase.SaveAssets();
        return m;
    }

    private static Material EnsureMaterial(string name, Shader shader, StringBuilder log, System.Action<Material> setup)
    {
        string path = $"{VisualsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(shader) { name = name };
        setup(mat);
        AssetDatabase.CreateAsset(mat, path);
        log.AppendLine($"+ {path}");
        return mat;
    }

    // White texture whose alpha comes from the function (u, v in 0..1).
    private static Texture2D EnsureGlowTexture(string fileName, StringBuilder log, System.Func<float, float, float> alpha)
    {
        string path = $"{VisualsFolder}/{fileName}";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha((x + 0.5f) / size, (y + 0.5f) / size))));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        log.AppendLine($"+ {path}");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Simple street lamp: one combined body mesh (pole + arm + head), a bulb and a light pool on the ground.
    private static GameObject EnsureLampPrefab(Materials m, StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
        if (existing != null) return existing;

        Mesh cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        var parts = new[]
        {
            new CombineInstance { mesh = cylinder, transform = Matrix4x4.TRS(new Vector3(0f, 0.15f, 0f), Quaternion.identity, new Vector3(0.45f, 0.15f, 0.45f)) },
            new CombineInstance { mesh = cylinder, transform = Matrix4x4.TRS(new Vector3(0f, 3f, 0f), Quaternion.identity, new Vector3(0.16f, 3f, 0.16f)) },
            new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(0.65f, 5.9f, 0f), Quaternion.identity, new Vector3(1.4f, 0.1f, 0.1f)) },
            new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(1.3f, 5.82f, 0f), Quaternion.identity, new Vector3(0.7f, 0.16f, 0.36f)) }
        };

        var body = new Mesh { name = "StreetLampBody" };
        body.CombineMeshes(parts, true, true);
        body.RecalculateBounds();
        AssetDatabase.CreateAsset(body, $"{VisualsFolder}/StreetLampBody.asset");

        var lamp = new GameObject("StreetLamp");

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(lamp.transform, false);
        bodyGo.AddComponent<MeshFilter>().sharedMesh = body;
        bodyGo.AddComponent<MeshRenderer>().sharedMaterial = m.pole;

        var bulb = new GameObject("Bulb");
        bulb.transform.SetParent(lamp.transform, false);
        bulb.transform.localPosition = new Vector3(1.3f, 5.72f, 0f);
        bulb.transform.localScale = new Vector3(0.55f, 0.1f, 0.28f);
        bulb.AddComponent<MeshFilter>().sharedMesh = sphere;
        var bulbRenderer = bulb.AddComponent<MeshRenderer>();
        bulbRenderer.sharedMaterial = m.bulb;
        bulbRenderer.shadowCastingMode = ShadowCastingMode.Off;
        bulbRenderer.receiveShadows = false;

        var pool = new GameObject("LightPool");
        pool.transform.SetParent(lamp.transform, false);
        pool.transform.localPosition = new Vector3(1.3f, 0.06f, 0f);
        pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        pool.transform.localScale = new Vector3(11f, 11f, 1f);
        pool.AddComponent<MeshFilter>().sharedMesh = quad;
        var poolRenderer = pool.AddComponent<MeshRenderer>();
        poolRenderer.sharedMaterial = m.pool;
        poolRenderer.shadowCastingMode = ShadowCastingMode.Off;
        poolRenderer.receiveShadows = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(lamp, LampPrefabPath);
        Object.DestroyImmediate(lamp);
        log.AppendLine($"+ {LampPrefabPath} (replace its Body mesh with a nicer lamp model any time)");
        return prefab;
    }

    private static GameObject EnsureVisualsRoot(Scene scene, Materials m)
    {
        GameObject root = null;
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == VisualsRootName) { root = go; break; }

        if (root == null)
        {
            root = new GameObject(VisualsRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        var overlay = root.GetComponent<ScreenTintOverlay>();
        if (overlay == null) overlay = root.AddComponent<ScreenTintOverlay>();
        var so = new SerializedObject(overlay);
        so.FindProperty("multiplyMaterial").objectReferenceValue = m.tint;
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // ================================================================== scene helpers

    /// <summary>The selection box of an overview building, in an upright frame turned like the building.</summary>
    private static bool TryGetBoxBounds(BoxCollider box, out Bounds local, out Matrix4x4 toWorld, out Quaternion rotation)
    {
        Transform t = box.transform;

        // Heading of the box: its most horizontal axis (models are often imported rotated -90 on X).
        Vector3 axis = t.right;
        foreach (Vector3 candidate in new[] { t.forward, t.up })
            if (Mathf.Abs(candidate.y) < Mathf.Abs(axis.y)) axis = candidate;
        axis.y = 0f;
        float yaw = axis.sqrMagnitude > 0.0001f ? Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg - 90f : 0f;

        rotation = Quaternion.Euler(0f, yaw, 0f);
        toWorld = Matrix4x4.TRS(t.TransformPoint(box.center), rotation, Vector3.one);
        Matrix4x4 toLocal = toWorld.inverse;

        local = default;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = box.center + Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 p = toLocal.MultiplyPoint3x4(t.TransformPoint(corner));
            if (i == 0) local = new Bounds(p, Vector3.zero);
            else local.Encapsulate(p);
        }
        return local.size.sqrMagnitude > 0.01f;
    }

    /// <summary>
    /// Bounds of the object in its own (rotated) space, so lamps and wall glows follow buildings that are
    /// turned at an angle. toWorld converts those local points to world space (rotation + position, no scale).
    /// </summary>
    private static bool TryGetOrientedBounds(GameObject go, out Bounds local, out Matrix4x4 toWorld, out Quaternion rotation)
    {
        rotation = Quaternion.Euler(0f, go.transform.eulerAngles.y, 0f); // upright, only the turn around Y
        toWorld = Matrix4x4.TRS(go.transform.position, rotation, Vector3.one);
        Matrix4x4 toLocal = toWorld.inverse;

        local = default;
        bool any = false;
        foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled) continue;
            MeshFilter filter = r.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            Bounds mb = filter.sharedMesh.bounds;
            Matrix4x4 meshToLocal = toLocal * r.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = meshToLocal.MultiplyPoint3x4(corner);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                else local.Encapsulate(p);
            }
        }
        return any;
    }

    private static float GroundHeight(Vector3 p, Vector3 buildingCenter, float buildingBottom)
    {
        Physics.SyncTransforms();
        var origin = new Vector3(p.x, buildingBottom + 60f, p.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 140f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
            if (hit.point.y <= buildingBottom + 1.5f && hit.point.y > best) best = hit.point.y; // ground, not a roof
        return float.IsNegativeInfinity(best) ? buildingBottom : best;
    }
}
