// unityBridge.ts – typed helpers for the Thermal Plant Unity build (react-unity-webgl).
// Same pattern as our other projects:
//   React -> Unity : sendMessage("CommunicationManager", "<Function>_Extern", value)
//   Unity -> React : addEventListener("handle<Event>", (data) => ...)
// Keep in sync with Assets/Scripts/CommunicationManager.cs and Docs/ReactBridge_API.md.

import { useEffect } from "react";

export const UNITY_BRIDGE_OBJECT = "CommunicationManager";
export type SceneName = "Main_Scene" | "BoilerRoom" | "TurbineRoom" | "Control_Room";

// ---------------------------------------------------------------- JSON shapes (Unity -> React)

export interface UnityStatus { scene: string; buildIndex: number; unityVersion: string; appVersion: string; platform: string; }
export interface BridgeError { command: string; message: string; }
export interface SceneInfo { name: SceneName; buildIndex: number; }
export interface SceneDownloadProgress { name: SceneName; progress: number; downloadedMB: number; totalMB: number; background: boolean; }
export interface SceneDownloadSize { name: SceneName; exists: boolean; cached: boolean; sizeMB: number; }
export interface SceneTrigger { targetScene: SceneName; text: string; }
export interface WorkerTransform { scene: string; active: boolean; x: number; y: number; z: number; heading: number; }
export interface OverviewSelection { name: string; objectName: string; description: string; sizeX: number; sizeY: number; sizeZ: number; }
export interface EquipmentInfo { type: "Turbine" | "Boiler" | "ControlRoom"; name: string; }
export interface EquipmentState {
  inRange: boolean; type: "None" | "Turbine" | "Boiler" | "ControlRoom"; name: string;
  activeAction: "none" | "explode" | "explodeAll" | "operation" | "info";
  exploded: boolean; operating: boolean; infoActive: boolean;
  canExplode: boolean; canExplodeAll: boolean; canOperate: boolean; canInfo: boolean;
}
export interface PartSelected { name: string; description: string; exploded: boolean; x: number; y: number; }
export interface TurbineData {
  id: string; name: string; type: string; steamPressure: number; temperature: number; temperatureF: number;
  vibration: number; rpm: number; steamMassFlowRate: number; operating: boolean;
}
export interface TurbineList { turbines: TurbineData[]; }
export interface BoilerDashboard {
  temperature: number; maxTemperature: number; waterLevel: number; steamPressure: number; maxPressure: number;
  burnerPower: number; valveOpening: number; status: "RUNNING" | "STOPPED" | "COMPLETED"; message: string;
  lowWater: boolean; completed: boolean;
}
export interface ElectricalValues {
  generatorValue: number; generatorKV: number; gridKV: number; loading: number; oilTemp: number; windingTemp: number;
  coolingFan: boolean; alarmLevel: "normal" | "high" | "warning";
}

// ---------------------------------------------------------------- Unity -> React events (parsed data type)

export interface UnityEvents {
  handleError: BridgeError;
  handleSceneLoadFailed: BridgeError;
  handleUnityReady: UnityStatus;
  handleCursorLockChanged: boolean;
  handleWorkerTransform: WorkerTransform;
  handleSceneLoading: string;
  handleSceneDownloadProgress: SceneDownloadProgress;
  handleSceneLoaded: SceneInfo;
  handleScenePreloaded: string;
  handleSceneDownloadSize: SceneDownloadSize;
  handleSceneTriggerEntered: SceneTrigger;
  handleSceneTriggerExited: SceneTrigger;
  handleCameraModeChanged: string;
  handleOverviewObjectSelected: OverviewSelection;
  handleOverviewObjectDeselected: void;
  handleEquipmentInRange: EquipmentInfo;
  handleEquipmentOutOfRange: EquipmentInfo;
  handleBoilerInRange: EquipmentInfo;
  handleBoilerOutOfRange: EquipmentInfo;
  handleEquipmentState: EquipmentState;
  handlePartHover: string;
  handlePartHoverEnd: void;
  handlePartSelected: PartSelected;
  handlePartDeselected: void;
  handleTurbineData: TurbineData;
  handleTurbineList: TurbineList;
  handleBoilerDashboardOpened: BoilerDashboard;
  handleBoilerDashboard: BoilerDashboard;
  handleBoilerDashboardClosed: void;
  handleElectricalPanelOpened: number;
  handleElectricalValues: ElectricalValues;
  handleElectricalPanelClosed: void;
  handleControlRoomInfoEntered: { index: number; name: string; title: string };
  handleControlRoomInfoExited: { index: number; name: string; title: string };
}

// ---------------------------------------------------------------- React -> Unity functions (value type)

export interface UnityFunctions {
  Ping_Extern: void;
  SetKeyboardCapture_Extern: string;
  SetPointerOverUI_Extern: string;
  SetCursorLocked_Extern: string;
  SetWorkerTracking_Extern: string;
  SetVolume_Extern: string;
  SetMiniMapVisible_Extern: string;
  ChangeScene_Extern: string;
  GetSceneDownloadSize_Extern: string;
  PreloadScene_Extern: string;
  SetCameraMode_Extern: string;
  ToggleCameraMode_Extern: void;
  ResetOverviewView_Extern: void;
  GetCameraMode_Extern: void;
  GetEquipmentState_Extern: void;
  ToggleExplode_Extern: void;
  ToggleExplodeAll_Extern: void;
  ToggleOperation_Extern: void;
  StartBoilerInfo_Extern: void;
  BoilerExplodeAll_Extern: void;
  BoilerCollapseAll_Extern: void;
  BoilerStartOperation_Extern: void;
  BoilerStopOperation_Extern: void;
  BoilerShowInfo_Extern: void;
  BoilerHideInfo_Extern: void;
  SetBoilerBurnerPower_Extern: string;
  CollapseEquipment_Extern: void;
  TogglePartExplode_Extern: void;
  ClearPartSelection_Extern: void;
  GetAllTurbineData_Extern: void;
  GetTurbineData_Extern: string;
  SetBurnerPower_Extern: string;
  SetValveOpening_Extern: string;
  RestartBoiler_Extern: void;
  CloseBoilerDashboard_Extern: void;
  GetBoilerDashboard_Extern: void;
  SetGeneratorValue_Extern: string;
  SetControlRoomSlider_Extern: string;
}

// ---------------------------------------------------------------- helpers

type SendMessage = (gameObjectName: string, methodName: string, parameter?: string | number) => void;
type AddListener = (eventName: string, callback: (...parameters: any[]) => any) => void;

/** Call a Unity function: callUnity(sendMessage, "ChangeScene_Extern", "BoilerRoom") */
export function callUnity<K extends keyof UnityFunctions>(
  sendMessage: SendMessage,
  fn: K,
  ...value: UnityFunctions[K] extends void ? [] : [UnityFunctions[K]]
) {
  if (value.length) sendMessage(UNITY_BRIDGE_OBJECT, fn, value[0] as string);
  else sendMessage(UNITY_BRIDGE_OBJECT, fn);
}

/** Unity sends strings; this turns them into the typed value (JSON parsed, "true" -> true, "12" -> 12). */
function parseUnityData(event: keyof UnityEvents, raw?: string): any {
  if (raw === undefined) return undefined;
  if (raw === "true" || raw === "false") return raw === "true";
  if (raw.startsWith("{") || raw.startsWith("[")) return JSON.parse(raw);
  if (event === "handleElectricalPanelOpened") return Number(raw);
  return raw;
}

/** Listen to a Unity event with typed data. Removes the listener on unmount. */
export function useUnityEvent<K extends keyof UnityEvents>(
  addEventListener: AddListener,
  removeEventListener: AddListener,
  event: K,
  callback: (data: UnityEvents[K]) => void
) {
  useEffect(() => {
    const handler = (raw?: string) => callback(parseUnityData(event, raw));
    addEventListener(event, handler);
    return () => removeEventListener(event, handler);
  }, [addEventListener, removeEventListener, event, callback]);
}

/*
---------------------------------------------------------------- example

const { unityProvider, sendMessage, addEventListener, removeEventListener, isLoaded } = useUnityContext({
  loaderUrl: "/unity/Build/ThermalPlant.loader.js",
  dataUrl: "/unity/Build/ThermalPlant.data.br",
  frameworkUrl: "/unity/Build/ThermalPlant.framework.js.br",
  codeUrl: "/unity/Build/ThermalPlant.wasm.br",
  streamingAssetsUrl: "/unity/StreamingAssets",          // REQUIRED: scenes download from here
});

// SIMPLE TEST BUILD (Tools > Thermal Plant > Build Mode > Simple Test Build): no compression,
// so the file names have NO ".br": Build/ThermalPlant.data, Build/ThermalPlant.framework.js, Build/ThermalPlant.wasm.
// Scenes are inside the build; handleSceneDownloadProgress then reports loading progress with totalMB = 0.

// Loading screen
useUnityEvent(addEventListener, removeEventListener, "handleSceneDownloadProgress", (p) => {
  if (!p.background) setProgress(p.progress);
});
useUnityEvent(addEventListener, removeEventListener, "handleSceneLoaded", (s) => setScene(s.name));

// Equipment buttons
const [state, setState] = useState<EquipmentState>();
useUnityEvent(addEventListener, removeEventListener, "handleEquipmentState", setState);
<button disabled={!state?.canExplode} onClick={() => callUnity(sendMessage, "ToggleExplode_Extern")}>Explode</button>

// Every React panel over the canvas
<div onMouseEnter={() => callUnity(sendMessage, "SetPointerOverUI_Extern", "true")}
     onMouseLeave={() => callUnity(sendMessage, "SetPointerOverUI_Extern", "false")}> ... </div>

// Room button
callUnity(sendMessage, "ChangeScene_Extern", "BoilerRoom");
*/
