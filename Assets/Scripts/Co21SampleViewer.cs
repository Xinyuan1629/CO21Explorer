using System.IO;
using UnityEngine;

/// <summary>
/// Small 2D viewer for the sample exported by export_co21_sample.py.
///
/// Setup:
/// 1. Copy visualization/sample_data into Assets/StreamingAssets/co21_sample.
/// 2. Add this component to an empty GameObject.
/// 3. Press Play. Use the slider or Left/Right arrows to change velocity.
///
/// This uses OnGUI deliberately: it keeps the first validation scene free from
/// prefab and UI-package dependencies. A production interface can replace it
/// with Canvas/RawImage controls later without changing the data layer.
/// </summary>
public class Co21SampleViewer : MonoBehaviour
{
    [Header("Input data")]
    public string relativeDataFolder = "co21_sample";

    [Header("Display")]
    public float channelMin = -1.0f;
    public float channelMax = 10.0f;
    public float integratedMin = -10.0f;
    public float integratedMax = 30.0f;

    private Co21SampleMetadata metadata;
    private float[] cube;
    private float[] integrated;
    private Texture2D channelTexture;
    private Texture2D integratedTexture;
    private int channelIndex;
    private string errorMessage;

    private int Width => metadata == null ? 0 : metadata.cube_shape[2];
    private int Height => metadata == null ? 0 : metadata.cube_shape[1];
    private int ChannelCount => metadata == null ? 0 : metadata.cube_shape[0];

    private void Start()
    {
        try
        {
            string folder = Path.Combine(Application.streamingAssetsPath, relativeDataFolder);
            metadata = JsonUtility.FromJson<Co21SampleMetadata>(
                File.ReadAllText(Path.Combine(folder, "metadata.json")));
            cube = Co21BinaryData.ReadLittleEndianFloat32(
                Path.Combine(folder, "cube_velocity_lat_lon_f32.bin"));
            integrated = Co21BinaryData.ReadLittleEndianFloat32(
                Path.Combine(folder, "integrated_intensity_lat_lon_f32.bin"));

            channelTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            integratedTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            channelTexture.filterMode = FilterMode.Point;
            integratedTexture.filterMode = FilterMode.Point;
            WriteTexture(integratedTexture, integrated, integratedMin, integratedMax);
            UpdateChannelTexture();
        }
        catch (System.Exception exception)
        {
            errorMessage = exception.ToString();
            Debug.LogError(errorMessage);
        }
    }

    private void Update()
    {
        if (metadata == null) return;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) SelectChannel(channelIndex - 1);
        if (Input.GetKeyDown(KeyCode.RightArrow)) SelectChannel(channelIndex + 1);
    }

    private void SelectChannel(int value)
    {
        channelIndex = Mathf.Clamp(value, 0, ChannelCount - 1);
        UpdateChannelTexture();
    }

    private void UpdateChannelTexture()
    {
        int offset = channelIndex * Width * Height;
        float[] image = new float[Width * Height];
        System.Array.Copy(cube, offset, image, 0, image.Length);
        WriteTexture(channelTexture, image, channelMin, channelMax);
    }

    private static void WriteTexture(Texture2D texture, float[] values, float min, float max)
    {
        Color[] colors = new Color[values.Length];
        float denominator = Mathf.Max(max - min, 1e-12f);
        for (int i = 0; i < values.Length; i++)
        {
            // Linear normalization is intentionally explicit and adjustable.
            // It is a display transform, not a scientific transformation.
            float normalized = Mathf.Clamp01((values[i] - min) / denominator);
            colors[i] = new Color(normalized, normalized, normalized, 1f);
        }
        texture.SetPixels(colors);
        texture.Apply(false);
    }

    private void OnGUI()
    {
        if (!string.IsNullOrEmpty(errorMessage))
        {
            GUI.Label(new Rect(20, 20, Screen.width - 40, Screen.height - 40), errorMessage);
            return;
        }
        if (metadata == null) return;

        const float margin = 20f;
        float imageWidth = Mathf.Min((Screen.width - 3 * margin) / 2f, 560f);
        float imageHeight = imageWidth * Height / Width;
        Rect channelRect = new Rect(margin, 55f, imageWidth, imageHeight);
        Rect integratedRect = new Rect(2 * margin + imageWidth, 55f, imageWidth, imageHeight);

        GUI.Label(new Rect(margin, 15f, 500f, 30f),
            $"CO(2-1) channel {channelIndex + 1}/{ChannelCount}   " +
            $"v = {metadata.velocity_km_s[channelIndex]:F2} km/s");
        GUI.DrawTexture(channelRect, channelTexture, ScaleMode.StretchToFill, false);
        GUI.DrawTexture(integratedRect, integratedTexture, ScaleMode.StretchToFill, false);
        GUI.Label(new Rect(channelRect.x, channelRect.y + imageHeight + 5f, imageWidth, 25f),
            "Current velocity channel (linear display scale)");
        GUI.Label(new Rect(integratedRect.x, integratedRect.y + imageHeight + 5f, imageWidth, 25f),
            "Integrated intensity (K km/s)");

        float sliderY = Mathf.Max(channelRect.y + imageHeight + 45f, 400f);
        float selected = GUI.HorizontalSlider(new Rect(margin, sliderY, imageWidth, 25f),
            channelIndex, 0, ChannelCount - 1);
        int selectedChannel = Mathf.RoundToInt(selected);
        if (selectedChannel != channelIndex) SelectChannel(selectedChannel);
    }
}
