# CO21Explorer

Unity-based interactive visualization for the APEX OGHReS 12CO(2-1) spectral
cube. The project is designed for full-resolution PPV exploration without
loading the complete data cube into GPU memory at once.

## Scientific data model

The cube is a position-position-velocity data product, not a Cartesian 3D
volume:

```text
X = Galactic longitude (GLON)
Y = Galactic latitude  (GLAT)
V = radial velocity    (VRAD)
```

The source cube has shape:

```text
V × Y × X = 1000 × 391 × 769
```

Velocity identifies spectral and kinematic structure. It must not be
interpreted directly as physical depth along the line of sight.

## Data products

The Unity project uses two data layers:

```text
Overview layer       1000 × 98 × 193 float32 product
Full-resolution layer 896 V/Y/X bricks, nominally 32 × 128 × 128 uint16
```

The overview layer is used for fast global browsing, channel maps, moment maps
and spectra. The full-resolution layer is loaded only around the current data
focus.

Expected Unity data locations:

```text
Assets/StreamingAssets/co21_products/
Assets/StreamingAssets/co21_volume/
Assets/StreamingAssets/fullres_tiles/
```

The large generated binary products are intentionally excluded from Git by
`.gitignore`. After cloning, generate them with the Python preparation scripts
or copy them into the paths above. The metadata JSON files are kept with the
project so the expected formats, shapes and axis order remain documented.

The full-resolution metadata is stored in:

```text
Assets/StreamingAssets/fullres_tiles/metadata.json
```

## Runtime architecture

```text
Overview map / velocity control
            ↓
      Co21DataFocus (V/X/Y)
            ↓
   FullResBrickWindow request
            ↓
   metadata brick lookup V/Y/X
            ↓
 asynchronous file read
            ↓
 main-thread decode and Texture3D upload
            ↓
       GPU Texture3D LRU cache
            ↓
       visible brick GameObjects
            ↓
       VolumeRaymarch shader
```

### Main scripts

- `Co21VolumePage.cs` — overview 3D volume rendering and controls.
- `Co21LinkedViews.cs` — linked channel map, spectrum and velocity controls.
- `Co21SampleViewer.cs` — full-screen overview viewer and map interaction.
- `Co21DataFocus.cs` — shared PPV focus using public order `V/X/Y`.
- `FullResBrickWindow.cs` — visible full-resolution brick window, streaming,
  cache and prefetch.
- `FullResTileStreamer.cs` — lower-level tile loading and LRU prototype.
- `FullResBrickRenderer.cs` — single-brick rendering test.
- `Co21VolumeRaymarch.shader` — raymarching shader for volume bricks.

## Full-resolution streaming design

`FullResBrickWindow` currently displays a configurable spatial window. The
default is 2×2 bricks for one velocity brick. Each visible brick is positioned
using its source grid location and actual edge shape.

The public coordinate convention is:

```csharp
SetWindowOrigin(velocity, longitude, latitude);
SetSpatialOrigin(longitude, latitude);
```

The metadata lookup convention remains `V×Y×X` internally.

### GPU cache

The default GPU cache capacity is 32 `Texture3D` objects. At the current
RGBA32 representation this is approximately 64 MB for nominal bricks. The
cache uses LRU eviction and never evicts textures used by the active window.

### File loading

Brick byte reads run in a background `Task`. Unity texture creation,
decompression/conversion and GPU upload remain on the Unity main thread.
Visible bricks are uploaded one at a time through the coroutine flow so that a
window request yields between uploads.

### Prefetching

The default configuration prefetches:

- one spatial ring around the visible window;
- one neighboring velocity brick on either side.

Prefetched tiles enter the same LRU cache but do not create visible
GameObjects. A new focus generation cancels obsolete prefetch work.

## Scene setup

1. Open the scene used for the 3D explorer.
2. Create an empty GameObject named `FullResBrickWindow`.
3. Add the `FullResBrickWindow` component.
4. Set the main fields to:

```text
Relative Data Folder        fullres_tiles
Velocity Origin             320
Latitude Origin             128
Longitude Origin            256
Spatial Tiles X             2
Spatial Tiles Y             2
GPU Texture Cache Capacity  32
Spatial Prefetch Radius     1
Velocity Prefetch Radius    1
```

5. Ensure the scene has a camera pointed at the window and an EventSystem for
   UI interaction.

`Co21DataFocus` is created automatically on the brick-window object if it is
not already present.

## Controls

```text
Left / Right arrow    velocity brick navigation
W / Up                latitude increase
S / Down              latitude decrease
A                    longitude decrease
D                    longitude increase
```

Clicking a channel or moment map converts overview coordinates to source
coordinates and updates `Co21DataFocus`. If a `FullResBrickWindow` exists in
the scene, it requests the corresponding full-resolution window.

## What “full-resolution” means here

The project does not upload the complete 1.2 GB-scale data set to the GPU at
once. Instead, the complete tile index is available and any region of the
`1000 × 391 × 769` source cube can be explored through streaming. GPU memory is
bounded by the active window, prefetch area and LRU cache.

This avoids creating hundreds of simultaneous GameObjects, materials and 3D
textures, which would make the Unity scene and GPU memory usage impractical.

## Current status

Implemented:

- FITS-derived overview and full-resolution tile products.
- Full-resolution brick metadata and binary validation.
- 2×2 visible brick window.
- Velocity, longitude and latitude navigation.
- Asynchronous brick file reads.
- GPU `Texture3D` LRU cache.
- Spatial and velocity prefetching.
- Shared `Co21DataFocus` coordinate state.
- Overview-map to full-resolution coordinate linking.
- Visible data-focus and streaming-status panel with load progress.
- Explicit V/X/Y focus viewports or camera-following spatial windows.
- Per-frame upload count/byte budgets and automatic lossless R16 texture evaluation.
- Edit-mode validation for brick edges, V/X/Y↔V/Y/X mapping and LRU eviction.

Known limitations:

- `R16` is selected automatically when supported, reducing nominal brick
  storage from about 2 MB to 1 MB; `RHalf` is intentionally not used as a
  bit reinterpretation because the source values are uint16, not IEEE half.
- Texture decoding and GPU upload still occur on the main thread.
- Camera following is enabled on `FullResBrickWindow` by default and can be
  replaced by `SetFocusViewport(v, x, y, tilesX, tilesY)`.
- The cache budget is exposed as a bounded texture count plus per-frame upload
  budgets; exact driver allocation remains GPU/platform dependent.

## Next development steps

- Run Unity Test Runner for brick boundaries, coordinate mapping and LRU eviction.
- Verify R16 rendering and edge-brick alignment on target GPUs.
- Add request debouncing for camera-following viewport updates.
- Refine velocity-range prefetching and migrate the status panel to Canvas UI.

## Unity version

```text
Unity 6000.5.2f1
```

The project uses URP, Input System, uGUI and UI Toolkit-compatible Unity
packages.
