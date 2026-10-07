using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Maintenance sheet of the selected part (Boiler / Turbine explosion view). Bottom-right corner, 150 px from
/// the right and bottom edge (1920x1080 reference). Header with the part name and a collapse / open button.
///
/// The UI is a normal uGUI prefab: Assets/Prefabs/UI/MaintenanceSheet.prefab, assigned on HUDController in
/// Bootstrap (created once at start, hidden, kept for the session). Restyle it freely - keep the references on
/// this component. Without the prefab the same layout is built in code. Shown when a part is clicked.
///
/// Edit (header button): the values become text fields (Part Name / ID / Equipment stay locked, Description is
/// hidden and not exported); Done shows the typed values (also sent to React as
/// handlePartMaintenance). Print Report (bottom button): downloads an Excel report with the values and
/// a picture of the part. Nothing is stored: selecting another part shows its own data again.
/// The Edit / Export buttons are created at runtime in the style of the collapse button when not assigned.
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
    private const int DescriptionRow = 13;
    // Part Name, Part ID, Equipment: identify the part - shown but not editable.
    private static bool IsReadOnly(int row) => row <= 2;
    // Description: on the sheet only - hidden while editing and not in the Excel report.
    private static bool IsEditable(int row) => !IsReadOnly(row) && row != DescriptionRow;

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

    [Header("Edit / Export (empty = created at runtime)")]
    [Tooltip("Header button: EDIT <-> DONE.")]
    [SerializeField] private Button editButton;
    [SerializeField] private TMP_Text editButtonLabel;
    [Tooltip("Bottom button: downloads the sheet + part picture as Excel (.xlsx).")]
    [SerializeField] private Button exportButton;
    [Tooltip("Off (default): PRINT REPORT shows whenever the sheet is open. On: only while editing.")]
    [SerializeField] private bool exportOnlyWhileEditing = false;
    [SerializeField] private Color inputBackground = new Color(1f, 1f, 1f, 0.1f);

    private bool collapsed, editing, wired;
    private MaintenanceInfo current;
    private Transform currentPart;
    private Camera currentView;
    private TMP_InputField[] inputs;
    private GameObject exportArea; // the footer (auto) or the assigned button

    public bool IsVisible => panel != null && panel.activeSelf;
    public bool IsEditing => editing;

    private void Awake()
    {
        collapsed = startCollapsed;
        EnsureEditControls();
        Wire();
        ApplyCollapsed();
        RefreshEditControls();
        if (panel != null) panel.SetActive(false);
    }

    private void Wire()
    {
        if (wired || collapseButton == null) return;
        wired = true;
        collapseButton.onClick.AddListener(ToggleCollapsed);
        if (editButton != null) editButton.onClick.AddListener(ToggleEdit);
        if (exportButton != null) exportButton.onClick.AddListener(Export);
    }

    /// <param name="part">The part (for the picture in the Excel report).</param>
    /// <param name="view">Camera the user looks through (the picture uses its direction).</param>
    public void Show(MaintenanceInfo info, Transform part = null, Camera view = null)
    {
        if (info == null) { Hide(); return; }

        EndEdit(false); // another part: unsaved typing is dropped
        current = info;
        currentPart = part;
        currentView = view;

        if (partTitle != null) partTitle.text = info.partName;

        string[] rowValues = ToRows(info);
        for (int i = 0; i < values.Length && i < rowValues.Length; i++)
        {
            if (values[i] == null) continue;
            values[i].text = rowValues[i];
            values[i].color = i == ConditionRow ? ConditionColor(info.condition) : valueColor;
        }

        RefreshEditControls();
        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        EndEdit(false);
        if (panel != null) panel.SetActive(false);
    }

    // ================================================================== edit

    public void ToggleEdit()
    {
        if (editing) EndEdit(true);
        else BeginEdit();
    }

    public void BeginEdit()
    {
        if (editing || current == null) return;
        if (collapsed) SetCollapsed(false);
        EnsureInputs();

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == null) continue;
            if (IsReadOnly(i)) values[i].alpha = 0.55f; // locked look
            if (inputs[i] == null) continue;
            inputs[i].text = values[i].text == "-" ? string.Empty : values[i].text;
            values[i].gameObject.SetActive(false);
            inputs[i].gameObject.SetActive(true);
        }
        SetDescriptionRowVisible(false);

        editing = true;
        RefreshEditControls();
    }

    /// <param name="apply">true = the typed values become the sheet (Done); false = dropped.</param>
    public void EndEdit(bool apply)
    {
        if (!editing) return;
        editing = false;

        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        SetDescriptionRowVisible(true);
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != null && IsReadOnly(i)) values[i].alpha = 1f;
            if (values[i] == null || inputs[i] == null) continue;
            if (apply)
            {
                string typed = inputs[i].text.Trim();
                values[i].text = typed.Length == 0 ? "-" : typed;
            }
            if (selected == inputs[i].gameObject) EventSystem.current.SetSelectedGameObject(null);
            inputs[i].gameObject.SetActive(false);
            values[i].gameObject.SetActive(true);
        }

        if (apply && current != null)
        {
            FromRows(current, CurrentRowValues());
            if (partTitle != null) partTitle.text = current.partName;
            if (ConditionRow < values.Length && values[ConditionRow] != null)
                values[ConditionRow].color = ConditionColor(current.condition);
            CommunicationManager.HandlePartMaintenance_Extern(current);
        }

        RefreshEditControls();
    }

    // ================================================================== export

    /// <summary>Downloads the sheet (as shown / as typed) + a picture of the part as an Excel file.</summary>
    public void Export()
    {
        if (current == null) return;

        string[] rowValues = CurrentRowValues();
        var rows = new List<XlsxReport.Row>(RowLabels.Length);
        for (int i = 0; i < RowLabels.Length; i++)
        {
            if (i == DescriptionRow) continue; // sheet only, not in the report
            string value = i < rowValues.Length ? rowValues[i] : "-";
            rows.Add(new XlsxReport.Row
            {
                label = RowLabels[i],
                value = value,
                highlight = i == ConditionRow ? ConditionHighlight(value) : XlsxReport.Highlight.None
            });
        }

        const int pictureWidth = 1024, pictureHeight = 768;
        byte[] picture = PartSnapshot.CaptureJpg(currentPart, currentView, pictureWidth, pictureHeight);

        string partName = rowValues.Length > 0 ? rowValues[0] : current.partName;
        string equipment = rowValues.Length > 2 ? rowValues[2] : current.equipment;
        byte[] file = XlsxReport.Build("MAINTENANCE REPORT", $"{partName}  |  {equipment}",
            "Generated " + DateTime.Now.ToString("dd MMM yyyy, HH:mm"), rows, picture, 640, 480);

        string id = rowValues.Length > 1 && rowValues[1] != "-" ? rowValues[1] : partName;
        FileDownload.Save(FileDownload.SafeName($"Maintenance_Report_{id}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"), file, FileDownload.XlsxMime);
    }

    // ================================================================== rows <-> data

    private static string[] ToRows(MaintenanceInfo info) => new[]
    {
        info.partName, info.partId, info.equipment, info.lastMaintenanceDate, info.maintenanceType, info.condition,
        info.runningHours, info.nextMaintenanceDue, info.technician, info.issueFound, info.actionTaken,
        info.sparePartUsed, info.remarks, info.description
    };

    private static void FromRows(MaintenanceInfo info, string[] rows)
    {
        string Get(int i) => i < rows.Length ? rows[i] : "-";
        info.partName = Get(0); info.partId = Get(1); info.equipment = Get(2); info.lastMaintenanceDate = Get(3);
        info.maintenanceType = Get(4); info.condition = Get(5); info.runningHours = Get(6); info.nextMaintenanceDue = Get(7);
        info.technician = Get(8); info.issueFound = Get(9); info.actionTaken = Get(10); info.sparePartUsed = Get(11);
        info.remarks = Get(12); info.description = Get(13);
    }

    // What the sheet shows right now: the typed text while editing, else the values.
    private string[] CurrentRowValues()
    {
        var rows = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            string text = editing && inputs != null && inputs[i] != null ? inputs[i].text.Trim()
                : values[i] != null ? values[i].text : string.Empty;
            rows[i] = string.IsNullOrEmpty(text) ? "-" : text;
        }
        return rows;
    }

    private static XlsxReport.Highlight ConditionHighlight(string condition)
    {
        string c = (condition ?? string.Empty).ToLowerInvariant();
        if (c.Contains("crit")) return XlsxReport.Highlight.Critical;
        if (c.Contains("warn")) return XlsxReport.Highlight.Warning;
        return c.Contains("good") ? XlsxReport.Highlight.Good : XlsxReport.Highlight.None;
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
        switch (ConditionHighlight(condition))
        {
            case XlsxReport.Highlight.Critical: return criticalColor;
            case XlsxReport.Highlight.Warning: return warningColor;
            case XlsxReport.Highlight.Good: return goodColor;
            default: return valueColor;
        }
    }

    private void RefreshEditControls()
    {
        if (editButtonLabel != null) editButtonLabel.text = editing ? "DONE" : "EDIT";
        if (exportArea != null) exportArea.SetActive(editing || !exportOnlyWhileEditing);
    }

    // ================================================================== edit controls (runtime-built when missing)

    /// <summary>Creates the Edit / Export buttons in the style of the collapse button when they are not assigned.</summary>
    private void EnsureEditControls()
    {
        if (collapseButton == null) return;

        if (editButton == null)
        {
            editButton = CloneButton("EditButton", collapseButton.transform.parent, "EDIT", 70f, out editButtonLabel);
            editButton.transform.SetSiblingIndex(collapseButton.transform.GetSiblingIndex());
        }
        else if (editButtonLabel == null) editButtonLabel = editButton.GetComponentInChildren<TMP_Text>(true);

        if (exportButton == null && body != null)
        {
            var footer = new GameObject("Footer", typeof(RectTransform));
            footer.transform.SetParent(body.transform, false);
            var layout = footer.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 10, 4);
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            exportButton = CloneButton("ExportButton", footer.transform, "PRINT REPORT", 150f, out _);
            if (exportButton.TryGetComponent(out Image image)) image.color = new Color(1f, 0.62f, 0.25f, 0.9f);
            exportArea = footer;
        }
        else if (exportButton != null)
        {
            Transform parent = exportButton.transform.parent;
            exportArea = parent != null && parent.name == "Footer" ? parent.gameObject : exportButton.gameObject;
        }
    }

    private Button CloneButton(string objectName, Transform parent, string label, float width, out TMP_Text text)
    {
        GameObject go = Instantiate(collapseButton.gameObject, parent, false);
        go.name = objectName;

        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent(); // not the collapse action

        text = go.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.text = label;
            text.enableAutoSizing = false;
            text.fontSize = 13f;
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 2f;
        }

        if (!go.TryGetComponent(out LayoutElement element)) element = go.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = width;
        return button;
    }

    // One text field per row, next to its value (hidden until Edit).
    private void EnsureInputs()
    {
        if (inputs != null && inputs.Length == values.Length) return;
        inputs = new TMP_InputField[values.Length];
        for (int i = 0; i < values.Length; i++)
            if (values[i] != null && IsEditable(i)) inputs[i] = CreateInput(values[i], i >= 9); // Issue Found and below: longer text
    }

    private void SetDescriptionRowVisible(bool visible)
    {
        if (DescriptionRow < values.Length && values[DescriptionRow] != null && values[DescriptionRow].transform.parent != null)
            values[DescriptionRow].transform.parent.gameObject.SetActive(visible);
    }

    private TMP_InputField CreateInput(TMP_Text value, bool multiLine)
    {
        var go = new GameObject("Input", typeof(RectTransform));
        go.SetActive(false);
        go.transform.SetParent(value.transform.parent, false);
        go.transform.SetSiblingIndex(value.transform.GetSiblingIndex() + 1);

        var background = go.AddComponent<Image>();
        background.color = inputBackground;

        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var areaRect = (RectTransform)area.transform;
        Stretch(areaRect);
        areaRect.offsetMin = new Vector2(6f, 4f);
        areaRect.offsetMax = new Vector2(-6f, -4f);

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(area.transform, false);
        Stretch((RectTransform)textObject.transform);
        var text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = value.font;
        text.fontSize = value.fontSize;
        text.color = valueColor;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = false;
        text.raycastTarget = false;

        var input = go.AddComponent<TMP_InputField>();
        input.textViewport = areaRect;
        input.textComponent = text;
        input.targetGraphic = background;
        input.richText = false;
        input.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.MultiLineSubmit;
        input.customCaretColor = true;
        input.caretColor = Color.white;
        input.caretWidth = 2;
        input.selectionColor = new Color(1f, 0.62f, 0.25f, 0.35f);

        var element = go.AddComponent<LayoutElement>();
        element.layoutPriority = 2;      // wins the width (fills the row) ...
        element.preferredWidth = 10f;
        element.flexibleWidth = 1f;
        element.minHeight = 26f;         // ... the height grows with the text (input field)

        // Re-layout as the text grows / shrinks (the panel grows upwards).
        var row = (RectTransform)value.transform.parent;
        input.onValueChanged.AddListener(_ => LayoutRebuilder.MarkLayoutForRebuild(row));
        return input;
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
        sheet.EnsureEditControls(); // Edit / Export buttons (saved in the prefab by the menu tool)

        // Built at runtime: Awake already ran before the references existed - wire it now.
        if (Application.isPlaying)
        {
            sheet.Wire();
            sheet.ApplyCollapsed();
            sheet.RefreshEditControls();
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
