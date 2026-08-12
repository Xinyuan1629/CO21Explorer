# Full-resolution brick window

`FullResBrickWindow` loads a deterministic spatial window of adjacent bricks
from the full-resolution tile set. The default is a 2×2 window at velocity
origin 320, covering the main CO velocity component.

Each brick is placed using its tile-grid position. Edge tiles use their actual
shape from `metadata.json`, so the final spatial extent is not assumed to be a
full 128 pixels.

This is a composition test. Press the left/right arrow keys to switch the
32-channel velocity brick. Press `W/S` or `↑/↓` to move in latitude (`Y`), and
`A/D` to move in longitude (`X`); each key press moves the complete window by
one tile-grid step. Origins are clamped to the source shape in
`metadata.json`. Public origin APIs use `X=longitude, Y=latitude, V=velocity`,
while the tile metadata remains in `V×Y×X` order. The old window is destroyed
before the new one is loaded. The next stage is to replace the fixed window
with a camera/viewport-driven request. The window now keeps a GPU `Texture3D`
LRU cache (32 bricks by default, about 64 MB at the current RGBA32 format), so
overlapping bricks are reused instead of being uploaded again. Brick file reads
now run on a background task; byte decoding and `Texture3D` upload remain on the
Unity main thread. One spatial tile ring is prefetched by default; prefetched
bricks enter the same LRU cache but do not create visible GameObjects. Set
`spatialPrefetchRadiusTiles` to `0` to disable spatial prefetching, or
`velocityPrefetchRadiusTiles` to `0` to disable velocity prefetching. The full
dataset remains available through the tile index; only the active window and
nearby tiles occupy GPU memory. `Co21DataFocus` now provides the shared PPV
focus in public `V/X/Y` order, so other views can request a new streamed window
without directly managing brick coordinates.
