# 3D volume page

The 3D page is intentionally separate from the existing 2D viewer.

## Scene setup

1. Create a new scene named `Explorer3D`.
2. Create an empty GameObject named `CO21VolumePage`.
3. Add the `Co21VolumePage` component.
4. Press Play.

The component creates the volume cube, Canvas controls, and orbit camera at runtime.

Required data:

```text
Assets/StreamingAssets/co21_volume/
├── metadata.json
└── volume_xyz_u16.bin
```

Controls:

- Left-drag: orbit around the volume;
- Mouse wheel: zoom;
- Density threshold: remove low-significance emission;
- Opacity: change volume opacity.

The volume texture is a display-only uint16 product. Scientific measurements
must continue to use the float32 FITS/overview products.
