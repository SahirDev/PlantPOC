# Thermal Plant – what to do in Unity, step by step

Do the steps in this order. Back up the project first (copy the whole folder).
Tick each box as you go. If the Console shows red errors at any step, stop and send them to Claude.

---

## Step 1 – Addressables package
- [ ] **Full project zip:** already included (Packages/manifest.json) – Unity installs it when the project opens. Version 2.10.3 is the one Unity lists as released for 6000.3; stay on 2.x (3.x/4.x may change the API).
- [ ] **Scripts-only zip:** install it first: Window → Package Manager → **Unity Registry** → **Addressables** → Install. Without it the scripts don't compile.

## Step 2 – Open the project
- [ ] **Full project zip:** unzip `ThermalPlant_FullProject.zip` to a new folder and open it with Unity Hub (Unity 6000.3.22f1). The first import takes a while.
  - If you changed anything in your local project after sending it to Claude, use the **scripts-only zip** instead: unzip it over your project, then delete by hand (if present): `Assets/Scripts/PlantManager.cs`, `Assets/Scripts/UI/MainScene/MainSceneUIController.cs`, `Assets/Scripts/Communication/MainSceneBridge.cs`, `Assets/Plugins/WebGL/ReactBridge.jslib`.
- [ ] Console must have **no red errors** (install Addressables first – Step 1).

## Step 3 – Scene changes
- [ ] **Tools → Thermal Plant → 0. Clean Up Scenes (remove old UI)**. It opens each scene, removes the old UI Toolkit / uGUI screens and missing scripts, saves, and prints a report in the Console.
- [ ] Read the Console report. Lines starting with **`!`** need you:
  - **BoilerRoom has 2 workers** (`Worker` and `Worker (1)`) → delete one.
  - Any **PersistentPlayer** component → remove it (each scene has its own worker).
  - **Boiler dashboard active at start** → untick that object (the info sequence switches it on).
- [ ] Still by hand:
  - `ScenChangeCollider` objects: delete the old "Press Y" text panel they used (the colliders stay).
  - Main_Scene minimap – keep the Unity minimap (`MinmapParent`) **or** delete it and the **MiniMapCamera** under the Worker and let React draw it from `SetWorkerTracking_Extern` (cheaper on WebGL).
  - If no uGUI canvas is left in a scene, delete its **EventSystem**.

## Step 4 – Run the setup tools (menu **Tools → Thermal Plant**)
- [ ] **1. Setup Addressables + Bootstrap** – makes every scene a separate download, creates `Assets/Scenes/Bootstrap.unity`, sets Build Settings to Bootstrap only.
  - Open `Bootstrap.unity` → select **Managers** → check **HUDController → Explosion View Camera Prefab** is filled (the tool tries to find it; assign the RTS camera prefab by hand if empty).
- [ ] Window → Asset Management → Addressables → **Analyze** → select **Check Duplicate Bundle Dependencies** → **Analyze Selected Rules** → **Fix Selected Rules**.
  (Moves assets used by several scenes – worker, shared materials, shaders – into one shared bundle so they download once.)
- [ ] **2. Apply WebGL Player Settings** – Brotli compression, High code stripping, caching, and the **REACT_BUILD** scripting define (without it Unity sends nothing to React, same as your other project). Answer the server question (choose "No / not sure" if you are testing on a simple host).
- [ ] **3. Apply WebGL Texture Settings** – caps WebGL textures at 1024 + crunch. Takes a few minutes.
- [ ] **4. Apply WebGL Model Settings (optional)** – mesh compression + Read/Write off. Test afterwards (see Step 7).

## Step 5 – Lighting (smaller lightmaps; needs a re-bake)
For Main_Scene, BoilerRoom, TurbineRoom, Control_Room:
- [ ] Window → Rendering → **Lighting** → Scene tab → Lighting Settings asset:
  - **Directional Mode: Non-Directional** (halves lightmap size)
  - **Max Lightmap Size: 1024** (Main_Scene: try 512)
  - **Lightmap Resolution**: lower it if the bake still makes many lightmaps (try half of the current value)
  - Mixed Lighting → **Lighting Mode: Baked Indirect** (drops the shadowmask textures) – only if dynamic shadows from the sun still look right; otherwise keep Shadowmask
- [ ] **Generate Lighting**, check the scene looks fine, save.

## Step 6 – Render settings for WebGL (from the camera optimisation step)
- [ ] Project Settings → Quality: add a **WebGL** level using a copy of `PC_RPAsset` (call it `WebGL_RPAsset`), make it the WebGL default (green tick under the WebGL column).
- [ ] In `WebGL_RPAsset`: Depth Texture **off**, Opaque Texture **off**, HDR off (unless you use bloom), soft shadows Low/off, 1 shadow cascade, additional light shadows off, Light Cookies off, Light Layers off, lens flares off.
- [ ] Its renderer (copy of `PC_Renderer`): Rendering Path **Forward**, remove the **Screen Space Ambient Occlusion** feature.
- [ ] PlantOverviewCamera: Post Processing off (if you don't need bloom/tonemapping), Far Clip ~1200. Worker camera Far Clip ~300–400. MiniMapCamera (if kept): Shadows, Post Processing, Depth/Opaque texture off.
- [ ] Package Manager: remove **High Definition RP**, **Post Processing** (v2), **ProGrids**. Check nothing turns pink.

## Step 7 – Test in the Editor
- [ ] Open **Bootstrap.unity** → Play. Main_Scene should open by itself.
- [ ] Tools → **React Bridge Debugger**: you see `handleUnityReady`, `handleSceneLoading(Main_Scene)`, `handleSceneLoaded`.
- [ ] Pick `ChangeScene_Extern` → each room and back; `GetSceneDownloadSize_Extern`.
- [ ] Overview → right-click floor teleports, worker in TPP; C key switches (`handleCameraModeChanged`).
- [ ] Turbine: `ToggleOperation_Extern`; Boiler: `StartBoilerInfo_Extern` then `CloseBoilerDashboard_Extern`; Control room panel: `SetGeneratorValue_Extern` = 92.

## Step 8 – Build
- [ ] File → Build Profiles → **WebGL** → **Development Build OFF** → Build (Addressables are built automatically).
- [ ] The output folder contains `Build/`, `index.html` **and `StreamingAssets/`** (the scene downloads). Upload **all** of it.
- [ ] Note the sizes: total of `Build/` (first download) and of `StreamingAssets/aa/WebGL` (per-scene downloads). Send them to Claude with the **Build Report** from `Editor.log` (`%LOCALAPPDATA%\Unity\Editor\Editor.log`, search "Build Report").

## Step 9 – Test in the browser
- [ ] Serve the folder over HTTP (not by double-clicking index.html). Quick local test: in the build folder run `npx http-server -p 8080` (needs Node) and open http://localhost:8080. With Decompression Fallback on, this works without special headers.
- [ ] Browser DevTools → Network tab: on start only the Build files + Main_Scene bundle download; a room bundle downloads when you enter it (or in the background a few seconds after start). Reload the page: bundles come from cache.
- [ ] Test every room, explosion view, boiler dashboard, electrical panel.

## Step 10 – For the React team
- [ ] Give them `Docs/ReactBridge_API.md` and `Docs/unityBridge.ts`.
- [ ] Tell them where `StreamingAssets` is hosted – they must set `streamingAssetsUrl` in `useUnityContext`.
