using TMPro;
using UnityEngine;

/// <summary>
/// Generator status lamps on the control room wall, next to the electrical panel (built at runtime by
/// ControlRoomTour - no setup):
///   GREEN  NORMAL  - below 75 %
///   AMBER  HIGH    - from 75 % (panel Red Threshold)
///   RED    ALARM   - from 80 % (panel Warning Threshold): flashing beacon + red light + siren
/// Follows the voltage of the electrical panel (React slider, tour slider - same value).
/// The unit mounts itself on the nearest wall beside the panel (raycast); without a wall it stands above it.
/// </summary>
public class ControlRoomStatusLamps : MonoBehaviour
{
    private static readonly Color GreenOn = new Color(0.25f, 1f, 0.4f), AmberOn = new Color(1f, 0.65f, 0.1f), RedOn = new Color(1f, 0.12f, 0.08f);
    private static readonly Color LampOff = new Color(0.12f, 0.12f, 0.12f);

    private ElectricalPanelInfo panel;
    private Renderer green, amber, red;
    private Material greenMat, amberMat, redMat;
    private Light greenLight, amberLight, redLight;
    private AudioSource siren;

    public static ControlRoomStatusLamps Create(ElectricalPanelInfo panel, Transform near)
    {
        var go = new GameObject("Generator Status Lamps");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, panel.gameObject.scene);
        var lamps = go.AddComponent<ControlRoomStatusLamps>();
        lamps.panel = panel;
        lamps.Build();
        lamps.Mount(near != null ? near : panel.transform);
        return lamps;
    }

    // ------------------------------------------------------------------ placement

    private void Mount(Transform near)
    {
        Bounds b = BoundsOf(near);
        float floor = b.min.y;
        if (Physics.Raycast(b.center + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 6f, ~0, QueryTriggerInteraction.Ignore))
            floor = ground.point.y;

        Vector3 origin = new Vector3(b.center.x, floor + 2.2f, b.center.z);
        RaycastHit best = default;
        bool found = false;
        for (int i = 0; i < 16; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(hit.normal.y) > 0.3f || hit.collider.GetComponentInParent<CharacterController>() != null) continue;
            if (!found || hit.distance < best.distance) { best = hit; found = true; }
        }

        if (found)
        {
            Vector3 normal = new Vector3(best.normal.x, 0f, best.normal.z).normalized;
            transform.SetPositionAndRotation(best.point + normal * 0.05f, Quaternion.LookRotation(-normal)); // front faces the room
        }
        else
        {
            transform.SetPositionAndRotation(new Vector3(b.center.x, floor + 2.3f, b.center.z), Quaternion.identity);
        }
    }

    // ------------------------------------------------------------------ build

    private void Build()
    {
        // Back plate (0.95 x 0.42 m) - the front of the unit is its -Z side (faces the room).
        Material plateMat = NewMaterial(new Color(0.16f, 0.17f, 0.19f), false);
        GameObject plate = Part(PrimitiveType.Cube, "Plate", new Vector3(0f, 0f, 0.02f), new Vector3(0.95f, 0.42f, 0.04f), plateMat);
        Object.Destroy(plate.GetComponent<Collider>());

        green = Lamp("Green", -0.3f, out greenMat, out greenLight, GreenOn);
        amber = Lamp("Amber", 0f, out amberMat, out amberLight, AmberOn);
        red = Lamp("Red", 0.3f, out redMat, out redLight, RedOn);
        redLight.range = 7f;

        Label("GENERATOR STATUS", new Vector3(0f, 0.15f, -0.005f), 0.045f, new Color(0.85f, 0.88f, 0.94f));
        Label("NORMAL", new Vector3(-0.3f, -0.15f, -0.005f), 0.04f, GreenOn);
        Label("HIGH 75%", new Vector3(0f, -0.15f, -0.005f), 0.04f, AmberOn);
        Label("ALARM 80%", new Vector3(0.3f, -0.15f, -0.005f), 0.04f, RedOn);

        siren = gameObject.AddComponent<AudioSource>();
        siren.clip = MakeSiren();
        siren.loop = true;
        siren.playOnAwake = false;
        siren.spatialBlend = 0.6f;
        siren.minDistance = 3f;
        siren.maxDistance = 25f;
        siren.volume = 0.35f;
    }

    private Renderer Lamp(string name, float x, out Material material, out Light lamp, Color color)
    {
        Material bezelMat = NewMaterial(new Color(0.3f, 0.3f, 0.32f), false);
        GameObject bezel = Part(PrimitiveType.Cylinder, name + " Bezel", new Vector3(x, 0f, -0.01f), new Vector3(0.15f, 0.015f, 0.15f), bezelMat);
        bezel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Object.Destroy(bezel.GetComponent<Collider>());

        material = NewMaterial(LampOff, true);
        GameObject bulb = Part(PrimitiveType.Sphere, name + " Lamp", new Vector3(x, 0f, -0.04f), new Vector3(0.12f, 0.12f, 0.08f), material);
        Object.Destroy(bulb.GetComponent<Collider>());

        var lightObject = new GameObject(name + " Light");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = new Vector3(x, 0f, -0.25f);
        lamp = lightObject.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = color;
        lamp.range = 2.5f;
        lamp.intensity = 0f;
        lamp.shadows = LightShadows.None;
        return bulb.GetComponent<Renderer>();
    }

    private GameObject Part(PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private void Label(string text, Vector3 localPosition, float height, Color color)
    {
        var go = new GameObject("Label " + text);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity; // text front = -Z = room side
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = height * 10f; // TextMeshPro: 10 font size ~ 1 m line height
        tmp.color = color;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(0.4f, height * 1.5f);
    }

    private static Material NewMaterial(Color color, bool emissive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;
        material.SetFloat("_Smoothness", 0.6f);
        if (emissive)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", Color.black);
        }
        return material;
    }

    // ------------------------------------------------------------------ per frame

    private void Update()
    {
        if (panel == null) return;
        float v = panel.GeneratorValue;
        bool high = v >= panel.RedThreshold, alarm = v >= panel.WarningThreshold;

        SetLamp(greenMat, greenLight, GreenOn, !high ? 1f : 0f, 0.6f);
        SetLamp(amberMat, amberLight, AmberOn, high && !alarm ? 1f : 0f, 0.8f);

        // Alarm: hard on / off beacon, 2 flashes per second.
        float flash = alarm ? Mathf.Clamp01(Mathf.Sin(Time.time * Mathf.PI * 4f) * 4f + 0.5f) : 0f;
        SetLamp(redMat, redLight, RedOn, flash, 4f);

        if (alarm && !siren.isPlaying) siren.Play();
        else if (!alarm && siren.isPlaying) siren.Stop();
    }

    private static void SetLamp(Material material, Light lamp, Color color, float level, float lightIntensity)
    {
        material.SetColor("_BaseColor", Color.Lerp(LampOff, color, level));
        material.SetColor("_EmissionColor", color * (level * 4f));
        lamp.intensity = lightIntensity * level;
        lamp.enabled = level > 0.01f;
    }

    // ------------------------------------------------------------------ helpers

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

    // Two-tone industrial siren (no audio file needed).
    private static AudioClip MakeSiren()
    {
        const int rate = 22050;
        const float seconds = 1.2f;
        int length = Mathf.RoundToInt(rate * seconds);
        var data = new float[length];
        float phase = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float frequency = (t % 0.6f) < 0.3f ? 760f : 980f;
            phase += 2f * Mathf.PI * frequency / rate;
            float square = Mathf.Sign(Mathf.Sin(phase)) * 0.35f + Mathf.Sin(phase) * 0.4f;
            data[i] = square * 0.5f;
        }
        AudioClip clip = AudioClip.Create("ControlRoomSiren", length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
