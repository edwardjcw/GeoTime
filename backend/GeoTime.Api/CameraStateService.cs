using GeoTime.Core.Models;

namespace GeoTime.Api;

/// <summary>
/// In-memory store for the current Unreal Engine camera state.
/// Registered as a singleton so the state persists across requests.
/// </summary>
public sealed class CameraStateService
{
    public CameraState State { get; set; } = new CameraState
    {
        Mode = CameraMode.Orbit,
        Lat = 0.0,
        Lon = 0.0,
        AltitudeKm = 500.0,
        Heading = 0.0,
        Pitch = -45.0,
    };
}
