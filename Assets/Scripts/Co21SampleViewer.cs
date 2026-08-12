using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Full-screen interactive CO cube explorer.
///
/// The UI is created with Unity's Canvas system at runtime, so the scene only
/// needs one GameObject with this component. This replaces the old OnGUI proof
/// of concept. Scientific values remain in the exported float arrays; color
/// mapping is display-only.
/// </summary>
public class Co21SampleViewer : MonoBehaviour
{
    [Header("Input data")]
    public string relativeDataFolder = "co21_products";

    [Header("Display range")]
    public float channelMin = -1.35f;
    public float channelMax = 8.0f;
    public float integratedMin = -4.0f;
    public float integratedMax = 35.0f;

    private Co21SampleMetadata metadata;
    private float[] cube;
    private float[] integrated;
    private Texture2D channelTexture;
    private Texture2D integratedTexture;
    private Canvas canvas;
    private RawImage channelImage;
    private RawImage integratedImage;
    private RawImage spectrumImage;
    private Slider velocitySlider;
    private Text velocityLabel;
    private Text cursorLabel;
    private Text statusLabel;
    private Text playLabel;
    private LineRenderer spectrumLine;
    private int channelIndex;
    private int selectedX;
    private int selectedY;
    private Co21DataFocus dataFocus;
    private bool playing;
    private float playTimer;
    private string errorMessage;

    private int Width => metadata == null ? 0 : metadata.overview_shape[2];
    private int Height => metadata == null ? 0 : metadata.overview_shape[1];
    private int ChannelCount => metadata == null ? 0 : metadata.overview_shape[0];

    private readonly Color background = new Color(0.025f, 0.035f, 0.06f, 1f);
    private readonly Color panel = new Color(0.055f, 0.075f, 0.12f, 0.98f);
    private readonly Color accent = new Color(0.17f, 0.82f, 0.95f, 1f);

    private void Start()
    {
        // Keep the scene camera enabled. Screen-space UI covers the background,
        // while disabling the camera makes Unity's Game view report
        // "No cameras rendering" even though the Canvas itself is visible.
        try
        {
            LoadData();
            dataFocus = FindDataFocus();
            BuildInterface();
            UpdateChannel();
            UpdateIntegrated();
            SelectPoint(Width / 2, Height / 2);
        }
        catch (Exception exception)
        {
            errorMessage = exception.ToString();
            Debug.LogError(errorMessage);
            BuildErrorInterface();
        }
    }

    private void LoadData()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, relativeDataFolder);
        metadata = JsonUtility.FromJson<Co21SampleMetadata>(
            File.ReadAllText(Path.Combine(folder, "metadata.json")));
        cube = Co21BinaryData.ReadLittleEndianFloat32(
            Path.Combine(folder, "overview_v_y_x_f32.bin"));
        integrated = Co21BinaryData.ReadLittleEndianFloat32(
            Path.Combine(folder, "main_8_16kms_moment0_f32.bin"));
    }

    private void BuildInterface()
    {
        GameObject canvasObject = new GameObject("CO21 Explorer UI");
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1440, 900);
        canvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvas.GetComponent<CanvasScaler>().matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null)
        {
            GameObject events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>();
        }

        Image backgroundImage = MakeImage("Background", canvas.transform, background);
        Stretch(backgroundImage.rectTransform, 0, 0, 0, 0);

        Text title = MakeText("Title", canvas.transform, "CO / 2-1  DATA EXPLORER", 25, Color.white, FontStyle.Bold);
        Anchor(title.rectTransform, 0, 1, 0, 1, 32, -55, 520, 38);
        Text subtitle = MakeText("Subtitle", canvas.transform,
            "OGHReS  •  APEX  •  processed spectral cube  •  overview level", 12,
            new Color(0.5f, 0.65f, 0.78f), FontStyle.Normal);
        Anchor(subtitle.rectTransform, 0, 1, 0, 1, 34, -82, 700, 24);

        Text dataBadge = MakeText("DataBadge", canvas.transform,
            $"{Width} × {Height} × {ChannelCount}   |   {metadata.brightness_unit}   |   {metadata.velocity_unit}",
            12, accent, FontStyle.Bold);
        dataBadge.alignment = TextAnchor.MiddleRight;
        Anchor(dataBadge.rectTransform, 1, 1, 1, 1, -400, -68, 365, 28);

        Image channelPanel = MakeImage("ChannelPanel", canvas.transform, panel);
        Anchor(channelPanel.rectTransform, 0, 0.34f, 0.58f, 0.91f, 28, 0, -18, -8);
        AddOutline(channelPanel.gameObject, new Color(0.12f, 0.27f, 0.38f));
        channelImage = MakeRawImage("ChannelMap", channelPanel.transform);
        Stretch(channelImage.rectTransform, 12, 38, 12, 12);
        channelImage.gameObject.AddComponent<MapClickHandler>().Initialize(this);
        Text channelCaption = MakeText("ChannelCaption", channelPanel.transform, "CHANNEL MAP", 12, accent, FontStyle.Bold);
        Anchor(channelCaption.rectTransform, 0, 1, 0, 1, 14, -30, 180, 22);

        Image integratedPanel = MakeImage("IntegratedPanel", canvas.transform, panel);
        Anchor(integratedPanel.rectTransform, 0.60f, 0.34f, 1, 0.91f, 18, 0, -28, -8);
        AddOutline(integratedPanel.gameObject, new Color(0.12f, 0.27f, 0.38f));
        integratedImage = MakeRawImage("IntegratedMap", integratedPanel.transform);
        Stretch(integratedImage.rectTransform, 12, 38, 12, 12);
        integratedImage.gameObject.AddComponent<MapClickHandler>().Initialize(this);
        Text integratedCaption = MakeText("IntegratedCaption", integratedPanel.transform,
            "MOMENT 0  ·  8–16 KM/S", 12, accent, FontStyle.Bold);
        Anchor(integratedCaption.rectTransform, 0, 1, 0, 1, 14, -30, 260, 22);

        Image spectrumPanel = MakeImage("SpectrumPanel", canvas.transform, panel);
        Anchor(spectrumPanel.rectTransform, 0, 0.07f, 0.72f, 0.31f, 28, 0, -18, 0);
        AddOutline(spectrumPanel.gameObject, new Color(0.12f, 0.27f, 0.38f));
        spectrumImage = MakeRawImage("Spectrum", spectrumPanel.transform);
        Stretch(spectrumImage.rectTransform, 12, 32, 12, 12);
        Text spectrumCaption = MakeText("SpectrumCaption", spectrumPanel.transform,
            "SELECTED PIXEL SPECTRUM", 12, accent, FontStyle.Bold);
        Anchor(spectrumCaption.rectTransform, 0, 1, 0, 1, 14, -27, 260, 20);

        Image infoPanel = MakeImage("InfoPanel", canvas.transform, panel);
        Anchor(infoPanel.rectTransform, 0.74f, 0.07f, 1, 0.31f, 18, 0, -28, 0);
        AddOutline(infoPanel.gameObject, new Color(0.12f, 0.27f, 0.38f));
        velocityLabel = MakeText("VelocityLabel", infoPanel.transform, "VELOCITY", 17, Color.white, FontStyle.Bold);
        Anchor(velocityLabel.rectTransform, 0, 0.54f, 1, 1, 18, -55, -36, -18);
        cursorLabel = MakeText("CursorLabel", infoPanel.transform, "PIXEL", 13, new Color(0.66f, 0.76f, 0.84f), FontStyle.Normal);
        Anchor(cursorLabel.rectTransform, 0, 0.28f, 1, 0.54f, 18, 0, -36, -4);
        statusLabel = MakeText("StatusLabel", infoPanel.transform, "READY", 11, new Color(0.36f, 0.88f, 0.73f), FontStyle.Bold);
        Anchor(statusLabel.rectTransform, 0, 0.06f, 1, 0.28f, 18, 0, -36, -4);

        Image controls = MakeImage("Controls", canvas.transform, new Color(0.035f, 0.05f, 0.085f, 1f));
        Anchor(controls.rectTransform, 0, 0, 1, 0.055f, 0, 0, 0, 0);
        GameObject sliderObject = new GameObject("VelocitySlider");
        sliderObject.transform.SetParent(controls.transform, false);
        velocitySlider = sliderObject.AddComponent<Slider>();
        velocitySlider.minValue = 0;
        velocitySlider.maxValue = ChannelCount - 1;
        velocitySlider.value = channelIndex;
        velocitySlider.onValueChanged.AddListener(value => SelectChannel(Mathf.RoundToInt(value)));
        RectTransform sliderRect = velocitySlider.GetComponent<RectTransform>();
        Anchor(sliderRect, 0, 0, 1, 1, 220, 12, -300, -12);
        BuildSliderVisuals(velocitySlider);

        Button playButton = MakeButton("PlayButton", controls.transform, "▶  PLAY", 12);
        Anchor(playButton.GetComponent<RectTransform>(), 0, 0, 0, 1, 28, 7, 178, -7);
        playButton.onClick.AddListener(() => { playing = !playing; playLabel.text = playing ? "Ⅱ  PAUSE" : "▶  PLAY"; });
        playLabel = playButton.GetComponentInChildren<Text>();
        Text velocityRange = MakeText("VelocityRange", controls.transform,
            $"{metadata.velocity_km_s[0]:F1} km/s", 11, new Color(0.5f, 0.65f, 0.78f), FontStyle.Normal);
        Anchor(velocityRange.rectTransform, 0, 0, 0, 0.25f, 220, 0, 150, 20);
        Text velocityEnd = MakeText("VelocityEnd", controls.transform,
            $"{metadata.velocity_km_s[ChannelCount - 1]:F1} km/s", 11, new Color(0.5f, 0.65f, 0.78f), FontStyle.Normal);
        velocityEnd.alignment = TextAnchor.UpperRight;
        Anchor(velocityEnd.rectTransform, 1, 0, 1, 0.25f, -300, 0, -28, 20);
    }

    private void BuildErrorInterface()
    {
        GameObject error = new GameObject("CO21 Error Canvas");
        Canvas errorCanvas = error.AddComponent<Canvas>();
        errorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        error.AddComponent<CanvasScaler>();
        error.AddComponent<GraphicRaycaster>();
        Text text = MakeText("Error", error.transform, errorMessage, 16, Color.red, FontStyle.Normal);
        Stretch(text.rectTransform, 40, 40, 40, 40);
    }

    private void Update()
    {
        if (metadata == null) return;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftArrowKey.wasPressedThisFrame) SelectChannel(channelIndex - 1);
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame) SelectChannel(channelIndex + 1);
            if (Keyboard.current.spaceKey.wasPressedThisFrame) { playing = !playing; if (playLabel != null) playLabel.text = playing ? "Ⅱ  PAUSE" : "▶  PLAY"; }
        }
        if (playing)
        {
            playTimer += Time.deltaTime;
            if (playTimer > 0.06f) { playTimer = 0; SelectChannel(channelIndex + 1); }
        }
    }

    private void SelectChannel(int value)
    {
        int next = Mathf.Clamp(value, 0, ChannelCount - 1);
        if (next == channelIndex && velocitySlider != null) { UpdateChannel(); return; }
        channelIndex = next;
        if (velocitySlider != null) velocitySlider.SetValueWithoutNotify(channelIndex);
        UpdateChannel();
        UpdateDataFocus();
    }

    private void UpdateChannel()
    {
        if (channelTexture == null) channelTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        WriteField(channelTexture, cube, channelIndex * Width * Height, channelMin, channelMax);
        channelImage.texture = channelTexture;
        velocityLabel.text = $"{metadata.velocity_km_s[channelIndex]:F2} KM/S";
    }

    private void UpdateIntegrated()
    {
        integratedTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        WriteField(integratedTexture, integrated, 0, integratedMin, integratedMax);
        integratedImage.texture = integratedTexture;
    }

    private void SelectPoint(int x, int y)
    {
        selectedX = Mathf.Clamp(x, 0, Width - 1);
        selectedY = Mathf.Clamp(y, 0, Height - 1);
        cursorLabel.text = $"PIXEL  X {selectedX}  ·  Y {selectedY}\nCLICK MAP TO INSPECT SPECTRUM";
        UpdateSpectrum();
        UpdateDataFocus();
    }

    private void UpdateDataFocus()
    {
        if (dataFocus == null || metadata.source_spatial_shape == null ||
            metadata.source_spatial_shape.Length < 2) return;
        int sourceY = metadata.source_spatial_shape[0];
        int sourceX = metadata.source_spatial_shape[1];
        int x = Mathf.RoundToInt(selectedX / (float)(Width - 1) * (sourceX - 1));
        int y = Mathf.RoundToInt(selectedY / (float)(Height - 1) * (sourceY - 1));
        dataFocus.Set(channelIndex, x, y);
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

    private void UpdateSpectrum()
    {
        float[] values = new float[ChannelCount];
        for (int channel = 0; channel < ChannelCount; channel++)
            values[channel] = cube[(channel * Height + selectedY) * Width + selectedX];
        DrawSpectrum(values);
    }

    private void DrawSpectrum(float[] values)
    {
        if (spectrumImage == null) return;
        Texture2D texture = new Texture2D(512, 160, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[512 * 160];
        Color transparent = new Color(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;
        for (int px = 0; px < 512; px++)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(px / 511f * (values.Length - 1)), 0, values.Length - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(channelMin, channelMax, values[index]) * 145), 0, 145);
            for (int thickness = -1; thickness <= 1; thickness++)
            {
                int row = Mathf.Clamp(py + thickness, 0, 159);
                pixels[row * 512 + px] = accent;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false);
        spectrumImage.texture = texture;
        statusLabel.text = $"{values[channelIndex]:F3} K  ·  SELECTED PIXEL";
    }

    private static void WriteField(Texture2D texture, float[] values, int offset, float min, float max)
    {
        Color[] colors = new Color[texture.width * texture.height];
        float denominator = Mathf.Max(max - min, 1e-12f);
        for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                float normalized = Mathf.Clamp01((values[offset + y * texture.width + x] - min) / denominator);
                colors[y * texture.width + x] = ScientificColor(normalized);
            }
        texture.SetPixels(colors);
        texture.Apply(false);
    }

    private static Color ScientificColor(float t)
    {
        // Compact blue-cyan-yellow-red scientific ramp for display only.
        Color a = Color.Lerp(new Color(0.02f, 0.04f, 0.18f), new Color(0.04f, 0.65f, 0.95f), Mathf.Clamp01(t * 2f));
        Color b = Color.Lerp(new Color(0.04f, 0.65f, 0.95f), new Color(1f, 0.86f, 0.18f), Mathf.Clamp01((t - 0.35f) * 2f));
        Color c = Color.Lerp(new Color(1f, 0.86f, 0.18f), new Color(0.95f, 0.12f, 0.06f), Mathf.Clamp01((t - 0.7f) * 3.33f));
        return t < 0.35f ? a : (t < 0.7f ? b : c);
    }

    private static Text MakeText(string name, Transform parent, string value, int size, Color color, FontStyle style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static RawImage MakeRawImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RawImage image = go.AddComponent<RawImage>();
        image.raycastTarget = true;
        return image;
    }

    private static Button MakeButton(string name, Transform parent, string value, int size)
    {
        Image image = MakeImage(name, parent, new Color(0.08f, 0.32f, 0.42f, 1f));
        Button button = image.gameObject.AddComponent<Button>();
        Text text = MakeText("Label", image.transform, value, size, Color.white, FontStyle.Bold);
        Stretch(text.rectTransform, 0, 0, 0, 0);
        return button;
    }

    private static void BuildSliderVisuals(Slider slider)
    {
        Image background = MakeImage("SliderBackground", slider.transform, new Color(0.12f, 0.19f, 0.28f, 1f));
        Anchor(background.rectTransform, 0, 0.30f, 1, 0.70f, 0, 0, 0, 0);

        GameObject fillAreaObject = new GameObject("FillArea");
        fillAreaObject.transform.SetParent(slider.transform, false);
        RectTransform fillArea = fillAreaObject.AddComponent<RectTransform>();
        Anchor(fillArea, 0, 0.30f, 1, 0.70f, 0, 0, 0, 0);

        Image fill = MakeImage("SliderFill", fillArea, new Color(0.1f, 0.72f, 0.9f, 1f));
        Anchor(fill.rectTransform, 0, 0, 1, 1, 0, 0, 0, 0);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        slider.fillRect = fill.rectTransform;

        Image handle = MakeImage("Handle", slider.transform, Color.white);
        handle.rectTransform.anchorMin = new Vector2(0, 0.5f);
        handle.rectTransform.anchorMax = new Vector2(0, 0.5f);
        handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        handle.rectTransform.sizeDelta = new Vector2(14, 28);
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
    }

    private static void AddOutline(GameObject target, Color color)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1.5f, 1.5f);
    }

    private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY,
                               float left, float bottom, float right, float top)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
    }

    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        Anchor(rect, 0, 0, 1, 1, left, bottom, -right, -top);
    }

    private class MapClickHandler : MonoBehaviour, IPointerClickHandler
    {
        private Co21SampleViewer owner;
        public void Initialize(Co21SampleViewer viewer) { owner = viewer; }
        public void OnPointerClick(PointerEventData eventData)
        {
            RectTransform rect = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position,
                eventData.pressEventCamera, out Vector2 local))
            {
                float x = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, local.x);
                float y = Mathf.InverseLerp(rect.rect.yMin, rect.rect.yMax, local.y);
                owner.SelectPoint(Mathf.RoundToInt(x * (owner.Width - 1)), Mathf.RoundToInt(y * (owner.Height - 1)));
            }
        }
    }
}
