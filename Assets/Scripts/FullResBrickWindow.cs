using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Loads a fixed spatial window of full-resolution bricks for one velocity
/// brick. Each brick is positioned from its source origin, so neighboring
/// tiles form one correctly aligned PPV window.
///
/// This is intentionally a deterministic spatial window before adding
/// camera-driven streaming and eviction.
/// </summary>
public class FullResBrickWindow : MonoBehaviour
{
    public string relativeDataFolder = "fullres_tiles";
    public int velocityOrigin = 320;
    public int latitudeOrigin = 128;
    public int longitudeOrigin = 256;
    public int spatialTilesX = 2;
    public int spatialTilesY = 2;
    [Min(1)] public int gpuTextureCacheCapacity = 32;
    [Min(0)] public int spatialPrefetchRadiusTiles = 1;
    [Min(0)] public int velocityPrefetchRadiusTiles = 1;
    public Vector3 tileWorldSize = new Vector3(2.5f, 2.5f, 1.2f);
    public float threshold = 0.04f;
    public float opacity = 1.4f;
    public int raySteps = 96;
    [Header("Streaming budget")]
    [Min(1)] public int uploadsPerFrame = 1;
    [Min(1)] public int uploadBudgetBytesPerFrame = 8 * 1024 * 1024;
    public Co21TexturePrecision texturePrecision = Co21TexturePrecision.Auto;
    [Header("Viewport")]
    public bool followCameraViewport = true;
    public Camera focusCamera;
    [Min(1)] public int viewportTilesX = 2;
    [Min(1)] public int viewportTilesY = 2;

    private WindowMetadata metadata;
    private readonly List<GameObject> loadedObjects = new List<GameObject>();
    private readonly List<Material> loadedMaterials = new List<Material>();
    private readonly Dictionary<string, Texture3D> textureCache = new Dictionary<string, Texture3D>();
    private readonly LinkedList<string> textureLru = new LinkedList<string>();
    private readonly HashSet<string> activeTextureKeys = new HashSet<string>();
    private Co21DataFocus dataFocus;
    private bool suppressFocusCallback;
    private bool loading;
    private int loadGeneration;
    private int frameUploadCount;
    private int frameUploadBytes;
    private int requestedBricks;
    private int completedBricks;
    private string streamStatus = "Starting";
    private Co21TexturePrecision activePrecision;

    public string StreamingStatus => streamStatus;
    public float LoadingProgress => requestedBricks == 0 ? 1f : completedBricks / (float)requestedBricks;
    public int CachedTextureCount => textureCache.Count;

    private IEnumerator Start()
    {
        string folder = GetDataFolder();
        metadata = JsonUtility.FromJson<WindowMetadata>(
            File.ReadAllText(Path.Combine(folder, "metadata.json")));

        dataFocus = GetComponent<Co21DataFocus>();
        if (dataFocus == null) dataFocus = gameObject.AddComponent<Co21DataFocus>();
        dataFocus.Changed += OnDataFocusChanged;
        suppressFocusCallback = true;
        dataFocus.SetWithoutNotify(velocityOrigin, longitudeOrigin, latitudeOrigin);
        suppressFocusCallback = false;

        yield return ReloadWindow();
    }

    private void Update()
    {
        frameUploadCount = 0;
        frameUploadBytes = 0;
        if (followCameraViewport && focusCamera != null && !loading)
            FollowCameraViewport();
        if (Keyboard.current == null || loading) return;
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            SetVelocityOrigin(velocityOrigin - metadata.tile_shape_nominal_v_y_x[0]);
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            SetVelocityOrigin(velocityOrigin + metadata.tile_shape_nominal_v_y_x[0]);

        int latitudeDelta = 0;
        int longitudeDelta = 0;
        if (Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame)
            latitudeDelta++;
        if (Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame)
            latitudeDelta--;
        if (Keyboard.current.aKey.wasPressedThisFrame) longitudeDelta--;
        if (Keyboard.current.dKey.wasPressedThisFrame) longitudeDelta++;
        if (latitudeDelta != 0 || longitudeDelta != 0)
            SetSpatialOrigin(
                longitudeOrigin + longitudeDelta * metadata.tile_shape_nominal_v_y_x[2],
                latitudeOrigin + latitudeDelta * metadata.tile_shape_nominal_v_y_x[1]);
    }

    public void SetVelocityOrigin(int requestedOrigin)
    {
        SetWindowOrigin(requestedOrigin, longitudeOrigin, latitudeOrigin);
    }

    /// <summary>
    /// Moves the complete spatial window by one or more tile-grid steps.
    /// Arguments use the public data convention: X is longitude, Y is latitude.
    /// </summary>
    public void SetSpatialOrigin(int requestedLongitudeOrigin, int requestedLatitudeOrigin)
    {
        SetWindowOrigin(velocityOrigin, requestedLongitudeOrigin, requestedLatitudeOrigin);
    }

    /// Sets an explicit data viewport. Coordinates use public V/X/Y order;
    /// the viewport is centered on the requested focus and snapped to bricks.
    public void SetFocusViewport(int velocity, int longitude, int latitude, int tilesX, int tilesY)
    {
        viewportTilesX = Mathf.Max(1, tilesX);
        viewportTilesY = Mathf.Max(1, tilesY);
        followCameraViewport = false;
        int xStep = metadata == null ? 128 : metadata.tile_shape_nominal_v_y_x[2];
        int yStep = metadata == null ? 128 : metadata.tile_shape_nominal_v_y_x[1];
        SetWindowOrigin(velocity, longitude - viewportTilesX * xStep / 2, latitude - viewportTilesY * yStep / 2);
    }

    /// <summary>
    /// Sets the complete PPV window origin. Public arguments are V/X/Y;
    /// metadata lookup remains V/Y/X internally.
    /// </summary>
    public void SetWindowOrigin(int requestedVelocityOrigin,
                                int requestedLongitudeOrigin,
                                int requestedLatitudeOrigin)
    {
        if (metadata == null || loading) return;
        velocityOrigin = ClampGridOrigin(requestedVelocityOrigin, 0);
        longitudeOrigin = ClampGridOrigin(requestedLongitudeOrigin, 2);
        latitudeOrigin = ClampGridOrigin(requestedLatitudeOrigin, 1);
        suppressFocusCallback = true;
        if (dataFocus != null)
            dataFocus.SetWithoutNotify(velocityOrigin, longitudeOrigin, latitudeOrigin);
        suppressFocusCallback = false;
        StartCoroutine(ReloadWindow());
    }

    private void OnDataFocusChanged()
    {
        if (suppressFocusCallback) return;
        SetWindowOrigin(dataFocus.velocity, dataFocus.longitude, dataFocus.latitude);
    }

    private IEnumerator ReloadWindow()
    {
        loading = true;
        streamStatus = "Requesting visible bricks";
        int generation = ++loadGeneration;
        ClearWindow();
        string folder = GetDataFolder();

        int v = Snap(velocityOrigin, metadata.tile_shape_nominal_v_y_x[0]);
        int yStart = Snap(latitudeOrigin, metadata.tile_shape_nominal_v_y_x[1]);
        int xStart = Snap(longitudeOrigin, metadata.tile_shape_nominal_v_y_x[2]);

        requestedBricks = 0;
        completedBricks = 0;
        for (int iy = 0; iy < spatialTilesY; iy++)
        for (int ix = 0; ix < spatialTilesX; ix++)
            if (FindRecord(v, yStart + iy * metadata.tile_shape_nominal_v_y_x[1], xStart + ix * metadata.tile_shape_nominal_v_y_x[2]) != null) requestedBricks++;

        for (int iy = 0; iy < spatialTilesY; iy++)
        for (int ix = 0; ix < spatialTilesX; ix++)
        {
            if (generation != loadGeneration) yield break;
            int y = yStart + iy * metadata.tile_shape_nominal_v_y_x[1];
            int x = xStart + ix * metadata.tile_shape_nominal_v_y_x[2];
            BrickRecord record = FindRecord(v, y, x);
            if (record == null) continue;
            yield return LoadBrick(folder, record, ix, iy);
            completedBricks++;
        }

        Debug.Log($"FULL-RES brick window loaded: {loadedObjects.Count} bricks, " +
                  $"velocity origin={v}, spatial origin={xStart},{yStart}, " +
                  $"GPU texture cache={textureCache.Count}/{Mathf.Max(1, gpuTextureCacheCapacity)}");
        loading = false;
        streamStatus = $"Ready · {completedBricks}/{requestedBricks} visible · cache {textureCache.Count}/{gpuTextureCacheCapacity}";
        StartCoroutine(PrefetchNeighbors(folder, generation, v, yStart, xStart));
    }

    private IEnumerator LoadBrick(string folder, BrickRecord record, int ix, int iy)
    {
        yield return WaitForUploadBudget(record.shape_v_y_x[0] * record.shape_v_y_x[1] * record.shape_v_y_x[2] * 2);
        activeTextureKeys.Add(record.file);
        Texture3D texture;
        if (textureCache.TryGetValue(record.file, out texture))
        {
            TouchTexture(record.file);
        }
        else
        {
            byte[] bytes = null;
            Exception readError = null;
            yield return ReadBytesAsync(
                Path.Combine(folder, record.file),
                result => bytes = result,
                error => readError = error);
            if (readError != null)
                throw new IOException($"Failed to read brick {record.file}", readError);
            if (bytes == null)
                throw new IOException($"Brick read returned no data: {record.file}");

            texture = CreateTexture(bytes, record);
            AddTextureToCache(record.file, texture);
            frameUploadCount++;
            frameUploadBytes += bytes.Length;
        }
        Shader shader = Shader.Find("CO21/VolumeRaymarch");
        Material material = new Material(shader);
        material.SetTexture("_VolumeTex", texture);
        material.SetFloat("_Threshold", threshold);
        material.SetFloat("_Opacity", opacity);
        material.SetFloat("_Steps", raySteps);
        material.SetFloat("_SliceZ", 0.5f);
        material.SetFloat("_SliceWidth", 1.0f);

        GameObject brick = GameObject.CreatePrimitive(PrimitiveType.Cube);
        brick.name = $"Brick {record.file}";
        brick.transform.SetParent(transform, false);
        brick.transform.localPosition = new Vector3(ix * tileWorldSize.x, iy * tileWorldSize.y, 0);
        brick.transform.localScale = new Vector3(
            tileWorldSize.x * record.shape_v_y_x[2] / metadata.tile_shape_nominal_v_y_x[2],
            tileWorldSize.y * record.shape_v_y_x[1] / metadata.tile_shape_nominal_v_y_x[1],
            tileWorldSize.z);
        MeshRenderer renderer = brick.GetComponent<MeshRenderer>();
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        loadedObjects.Add(brick);
        loadedMaterials.Add(material);
        yield return null;
    }

    private IEnumerator PrefetchNeighbors(string folder, int generation,
                                           int v, int yStart, int xStart)
    {
        if (spatialPrefetchRadiusTiles > 0)
            yield return PrefetchSpatialNeighbors(folder, generation, v, yStart, xStart);
        if (generation != loadGeneration || velocityPrefetchRadiusTiles <= 0) yield break;

        int vStep = metadata.tile_shape_nominal_v_y_x[0];
        int yStep = metadata.tile_shape_nominal_v_y_x[1];
        int xStep = metadata.tile_shape_nominal_v_y_x[2];
        int requestedCount = 0;
        for (int iv = -velocityPrefetchRadiusTiles; iv <= velocityPrefetchRadiusTiles; iv++)
        {
            if (iv == 0) continue;
            int requestedVelocity = v + iv * vStep;
            if (requestedVelocity < 0 || requestedVelocity >= metadata.source_shape_v_y_x[0])
                continue;

            for (int iy = 0; iy < spatialTilesY; iy++)
            for (int ix = 0; ix < spatialTilesX; ix++)
            {
                if (generation != loadGeneration) yield break;
                BrickRecord record = FindRecord(
                    requestedVelocity, yStart + iy * yStep, xStart + ix * xStep);
                if (record == null) continue;
                bool loaded = false;
                yield return PrefetchTexture(folder, generation, record, value => loaded = value);
                if (loaded) requestedCount++;
            }
        }

        if (generation == loadGeneration && requestedCount > 0)
            Debug.Log($"FULL-RES velocity prefetch complete: {requestedCount} bricks, " +
                      $"GPU texture cache={textureCache.Count}/{Mathf.Max(1, gpuTextureCacheCapacity)}");
    }

    private IEnumerator PrefetchSpatialNeighbors(string folder, int generation,
                                                  int v, int yStart, int xStart)
    {
        int yStep = metadata.tile_shape_nominal_v_y_x[1];
        int xStep = metadata.tile_shape_nominal_v_y_x[2];
        int radius = spatialPrefetchRadiusTiles;
        int requestedCount = 0;

        for (int iy = -radius; iy < spatialTilesY + radius; iy++)
        for (int ix = -radius; ix < spatialTilesX + radius; ix++)
        {
            if (generation != loadGeneration) yield break;
            if (iy >= 0 && iy < spatialTilesY && ix >= 0 && ix < spatialTilesX)
                continue;

            int y = yStart + iy * yStep;
            int x = xStart + ix * xStep;
            BrickRecord record = FindRecord(v, y, x);
            if (record == null) continue;
            bool loaded = false;
            yield return PrefetchTexture(folder, generation, record, value => loaded = value);
            if (loaded) requestedCount++;
        }

        if (generation == loadGeneration && requestedCount > 0)
            Debug.Log($"FULL-RES spatial prefetch complete: {requestedCount} bricks, " +
                      $"GPU texture cache={textureCache.Count}/{Mathf.Max(1, gpuTextureCacheCapacity)}");
    }

    private IEnumerator PrefetchTexture(string folder, int generation,
                                         BrickRecord record, Action<bool> onComplete)
    {
        onComplete(false);
        if (generation != loadGeneration || textureCache.ContainsKey(record.file)) yield break;

        byte[] bytes = null;
        Exception readError = null;
        yield return ReadBytesAsync(
            Path.Combine(folder, record.file),
            result => bytes = result,
            error => readError = error);
        if (generation != loadGeneration) yield break;
        if (readError != null)
        {
            Debug.LogWarning($"Prefetch failed for {record.file}: {readError.Message}");
            yield break;
        }
        if (bytes == null) yield break;

        AddTextureToCache(record.file, CreateTexture(bytes, record));
        onComplete(true);
        yield return null;
    }

    private IEnumerator ReadBytesAsync(string path,
                                       Action<byte[]> onSuccess,
                                       Action<Exception> onError)
    {
        Task<byte[]> readTask = Task.Run(() => File.ReadAllBytes(path));
        while (!readTask.IsCompleted) yield return null;

        if (readTask.IsCanceled)
        {
            onError(new IOException($"Brick read was canceled: {path}"));
            yield break;
        }
        if (readTask.IsFaulted)
        {
            onError(readTask.Exception.GetBaseException());
            yield break;
        }
        onSuccess(readTask.Result);
    }

    private Texture3D CreateTexture(byte[] bytes, BrickRecord record)
    {
        int voxelCount = record.shape_v_y_x[0] * record.shape_v_y_x[1] * record.shape_v_y_x[2];
        if (bytes.Length != voxelCount * 2)
            throw new InvalidDataException($"Brick byte mismatch: {record.file}");

        activePrecision = texturePrecision == Co21TexturePrecision.Auto ? SelectPrecision() : texturePrecision;
        // Source values are uint16 normalized samples. R16 is lossless and
        // maps directly to the bytes; RHalf needs an explicit uint16->half
        // conversion, so do not reinterpret the source bits as IEEE half.
        if (activePrecision == Co21TexturePrecision.Half)
        {
            Debug.LogWarning("CO21: RHalf requested for uint16 source; using R16 to preserve values.");
            activePrecision = SelectPrecision();
        }
        int width = record.shape_v_y_x[2];
        int height = record.shape_v_y_x[1];
        int depth = record.shape_v_y_x[0];
        if (activePrecision == Co21TexturePrecision.R16)
        {
            Texture3D compact = new Texture3D(width, height, depth, TextureFormat.R16, false);
            compact.name = $"FullRes {activePrecision} {record.file}";
            compact.wrapMode = TextureWrapMode.Clamp; compact.filterMode = FilterMode.Bilinear;
            // Texture3D does not expose Texture2D.LoadRawTextureData in all
            // supported Unity versions. SetPixelData preserves the uint16
            // source bytes without expanding them to RGBA32.
            compact.SetPixelData(bytes, 0);
            compact.Apply(false, true); return compact;
        }
        ushort[] packed = new ushort[voxelCount];
        Buffer.BlockCopy(bytes, 0, packed, 0, bytes.Length);
        Color32[] pixels = new Color32[voxelCount];
        for (int i = 0; i < voxelCount; i++)
            pixels[i] = new Color32((byte)(packed[i] >> 8), 0, 0, 255);

        Texture3D texture = new Texture3D(width, height, depth, TextureFormat.RGBA32, false);
        texture.name = $"FullRes Texture {record.file}";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private Co21TexturePrecision SelectPrecision()
    {
        if (SystemInfo.SupportsTextureFormat(TextureFormat.R16)) return Co21TexturePrecision.R16;
        return Co21TexturePrecision.Rgba32;
    }

    private IEnumerator WaitForUploadBudget(int bytes)
    {
        while (frameUploadCount >= uploadsPerFrame ||
               frameUploadBytes > 0 && frameUploadBytes + bytes > uploadBudgetBytesPerFrame)
        {
            streamStatus = $"Uploading · {completedBricks}/{requestedBricks} · {frameUploadBytes / 1048576f:F1}/{uploadBudgetBytesPerFrame / 1048576f:F1} MB frame budget";
            yield return null;
            frameUploadCount = 0; frameUploadBytes = 0;
        }
    }

    private void FollowCameraViewport()
    {
        Vector3 local = transform.InverseTransformPoint(focusCamera.transform.position);
        int x = Mathf.RoundToInt((local.x / Mathf.Max(0.01f, tileWorldSize.x) + viewportTilesX * 0.5f) * metadata.tile_shape_nominal_v_y_x[2]);
        int y = Mathf.RoundToInt((local.y / Mathf.Max(0.01f, tileWorldSize.y) + viewportTilesY * 0.5f) * metadata.tile_shape_nominal_v_y_x[1]);
        int v = dataFocus != null ? dataFocus.velocity : velocityOrigin;
        if (x != longitudeOrigin || y != latitudeOrigin) SetSpatialOrigin(x, y);
    }

    private void OnGUI()
    {
        GUI.color = new Color(0.45f, 0.95f, 1f);
        GUI.Box(new Rect(16, 16, 410, 92), "DATA FOCUS / STREAMING");
        GUI.Label(new Rect(28, 40, 390, 22), $"V {velocityOrigin} · X {longitudeOrigin} · Y {latitudeOrigin}");
        GUI.Label(new Rect(28, 62, 390, 22), $"{streamStatus} · {(LoadingProgress * 100f):F0}%");
        GUI.Label(new Rect(28, 84, 390, 22), $"precision {activePrecision} · cache {textureCache.Count}/{gpuTextureCacheCapacity}");
    }

    private void AddTextureToCache(string key, Texture3D texture)
    {
        textureCache[key] = texture;
        textureLru.AddFirst(key);
        TrimTextureCache();
    }

    private void TouchTexture(string key)
    {
        textureLru.Remove(key);
        textureLru.AddFirst(key);
    }

    private void TrimTextureCache()
    {
        int capacity = Mathf.Max(1, gpuTextureCacheCapacity);
        while (textureCache.Count > capacity)
        {
            LinkedListNode<string> candidate = textureLru.Last;
            while (candidate != null && activeTextureKeys.Contains(candidate.Value))
                candidate = candidate.Previous;
            if (candidate == null) break;

            string key = candidate.Value;
            textureLru.Remove(candidate);
            if (textureCache.TryGetValue(key, out Texture3D texture))
            {
                textureCache.Remove(key);
                if (texture != null) Destroy(texture);
            }
        }
    }

    private void ClearWindow()
    {
        activeTextureKeys.Clear();
        foreach (GameObject brick in loadedObjects)
            if (brick != null) Destroy(brick);
        foreach (Material material in loadedMaterials)
            if (material != null) Destroy(material);
        loadedObjects.Clear();
        loadedMaterials.Clear();
    }

    private void OnDestroy()
    {
        if (dataFocus != null) dataFocus.Changed -= OnDataFocusChanged;
        foreach (Texture3D texture in textureCache.Values)
            if (texture != null) Destroy(texture);
        textureCache.Clear();
        textureLru.Clear();
        activeTextureKeys.Clear();
    }

    private BrickRecord FindRecord(int v, int y, int x)
    {
        foreach (BrickRecord record in metadata.tiles)
            if (record.origin_v_y_x[0] == v && record.origin_v_y_x[1] == y && record.origin_v_y_x[2] == x)
                return record;
        return null;
    }

    private int ClampGridOrigin(int requestedOrigin, int axis)
    {
        int step = metadata.tile_shape_nominal_v_y_x[axis];
        int maxOrigin = (metadata.source_shape_v_y_x[axis] - 1) / step * step;
        return Mathf.Clamp(requestedOrigin / step * step, 0, maxOrigin);
    }

    private string GetDataFolder() => Path.Combine(Application.streamingAssetsPath, relativeDataFolder);

    private static int Snap(int value, int step) => Mathf.Max(0, value / step * step);

    [Serializable] private class WindowMetadata
    {
        public int[] tile_shape_nominal_v_y_x;
        public int[] source_shape_v_y_x;
        public BrickRecord[] tiles;
    }

    [Serializable] private class BrickRecord
    {
        public string file;
        public int[] origin_v_y_x;
        public int[] shape_v_y_x;
    }
}
