using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click setup for the WebGL build. Menu: Tools > Thermal Plant.
///   0. Clean Up Scenes                  – removes the old UI Toolkit / uGUI screens, reports manual fixes
///   1. Setup Addressables + Bootstrap  – each scene becomes its own download, Bootstrap is the only built-in scene
///   2. Apply WebGL Player Settings      – Brotli, code stripping, caching
///   3. Apply WebGL Texture Settings     – WebGL-only max size + crunch (PC keeps full quality)
///   4. Apply WebGL Model Settings       – optional: mesh compression, Read/Write off
/// Every step can be run again safely.
/// </summary>
public static class ThermalPlantTools
{
    private const string MenuRoot = "Tools/Thermal Plant/";
    private const string ScenesFolder = "Assets/Scenes";
    private const string BootstrapPath = ScenesFolder + "/Bootstrap.unity";
    private const string WebGL = "WebGL";
    private const string ReactBuildDefine = "REACT_BUILD";

    /// <summary>Scenes that are downloaded on demand. The address is the scene name.</summary>
    private static readonly string[] DownloadableScenes = { "Main_Scene", "BoilerRoom", "TurbineRoom", "Control_Room" };

    /// <summary>Largest texture size used in the WebGL build. 1024 is a good start; try 512 for props.</summary>
    private const int WebGLMaxTextureSize = 1024;

    /// <summary>Crunch quality 0-100. Lower = smaller download, more artefacts.</summary>
    private const int CrunchQuality = 50;

    // ===================================================================================== 0. Scene cleanup

    [MenuItem(MenuRoot + "0. Clean Up Scenes (remove old UI)", priority = 0)]
    private static void CleanUpScenes()
    {
        if (!EditorUtility.DisplayDialog("Clean up scenes",
                "For Main_Scene, BoilerRoom, TurbineRoom and Control_Room this will:\n\n" +
                "- delete objects that only hold a UI Document (old UI Toolkit screens)\n" +
                "- remove the UI Document component from objects that also hold game logic\n" +
                "- remove 'Missing script' components (e.g. the deleted MainSceneUIController)\n" +
                "- delete the PlantCameraUI canvas (React replaces it)\n" +
                "- report anything that needs a manual decision (extra workers, cameras)\n\n" +
                "Each scene is saved. Make a backup first. Continue?", "Clean up", "Cancel"))
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

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            log.AppendLine($"== {sceneName}");
            CleanUpScene(scene, log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log("[Thermal Plant] Scene clean-up:\n" + log);
        EditorUtility.DisplayDialog("Clean up scenes – done",
            "Done. The full report is in the Console.\n\nLines starting with '!' need a manual decision.", "OK");
    }

    private static void CleanUpScene(UnityEngine.SceneManagement.Scene scene, StringBuilder log)
    {
        var allObjects = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                allObjects.Add(t.gameObject);

        // 1. Missing scripts
        int missing = 0;
        foreach (GameObject go in allObjects)
            missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        if (missing > 0) log.AppendLine($"  - removed {missing} missing-script component(s)");

        // 2. UI Toolkit documents
        foreach (GameObject go in allObjects)
        {
            if (go == null) continue;
            var document = go.GetComponent<UnityEngine.UIElements.UIDocument>();
            if (document == null) continue;

            bool onlyUI = go.transform.childCount == 0 && OnlyHasComponents(go, typeof(Transform), typeof(RectTransform), typeof(UnityEngine.UIElements.UIDocument));
            if (onlyUI)
            {
                log.AppendLine($"  - deleted UI object '{GetPath(go)}'");
                Object.DestroyImmediate(go);
            }
            else
            {
                log.AppendLine($"  - removed UI Document from '{GetPath(go)}'");
                Object.DestroyImmediate(document);
            }
        }

        // 3. uGUI canvas replaced by React
        foreach (PlantCameraUI cameraUI in Object.FindObjectsByType<PlantCameraUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cameraUI == null || cameraUI.gameObject.scene != scene) continue;
            log.AppendLine($"  - deleted '{GetPath(cameraUI.gameObject)}' (PlantCameraUI)");
            Object.DestroyImmediate(cameraUI.gameObject);
        }

        // 4. Report what needs a decision
        var workers = new List<string>();
        foreach (Player player in Object.FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (player.gameObject.scene == scene) workers.Add(GetPath(player.gameObject) + (player.gameObject.activeInHierarchy ? "" : " (inactive)"));
        if (workers.Count > 1) log.AppendLine($"  ! {workers.Count} workers: {string.Join(", ", workers)} – keep one");
        else if (workers.Count == 1) log.AppendLine($"  = worker: {workers[0]}");

        foreach (PersistentPlayer persistent in Object.FindObjectsByType<PersistentPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (persistent.gameObject.scene == scene) log.AppendLine($"  ! '{GetPath(persistent.gameObject)}' has PersistentPlayer – remove it (each scene has its own worker)");

        int activeCameras = 0, activeListeners = 0;
        foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (cam.gameObject.scene == scene && cam.enabled && cam.targetTexture == null) activeCameras++;
        foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (listener.gameObject.scene == scene && listener.enabled) activeListeners++;
        if (activeCameras > 2 || activeListeners > 1)
            log.AppendLine($"  ! {activeCameras} screen cameras / {activeListeners} audio listeners enabled at start – check (Main_Scene: overview + worker camera is fine, the script switches them)");

        if (scene.name == "BoilerRoom")
        {
            foreach (BoilerDashboardController dashboard in Object.FindObjectsByType<BoilerDashboardController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (dashboard.gameObject.scene == scene && dashboard.gameObject.activeSelf)
                    log.AppendLine($"  ! '{GetPath(dashboard.gameObject)}' (boiler dashboard) is active at start – untick it, the info sequence switches it on");
        }
    }

    private static bool OnlyHasComponents(GameObject go, params System.Type[] allowed)
    {
        foreach (Component component in go.GetComponents<Component>())
        {
            if (component == null) continue;
            bool ok = false;
            foreach (System.Type type in allowed)
                if (type.IsInstanceOfType(component)) { ok = true; break; }
            if (!ok) return false;
        }

        return true;
    }

    private static string GetPath(GameObject go)
    {
        string path = go.name;
        for (Transform t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
        return path;
    }

    // ===================================================================================== 1. Addressables

    [MenuItem(MenuRoot + "1. Setup Addressables + Bootstrap", priority = 1)]
    private static void SetupAddressables()
    {
        if (!EditorUtility.DisplayDialog("Setup Addressables",
                "This will:\n\n" +
                "- make Main_Scene, BoilerRoom, TurbineRoom and Control_Room downloadable (one bundle each)\n" +
                "- create Assets/Scenes/Bootstrap.unity (if missing)\n" +
                "- set Build Settings to Bootstrap only\n" +
                "- build Addressables automatically with every player build\n\n" +
                "Open scenes will be saved first. Continue?", "Continue", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder();
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

        foreach (string sceneName in DownloadableScenes)
        {
            string scenePath = FindScenePath(sceneName);
            if (scenePath == null)
            {
                log.AppendLine($"! Scene '{sceneName}' not found – skipped.");
                continue;
            }

            AddressableAssetGroup group = GetOrCreateSceneGroup(settings, "Scene - " + sceneName);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(scenePath), group, false, false);
            entry.address = sceneName;
            log.AppendLine($"+ {sceneName}  ->  group 'Scene - {sceneName}', address '{sceneName}'");
        }

        settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);

        bool created = CreateBootstrapSceneIfMissing(log);

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapPath, true) };
        log.AppendLine("+ Build Settings: Bootstrap only (the other scenes are downloaded on demand).");
        log.AppendLine("+ Addressables are built automatically with every player build.");

        AssetDatabase.SaveAssets();
        Debug.Log("[Thermal Plant] Addressables setup done:\n" + log);

        EditorUtility.DisplayDialog("Setup Addressables – done", log +
            (created ? "\nOpen Bootstrap and check 'Managers > HUDController > Explosion View Camera Prefab'." : "") +
            "\n\nNext: Window > Asset Management > Addressables > Analyze > 'Check Duplicate Bundle Dependencies' > Fix (moves shared assets into one shared bundle).",
            "OK");
    }

    private static AddressableAssetGroup GetOrCreateSceneGroup(AddressableAssetSettings settings, string groupName)
    {
        AddressableAssetGroup group = settings.FindGroup(groupName);
        if (group == null)
        {
            group = settings.CreateGroup(groupName, false, false, true, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        }

        var schema = group.GetSchema<BundledAssetGroupSchema>();
        if (schema == null) schema = group.AddSchema<BundledAssetGroupSchema>();

        // Local paths: on WebGL the bundles land in Build/StreamingAssets and are fetched over HTTP
        // only when needed (not part of the initial download). Same server as the build, no extra hosting.
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4; // recommended for WebGL
        schema.UseAssetBundleCache = true;   // browser cache: download once
        schema.IncludeInBuild = true;
        return group;
    }

    private static bool CreateBootstrapSceneIfMissing(StringBuilder log)
    {
        if (File.Exists(BootstrapPath))
        {
            log.AppendLine("= Bootstrap scene already exists – left unchanged.");
            return false;
        }

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // The React bridge on its own object: React calls sendMessage("CommunicationManager", ...).
        new GameObject(CommunicationManager.GameObjectName).AddComponent<CommunicationManager>();

        // Persistent managers: they survive every scene change from here on.
        var managers = new GameObject("Managers");
        managers.AddComponent<SceneController>();
        var hud = managers.AddComponent<HUDController>();
        AssignExplosionCameraPrefab(hud, log);

        new GameObject("BootstrapLoader").AddComponent<BootstrapLoader>();

        // A camera so the screen is a clean black (not garbage) while Main_Scene downloads.
        var cameraObject = new GameObject("Bootstrap Camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 0;

        EditorSceneManager.SaveScene(scene, BootstrapPath);
        log.AppendLine("+ Created " + BootstrapPath + " (CommunicationManager, Managers, BootstrapLoader, black camera).");
        return true;
    }

    // Finds the RTS explosion camera prefab so it doesn't have to be assigned by hand.
    private static void AssignExplosionCameraPrefab(HUDController hud, StringBuilder log)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<RTSCameraController>() == null) continue;

            var serialized = new SerializedObject(hud);
            serialized.FindProperty("explosionViewCameraPrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("+ HUDController explosion camera prefab: " + path);
            return;
        }

        log.AppendLine("! No prefab with RTSCameraController found – assign HUDController's Explosion View Camera Prefab by hand.");
    }

    private static string FindScenePath(string sceneName)
    {
        string direct = $"{ScenesFolder}/{sceneName}.unity";
        if (File.Exists(direct)) return direct;

        foreach (string guid in AssetDatabase.FindAssets(sceneName + " t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == sceneName) return path;
        }

        return null;
    }

    // ===================================================================================== 2. Player settings

    [MenuItem(MenuRoot + "2. Apply WebGL Player Settings", priority = 2)]
    private static void ApplyPlayerSettings()
    {
        bool serverSendsBrotliHeader = EditorUtility.DisplayDialog("WebGL compression",
            "Brotli compression will be enabled.\n\n" +
            "Can your web server send the header 'Content-Encoding: br' for .br files?\n\n" +
            "If you don't know / testing on a simple host: choose 'No / not sure' (enables Decompression Fallback – works everywhere, slightly slower start).",
            "Yes, server is configured", "No / not sure");

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = !serverSendsBrotliHeader;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
        PlayerSettings.stripEngineCode = true;

        // REACT_BUILD switches on the Unity -> React calls in CommunicationManager (same as your other project).
        string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
        if (!(";" + defines + ";").Contains(";" + ReactBuildDefine + ";"))
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, string.IsNullOrEmpty(defines) ? ReactBuildDefine : defines + ";" + ReactBuildDefine);

        AssetDatabase.SaveAssets();

        string message = "Applied:\n- Compression: Brotli" + (!serverSendsBrotliHeader ? " + Decompression Fallback" : "") +
                         "\n- Data caching: on\n- Managed stripping: High\n- Engine code stripping: on" +
                         "\n- Scripting define: " + ReactBuildDefine + " (WebGL)\n\n" +
                         "Make sure 'Development Build' is OFF in File > Build Profiles.\n" +
                         "If High stripping removes something needed at runtime (errors only in the build), tell me the error.";
        Debug.Log("[Thermal Plant] " + message);
        EditorUtility.DisplayDialog("WebGL Player Settings", message, "OK");
    }

    // ===================================================================================== 3. Textures

    [MenuItem(MenuRoot + "3. Apply WebGL Texture Settings", priority = 3)]
    private static void ApplyTextureSettings()
    {
        if (!EditorUtility.DisplayDialog("WebGL texture settings",
                $"For every texture in Assets (WebGL only – PC/Editor keep full quality):\n\n" +
                $"- max size capped at {WebGLMaxTextureSize}\n" +
                $"- compressed, crunch quality {CrunchQuality} (normal maps: no crunch)\n\n" +
                "Skipped: lightmaps, reflection probes, sprites/UI, Editor folders.\n\n" +
                "Unity re-imports the textures – this can take several minutes.", "Apply", "Cancel"))
            return;

        int changed = 0, skipped = 0;
        var report = new StringBuilder();
        string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" });

        try
        {
            AssetDatabase.StartAssetEditing();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("WebGL textures", path, (float)i / guids.Length)) break;

                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || ShouldSkipTexture(path, importer))
                {
                    skipped++;
                    continue;
                }

                bool isNormalMap = importer.textureType == TextureImporterType.NormalMap;
                TextureImporterPlatformSettings webgl = importer.GetPlatformTextureSettings(WebGL);
                int defaultMax = importer.maxTextureSize;

                webgl.overridden = true;
                webgl.maxTextureSize = Mathf.Min(defaultMax, WebGLMaxTextureSize);
                webgl.format = TextureImporterFormat.Automatic;
                webgl.textureCompression = TextureImporterCompression.Compressed;
                webgl.crunchedCompression = !isNormalMap;
                webgl.compressionQuality = CrunchQuality;

                importer.SetPlatformTextureSettings(webgl);
                importer.SaveAndReimport();

                changed++;
                if (defaultMax > WebGLMaxTextureSize) report.AppendLine($"  {defaultMax} -> {webgl.maxTextureSize}  {path}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[Thermal Plant] WebGL texture settings: {changed} changed, {skipped} skipped.\nDownsized:\n{report}");
        EditorUtility.DisplayDialog("WebGL texture settings",
            $"{changed} textures updated, {skipped} skipped.\nThe Console lists every texture that was downsized.\n\n" +
            "If a texture now looks too blurry in the build, select it and raise its WebGL Max Size by hand.", "OK");
    }

    private static bool ShouldSkipTexture(string path, TextureImporter importer)
    {
        string file = Path.GetFileName(path);
        if (file.StartsWith("Lightmap-") || file.StartsWith("ReflectionProbe-")) return true; // owned by lighting bake
        if (path.Contains("/Editor/") || path.Contains("/Gizmos/")) return true;
        if (importer.textureType == TextureImporterType.Sprite || importer.textureType == TextureImporterType.GUI) return true;
        if (importer.textureType == TextureImporterType.Cursor || importer.textureType == TextureImporterType.Cookie) return true;
        return false;
    }

    // ===================================================================================== 4. Models (optional)

    [MenuItem(MenuRoot + "4. Apply WebGL Model Settings (optional)", priority = 4)]
    private static void ApplyModelSettings()
    {
        if (!EditorUtility.DisplayDialog("WebGL model settings (optional)",
                "For every model in Assets:\n\n" +
                "- Mesh Compression: Medium (smaller download)\n" +
                "- Read/Write: OFF (half the mesh memory)\n" +
                "- Optimize Mesh: on\n\n" +
                "Note: these apply to all platforms. After this, test the build: if a MeshCollider or a script " +
                "that reads mesh data stops working, turn Read/Write back ON for that model only.", "Apply", "Cancel"))
            return;

        int changed = 0;
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });

        try
        {
            AssetDatabase.StartAssetEditing();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("WebGL models", path, (float)i / guids.Length)) break;
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;

                importer.meshCompression = ModelImporterMeshCompression.Medium;
                importer.isReadable = false;
                importer.optimizeMeshPolygons = true;
                importer.optimizeMeshVertices = true;
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[Thermal Plant] WebGL model settings applied to {changed} models.");
        EditorUtility.DisplayDialog("WebGL model settings", $"{changed} models updated.", "OK");
    }
}
