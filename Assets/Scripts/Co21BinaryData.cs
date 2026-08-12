using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Minimal binary reader for the arrays exported by export_co21_sample.py.
/// Scientific calculations remain in Python; this class only maps bytes to
/// Unity-friendly float arrays.
/// </summary>
public static class Co21BinaryData
{
    public static float[] ReadLittleEndianFloat32(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length % sizeof(float) != 0)
            throw new InvalidDataException("Binary file length is not divisible by 4 bytes.");

        float[] values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);

        // Unity commonly runs on little-endian platforms. Keep this explicit
        // so a future big-endian target fails loudly rather than silently
        // displaying corrupt scientific data.
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("Big-endian platforms need byte swapping.");

        return values;
    }

    public static int FlattenCubeIndex(int velocity, int latitude, int longitude,
                                       int latitudeSize, int longitudeSize)
    {
        // Matches NumPy C-order for [velocity, latitude, longitude].
        return (velocity * latitudeSize + latitude) * longitudeSize + longitude;
    }
}
