namespace GeoTime.Core.Models;

/// <summary>Metadata for Unreal Engine terrain import and camera setup.</summary>
public sealed class TerrainMeta
{
    /// <summary>Number of grid cells along each axis (e.g. 512).</summary>
    public int GridSize { get; set; }

    /// <summary>Total number of grid cells.</summary>
    public int CellCount { get; set; }

    /// <summary>Width and height of each terrain cell in Unreal units (1 UU = 1 cm).</summary>
    public float CellSizeCm { get; set; }

    /// <summary>Maximum terrain height above sea level in Unreal units (cm).</summary>
    public float MaxHeightCm { get; set; }

    /// <summary>Minimum terrain height (ocean floor) in Unreal units (cm).</summary>
    public float MinHeightCm { get; set; }

    /// <summary>
    /// Camera altitude threshold in km below which the viewer switches from
    /// orbit (globe) mode to first-person (landscape) mode.
    /// </summary>
    public double FirstPersonThresholdKm { get; set; }
}

/// <summary>Camera state shared between the backend and the Unreal Engine viewer.</summary>
public sealed class CameraState
{
    /// <summary>"orbit" (globe view) or "firstperson" (landscape view).</summary>
    public string Mode { get; set; } = CameraMode.Orbit;

    /// <summary>Latitude of the camera focus point, in degrees (-90 to 90).</summary>
    public double Lat { get; set; }

    /// <summary>Longitude of the camera focus point, in degrees (-180 to 180).</summary>
    public double Lon { get; set; }

    /// <summary>Camera altitude above the terrain surface, in km.</summary>
    public double AltitudeKm { get; set; } = 500.0;

    /// <summary>Camera heading (yaw) in degrees, measured clockwise from north.</summary>
    public double Heading { get; set; }

    /// <summary>Camera pitch in degrees. Negative values look downward.</summary>
    public double Pitch { get; set; } = -45.0;
}

/// <summary>Camera mode string constants.</summary>
public static class CameraMode
{
    public const string Orbit = "orbit";
    public const string FirstPerson = "firstperson";
    /// <summary>Altitude (km) below which the camera switches to first-person view.</summary>
    public const double FirstPersonThresholdKm = 0.1; // 100 metres
}
