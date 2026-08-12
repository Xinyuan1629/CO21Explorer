using System;
using UnityEngine;

/// <summary>
/// Shared PPV data focus for linked views and full-resolution streaming.
/// Public order is V/X/Y; tile metadata remains V/Y/X internally.
/// </summary>
public class Co21DataFocus : MonoBehaviour
{
    public int velocity;
    public int longitude;
    public int latitude;
    public int velocityMin;
    public int velocityMax;

    public event Action Changed;

    public void Set(int requestedVelocity, int requestedLongitude, int requestedLatitude)
    {
        if (velocity == requestedVelocity && longitude == requestedLongitude &&
            latitude == requestedLatitude) return;
        velocity = requestedVelocity;
        velocityMin = requestedVelocity;
        velocityMax = requestedVelocity;
        longitude = requestedLongitude;
        latitude = requestedLatitude;
        Changed?.Invoke();
    }

    public void SetVelocityRange(int min, int max)
    {
        velocityMin = Mathf.Min(min, max);
        velocityMax = Mathf.Max(min, max);
        Changed?.Invoke();
    }

    public void SetWithoutNotify(int requestedVelocity, int requestedLongitude,
                                  int requestedLatitude)
    {
        velocity = requestedVelocity;
        longitude = requestedLongitude;
        latitude = requestedLatitude;
    }
}
