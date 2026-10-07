using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Thermal Plant, Main_Scene visuals:
///   12. Create Day-Night + Street Lamps (Main_Scene)
///   13. Create Steam-Cycle Flow View (Main_Scene)
/// Both build everything from the real building positions, put it under "PlantVisuals" and save the scene.
/// Run again to rebuild (the old lamps / flows are replaced). Move lamps or flow points freely afterwards.
/// </summary>
public static partial class ThermalPlantTools
{
    private const string VisualsFolder = "Assets/Settings/PlantVisuals";
    private const string MultiplyShaderPath = "Assets/Shaders/PlantVisuals/ScreenMultiply.shader";
    private const string GlowShaderPath = "Assets/Shaders/PlantVisuals/AdditiveGlow.shader";
    private const string LampPrefabPath = "Assets/Prefabs/StreetLamp.prefab";
    private const string VisualsRootName = "PlantVisuals";

    // Buildings that get street lamps (direct children of MainScene_Parent). Others are skipped.
    private static readonly string[] LampSkipNames = { "Environment", "PlantSupporting assets", "pipe_pieces", "High Power Lines" };

    // ================================================================== 12. day / night + lamps

    [MenuItem(MenuRoot + "12. Create Day-Night + Street Lamps (Main_Scene)", priority = 12)]
    private static void CreateDayNightAndLamps()
    {
        if (!EditorUtility.DisplayDialog("Day / Night + street lamps",
                "In Main_Scene this creates:\n\n" +
                "- PlantVisuals with Screen Tint Overlay + Day Night Controller (N key cycles Day / Evening / Night)\n" +
                "- a StreetLamp prefab and lamps at the corners of the main buildings (old ones are replaced)\n\n" +
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

        // Lamps
        Transform old = root.transform.Find("Street Lamps");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var lampsRoot = new GameObject("Street Lamps").transform;
        lampsRoot.SetParent(root.transform, false);

        int count = 0;
        var placed = new List<Vector3>();
        foreach (GameObject building in LampBuildings(scene))
        {
            if (!TryGetBounds(building, out Bounds b)) continue;
            Vector3 size = b.size;
            if (Mathf.Max(size.x, size.z) < 6f || Mathf.Max(size.x, size.z) > 160f) continue;

            const float margin = 2.5f;
            var corners = new[]
            {
                new Vector3(b.min.x - margin, 0f, b.min.z - margin), new Vector3(b.max.x + margin, 0f, b.min.z - margin),
                new Vector3(b.min.x - margin, 0f, b.max.z + margin), new Vector3(b.max.x + margin, 0f, b.max.z + margin)
            };

            foreach (Vector3 corner in corners)
            {
                Vector3 p = corner;
                p.y = GroundHeight(p, b);
                if (placed.Exists(q => (q - p).sqrMagnitude < 10f * 10f)) continue;

                var lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefab, scene);
                lamp.transform.SetParent(lampsRoot, true);
                Vector3 toCenter = b.center - p; toCenter.y = 0f;
                lamp.transform.SetPositionAndRotation(p, toCenter.sqrMagnitude > 0.01f
                    ? Quaternion.FromToRotation(Vector3.right, toCenter.normalized) : Quaternion.identity);
                lamp.name = $"StreetLamp {building.name} {count + 1}";

                Transform body = lamp.transform.Find("Body");
                if (body != null) GameObjectUtility.SetStaticEditorFlags(body.gameObject, StaticEditorFlags.BatchingStatic);

                placed.Add(p);
                count++;
            }
        }

        // Controller
        var dayNight = root.GetComponent<DayNightController>();
        if (dayNight == null) dayNight = root.AddComponent<DayNightController>();
        var so = new SerializedObject(dayNight);
        so.FindProperty("lampsRoot").objectReferenceValue = lampsRoot;
        so.FindProperty("bulbMaterial").objectReferenceValue = m.bulb;
        so.FindProperty("poolMaterial").objectReferenceValue = m.pool;
        so.ApplyModifiedPropertiesWithoutUndo();

        log.AppendLine($"+ {count} street lamps under PlantVisuals/Street Lamps");
        log.AppendLine("+ PlantVisuals: Screen Tint Overlay + Day Night Controller");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Report("Day / Night + street lamps", log,
            "Press Play and press N (Day -> Evening -> Night). Move / delete lamps freely, change colours on Day Night Controller. " +
            "React: SetTimeOfDay_Extern(\"day\" | \"evening\" | \"night\").");
    }

    // ================================================================== 13. steam-cycle flow view

    [MenuItem(MenuRoot + "13. Create Steam-Cycle Flow View (Main_Scene)", priority = 13)]
    private static void CreateSteamCycleFlowView()
    {
        if (!EditorUtility.DisplayDialog("Steam-cycle flow view",
                "In Main_Scene this creates glowing flow lines (visible through the buildings) and labels:\n\n" +
                "  Boiler -> Turbine (steam, white)\n  Turbine -> Condenser / Cooling (exhaust steam)\n" +
                "  Condenser -> Boiler (water, blue)\n  Turbine -> Power lines (electricity, yellow)\n\n" +
                "F key toggles it. Old flow view is replaced, the scene is saved. Continue?", "Create", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = OpenMainScene();
        if (!scene.IsValid()) return;

        var log = new StringBuilder();
        Materials m = EnsureVisualAssets(log);
        if (m == null) return;

        GameObject root = EnsureVisualsRoot(scene, m);

        GameObject boiler = FindByName(scene, "Boiler Room");
        GameObject turbine = FindByName(scene, "Turbine Room");
        GameObject cooling = FindByName(scene, "Cooling_Unit");
        GameObject grid = FindByName(scene, "High Power Lines");

        foreach (var (go, n) in new[] { (boiler, "Boiler Room"), (turbine, "Turbine Room"), (cooling, "Cooling_Unit"), (grid, "High Power Lines") })
            log.AppendLine(go != null ? $"= found {n}" : $"! {n} not found - its flow / label is skipped");

        Transform old = root.transform.Find("Steam Cycle Flows");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Transform oldAnchors = root.transform.Find("Flow Labels");
        if (oldAnchors != null) Object.DestroyImmediate(oldAnchors.gameObject);

        var flowsRoot = new GameObject("Steam Cycle Flows");
        flowsRoot.transform.SetParent(root.transform, false);
        var anchorsRoot = new GameObject("Flow Labels").transform;
        anchorsRoot.SetParent(root.transform, false);

        Texture2D steamTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Material/SplineFlow/Tex_SteamFlow.png");
        Texture2D arrowTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Material/SplineFlow/Tex_ArrowFlow.png");
        Texture2D electricTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Material/SplineFlow/Tex_ElectricFlow.png");

        int flows = 0;
        if (AddFlow(flowsRoot.transform, "1 Steam (Boiler -> Turbine)", boiler, turbine, 0f, steamTex, new Color(0.95f, 0.97f, 1f, 0.95f), 1.4f, 1.6f, 3f, m.flow)) flows++;
        if (AddFlow(flowsRoot.transform, "2 Exhaust Steam (Turbine -> Condenser)", turbine, cooling, 0f, steamTex, new Color(0.65f, 0.85f, 1f, 0.9f), 1.1f, 1.3f, 3f, m.flow)) flows++;
        if (AddFlow(flowsRoot.transform, "3 Water (Condenser -> Boiler)", cooling, boiler, 7f, arrowTex, new Color(0.15f, 0.55f, 1f, 1f), 1.0f, 2.5f, 6f, m.flow)) flows++;
        if (AddFlow(flowsRoot.transform, "4 Power (Generator -> Grid)", turbine, grid, -6f, electricTex, new Color(1f, 0.85f, 0.2f, 1f), 0.9f, 4f, 5f, m.flow)) flows++;

        var stations = new List<(string title, string subtitle, GameObject go, Color color)>
        {
            ("Boiler", "Fuel heats water into high-pressure steam", boiler, new Color(1f, 0.6f, 0.3f)),
            ("Steam Turbine", "Steam spins the turbine and generator", turbine, Color.white),
            ("Condenser / Cooling", "Used steam is cooled back into water", cooling, new Color(0.5f, 0.8f, 1f)),
            ("Power Grid", "Electricity leaves the plant", grid, new Color(1f, 0.85f, 0.2f))
        };

        var view = root.GetComponent<SteamCycleFlowView>();
        if (view == null) view = root.AddComponent<SteamCycleFlowView>();
        var so = new SerializedObject(view);
        so.FindProperty("flowsRoot").objectReferenceValue = flowsRoot;
        SerializedProperty list = so.FindProperty("stations");
        list.arraySize = 0;
        foreach (var s in stations)
        {
            if (s.go == null || !TryGetBounds(s.go, out Bounds b)) continue;

            var anchor = new GameObject("Label " + s.title).transform;
            anchor.SetParent(anchorsRoot, false);
            anchor.position = new Vector3(b.center.x, b.max.y + 4f, b.center.z);

            int i = list.arraySize;
            list.arraySize++;
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("title").stringValue = s.title;
            item.FindPropertyRelative("subtitle").stringValue = s.subtitle;
            item.FindPropertyRelative("anchor").objectReferenceValue = anchor;
            item.FindPropertyRelative("color").colorValue = s.color;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        log.AppendLine($"+ {flows} flow lines under PlantVisuals/Steam Cycle Flows, {list.arraySize} labels");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Report("Steam-cycle flow view", log,
            "Press Play and press F. Adjust a line by moving its Start / Bend / End children (the lines are visible in the " +
            "Scene view while PlantVisuals/Steam Cycle Flows is active). React: SetFlowView_Extern(\"true\" | \"false\").");
    }

    // ================================================================== helpers

    private class Materials
    {
        public Material tint, bulb, pool, pole, flow;
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

        Texture2D poolTexture = EnsurePoolTexture(log);

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
            flow = EnsureMaterial("FlowOnTop", glow, log, mat =>
            {
                mat.SetFloat("_ZTest", (float)CompareFunction.Always); // visible through buildings
                mat.SetFloat("_Intensity", 1.3f);
                mat.renderQueue = (int)RenderQueue.Transparent + 700;
            }),
            pole = EnsureMaterial("LampPole", Shader.Find("Universal Render Pipeline/Lit"), log, mat =>
            {
                mat.SetColor("_BaseColor", new Color(0.22f, 0.23f, 0.25f));
                mat.SetFloat("_Smoothness", 0.25f);
                mat.SetFloat("_Metallic", 0.3f);
            })
        };

        foreach (Material mat in new[] { m.bulb, m.pool, m.pole }) if (mat != null) mat.enableInstancing = true;
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

    // Soft round light spot for the ground under each lamp.
    private static Texture2D EnsurePoolTexture(StringBuilder log)
    {
        string path = $"{VisualsFolder}/LightPool.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
            float a = Mathf.Pow(1f - d, 2.2f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
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

    private static IEnumerable<GameObject> LampBuildings(Scene scene)
    {
        GameObject parent = FindByName(scene, "MainScene_Parent");
        if (parent == null) yield break;

        foreach (Transform child in parent.transform)
        {
            if (!child.gameObject.activeInHierarchy) continue;
            if (System.Array.IndexOf(LampSkipNames, child.name) >= 0) continue;
            yield return child.gameObject;
        }
    }

    private static GameObject FindByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        }
        return null;
    }

    private static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static float GroundHeight(Vector3 p, Bounds building)
    {
        Physics.SyncTransforms();
        var origin = new Vector3(p.x, building.max.y + 30f, p.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, building.size.y + 80f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
            if (hit.point.y <= building.min.y + 1.5f && hit.point.y > best) best = hit.point.y; // ground, not a roof
        return float.IsNegativeInfinity(best) ? building.min.y : best;
    }

    private static bool AddFlow(Transform parent, string name, GameObject from, GameObject to, float sideOffset,
        Texture2D texture, Color color, float size, float speed, float density, Material material)
    {
        if (from == null || to == null || !TryGetBounds(from, out Bounds a) || !TryGetBounds(to, out Bounds b)) return false;

        Vector3 start = new Vector3(a.center.x, a.max.y + 1.5f, a.center.z);
        Vector3 end = new Vector3(b.center.x, b.max.y + 1.5f, b.center.z);
        float arc = Mathf.Max(start.y, end.y) + 6f;

        Vector3 flat = end - start; flat.y = 0f;
        Vector3 side = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) * sideOffset : Vector3.zero;

        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.SetParent(parent, false);

        var positions = new[]
        {
            start + side * 0.5f,
            Vector3.Lerp(start, end, 0.25f) + side + Vector3.up * (arc - Mathf.Lerp(start.y, end.y, 0.25f)),
            Vector3.Lerp(start, end, 0.75f) + side + Vector3.up * (arc - Mathf.Lerp(start.y, end.y, 0.75f)),
            end + side * 0.5f
        };
        string[] names = { "Start (0)", "Bend (1)", "Bend (2)", "End (3)" };

        var pointTransforms = new List<Transform>();
        for (int i = 0; i < positions.Length; i++)
        {
            var point = new GameObject(names[i]).transform;
            point.SetParent(go.transform, false);
            point.position = positions[i];
            pointTransforms.Add(point);
        }

        var flow = go.AddComponent<SplineParticleFlow>();
        var so = new SerializedObject(flow);
        so.FindProperty("flowTexture").objectReferenceValue = texture;
        so.FindProperty("color").colorValue = color;
        so.FindProperty("size").floatValue = size;
        so.FindProperty("speed").floatValue = speed;
        so.FindProperty("density").floatValue = density;
        so.FindProperty("materialTemplate").objectReferenceValue = material;
        SerializedProperty points = so.FindProperty("points");
        points.arraySize = pointTransforms.Count;
        for (int i = 0; i < pointTransforms.Count; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = pointTransforms[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        go.SetActive(true);
        return true;
    }
}
