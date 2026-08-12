using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Minimal 3D volume page. Attach to an empty GameObject in a separate scene.
/// It creates a volume cube, loads the uint16 display product, and exposes
/// threshold/opacity controls without modifying the existing 2D page.
/// </summary>
public class Co21VolumePage : MonoBehaviour
{
    public string relativeDataFolder = "co21_volume";
    public Vector3 volumeScale = new Vector3(7.0f, 3.6f, 10.0f);
    public float initialThreshold = 0.04f;
    public float initialOpacity = 1.2f;
    [Range(0.01f, 1.0f)] public float initialSliceWidth = 0.08f;
    public int raySteps = 256;

    private Material material;
    private Slider thresholdSlider;
    private Slider opacitySlider;
    private Text readout;

    private void Start()
    {
        HideDefaultEnvironment();
        GameObject volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
        volume.name = "CO21 Volume";
        volume.transform.SetParent(transform, false);
        volume.transform.localScale = volumeScale;

        string folder = Path.Combine(Application.streamingAssetsPath, relativeDataFolder);
        string metadataPath = Path.Combine(folder, "metadata.json");
        string binaryPath = Path.Combine(folder, "volume_xyz_u16.bin");
        VolumeMetadata metadata = JsonUtility.FromJson<VolumeMetadata>(File.ReadAllText(metadataPath));
        int expectedVoxelCount = metadata.texture3d_width_x * metadata.texture3d_height_y * metadata.texture3d_depth_z;
        int maxTextureSize = SystemInfo.maxTexture3DSize;
        if (metadata.texture3d_width_x > maxTextureSize ||
            metadata.texture3d_height_y > maxTextureSize ||
            metadata.texture3d_depth_z > maxTextureSize)
        {
            throw new System.InvalidOperationException(
                $"Full volume {metadata.texture3d_width_x}x{metadata.texture3d_height_y}x{metadata.texture3d_depth_z} " +
                $"exceeds this GPU's max 3D texture size {maxTextureSize}.");
        }
        byte[] bytes = File.ReadAllBytes(binaryPath);
        if (bytes.Length != expectedVoxelCount * 2)
        {
            throw new InvalidDataException(
                $"Volume byte count mismatch: expected {expectedVoxelCount * 2}, got {bytes.Length}.");
        }
        ushort[] packedValues = new ushort[bytes.Length / 2];
        System.Buffer.BlockCopy(bytes, 0, packedValues, 0, bytes.Length);

        // Use a conventional RGBA8 3D texture for maximum Metal/URP
        // compatibility. The display value is stored in red; the other
        // channels are neutral. Sampling .r returns a normalized [0,1] value.
        Color32[] rgba = new Color32[packedValues.Length];
        for (int i = 0; i < packedValues.Length; i++)
        {
            rgba[i] = new Color32((byte)(packedValues[i] >> 8), 0, 0, 255);
        }

        Texture3D texture = new Texture3D(metadata.texture3d_width_x,
            metadata.texture3d_height_y, metadata.texture3d_depth_z,
            TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(rgba);
        texture.Apply(false, true);

        Debug.Log($"CO21 FULL volume loaded: {metadata.texture3d_width_x} x " +
            $"{metadata.texture3d_height_y} x {metadata.texture3d_depth_z}, " +
            $"axis=X longitude, Y latitude, Z velocity, " +
            $"raw bytes={bytes.Length}, voxels={rgba.Length}, " +
            $"GPU RGBA32 estimate={rgba.Length * 4 / 1048576f:F1} MB, " +
            $"max 3D texture size={maxTextureSize}");

        Shader shader = Shader.Find("CO21/VolumeRaymarch");
        if (shader == null) throw new System.Exception("CO21/VolumeRaymarch shader was not found.");
        material = new Material(shader);
        material.SetTexture("_VolumeTex", texture);
        material.SetFloat("_Threshold", initialThreshold);
        material.SetFloat("_Opacity", initialOpacity);
        material.SetFloat("_Steps", raySteps);
        material.SetFloat("_SliceZ", 0.5f);
        material.SetFloat("_SliceWidth", initialSliceWidth);
        volume.GetComponent<MeshRenderer>().material = material;
        volume.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        volume.GetComponent<MeshRenderer>().receiveShadows = false;

        CreateInterface(metadata);
        CreateOrbitCamera(volume.transform);
        if (GetComponent<Co21LinkedViews>() == null) gameObject.AddComponent<Co21LinkedViews>();
    }

    public void SetVelocityChannel(int channel, int channelCount)
    {
        if (material == null || channelCount <= 1) return;
        material.SetFloat("_SliceZ", Mathf.Clamp01(channel / (float)(channelCount - 1)));
    }

    private void CreateInterface(VolumeMetadata metadata)
    {
        GameObject canvasObject = new GameObject("3D Volume Controls");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 900);
        canvasObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        Image panel = MakeImage("Panel", canvas.transform, new Color(0.025f, 0.04f, 0.075f, 0.94f));
        Anchor(panel.rectTransform, 0.84f, 0.08f, 0.985f, 0.92f, 0, 0, 0, 0);
        Text title = MakeText("Title", panel.transform, "3D VOLUME", 18, Color.white);
        Anchor(title.rectTransform, 0, 0.88f, 1, 1, 12, 0, -12, -8);
        Text hint = MakeText("Hint", panel.transform, "DRAG TO ORBIT  ·  SCROLL TO ZOOM", 11, new Color(0.45f, 0.7f, 0.82f));
        Anchor(hint.rectTransform, 0, 0.78f, 1, 0.88f, 12, 0, -12, 0);
        thresholdSlider = MakeSlider(panel.transform, "THRESHOLD", 0.0f, 0.5f, initialThreshold, 0.55f);
        opacitySlider = MakeSlider(panel.transform, "OPACITY", 0.1f, 4.0f, initialOpacity, 0.35f);
        thresholdSlider.onValueChanged.AddListener(value => { material.SetFloat("_Threshold", value); UpdateReadout(); });
        opacitySlider.onValueChanged.AddListener(value => { material.SetFloat("_Opacity", value); UpdateReadout(); });
        readout = MakeText("Readout", panel.transform, "", 11, new Color(0.35f, 0.9f, 0.75f));
        Anchor(readout.rectTransform, 0, 0.08f, 1, 0.23f, 12, 0, -12, 0);
        UpdateReadout();
    }

    private void UpdateReadout() { if (readout != null) readout.text = $"THRESHOLD  {thresholdSlider.value:F3}\nOPACITY  {opacitySlider.value:F2}"; }

    private void CreateOrbitCamera(Transform target)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("3D Volume Camera");
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
        }
        camera.enabled = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 100f;
        camera.transform.position = new Vector3(0, 1.2f, -12f);
        camera.transform.LookAt(target);
        camera.backgroundColor = new Color(0.008f, 0.012f, 0.025f);
        camera.gameObject.AddComponent<OrbitCamera>().target = target;
    }

    private void HideDefaultEnvironment()
    {
        // The 3D page uses a controlled dark background. Hide only common
        // starter-scene environment objects; the generated volume is created
        // after this call and is therefore never affected.
        string[] environmentNames = { "Ground", "Plane", "Terrain", "Environment" };
        foreach (string name in environmentNames)
        {
            GameObject environment = GameObject.Find(name);
            if (environment != null) environment.SetActive(false);
        }
    }

    [System.Serializable]
    private class VolumeMetadata
    {
        public int texture3d_width_x;
        public int texture3d_height_y;
        public int texture3d_depth_z;
    }

    // Compact UI helpers keep this page self-contained; this page is separate
    // from the existing 2D interface and can later move to prefabs/UI Toolkit.
    private static Text MakeText(string name, Transform parent, string value, int size, Color color)
    {
        GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value; text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleLeft;
        return text;
    }
    private static Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>(); image.color = color; return image;
    }
    private static Slider MakeSlider(Transform parent, string label, float min, float max, float value, float y)
    {
        Text text = MakeText(label + "Label", parent, label, 11, new Color(0.45f, 0.7f, 0.82f));
        Anchor(text.rectTransform, 0, y + 0.045f, 1, y + 0.12f, 12, 0, -12, 0);
        GameObject go = new GameObject(label); go.transform.SetParent(parent, false);
        Slider slider = go.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.value = value;
        Image hitArea = go.AddComponent<Image>();
        hitArea.color = new Color(1f, 1f, 1f, 0.001f);
        hitArea.raycastTarget = true;
        Anchor(slider.GetComponent<RectTransform>(), 0, y, 1, y + 0.026f, 12, 0, -12, 0);
        Image background = MakeImage("Background", slider.transform, new Color(0.12f, 0.2f, 0.3f));
        background.raycastTarget = false;
        Anchor(background.rectTransform, 0, 0.38f, 1, 0.62f, 0, 0, 0, 0);
        Image fill = MakeImage("Fill", slider.transform, new Color(0.1f, 0.75f, 0.95f));
        fill.raycastTarget = false;
        Anchor(fill.rectTransform, 0, 0.38f, 1, 0.62f, 0, 0, 0, 0); slider.fillRect = fill.rectTransform;
        Image handle = MakeImage("Handle", slider.transform, Color.white);
        handle.sprite = MakeCircleSprite(new Color(0.55f, 0.95f, 1f));
        handle.rectTransform.sizeDelta = new Vector2(12, 12);
        slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; return slider;
    }

    private static Sprite MakeCircleSprite(Color color)
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];
        float center = (size - 1) * 0.5f;
        float radius = center - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                pixels[y * size + x] = distance <= radius ? color : new Color(0, 0, 0, 0);
            }
        texture.SetPixels(pixels); texture.Apply(false);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    private static void EnsureEventSystem()
    {
        EventSystem existing = FindAnyObjectByType<EventSystem>();
        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }
        GameObject events = new GameObject("EventSystem");
        events.AddComponent<EventSystem>();
        events.AddComponent<InputSystemUIInputModule>();
    }
    private static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top)
    { rect.anchorMin = new Vector2(minX, minY); rect.anchorMax = new Vector2(maxX, maxY); rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(right, top); }
}
