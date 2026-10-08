using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The control room tour, shown in the room (used by ControlRoomTour):
///   - card: a floating 3D card with the step, clickable BACK / NEXT / X and the VOLTAGE slider.
///     Before the tour it is the tour sign (START) standing near the entrance.
///   - floor path: glowing arrows from the worker to the place to go.
///   - ring + marker: pulsing ring on the floor around the object and a diamond above it.
///   - chime: short sound when a step is done.
/// All graphics are made in code (no textures / materials in the project needed).
/// </summary>
internal class TourVisuals
{
    private static readonly Color Accent = new Color(1f, 0.62f, 0.25f, 1f);
    private static readonly Color Done = new Color(0.35f, 0.88f, 0.5f, 1f);
    private static readonly Color Danger = new Color(1f, 0.38f, 0.32f, 1f);
    private static readonly Color CardColor = new Color(0.055f, 0.07f, 0.13f, 0.94f);
    private static readonly Color TextColor = new Color(0.86f, 0.89f, 0.95f, 1f);
    private static readonly Color Muted = new Color(0.55f, 0.6f, 0.72f, 1f);

    private const float CardWidth = 720f, CardHeight = 470f, CardScale = 0.0015f; // 1.08 m x 0.7 m

    private readonly ControlRoomTour tour;
    private readonly Scene scene;

    // card
    private readonly GameObject card;
    private readonly Canvas cardCanvas;
    private readonly TMP_Text caption, title, body, status, nextLabel, voltageValue;
    private readonly Image statusIcon;
    private readonly Button backButton, nextButton, closeButton;
    private readonly GameObject voltageRow, dotsRow;
    private readonly Slider voltageSlider;
    private readonly List<Image> dots = new List<Image>();
    private bool cardVisible = true, showingStart = true;
    private Vector3 startAnchor;
    private bool cardPlaced, startAnchorValid;

    // floor guides
    private readonly GameObject path, ring, marker;
    private readonly RectTransform pathRect;
    private readonly List<RectTransform> chevrons = new List<RectTransform>();
    private readonly List<Image> chevronImages = new List<Image>();
    private readonly Image ringImage;
    private Transform currentTarget;
    private Bounds targetBounds;

    // sound
    private readonly AudioSource audio;
    private AudioClip chime, fanfare;

    // ------------------------------------------------------------------ build

    public TourVisuals(ControlRoomTour owner)
    {
        tour = owner;
        scene = owner.gameObject.scene;

        // ---- card
        card = NewWorldCanvas("Tour Card", new Vector2(CardWidth, CardHeight), CardScale, true, out cardCanvas);
        Image bg = NewImage("Background", card.transform, CardColor, Sprites.Rounded);
        bg.type = Image.Type.Sliced;
        Fill(bg.rectTransform);
        Image bar = NewImage("Accent", card.transform, Accent, Sprites.Rounded);
        bar.type = Image.Type.Sliced;
        bar.pixelsPerUnitMultiplier = 5f; // small corners on the thin bar
        Place(bar.rectTransform, 0f, 1f, 1f, 1f, 0f, -8f, 0f, 0f);

        caption = NewText("Caption", card.transform, "GUIDED TOUR", 17f, FontStyles.Bold, Accent);
        caption.characterSpacing = 4f;
        Place(caption.rectTransform, 0f, 1f, 0.6f, 1f, 30f, -56f, 0f, -18f);

        dotsRow = new GameObject("Dots", typeof(RectTransform));
        dotsRow.transform.SetParent(card.transform, false);
        Place((RectTransform)dotsRow.transform, 1f, 1f, 1f, 1f, -270f, -52f, -70f, -22f);

        closeButton = NewButton("Close", card.transform, "X", new Color(1f, 1f, 1f, 0.08f), tour.StopTour, out _);
        Place((RectTransform)closeButton.transform, 1f, 1f, 1f, 1f, -58f, -58f, -16f, -16f);

        title = NewText("Title", card.transform, "", 34f, FontStyles.Bold, Color.white);
        Place(title.rectTransform, 0f, 1f, 1f, 1f, 30f, -112f, -30f, -62f);

        body = NewText("Body", card.transform, "", 21f, FontStyles.Normal, TextColor);
        body.alignment = TextAlignmentOptions.TopLeft;
        body.lineSpacing = 6f;
        body.enableAutoSizing = true;
        body.fontSizeMin = 15f;
        body.fontSizeMax = 21f;
        Place(body.rectTransform, 0f, 0f, 1f, 1f, 30f, 168f, -30f, -120f);

        // voltage row
        voltageRow = new GameObject("Voltage", typeof(RectTransform));
        voltageRow.transform.SetParent(card.transform, false);
        Place((RectTransform)voltageRow.transform, 0f, 0f, 1f, 0f, 30f, 118f, -30f, 158f);
        TMP_Text vLabel = NewText("Label", voltageRow.transform, "VOLTAGE", 17f, FontStyles.Bold, Muted);
        vLabel.characterSpacing = 3f;
        Place(vLabel.rectTransform, 0f, 0f, 0f, 1f, 0f, 0f, 120f, 0f);
        voltageValue = NewText("Value", voltageRow.transform, "0 %", 24f, FontStyles.Bold, Done);
        voltageValue.alignment = TextAlignmentOptions.MidlineRight;
        Place(voltageValue.rectTransform, 1f, 0f, 1f, 1f, -90f, 0f, 0f, 0f);
        voltageSlider = NewSlider(voltageRow.transform);
        Place((RectTransform)voltageSlider.transform, 0f, 0f, 1f, 1f, 126f, 4f, -100f, -4f);
        voltageSlider.onValueChanged.AddListener(tour.SetVoltage);

        // status
        statusIcon = NewImage("Icon", card.transform, Done, Sprites.Check);
        Place(statusIcon.rectTransform, 0f, 0f, 0f, 0f, 30f, 80f, 60f, 110f);
        status = NewText("Status", card.transform, "", 22f, FontStyles.Bold, Accent);
        Place(status.rectTransform, 0f, 0f, 1f, 0f, 30f, 76f, -30f, 114f);

        // buttons
        backButton = NewButton("Back", card.transform, "BACK", new Color(1f, 1f, 1f, 0.09f), tour.Back, out _);
        Place((RectTransform)backButton.transform, 0f, 0f, 0f, 0f, 30f, 18f, 190f, 66f);
        nextButton = NewButton("Next", card.transform, "NEXT", Accent, tour.Next, out nextLabel);
        Place((RectTransform)nextButton.transform, 1f, 0f, 1f, 0f, -230f, 18f, -30f, 66f);

        // ---- floor path (flat canvas, arrows scroll towards the target)
        path = NewWorldCanvas("Tour Path", new Vector2(60f, 100f), 0.01f, false, out _);
        pathRect = (RectTransform)path.transform;
        path.SetActive(false);

        // ---- ring around the object + marker above it
        ring = NewWorldCanvas("Tour Ring", new Vector2(200f, 200f), 0.01f, false, out _);
        ringImage = NewImage("Ring", ring.transform, Accent, Sprites.Ring);
        Fill(ringImage.rectTransform);
        ring.SetActive(false);

        marker = NewWorldCanvas("Tour Marker", new Vector2(100f, 100f), 0.005f, false, out _);
        Image diamond = NewImage("Diamond", marker.transform, Accent, null);
        Place(diamond.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, -25f, -25f, 25f, 25f);
        diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        marker.SetActive(false);

        // ---- sound
        audio = owner.gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;
        audio.volume = 0.5f;
    }

    public void Destroy()
    {
        foreach (GameObject go in new[] { card, path, ring, marker })
            if (go != null) Object.Destroy(go);
    }

    // ------------------------------------------------------------------ content

    public void SetCardVisible(bool visible)
    {
        cardVisible = visible;
        card.SetActive(visible);
    }

    /// <summary>The tour sign (before / after the tour).</summary>
    public void ShowStart()
    {
        showingStart = true;
        cardPlaced = false;
        startAnchorValid = false;
        caption.text = "CONTROL ROOM";
        title.text = "Guided Tour";
        body.text = "Learn voltage control and electrical safety in 6 short steps.\n\n" +
                    "Follow the glowing arrows, use the controls on this card, and watch the panel lamps.";
        dotsRow.SetActive(false);
        closeButton.gameObject.SetActive(false);
        backButton.gameObject.SetActive(false);
        voltageRow.SetActive(false);
        status.text = "";
        statusIcon.enabled = false;
        nextLabel.text = "START TOUR";
        nextButton.interactable = true;
        nextButton.onClick.RemoveAllListeners();
        nextButton.onClick.AddListener(tour.StartTour);
    }

    /// <param name="stepIndex">0-based; -1 = completed screen.</param>
    public void ShowStep(int stepIndex, int total, string stepTitle, string instruction, bool done, bool canBack, string next, bool showVoltage)
    {
        if (showingStart)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(tour.Next);
        }
        showingStart = false;

        caption.text = stepIndex < 0 ? "GUIDED TOUR  ·  COMPLETE" : $"STEP {stepIndex + 1} OF {total}";
        title.text = stepTitle;
        body.text = instruction;
        nextLabel.text = next;
        closeButton.gameObject.SetActive(stepIndex >= 0);
        backButton.gameObject.SetActive(canBack);
        voltageRow.SetActive(showVoltage);
        if (stepIndex < 0) { status.text = ""; statusIcon.enabled = false; nextButton.interactable = true; }

        BuildDots(total);
        dotsRow.SetActive(true);
        for (int i = 0; i < dots.Count; i++)
        {
            bool isDone = stepIndex < 0 || i < stepIndex;
            dots[i].color = isDone ? Done : i == stepIndex ? Accent : new Color(1f, 1f, 1f, 0.18f);
            dots[i].rectTransform.localScale = Vector3.one * (i == stepIndex ? 1.25f : 1f);
        }
    }

    /// <summary>Every frame while a step is running: progress text, NEXT state, voltage.</summary>
    public void UpdateLive(string statusText, bool stepDone, bool showTick, bool canNext, float voltage, float red, float warning)
    {
        statusIcon.enabled = showTick;
        float indent = showTick ? 40f : 0f;
        status.rectTransform.offsetMin = new Vector2(30f + indent, status.rectTransform.offsetMin.y);
        status.text = statusText;
        status.color = stepDone ? Done : voltage >= warning && voltageRow.activeSelf ? Danger : Accent;
        nextButton.interactable = canNext;

        if (voltageRow.activeSelf)
        {
            if (!Mathf.Approximately(voltageSlider.value, voltage)) voltageSlider.SetValueWithoutNotify(voltage);
            voltageValue.text = $"{voltage:F0} %";
            voltageValue.color = voltage >= warning ? Danger : voltage >= red ? Accent : Done;
        }
    }

    private void BuildDots(int total)
    {
        if (dots.Count == total) return;
        for (int i = 0; i < total; i++)
        {
            Image dot = NewImage("Dot", dotsRow.transform, Muted, Sprites.Dot);
            float right = -(total - 1 - i) * 28f; // right-aligned row, step 1 leftmost
            Place(dot.rectTransform, 1f, 0.5f, 1f, 0.5f, right - 16f, -8f, right, 8f);
            dots.Add(dot);
        }
    }

    // ------------------------------------------------------------------ per frame

    public void Tick(Transform worker, Transform target, bool walking, bool cardAtTarget)
    {
        Camera cam = ViewCamera();
        if (cam != null && cardCanvas.worldCamera != cam) cardCanvas.worldCamera = cam; // clicks on the card

        if (target != currentTarget)
        {
            currentTarget = target;
            if (target != null) targetBounds = BoundsOf(target);
        }

        float floor = worker != null ? worker.position.y : targetBounds.min.y;
        UpdateRingAndMarker(target, floor);
        UpdatePath(worker, target, walking, floor);
        if (cardVisible) UpdateCard(cam, worker, target, cardAtTarget, floor);
    }

    private void UpdateCard(Camera cam, Transform worker, Transform target, bool atTarget, float floor)
    {
        Vector3 anchor;
        if (showingStart)
        {
            if (!startAnchorValid) { startAnchor = tour.StartSignPosition(out _); startAnchorValid = worker != null; }
            anchor = startAnchor;
        }
        else if (atTarget && target != null)
        {
            // Beside the object, on the worker's side, at eye height.
            Vector3 from = worker != null ? worker.position : (cam != null ? cam.transform.position : targetBounds.center);
            Vector3 side = Vector3.ProjectOnPlane(from - targetBounds.center, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.forward;
            side.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, side);
            float reach = Mathf.Max(targetBounds.extents.x, targetBounds.extents.z);
            anchor = targetBounds.center + side * Mathf.Min(reach + 0.35f, 2.5f) + right * 0.9f;
            anchor.y = floor + 1.6f;
        }
        else if (cam != null)
        {
            // Floating ahead of the worker, a bit to the right (does not block the way).
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            anchor = cam.transform.position + forward * 2.3f + right * 0.75f;
            anchor.y = Mathf.Max(floor + 1.2f, cam.transform.position.y - 0.15f);
        }
        else return;

        Transform t = card.transform;
        float follow = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
        bool first = !cardPlaced;
        t.position = first ? anchor : Vector3.Lerp(t.position, anchor, follow);
        cardPlaced = true;

        // Turn to the camera (around the vertical axis only, readable from any side).
        if (cam != null)
        {
            Vector3 look = Vector3.ProjectOnPlane(t.position - cam.transform.position, Vector3.up);
            if (look.sqrMagnitude > 0.0001f)
                t.rotation = first ? Quaternion.LookRotation(look) : Quaternion.Slerp(t.rotation, Quaternion.LookRotation(look), Mathf.Clamp01(follow * 1.5f));
        }
    }

    private void UpdateRingAndMarker(Transform target, float floor)
    {
        bool show = target != null;
        if (ring.activeSelf != show) ring.SetActive(show);
        if (marker.activeSelf != show) marker.SetActive(show);
        if (!show) return;

        float radius = Mathf.Clamp(Mathf.Max(targetBounds.extents.x, targetBounds.extents.z) + 0.45f, 0.8f, 3f);
        float pulse = 1f + Mathf.Sin(Time.time * 3.2f) * 0.06f;
        ring.transform.SetPositionAndRotation(new Vector3(targetBounds.center.x, floor + 0.04f, targetBounds.center.z), Quaternion.LookRotation(Vector3.down, Vector3.forward));
        ring.transform.localScale = Vector3.one * (radius * 2f / 200f) * pulse;
        Color c = Accent;
        c.a = 0.55f + Mathf.Sin(Time.time * 3.2f) * 0.25f;
        ringImage.color = c;

        float bob = Mathf.Sin(Time.time * 2.5f) * 0.1f;
        marker.transform.position = new Vector3(targetBounds.center.x, Mathf.Max(targetBounds.max.y, floor + 1.4f) + 0.45f + bob, targetBounds.center.z);
        Camera cam = ViewCamera();
        if (cam != null)
        {
            Vector3 look = Vector3.ProjectOnPlane(marker.transform.position - cam.transform.position, Vector3.up);
            if (look.sqrMagnitude > 0.0001f) marker.transform.rotation = Quaternion.LookRotation(look);
        }
    }

    private void UpdatePath(Transform worker, Transform target, bool walking, float floor)
    {
        if (!walking || worker == null || target == null) { if (path.activeSelf) path.SetActive(false); return; }

        Vector3 start = worker.position; start.y = floor;
        Vector3 end = targetBounds.ClosestPoint(new Vector3(start.x, targetBounds.center.y, start.z)); end.y = floor;
        Vector3 delta = end - start;
        float length = delta.magnitude - 0.6f; // start just ahead of the feet
        if (length < 0.8f) { if (path.activeSelf) path.SetActive(false); return; }
        if (!path.activeSelf) path.SetActive(true);

        Vector3 dir = delta / delta.magnitude;
        start += dir * 0.6f;

        // Flat canvas along the way: canvas up = walking direction, facing the sky.
        float units = length / 0.01f;
        pathRect.sizeDelta = new Vector2(60f, units);
        path.transform.SetPositionAndRotation(start + dir * (length * 0.5f) + Vector3.up * 0.05f, Quaternion.LookRotation(Vector3.down, dir));

        const float spacing = 70f;
        int needed = Mathf.Min(60, Mathf.CeilToInt(units / spacing) + 1);
        while (chevrons.Count < needed)
        {
            Image chevron = NewImage("Arrow", path.transform, Accent, Sprites.Chevron);
            chevron.rectTransform.anchorMin = chevron.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            chevron.rectTransform.sizeDelta = new Vector2(46f, 34f);
            chevrons.Add(chevron.rectTransform);
            chevronImages.Add(chevron);
        }

        float offset = Time.time * 120f % spacing; // arrows flow towards the target
        for (int i = 0; i < chevrons.Count; i++)
        {
            float y = i * spacing + offset;
            bool visible = i < needed && y <= units;
            chevrons[i].gameObject.SetActive(visible);
            if (!visible) continue;
            chevrons[i].anchoredPosition = new Vector2(0f, y);
            float fade = Mathf.Clamp01(y / 60f) * Mathf.Clamp01((units - y) / 60f);
            Color c = Accent;
            c.a = 0.9f * fade;
            chevronImages[i].color = c;
        }
    }

    // ------------------------------------------------------------------ camera

    // The camera the user looks through. Not Camera.main: the worker cameras are not tagged MainCamera
    // (Control_Room has none), so clicks on the card went nowhere.
    private Camera viewCamera;
    private float nextCameraSearch;

    private Camera ViewCamera()
    {
        if (viewCamera != null && viewCamera.isActiveAndEnabled) return viewCamera;
        if (Time.unscaledTime < nextCameraSearch) return viewCamera;
        nextCameraSearch = Time.unscaledTime + 0.5f;

        CharacterCameraController worker = Object.FindAnyObjectByType<CharacterCameraController>();
        if (worker != null && worker.PlayerCamera != null && worker.PlayerCamera.isActiveAndEnabled) viewCamera = worker.PlayerCamera;
        else if (Camera.main != null) viewCamera = Camera.main;
        else
        {
            viewCamera = null;
            foreach (Camera c in Camera.allCameras)
                if (c.targetTexture == null && (viewCamera == null || c.depth > viewCamera.depth)) viewCamera = c;
        }
        return viewCamera;
    }

    // ------------------------------------------------------------------ chime

    public void PlayChime(bool big)
    {
        if (big) { if (fanfare == null) fanfare = MakeChime(new[] { 784f, 988f, 1175f, 1568f }, 0.11f); audio.PlayOneShot(fanfare); }
        else { if (chime == null) chime = MakeChime(new[] { 880f, 1320f }, 0.09f); audio.PlayOneShot(chime); }
    }

    private static AudioClip MakeChime(float[] notes, float noteSeconds)
    {
        const int rate = 44100;
        float tail = 0.35f;
        int length = Mathf.CeilToInt((notes.Length * noteSeconds + tail) * rate);
        var data = new float[length];
        for (int n = 0; n < notes.Length; n++)
        {
            int start = Mathf.RoundToInt(n * noteSeconds * rate);
            for (int i = start; i < length; i++)
            {
                float t = (i - start) / (float)rate;
                float envelope = Mathf.Exp(-t * 7f) * Mathf.Clamp01(t * 400f);
                data[i] += 0.28f * envelope * (Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[n] * t));
            }
        }
        AudioClip clip = AudioClip.Create("TourChime", length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ------------------------------------------------------------------ helpers

    private GameObject NewWorldCanvas(string name, Vector2 size, float scale, bool interactive, out Canvas canvas)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene); // unloads with the room
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one * scale;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        if (interactive) go.AddComponent<GraphicRaycaster>();
        return go;
    }

    private static Bounds BoundsOf(Transform target)
    {
        Bounds b = new Bounds(target.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any && target.TryGetComponent(out Collider c)) b = c.bounds;
        return b;
    }

    private static Image NewImage(string name, Transform parent, Color color, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
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

    private static Button NewButton(string name, Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, out TMP_Text text)
    {
        Image image = NewImage(name, parent, color, Sprites.Rounded);
        image.type = Image.Type.Sliced;
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        button.colors = colors;
        button.onClick.AddListener(onClick);
        text = NewText("Label", image.transform, label, 20f, FontStyles.Bold, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        text.characterSpacing = 3f;
        Fill(text.rectTransform);
        return button;
    }

    private static Slider NewSlider(Transform parent)
    {
        var go = new GameObject("Slider", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image track = NewImage("Track", go.transform, new Color(1f, 1f, 1f, 0.12f), Sprites.Rounded);
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 3f;
        track.raycastTarget = true; // the whole bar is draggable
        Place(track.rectTransform, 0f, 0.3f, 1f, 0.7f, 0f, 0f, 0f, 0f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        Place((RectTransform)fillArea.transform, 0f, 0.3f, 1f, 0.7f, 0f, 0f, 0f, 0f);
        Image fill = NewImage("Fill", fillArea.transform, Accent, Sprites.Rounded);
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = 3f;
        Place(fill.rectTransform, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f);

        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Place((RectTransform)handleArea.transform, 0f, 0f, 1f, 1f, 14f, 0f, -14f, 0f);
        Image handle = NewImage("Handle", handleArea.transform, Color.white, Sprites.Dot);
        handle.raycastTarget = true;
        handle.rectTransform.sizeDelta = new Vector2(28f, 0f);

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
        return slider;
    }

    private static void Fill(RectTransform rect) => Place(rect, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);

    private static void Place(RectTransform rect, float minX, float minY, float maxX, float maxY, float offMinX, float offMinY, float offMaxX, float offMaxY)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = new Vector2(offMinX, offMinY);
        rect.offsetMax = new Vector2(offMaxX, offMaxY);
    }

    // ------------------------------------------------------------------ sprites made in code

    private static class Sprites
    {
        private static Sprite rounded, dot, ring, chevron, check;

        public static Sprite Rounded => rounded != null ? rounded : rounded = Make(64, 20, (x, y) => RoundedRect(x, y, 64, 18f));
        public static Sprite Dot => dot != null ? dot : dot = Make(64, 0, (x, y) => Edge(Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)), 30f));
        public static Sprite Ring => ring != null ? ring : ring = Make(256, 0, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(127.5f, 127.5f));
            return Edge(d, 126f) * (1f - Edge(d, 112f)) + 0.18f * Edge(d, 112f) * (1f - Edge(d, 96f));
        });
        public static Sprite Chevron => chevron != null ? chevron : chevron = Make(64, 0, (x, y) =>
        {
            // ^ shape pointing up (+y): two thick strokes.
            float u = Mathf.Abs(x - 31.5f) / 31.5f, v = y / 63f;
            float lineY = 0.85f - u * 0.7f;
            return Mathf.Clamp01(1f - Mathf.Abs(v - lineY) * 9f + 0.9f) * (u < 0.98f ? 1f : 0f);
        });
        public static Sprite Check => check != null ? check : check = Make(64, 0, (x, y) =>
        {
            float c = Edge(Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)), 30f);
            float a = SegmentDistance(new Vector2(x, y), new Vector2(17f, 33f), new Vector2(27f, 22f));
            float b = SegmentDistance(new Vector2(x, y), new Vector2(27f, 22f), new Vector2(47f, 43f));
            float tick = Edge(Mathf.Min(a, b), 3.5f);
            return c * (1f - tick * 0.85f); // filled circle with the tick cut out
        });

        private static float Edge(float distance, float radius) => Mathf.Clamp01(radius - distance + 0.5f);

        private static float RoundedRect(int x, int y, int size, float radius)
        {
            float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            return Edge(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)), radius);
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Sprite Make(int size, int border, System.Func<int, int, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255f));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }
    }
}
