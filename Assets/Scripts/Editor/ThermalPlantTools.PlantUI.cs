using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Thermal Plant, part 7:
///   18. Update Plant UI (keeps your edits) – adds what is new to the existing Plant UI: minimap inside the canvas,
///       guided tour card, Explode All / Collapse All buttons, no Home button; old MiniMapCanvas removed.
///   17. Create Plant UI (Bootstrap) – the one in-Unity UI canvas (DontDestroyOnLoad):
///       Boiler / Turbine / Electrical info panels (top-left), navigation bar (bottom), toolbar (right),
///       Day / Night button (top-right) and the slot the maintenance sheet opens in.
/// Saved as Assets/Prefabs/UI/PlantUI.prefab and placed in Bootstrap. After that, edit it freely in Bootstrap
/// (colours, sizes, texts, icons) – only keep the references on the PlantUI / EquipmentInfoPanel /
/// PlantNavBar / PlantToolbar components. Running the tool again asks before rebuilding (that loses edits).
/// </summary>
public static partial class ThermalPlantTools
{
    private const string PlantUIIconFolder = "Assets/UI/PlantUI/Icons/";
    private const string PlantUIPrefabPath = "Assets/Prefabs/UI/PlantUI.prefab";

    private static readonly Color PuiPanel = new Color(0.07f, 0.09f, 0.17f, 0.95f);
    private static readonly Color PuiBorder = new Color(0.35f, 0.45f, 0.8f, 0.35f);
    private static readonly Color PuiTextColor = new Color(0.92f, 0.94f, 0.98f, 1f);
    private static readonly Color PuiMuted = new Color(0.62f, 0.67f, 0.78f, 1f);
    private static readonly Color PuiSeparator = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color PuiButton = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color PuiHighlight = new Color(0.17f, 0.27f, 0.6f, 1f);
    private static readonly Color PuiTrack = new Color(0.3f, 0.35f, 0.5f, 1f);
    private static readonly Color PuiOrange = new Color(1f, 0.55f, 0.2f, 1f);
    private static readonly Color PuiStart = new Color(0.12f, 0.36f, 0.28f, 1f);

    private static readonly Color PuiRed = new Color(1f, 0.42f, 0.42f);
    private static readonly Color PuiBlue = new Color(0.38f, 0.62f, 1f);
    private static readonly Color PuiPurple = new Color(0.7f, 0.5f, 1f);
    private static readonly Color PuiAmber = new Color(1f, 0.75f, 0.3f);
    private static readonly Color PuiCyan = new Color(0.3f, 0.85f, 0.92f);
    private static readonly Color PuiRust = new Color(0.95f, 0.55f, 0.35f);

    private struct PuiRow
    {
        public string icon, label;
        public Color color;
        public PuiRow(string icon, string label, Color color) { this.icon = icon; this.label = label; this.color = color; }
    }

    // ===================================================================================== 17. Plant UI

    [MenuItem(MenuRoot + "17. Create Plant UI (Bootstrap)", priority = 17)]
    private static void CreatePlantUI()
    {
        if (!File.Exists(BootstrapPath))
        {
            EditorUtility.DisplayDialog("Plant UI", "Bootstrap scene not found. Run '1. Setup Addressables + Bootstrap' first.", "OK");
            return;
        }
        if (PuiSprite("rounded") == null)
        {
            EditorUtility.DisplayDialog("Plant UI", "Icons not found in " + PlantUIIconFolder + " – pull the branch again.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var log = new StringBuilder();
        Scene scene = EditorSceneManager.OpenScene(BootstrapPath, OpenSceneMode.Single);

        PlantUI existing = FindInScene<PlantUI>(scene);
        if (existing != null)
        {
            bool rebuild = EditorUtility.DisplayDialog("Plant UI",
                "Bootstrap already has a Plant UI. Rebuild it from scratch? (Your edits on it are lost.)", "Rebuild", "Keep it");
            if (!rebuild)
            {
                log.AppendLine("= PlantUI kept as it is");
                EnsurePlantUIEventSystem(scene, log);
                EditorSceneManager.SaveScene(scene);
                Report("Plant UI", log, "Nothing rebuilt.");
                return;
            }
            foreach (PlantUI old in FindAllInScene<PlantUI>(scene))
                if (old != null) Object.DestroyImmediate(old.gameObject);
            log.AppendLine("- old PlantUI removed");
        }

        GameObject root = BuildPlantUI();
        AddPlantUIExtras(root, log);
        Directory.CreateDirectory(Path.GetDirectoryName(PlantUIPrefabPath));
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PlantUIPrefabPath, InteractionMode.AutomatedAction);
        log.AppendLine("+ PlantUI canvas (prefab " + PlantUIPrefabPath + ")");
        log.AppendLine("  Boiler / Turbine / Electrical panels, NavBar, Toolbar, Day-Night, SheetSlot");

        RemoveOldMiniMapCanvas(scene, log);
        EnsurePlantUIEventSystem(scene, log);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;

        Report("Plant UI", log,
            "Bootstrap > PlantUI is selected. Edit it there (the panels are switched off – tick them on to edit, " +
            "they are switched by the script while playing). Press Play from Bootstrap to test.");
    }

    private static void EnsurePlantUIEventSystem(Scene scene, StringBuilder log)
    {
        if (FindInScene<EventSystem>(scene) != null) { log.AppendLine("= EventSystem already there"); return; }
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
        log.AppendLine("+ EventSystem (Input System UI module)");
    }

    private static GameObject BuildPlantUI()
    {
        GameObject root = CreateOverlayCanvas("PlantUI", 20, true);
        PlantUI plantUI = root.AddComponent<PlantUI>();

        // Maintenance sheet slot (full screen, the sheet keeps its own sorting order above the panels).
        RectTransform sheetSlot = NewUI("SheetSlot", root.transform);
        Anchor(sheetSlot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        EquipmentInfoPanel boiler = BuildEquipmentPanel(root.transform, EquipmentInfoPanel.Kind.Boiler, "Boiler Room",
            "Start or stop the boiler simulation", "Boiler Parameters",
            new[]
            {
                new PuiRow("thermometer", "Temperature", PuiRed),
                new PuiRow("droplet", "Water Level", PuiBlue),
                new PuiRow("gauge", "Steam Pressure", PuiPurple),
                new PuiRow("flame", "Burner Output", PuiAmber),
                new PuiRow("valve", "Steam Valve", PuiCyan),
                new PuiRow("coil", "Winding Temperature", PuiRust),
            }, "fire", "Burner Power");

        EquipmentInfoPanel turbine = BuildEquipmentPanel(root.transform, EquipmentInfoPanel.Kind.Turbine, "Turbine",
            "Start or stop the turbine simulation", "Turbine Parameters",
            new[]
            {
                new PuiRow("thermometer", "Temperature", PuiRed),
                new PuiRow("gauge", "Steam Pressure", PuiPurple),
                new PuiRow("fan", "Speed", PuiCyan),
                new PuiRow("droplet", "Steam Flow", PuiBlue),
                new PuiRow("wave", "Vibration", PuiAmber),
            }, "valve", "Steam Load");

        EquipmentInfoPanel electrical = BuildEquipmentPanel(root.transform, EquipmentInfoPanel.Kind.Electrical, "Electrical Panel",
            null, "Panel Parameters",
            new[]
            {
                new PuiRow("bolt", "Generator Side", PuiAmber),
                new PuiRow("plant", "Grid Side", PuiCyan),
                new PuiRow("gauge", "Loading", PuiPurple),
                new PuiRow("oil", "Oil Temperature", PuiRed),
                new PuiRow("coil", "Winding Temperature", PuiRust),
            }, "bolt", "Voltage");

        PlantNavBar navBar = BuildNavBar(root.transform);
        PlantToolbar toolbar = BuildToolbar(root.transform);
        BuildDayNight(root.transform, toolbar);

        sheetSlot.SetAsLastSibling();

        SetRef(plantUI, "boilerPanel", boiler);
        SetRef(plantUI, "turbinePanel", turbine);
        SetRef(plantUI, "electricalPanel", electrical);
        SetRef(plantUI, "navBar", navBar);
        SetRef(plantUI, "toolbar", toolbar);
        SetRef(plantUI, "sheetParent", sheetSlot);
        return root;
    }

    // ------------------------------------------------------------------ equipment panel

    private static EquipmentInfoPanel BuildEquipmentPanel(Transform parent, EquipmentInfoPanel.Kind kind, string titleText,
        string operationHint, string sectionText, PuiRow[] rows, string sliderIcon, string sliderText)
    {
        RectTransform panel = NewUI(kind + "Panel", parent);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(24f, -24f);
        panel.sizeDelta = new Vector2(400f, 600f);
        AddImage(panel, PuiPanel, PuiSprite("rounded"));
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = PuiBorder;
        outline.effectDistance = new Vector2(1f, -1f);
        PuiVertical(panel, 10, new RectOffset(20, 20, 18, 20));
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        EquipmentInfoPanel info = panel.gameObject.AddComponent<EquipmentInfoPanel>();

        // Header: title + status, collapse button.
        RectTransform header = NewUI("Header", panel);
        PuiHorizontal(header, 8, new RectOffset(0, 0, 0, 0));
        RectTransform titleBlock = NewUI("TitleBlock", header);
        PuiVertical(titleBlock, 2, new RectOffset(0, 0, 0, 0));
        PuiFlexible(titleBlock);
        TextMeshProUGUI title = PuiText(titleBlock, "Title", titleText, 27, true, PuiTextColor, TextAlignmentOptions.Left);
        RectTransform statusRow = NewUI("Status", titleBlock);
        PuiHorizontal(statusRow, 6, new RectOffset(0, 0, 0, 0));
        Image dot = PuiIcon(statusRow, "Dot", "dot", 10f, new Color(0.3f, 0.9f, 0.92f));
        TextMeshProUGUI status = PuiText(statusRow, "StatusText", "Running", 17, false, new Color(0.3f, 0.9f, 0.92f), TextAlignmentOptions.Left);

        Button collapse = PuiIconButton(header, "CollapseButton", "chevron_up", 34f, 20f, PuiButton, out Image collapseIcon);

        // Body (hidden when collapsed).
        RectTransform body = NewUI("Body", panel);
        PuiVertical(body, 10, new RectOffset(0, 0, 0, 0));

        Button operationButton = null;
        TextMeshProUGUI operationLabel = null;
        Image operationIcon = null;
        if (operationHint == null)
        {
            TextMeshProUGUI purpose = PuiText(body, "Purpose", "<b>Purpose :</b> Increase generator voltage for transmission", 17, false, PuiMuted, TextAlignmentOptions.Left);
            purpose.richText = true;
        }
        else
        {
            RectTransform opRow = NewUI("Operation", body);
            PuiHorizontal(opRow, 10, new RectOffset(0, 0, 0, 0));
            RectTransform opText = NewUI("Text", opRow);
            PuiVertical(opText, 2, new RectOffset(0, 0, 0, 0));
            PuiFlexible(opText);
            PuiText(opText, "Label", "Operation", 19, true, PuiTextColor, TextAlignmentOptions.Left);
            PuiText(opText, "Hint", operationHint, 14, false, PuiMuted, TextAlignmentOptions.Left);

            RectTransform buttonRect = NewUI("OperationButton", opRow);
            Image buttonImage = AddImage(buttonRect, PuiStart, PuiSprite("rounded"));
            operationButton = buttonRect.gameObject.AddComponent<Button>();
            operationButton.targetGraphic = buttonImage;
            PuiHorizontal(buttonRect, 8, new RectOffset(14, 16, 9, 9));
            operationIcon = PuiIcon(buttonRect, "Icon", "play", 18f, Color.white);
            operationLabel = PuiText(buttonRect, "Label", "Start Operation", 17, true, Color.white, TextAlignmentOptions.Left);
        }

        PuiSeparatorLine(body);
        PuiText(body, "Section", sectionText, 18, true, PuiMuted, TextAlignmentOptions.Left);

        var values = new Object[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            RectTransform row = NewUI(rows[i].label.Replace(" ", ""), body);
            PuiHorizontal(row, 12, new RectOffset(0, 0, 2, 2));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 40f;
            PuiIconBadge(row, rows[i].icon, rows[i].color);
            TextMeshProUGUI label = PuiText(row, "Label", rows[i].label, 19, false, PuiTextColor, TextAlignmentOptions.Left);
            PuiFlexible(label.rectTransform);
            values[i] = PuiText(row, "Value", "--", 20, true, Color.white, TextAlignmentOptions.Right);
            if (i < rows.Length - 1) PuiSeparatorLine(body);
        }

        // Slider.
        PuiSeparatorLine(body);
        RectTransform sliderRow = NewUI("SliderRow", body);
        PuiHorizontal(sliderRow, 12, new RectOffset(0, 0, 2, 2));
        PuiIconBadge(sliderRow, sliderIcon, PuiOrange);
        RectTransform sliderBlock = NewUI("Control", sliderRow);
        PuiVertical(sliderBlock, 6, new RectOffset(0, 0, 0, 0));
        PuiFlexible(sliderBlock);
        PuiText(sliderBlock, "Label", sliderText, 19, false, PuiTextColor, TextAlignmentOptions.Left);
        Slider slider = PuiSlider(sliderBlock);
        TextMeshProUGUI sliderValue = PuiText(sliderRow, "Value", "50 %", 20, true, Color.white, TextAlignmentOptions.Right);
        sliderValue.gameObject.AddComponent<LayoutElement>().minWidth = 64f;

        // Wire.
        var so = new SerializedObject(info);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("title").objectReferenceValue = title;
        so.FindProperty("statusDot").objectReferenceValue = dot;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("collapseButton").objectReferenceValue = collapse;
        so.FindProperty("collapseIcon").objectReferenceValue = collapseIcon;
        so.FindProperty("collapseSprite").objectReferenceValue = PuiSprite("chevron_up");
        so.FindProperty("expandSprite").objectReferenceValue = PuiSprite("chevron_down");
        so.FindProperty("body").objectReferenceValue = body.gameObject;
        so.FindProperty("operationButton").objectReferenceValue = operationButton;
        so.FindProperty("operationLabel").objectReferenceValue = operationLabel;
        so.FindProperty("operationIcon").objectReferenceValue = operationIcon;
        so.FindProperty("startSprite").objectReferenceValue = PuiSprite("play");
        so.FindProperty("stopSprite").objectReferenceValue = PuiSprite("stop");
        SerializedProperty valuesProp = so.FindProperty("values");
        valuesProp.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) valuesProp.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.FindProperty("slider").objectReferenceValue = slider;
        so.FindProperty("sliderValue").objectReferenceValue = sliderValue;
        so.ApplyModifiedPropertiesWithoutUndo();

        panel.gameObject.SetActive(false); // PlantUI shows it at the equipment
        return info;
    }

    private static Slider PuiSlider(Transform parent)
    {
        RectTransform rect = NewUI("Slider", parent);
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        Slider slider = rect.gameObject.AddComponent<Slider>();

        RectTransform background = NewUI("Background", rect);
        Anchor(background, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(0f, 4f));
        AddImage(background, PuiTrack, PuiSprite("rounded"));

        RectTransform fillArea = NewUI("Fill Area", rect);
        Anchor(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(0f, 4f));
        RectTransform fill = NewUI("Fill", fillArea);
        Anchor(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddImage(fill, PuiOrange, PuiSprite("rounded"));

        RectTransform handleArea = NewUI("Handle Slide Area", rect);
        Anchor(handleArea, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
        RectTransform handle = NewUI("Handle", handleArea);
        handle.anchorMin = handle.anchorMax = new Vector2(0f, 0.5f);
        handle.sizeDelta = new Vector2(22f, 22f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.sprite = PuiSprite("dot");
        handleImage.color = Color.white;

        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
        slider.value = 50f;
        return slider;
    }

    // ------------------------------------------------------------------ nav bar

    private static PlantNavBar BuildNavBar(Transform parent)
    {
        RectTransform bar = NewUI("NavBar", parent);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 20f);
        AddImage(bar, PuiPanel, PuiSprite("rounded"));
        PuiHorizontal(bar, 6, new RectOffset(10, 10, 8, 8));
        ContentSizeFitter fitter = bar.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        PlantNavBar navBar = bar.gameObject.AddComponent<PlantNavBar>();


        (string label, string icon, string scene)[] entries =
        {
            ("Power Plant Area", "plant", PlantNavBar.PlantScene),
            ("Boiler Room", "fire", "BoilerRoom"),
            ("Turbine Room", "fan", "TurbineRoom"),
            ("Control Room", "monitor", "Control_Room"),
        };

        var so = new SerializedObject(navBar);
        SerializedProperty items = so.FindProperty("items");
        items.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            RectTransform item = NewUI(entries[i].label.Replace(" ", ""), bar);
            Image highlight = AddImage(item, new Color(PuiHighlight.r, PuiHighlight.g, PuiHighlight.b, 0f), PuiSprite("rounded"));
            Button button = item.gameObject.AddComponent<Button>();
            button.targetGraphic = highlight;
            PuiHorizontal(item, 8, new RectOffset(16, 18, 10, 10));
            Image icon = PuiIcon(item, "Icon", entries[i].icon, 24f, PuiTextColor);
            TextMeshProUGUI label = PuiText(item, "Label", entries[i].label, 18, true, PuiTextColor, TextAlignmentOptions.Left);

            SerializedProperty element = items.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("button").objectReferenceValue = button;
            element.FindPropertyRelative("sceneName").stringValue = entries[i].scene;
            element.FindPropertyRelative("highlight").objectReferenceValue = highlight;
            element.FindPropertyRelative("label").objectReferenceValue = label;
            element.FindPropertyRelative("icon").objectReferenceValue = icon;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return navBar;
    }

    // ------------------------------------------------------------------ toolbar

    private static PlantToolbar BuildToolbar(Transform parent)
    {
        RectTransform bar = NewUI("Toolbar", parent);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 0.5f);
        bar.anchoredPosition = new Vector2(-20f, 0f);
        AddImage(bar, PuiPanel, PuiSprite("rounded"));
        PuiVertical(bar, 6, new RectOffset(8, 8, 8, 8));
        ContentSizeFitter fitter = bar.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        PlantToolbar toolbar = bar.gameObject.AddComponent<PlantToolbar>();

        RectTransform workerGroup = NewUI("WorkerViews", bar);
        PuiVertical(workerGroup, 6, new RectOffset(0, 0, 0, 0));
        Color off = new Color(PuiHighlight.r, PuiHighlight.g, PuiHighlight.b, 0f);
        Button tpp = PuiIconButton(workerGroup, "TPP", "person_add", 48f, 26f, off, out _);
        Button fpp = PuiIconButton(workerGroup, "FPP", "camera", 48f, 26f, off, out _);
        Button fly = PuiIconButton(workerGroup, "FlyCamera", "drone", 48f, 26f, off, out _);
        RectTransform separator = NewUI("Separator", workerGroup);
        separator.gameObject.AddComponent<Image>().color = PuiSeparator;
        separator.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;

        Button overview = PuiIconButton(bar, "Overview", "apps", 48f, 26f, PuiButton, out _);
        Button hide = PuiIconButton(bar, "HideUI", "collapse", 48f, 26f, PuiButton, out Image hideIcon);

        var so = new SerializedObject(toolbar);
        so.FindProperty("workerGroup").objectReferenceValue = workerGroup.gameObject;
        so.FindProperty("tppButton").objectReferenceValue = tpp;
        so.FindProperty("fppButton").objectReferenceValue = fpp;
        so.FindProperty("flyButton").objectReferenceValue = fly;
        so.FindProperty("tppHighlight").objectReferenceValue = tpp.targetGraphic;
        so.FindProperty("fppHighlight").objectReferenceValue = fpp.targetGraphic;
        so.FindProperty("flyHighlight").objectReferenceValue = fly.targetGraphic;
        so.FindProperty("overviewButton").objectReferenceValue = overview;
        so.FindProperty("hideButton").objectReferenceValue = hide;
        so.FindProperty("hideIcon").objectReferenceValue = hideIcon;
        so.FindProperty("hideSprite").objectReferenceValue = PuiSprite("collapse");
        so.FindProperty("showSprite").objectReferenceValue = PuiSprite("expand");
        so.ApplyModifiedPropertiesWithoutUndo();
        return toolbar;
    }

    private static void BuildDayNight(Transform parent, PlantToolbar toolbar)
    {
        RectTransform pill = NewUI("DayNight", parent);
        pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(1f, 1f);
        pill.anchoredPosition = new Vector2(-20f, -20f);
        Image background = AddImage(pill, PuiPanel, PuiSprite("rounded"));
        Button button = pill.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        PuiHorizontal(pill, 8, new RectOffset(14, 18, 10, 10));
        ContentSizeFitter fitter = pill.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Image icon = PuiIcon(pill, "Icon", "sun", 24f, PuiAmber);
        TextMeshProUGUI label = PuiText(pill, "Label", "Day", 18, true, PuiTextColor, TextAlignmentOptions.Left);

        var so = new SerializedObject(toolbar);
        so.FindProperty("dayNightButton").objectReferenceValue = button;
        so.FindProperty("dayNightIcon").objectReferenceValue = icon;
        so.FindProperty("dayNightLabel").objectReferenceValue = label;
        so.FindProperty("daySprite").objectReferenceValue = PuiSprite("sun");
        so.FindProperty("eveningSprite").objectReferenceValue = PuiSprite("evening");
        so.FindProperty("nightSprite").objectReferenceValue = PuiSprite("moon");
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ small helpers

    private static Sprite PuiSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(PlantUIIconFolder + name + ".png");

    private static void PuiVertical(RectTransform rect, float spacing, RectOffset padding)
    {
        VerticalLayoutGroup group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = TextAnchor.UpperLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
    }

    private static void PuiHorizontal(RectTransform rect, float spacing, RectOffset padding)
    {
        HorizontalLayoutGroup group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
    }

    private static void PuiFlexible(RectTransform rect)
    {
        LayoutElement element = rect.GetComponent<LayoutElement>();
        if (element == null) element = rect.gameObject.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
    }

    private static TextMeshProUGUI PuiText(Transform parent, string name, string content, float size, bool bold, Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = NewUI(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        if (styFont != null) text.font = styFont;
        return text;
    }

    private static Image PuiIcon(Transform parent, string name, string sprite, float size, Color color)
    {
        RectTransform rect = NewUI(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = PuiSprite(sprite);
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = size;
        element.minHeight = element.preferredHeight = size;
        rect.sizeDelta = new Vector2(size, size);
        return image;
    }

    // Coloured round badge with an icon (parameter rows).
    private static void PuiIconBadge(Transform parent, string sprite, Color color)
    {
        RectTransform badge = NewUI("Icon", parent);
        AddImage(badge, new Color(color.r, color.g, color.b, 0.16f), PuiSprite("rounded")).raycastTarget = false;
        LayoutElement element = badge.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = 36f;
        element.minHeight = element.preferredHeight = 36f;

        RectTransform icon = NewUI("Glyph", badge);
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = new Vector2(22f, 22f);
        Image image = icon.gameObject.AddComponent<Image>();
        image.sprite = PuiSprite(sprite);
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static Button PuiIconButton(Transform parent, string name, string sprite, float size, float iconSize, Color background, out Image icon)
    {
        RectTransform rect = NewUI(name, parent);
        Image image = AddImage(rect, background, PuiSprite("rounded"));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = size;
        element.minHeight = element.preferredHeight = size;
        rect.sizeDelta = new Vector2(size, size);

        RectTransform iconRect = NewUI("Icon", rect);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);
        icon = iconRect.gameObject.AddComponent<Image>();
        icon.sprite = PuiSprite(sprite);
        icon.color = PuiTextColor;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        return button;
    }

    private static void PuiSeparatorLine(Transform parent)
    {
        RectTransform line = NewUI("Separator", parent);
        Image image = line.gameObject.AddComponent<Image>();
        image.color = PuiSeparator;
        image.raycastTarget = false;
        LayoutElement element = line.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = 1f;
    }
}
