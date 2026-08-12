using System;
using System.Collections.Generic;
using UnityEngine;

/// Pure coordinate helpers shared by runtime code and edit-mode tests.
public static class Co21BrickCoordinates
{
    public static int Snap(int value, int step) => Mathf.Max(0, value / step * step);

    public static int ClampOrigin(int value, int axis, int[] sourceShape, int[] tileShape)
    {
        int step = tileShape[axis];
        int max = Mathf.Max(0, (sourceShape[axis] - 1) / step * step);
        return Mathf.Clamp(Snap(value, step), 0, max);
    }

    public static Vector3Int PublicToMetadata(int velocity, int longitude, int latitude)
        => new Vector3Int(velocity, latitude, longitude);

    public static bool Contains(Vector3Int metadataOrigin, int[] shape, Vector3Int voxel)
        => voxel.x >= metadataOrigin.x && voxel.y >= metadataOrigin.y && voxel.z >= metadataOrigin.z &&
           voxel.x < metadataOrigin.x + shape[0] && voxel.y < metadataOrigin.y + shape[1] && voxel.z < metadataOrigin.z + shape[2];
}

public sealed class Co21Lru<TKey, TValue>
{
    private readonly int capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> values = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
    private readonly LinkedList<KeyValuePair<TKey, TValue>> order = new LinkedList<KeyValuePair<TKey, TValue>>();
    public Co21Lru(int capacity) { this.capacity = Mathf.Max(1, capacity); }
    public int Count => values.Count;
    public bool TryGet(TKey key, out TValue value)
    {
        LinkedListNode<KeyValuePair<TKey, TValue>> node;
        if (!values.TryGetValue(key, out node)) { value = default(TValue); return false; }
        order.Remove(node); order.AddFirst(node); value = node.Value.Value; return true;
    }
    public TKey Put(TKey key, TValue value)
    {
        LinkedListNode<KeyValuePair<TKey, TValue>> old;
        if (values.TryGetValue(key, out old)) order.Remove(old);
        LinkedListNode<KeyValuePair<TKey, TValue>> node = order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        values[key] = node;
        if (values.Count <= capacity) return default(TKey);
        LinkedListNode<KeyValuePair<TKey, TValue>> last = order.Last;
        TKey evicted = last.Value.Key; order.RemoveLast(); values.Remove(evicted); return evicted;
    }
}

public enum Co21TexturePrecision { Auto, R16, Half, Rgba32 }

