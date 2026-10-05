using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Thermal Plant, part 2:
///   5. Create MiniMap Panel        – the persistent minimap UI (+ the one EventSystem) in Bootstrap
///   6. Setup MiniMap In All Scenes  – one MiniMapArea (picture + position) per scene, no camera
///   7. Setup Object Info Card       – Main_Scene: card beside a clicked object (overview camera)
///   8. Reimport glTF Models         – after pulling: textures first, then the .glb models that use them
///   Build Mode                      – Simple Test Build (for the React team) / Addressables Build
/// Every step can be run again safely: what exists is kept and reported.
/// </summary>
public static partial class ThermalPlantTools
{
    private const string BuildModeMenu = MenuRoot + "Build Mode/";

    // Colours of the Figma screens (dark navy panels).
    private static readonly Color PanelColor = new Color(0.075f, 0.09f, 0.16f, 0.94f);
    private static readonly Color PanelInnerColor = new Color(0.12f, 0.14f, 0.23f, 1f);
    private static readonly Color ButtonColor = new Color(0.17f, 0.2f, 0.32f, 1f);
    private static readonly Color TitleColor = Color.white;
    private static readonly Color BodyColor = new Color(0.78f, 0.82f, 0.9f, 1f);
    private static readonly Color MutedColor = new Color(0.55f, 0.6f, 0.7f, 1f);

    // ===================================================================================== 5. MiniMap panel

    private const string MiniMapTextureFolder = "Assets/Texture/MiniMap";
    private const string WorkerIconPath = MiniMapTextureFolder + "/MiniMapWorkerIcon.png";

    [MenuItem(MenuRoot + "5. Create MiniMap Panel (Bootstrap)", priority = 5)]
    private static void CreateMiniMapPanel()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (!File.Exists(BootstrapPath))
        {
            EditorUtility.DisplayDialog("MiniMap panel", "Bootstrap scene not found. Run '1. Setup Addressables + Bootstrap' first.", "OK");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);
        var log = new StringBuilder();

        // One EventSystem for the whole session (the minimap buttons need it).
        EventSystem eventSystem = FindInScene<EventSystem>(scene);
        if (eventSystem == null)
        {
            var esObject = new GameObject("EventSystem");
            eventSystem = esObject.AddComponent<EventSystem>();
            esObject.AddComponent<InputSystemUIInputModule>();
            log.AppendLine("+ EventSystem (Input System UI module)");
        }
        else log.AppendLine("= EventSystem already there");

        MiniMapPanel existing = FindInScene<MiniMapPanel>(scene);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("MiniMap panel",
                    $"'{GetPath(existing.gameObject)}' already exists.\n\nRebuild it? (Needed once after the camera-less minimap update; your own styling on it is lost.)",
                    "Rebuild", "Keep"))
            {
                SetRef(existing, "eventSystem", eventSystem);
                log.AppendLine("= MiniMapPanel kept as it is");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Report("MiniMap panel", log, "");
                return;
            }

            Object.DestroyImmediate(existing.gameObject);
            log.AppendLine("- old MiniMapPanel removed");
        }

        Sprite icon = GetOrCreateWorkerIcon(log);
        BuildMiniMapPanel(eventSystem, icon);
        log.AppendLine("+ MiniMapCanvas: panel bottom-right (300 x 230), map image, worker icon, + / - / expand, north. Restyle freely; keep the references on MiniMapPanel.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Report("MiniMap panel", log, "Next: 6. Setup MiniMap In All Scenes.");
    }

    private static void BuildMiniMapPanel(EventSystem eventSystem, Sprite workerIconSprite)
    {
        GameObject root = CreateOverlayCanvas("MiniMapCanvas", 20, true);
        MiniMapPanel panelScript = root.AddComponent<MiniMapPanel>();

        // Panel – bottom-right corner like the Figma.
        RectTransform panel = NewUI("Panel", root.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-24f, 24f);
        panel.sizeDelta = new Vector2(300f, 230f);
        AddImage(panel, PanelColor);

        // Header: "Mini Map" + expand button.
        RectTransform title = NewUI("Title", panel);
        Anchor(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -32f), new Vector2(-44f, -6f));
        AddText(title, "Mini Map", 15, FontStyle.Bold, TitleColor, TextAnchor.MiddleLeft);

        Button expand = AddButton(panel, "ExpandButton", "[ ]", 12);
        RectTransform expandRect = (RectTransform)expand.transform;
        expandRect.anchorMin = expandRect.anchorMax = expandRect.pivot = new Vector2(1f, 1f);
        expandRect.anchoredPosition = new Vector2(-10f, -6f);
        expandRect.sizeDelta = new Vector2(26f, 26f);

        // Map frame (mask) -> map image -> worker icon.
        RectTransform mapFrame = NewUI("MapFrame", panel);
        Anchor(mapFrame, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -38f));
        AddImage(mapFrame, PanelInnerColor);
        mapFrame.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        RectTransform mapRect = NewUI("MapImage", mapFrame);
        mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = new Vector2(0.5f, 0.5f);
        mapRect.sizeDelta = new Vector2(280f, 182f);
        Image map = mapRect.gameObject.AddComponent<Image>();
        map.raycastTarget = true; // clicks on the map never reach the world

        RectTransform worker = NewUI("WorkerIcon", mapRect);
        worker.anchorMin = worker.anchorMax = worker.pivot = new Vector2(0.5f, 0.5f);
        worker.sizeDelta = new Vector2(22f, 22f);
        Image workerImage = worker.gameObject.AddComponent<Image>();
        workerImage.sprite = workerIconSprite;
        workerImage.preserveAspect = true;
        workerImage.raycastTarget = false;

        // North icon (top-right of the map).
        RectTransform north = NewUI("NorthIcon", mapFrame);
        north.anchorMin = north.anchorMax = new Vector2(1f, 1f);
        north.pivot = new Vector2(0.5f, 0.5f);
        north.anchoredPosition = new Vector2(-18f, -18f);
        north.sizeDelta = new Vector2(26f, 26f);
        Image northBg = AddImage(north, ButtonColor, AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"));
        northBg.type = Image.Type.Simple;
        northBg.raycastTarget = false;
        RectTransform northLabel = NewUI("N", north);
        Anchor(northLabel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddText(northLabel, "N", 13, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);

        // Zoom buttons (right side of the map).
        Button zoomIn = AddButton(mapFrame, "ZoomInButton", "+", 18);
        Button zoomOut = AddButton(mapFrame, "ZoomOutButton", "-", 18);
        PlaceRight((RectTransform)zoomIn.transform, 14f);
        PlaceRight((RectTransform)zoomOut.transform, -16f);

        SetRef(panelScript, "panel", panel);
        SetRef(panelScript, "mapFrame", mapFrame);
        SetRef(panelScript, "mapImage", map);
        SetRef(panelScript, "workerIcon", worker);
        SetRef(panelScript, "zoomInButton", zoomIn);
        SetRef(panelScript, "zoomOutButton", zoomOut);
        SetRef(panelScript, "expandButton", expand);
        SetRef(panelScript, "northIcon", north);
        SetRef(panelScript, "eventSystem", eventSystem);
    }

    private static void PlaceRight(RectTransform rect, float y)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-6f, y);
        rect.sizeDelta = new Vector2(26f, 26f);
    }

    /// <summary>Orange arrow (pointing up) with a white edge, saved once as a sprite. Replace the PNG to restyle.</summary>
    private static Sprite GetOrCreateWorkerIcon(StringBuilder log)
    {
        if (!File.Exists(WorkerIconPath))
        {
            Directory.CreateDirectory(MiniMapTextureFolder);

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            Vector2 tip = new Vector2(64f, 120f), left = new Vector2(16f, 10f), right = new Vector2(112f, 10f), notch = new Vector2(64f, 40f);
            Vector2 center = new Vector2(64f, 60f);
            var orange = new Color32(255, 115, 25, 255);
            var white = new Color32(255, 255, 255, 255);
            var clear = new Color32(0, 0, 0, 0);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                // Inner arrow = shrunk towards the centre; outer = the full arrow (white edge).
                Vector2 q = center + (p - center) * 1.18f;
                bool inner = InArrow(q, tip, left, right, notch);
                bool outer = InArrow(p, tip, left, right, notch);
                pixels[y * size + x] = inner ? orange : outer ? white : clear;
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(WorkerIconPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(WorkerIconPath);
            log.AppendLine("+ created " + WorkerIconPath + " (worker icon – replace the picture if you like, keep it pointing UP)");
        }

        MakeSprite(WorkerIconPath, 256);
        return AssetDatabase.LoadAssetAtPath<Sprite>(WorkerIconPath);
    }

    private static bool InArrow(Vector2 p, Vector2 tip, Vector2 left, Vector2 right, Vector2 notch)
    {
        return InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right);
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static float Cross(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    /// <summary>Texture -> Sprite (UI), no mipmaps; WebGL capped at webGLMaxSize.</summary>
    private static bool MakeSprite(string path, int webGLMaxSize)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return false;

        bool changed = false;
        if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
        if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
        if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
        if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }

        TextureImporterPlatformSettings webgl = importer.GetPlatformTextureSettings(WebGL);
        if (!webgl.overridden || webgl.maxTextureSize != webGLMaxSize)
        {
            webgl.overridden = true;
            webgl.maxTextureSize = webGLMaxSize;
            webgl.format = TextureImporterFormat.Automatic;
            webgl.textureCompression = TextureImporterCompression.Compressed;
            webgl.crunchedCompression = true;
            webgl.compressionQuality = 75;
            importer.SetPlatformTextureSettings(webgl);
            changed = true;
        }

        if (changed) importer.SaveAndReimport();
        return changed;
    }

    // ===================================================================================== 6. MiniMap per scene

    /// <summary>Words in the picture's file name that pick it for a scene.</summary>
    private static readonly Dictionary<string, string[]> MiniMapKeywords = new Dictionary<string, string[]>
    {
        { "Main_Scene", new[] { "main", "plant", "power" } },
        { "BoilerRoom", new[] { "boiler" } },
        { "TurbineRoom", new[] { "turbine" } },
        { "Control_Room", new[] { "control" } },
    };

    [MenuItem(MenuRoot + "6. Setup MiniMap In All Scenes", priority = 6)]
    private static void SetupMiniMapInScenes()
    {
        if (!EditorUtility.DisplayDialog("Setup MiniMap",
                "For Main_Scene, BoilerRoom, TurbineRoom and Control_Room:\n\n" +
                "- add a 'MiniMap' object (MiniMapArea) if missing\n" +
                "- pick the scene's picture from " + MiniMapTextureFolder + " by file name\n" +
                "  (main/plant, boiler, turbine, control) and import it as a Sprite\n" +
                "- switch off the old minimap camera(s), old map sprite and old minimap UI\n\n" +
                "No camera is used any more. Each scene is saved. Continue?", "Continue", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder();

        foreach (string sceneName in DownloadableScenes)
        {
            string path = FindScenePath(sceneName);
            if (path == null)
            {
                log.AppendLine($"! {sceneName}: not found");
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            log.AppendLine($"== {sceneName}");
            SetupMiniMapInScene(scene, sceneName, log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Report("Setup MiniMap", log,
            "Now line up each scene: open it, select 'MiniMap' and move / rotate (Y) / scale it until the see-through " +
            "picture on the floor matches the walls. Play from Bootstrap to test.");
    }

    private static void SetupMiniMapInScene(Scene scene, string sceneName, StringBuilder log)
    {
        // --- Area
        MiniMapArea area = FindInScene<MiniMapArea>(scene);
        bool newArea = area == null;
        if (newArea)
        {
            var areaObject = new GameObject("MiniMap");
            SceneManager.MoveGameObjectToScene(areaObject, scene);
            area = areaObject.AddComponent<MiniMapArea>();
            log.AppendLine("  + 'MiniMap' object (MiniMapArea)");
        }
        else log.AppendLine($"  = MiniMapArea on '{GetPath(area.gameObject)}'");

        // --- Picture
        var areaSO = new SerializedObject(area);
        SerializedProperty imageProperty = areaSO.FindProperty("mapImage");
        Sprite current = imageProperty.objectReferenceValue as Sprite;
        string picked = FindMiniMapPicture(sceneName);

        if (picked != null && (current == null || AssetDatabase.GetAssetPath(current) != picked))
        {
            if (MakeSprite(picked, 1024)) log.AppendLine($"  + {picked}: imported as Sprite (WebGL max 1024, compressed)");
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(picked);
            if (sprite != null)
            {
                imageProperty.objectReferenceValue = sprite;
                areaSO.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"  + Map Image = {picked}");
            }
        }
        else if (current != null) log.AppendLine($"  = Map Image = {AssetDatabase.GetAssetPath(current)}");
        else log.AppendLine($"  ! no picture found for {sceneName} in {MiniMapTextureFolder} – assign Map Image by hand");

        // --- First guess of the position: picture over the whole scene (you fine-tune it).
        Transform t = area.transform;
        bool neverLinedUp = t.position == Vector3.zero && t.rotation == Quaternion.identity && t.localScale == Vector3.one;
        if ((newArea || neverLinedUp) && area.MapImage != null)
        {
            Bounds bounds = SceneBounds(scene);
            Vector2 image = area.ImageSize;
            float scale = Mathf.Max(bounds.size.x / image.x, bounds.size.z / image.y);
            t.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Quaternion.identity);
            t.localScale = new Vector3(scale, 1f, scale);
            log.AppendLine("  ! first guess: picture laid over the whole scene – line it up (see below)");
        }

        // --- Old camera-based minimap off
        foreach (Transform child in new List<Transform>(FindAllInScene<Transform>(scene)))
        {
            if (child == null) continue;

            // Objects the previous version of this tool created.
            if (child.parent == area.transform && (child.name == "MapImage" || child.name == "MiniMapCamera"))
            {
                log.AppendLine($"  - deleted old '{GetPath(child.gameObject)}'");
                Object.DestroyImmediate(child.gameObject);
                continue;
            }

            if ((child.name == "MinmapParent" || child.name == "Plantminmap") && child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(false);
                log.AppendLine($"  - switched off old '{GetPath(child.gameObject)}' (delete it when happy)");
            }
        }

        foreach (MiniMap old in FindAllInScene<MiniMap>(scene))
        {
            Camera cam = old.GetComponent<Camera>();
            if (cam != null && cam.enabled)
            {
                cam.enabled = false;
                EditorUtility.SetDirty(cam);
            }

            log.AppendLine($"  ! old minimap camera '{GetPath(old.gameObject)}' switched off – delete it" +
                           (PrefabUtility.IsPartOfPrefabInstance(old) ? " from the Worker prefab" : ""));
        }

        foreach (EventSystem es in FindAllInScene<EventSystem>(scene))
            log.AppendLine($"  ! EventSystem '{GetPath(es.gameObject)}' – delete it (Bootstrap has the only one; it is switched off at runtime anyway)");
    }

    /// <summary>Newest picture in the minimap folder whose file name contains one of the scene's words.</summary>
    private static string FindMiniMapPicture(string sceneName)
    {
        if (!MiniMapKeywords.TryGetValue(sceneName, out string[] words) || !AssetDatabase.IsValidFolder(MiniMapTextureFolder)) return null;

        string best = null;
        System.DateTime bestTime = System.DateTime.MinValue;

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { MiniMapTextureFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path == WorkerIconPath) continue;

            string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool match = false;
            foreach (string word in words) if (file.Contains(word)) { match = true; break; }
            if (!match) continue;

            System.DateTime time = File.GetLastWriteTimeUtc(path);
            if (best == null || time > bestTime)
            {
                best = path;
                bestTime = time;
            }
        }

        return best;
    }

    // Size of the scene's visible geometry, for a first guess of the map area.
    private static Bounds SceneBounds(Scene scene)
    {
        bool any = false;
        var bounds = new Bounds();
        foreach (Renderer r in FindAllInScene<Renderer>(scene))
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || r is SpriteRenderer) continue;
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return any ? bounds : new Bounds(Vector3.zero, new Vector3(100f, 1f, 100f));
    }

    // ===================================================================================== 7. Object info card

    [MenuItem(MenuRoot + "7. Setup Object Info Card (Main_Scene)", priority = 7)]
    private static void SetupObjectInfoCard()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string path = FindScenePath("Main_Scene");
        if (path == null)
        {
            EditorUtility.DisplayDialog("Object info card", "Main_Scene not found.", "OK");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var log = new StringBuilder();

        PlantCameraUI ui = FindInScene<PlantCameraUI>(scene);
        if (ui == null)
        {
            GameObject canvas = CreateOverlayCanvas("ObjectInfoCanvas", 5, false);
            SceneManager.MoveGameObjectToScene(canvas, scene);
            ui = canvas.AddComponent<PlantCameraUI>();
            log.AppendLine("+ ObjectInfoCanvas with PlantCameraUI");
        }
        else log.AppendLine($"= PlantCameraUI on '{GetPath(ui.gameObject)}'");

        // Old card -> kept but switched off, a new one is built (name + description, grows with the text).
        var uiSO = new SerializedObject(ui);
        var oldCard = uiSO.FindProperty("selectionCardObj").objectReferenceValue as GameObject;
        if (oldCard == null)
        {
            Transform found = ui.transform.Find("SelectionInfoCard");
            if (found != null) oldCard = found.gameObject;
        }

        if (oldCard != null && oldCard.GetComponent<ContentSizeFitter>() != null && oldCard.transform.Find("ObjectDescription") != null)
        {
            log.AppendLine("= info card already set up – nothing changed");
        }
        else
        {
            if (oldCard != null)
            {
                oldCard.name = "SelectionInfoCard_Old";
                oldCard.SetActive(false);
                log.AppendLine("- old card renamed 'SelectionInfoCard_Old' and switched off (delete it when happy)");
            }

            BuildInfoCard(ui, uiSO);
            log.AppendLine("+ new 'SelectionInfoCard': title + description, 340 px wide, grows with the text, never takes clicks");
        }

        Transform oldMiniMap = ui.transform.Find("MinmapParent");
        if (oldMiniMap != null && oldMiniMap.gameObject.activeSelf)
        {
            oldMiniMap.gameObject.SetActive(false);
            log.AppendLine("- switched off old 'MinmapParent' (MiniMapPanel replaces it)");
        }

        // Make sure the camera controller talks to this UI.
        PlantIsometricCameraController controller = FindInScene<PlantIsometricCameraController>(scene);
        if (controller != null)
        {
            var controllerSO = new SerializedObject(controller);
            SerializedProperty uiProperty = controllerSO.FindProperty("plantCameraUI");
            if (uiProperty != null && uiProperty.objectReferenceValue != ui)
            {
                uiProperty.objectReferenceValue = ui;
                controllerSO.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("+ PlantIsometricCameraController > Plant Camera UI assigned");
            }

            uiSO.FindProperty("cameraController").objectReferenceValue = controller;
            uiSO.ApplyModifiedPropertiesWithoutUndo();
        }
        else log.AppendLine("! no PlantIsometricCameraController in Main_Scene");

        int withInfo = 0, withoutInfo = 0;
        foreach (BoxCollider box in FindAllInScene<BoxCollider>(scene))
        {
            if (!box.CompareTag("Highlight")) continue;
            if (ObjectInfo.For(box) != null) withInfo++; else withoutInfo++;
        }
        log.AppendLine($"= Highlight box colliders: {withInfo} with ObjectInfo, {withoutInfo} without (add ObjectInfo to give them a description)");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Report("Object info card", log, "Add an ObjectInfo component to each 'Highlight' box collider and type its name + description.");
    }

    private static void BuildInfoCard(PlantCameraUI ui, SerializedObject uiSO)
    {
        RectTransform card = NewUI("SelectionInfoCard", ui.transform);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0f, 0.5f);
        card.sizeDelta = new Vector2(340f, 100f);
        AddImage(card, PanelColor).raycastTarget = false;

        VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CanvasGroup group = card.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        Text title = AddText(NewUI("ObjectName", card), "Object name", 20, FontStyle.Bold, TitleColor, TextAnchor.UpperLeft);
        Text description = AddText(NewUI("ObjectDescription", card), "Description", 15, FontStyle.Normal, BodyColor, TextAnchor.UpperLeft);
        Text size = AddText(NewUI("ObjectBounds", card), "Size", 13, FontStyle.Normal, MutedColor, TextAnchor.UpperLeft);
        size.gameObject.SetActive(false);

        card.gameObject.SetActive(false);

        uiSO.Update();
        uiSO.FindProperty("selectionCardObj").objectReferenceValue = card.gameObject;
        uiSO.FindProperty("selectionNameText").objectReferenceValue = title;
        uiSO.FindProperty("selectionNameTMPText").objectReferenceValue = null;
        uiSO.FindProperty("selectionDescriptionText").objectReferenceValue = description;
        uiSO.FindProperty("selectionDescriptionTMPText").objectReferenceValue = null;
        uiSO.FindProperty("selectionBoundsText").objectReferenceValue = size;
        uiSO.FindProperty("selectionBoundsTMPText").objectReferenceValue = null;
        uiSO.ApplyModifiedPropertiesWithoutUndo();
    }

    // ===================================================================================== 8. glTF models

    /// <summary>
    /// The .glb models no longer carry their textures inside: the pictures sit next to them in "<model>_Textures"
    /// folders, so Unity compresses them (DXT/crunch, WebGL max 1024) instead of storing them uncompressed.
    /// Unity may import a .glb before its textures on the first pull; this re-imports textures first, then models.
    /// </summary>
    [MenuItem(MenuRoot + "8. Reimport glTF Models (after pulling)", priority = 8)]
    private static void ReimportGltfModels()
    {
        var textures = new List<string>();
        var models = new List<string>();

        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.StartsWith("Assets/")) continue;
            string lower = path.ToLowerInvariant();
            if (lower.EndsWith(".glb") || lower.EndsWith(".gltf")) models.Add(path);
            else if (path.Contains("_Textures/") && (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg"))) textures.Add(path);
        }

        try
        {
            for (int i = 0; i < textures.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Reimport glTF", textures[i], 0.5f * i / Mathf.Max(1, textures.Count));
                AssetDatabase.ImportAsset(textures[i], ImportAssetOptions.ForceUpdate);
            }

            for (int i = 0; i < models.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Reimport glTF", models[i], 0.5f + 0.5f * i / Mathf.Max(1, models.Count));
                AssetDatabase.ImportAsset(models[i], ImportAssetOptions.ForceUpdate);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        var log = new StringBuilder();
        log.AppendLine($"+ {textures.Count} model textures re-imported");
        log.AppendLine($"+ {models.Count} glTF models re-imported");
        Report("Reimport glTF models", log, "If a model still looks untextured, check the Console for glTFast errors and send them to Claude.");
    }

    // ===================================================================================== Build mode

    [MenuItem(BuildModeMenu + "Simple Test Build (no downloads, no compression)", priority = 20)]
    private static void SwitchToSimpleBuild()
    {
        if (!EditorUtility.DisplayDialog("Simple test build",
                "For the React team's local testing:\n\n" +
                "- Build Settings: Bootstrap + Main_Scene + BoilerRoom + TurbineRoom + Control_Room\n" +
                "- Addressables are NOT built (scenes load from the build itself)\n" +
                "- Compression: Disabled (no server headers needed, any local server works)\n" +
                "- REACT_BUILD stays on, so every React function works the same\n\n" +
                "The build is bigger and the first load slower – only for testing.\n" +
                "Switch back with Build Mode > Addressables Build.", "Switch", "Cancel"))
            return;

        var log = new StringBuilder();

        if (!File.Exists(BootstrapPath))
        {
            EditorUtility.DisplayDialog("Simple test build", "Bootstrap scene not found. Run '1. Setup Addressables + Bootstrap' first.", "OK");
            return;
        }

        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(BootstrapPath, true) };
        foreach (string sceneName in DownloadableScenes)
        {
            string path = FindScenePath(sceneName);
            if (path == null) log.AppendLine($"! {sceneName} not found");
            else scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        log.AppendLine($"+ Build Settings: {scenes.Count} scenes (Bootstrap first)");

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            log.AppendLine("+ Addressables: not built with the player");
        }

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        // Browser cache (IndexedDB) of a big uncompressed .data file breaks the loader
        // ("Cannot read properties of undefined (reading 'subarray')"): off for testing.
        PlayerSettings.WebGL.dataCaching = false;
        EnsureReactDefine();
        log.AppendLine("+ Compression: Disabled, Decompression Fallback off, Data Caching off");
        log.AppendLine("+ Scripting define REACT_BUILD: on");

        AssetDatabase.SaveAssets();
        Report("Simple test build", log,
            "Build: File > Build Profiles > Web > Build. Give the whole output folder to the React team.\n" +
            "React: loaderUrl/dataUrl/frameworkUrl/codeUrl WITHOUT '.br' – e.g. Build/<name>.data, Build/<name>.wasm. " +
            "streamingAssetsUrl can stay set, it is simply not used.");
    }

    [MenuItem(BuildModeMenu + "Addressables Build (downloads + Brotli)", priority = 21)]
    private static void SwitchToAddressablesBuild()
    {
        if (!File.Exists(BootstrapPath))
        {
            EditorUtility.DisplayDialog("Addressables build", "Bootstrap scene not found. Run '1. Setup Addressables + Bootstrap' first.", "OK");
            return;
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapPath, true) };

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[Thermal Plant] Build mode: Addressables (Bootstrap only, scenes downloaded on demand). Now applying player settings.");

        // Brotli + the server question.
        ApplyPlayerSettings();
    }

    private static void EnsureReactDefine()
    {
        string defines = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.WebGL);
        if (!(";" + defines + ";").Contains(";" + ReactBuildDefine + ";"))
            PlayerSettings.SetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.WebGL, string.IsNullOrEmpty(defines) ? ReactBuildDefine : defines + ";" + ReactBuildDefine);
    }

    // ===================================================================================== UI helpers

    private static GameObject CreateOverlayCanvas(string name, int sortingOrder, bool raycaster)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (raycaster) root.AddComponent<GraphicRaycaster>();
        return root;
    }

    private static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Image AddImage(RectTransform rect, Color color, Sprite sprite = null)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite != null ? sprite : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        image.color = color;
        return image;
    }

    private static Text AddText(RectTransform rect, string content, int size, FontStyle style, Color color, TextAnchor anchor)
    {
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Button AddButton(Transform parent, string name, string label, int fontSize)
    {
        RectTransform rect = NewUI(name, parent);
        rect.sizeDelta = new Vector2(26f, 26f);
        Image image = AddImage(rect, ButtonColor);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        RectTransform labelRect = NewUI("Label", rect);
        Anchor(labelRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddText(labelRect, label, fontSize, FontStyle.Bold, TitleColor, TextAnchor.MiddleCenter);
        return button;
    }

    private static void SetRef(Object target, string property, Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(property);
        if (prop == null)
        {
            Debug.LogWarning($"[Thermal Plant] '{property}' not found on {target.GetType().Name}");
            return;
        }

        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ===================================================================================== scene helpers

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (T item in FindAllInScene<T>(scene)) return item;
        return null;
    }

    private static IEnumerable<T> FindAllInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T item in root.GetComponentsInChildren<T>(true))
                yield return item;
    }

    private static void Report(string title, StringBuilder log, string next)
    {
        Debug.Log($"[Thermal Plant] {title}:\n{log}\n{next}");
        EditorUtility.DisplayDialog(title + " – done", log + "\n" + next + "\n\n(The same report is in the Console.)", "OK");
    }
}
