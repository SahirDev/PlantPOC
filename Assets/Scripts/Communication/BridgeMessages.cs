using System;

// =====================================================================================
// JSON payloads Unity sends to React (the "data" of the handleXxx events), in one place.
// React -> Unity uses plain strings (see CommunicationManager), so only Unity -> React needs classes.
// Field names here are exactly the JSON field names React receives. Keep in sync with
// Docs/ReactBridge_API.md and Docs/unityBridge.ts.
// JsonUtility rules: public fields only, no Dictionaries, no properties.
// =====================================================================================

// -------------------------------------------------------------------------------------
// Errors
// -------------------------------------------------------------------------------------

/// <summary>handleError / handleSceneLoadFailed: command = the _Extern function (or scene name), message = what went wrong.</summary>
[Serializable]
public class BridgeErrorPayload
{
    public string command;
    public string message;
}

// -------------------------------------------------------------------------------------
// App / scene / input (Unity -> React)
// -------------------------------------------------------------------------------------

[Serializable]
public class UnityStatusPayload
{
    public string scene;
    public int buildIndex;
    public string unityVersion;
    public string appVersion;
    public string platform;
}

[Serializable]
public class SceneInfoPayload
{
    public string name;
    public int buildIndex;
}

[Serializable]
public class WorkerTransformPayload
{
    public string scene;
    public bool active;   // false while the worker is switched off (overview / explosion view)
    public float x, y, z;
    public float heading; // degrees, 0 = +Z (north), clockwise
}

// -------------------------------------------------------------------------------------
// Main scene / overview camera (Unity -> React)
// -------------------------------------------------------------------------------------

[Serializable]
public class OverviewSelectionPayload
{
    public string name;        // ObjectInfo display name (GameObject name if none)
    public string objectName;  // GameObject name
    public string description; // ObjectInfo description ("" if none)
    public float sizeX, sizeY, sizeZ; // metres
}

// -------------------------------------------------------------------------------------
// Equipment (turbine / boiler / control room) (Unity -> React)
// -------------------------------------------------------------------------------------

[Serializable]
public class EquipmentInfoPayload
{
    public string type; // "Turbine" | "Boiler" | "ControlRoom"
    public string name;
}

/// <summary>
/// Full state of the equipment the worker is standing at. React enables/disables its buttons
/// from the can* flags only, so the rules stay in Unity.
/// activeAction: "none" | "explode" | "explodeAll" | "operation" | "info"
/// </summary>
[Serializable]
public class EquipmentStatePayload
{
    public bool inRange;
    public string type;
    public string name;
    public string activeAction;
    public bool exploded;
    public bool operating;
    public bool infoActive;
    public bool canExplode;
    public bool canExplodeAll;
    public bool canOperate;
    public bool canInfo;
}

/// <summary>x/y: click position as 0..1 of the canvas, origin TOP-LEFT (multiply by canvas CSS size).</summary>
[Serializable]
public class PartSelectedPayload
{
    public string name;
    public string description;
    public bool exploded;
    public float x, y;
}

// -------------------------------------------------------------------------------------
// Electrical panel (Control room)
// -------------------------------------------------------------------------------------

[Serializable]
public class ElectricalValuesPayload
{
    public float generatorValue; // slider 0..100
    public float generatorKV;
    public float gridKV;
    public float loading;        // %
    public float oilTemp;        // °C
    public float windingTemp;    // °C
    public bool coolingFan;
    public string alarmLevel;    // "normal" | "high" (red lamp) | "warning" (blinking, >= 80 by default)
}

/// <summary>Chimney smoke indication (Main_Scene).</summary>
[Serializable]
public class SmokeStatusPayload
{
    public float level;        // 0..1
    public string status;      // "none" | "normal" | "degrading" | "severe" | "nox_sox" | "contamination"
    public string indication;  // text for the UI, e.g. "Normal operation / steam-heavy exhaust"
    public string color;       // current smoke colour, e.g. "#DBDBDB"
}

/// <summary>Control room info point the worker walked into / out of.</summary>
[Serializable]
public class ControlRoomInfoPayload
{
    public int index;     // 1, 2 or 3 – which info panel to show
    public string name;   // GameObject name
    public string title;  // optional title (GameObject name if empty)
}

// -------------------------------------------------------------------------------------
// Turbines
// -------------------------------------------------------------------------------------

[Serializable]
public class TurbineDataPayload
{
    public string id;
    public string name;
    public string type;
    public float steamPressure;
    public float temperature;  // °C
    public float temperatureF; // °F
    public float vibration;
    public float rpm;
    public float steamMassFlowRate;
    public bool operating;
}

[Serializable]
public class TurbineListPayload
{
    public TurbineDataPayload[] turbines;
}

// -------------------------------------------------------------------------------------
// Boiler dashboard
// -------------------------------------------------------------------------------------

[Serializable]
public class BoilerDashboardPayload
{
    public float temperature;    // °C
    public float maxTemperature;
    public float waterLevel;     // %
    public float steamPressure;  // bar
    public float maxPressure;
    public float burnerPower;    // %
    public float valveOpening;   // %
    public string status;        // "RUNNING" | "STOPPED" | "COMPLETED"
    public string message;
    public bool lowWater;        // water < 35 %
    public bool completed;       // cycle finished; offer "restart"
}

// -------------------------------------------------------------------------------------
// Scene change triggers (doors / colliders that offer "go to another room")
// -------------------------------------------------------------------------------------

[Serializable]
public class SceneTriggerPayload
{
    public string targetScene;
    public string text;
}

// -------------------------------------------------------------------------------------
// Scene downloads (Addressables)
// -------------------------------------------------------------------------------------

/// <summary>Download progress of a scene's content. background = true for quiet pre-downloads.</summary>
[Serializable]
public class SceneDownloadProgressPayload
{
    public string name;
    public float progress;     // 0..1
    public float downloadedMB;
    public float totalMB;
    public bool background;
}

[Serializable]
public class SceneDownloadSizePayload
{
    public string name;
    public bool exists;  // false if no scene with this name can be loaded
    public bool cached;  // true = already downloaded, opens immediately
    public float sizeMB; // still to download (0 when cached)
}
