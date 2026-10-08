using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > Thermal Plant > 18 (part): the "Thermal Power Plant Status Panel" (observation only) and the (i) button
/// beside Day / Night that opens it. Tabs: Boiler Room (2), Turbine Room (4), Electrical Info Panel (3),
/// Condenser (2), Chimney (smoke cases 0 / 0.2 / 0.4 / 0.6 / 0.8 / 1.0). 900 x 900 window at the top-left; no
/// backdrop, so the plant stays visible and clickable next to it.
/// </summary>
public static partial class ThermalPlantTools
{
    private struct StatusRow
    {
        public string icon, label;
        public Color color;
        public StatusRow(string icon, string label, Color color) { this.icon = icon; this.label = label; this.color = color; }
    }

    private static readonly Color StCard = new Color(0.10f, 0.13f, 0.24f, 1f);
    private static readonly Color StTile = new Color(1f, 1f, 1f, 0.04f);
    private static readonly Color StArt = new Color(0.55f, 0.68f, 0.95f, 1f);
    private static readonly Color StGreen = new Color(0.35f, 0.88f, 0.5f);
    private static readonly Color StBlue = new Color(0.38f, 0.66f, 1f);
    private static readonly Color StRed = new Color(1f, 0.42f, 0.42f);
    private static readonly Color StAmber = new Color(1f, 0.75f, 0.3f);
    private static readonly Color StPurple = new Color(0.78f, 0.52f, 1f);
    private static readonly Color StCyan = new Color(0.3f, 0.85f, 0.92f);
    private static readonly Color StOrange = new Color(1f, 0.55f, 0.2f);

    // ===================================================================================== panel

    private static PlantStatusPanel BuildPlantStatusPanel(Transform parent)
    {
        RectTransform overlay = NewUI("PlantStatus", parent);
        Anchor(overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        PlantStatusPanel panel = overlay.gameObject.AddComponent<PlantStatusPanel>();

        // No backdrop: the rest of the screen stays free - the plant can be viewed and clicked while it is open.
        // The overlay itself has no graphic, so it never catches clicks.
        Button backdropButton = null;

        // Window: 900 x 900, top-left.
        RectTransform window = NewUI("Window", overlay);
        window.anchorMin = window.anchorMax = window.pivot = new Vector2(0f, 1f);
        window.anchoredPosition = new Vector2(24f, -24f);
        window.sizeDelta = new Vector2(900f, 900f);
        AddImage(window, new Color(styPanel.r, styPanel.g, styPanel.b, 0.98f), PuiSprite("rounded"));
        Outline outline = window.gameObject.AddComponent<Outline>();
        outline.effectColor = PuiBorder;
        outline.effectDistance = new Vector2(1f, -1f);
        PuiVertical(window, 14, new RectOffset(20, 20, 18, 20));

        // ---- header
        RectTransform header = NewUI("Header", window);
        PuiHorizontal(header, 16, new RectOffset(0, 0, 0, 0));
        header.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
        RectTransform logo = NewUI("Logo", header);
        AddImage(logo, new Color(1f, 1f, 1f, 0.08f), PuiSprite("rounded")).raycastTarget = false;
        LayoutElement logoSize = logo.gameObject.AddComponent<LayoutElement>();
        logoSize.minWidth = logoSize.preferredWidth = 64f;
        logoSize.minHeight = logoSize.preferredHeight = 64f;
        CenterIcon(logo, "factory", 40f, StArt);

        RectTransform titles = NewUI("Titles", header);
        PuiVertical(titles, 2, new RectOffset(0, 0, 0, 0));
        PuiFlexible(titles);
        PuiText(titles, "Title", "Thermal Power Plant Status Panel", 24, true, Color.white, TextAlignmentOptions.Left);
        PuiText(titles, "Subtitle", "Real-time monitoring of critical equipment and operating parameters.", 13, false, PuiMuted, TextAlignmentOptions.Left);

        RectTransform online = NewUI("Online", titles);
        PuiHorizontal(online, 8, new RectOffset(0, 0, 0, 0));
        PuiIcon(online, "Dot", "dot", 10f, StGreen);
        PuiText(online, "Text", "Plant Online", 14, false, styText, TextAlignmentOptions.Left);
        PuiText(online, "Sep1", "|", 14, false, PuiMuted, TextAlignmentOptions.Left);
        TextMeshProUGUI date = PuiText(online, "Date", "Wed, 16 Apr 2025", 14, false, styText, TextAlignmentOptions.Left);
        PuiText(online, "Sep2", "|", 14, false, PuiMuted, TextAlignmentOptions.Left);
        TextMeshProUGUI time = PuiText(online, "Time", "14:28:32", 14, true, styText, TextAlignmentOptions.Left);
        Button close = PuiIconButton(header, "Close", "close", 36f, 16f, PuiButton, out _);

        // ---- tabs
        RectTransform tabBar = NewUI("Tabs", window);
        AddImage(tabBar, new Color(0f, 0f, 0f, 0.25f), PuiSprite("rounded")).raycastTarget = false;
        PuiHorizontal(tabBar, 6, new RectOffset(6, 6, 6, 6));
        tabBar.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;

        (string label, string icon, string count)[] tabInfo =
        {
            ("Boiler", "tp_boiler", "2"),
            ("Turbine", "tp_turbine", "4"),
            ("Electrical", "tp_electrical", "3"),
            ("Condenser", "condenser", "2"),
            ("Chimney", "chimney", "2"),
        };

        // ---- pages
        RectTransform pages = NewUI("Pages", window);
        LayoutElement pagesLayout = pages.gameObject.AddComponent<LayoutElement>();
        pagesLayout.flexibleHeight = 1f;
        pagesLayout.minHeight = 200f;

        var panelSO = new SerializedObject(panel);
        SerializedProperty tabs = panelSO.FindProperty("tabs");
        tabs.arraySize = tabInfo.Length;
        var pageContents = new RectTransform[tabInfo.Length];

        for (int i = 0; i < tabInfo.Length; i++)
        {
            RectTransform tab = NewUI(tabInfo[i].label.Replace(" ", ""), tabBar);
            Image highlight = AddImage(tab, new Color(0.17f, 0.32f, 0.85f, 0f), PuiSprite("rounded"));
            Button button = tab.gameObject.AddComponent<Button>();
            button.targetGraphic = highlight;
            PuiHorizontal(tab, 8, new RectOffset(10, 10, 8, 8));
            tab.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            tab.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Image icon = PuiIcon(tab, "Icon", tabInfo[i].icon, 22f, styText);
            TextMeshProUGUI label = PuiText(tab, "Label", tabInfo[i].label, 15, true, styText, TextAlignmentOptions.Left);
            RectTransform badge = NewUI("Count", tab);
            Image badgeImage = AddImage(badge, new Color(1f, 1f, 1f, 0.14f), PuiSprite("rounded"));
            badgeImage.raycastTarget = false;
            PuiHorizontal(badge, 0, new RectOffset(10, 10, 2, 2));
            PuiText(badge, "Text", tabInfo[i].count, 13, true, Color.white, TextAlignmentOptions.Center);

            GameObject page = BuildScrollPage(pages, tabInfo[i].label.Replace(" ", "") + "Page", out pageContents[i]);
            page.SetActive(i == 1);

            SerializedProperty t = tabs.GetArrayElementAtIndex(i);
            t.FindPropertyRelative("button").objectReferenceValue = button;
            t.FindPropertyRelative("highlight").objectReferenceValue = highlight;
            t.FindPropertyRelative("label").objectReferenceValue = label;
            t.FindPropertyRelative("icon").objectReferenceValue = icon;
            t.FindPropertyRelative("countBackground").objectReferenceValue = badgeImage;
            t.FindPropertyRelative("page").objectReferenceValue = page;
        }

        // ---- cards
        StatusRow[] boilerLeft =
        {
            new StatusRow("tp_temperature", "Furnace Temperature", StRed),
            new StatusRow("tp_water", "Water Level", StCyan),
            new StatusRow("tp_drum_pressure", "Drum Pressure", StPurple),
            new StatusRow("tp_steam_flow", "Steam Flow", StGreen),
        };
        StatusRow[] boilerRight =
        {
            new StatusRow("tp_burner", "Burner Output", StAmber),
            new StatusRow("tp_temperature", "Feedwater Temperature", StCyan),
            new StatusRow("smoke", "Flue Gas Temperature", StRed),
            new StatusRow("tp_oxygen", "Oxygen Level (O<sub>2</sub>)", StGreen),
        };
        StatusRow[] turbineLeft =
        {
            new StatusRow("tp_rpm", "RPM", StGreen),
            new StatusRow("tp_steam_pressure", "Steam Inlet Pressure", StRed),
            new StatusRow("tp_temperature", "Bearing Temperature", StAmber),
            new StatusRow("tp_vibration", "Vibration (RMS)", StPurple),
        };
        StatusRow[] turbineRight =
        {
            new StatusRow("tp_power", "Output Power", StBlue),
            new StatusRow("tp_oil_pressure", "Lube Oil Pressure", StCyan),
            new StatusRow("tp_temperature", "Exhaust Temp.", StRed),
            new StatusRow("tp_efficiency", "Efficiency", StGreen),
        };
        StatusRow[] electricalLeft =
        {
            new StatusRow("tp_power", "Generator Side Voltage", StRed),
            new StatusRow("tp_grid", "Grid Side Voltage", StBlue),
            new StatusRow("tp_current", "Current", StAmber),
            new StatusRow("tp_frequency", "Frequency", StBlue),
            new StatusRow("tp_rpm", "Loading", StPurple),
        };
        StatusRow[] electricalRight =
        {
            new StatusRow("tp_temperature", "Oil Temperature", StRed),
            new StatusRow("tp_temperature", "Winding Temperature", StAmber),
            new StatusRow("tp_power_factor", "Power Factor", StGreen),
            new StatusRow("tp_breaker", "Breaker Status", StGreen),
        };
        StatusRow[] condenserLeft =
        {
            new StatusRow("tp_drum_pressure", "Vacuum", StPurple),
            new StatusRow("tp_water", "Hotwell Level", StCyan),
            new StatusRow("tp_temperature", "CW Inlet Temperature", StBlue),
            new StatusRow("tp_temperature", "CW Outlet Temperature", StRed),
        };
        StatusRow[] condenserRight =
        {
            new StatusRow("tp_steam_flow", "Steam Inflow", StGreen),
            new StatusRow("tp_temperature", "Condensate Temperature", StAmber),
            new StatusRow("tp_water", "Cooling Water Flow", StBlue),
            new StatusRow("tp_efficiency", "Cleanliness Factor", StGreen),
        };
        StatusRow[] chimneyLeft =
        {
            new StatusRow("smoke", "Smoke Level", Color.white),
            new StatusRow("smoke", "Opacity", StAmber),
            new StatusRow("tp_temperature", "Stack Temperature", StRed),
            new StatusRow("tp_steam_flow", "Flue Gas Velocity", StCyan),
        };
        StatusRow[] chimneyRight =
        {
            new StatusRow("smoke", "Particulate (PM)", StAmber),
            new StatusRow("tp_steam_pressure", "SO<sub>x</sub>", StPurple),
            new StatusRow("tp_steam_pressure", "NO<sub>x</sub>", StBlue),
            new StatusRow("smoke", "CO", StRed),
        };

        var boilers = new Object[2];
        for (int i = 0; i < boilers.Length; i++)
            boilers[i] = BuildStatusCard(pageContents[0], $"Boiler {i + 1:00}", "tp_boiler", boilerLeft, boilerRight, "tp_load", "Load", false);

        var turbines = new Object[4];
        for (int i = 0; i < turbines.Length; i++)
            turbines[i] = BuildStatusCard(pageContents[1], $"Turbine {i + 1:00}", "tp_turbine", turbineLeft, turbineRight, "tp_load", "Load", false);

        var electricals = new Object[3];
        for (int i = 0; i < electricals.Length; i++)
            electricals[i] = BuildStatusCard(pageContents[2], $"Electrical Panel {i + 1:00}", "cabinet", electricalLeft, electricalRight, "tp_load", "Load", false);

        var condensers = new Object[2];
        for (int i = 0; i < condensers.Length; i++)
            condensers[i] = BuildStatusCard(pageContents[3], $"Condenser {i + 1:00}", "condenser", condenserLeft, condenserRight, "tp_load", "Load", false);

        var chimneys = new Object[2];
        for (int i = 0; i < chimneys.Length; i++)
            chimneys[i] = BuildStatusCard(pageContents[4], $"Chimney {i + 1:00}", "chimney", chimneyLeft, chimneyRight, "smoke", "Smoke Level", false);
        BuildSmokeCases(pageContents[4], out Object[] frames, out Object[] swatches, out TextMeshProUGUI indication);

        SetArray(panelSO, "boilers", boilers);
        SetArray(panelSO, "turbines", turbines);
        SetArray(panelSO, "electricalPanels", electricals);
        SetArray(panelSO, "condensers", condensers);
        SetArray(panelSO, "chimneys", chimneys);
        SetArray(panelSO, "caseFrames", frames);
        SetArray(panelSO, "caseSwatches", swatches);
        panelSO.FindProperty("smokeIndication").objectReferenceValue = indication;
        panelSO.FindProperty("dateText").objectReferenceValue = date;
        panelSO.FindProperty("timeText").objectReferenceValue = time;
        panelSO.FindProperty("closeButton").objectReferenceValue = close;
        panelSO.FindProperty("backdropButton").objectReferenceValue = backdropButton;
        panelSO.FindProperty("tabActive").colorValue = new Color(0.17f, 0.32f, 0.85f, 1f);
        panelSO.FindProperty("tabText").colorValue = styText;
        panelSO.ApplyModifiedPropertiesWithoutUndo();

        overlay.gameObject.SetActive(false); // opened with the (i) button
        return panel;
    }

    private static void SetArray(SerializedObject so, string property, Object[] items)
    {
        SerializedProperty array = so.FindProperty(property);
        array.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private static void CenterIcon(RectTransform parent, string sprite, float size, Color color)
    {
        RectTransform icon = NewUI("Icon", parent);
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = new Vector2(size, size);
        Image image = icon.gameObject.AddComponent<Image>();
        image.sprite = PuiSprite(sprite);
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    // Scroll view filling the Pages area; returns the page object, content = vertical list.
    private static GameObject BuildScrollPage(Transform parent, string name, out RectTransform content)
    {
        RectTransform page = NewUI(name, parent);
        Anchor(page, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ScrollRect scroll = page.gameObject.AddComponent<ScrollRect>();

        RectTransform viewport = NewUI("Viewport", page);
        Anchor(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); // catches the mouse wheel
        viewport.gameObject.AddComponent<RectMask2D>();

        content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        PuiVertical(content, 16, new RectOffset(0, 0, 0, 4));
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        return page.gameObject;
    }

    // ===================================================================================== card

    private static PlantStatusCard BuildStatusCard(Transform parent, string titleText, string art, StatusRow[] left, StatusRow[] right,
        string barIcon, string barLabel, bool compact)
    {
        RectTransform card = NewUI(titleText.Replace(" ", ""), parent);
        AddImage(card, StCard, PuiSprite("rounded"));
        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = PuiBorder;
        outline.effectDistance = new Vector2(1f, -1f);
        PuiVertical(card, 10, new RectOffset(16, 16, 12, 14));
        card.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        PlantStatusCard script = card.gameObject.AddComponent<PlantStatusCard>();

        // header
        RectTransform header = NewUI("Header", card);
        PuiHorizontal(header, 10, new RectOffset(0, 0, 0, 0));
        TextMeshProUGUI title = PuiText(header, "Title", titleText, 21, true, Color.white, TextAlignmentOptions.Left);
        Image dot = PuiIcon(header, "Dot", "dot", 10f, StCyan);
        TextMeshProUGUI status = PuiText(header, "Status", "Running", 16, false, StCyan, TextAlignmentOptions.Left);
        RectTransform space = NewUI("Space", header);
        PuiFlexible(space);
        Button collapse = PuiIconButton(header, "Collapse", "chevron_up", 30f, 16f, PuiButton, out Image collapseIcon);
        PuiSeparatorLine(card);

        // body
        RectTransform body = NewUI("Body", card);
        PuiVertical(body, 12, new RectOffset(0, 0, 0, 0));

        if (compact)
        {
            RectTransform tile = NewUI("Picture", body);
            AddImage(tile, StTile, PuiSprite("rounded")).raycastTarget = false;
            tile.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
            CenterIcon(tile, art, 96f, StArt);
        }

        RectTransform main = NewUI("Values", body);
        PuiHorizontal(main, 14, new RectOffset(0, 0, 0, 0));
        main.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
        if (!compact)
        {
            RectTransform tile = NewUI("Picture", main);
            AddImage(tile, StTile, PuiSprite("rounded")).raycastTarget = false;
            LayoutElement size = tile.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = 110f;
            size.minHeight = size.preferredHeight = 110f;
            CenterIcon(tile, art, 80f, StArt);
        }

        var values = new Object[left.Length + right.Length];
        BuildStatusColumn(main, "Left", left, compact, values, 0);
        RectTransform divider = NewUI("Divider", main);
        Image dividerImage = divider.gameObject.AddComponent<Image>();
        dividerImage.color = PuiSeparator;
        dividerImage.raycastTarget = false;
        LayoutElement dividerSize = divider.gameObject.AddComponent<LayoutElement>();
        dividerSize.minWidth = dividerSize.preferredWidth = 1f;
        BuildStatusColumn(main, "Right", right, compact, values, left.Length);

        PuiSeparatorLine(body);

        // read-only bar
        RectTransform barRow = NewUI("Bar", body);
        PuiHorizontal(barRow, 12, new RectOffset(0, 0, 2, 2));
        PuiIcon(barRow, "Icon", barIcon, 26f, StOrange);
        PuiText(barRow, "Label", barLabel, 18, false, styText, TextAlignmentOptions.Left);
        RectTransform track = NewUI("Track", barRow);
        AddImage(track, new Color(1f, 1f, 1f, 0.12f), PuiSprite("rounded")).raycastTarget = false;
        LayoutElement trackSize = track.gameObject.AddComponent<LayoutElement>();
        trackSize.flexibleWidth = 1f;
        trackSize.minHeight = trackSize.preferredHeight = 16f;
        RectTransform fill = NewUI("Fill", track);
        Anchor(fill, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.sprite = PuiSprite("bar_gradient");
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0.75f;
        fillImage.raycastTarget = false;
        RectTransform knob = NewUI("Knob", track);
        knob.anchorMin = knob.anchorMax = new Vector2(0.75f, 0.5f);
        knob.sizeDelta = new Vector2(10f, 26f);
        Image knobImage = knob.gameObject.AddComponent<Image>();
        knobImage.sprite = PuiSprite("rounded");
        knobImage.type = Image.Type.Sliced;
        knobImage.pixelsPerUnitMultiplier = 4f;
        knobImage.color = Color.white;
        knobImage.raycastTarget = false;
        TextMeshProUGUI barValue = PuiText(barRow, "Value", "75 %", 20, true, Color.white, TextAlignmentOptions.Right);
        barValue.gameObject.AddComponent<LayoutElement>().minWidth = 70f;

        var so = new SerializedObject(script);
        so.FindProperty("title").objectReferenceValue = title;
        so.FindProperty("statusDot").objectReferenceValue = dot;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("collapseButton").objectReferenceValue = collapse;
        so.FindProperty("collapseIcon").objectReferenceValue = collapseIcon;
        so.FindProperty("collapseSprite").objectReferenceValue = PuiSprite("chevron_up");
        so.FindProperty("expandSprite").objectReferenceValue = PuiSprite("chevron_down");
        so.FindProperty("body").objectReferenceValue = body.gameObject;
        SerializedProperty valueArray = so.FindProperty("values");
        valueArray.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) valueArray.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.FindProperty("barFill").objectReferenceValue = fillImage;
        so.FindProperty("barKnob").objectReferenceValue = knob;
        so.FindProperty("barValue").objectReferenceValue = barValue;
        so.ApplyModifiedPropertiesWithoutUndo();
        return script;
    }

    private static void BuildStatusColumn(Transform parent, string name, StatusRow[] rows, bool compact, Object[] values, int offset)
    {
        RectTransform column = NewUI(name, parent);
        PuiVertical(column, 0, new RectOffset(0, 0, 0, 0));
        PuiFlexible(column);
        for (int i = 0; i < rows.Length; i++)
        {
            RectTransform row = NewUI(StripTags(rows[i].label), column);
            PuiHorizontal(row, 8, new RectOffset(0, 0, 3, 3));
            row.gameObject.AddComponent<LayoutElement>().minHeight = 38f;
            PuiIcon(row, "Icon", rows[i].icon, 22f, rows[i].color);
            TextMeshProUGUI label = PuiText(row, "Label", rows[i].label, 14, false, styText, TextAlignmentOptions.Left);
            label.enableAutoSizing = true;
            label.fontSizeMin = 11f;
            label.fontSizeMax = 14f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            PuiFlexible(label.rectTransform);
            TextMeshProUGUI value = PuiText(row, "Value", "--", 17, true, rows[i].color, TextAlignmentOptions.Right);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            values[offset + i] = value;
            if (i < rows.Length - 1) PuiSeparatorLine(column);
        }
    }

    private static string StripTags(string text) => System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Replace(" ", "");

    // Six smoke cases (0, 0.2 ... 1.0) - the current one is highlighted; indication text below.
    private static void BuildSmokeCases(Transform parent, out Object[] frames, out Object[] swatches, out TextMeshProUGUI indication)
    {
        RectTransform box = NewUI("SmokeCases", parent);
        AddImage(box, StCard, PuiSprite("rounded")).raycastTarget = false;
        PuiVertical(box, 14, new RectOffset(20, 20, 16, 18));
        PuiText(box, "Title", "Smoke Level Cases", 22, true, Color.white, TextAlignmentOptions.Left);

        RectTransform strip = NewUI("Cases", box);
        PuiHorizontal(strip, 8, new RectOffset(0, 0, 0, 0));
        strip.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;

        string[] names = { "No Smoke", "Normal", "Degrading", "Severe", "NOx / SOx", "Contamination" };
        frames = new Object[names.Length];
        swatches = new Object[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            RectTransform tile = NewUI("Case" + i, strip);
            Image frame = AddImage(tile, new Color(1f, 1f, 1f, 0.05f), PuiSprite("rounded"));
            frame.raycastTarget = false;
            PuiVertical(tile, 4, new RectOffset(6, 6, 10, 10));
            tile.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            tile.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            LayoutElement size = tile.gameObject.AddComponent<LayoutElement>();
            size.flexibleWidth = 1f;
            size.minHeight = 104f;
            Image swatch = PuiIcon(tile, "Swatch", "dot", 32f, Color.gray);
            PuiText(tile, "Level", (i * 0.2f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), 18, true, Color.white, TextAlignmentOptions.Center);
            TextMeshProUGUI caseName = PuiText(tile, "Name", names[i], 12, false, PuiMuted, TextAlignmentOptions.Center);
            caseName.enableAutoSizing = true;
            caseName.fontSizeMin = 9f;
            caseName.fontSizeMax = 12f;
            frames[i] = frame;
            swatches[i] = swatch;
        }

        RectTransform note = NewUI("Indication", box);
        PuiHorizontal(note, 10, new RectOffset(0, 0, 0, 0));
        PuiIcon(note, "Icon", "tp_info", 22f, StAmber);
        indication = PuiText(note, "Text", "Normal operation / steam-heavy exhaust", 16, false, styText, TextAlignmentOptions.Left);
        PuiFlexible(indication.rectTransform);
    }

    // ===================================================================================== (i) button

    private static bool AddInfoButton(Transform root, PlantToolbar toolbar)
    {
        var so = new SerializedObject(toolbar);
        SerializedProperty info = so.FindProperty("infoButton");
        if (info == null || info.objectReferenceValue != null) return false;

        RectTransform rect = NewUI("PlantStatusButton", root);
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(46f, 46f);
        var dayNight = so.FindProperty("dayNightButton").objectReferenceValue as Button;
        rect.anchoredPosition = dayNight != null
            ? ((RectTransform)dayNight.transform).anchoredPosition + new Vector2(-150f, -23f) // fixed while playing
            : new Vector2(-150f, -43f);
        Image background = AddImage(rect, styPanel, PuiSprite("rounded"));
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        CenterIcon(rect, "tp_info", 24f, Color.white);

        info.objectReferenceValue = button;
        so.FindProperty("infoBackground").objectReferenceValue = background;
        so.FindProperty("infoIdle").colorValue = styPanel;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }
}
