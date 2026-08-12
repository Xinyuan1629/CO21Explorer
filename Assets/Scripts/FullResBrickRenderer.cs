using System;
using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Renders one full-resolution tile as a GPU brick.
///
/// This is the bridge between FullResTileStreamer and the volume shader. It
/// intentionally renders one brick first: composition of neighboring bricks
/// and view-dependent streaming should be added after this data-to-GPU step is
/// visually verified.
/// </summary>
public class FullResBrickRenderer : MonoBehaviour
{
    public string relativeDataFolder = "fullres_tiles";
    public int velocityOrigin = 320;
    public int latitudeOrigin = 128;
    public int longitudeOrigin = 256;
    public Vector3 brickScale = new Vector3(7f, 3.6f, 2.5f);
    public float threshold = 0.04f;
    public float opacity = 1.4f;
    public int raySteps = 128;

    private GameObject brickObject;
    private Material material;
    private BrickMetadata metadata;

    private IEnumerator Start()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, relativeDataFolder);
        string metadataPath = Path.Combine(folder, "metadata.json");
        metadata = JsonUtility.FromJson<BrickMetadata>(File.ReadAllText(metadataPath));

        int v = Snap(velocityOrigin, metadata.tile_shape_nominal_v_y_x[0]);
        int y = Snap(latitudeOrigin, metadata.tile_shape_nominal_v_y_x[1]);
        int x = Snap(longitudeOrigin, metadata.tile_shape_nominal_v_y_x[2]);
        BrickRecord record = FindRecord(v, y, x);
        if (record == null) throw new InvalidOperationException($"Brick not found: v{v}_y{y}_x{x}");

        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, record.file));
        int voxelCount = record.shape_v_y_x[0] * record.shape_v_y_x[1] * record.shape_v_y_x[2];
        if (bytes.Length != voxelCount * 2)
            throw new InvalidDataException($"Brick byte mismatch for {record.file}");

        ushort[] packed = new ushort[voxelCount];
        Buffer.BlockCopy(bytes, 0, packed, 0, bytes.Length);
        Color32[] pixels = new Color32[voxelCount];
        for (int i = 0; i < voxelCount; i++)
            pixels[i] = new Color32((byte)(packed[i] >> 8), 0, 0, 255);

        int width = record.shape_v_y_x[2];
        int height = record.shape_v_y_x[1];
        int depth = record.shape_v_y_x[0];
        Texture3D texture = new Texture3D(width, height, depth, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Shader shader = Shader.Find("CO21/VolumeRaymarch");
        if (shader == null) throw new InvalidOperationException("CO21/VolumeRaymarch shader not found.");
        material = new Material(shader);
        material.SetTexture("_VolumeTex", texture);
        material.SetFloat("_Threshold", threshold);
        material.SetFloat("_Opacity", opacity);
        material.SetFloat("_Steps", raySteps);
        material.SetFloat("_SliceZ", 0.5f);
        material.SetFloat("_SliceWidth", 1.0f);

        brickObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        brickObject.name = $"FullRes Brick {record.file}";
        brickObject.transform.SetParent(transform, false);
        brickObject.transform.localScale = brickScale;
        MeshRenderer renderer = brickObject.GetComponent<MeshRenderer>();
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Debug.Log($"FULL-RES brick loaded: {record.file}, " +
                  $"shape V×Y×X={depth}×{height}×{width}, " +
                  $"origin V×Y×X={v}×{y}×{x}");
        yield break;
    }

    private BrickRecord FindRecord(int v, int y, int x)
    {
        foreach (BrickRecord record in metadata.tiles)
            if (record.origin_v_y_x[0] == v && record.origin_v_y_x[1] == y && record.origin_v_y_x[2] == x)
                return record;
        return null;
    }

    private static int Snap(int value, int step) => Mathf.Max(0, value / step * step);

    [Serializable] private class BrickMetadata
    {
        public int[] tile_shape_nominal_v_y_x;
        public BrickRecord[] tiles;
    }

    [Serializable] private class BrickRecord
    {
        public string file;
        public int[] origin_v_y_x;
        public int[] shape_v_y_x;
    }
}
