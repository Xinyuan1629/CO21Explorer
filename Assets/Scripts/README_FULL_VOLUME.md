# Full overview Texture3D validation

The current 3D page loads the complete processed overview product:

```text
X = 193 longitude samples
Y = 98 latitude samples
Z = 1000 velocity channels
```

This is the full `unity_products` overview, not the original 1.2 GB FITS file.
The display product is a normalized uint16 file. The current compatibility path
expands it to an RGBA32 `Texture3D` for Metal/URP sampling.

Approximate memory during this validation path:

```text
uint16 source file       ~36 MB
RGBA32 CPU upload array  ~72 MB
GPU Texture3D            ~72 MB
```

The Console prints a size and byte-count validation when the complete volume is
loaded. This path is intended for the current 193×98×1000 overview. The original
FITS should remain outside Unity; larger products need brick/tile streaming.
