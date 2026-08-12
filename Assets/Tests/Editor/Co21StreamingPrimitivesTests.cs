using NUnit.Framework;
using UnityEngine;

public class Co21StreamingPrimitivesTests
{
    private readonly int[] source = { 1000, 391, 769 };
    private readonly int[] tile = { 32, 128, 128 };

    [Test]
    public void BrickOriginsSnapAndClampAtEdges()
    {
        Assert.AreEqual(0, Co21BrickCoordinates.ClampOrigin(-4, 0, source, tile));
        Assert.AreEqual(960, Co21BrickCoordinates.ClampOrigin(999, 0, source, tile));
        Assert.AreEqual(256, Co21BrickCoordinates.ClampOrigin(300, 1, source, tile));
        Assert.AreEqual(768, Co21BrickCoordinates.ClampOrigin(999, 2, source, tile));
    }

    [Test]
    public void PublicCoordinatesMapToMetadataVYXSafely()
    {
        Vector3Int mapped = Co21BrickCoordinates.PublicToMetadata(320, 256, 128);
        Assert.AreEqual(new Vector3Int(320, 128, 256), mapped);
        Assert.IsTrue(Co21BrickCoordinates.Contains(mapped, new[] { 32, 128, 128 }, new Vector3Int(351, 255, 383)));
        Assert.IsFalse(Co21BrickCoordinates.Contains(mapped, new[] { 32, 128, 128 }, new Vector3Int(352, 255, 383)));
    }

    [Test]
    public void CacheEvictsLeastRecentlyUsedAndKeepsTouch()
    {
        Co21Lru<string, int> cache = new Co21Lru<string, int>(2);
        cache.Put("a", 1); cache.Put("b", 2);
        int value;
        Assert.IsTrue(cache.TryGet("a", out value));
        Assert.AreEqual(1, value);
        Assert.AreEqual("b", cache.Put("c", 3));
        Assert.IsFalse(cache.TryGet("b", out value));
        Assert.IsTrue(cache.TryGet("a", out value));
        Assert.IsTrue(cache.TryGet("c", out value));
    }
}
