using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Guided tour of the control room: voltage control + safety, 6 steps. Each step says what to do; steps that
/// need an action (walk somewhere, set the voltage) are checked live and advance by themselves, the others
/// with Next. The place to go is marked in the room by a pulsing beacon.
///
///   1 Safety briefing        (read, Next)
///   2 Wear your PPE          (walk to the helmet; skipped if the scene has none)
///   3 Go to the electrical panel (walk to it)
///   4 Read the panel status  (read, Next)
///   5 Raise the voltage      (slider to 40-70 %)
///   6 Over-voltage drill     (above 80 % -> alarm, then back below 75 %)
///
/// Installs itself in the scene with the ElectricalPanelInfo (Control_Room). Unity shows a START GUIDED TOUR
/// button and the step panel; React can drive it instead:
///   StartControlRoomTour_Extern / TourNext_Extern / TourBack_Extern / StopControlRoomTour_Extern /
///   GetTourState_Extern / SetTourUnityUI_Extern("false" = React draws it)  ->  handleTourChanged.
/// </summary>
public class ControlRoomTour : MonoBehaviour
{
    public static ControlRoomTour Instance { get; private set; }

    private enum Goal { Read, Reach, VoltageInRange, OverVoltageDrill }

    private class Step
    {
        public string title, instruction;
        public Goal goal;
        public Func<Transform> target;
    }

    [Header("Voltage step (slider %)")]
    [SerializeField] private Vector2 normalRange = new Vector2(40f, 70f);
    [SerializeField, Min(0f)] private float holdSeconds = 1.5f;
    [Header("Walking steps")]
    [Tooltip("The worker has arrived when this close (m) to the target.")]
    [SerializeField, Min(0.5f)] private float arriveDistance = 2f;
    [Tooltip("Seconds between a step being done and the next one.")]
    [SerializeField, Min(0f)] private float autoAdvanceDelay = 1.5f;

    private static readonly Color Accent = new Color(1f, 0.62f, 0.25f, 1f);
    private static readonly Color Done = new Color(0.35f, 0.88f, 0.5f, 1f);

    private Step[] steps;
    private ElectricalPanelInfo panel;
    private Transform helmet;
    private Transform worker;

    private bool active, completed, stepDone;
    private int index;
    private float doneTime, holdStart = -1f;
    private bool drillAlarmSeen;
    private string status = "";
    private bool unityUI = true;

    // UI
    private GameObject canvasObject, panelObject, startButton;
    private TMP_Text stepLabel, titleLabel, bodyLabel, statusLabel, nextLabel;
    private Button backButton, nextButton;
    private Beacon beacon;
    private string lastSentState;
    private float nextSendTime;

    // ------------------------------------------------------------------ self-install

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) InstallIn(SceneManager.GetSceneAt(i));
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallIn(scene);

    private static void InstallIn(Scene scene)
    {
        if (!scene.isLoaded || (Instance != null && Instance.gameObject.scene == scene)) return;

        ElectricalPanelInfo found = null;
        Transform helmetFound = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (found == null) found = root.GetComponentInChildren<ElectricalPanelInfo>(true);
            if (helmetFound == null)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.IndexOf("helmet", StringComparison.OrdinalIgnoreCase) >= 0) { helmetFound = t; break; }
        }
        if (found == null) return;

        var go = new GameObject("Control Room Tour");
        SceneManager.MoveGameObjectToScene(go, scene); // unloads with the control room
        var tour = go.AddComponent<ControlRoomTour>();
        tour.panel = found;
        tour.helmet = helmetFound;
    }

    private void Awake()
    {
        Instance = this;
        steps = new[]
        {
            new Step { title = "Safety Briefing", goal = Goal.Read, instruction =
                "Before working on electrical equipment:\n" +
                "•  Wear a helmet, insulated gloves and safety shoes\n" +
                "•  Never touch live parts or open a live panel\n" +
                "•  Know where the emergency stop and the exits are\n" +
                "•  Report every alarm to the shift engineer" },
            new Step { title = "Wear Your PPE", goal = Goal.Reach, target = () => helmet, instruction =
                "Walk to the highlighted helmet (PPE station).\n" +
                "Check that your helmet, gloves and shoes are undamaged before you go near the panel." },
            new Step { title = "Go to the Electrical Panel", goal = Goal.Reach, target = () => panel != null ? panel.transform : null, instruction =
                "Walk to the highlighted electrical panel.\n" +
                "Stand in front of it: the panel values open and the voltage slider becomes active." },
            new Step { title = "Read the Panel Status", goal = Goal.Read, target = () => panel != null ? panel.transform : null, instruction =
                "•  Green lamp ON  =  normal operation\n" +
                "•  Red lamp  =  high voltage (75 % and above)\n" +
                "•  Flashing warning light + alarm  =  over-voltage (80 % and above)\n" +
                "Check Generator kV, Loading and the oil / winding temperatures." },
            new Step { title = "Raise the Voltage Gradually", goal = Goal.VoltageInRange, target = () => panel != null ? panel.transform : null, instruction =
                $"Use the voltage slider to bring the generator to {normalRange.x:F0}–{normalRange.y:F0} %.\n" +
                "Raise it slowly and watch Loading and Winding Temperature as it climbs." },
            new Step { title = "Over-voltage Drill", goal = Goal.OverVoltageDrill, target = () => panel != null ? panel.transform : null, instruction =
                "Push the slider above 80 %: the alarm sounds and the warning light flashes.\n" +
                "Then bring it back below 75 % straight away - that is the correct response to an over-voltage alarm." },
        };

        BuildUI();
        RefreshUI();
    }

    private void OnDestroy()
    {
        if (beacon != null && beacon.gameObject != null) Destroy(beacon.gameObject);
        if (canvasObject != null) Destroy(canvasObject);
        if (Instance == this)
        {
            if (active) Send(true);
            Instance = null;
        }
    }

    // ------------------------------------------------------------------ public (Unity buttons + React)

    public bool IsActive => active;

    public void StartTour()
    {
        active = true;
        completed = false;
        GoTo(0, 1);
    }

    public void Next()
    {
        if (!active) return;
        if (completed) { StopTour(); return; }
        if (!CanNext()) return;
        if (index + 1 >= steps.Length) Complete();
        else GoTo(index + 1, 1);
    }

    public void Back()
    {
        if (!active || completed || index == 0) return;
        GoTo(index - 1, -1);
    }

    public void StopTour()
    {
        active = completed = false;
        ShowBeacon(null);
        RefreshUI();
        Send(true);
    }

    /// <summary>false: no Unity panel / start button (React draws the tour from handleTourChanged).</summary>
    public void SetUnityUI(bool show)
    {
        unityUI = show;
        RefreshUI();
    }

    public TourStatePayload BuildState() => new TourStatePayload
    {
        active = active,
        completed = completed,
        step = active ? (completed ? steps.Length : index + 1) : 0,
        total = steps.Length,
        title = !active ? "" : completed ? "Tour Complete" : steps[index].title,
        instruction = !active ? "" : completed ? CompletedText : steps[index].instruction,
        status = active ? status : "",
        stepDone = active && (completed || stepDone),
        canNext = active && (completed || CanNext()),
        canBack = active && !completed && index > 0,
        target = active && !completed && steps[index].target != null && steps[index].target() != null ? steps[index].target().name : ""
    };

    private const string CompletedText =
        "Well done - you have practised safe voltage control.\n" +
        "Remember: wear your PPE, raise the voltage slowly, watch the lamps, and react to an alarm at once.";

    // ------------------------------------------------------------------ steps

    // direction +1 / -1: a walking step without a target (no helmet in the scene) is skipped that way.
    private void GoTo(int stepIndex, int direction)
    {
        index = Mathf.Clamp(stepIndex, 0, steps.Length - 1);
        while (steps[index].goal == Goal.Reach && steps[index].target() == null &&
               index + direction >= 0 && index + direction < steps.Length)
            index += direction;

        stepDone = steps[index].goal == Goal.Read;
        holdStart = -1f;
        drillAlarmSeen = false;
        status = "";
        ShowBeacon(steps[index].target != null ? steps[index].target() : null);
        RefreshUI();
        Send(true);
    }

    private void Complete()
    {
        completed = true;
        status = "";
        ShowBeacon(null);
        RefreshUI();
        Send(true);
    }

    private bool CanNext() => steps[index].goal == Goal.Read || stepDone;

    private void Update()
    {
        if (!active || completed) return;

        Step step = steps[index];
        bool wasDone = stepDone;

        switch (step.goal)
        {
            case Goal.Reach:
            {
                float distance = DistanceToWorker(step.target());
                if (distance < 0f) { status = "Walk with the worker (not in this view)"; break; }
                if (distance <= arriveDistance) { stepDone = true; status = "Arrived"; }
                else if (!stepDone) status = $"Distance: {distance:F1} m";
                break;
            }
            case Goal.VoltageInRange:
            {
                float v = Voltage();
                bool inRange = v >= normalRange.x && v <= normalRange.y;
                if (inRange && holdStart < 0f) holdStart = Time.time;
                if (!inRange) holdStart = -1f;
                if (inRange && Time.time - holdStart >= holdSeconds) stepDone = true;
                if (!stepDone) status = inRange ? $"Voltage {v:F0} % - hold it there..." : $"Voltage {v:F0} %  (target {normalRange.x:F0}–{normalRange.y:F0} %)";
                else status = $"Voltage {v:F0} % - normal range";
                break;
            }
            case Goal.OverVoltageDrill:
            {
                float v = Voltage();
                float warning = panel != null ? panel.WarningThreshold : 80f;
                float red = panel != null ? panel.RedThreshold : 75f;
                if (!drillAlarmSeen && v >= warning) drillAlarmSeen = true;
                if (drillAlarmSeen && v < red) stepDone = true;
                if (stepDone) status = $"Voltage {v:F0} % - back to safe, alarm cleared";
                else status = drillAlarmSeen ? $"ALARM at {v:F0} % - lower below {red:F0} % now!" : $"Voltage {v:F0} % - raise above {warning:F0} %";
                break;
            }
        }

        if (stepDone && !wasDone) doneTime = Time.time;
        if (stepDone && step.goal != Goal.Read && Time.time - doneTime >= autoAdvanceDelay) { Next(); return; }

        RefreshDynamicUI();
        Send(false);
    }

    private float Voltage() => panel != null ? panel.GeneratorValue : 0f;

    // Horizontal distance from the worker to the target's collider / renderer; -1 when the worker is off.
    private float DistanceToWorker(Transform target)
    {
        if (target == null) return float.MaxValue;
        if (worker == null || !worker.gameObject.activeInHierarchy)
        {
            CharacterMovementController movement = FindAnyObjectByType<CharacterMovementController>();
            worker = movement != null ? movement.transform : null;
        }
        if (worker == null) return -1f;

        Vector3 closest = target.position;
        if (target.TryGetComponent(out Collider c)) closest = c.ClosestPoint(worker.position);
        else if (target.TryGetComponent(out Renderer r)) closest = r.bounds.ClosestPoint(worker.position);
        Vector3 d = closest - worker.position;
        d.y = 0f;
        return d.magnitude;
    }

    // ------------------------------------------------------------------ React

    private void Send(bool force)
    {
        if (!force && Time.unscaledTime < nextSendTime) return;
        TourStatePayload state = BuildState();
        string key = JsonUtility.ToJson(state);
        if (!force && key == lastSentState) return;
        lastSentState = key;
        nextSendTime = Time.unscaledTime + 0.25f;
        CommunicationManager.HandleTourChanged_Extern(state);
    }

    // ------------------------------------------------------------------ beacon (where to go)

    private void ShowBeacon(Transform target)
    {
        if (target == null) { if (beacon != null) beacon.gameObject.SetActive(false); return; }
        if (beacon == null) beacon = Beacon.Create(Accent, gameObject.scene);
        beacon.gameObject.SetActive(true);
        beacon.SetTarget(target);
    }

    private void LateUpdate()
    {
        if (beacon != null) beacon.Tick();
    }

    // Pulsing marker above the place to go (world-space canvas, faces the camera). Ticked by the tour.
    private class Beacon
    {
        public GameObject gameObject;
        private Transform transform;
        private Vector3 top;
        private RectTransform diamond;
        private bool hasTarget;

        public static Beacon Create(Color color, Scene scene)
        {
            var go = new GameObject("Tour Beacon", typeof(RectTransform));
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(100f, 300f);
            rect.localScale = Vector3.one * 0.005f; // 0.5 m wide, 1.5 m tall

            var beam = NewImage("Beam", go.transform, new Color(color.r, color.g, color.b, 0.35f));
            beam.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            beam.rectTransform.anchorMax = new Vector2(0.5f, 0.7f);
            beam.rectTransform.sizeDelta = new Vector2(10f, 0f);

            var tip = NewImage("Diamond", go.transform, color);
            tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = new Vector2(0.5f, 0.8f);
            tip.rectTransform.sizeDelta = new Vector2(50f, 50f);
            tip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            return new Beacon { gameObject = go, transform = go.transform, diamond = tip.rectTransform };
        }

        public void SetTarget(Transform t)
        {
            hasTarget = t != null;
            if (!hasTarget) return;
            Bounds b = new Bounds(t.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any && t.TryGetComponent(out Collider c)) b = c.bounds;
            top = new Vector3(b.center.x, b.max.y + 0.3f, b.center.z);
        }

        public void Tick()
        {
            if (!hasTarget || gameObject == null || !gameObject.activeSelf) return;
            float bob = Mathf.Sin(Time.time * 3f) * 0.12f;
            transform.position = top + Vector3.up * (0.75f + bob);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 look = transform.position - cam.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(look);
            }

            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.12f;
            diamond.localScale = new Vector3(pulse, pulse, 1f);
        }
    }

    // ------------------------------------------------------------------ Unity UI

    private void BuildUI()
    {
        canvasObject = new GameObject("Tour UI", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        // Start button (bottom-left).
        var start = NewButton("Start Guided Tour", canvasObject.transform, "START GUIDED TOUR", Accent, StartTour, out _);
        startButton = start.gameObject;
        var startRect = (RectTransform)startButton.transform;
        startRect.anchorMin = startRect.anchorMax = startRect.pivot = new Vector2(0f, 0f);
        startRect.anchoredPosition = new Vector2(40f, 40f);
        startRect.sizeDelta = new Vector2(260f, 48f);

        // Step panel (top-left).
        panelObject = NewImage("Tour Panel", canvasObject.transform, new Color(0.075f, 0.09f, 0.16f, 0.96f)).gameObject;
        var panelRect = (RectTransform)panelObject.transform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(40f, -40f);
        panelRect.sizeDelta = new Vector2(500f, 330f);

        var bar = NewImage("Accent", panelObject.transform, Accent);
        Anchor(bar.rectTransform, 0f, 1f, 1f, 1f, 0f, -6f, 0f, 0f);

        stepLabel = NewText("Step", panelObject.transform, "", 13f, FontStyles.Bold, Accent);
        stepLabel.characterSpacing = 3f;
        Anchor(stepLabel.rectTransform, 0f, 1f, 1f, 1f, 22f, -40f, -60f, -14f);

        var close = NewButton("Close", panelObject.transform, "X", new Color(1f, 1f, 1f, 0.08f), StopTour, out _);
        Anchor((RectTransform)close.transform, 1f, 1f, 1f, 1f, -46f, -46f, -12f, -12f);

        titleLabel = NewText("Title", panelObject.transform, "", 22f, FontStyles.Bold, Color.white);
        Anchor(titleLabel.rectTransform, 0f, 1f, 1f, 1f, 22f, -80f, -22f, -42f);

        bodyLabel = NewText("Instruction", panelObject.transform, "", 15f, FontStyles.Normal, new Color(0.85f, 0.88f, 0.94f));
        bodyLabel.alignment = TextAlignmentOptions.TopLeft;
        bodyLabel.lineSpacing = 8f;
        Anchor(bodyLabel.rectTransform, 0f, 0f, 1f, 1f, 22f, 100f, -22f, -88f);

        statusLabel = NewText("Status", panelObject.transform, "", 15f, FontStyles.Bold, Accent);
        Anchor(statusLabel.rectTransform, 0f, 0f, 1f, 0f, 22f, 66f, -22f, 94f);

        backButton = NewButton("Back", panelObject.transform, "BACK", new Color(1f, 1f, 1f, 0.08f), Back, out _);
        Anchor((RectTransform)backButton.transform, 0f, 0f, 0f, 0f, 22f, 16f, 142f, 56f);

        nextButton = NewButton("Next", panelObject.transform, "NEXT", Accent, Next, out nextLabel);
        Anchor((RectTransform)nextButton.transform, 1f, 0f, 1f, 0f, -162f, 16f, -22f, 56f);
    }

    private void RefreshUI()
    {
        if (canvasObject == null) return;
        canvasObject.SetActive(unityUI);
        startButton.SetActive(!active);
        panelObject.SetActive(active);
        if (!active) return;

        stepLabel.text = completed ? "GUIDED TOUR  ·  COMPLETE" : $"GUIDED TOUR  ·  STEP {index + 1} / {steps.Length}";
        titleLabel.text = completed ? "Tour Complete" : steps[index].title;
        bodyLabel.text = completed ? CompletedText : steps[index].instruction;
        nextLabel.text = completed ? "FINISH" : index + 1 >= steps.Length ? "COMPLETE" : "NEXT";
        backButton.gameObject.SetActive(!completed && index > 0);
        RefreshDynamicUI();
    }

    private void RefreshDynamicUI()
    {
        if (!unityUI || statusLabel == null || !active) return;
        statusLabel.text = completed ? "" : (stepDone && steps[index].goal != Goal.Read ? "DONE  -  " : "") + status;
        statusLabel.color = stepDone ? Done : Accent;
        nextButton.interactable = completed || CanNext();
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
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
        Image image = NewImage(name, parent, color);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        text = NewText("Label", image.transform, label, 15f, FontStyles.Bold, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        text.characterSpacing = 2f;
        Anchor(text.rectTransform, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
        return button;
    }

    // anchors (min x/y, max x/y) + offsets (min x/y, max x/y)
    private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float offMinX, float offMinY, float offMaxX, float offMaxY)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = new Vector2(offMinX, offMinY);
        rect.offsetMax = new Vector2(offMaxX, offMaxY);
    }
}
