using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Thermal Plant, part 8:
///   18. Update Plant UI (keeps your edits) – adds the new parts to the Plant UI that is already in Bootstrap,
///       in the colours / font you gave it (sampled from the nav bar), without rebuilding anything:
///         - Minimap inside the Plant UI canvas (bottom-right) – the old separate MiniMapCanvas is removed
///         - Control room guided tour card (top-right) + "Guided Tour" start pill
///         - Explode All (at a boiler / turbine) and Collapse All (explosion view) buttons
///         - Home button removed from the nav bar
///       Every part is only added when it is missing, so running it again is safe.
/// </summary>
public static partial class ThermalPlantTools
{
    // Style of the existing Plant UI (sampled), used for the new parts.
    private static Color styPanel = new Color(0.07f, 0.09f, 0.17f, 0.95f);
    private static Color styText = new Color(0.92f, 0.94f, 0.98f, 1f);
    private static Color styAccent = new Color(0.17f, 0.27f, 0.6f, 1f);
    private static TMP_FontAsset styFont;

    private static readonly Color TourOrange = new Color(1f, 0.62f, 0.25f, 1f);
    private static readonly Color TourMuted = new Color(0.55f, 0.6f, 0.72f, 1f);

    // ===================================================================================== 18. Update Plant UI

    [MenuItem(MenuRoot + "18. Update Plant UI (keeps your edits)", priority = 18)]
    private static void UpdatePlantUI()
    {
        if (!System.IO.File.Exists(BootstrapPath))
        {
            EditorUtility.DisplayDialog("Plant UI", "Bootstrap scene not found.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);
        PlantUI ui = FindInScene<PlantUI>(scene);
        if (ui == null)
        {
            EditorUtility.DisplayDialog("Plant UI", "No Plant UI in Bootstrap yet. Run '17. Create Plant UI (Bootstrap)' first.", "OK");
            return;
        }

        var log = new StringBuilder();
        string prefabPath = PrefabUtility.IsPartOfPrefabInstance(ui.gameObject)
            ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(ui.gameObject) : null;

        if (!string.IsNullOrEmpty(prefabPath))
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                AddPlantUIExtras(contents, log);
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                log.AppendLine("= saved in the prefab " + prefabPath + " (the Bootstrap copy follows it)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        else
        {
            AddPlantUIExtras(ui.gameObject, log);
        }

        RemoveOldMiniMapCanvas(scene, log);
        EnsurePlantUIEventSystem(scene, log);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = FindInScene<PlantUI>(scene) != null ? FindInScene<PlantUI>(scene).gameObject : null;

        Report("Plant UI updated", log,
            "Your colours / layout are kept. New parts start hidden (the script shows them while playing): tick them on " +
            "to restyle - MiniMap/Panel, TourPanel/Card, ExplodeAllButton, CollapseAllButton.");
    }

    private static void RemoveOldMiniMapCanvas(Scene scene, StringBuilder log)
    {
        var old = new List<MiniMapPanel>();
        foreach (MiniMapPanel panel in FindAllInScene<MiniMapPanel>(scene))
            if (panel.GetComponentInParent<PlantUI>(true) == null) old.Add(panel);

        foreach (MiniMapPanel panel in old)
        {
            if (panel == null) continue;
            GameObject go = panel.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(go)) go = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            log.AppendLine("- old minimap canvas '" + go.name + "' removed (the minimap is in the Plant UI now)");
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>Adds the missing parts to a Plant UI root (prefab contents or scene object).</summary>
    private static void AddPlantUIExtras(GameObject root, StringBuilder log)
    {
        SamplePlantUIStyle(root);
        PlantUI plantUI = root.GetComponent<PlantUI>();
        var so = new SerializedObject(plantUI);

        // ---- Home button off the nav bar
        PlantNavBar nav = root.GetComponentInChildren<PlantNavBar>(true);
        if (nav != null)
        {
            var navSO = new SerializedObject(nav);
            SerializedProperty home = navSO.FindProperty("homeButton");
            if (home != null && home.objectReferenceValue != null)
            {
                Object.DestroyImmediate(((Component)home.objectReferenceValue).gameObject);
                home.objectReferenceValue = null;
                navSO.ApplyModifiedPropertiesWithoutUndo();
                Transform separator = nav.transform.Find("Separator");
                if (separator != null) Object.DestroyImmediate(separator.gameObject);
                log.AppendLine("- Home button removed from the nav bar");
            }
        }

        // ---- Explode All / Collapse All
        if (so.FindProperty("explodeAllButton").objectReferenceValue == null)
        {
            so.FindProperty("explodeAllButton").objectReferenceValue = BuildActionPill(root.transform, "ExplodeAllButton", "explode", "Explode All", 104f);
            log.AppendLine("+ Explode All button (bottom, above the nav bar - shown at a boiler / turbine)");
        }
        if (so.FindProperty("collapseAllButton").objectReferenceValue == null)
        {
            so.FindProperty("collapseAllButton").objectReferenceValue = BuildActionPill(root.transform, "CollapseAllButton", "implode", "Collapse All", 24f);
            log.AppendLine("+ Collapse All button (bottom - the only button in the explosion view)");
        }

        // ---- Minimap
        if (root.GetComponentInChildren<MiniMapPanel>(true) == null)
        {
            BuildPlantMiniMap(root.transform, log);
            log.AppendLine("+ MiniMap inside the Plant UI (bottom-right, title per area)");
        }

        // ---- Tour
        if (so.FindProperty("tourPanel").objectReferenceValue == null)
        {
            so.FindProperty("tourPanel").objectReferenceValue = BuildTourPanel(root.transform);
            log.AppendLine("+ Guided tour card (top-right, Control Room only)");
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        // The maintenance sheet stays on top of everything.
        Transform slot = root.transform.Find("SheetSlot");
        if (slot != null) slot.SetAsLastSibling();
        if (log.Length == 0) log.AppendLine("= everything was already there");
    }

    private static void SamplePlantUIStyle(GameObject root)
    {
        styPanel = PuiPanel;
        styText = PuiTextColor;
        styAccent = PuiHighlight;
        styFont = null;

        PlantNavBar nav = root.GetComponentInChildren<PlantNavBar>(true);
        if (nav != null)
        {
            if (nav.TryGetComponent(out Image background)) styPanel = background.color;
            var so = new SerializedObject(nav);
            SerializedProperty active = so.FindProperty("activeBackground");
            SerializedProperty normal = so.FindProperty("normalText");
            if (active != null) styAccent = active.colorValue;
            if (normal != null) styText = normal.colorValue;
        }

        TMP_Text anyText = root.GetComponentInChildren<TMP_Text>(true);
        if (anyText != null) styFont = anyText.font;
    }

    // ------------------------------------------------------------------ explode / collapse

    private static Button BuildActionPill(Transform parent, string name, string icon, string label, float y)
    {
        RectTransform rect = NewUI(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, y);
        Image image = AddImage(rect, styAccent, PuiSprite("rounded"));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        PuiHorizontal(rect, 10, new RectOffset(20, 24, 12, 12));
        ContentSizeFitter fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        PuiIcon(rect, "Icon", icon, 22f, Color.white);
        PuiText(rect, "Label", label, 19, true, Color.white, TextAlignmentOptions.Left);
        rect.gameObject.SetActive(false);
        return button;
    }

    // ------------------------------------------------------------------ minimap

    private static void BuildPlantMiniMap(Transform parent, StringBuilder log)
    {
        // Holder (always on, no graphic) carries the script; its Panel is shown / hidden.
        RectTransform holder = NewUI("MiniMap", parent);
        Anchor(holder, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        MiniMapPanel script = holder.gameObject.AddComponent<MiniMapPanel>();

        RectTransform panel = NewUI("Panel", holder);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-24f, 24f);
        panel.sizeDelta = new Vector2(320f, 250f);
        AddImage(panel, styPanel, PuiSprite("rounded"));
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = PuiBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        TextMeshProUGUI title = PuiText(panel, "Title", "Thermal Plant Layout", 18, true, styText, TextAlignmentOptions.Left);
        Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -42f), new Vector2(-54f, -8f));

        Button expand = PuiIconButton(panel, "ExpandButton", "expand", 32f, 18f, PuiButton, out _);
        var expandRect = (RectTransform)expand.transform;
        expandRect.anchorMin = expandRect.anchorMax = expandRect.pivot = new Vector2(1f, 1f);
        expandRect.anchoredPosition = new Vector2(-10f, -8f);

        RectTransform frame = NewUI("MapFrame", panel);
        Anchor(frame, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -48f));
        AddImage(frame, new Color(0f, 0f, 0f, 0.3f), PuiSprite("rounded"));
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        RectTransform mapRect = NewUI("MapImage", frame);
        mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = new Vector2(0.5f, 0.5f);
        mapRect.sizeDelta = new Vector2(300f, 192f);
        Image map = mapRect.gameObject.AddComponent<Image>();
        map.raycastTarget = true; // clicks on the map never reach the world

        RectTransform worker = NewUI("WorkerIcon", mapRect);
        worker.anchorMin = worker.anchorMax = worker.pivot = new Vector2(0.5f, 0.5f);
        worker.sizeDelta = new Vector2(22f, 22f);
        Image workerImage = worker.gameObject.AddComponent<Image>();
        workerImage.sprite = GetOrCreateWorkerIcon(log);
        workerImage.preserveAspect = true;
        workerImage.raycastTarget = false;

        RectTransform north = NewUI("NorthIcon", frame);
        north.anchorMin = north.anchorMax = new Vector2(1f, 1f);
        north.anchoredPosition = new Vector2(-20f, -20f);
        north.sizeDelta = new Vector2(28f, 28f);
        Image northBg = north.gameObject.AddComponent<Image>();
        northBg.sprite = PuiSprite("dot");
        northBg.color = styPanel;
        northBg.raycastTarget = false;
        TextMeshProUGUI northLabel = PuiText(north, "N", "N", 14, true, styText, TextAlignmentOptions.Center);
        Anchor(northLabel.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Button zoomIn = PuiTextButton(frame, "ZoomInButton", "+", styPanel, styText, 30f, 30f, out _);
        Button zoomOut = PuiTextButton(frame, "ZoomOutButton", "-", styPanel, styText, 30f, 30f, out _);
        PlaceZoom((RectTransform)zoomIn.transform, 18f);
        PlaceZoom((RectTransform)zoomOut.transform, -18f);

        SetRef(script, "panel", panel);
        SetRef(script, "mapFrame", frame);
        SetRef(script, "mapImage", map);
        SetRef(script, "workerIcon", worker);
        SetRef(script, "zoomInButton", zoomIn);
        SetRef(script, "zoomOutButton", zoomOut);
        SetRef(script, "expandButton", expand);
        SetRef(script, "northIcon", north);
        SetRef(script, "title", title);

        panel.gameObject.SetActive(false); // shown while the worker walks a scene with a map
    }

    private static void PlaceZoom(RectTransform rect, float y)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-8f, y);
    }

    // ------------------------------------------------------------------ guided tour

    private static TourPanel BuildTourPanel(Transform parent)
    {
        RectTransform holder = NewUI("TourPanel", parent);
        Anchor(holder, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        TourPanel tour = holder.gameObject.AddComponent<TourPanel>();

        // Start pill (before the tour).
        RectTransform pill = NewUI("StartPill", holder);
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(1f, 1f);
        pill.anchoredPosition = new Vector2(-100f, -20f);
        Image pillImage = AddImage(pill, styPanel, PuiSprite("rounded"));
        Button startButton = pill.gameObject.AddComponent<Button>();
        startButton.targetGraphic = pillImage;
        PuiHorizontal(pill, 10, new RectOffset(16, 20, 10, 10));
        ContentSizeFitter pillFit = pill.gameObject.AddComponent<ContentSizeFitter>();
        pillFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        pillFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        PuiIcon(pill, "Icon", "guide", 22f, TourOrange);
        RectTransform pillText = NewUI("Text", pill);
        PuiVertical(pillText, 0, new RectOffset(0, 0, 0, 0));
        PuiText(pillText, "Title", "Guided Tour", 18, true, styText, TextAlignmentOptions.Left);
        PuiText(pillText, "Hint", "Voltage control & safety · 6 steps", 14, false, TourMuted, TextAlignmentOptions.Left);
        // "START" badge: only a picture - the whole pill is the button.
        Button badge = PuiTextButton(pill, "Start", "START", TourOrange, new Color(0.08f, 0.08f, 0.12f), 84f, 36f, out _);
        badge.targetGraphic.raycastTarget = false;
        Object.DestroyImmediate(badge);

        // Step card.
        RectTransform card = NewUI("Card", holder);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 1f);
        card.anchoredPosition = new Vector2(-100f, -20f);
        card.sizeDelta = new Vector2(440f, 400f);
        AddImage(card, styPanel, PuiSprite("rounded"));
        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = PuiBorder;
        outline.effectDistance = new Vector2(1f, -1f);
        PuiVertical(card, 12, new RectOffset(22, 22, 18, 20));
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform header = NewUI("Header", card);
        PuiHorizontal(header, 10, new RectOffset(0, 0, 0, 0));
        TextMeshProUGUI caption = PuiText(header, "Caption", "STEP 1 OF 6", 15, true, TourOrange, TextAlignmentOptions.Left);
        caption.characterSpacing = 3f;
        PuiFlexible(caption.rectTransform);
        RectTransform dots = NewUI("Dots", header);
        PuiHorizontal(dots, 8, new RectOffset(0, 0, 0, 0));
        Button close = PuiIconButton(header, "Close", "close", 32f, 16f, PuiButton, out _);

        TextMeshProUGUI title = PuiText(card, "Title", "Safety Briefing", 26, true, Color.white, TextAlignmentOptions.Left);
        TextMeshProUGUI body = PuiText(card, "Body", "Instruction", 17, false, styText, TextAlignmentOptions.TopLeft);
        body.lineSpacing = 6f;

        RectTransform voltageRow = NewUI("Voltage", card);
        PuiHorizontal(voltageRow, 12, new RectOffset(0, 0, 4, 4));
        TextMeshProUGUI vLabel = PuiText(voltageRow, "Label", "VOLTAGE", 15, true, TourMuted, TextAlignmentOptions.Left);
        vLabel.characterSpacing = 2f;
        Slider slider = PuiSlider(voltageRow);
        PuiFlexible((RectTransform)slider.transform);
        TextMeshProUGUI vValue = PuiText(voltageRow, "Value", "0 %", 20, true, new Color(0.35f, 0.88f, 0.5f), TextAlignmentOptions.Right);
        vValue.gameObject.AddComponent<LayoutElement>().minWidth = 64f;
        voltageRow.gameObject.SetActive(false);

        RectTransform statusRow = NewUI("Status", card);
        PuiHorizontal(statusRow, 8, new RectOffset(0, 0, 0, 0));
        Image tick = PuiIcon(statusRow, "Tick", "check", 22f, new Color(0.35f, 0.88f, 0.5f));
        tick.enabled = false;
        TextMeshProUGUI status = PuiText(statusRow, "Text", "", 18, true, TourOrange, TextAlignmentOptions.Left);
        PuiFlexible(status.rectTransform);

        RectTransform buttons = NewUI("Buttons", card);
        PuiHorizontal(buttons, 10, new RectOffset(0, 0, 4, 0));
        Button back = PuiTextButton(buttons, "Back", "BACK", PuiButton, styText, 120f, 44f, out _);
        RectTransform spacer = NewUI("Space", buttons);
        PuiFlexible(spacer);
        Button next = PuiTextButton(buttons, "Next", "NEXT", TourOrange, new Color(0.08f, 0.08f, 0.12f), 170f, 44f, out TextMeshProUGUI nextLabel);
        card.gameObject.SetActive(false);

        var so = new SerializedObject(tour);
        so.FindProperty("startPill").objectReferenceValue = pill.gameObject;
        so.FindProperty("startButton").objectReferenceValue = startButton;
        so.FindProperty("card").objectReferenceValue = card.gameObject;
        so.FindProperty("caption").objectReferenceValue = caption;
        so.FindProperty("title").objectReferenceValue = title;
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("dotsRow").objectReferenceValue = dots;
        so.FindProperty("dotSprite").objectReferenceValue = PuiSprite("dot");
        so.FindProperty("statusIcon").objectReferenceValue = tick;
        so.FindProperty("status").objectReferenceValue = status;
        so.FindProperty("voltageRow").objectReferenceValue = voltageRow.gameObject;
        so.FindProperty("voltageSlider").objectReferenceValue = slider;
        so.FindProperty("voltageValue").objectReferenceValue = vValue;
        so.FindProperty("backButton").objectReferenceValue = back;
        so.FindProperty("nextButton").objectReferenceValue = next;
        so.FindProperty("nextLabel").objectReferenceValue = nextLabel;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.ApplyModifiedPropertiesWithoutUndo();

        holder.gameObject.SetActive(false); // PlantUI shows it in the control room
        return tour;
    }

    private static Button PuiTextButton(Transform parent, string name, string label, Color background, Color textColor,
        float width, float height, out TextMeshProUGUI text)
    {
        RectTransform rect = NewUI(name, parent);
        Image image = AddImage(rect, background, PuiSprite("rounded"));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = width;
        element.minHeight = element.preferredHeight = height;
        rect.sizeDelta = new Vector2(width, height);

        text = PuiText(rect, "Label", label, 17, true, textColor, TextAlignmentOptions.Center);
        Anchor(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return button;
    }
}
