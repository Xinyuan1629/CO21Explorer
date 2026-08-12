using UnityEngine;

/// <summary>
/// The subset of metadata required by the Unity viewer.
/// Field names intentionally match visualization/unity_products/metadata.json.
/// </summary>
[System.Serializable]
public class Co21SampleMetadata
{
    public int[] cube_shape;
    public int[] overview_shape;
    public int[] integrated_shape;
    public int[] integrated_map_shape;
    public int[] source_spatial_shape;
    public float[] velocity_km_s;
    public string[] axis_order_cube;
    public string[] axis_order_overview;
    public string brightness_unit;
    public string velocity_unit;
    public string integrated_unit;
}
