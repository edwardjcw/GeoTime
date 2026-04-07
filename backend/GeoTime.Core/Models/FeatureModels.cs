namespace GeoTime.Core.Models;

/// <summary>Types of geographic features detected on the planet.</summary>
public enum FeatureType
{
    TectonicPlate,
    Continent,
    Ocean,
    Sea,
    Island,
    IslandChain,
    MountainRange,
    MountainPeak,
    Rift,
    SubductionZone,
    HotspotChain,
    ImpactBasin,
    River,
    RiverDelta,
    Lake,
    InlandSea,
    Desert,
    Rainforest,
    Savanna,
    Tundra,
    PolarIceCap,
    OceanCurrentSystem,
    JetStream,
    MonsoonBelt,
    HurricaneCorridor,
    ITCZ,
}

/// <summary>Lifecycle status of a geographic feature.</summary>
public enum FeatureStatus
{
    Nascent,
    Active,
    Waning,
    Extinct,
    Submerged,
    Exposed,
}

/// <summary>A point-in-time snapshot of a feature's state.</summary>
public record FeatureSnapshot(
    long   SimTickCreated,
    long   SimTickExtinct,
    string Name,
    float  CenterLat,
    float  CenterLon,
    float  AreaKm2,
    FeatureStatus Status,
    string? ParentFeatureId,
    string? MergedIntoId,
    string? SplitFromId
);

/// <summary>A geographic feature with full temporal history.</summary>
public class DetectedFeature
{
    public string Id { get; init; } = "";
    public FeatureType Type { get; init; }
    public List<FeatureSnapshot> History { get; init; } = [];
    public FeatureSnapshot Current => History[^1];
    public List<string> AssociatedPlateIds { get; init; } = [];
    public List<int> CellIndices { get; init; } = [];
    /// <summary>
    /// Quantitative metrics for the feature. Keys include "max_elevation_m",
    /// "mean_precip_mm", "river_length_km", "discharge_m3s", etc.
    /// </summary>
    public Dictionary<string, float> Metrics { get; init; } = [];
    /// <summary>
    /// Accumulated list of former names this feature (or its ancestors) was known by.
    /// Populated during split, merge, submergence, exposure, and name-evolution events.
    /// Most recent former name is last.
    /// </summary>
    public List<string> FormerNames { get; init; } = [];
}

/// <summary>Registry of all detected features on the planet.</summary>
public class FeatureRegistry
{
    public Dictionary<string, DetectedFeature> Features { get; init; } = [];
    public long LastUpdatedTick { get; set; }
}
