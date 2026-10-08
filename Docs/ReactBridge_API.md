# Thermal Plant – Unity ⇄ React functions

Same pattern as our other projects (react-unity-webgl):

- **React → Unity:** `sendMessage("CommunicationManager", "<Function>_Extern", "<value>")`
- **Unity → React:** `addEventListener("handle<Event>", (data) => ...)`

Values are strings: `"true"`/`"false"`, numbers like `"42.5"`, names, or JSON (parse with `JSON.parse`).

## Loading
The WebGL build contains only a tiny **Bootstrap** scene. Main_Scene and every room are separate downloads from the build's `StreamingAssets` folder, cached by the browser after the first time.

- **Simple test build** (no downloads, no compression, for local testing): every scene is inside the build, file names have no `.br`. Same functions and events; `handleSceneDownloadProgress` then shows loading progress with `totalMB: 0`.
- Pass `streamingAssetsUrl` to `useUnityContext` (e.g. `"/unity/StreamingAssets"`) – **required**, otherwise scenes can't be downloaded.
- Startup: `handleUnityReady` → `handleSceneLoading("Main_Scene")` → `handleSceneDownloadProgress`… → `handleSceneLoaded`. Show your loading screen until `handleSceneLoaded`.
- After Main_Scene opens, Unity downloads the rooms quietly in the background (`handleSceneDownloadProgress` with `background: true`, then `handleScenePreloaded`). Don't block the UI for background progress.

## React → Unity (`sendMessage("CommunicationManager", name, value)`)

### App / input
| Function | Value | Notes |
|---|---|---|
| `Ping_Extern` | – | Answer: `handleUnityReady` |
| `SetPointerOverUI_Extern` | `"true"`/`"false"` | **Send on mouseenter / mouseleave of every React panel over the canvas.** Unity ignores clicks, scroll and hover under the panel. |
| `SetKeyboardCapture_Extern` | `"true"`/`"false"` | Default false, so React text fields get the keyboard |
| `SetCursorLocked_Extern` | `"true"`/`"false"` | Unlock before showing a panel in worker mode. A lock may need one click on the canvas (browser rule). |
| `SetWorkerTracking_Extern` | `"true"`, `"false"`, `"true/0.2"` | Streams `handleWorkerTransform` every 0.2 s (for a React minimap) |
| `SetVolume_Extern` | `"0"`..`"1"` | Master volume of all sounds (boiler boiling, turbine hum, alarms) |
| `SetMiniMapVisible_Extern` | `"true"`/`"false"` | Show/hide the Unity minimap (bottom-right). It also hides by itself in overview and explosion view. |
| `SetTopInset_Extern` | `"48"` (CSS px) | Height of the React top bar over the Unity view; the Unity UI stays below it. Default 48, `"0"` = full view. |

### Scenes
| Function | Value | Notes |
|---|---|---|
| `ChangeScene_Extern` | `"Main_Scene"`, `"BoilerRoom"`, `"TurbineRoom"`, `"Control_Room"` | → `handleSceneLoading`, `handleSceneDownloadProgress`…, `handleSceneLoaded` or `handleSceneLoadFailed` |
| `GetSceneDownloadSize_Extern` | scene name | Answer: `handleSceneDownloadSize` `{ name, exists, cached, sizeMB }` |
| `PreloadScene_Extern` | scene name | Download without opening. Done: `handleScenePreloaded` |

### Main_Scene camera
The scene starts in **overview** (worker hidden). Right-click on the floor teleports the worker (always third person); key **C** toggles.
Clicking a highlighted building opens a small **Unity** info card beside it (name + description). React gets `handleOverviewObjectSelected` too (e.g. for a breadcrumb) but doesn't need to draw that card.
The **minimap** is Unity UI (bottom-right, about 300 × 230 px at 1920 × 1080) – keep React panels out of that corner.
| Function | Value | Notes |
|---|---|---|
| `SetCameraMode_Extern` | `"overview"` / `"worker"` | Answer: `handleCameraModeChanged` |
| `ToggleCameraMode_Extern` | – | |
| `GetCameraMode_Extern` | – | Answer: `handleCameraModeChanged` |
| `SetWorkerView_Extern` | `"tpp"` / `"fpp"` / `"fly"` | Worker camera: third person / first person / fly camera (same as keys 1 / 2 / 3; needs the worker). Answer: `handleWorkerViewChanged` |
| `ToggleWorkerView_Extern` | – | TPP → FPP → Fly → TPP. Answer: `handleWorkerViewChanged` |
| `GetWorkerView_Extern` | – | Answer: `handleWorkerViewChanged` with the current view |
| `ResetOverviewView_Extern` | – | Overview only |

### Equipment (turbine / boiler / control room)
Walking up to equipment sends `handleEquipmentInRange` + `handleEquipmentState`. **Enable/disable buttons only from the `can*` flags of `handleEquipmentState`.**
| Function | Value | Notes |
|---|---|---|
| `GetEquipmentState_Extern` | – | Answer: `handleEquipmentState` |
| `ToggleExplode_Extern` | – | Explosion view on/off |
| `ToggleExplodeAll_Extern` | – | Explode all on/off |
| `ToggleOperation_Extern` | – | Turbine: animated + exploded. Boiler: fluid process |
| `StartBoilerInfo_Extern` | – | Boiler only; opens the boiler dashboard |
| `CollapseEquipment_Extern` | – | Leave any explosion view / turbine operation |
| `TogglePartExplode_Extern` | – | Part from the last `handlePartSelected` |
| `ClearPartSelection_Extern` | – | Close the part menu |

### Main_Scene visuals
| Function | Value | Notes |
|---|---|---|
| `SetSmokeLevel_Extern` | `"0"`..`"1"` (default 0.2) | Chimney smoke colour: 0 none, 0.2 white, 0.4 dark gray, 0.6 black, 0.8 yellow-brown, 1 blue-gray. Answer: `handleSmokeLevelChanged` |
| `GetSmokeLevel_Extern` | – | Answer: `handleSmokeLevelChanged` |
| `StartControlRoomTour_Extern` | – | Control room: starts the 6-step voltage control + safety tour. Answer: `handleTourChanged` |
| `TourNext_Extern` / `TourBack_Extern` | – | Next / previous step (Next only when the step is done; on the last screen it closes the tour). |
| `StopControlRoomTour_Extern` | – | Ends the tour. |
| `GetTourState_Extern` | – | Answer: `handleTourChanged` |
| `SetTourUnityUI_Extern` | `"true"` / `"false"` | `false`: Unity hides its START GUIDED TOUR button and step panel (React draws the tour). |
| `SetTimeOfDay_Extern` | `"day"` / `"evening"` / `"night"` | Answer: `handleTimeOfDayChanged`. Unity key: N |
| `GetTimeOfDay_Extern` | – | Main_Scene: answer `handleTimeOfDayChanged` with the current time (to set the toggle). |

### Turbine room (one function per button, acts on the turbine the worker is at)
Near turbine 1-4 Unity sends `handleTurbineInRange` { id: "1".."4", name: "Turbine-1", operating, ... }; walking away sends `handleTurbineOutOfRange`.
| Function | Value | Notes |
|---|---|---|
| `TurbineExplodeAll_Extern` | – | Explode all parts |
| `TurbineCollapseAll_Extern` | – | Collapse all (also stops operation), back to the worker |
| `TurbineStartOperation_Extern` | – | Operation: exploded + spinning + steam; live values via `handleTurbineData` |
| `TurbineStopOperation_Extern` | – | Stop operation |

### Boiler room (one function per button)
Near the boiler Unity sends `handleBoilerInRange` (show the boiler buttons); walking away sends `handleBoilerOutOfRange`.
| Function | Value | Notes |
|---|---|---|
| `BoilerExplodeAll_Extern` | – | Explode all parts |
| `BoilerCollapseAll_Extern` | – | Collapse all, back to the worker |
| `BoilerStartOperation_Extern` | – | Start the operation animation |
| `BoilerStopOperation_Extern` | – | Stop the operation animation |
| `BoilerShowInfo_Extern` | – | Info panel + particle effects (also sends `handleBoilerDashboardOpened`) |
| `BoilerHideInfo_Extern` | – | Close the info panel + particles |
| `SetBoilerBurnerPower_Extern` | `"0"`..`"100"` | Burner power slider; works any time in the boiler room |

### Control room
Walking into info point 1, 2 or 3 sends `handleControlRoomInfoEntered` { index, name, title }: show info panel `index`. Leaving sends `handleControlRoomInfoExited`.
| Function | Value | Notes |
|---|---|---|
| `SetControlRoomSlider_Extern` | `"0"`..`"100"` | Works anywhere in the control room. From 80 the warning lights blink. Answer: `handleElectricalValues` (`alarmLevel`: normal / high / warning) |

### Turbines
| Function | Value | Notes |
|---|---|---|
| `GetAllTurbineData_Extern` | – | Answer: `handleTurbineList` |
| `GetTurbineData_Extern` | turbine id, e.g. `"1"` | Answer: `handleTurbineData` |

### Boiler dashboard (open after `StartBoilerInfo_Extern`)
| Function | Value |
|---|---|
| `SetBurnerPower_Extern` | `"0"`..`"100"` |
| `SetValveOpening_Extern` | `"0"`..`"100"` |
| `RestartBoiler_Extern` | – (after `completed: true`) |
| `CloseBoilerDashboard_Extern` | – (also ends the info sequence) |
| `GetBoilerDashboard_Extern` | – Answer: `handleBoilerDashboard` |

### Electrical panel (Control_Room)
| Function | Value |
|---|---|
| `SetGeneratorValue_Extern` | `"0"`..`"100"` – only while the worker is at the panel |

## Unity → React (`addEventListener(name, (data) => ...)`)

| Event (addEventListener) | Data | When |
|---|---|---|
| `handleError` | JSON `BridgeError` | Something React asked for could not be done: { command, message }. |
| `handleSceneLoadFailed` | JSON `BridgeError` | Scene could not be downloaded/loaded: { command = scene name, message }. |
| `handleUnityReady` | JSON `UnityStatus` | Unity started (sent once). Also the answer to Ping_Extern. |
| `handleCursorLockChanged` | `"true"`/`"false"` | "true"/"false" – cursor locked (worker look mode) or free. Also fires when the user presses Esc. |
| `handleWorkerTransform` | JSON `WorkerTransform` | Worker position/heading, while tracking is on (SetWorkerTracking_Extern). For a React minimap. |
| `handleSceneLoading` | string | Scene name – a scene change started. |
| `handleSceneDownloadProgress` | JSON `SceneDownloadProgress` | Download progress of a scene that is not cached yet. |
| `handleSceneLoaded` | JSON `SceneInfo` | A scene finished loading. |
| `handleScenePreloaded` | string | Scene name – finished downloading in the background. |
| `handleSceneDownloadSize` | JSON `SceneDownloadSize` | Answer to GetSceneDownloadSize_Extern. |
| `handleSceneTriggerEntered` | JSON `SceneTrigger` | Worker stands at a door to another room: show "Go to …" (button calls ChangeScene_Extern). |
| `handleSceneTriggerExited` | JSON `SceneTrigger` | Worker left the door. |
| `handleCameraModeChanged` | string | "overview" or "worker". |
| `handleWorkerViewChanged` | string | "tpp", "fpp" or "fly" - also when the user presses 1 / 2 / 3. |
| `handleOverviewObjectSelected` | JSON `OverviewSelection` | Building clicked in plant overview. |
| `handleOverviewObjectDeselected` | – | Overview selection cleared. |
| `handleEquipmentInRange` | JSON `EquipmentInfo` | Worker reached a turbine / boiler / control room. |
| `handleEquipmentOutOfRange` | JSON `EquipmentInfo` | Worker walked away. |
| `handleControlRoomInfoEntered` | JSON `{index,name,title}` | Worker in control room info point 1/2/3: show that info panel. |
| `handleControlRoomInfoExited` | JSON `{index,name,title}` | Worker left the info point: hide it. |
| `handlePartMaintenance` | JSON `MaintenanceInfo` | Maintenance sheet of the part clicked in the explosion view (Unity also shows it bottom-right). Fields: partName, partId, equipment, lastMaintenanceDate, maintenanceType, condition (Good / Warning / Critical), runningHours, nextMaintenanceDue, technician, issueFound, actionTaken, sparePartUsed, remarks, description. Sent again with the typed values when the user edits the sheet in Unity and presses DONE (Part Name, Part ID and Equipment are not editable; Description is hidden while editing. Edits are not stored; PRINT REPORT (always on the sheet) downloads the sheet without Description + a part picture as .xlsx in the browser). |
| `handleTourChanged` | JSON `TourStatePayload` | Tour started / step changed / live progress / done / stopped: active, completed, step, total, title, instruction, status, stepDone, canNext, canBack, target. |
| `handleSmokeLevelChanged` | JSON `{level,status,indication,color}` | Smoke level changed (also at Main_Scene start). status: none / normal / degrading / severe / nox_sox / contamination. |
| `handleTimeOfDayChanged` | string | "day" / "evening" / "night" (also sent when Main_Scene starts). |
| `handleTurbineInRange` | JSON `TurbineData` | Worker near turbine `id` 1-4: show its buttons. |
| `handleTurbineOutOfRange` | JSON `TurbineData` | Worker left that turbine. |
| `handleBoilerInRange` | JSON `EquipmentInfo` | Worker is near the boiler: show the boiler buttons. |
| `handleBoilerOutOfRange` | JSON `EquipmentInfo` | Worker left the boiler: hide the boiler buttons. |
| `handleEquipmentState` | JSON `EquipmentState` | Full button state. Enable/disable React buttons from the can* flags. |
| `handlePartHover` | string | Part name under the mouse (explosion view). |
| `handlePartHoverEnd` | – | Mouse left the part. |
| `handlePartSelected` | JSON `PartSelected` | Part clicked: show its menu at x/y (0..1 of canvas, top-left). |
| `handlePartDeselected` | – | Close the part menu. |
| `handleTurbineData` | JSON `TurbineData` | Live values of one turbine (every few seconds while the worker is there or it operates). |
| `handleTurbineList` | JSON `TurbineList` | Answer to GetAllTurbineData_Extern. |
| `handleBoilerDashboardOpened` | JSON `BoilerDashboard` | Boiler dashboard opened (after StartBoilerInfo_Extern). |
| `handleBoilerDashboard` | JSON `BoilerDashboard` | Live boiler values (max 5x per second, only on change). |
| `handleBoilerDashboardClosed` | – | Boiler dashboard closed. |
| `handleElectricalPanelOpened` | number as string | Worker at the electrical panel; value = current generator slider value (0-100). |
| `handleElectricalValues` | JSON `ElectricalValues` | Panel values after every change. |
| `handleElectricalPanelClosed` | – | Worker left the panel. |

### JSON shapes
```ts
UnityStatus        { scene, buildIndex, unityVersion, appVersion, platform }
BridgeError        { command, message }
SceneInfo          { name, buildIndex }
SceneDownloadProgress { name, progress (0..1), downloadedMB, totalMB, background }
SceneDownloadSize  { name, exists, cached, sizeMB }
SceneTrigger       { targetScene, text }
WorkerTransform    { scene, active, x, y, z, heading }
OverviewSelection  { name, objectName, description, sizeX, sizeY, sizeZ }   // name/description from ObjectInfo
EquipmentInfo      { type: "Turbine"|"Boiler"|"ControlRoom", name }
EquipmentState     { inRange, type, name, activeAction: "none"|"explode"|"explodeAll"|"operation"|"info",
                     exploded, operating, infoActive, canExplode, canExplodeAll, canOperate, canInfo }
PartSelected       { name, description, exploded, x, y }   // x,y = 0..1 of canvas from top-left
TurbineData        { id, name, type, steamPressure, temperature, temperatureF, vibration, rpm, steamMassFlowRate, operating }
TurbineList        { turbines: TurbineData[] }
BoilerDashboard    { temperature, maxTemperature, waterLevel, steamPressure, maxPressure, burnerPower, valveOpening,
                     status: "RUNNING"|"STOPPED"|"COMPLETED", message, lowWater, completed }
ElectricalValues   { generatorValue, generatorKV, gridKV, loading, oilTemp, windingTemp, coolingFan,
                     alarmLevel: "normal"|"high"|"warning" }
```
While `EquipmentState.exploded` is true the worker is off and an orbit camera is active: hide worker-only UI (minimap).

## Adding a function (Unity side)
- **React → Unity:** add a public method `MyThing_Extern(string value)` to `CommunicationManager.cs` that calls the right singleton, e.g. `PumpController.Instance.Start(value)`.
- **Unity → React:** add `handleMyThing` to `Assets/Plugins/WebGL/React.jslib`, a matching `[DllImport("__Internal")] private static extern void handleMyThing(string data);` and a `public static void HandleMyThing_Extern(...)` wrapper with the `#if UNITY_WEBGL && !UNITY_EDITOR && REACT_BUILD` guard (copy an existing one).
- Then add it to this document.
