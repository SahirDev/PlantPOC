using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Thermal Plant, maintenance sheet (explosion view, Boiler + Turbine rooms):
///   14. Create Maintenance Sheet Prefab   -> Assets/Prefabs/UI/MaintenanceSheet.prefab (restyle freely;
///       HUDController in Bootstrap references it)
///   15. Add Maintenance Records To Parts  -> a MaintenanceRecord with generated values on every part of the
///       boiler (BoilerRoom) and the turbine (SteamTurbine prefab), to edit per part in the Inspector.
/// Both are optional: without them the sheet is built in code and the values are generated.
/// </summary>
public static partial class ThermalPlantTools
{
    private const string SheetPrefabPath = "Assets/Prefabs/UI/MaintenanceSheet.prefab";
    private const string TurbinePrefabPath = "Assets/Prefabs/SteamTurbine.prefab";

    [MenuItem(MenuRoot + "14. Create Maintenance Sheet Prefab", priority = 14)]
    private static void CreateMaintenanceSheetPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SheetPrefabPath) != null &&
            !EditorUtility.DisplayDialog("Maintenance sheet prefab",
                $"{SheetPrefabPath} already exists. Replace it with the default layout (your changes to it are lost)?",
                "Replace", "Cancel"))
            return;

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

        MaintenanceSheetPanel sheet = MaintenanceSheetPanel.CreateDefault(null);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(sheet.gameObject, SheetPrefabPath);
        Object.DestroyImmediate(sheet.gameObject);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        var log = new StringBuilder($"+ {SheetPrefabPath}\n");
        Report("Maintenance sheet prefab", log,
            "Open the prefab and restyle it (colours, fonts, sizes, row order of the labels). Keep the references on " +
            "Maintenance Sheet Panel (Panel, Body, Collapse Button, Collapse Icon, Part Title, Values). " +
            "Position: Panel is anchored bottom-right, 150 / 150 from the corner.");
    }

    [MenuItem(MenuRoot + "15. Add Maintenance Records To Parts", priority = 15)]
    private static void AddMaintenanceRecords()
    {
        if (!EditorUtility.DisplayDialog("Maintenance records",
                "Adds a Maintenance Record with generated values to every explodable part of:\n\n" +
                "- the boiler (BoilerRoom scene)\n- the steam turbine (SteamTurbine prefab, used by all 4 turbines)\n\n" +
                "Parts that already have one are kept. Edit the values per part in the Inspector afterwards. Continue?",
                "Add", "Cancel"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var log = new StringBuilder();

        // Boiler room scene.
        string boilerPath = FindScenePath("BoilerRoom");
        if (boilerPath != null)
        {
            Scene scene = EditorSceneManager.OpenScene(boilerPath, OpenSceneMode.Single);
            int added = 0;
            foreach (ExplodableViewNode node in FindAllInScene<ExplodableViewNode>(scene))
                if (AddRecord(node)) added++;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"+ BoilerRoom: {added} parts got a Maintenance Record");
        }
        else log.AppendLine("! BoilerRoom scene not found");

        // Turbine prefab (all 4 turbines; the turbine number is added to the Part ID at runtime).
        if (AssetDatabase.LoadAssetAtPath<GameObject>(TurbinePrefabPath) != null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(TurbinePrefabPath);
            int added = 0;
            foreach (ExplodableViewNode node in contents.GetComponentsInChildren<ExplodableViewNode>(true))
                if (AddRecord(node)) added++;
            PrefabUtility.SaveAsPrefabAsset(contents, TurbinePrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            log.AppendLine($"+ {TurbinePrefabPath}: {added} parts got a Maintenance Record");
        }
        else log.AppendLine($"! {TurbinePrefabPath} not found");

        Report("Maintenance records", log,
            "Select any part (object with Explodable View Node) to edit its sheet. Empty fields are still generated.");
    }

    // Adds a record filled with the generated values (so they become editable). False if it already had one.
    private static bool AddRecord(ExplodableViewNode node)
    {
        if (node.GetComponent<MaintenanceRecord>() != null) return false;

        var record = node.gameObject.AddComponent<MaintenanceRecord>();
        MaintenanceInfo info = MaintenanceData.For(node); // record is empty -> all generated

        record.partId = Regex.Replace(info.partId, @"^T\d+-", ""); // turbine number is added at runtime
        record.equipment = node.GetComponentInParent<TurbineData>() != null ? "" : info.equipment;
        record.lastMaintenanceDate = info.lastMaintenanceDate;
        record.maintenanceType = info.maintenanceType;
        record.technician = info.technician;
        record.currentCondition = System.Enum.TryParse(info.condition, out MaintenanceRecord.Condition c) ? c : MaintenanceRecord.Condition.Auto;
        record.runningHours = int.TryParse(Regex.Replace(info.runningHours, @"[^\d]", ""), out int hours) ? hours : 0;
        record.nextMaintenanceDue = info.nextMaintenanceDue;
        record.issueFound = info.issueFound;
        record.actionTaken = info.actionTaken;
        record.sparePartUsed = info.sparePartUsed;
        record.remarks = info.remarks;
        EditorUtility.SetDirty(record);
        return true;
    }
}
