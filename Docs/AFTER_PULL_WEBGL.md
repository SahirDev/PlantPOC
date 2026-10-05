# WebGL optimisation – what changed and what to do after pulling

## What was wrong (biggest first)

1. **Model textures were stored uncompressed.** The `.glb` models carry their textures inside. glTFast
   imports those as uncompressed RGBA32 textures that ignore every texture setting. *Thermal Plant.glb*
   (12 × 2048²) and *Steam Turbine Scene.glb* (34 × 2048²) alone are several hundred MB of raw pixels
   in the build and in browser memory.
   **Fix:** the pictures now sit next to each model in `<model>_Textures/` and glTFast uses them as
   normal Unity textures: DXT + crunch, WebGL max 1024, normal/metal/AO maps imported as linear.
2. **Web texture format was ASTC** (a mobile format). Desktop browsers can't use ASTC, so Unity
   unpacked those textures to RGBA32 at runtime, and crunch doesn't apply to ASTC. **Fix:** DXT.
3. **The build profile overrode the Player Settings**, so the Tools > Thermal Plant settings were ignored
   (it had 32 MB initial memory and Medium stripping). There were also two profiles with different settings.
   **Fix:** the duplicate was deleted and the override removed.
4. **The camera ran desktop-quality effects in the browser:** SSAO, Forward+, soft shadows, 2 cascades,
   additional-light shadows, light cookies and layers. **Fix:** a new **WebGL** quality level that is the
   WebGL default, using `WebGL_RPAsset` / `WebGL_Renderer`. **PC** keeps the full look in the editor.
5. **Unused packages:** HDRP, Post Processing v2, ProGrids, Visual Scripting, Multiplayer Center,
   Performance Testing and VS Code were removed. None of the project's scenes, prefabs or code used them.
6. **Code size:** IL2CPP now uses *smaller builds* for Web.
7. **Lightmaps:** the WebGL lighting settings are now *Non-Directional* (half the lightmap textures). This needs a re-bake.
8. **About 188 MB of unused files were removed.** The list is in `Docs/REMOVED_ASSETS.md`, and everything can be restored from git.

## After `git pull` – do these in order

1. Open the project. The first import takes a while because of the textures and the packages being removed.
2. **Tools → Thermal Plant → 8. Reimport glTF Models (after pulling).** This makes sure every model picks up
   its new texture files. Then check that BoilerRoom, TurbineRoom and Main_Scene look textured.
3. Check **Main_Scene → cooling tower / chimney**. Their materials (`Tower`, `Towerrings`) used a texture
   from inside the turbine model, and the polished-concrete one was assigned as the best match. If it
   looks wrong, pick another texture from `Assets/Model/Steam Turbine Scene/Steam_Turbine_Scene_Textures`.
4. **Tools → Thermal Plant → 9. Remove Old Minimap Leftovers.** This removes the old MiniMapCamera /
   MiniMap-Icon objects from the Worker prefab and the scenes.
5. **Re-bake lighting** in Main_Scene, BoilerRoom, TurbineRoom and Control_Room
   (Window → Rendering → Lighting → Generate Lighting). TurbineRoom and BoilerRoom still have large old lightmaps.
6. **Project Settings → Quality:** the green tick under the Web column should be on **WebGL**. In the editor,
   pick WebGL in the Game view's quality drop-down (or switch the platform to Web) to see what the browser will show.
7. Build (File → Build Profiles → *Web - Desktop - Release* → Build). Compare the size of `Build/` with the earlier 538 MB.

## Playing in the editor

**Tools → Thermal Plant → Play From Bootstrap** is on by default. Pressing Play in any scene now starts from
Bootstrap and then opens the scene you were editing, so the minimap panel, the React bridge and the managers
are always there, just like in the build.

## Still worth doing later (not done automatically)

- **The boilers are very heavy:** `Boiler.glb` and `fire Boiler1 GL1.glb` have about 1.4 million vertices each,
  and BoilerRoom uses both. Decimate them in Blender (e.g. Decimate 0.3–0.5) or use one model for both.
  This is now the largest part of the BoilerRoom download.
- *Standard Walk.fbx* (the worker) is 65 MB at source. If it has unused animations or blend shapes, removing them helps.
- When the React side is ready: **Build Mode → Addressables Build** (Brotli + per-scene downloads).
