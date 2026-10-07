using System;
using UnityEngine;

/// <summary>One part's maintenance sheet, ready to show (all fields filled). Also sent to React as JSON.</summary>
[Serializable]
public class MaintenanceInfo
{
    public string partName, partId, equipment, lastMaintenanceDate, maintenanceType, condition, runningHours,
        nextMaintenanceDue, technician, issueFound, actionTaken, sparePartUsed, remarks;
}

/// <summary>
/// Builds the maintenance sheet of a part: values from its MaintenanceRecord where typed in, the rest generated.
/// Generated values are stable (same part = same values every time) and use dates around today.
/// </summary>
public static class MaintenanceData
{
    private static readonly string[] Types = { "Inspection", "Cleaning", "Repair", "Replacement", "Lubrication" };
    private static readonly string[] Technicians =
    {
        "R. Sharma (Mechanical)", "A. Khan (Mechanical)", "S. Patel (Instrumentation)", "M. Iyer (Electrical)",
        "Maintenance Team A", "Maintenance Team B", "OEM Service Engineer"
    };

    private static readonly string[] GoodIssues = { "None - within limits", "Minor dust build-up", "Normal wear, within tolerance" };
    private static readonly string[] GoodActions = { "Visual inspection, no action needed", "Cleaned and re-tightened fasteners", "Lubricated and checked alignment" };
    private static readonly string[] WarningIssues = { "Slight vibration above baseline", "Early signs of corrosion", "Gasket showing minor seepage", "Bearing temperature trending up" };
    private static readonly string[] WarningActions = { "Re-torqued mounts, monitoring vibration", "Applied anti-corrosion coating", "Tightened flange, gasket scheduled", "Re-greased bearing, trend under watch" };
    private static readonly string[] CriticalIssues = { "Crack detected near weld seam", "Bearing wear beyond limit", "Severe erosion on surface", "Leak at high-pressure joint" };
    private static readonly string[] CriticalActions = { "Temporary repair, replacement ordered", "Bearing replaced", "Section isolated, repair planned", "Joint re-sealed, NDT scheduled" };
    private static readonly string[] Spares = { "None", "Gasket set", "Bearing assembly", "Seal kit", "Fastener kit", "Lubricant (ISO VG 68)", "Filter element" };

    public static MaintenanceInfo For(ExplodableViewNode part)
    {
        part.TryGetComponent(out MaintenanceRecord record); // real null when missing (safe with ?.)
        string partName = part.PartName;
        string equipment = Equipment(part, record);
        var random = new System.Random(StableHash(equipment + "/" + partName));

        // Condition first: it decides the findings.
        MaintenanceRecord.Condition condition = record != null && record.currentCondition != MaintenanceRecord.Condition.Auto
            ? record.currentCondition
            : Pick(random, new[] { MaintenanceRecord.Condition.Good, MaintenanceRecord.Condition.Good, MaintenanceRecord.Condition.Good,
                MaintenanceRecord.Condition.Warning, MaintenanceRecord.Condition.Warning, MaintenanceRecord.Condition.Critical });

        DateTime last = DateTime.Today.AddDays(-random.Next(12, 160));
        int interval = condition == MaintenanceRecord.Condition.Critical ? 30 : condition == MaintenanceRecord.Condition.Warning ? 90 : 180;

        string[] issues = condition == MaintenanceRecord.Condition.Critical ? CriticalIssues : condition == MaintenanceRecord.Condition.Warning ? WarningIssues : GoodIssues;
        string[] actions = condition == MaintenanceRecord.Condition.Critical ? CriticalActions : condition == MaintenanceRecord.Condition.Warning ? WarningActions : GoodActions;
        int finding = random.Next(issues.Length);

        string id = Value(record?.partId, $"{IdPrefix(equipment)}-{1000 + random.Next(9000)}");
        int turbine = TurbineNumber(part);
        if (turbine > 0 && !id.StartsWith("T" + turbine + "-")) id = $"T{turbine}-{id}";

        int hours = record != null && record.runningHours > 0 ? record.runningHours : 2000 + random.Next(30) * 750 + turbine * 137;

        return new MaintenanceInfo
        {
            partName = partName,
            partId = id,
            equipment = equipment,
            lastMaintenanceDate = Value(record?.lastMaintenanceDate, last.ToString("dd MMM yyyy")),
            maintenanceType = Value(record?.maintenanceType, Types[random.Next(Types.Length)]),
            condition = condition.ToString(),
            runningHours = hours.ToString("N0") + " h",
            nextMaintenanceDue = Value(record?.nextMaintenanceDue, last.AddDays(interval).ToString("dd MMM yyyy")),
            technician = Value(record?.technician, Technicians[random.Next(Technicians.Length)]),
            issueFound = Value(record?.issueFound, issues[finding]),
            actionTaken = Value(record?.actionTaken, actions[finding]),
            sparePartUsed = Value(record?.sparePartUsed, condition == MaintenanceRecord.Condition.Good ? "None" : Spares[1 + random.Next(Spares.Length - 1)]),
            remarks = Value(record?.remarks, condition == MaintenanceRecord.Condition.Critical ? "Priority job - review at next shift handover"
                : condition == MaintenanceRecord.Condition.Warning ? "Monitor closely until next planned service" : "Fit for service")
        };
    }

    private static string Equipment(ExplodableViewNode part, MaintenanceRecord record)
    {
        int turbine = TurbineNumber(part);
        if (turbine > 0) return $"Steam Turbine {turbine}";
        if (record != null && !string.IsNullOrWhiteSpace(record.equipment)) return record.equipment;

        string scene = part.gameObject.scene.name ?? "";
        if (scene.IndexOf("Boiler", StringComparison.OrdinalIgnoreCase) >= 0) return "Boiler";

        var view = part.GetComponentInParent<ModularExplodedView>();
        return view != null ? view.gameObject.name : scene;
    }

    private static int TurbineNumber(ExplodableViewNode part)
    {
        TurbineData turbine = part.GetComponentInParent<TurbineData>();
        if (turbine == null) return 0;
        return int.TryParse(turbine.ToPayload().id, out int id) ? id : 0;
    }

    private static string IdPrefix(string equipment)
    {
        if (equipment.StartsWith("Steam Turbine", StringComparison.OrdinalIgnoreCase)) return "TRB";
        if (equipment.StartsWith("Boiler", StringComparison.OrdinalIgnoreCase)) return "BLR";
        string letters = new string(Array.FindAll(equipment.ToUpperInvariant().ToCharArray(), char.IsLetter));
        return letters.Length >= 3 ? letters.Substring(0, 3) : "PRT";
    }

    private static string Value(string typed, string generated) => string.IsNullOrWhiteSpace(typed) ? generated : typed.Trim();

    private static T Pick<T>(System.Random random, T[] items) => items[random.Next(items.Length)];

    // string.GetHashCode differs between runs / platforms; this one is stable.
    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = 23;
            foreach (char c in text) hash = hash * 31 + c;
            return hash & 0x7fffffff;
        }
    }
}
