using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Thermal Plant > 16. Make Boiler Materials Editable
///
/// The boiler's materials live inside its model files (glTF / .glb) - Unity shows them greyed out, they cannot
/// be edited. This copies every such material used by the boiler into its own .mat file
/// (Assets/Material/Boiler/) and points the boiler at the copies:
///   - every renderer of the boiler prefabs (Boiler Parent / 1 / 2) and of the boiler in BoilerRoom,
///   - every script field on the boiler that referenced one of those materials,
/// so nothing loses its link. Textures stay where they are (shared). Safe to run again: existing copies are
/// reused, nothing is duplicated. Edit the colours afterwards in Assets/Material/Boiler.
/// </summary>
public static partial class ThermalPlantTools
{
    private const string BoilerMaterialFolder = "Assets/Material/Boiler";
    private static readonly string[] BoilerPrefabPaths =
    {
        "Assets/Prefabs/Boiler Parent 2.prefab",
        "Assets/Prefabs/Boiler Parent 1.prefab",
        "Assets/Prefabs/Boiler Parent.prefab",
    };

    [MenuItem(MenuRoot + "16. Make Boiler Materials Editable", priority = 16)]
    private static void MakeBoilerMaterialsEditable()
    {
        if (!EditorUtility.DisplayDialog("Make boiler materials editable",
                "Copies the boiler's locked materials (inside its model files) into editable .mat files in " +
                BoilerMaterialFolder + " and points the boiler prefabs, the BoilerRoom boiler and their scripts at the copies.\n\n" +
                "Commit / back up first if you want an easy undo. Continue?", "Make editable", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolderPath(BoilerMaterialFolder);

        var map = new Dictionary<Material, Material>();
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var log = new StringBuilder();

        // 1. Boiler prefabs: every locked material on them.
        foreach (string path in BoilerPrefabPaths)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            int renderers = RemapLockedMaterials(contents.transform, map, sourceFiles, null, out int fields);
            if (renderers + fields > 0) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            log.AppendLine($"+ {Path.GetFileName(path)}: {renderers} renderers, {fields} script fields");
        }

        // 2. BoilerRoom: only materials from the boiler's model files (not the room / walls model).
        string scenePath = FindScenePath("BoilerRoom");
        if (scenePath != null)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            int renderers = 0, fields = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // The boiler itself (no prefabs left to learn its model files from): every locked material on it.
                // Other roots (room, walls): only materials from the boiler's model files.
                bool isBoiler = root.GetComponentInChildren<BoilerFluidController>(true) != null;
                HashSet<string> filter = isBoiler ? null : sourceFiles;
                if (!isBoiler && sourceFiles.Count == 0) continue;
                renderers += RemapLockedMaterials(root.transform, map, sourceFiles, filter, out int f);
                fields += f;
            }
            if (renderers + fields > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            log.AppendLine($"+ BoilerRoom: {renderers} renderers, {fields} script fields");
        }
        else log.AppendLine("! BoilerRoom scene not found");

        AssetDatabase.SaveAssets();
        log.AppendLine($"= {map.Count} editable materials in {BoilerMaterialFolder}");
        foreach (string file in sourceFiles) log.AppendLine($"  from {file}");

        Report("Boiler materials", log,
            $"Edit the colours in {BoilerMaterialFolder} (select a material, change Base Color / Metallic / Smoothness). " +
            "The model files are unchanged; re-running this tool reuses the same copies.");
    }

    // Replaces locked materials on all renderers under root (and in script fields). onlyFrom = limit to these
    // source files (null = any locked material, and remember where it came from in sourceFiles).
    private static int RemapLockedMaterials(Transform root, Dictionary<Material, Material> map, HashSet<string> sourceFiles,
        HashSet<string> onlyFrom, out int fieldsChanged)
    {
        int renderersChanged = 0;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!IsLockedMaterial(materials[i], onlyFrom, out string source)) continue;
                if (onlyFrom == null) sourceFiles.Add(source);
                materials[i] = EditableCopy(materials[i], map);
                changed = true;
            }
            if (!changed) continue;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
            renderersChanged++;
        }

        // Script fields (e.g. original / highlight materials kept by a component) - same copies.
        fieldsChanged = 0;
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null) continue; // missing script
            var serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            bool any = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!(property.objectReferenceValue is Material material)) continue;
                if (!map.TryGetValue(material, out Material copy))
                {
                    if (!IsLockedMaterial(material, onlyFrom ?? sourceFiles, out _)) continue;
                    copy = EditableCopy(material, map);
                }
                property.objectReferenceValue = copy;
                any = true;
                fieldsChanged++;
            }
            if (any) serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        return renderersChanged;
    }

    // Locked = cannot be edited: inside a model file (sub-asset of .glb / .fbx ...) or in a read-only package.
    private static bool IsLockedMaterial(Material material, HashSet<string> onlyFrom, out string source)
    {
        source = null;
        if (material == null) return false;
        source = AssetDatabase.GetAssetPath(material);
        if (string.IsNullOrEmpty(source)) return false;

        bool inProject = source.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        bool editableFile = inProject && source.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) && !AssetDatabase.IsSubAsset(material);
        if (editableFile) return false;
        if (source.StartsWith(BoilerMaterialFolder, StringComparison.OrdinalIgnoreCase)) return false;
        return onlyFrom == null || onlyFrom.Contains(source);
    }

    private static Material EditableCopy(Material locked, Dictionary<Material, Material> map)
    {
        if (map.TryGetValue(locked, out Material copy)) return copy;

        string baseName = SafeFileName(string.IsNullOrEmpty(locked.name) ? "Material" : locked.name);
        string path = $"{BoilerMaterialFolder}/{baseName}.mat";

        // Re-run: reuse the copy made last time (same name), unless this run already used that file for another one.
        copy = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (copy != null && map.ContainsValue(copy)) copy = null;
        if (copy == null)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) path = AssetDatabase.GenerateUniqueAssetPath(path);
            copy = new Material(locked) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(copy, path);
        }

        map[locked] = copy;
        return copy;
    }

    private static string SafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    private static void EnsureFolderPath(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
