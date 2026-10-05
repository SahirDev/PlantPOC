using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "TurbineData", menuName = "Data/TurbineData")]
public class TurbineScriptableObject : ScriptableObject
{
    public string TurbineID = "NaN";
    public string TurbineDisplayName = "NaN";
    public string SteamPressure = "NaN";
    public string Temperature = "NaN";
    public string TemperatureF = "NaN";
    public string Vibration = "NaN";
    public string RPM = "NaN";
    public string SteamMassFlowRate = "NaN";
    public string TurbineType = "Steam";
}
