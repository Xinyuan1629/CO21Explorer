using UnityEngine;

/// <summary>
/// The subset of metadata required by the Unity viewer.
/// Field names intentionally match visualization/sample_data/metadata.json.
/// </summary>
[System.Serializable]
public class Co21SampleMetadata
{
    public int[] cube_shape;
    public int[] integrated_shape;
    public float[] velocity_km_s;
    public string[] axis_order_cube;
    public string brightness_unit;
    public string integrated_unit;
}
