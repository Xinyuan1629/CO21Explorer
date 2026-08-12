using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Linked scientific views for the 3D page.
/// The channel map and spectrum use the float32 overview product; the volume
/// renderer remains a separate display layer. A shared channel/point selection
/// drives all views.
/// </summary>
    public class Co21LinkedViews : MonoBehaviour
{
    private const int Channels = 1000;
    private const int Height = 98;
    private const int Width = 193;
    private const string Folder = "co21_products";

    private float[] overview;
    private float[] velocity;
    private Texture2D channelTexture;
    private RawImage channelImage;
    private RawImage spectrumImage;
    private Slider channelSlider;
    private Text channelLabel;
    private Text pointLabel;
    private int channelIndex = 349;
    private int selectedX = Width / 2;
    private int selectedY = Height / 2;
    [Min(0)] public int velocityRangeHalfWidthChannels = 8;
    private Co21VolumePage volumePage;
    private Co21DataFocus dataFocus;

    private void Start()
    {
        volumePage = GetComponent<Co21VolumePage>();
        dataFocus = FindDataFocus();
        string folder = Path.Combine(Application.streamingAssetsPath, Folder);
        LinkedMetadata metadata = JsonUtility.FromJson<LinkedMetadata>(
            File.ReadAllText(Path.Combine(folder, "metadata.json")));
        velocity = metadata.velocity_km_s;
        if (metadata.source_spatial_shape != null && metadata.source_spatial_shape.Length >= 2)
        {
            dataFocusSourceY = metadata.source_spatial_shape[0];
            dataFocusSourceX = metadata.source_spatial_shape[1];
        }
        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "overview_v_y_x_f32.bin"));
        overview = new float[bytes.Length / 4];
        System.Buffer.BlockCopy(bytes, 0, overview, 0, bytes.Length);
        BuildViews();
        SelectChannel(channelIndex);
        SelectPoint(selectedX, selectedY);
    }

    private void BuildViews()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;
        EnsureEventSystem();
        Image panel = MakeImage("LinkedViewsPanel", canvas.transform, new Color(0.025f, 0.04f, 0.075f, 0.95f));
        Anchor(panel.rectTransform, 0.02f, 0.04f, 0.82f, 0.40f, 0, 0, 0, 0);
        Text title = MakeText("LinkedViewsTitle", panel.transform, "LINKED PPV VIEWS", 18, new Color(0.2f, 0.85f, 0.95f));
        Anchor(title.rectTransform, 0, 0.98f, 1, 1, 16, 0, -16, -6);

        channelImage = MakeRawImage("LinkedChannelMap", panel.transform);
        Anchor(channelImage.rectTransform, 0, 0.18f, 0.48f, 0.84f, 16, 0, -10, 0);
        channelImage.gameObject.AddComponent<LinkedMapClick>().Initialize(this);
        Text mapLabel = MakeText("MapLabel", panel.transform, "CHANNEL MAP", 12, Color.white);
        Anchor(mapLabel.rectTransform, 0, 0.04f, 0.48f, 0.17f, 16, 0, -10, 0);

        spectrumImage = MakeRawImage("LinkedSpectrum", panel.transform);
        Anchor(spectrumImage.rectTransform, 0.50f, 0.18f, 1, 0.84f, 10, 0, -16, 0);
        Text spectrumLabel = MakeText("SpectrumLabel", panel.transform, "SELECTED PIXEL SPECTRUM", 12, Color.white);
        Anchor(spectrumLabel.rectTransform, 0.50f, 0.04f, 1, 0.17f, 10, 0, -16, 0);

        channelSlider = MakeSlider(panel.transform);
        Anchor(channelSlider.GetComponent<RectTransform>(), 0.02f, 0.85f, 0.96f, 0.89f, 0, 0, 0, 0);
        channelSlider.onValueChanged.AddListener(value => SelectChannel(Mathf.RoundToInt(value)));
        channelLabel = MakeText("ChannelLabel", panel.transform, "", 13, new Color(0.35f, 0.9f, 0.75f));
        Anchor(channelLabel.rectTransform, 0.02f, 0.90f, 0.5f, 0.98f, 16, 0, 0, 0);
        pointLabel = MakeText("PointLabel", panel.transform, "", 13, new Color(0.45f, 0.7f, 0.82f));
        Anchor(pointLabel.rectTransform, 0.50f, 0.90f, 0.98f, 0.98f, 10, 0, -16, 0);
    }

    private void SelectChannel(int value)
    {
        channelIndex = Mathf.Clamp(value, 0, Channels - 1);
        if (channelSlider != null) channelSlider.SetValueWithoutNotify(channelIndex);
        if (channelLabel != null) channelLabel.text = $"CHANNEL {channelIndex}   ·   {velocity[channelIndex]:F2} KM/S";
        if (volumePage != null) volumePage.SetVelocityChannel(channelIndex, Channels);
        UpdateChannelTexture();
        UpdateSpectrum();
        UpdateDataFocus();
    }

    private void SelectPoint(int x, int y)
    {
        selectedX = Mathf.Clamp(x, 0, Width - 1);
        selectedY = Mathf.Clamp(y, 0, Height - 1);
        if (pointLabel != null) pointLabel.text = $"PIXEL X {selectedX} · Y {selectedY}";
        UpdateSpectrum();
        UpdateDataFocus();
    }

    private void UpdateDataFocus()
    {
        if (dataFocus == null) return;
        int sourceX = dataFocusSourceX;
        int sourceY = dataFocusSourceY;
        int x = Mathf.RoundToInt(selectedX / (float)(Width - 1) * (sourceX - 1));
        int y = Mathf.RoundToInt(selectedY / (float)(Height - 1) * (sourceY - 1));
        dataFocus.Set(channelIndex, x, y);
        dataFocus.SetVelocityRange(
            Mathf.Max(0, channelIndex - velocityRangeHalfWidthChannels),
            Mathf.Min(Channels - 1, channelIndex + velocityRangeHalfWidthChannels));
    }

    private static Co21DataFocus FindDataFocus()
    {
        Co21DataFocus focus = FindAnyObjectByType<Co21DataFocus>();
        if (focus != null) return focus;
        FullResBrickWindow window = FindAnyObjectByType<FullResBrickWindow>();
        if (window == null) return null;
        focus = window.GetComponent<Co21DataFocus>();
        return focus != null ? focus : window.gameObject.AddComponent<Co21DataFocus>();
    }

    private void UpdateChannelTexture()
    {
        if (channelTexture == null) channelTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[Width * Height];
        int offset = channelIndex * Width * Height;
        for (int i = 0; i < pixels.Length; i++)
        {
            float t = Mathf.Clamp01((overview[offset + i] + 1.35f) / 9.35f);
            pixels[i] = ToColor(t);
        }
        channelTexture.SetPixels32(pixels);
        channelTexture.Apply(false);
        channelImage.texture = channelTexture;
    }

    private void UpdateSpectrum()
    {
        if (spectrumImage == null) return;
        Texture2D texture = new Texture2D(512, 150, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[512 * 150];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(5, 10, 20, 255);
        for (int px = 0; px < 512; px++)
        {
            int channel = Mathf.Clamp(Mathf.RoundToInt(px / 511f * (Channels - 1)), 0, Channels - 1);
            float value = overview[(channel * Height + selectedY) * Width + selectedX];
            int py = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(-1.35f, 8f, value) * 140), 0, 149);
            for (int dy = -1; dy <= 1; dy++) pixels[Mathf.Clamp(py + dy, 0, 149) * 512 + px] = new Color32(30, 220, 245, 255);
        }
        // Current velocity cursor: the spectrum itself stays fixed when the
        // channel changes, but this marker shows the linked channel clearly.
        int cursorX = Mathf.Clamp(Mathf.RoundToInt(channelIndex / (float)(Channels - 1) * 511f), 0, 511);
        for (int py = 0; py < 150; py++)
            for (int dx = -1; dx <= 1; dx++)
                pixels[py * 512 + Mathf.Clamp(cursorX + dx, 0, 511)] = new Color32(255, 190, 70, 255);
        texture.SetPixels32(pixels); texture.Apply(false); spectrumImage.texture = texture;
    }

    private static Color32 ToColor(float t)
    {
        Color color = t < 0.5f ? Color.Lerp(new Color(0.01f, 0.03f, 0.18f), new Color(0.02f, 0.75f, 0.95f), t * 2f) : Color.Lerp(new Color(0.02f, 0.75f, 0.95f), new Color(1f, 0.35f, 0.05f), (t - 0.5f) * 2f);
        return color;
    }

    private int dataFocusSourceX = 769;
    private int dataFocusSourceY = 391;

    [System.Serializable] private class LinkedMetadata
    {
        public float[] velocity_km_s;
        public int[] source_spatial_shape;
    }
    private class LinkedMapClick : MonoBehaviour, IPointerClickHandler
    {
        private Co21LinkedViews owner;
        public void Initialize(Co21LinkedViews value) { owner = value; }
        public void OnPointerClick(PointerEventData eventData)
        {
            RectTransform rect = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                float x = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, local.x);
                float y = Mathf.InverseLerp(rect.rect.yMin, rect.rect.yMax, local.y);
                owner.SelectPoint(Mathf.RoundToInt(x * (Width - 1)), Mathf.RoundToInt(y * (Height - 1)));
            }
        }
    }
    private static Text MakeText(string name, Transform parent, string value, int size, Color color)
    { GameObject go = new GameObject(name); go.transform.SetParent(parent, false); Text text = go.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = value; text.fontSize = size; text.color = color; return text; }
    private static Image MakeImage(string name, Transform parent, Color color)
    { GameObject go = new GameObject(name); go.transform.SetParent(parent, false); Image image = go.AddComponent<Image>(); image.color = color; return image; }
    private static RawImage MakeRawImage(string name, Transform parent)
    { GameObject go = new GameObject(name); go.transform.SetParent(parent, false); return go.AddComponent<RawImage>(); }
    private static Slider MakeSlider(Transform parent)
    { GameObject go = new GameObject("LinkedVelocitySlider"); go.transform.SetParent(parent, false); Slider slider = go.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = Channels - 1; slider.value = 349; Image hitArea = go.AddComponent<Image>(); hitArea.color = new Color(1f, 1f, 1f, 0.001f); Image bg = MakeImage("Background", slider.transform, new Color(0.1f, 0.2f, 0.3f)); bg.raycastTarget = false; Anchor(bg.rectTransform, 0, 0.40f, 1, 0.60f, 0, 0, 0, 0); Image fill = MakeImage("Fill", slider.transform, new Color(0.1f, 0.75f, 0.95f)); fill.raycastTarget = false; Anchor(fill.rectTransform, 0, 0.40f, 1, 0.60f, 0, 0, 0, 0); slider.fillRect = fill.rectTransform; Image handle = MakeImage("Handle", slider.transform, Color.white); handle.sprite = MakeCircleSprite(new Color(0.55f, 0.95f, 1f)); handle.rectTransform.sizeDelta = new Vector2(14, 14); slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; return slider; }

    private static Sprite MakeCircleSprite(Color color)
    { const int size = 32; Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false); Color[] pixels = new Color[size * size]; float center = (size - 1) * 0.5f; float radius = center - 1f; for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) { float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)); pixels[y * size + x] = distance <= radius ? color : new Color(0, 0, 0, 0); } texture.SetPixels(pixels); texture.Apply(false); return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size); }

    private static void EnsureEventSystem()
    { EventSystem existing = FindAnyObjectByType<EventSystem>(); if (existing != null) { if (existing.GetComponent<InputSystemUIInputModule>() == null) existing.gameObject.AddComponent<InputSystemUIInputModule>(); return; } GameObject events = new GameObject("EventSystem"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>(); }
    private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top)
    { rect.anchorMin = new Vector2(minX, minY); rect.anchorMax = new Vector2(maxX, maxY); rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(right, top); }
}
