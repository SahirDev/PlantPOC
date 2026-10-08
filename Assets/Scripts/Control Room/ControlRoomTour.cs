using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Guided tour of the control room: voltage control + safety, 6 steps - shown IN the room, not on the screen:
///   - a tour sign stands in the room (START), the step card floats in the room (in front of the worker, or at
///     the panel for the panel steps) with clickable NEXT / BACK / X and the VOLTAGE slider,
///   - glowing arrows on the floor lead to the place to go, a pulsing ring + marker show the object,
///   - a tick and a chime when a step is done.
/// Steps that need an action (walk somewhere, set the voltage) are checked live and advance by themselves,
/// the others with Next.
///
///   1 Safety briefing        (read, Next)
///   2 Wear your PPE          (walk to the helmet; skipped if the scene has none)
///   3 Go to the electrical panel (walk to it)
///   4 Read the panel status  (read, Next)
///   5 Raise the voltage      (slider to 40-70 %)
///   6 Over-voltage drill     (above 80 % -> alarm, then back below 75 %)
///
/// Installs itself in the scene with the ElectricalPanelInfo (Control_Room) - no setup. React can drive it too:
///   StartControlRoomTour_Extern / TourNext_Extern / TourBack_Extern / StopControlRoomTour_Extern /
///   GetTourState_Extern / SetTourUnityUI_Extern("false" = no card, React draws it)  ->  handleTourChanged.
/// </summary>
public class ControlRoomTour : MonoBehaviour
{
    public static ControlRoomTour Instance { get; private set; }

    private enum Goal { Read, Reach, VoltageInRange, OverVoltageDrill }

    private class Step
    {
        public string title, instruction;
        public string instructionWithoutTarget; // walking step whose target is missing: read + Next instead
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

    // In-room UI (TourVisuals: card, floor arrows, ring, marker, chime)
    private TourVisuals visuals;
    private const int VoltageSliderFromStep = 3; // 0-based: "Read the Panel Status"
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

        // Control_Room has three electrical panels, each with its trigger box (floor pad) in front:
        // "1st / 2nd / 3rd info position" (ElectricalPanelInfo). Their parent "Info Pointing Position" is switched
        // off in the scene, so no panel received the voltage: it is switched on here.
        var panels = new System.Collections.Generic.List<ElectricalPanelInfo>();
        Transform panelSpot = null, helmetFound = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            panels.AddRange(root.GetComponentsInChildren<ElectricalPanelInfo>(true));
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (panelSpot == null && n.Contains("panel") && n.Contains("trigger") && t.gameObject.activeInHierarchy) panelSpot = t;
                if (helmetFound == null && n.Contains("helmet") && t.GetComponentInParent<CharacterController>(true) == null) helmetFound = t;
            }
        }
        if (panels.Count == 0 && panelSpot == null) return; // not the control room

        foreach (ElectricalPanelInfo p in panels)
            for (Transform t = p.transform; t != null; t = t.parent)
                if (!t.gameObject.activeSelf)
                {
                    Debug.Log($"[ControlRoomTour] Switching on '{t.name}' (electrical panel '{p.name}' was inactive).");
                    t.gameObject.SetActive(true);
                }

        // 1st, 2nd, 3rd by the number in the name.
        panels.Sort((x, y) => PanelNumber(x.name).CompareTo(PanelNumber(y.name)));
        if (panels.Count == 0) panels.Add(panelSpot.gameObject.AddComponent<ElectricalPanelInfo>()); // no panel at all: make one

        var go = new GameObject("Control Room Tour");
        SceneManager.MoveGameObjectToScene(go, scene); // unloads with the control room
        var tour = go.AddComponent<ControlRoomTour>();
        tour.panel = panels[0];   // the tour walks to the 1st panel; the voltage drives all of them
        tour.helmet = helmetFound;

        // Status lamps (green / amber / red alarm beacon + siren) on the wall at the 3rd panel.
        ControlRoomStatusLamps.Create(panels[0], panels[panels.Count - 1].transform);
    }

    private static int PanelNumber(string name)
    {
        foreach (char c in name) if (char.IsDigit(c)) return c - '0';
        return 99;
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
                "Follow the glowing arrows to the PPE station (orange ring).\n" +
                "Check that your helmet, gloves and shoes are undamaged before you go near the panel.",
                instructionWithoutTarget =
                "Check your PPE before going near the panel:\n" +
                "•  Helmet on and strapped\n" +
                "•  Insulated (electrical) gloves, no cuts or holes\n" +
                "•  Safety shoes, dry hands, no metal jewellery" },
            new Step { title = "Go to the Electrical Panel", goal = Goal.Reach, target = () => panel != null ? panel.transform : null, instruction =
                "Follow the glowing arrows on the floor to the electrical panel (orange ring).\n" +
                "The step completes when you are standing in front of it." },
            new Step { title = "Read the Panel Status", goal = Goal.Read, target = () => panel != null ? panel.transform : null, instruction =
                "Find the GENERATOR STATUS lamps on the wall:\n" +
                "•  Green  NORMAL  =  normal operation\n" +
                "•  Amber  HIGH  =  high voltage (75 % and above)\n" +
                "•  Red flashing  ALARM + siren  =  over-voltage (80 % and above)" },
            new Step { title = "Raise the Voltage Gradually", goal = Goal.VoltageInRange, target = () => panel != null ? panel.transform : null, instruction =
                $"Drag the VOLTAGE slider on this card to bring the generator to {normalRange.x:F0}–{normalRange.y:F0} %.\n" +
                "Raise it slowly and watch the wall lamps: amber comes on at 75 %." },
            new Step { title = "Over-voltage Drill", goal = Goal.OverVoltageDrill, target = () => panel != null ? panel.transform : null, instruction =
                "Push the slider above 80 %: the red ALARM lamp flashes and the siren sounds.\n" +
                "Then bring it back below 75 % straight away - that is the correct response to an over-voltage alarm." },
        };

        visuals = new TourVisuals(this);
        RefreshUI();
    }

    private void OnDestroy()
    {
        visuals?.Destroy();
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
        instruction = !active ? "" : completed ? CompletedText : InstructionOf(steps[index]),
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

    // A walking step without a target (e.g. no PPE station in the scene) becomes a read step with its own text.
    private bool IsRead(Step step) => step.goal == Goal.Read || (step.goal == Goal.Reach && (step.target == null || step.target() == null));

    private string InstructionOf(Step step) =>
        step.goal == Goal.Reach && !string.IsNullOrEmpty(step.instructionWithoutTarget) && (step.target == null || step.target() == null)
            ? step.instructionWithoutTarget : step.instruction;

    // direction is kept for Back / Next callers (no steps are skipped any more).
    private void GoTo(int stepIndex, int direction)
    {
        index = Mathf.Clamp(stepIndex, 0, steps.Length - 1);

        stepDone = IsRead(steps[index]);
        holdStart = -1f;
        drillAlarmSeen = false;
        status = "";
        RefreshUI();
        Send(true);
    }

    private void Complete()
    {
        completed = true;
        status = "";
        visuals?.PlayChime(true);
        RefreshUI();
        Send(true);
    }

    private bool CanNext() => IsRead(steps[index]) || stepDone;

    private void Update()
    {
        if (!active || completed) return;

        Step step = steps[index];
        bool wasDone = stepDone;

        switch (step.goal)
        {
            case Goal.Reach:
            {
                if (IsRead(step)) break;
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

        if (stepDone && !wasDone) { doneTime = Time.time; visuals?.PlayChime(false); }
        if (stepDone && !IsRead(step) && Time.time - doneTime >= autoAdvanceDelay) { Next(); return; }

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

    // ------------------------------------------------------------------ in-room UI glue

    private Transform CurrentTarget() => active && !completed && steps[index].target != null ? steps[index].target() : null;

    // Card at the object for the panel steps; in front of the worker otherwise (briefing, walking).
    private bool CardAtTarget() => active && !completed && CurrentTarget() != null && steps[index].goal != Goal.Reach;

    private Transform Worker()
    {
        if (worker == null || !worker.gameObject.activeInHierarchy)
        {
            CharacterMovementController movement = FindAnyObjectByType<CharacterMovementController>();
            worker = movement != null ? movement.transform : null;
        }
        return worker;
    }

    private void RefreshUI()
    {
        if (visuals == null) return;
        visuals.SetCardVisible(unityUI);

        if (!active)
        {
            visuals.ShowStart();
            return;
        }

        if (completed)
        {
            visuals.ShowStep(-1, steps.Length, "Tour Complete", CompletedText, true, false, "FINISH", false);
            return;
        }

        visuals.ShowStep(index, steps.Length, steps[index].title, InstructionOf(steps[index]), stepDone && !IsRead(steps[index]),
            index > 0, index + 1 >= steps.Length ? "COMPLETE" : "NEXT", index >= VoltageSliderFromStep && panel != null);
    }

    private void LateUpdate()
    {
        if (visuals == null) return;
        Transform target = CurrentTarget();
        bool walking = active && !completed && target != null && steps[index].goal == Goal.Reach && !stepDone;
        visuals.Tick(Worker(), target, walking, CardAtTarget());

        if (active && !completed)
        {
            float v = Voltage();
            visuals.UpdateLive(status, stepDone, !IsRead(steps[index]) && stepDone, CanNext(), v,
                panel != null ? panel.RedThreshold : 75f, panel != null ? panel.WarningThreshold : 80f);
        }
    }

    internal void SetVoltage(float value)
    {
        if (HUDController.Instance != null) HUDController.Instance.SetControlRoomSlider(value); // same path as React
        else if (panel != null) panel.SetGeneratorValue(value);
    }

    internal Vector3 StartSignPosition(out Vector3 facing)
    {
        // In front of the spawn point (where the worker enters), facing it.
        PlayerSceneContext context = FindAnyObjectByType<PlayerSceneContext>();
        Transform spawn = context != null ? context.SpawnPoint : null;
        Transform from = spawn != null ? spawn : Worker();
        if (from == null) { facing = Vector3.forward; return transform.position + Vector3.up * 1.6f; }

        Vector3 forward = Vector3.ProjectOnPlane(from.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        facing = -forward;
        return from.position + forward * 2.6f + Vector3.up * 1.55f;
    }
}
