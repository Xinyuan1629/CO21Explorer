# Full-resolution brick renderer

This component validates the next stage after the overview volume:

```text
full-resolution tile file
    → uint16 display values
    → RGBA32 Texture3D brick
    → CO21/VolumeRaymarch material
    → one rendered brick
```

Setup in a separate test scene:

1. Ensure `Assets/StreamingAssets/fullres_tiles/` exists.
2. Create an empty GameObject named `FullResBrick`.
3. Add `FullResBrickRenderer`.
4. Press Play.

The default brick is `v0320_y0128_x0256`, corresponding to a velocity block
near the main 12.5 km/s emission. Change the three origin fields in the
Inspector to test another tile. This intentionally renders one brick before
implementing multi-brick composition and view-dependent streaming.
