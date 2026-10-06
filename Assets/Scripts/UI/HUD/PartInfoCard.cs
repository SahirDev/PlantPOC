using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Part info card for the explosion view - same look and behaviour as the Main_Scene object card:
/// name + description beside the clicked part, following it while the camera orbits / zooms and while
/// the part moves (explode animation). Display only: it never takes clicks.
///
/// Created by HUDController (persistent), shown from HUDController.ShowClickContext, hidden with
/// HideClickContext / collapse. The text comes from ExplodableViewNode (Part Name, Description).
/// </summary>
[DefaultExecutionOrder(700)]
public class PartInfoCard : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0.075f, 0.09f, 0.16f, 0.94f);
    private static readonly Color TitleColor = Color.white;
    private static readonly Color BodyColor = new Color(0.78f, 0.82f, 0.9f, 1f);

    private const float GapFromObject = 24f;
    private const float ScreenMargin = 16f;

    private Canvas canvas;
    private RectTransform card;
    private CanvasGroup group;
    private Text title;
    private Text description;

    private Transform target;
    private Renderer[] renderers;
    private Camera viewCamera;

    public bool IsVisible => target != null && card.gameObject.activeSelf;

    public static PartInfoCard Create(Transform parent)
    {
        var go = new GameObject("PartInfoCard", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.AddComponent<PartInfoCard>();
    }

    private void Awake()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var cardObject = new GameObject("Card", typeof(RectTransform));
        cardObject.transform.SetParent(transform, false);
        card = (RectTransform)cardObject.transform;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0f, 0.5f);
        card.sizeDelta = new Vector2(340f, 100f);

        Image background = cardObject.AddComponent<Image>();
        background.sprite = CreateRoundedSprite();
        background.type = Image.Type.Sliced;
        background.color = PanelColor;
        background.raycastTarget = false;

        VerticalLayoutGroup layout = cardObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 14, 14);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        cardObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        group = cardObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        title = AddText(card, "PartName", font, 20, FontStyle.Bold, TitleColor);
        description = AddText(card, "PartDescription", font, 15, FontStyle.Normal, BodyColor);

        cardObject.SetActive(false);
    }

    /// <summary>Shows the card beside this part, seen through this camera (null = Camera.main).</summary>
    public void Show(Transform part, string partName, string partDescription, Camera cam)
    {
        if (part == null)
        {
            Hide();
            return;
        }

        target = part;
        renderers = part.GetComponentsInChildren<Renderer>();
        viewCamera = cam;

        title.text = partName;
        description.text = partDescription ?? string.Empty;
        description.gameObject.SetActive(!string.IsNullOrEmpty(partDescription));

        card.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(card);
        Place();
    }

    public void Hide()
    {
        target = null;
        renderers = null;
        if (card != null) card.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (target == null || !card.gameObject.activeSelf) return;

        if (!target.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        Place();
    }

    /// <summary>Beside the part's screen rectangle (right, or left if no room), clamped to the screen.</summary>
    private void Place()
    {
        Camera cam = viewCamera != null && viewCamera.isActiveAndEnabled ? viewCamera : Camera.main;
        if (cam == null || !TryGetBounds(out Bounds bounds)) return;

        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        int inFront = 0;
        Vector3 c = bounds.center, e = bounds.extents;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            Vector3 screen = cam.WorldToScreenPoint(corner);
            if (screen.z <= 0f) continue;

            inFront++;
            xMin = Mathf.Min(xMin, screen.x);
            xMax = Mathf.Max(xMax, screen.x);
            yMin = Mathf.Min(yMin, screen.y);
            yMax = Mathf.Max(yMax, screen.y);
        }

        group.alpha = inFront > 0 ? 1f : 0f;
        if (inFront == 0) return;

        float scale = canvas.scaleFactor;
        Vector2 size = card.rect.size * scale;
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);

        float rightX = xMax + GapFromObject;
        float leftX = xMin - GapFromObject - size.x;
        bool useRight = rightX + size.x <= screenSize.x - ScreenMargin || leftX < ScreenMargin;

        float x = Mathf.Clamp(useRight ? rightX : leftX, ScreenMargin, Mathf.Max(ScreenMargin, screenSize.x - ScreenMargin - size.x));
        float y = Mathf.Clamp((yMin + yMax) * 0.5f - size.y * 0.5f, ScreenMargin, Mathf.Max(ScreenMargin, screenSize.y - ScreenMargin - size.y));

        // (x, y) = bottom-left of the card in pixels -> card pivot (left, middle) in canvas space.
        Vector2 pivotScreen = new Vector2(x, y + size.y * 0.5f);
        RectTransform root = (RectTransform)transform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, pivotScreen, null, out Vector2 local))
            card.localPosition = new Vector3(local.x, local.y, 0f);
    }

    private bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        if (renderers != null)
        {
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
        }

        if (!any && target != null)
        {
            bounds = new Bounds(target.position, Vector3.one * 0.5f);
            any = true;
        }

        return any;
    }

    private static Text AddText(Transform parent, string name, Font font, int size, FontStyle style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Rounded rectangle (radius 10 px), 9-sliced, made once in code.</summary>
    private static Sprite CreateRoundedSprite()
    {
        const int size = 40, radius = 10;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "PartInfoCard Rounded", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
            float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - distance + 0.5f) * 255f));
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
    }
}
