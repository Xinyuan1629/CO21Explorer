# Full-resolution tile streamer

`FullResTileStreamer` is the first Unity-side layer for the 896 full-resolution
tiles under:

```text
Assets/StreamingAssets/fullres_tiles/
```

Setup:

1. Copy the generated `visualization/fullres_tiles/` directory into
   `Assets/StreamingAssets/fullres_tiles/`.
2. Add `FullResTileStreamer` to an empty GameObject.
3. Press Play.

The script loads one initial tile, checks its byte count against metadata, and
keeps an LRU cache. `RequestTile(v, y, x)` requests a tile by source-cube
origin. It is intentionally not connected to the volume shader yet; this makes
the disk/cache layer testable before GPU brick composition is added.
