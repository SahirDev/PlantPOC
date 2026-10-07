using UnityEngine;

/// <summary>
/// Maintenance sheet data of one part (put it on the same object as its ExplodableViewNode).
/// Every field is optional: an empty field is filled with a realistic generated value, so parts without
/// this component (or with only some fields typed in) still show a complete sheet.
///
/// Tools > Thermal Plant > 15. Add Maintenance Records To Parts adds it to every part of the boiler and the
/// turbines with generated values you can then edit here.
/// </summary>
public class MaintenanceRecord : MonoBehaviour
{
    public enum Condition { Auto, Good, Warning, Critical }

    [Tooltip("Unique part number, e.g. BLR-1024. Turbines get their turbine number added in front (T2-...).")]
    public string partId;
    [Tooltip("Machine / system the part belongs to. Empty = from the scene (Boiler, Steam Turbine 1, ...).")]
    public string equipment;

    [Header("Last service")]
    [Tooltip("e.g. 12 Aug 2026")]
    public string lastMaintenanceDate;
    [Tooltip("Inspection, Cleaning, Repair, Replacement, Lubrication")]
    public string maintenanceType;
    public string technician;

    [Header("State")]
    public Condition currentCondition = Condition.Auto;
    [Tooltip("Total operating hours. 0 = generated.")]
    public int runningHours;
    [Tooltip("e.g. 08 Feb 2027")]
    public string nextMaintenanceDue;

    [Header("Findings")]
    [TextArea(1, 3)] public string issueFound;
    [TextArea(1, 3)] public string actionTaken;
    public string sparePartUsed;
    [TextArea(1, 3)] public string remarks;
}
