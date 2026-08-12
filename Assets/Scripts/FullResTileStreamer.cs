using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// First full-resolution tile-streaming layer.
///
/// It loads only the requested velocity brick and spatial tile from
/// StreamingAssets/fullres_tiles. The current version is deliberately a data
/// service: it validates tile metadata, performs asynchronous file reads, and
/// keeps a small LRU cache. Rendering integration comes after this layer is
/// verified independently.
/// </summary>
public class FullResTileStreamer : MonoBehaviour
{
    [Header("Data")]
    public string relativeDataFolder = "fullres_tiles";
    public int cacheCapacity = 8;

    [Header("Initial request")]
    public int velocityTileStart = 320;
    public int latitudeTileStart = 128;
    public int longitudeTileStart = 256;

    private TileMetadata metadata;
    private readonly Dictionary<string, TileData> cache = new Dictionary<string, TileData>();
    private readonly LinkedList<string> lru = new LinkedList<string>();
    private string status = "Waiting";

    private void Start()
    {
        StartCoroutine(LoadMetadataAndInitialTile());
    }

    private IEnumerator LoadMetadataAndInitialTile()
    {
        string metadataPath = Path.Combine(Application.streamingAssetsPath, relativeDataFolder, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            status = $"Missing tile metadata: {metadataPath}";
            Debug.LogError(status);
            yield break;
        }

        metadata = JsonUtility.FromJson<TileMetadata>(File.ReadAllText(metadataPath));
        status = $"Metadata loaded: {metadata.tile_count} tiles";
        yield return LoadTile(velocityTileStart, latitudeTileStart, longitudeTileStart);
    }

    /// <summary>
    /// Request a tile by its origin in source-cube pixel coordinates.
    /// Origins are snapped to the tile grid before lookup.
    /// </summary>
    public void RequestTile(int velocityOrigin, int latitudeOrigin, int longitudeOrigin)
    {
        if (metadata == null) return;
        int v = Snap(velocityOrigin, metadata.tile_shape_nominal_v_y_x[0]);
        int y = Snap(latitudeOrigin, metadata.tile_shape_nominal_v_y_x[1]);
        int x = Snap(longitudeOrigin, metadata.tile_shape_nominal_v_y_x[2]);
        StartCoroutine(LoadTile(v, y, x));
    }

    private IEnumerator LoadTile(int v, int y, int x)
    {
        string key = $"v{v:0000}_y{y:0000}_x{x:0000}";
        if (cache.TryGetValue(key, out TileData existing))
        {
            Touch(key);
            status = $"Cache hit: {key}";
            Debug.Log(status);
            yield break;
        }

        TileRecord record = FindRecord(v, y, x);
        if (record == null)
        {
            status = $"Tile not found: {key}";
            Debug.LogError(status);
            yield break;
        }

        string path = Path.Combine(Application.streamingAssetsPath, relativeDataFolder, record.file);
        byte[] bytes = null;
        Exception readError = null;
        yield return ReadBytesAsync(path, result => bytes = result, error => readError = error);
        if (readError != null)
        {
            status = $"Tile read failed: {readError.Message}";
            Debug.LogError(status);
            yield break;
        }

        int expectedBytes = 2;
        foreach (int size in record.shape_v_y_x) expectedBytes *= size;
        if (bytes == null || bytes.Length != expectedBytes)
        {
            status = $"Tile size mismatch: {record.file}";
            Debug.LogError(status);
            yield break;
        }

        TileData loaded = new TileData(record, bytes);
        AddToCache(key, loaded);
        status = $"Loaded {record.file} ({bytes.Length / 1024f:F1} KB)";
        Debug.Log(status);
    }

    private IEnumerator ReadBytesAsync(string path, Action<byte[]> onSuccess, Action<Exception> onError)
    {
        bool done = false;
        byte[] result = null;
        Exception error = null;
        System.Threading.Tasks.Task task = System.Threading.Tasks.Task.Run(() =>
        {
            try { result = File.ReadAllBytes(path); }
            catch (Exception exception) { error = exception; }
            done = true;
        });
        while (!done) yield return null;
        if (error != null) onError(error); else onSuccess(result);
    }

    private TileRecord FindRecord(int v, int y, int x)
    {
        foreach (TileRecord record in metadata.tiles)
            if (record.origin_v_y_x[0] == v && record.origin_v_y_x[1] == y && record.origin_v_y_x[2] == x)
                return record;
        return null;
    }

    private void AddToCache(string key, TileData data)
    {
        if (cache.ContainsKey(key)) { Touch(key); return; }
        cache[key] = data;
        lru.AddFirst(key);
        while (lru.Count > Mathf.Max(1, cacheCapacity))
        {
            string oldKey = lru.Last.Value;
            lru.RemoveLast();
            cache.Remove(oldKey);
        }
    }

    private void Touch(string key)
    {
        lru.Remove(key);
        lru.AddFirst(key);
    }

    private static int Snap(int value, int step) { return Mathf.Max(0, value / step * step); }

    private void OnGUI()
    {
        GUI.color = new Color(0.4f, 0.9f, 1f);
        GUI.Label(new Rect(18, 18, 700, 25), $"FULL-RES TILE STREAMER  ·  {status}  ·  cache {cache.Count}/{cacheCapacity}");
    }

    [Serializable] private class TileMetadata
    {
        public int[] tile_shape_nominal_v_y_x;
        public int tile_count;
        public TileRecord[] tiles;
    }
    [Serializable] private class TileRecord
    {
        public string file;
        public int[] origin_v_y_x;
        public int[] shape_v_y_x;
    }
    private class TileData
    {
        public readonly TileRecord record;
        public readonly byte[] bytes;
        public TileData(TileRecord value, byte[] raw) { record = value; bytes = raw; }
    }
}
