using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Maintenance sheet of the selected part (Boiler / Turbine explosion view). Bottom-right corner, 150 px from
/// the right and bottom edge (1920x1080 reference). Header with the part name and a collapse / open button.
///
/// The UI is a normal uGUI prefab: Assets/Prefabs/UI/MaintenanceSheet.prefab, assigned on HUDController in
/// Bootstrap (created once at start, hidden, kept for the session). Restyle it freely - keep the references on
/// this component. Without the prefab the same layout is built in code. Shown when a part is clicked.
/// </summary>
public class MaintenanceSheetPanel : MonoBehaviour
{
    public static readonly string[] RowLabels =
    {
        "Part Name", "Part ID", "Equipment", "Last Maintenance Date", "Maintenance Type", "Current Condition",
        "Running Hours", "Next Maintenance Due", "Technician", "Issue Found", "Action Taken", "Spare Part Used", "Remarks",
        "Description"
    };
    private const int ConditionRow = 5;

    [Header("References")]
    [Tooltip("The sheet (shown / hidden).")]
    [SerializeField] private GameObject panel;
    [Tooltip("The table (hidden when collapsed).")]
    [SerializeField] private GameObject body;
    [SerializeField] private Button collapseButton;
    [SerializeField] private TMP_Text collapseIcon;
    [SerializeField] private TMP_Text partTitle;
    [Tooltip("One value text per row, in the order of RowLabels.")]
    [SerializeField] private TMP_Text[] values = new TMP_Text[0];

    [Header("Behaviour")]
    [SerializeField] private bool startCollapsed = false;
    [SerializeField] private Color goodColor = new Color(0.3f, 0.85f, 0.45f);
    [SerializeField] private Color warningColor = new Color(1f, 0.75f, 0.2f);
    [SerializeField] private Color criticalColor = new Color(1f, 0.38f, 0.32f);
    [SerializeField] private Color valueColor = Color.white;

    private bool collapsed;

    public bool IsVisible => panel != null && panel.activeSelf;

    private void Awake()
    {
        collapsed = startCollapsed;
        if (collapseButton != null) collapseButton.onClick.AddListener(ToggleCollapsed);
        ApplyCollapsed();
        if (panel != null) panel.SetActive(false);
    }

    public void Show(MaintenanceInfo info)
    {
        if (info == null) { Hide(); return; }

        if (partTitle != null) partTitle.text = info.partName;

        string[] rowValues =
        {
            info.partName, info.partId, info.equipment, info.lastMaintenanceDate, info.maintenanceType, info.condition,
            info.runningHours, info.nextMaintenanceDue, info.technician, info.issueFound, info.actionTaken,
            info.sparePartUsed, info.remarks, info.description
        };

        for (int i = 0; i < values.Length && i < rowValues.Length; i++)
        {
            if (values[i] == null) continue;
            values[i].text = rowValues[i];
            values[i].color = i == ConditionRow ? ConditionColor(info.condition) : valueColor;
        }

        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    public void ToggleCollapsed()
    {
        collapsed = !collapsed;
        ApplyCollapsed();
    }

    public void SetCollapsed(bool value)
    {
        collapsed = value;
        ApplyCollapsed();
    }

    private void ApplyCollapsed()
    {
        if (body != null) body.SetActive(!collapsed);
        if (collapseIcon != null) collapseIcon.text = collapsed ? "+" : "–";
    }

    private Color ConditionColor(string condition)
    {
        switch (condition)
        {
            case "Critical": return criticalColor;
            case "Warning": return warningColor;
            default: return goodColor;
        }
    }

    // ================================================================== default layout (code + prefab tool)

    private static readonly Color PanelColor = new Color(0.075f, 0.09f, 0.16f, 0.96f);
    private static readonly Color HeaderColor = new Color(0.12f, 0.15f, 0.26f, 1f);
    private static readonly Color AccentColor = new Color(1f, 0.62f, 0.25f, 1f);
    private static readonly Color LabelColor = new Color(0.68f, 0.73f, 0.84f, 1f);
    private static readonly Color RowShade = new Color(1f, 1f, 1f, 0.035f);

    /// <summary>Builds the whole sheet (canvas + panel + table) and wires this component.</summary>
    public static MaintenanceSheetPanel CreateDefault(Transform parent)
    {
        var root = new GameObject("MaintenanceSheet", typeof(RectTransform));
        if (parent != null) root.transform.SetParent(parent, false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // Panel: bottom-right, 150 px from the right and the bottom, grows upwards.
        var panel = NewUI("Panel", root.transform);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-150f, 150f);
        panelRect.sizeDelta = new Vector2(480f, 0f);
        panel.AddComponent<Image>().color = PanelColor;
        var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header: titles + collapse button.
        var header = NewUI("Header", panel.transform);
        header.AddComponent<Image>().color = HeaderColor;
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(16, 10, 10, 10);
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        var titles = NewUI("Titles", header.transform);
        var titlesLayout = titles.AddComponent<VerticalLayoutGroup>();
        titlesLayout.childControlWidth = true;
        titlesLayout.childControlHeight = true;
        titlesLayout.childForceExpandHeight = false;
        titles.AddComponent<LayoutElement>().flexibleWidth = 1f;
        NewText("Caption", titles.transform, "MAINTENANCE SHEET", 12f, FontStyles.Bold, AccentColor).characterSpacing = 4f;
        TMP_Text partTitle = NewText("PartTitle", titles.transform, "Part", 19f, FontStyles.Bold, Color.white);

        var button = NewUI("CollapseButton", header.transform);
        var buttonImage = button.AddComponent<Image>();
        buttonImage.color = new Color(1f, 1f, 1f, 0.08f);
        var buttonComponent = button.AddComponent<Button>();
        buttonComponent.targetGraphic = buttonImage;
        var buttonElement = button.AddComponent<LayoutElement>();
        buttonElement.preferredWidth = buttonElement.minWidth = 34f;
        buttonElement.preferredHeight = buttonElement.minHeight = 34f;
        TMP_Text icon = NewText("Icon", button.transform, "–", 22f, FontStyles.Bold, Color.white);
        icon.alignment = TextAlignmentOptions.Center;
        Stretch((RectTransform)icon.transform);

        // Body: the table.
        var body = NewUI("Body", panel.transform);
        var bodyLayout = body.AddComponent<VerticalLayoutGroup>();
        bodyLayout.padding = new RectOffset(0, 0, 6, 10);
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = false;

        var values = new TMP_Text[RowLabels.Length];
        for (int i = 0; i < RowLabels.Length; i++)
        {
            var row = NewUI("Row " + RowLabels[i], body.transform);
            row.AddComponent<Image>().color = i % 2 == 0 ? RowShade : Color.clear;
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(16, 16, 6, 6);
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            TMP_Text label = NewText("Label", row.transform, RowLabels[i], 13f, FontStyles.Normal, LabelColor);
            var labelElement = label.gameObject.AddComponent<LayoutElement>();
            labelElement.minWidth = labelElement.preferredWidth = 170f;

            TMP_Text value = NewText("Value", row.transform, "-", 14f, i == ConditionRow ? FontStyles.Bold : FontStyles.Normal, Color.white);
            value.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            values[i] = value;
        }

        var sheet = root.AddComponent<MaintenanceSheetPanel>();
        sheet.panel = panel;
        sheet.body = body;
        sheet.collapseButton = buttonComponent;
        sheet.collapseIcon = icon;
        sheet.partTitle = partTitle;
        sheet.values = values;

        // Built at runtime: Awake already ran before the references existed - wire it now.
        if (Application.isPlaying)
        {
            buttonComponent.onClick.AddListener(sheet.ToggleCollapsed);
            sheet.ApplyCollapsed();
            panel.SetActive(false);
        }

        return sheet;
    }

    private static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, FontStyles style, Color color)
    {
        var go = NewUI(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
