using GeoTime.Core.Engines;

namespace GeoTime.Core.Models;

/// <summary>
/// Fully assembled geological context for a single grid cell.  Built by
/// <see cref="GeoTime.Core.Services.GeologicalContextAssembler"/> from raw
/// simulation maps and feature registry data; consumed by the template engine
/// or an LLM description provider in Phase D3+.
/// </summary>
public sealed class GeologicalContext
{
    // === Location ===

    /// <summary>Latitude of the inspected cell in degrees (−90 to +90).</summary>
    public float Lat { get; init; }

    /// <summary>Longitude of the inspected cell in degrees (−180 to +180).</summary>
    public float Lon { get; init; }

    /// <summary>Current simulation tick when the context was assembled.</summary>
    public long CurrentTick { get; init; }

    /// <summary>Human-readable sim age, e.g. "~2.4 billion years".</summary>
    public string SimAgeDescription { get; init; } = "";

    // === Cell-level data ===

    /// <summary>Raw CellInspection data from the simulation orchestrator.</summary>
    public CellInspection Cell { get; init; } = new();

    /// <summary>Full stratigraphic column for this cell (oldest layer first).</summary>
    public StratigraphicColumn Column { get; init; } = new();

    // === Feature hierarchy ===

    /// <summary>
    /// All features whose cell list contains the inspected cell, sorted by
    /// spatial scale: TectonicPlate → Continent/Ocean → Mountain/Rift/Sea →
    /// River/Lake → atmospheric features.
    /// </summary>
    public List<DetectedFeature> ContainingFeatures { get; init; } = [];

    /// <summary>Most specific land feature containing this cell, or null.</summary>
    public DetectedFeature? PrimaryLandFeature { get; init; }

    /// <summary>Most specific water feature containing this cell, or null.</summary>
    public DetectedFeature? PrimaryWaterFeature { get; init; }

    // === Tectonic context ===

    /// <summary>The tectonic plate this cell belongs to.</summary>
    public PlateInfo CurrentPlate { get; init; } = new();

    /// <summary>Distance in km to the nearest plate margin.</summary>
    public float DistanceToPlateMarginKm { get; init; }

    /// <summary>Classification of the nearest plate margin.</summary>
    public BoundaryType NearestMarginType { get; init; }

    /// <summary>
    /// Adjacent plate that is colliding with the current plate within 500 km, or null.
    /// Set when both plates are continental.
    /// </summary>
    public PlateInfo? CollidingPlate { get; init; }

    /// <summary>
    /// Adjacent oceanic plate that is subducting beneath the current plate within 500 km, or null.
    /// </summary>
    public PlateInfo? SubductingPlate { get; init; }

    /// <summary>Approximate convergence rate in cm/year, or 0 if no converging neighbour.</summary>
    public float ConvergenceRateCmPerYear { get; init; }

    // === Hydrological context ===

    /// <summary>Name of the river flowing through this cell, or null.</summary>
    public string? RiverName { get; init; }

    /// <summary>Total length of the river in km, or null if no river.</summary>
    public float? RiverLengthKm { get; init; }

    /// <summary>Approximate catchment area in km², or null if no river.</summary>
    public float? CatchmentAreaKm2 { get; init; }

    /// <summary>Name of the ocean/sea the river drains into, or null.</summary>
    public string? RiverOutletOcean { get; init; }

    /// <summary>Name of the watershed basin, or null.</summary>
    public string? WatershedName { get; init; }

    /// <summary>True when this cell's drainage basin does not reach the open ocean.</summary>
    public bool IsInEndorheicBasin { get; init; }

    /// <summary>Mean downstream elevation gradient (m/km) along the river, or null.</summary>
    public float? DrainageGradient { get; init; }

    // === Mountain / orographic context ===

    /// <summary>True when the inspected cell is inside a named mountain range.</summary>
    public bool IsInMountainRange { get; init; }

    /// <summary>Name of the containing mountain range, or null.</summary>
    public string? RangeName { get; init; }

    /// <summary>Maximum elevation (m) of the range, or null.</summary>
    public float? RangeMaxElevationM { get; init; }

    /// <summary>True when the cell is on the windward (upwind) side of the range.</summary>
    public bool IsOnWindwardSide { get; init; }

    /// <summary>True when a rain-shadow effect has been detected for this range.</summary>
    public bool HasRainShadow { get; init; }

    /// <summary>Origin type of the mountain range: "fold-belt", "volcanic arc", or "hotspot shield".</summary>
    public string? MountainOriginType { get; init; }

    // === Climate context ===

    /// <summary>Biome classification string inferred from temperature + precipitation + elevation.</summary>
    public string BiomeType { get; init; } = "";

    /// <summary>Mean surface temperature in °C.</summary>
    public float MeanTempC { get; init; }

    /// <summary>Mean annual precipitation in mm.</summary>
    public float MeanPrecipMm { get; init; }

    /// <summary>True when this cell lies inside a monsoon belt feature.</summary>
    public bool IsInMonsoonZone { get; init; }

    /// <summary>True when this cell lies inside a hurricane corridor feature.</summary>
    public bool IsInHurricaneCorridor { get; init; }

    /// <summary>True when this cell lies inside a jet-stream feature.</summary>
    public bool IsInJetStreamZone { get; init; }

    /// <summary>Name of the nearest ocean-current system feature, or null.</summary>
    public string? NearestOceanCurrentName { get; init; }

    /// <summary>True when the nearest ocean current is warm (mean SST > 20 °C).</summary>
    public bool NearestCurrentIsWarm { get; init; }

    // === Extraordinary events in stratigraphic record ===

    /// <summary>
    /// Stratigraphic layers produced by extraordinary events
    /// (i.e. <see cref="StratigraphicLayer.EventType"/> ≠ Normal).
    /// </summary>
    public List<StratigraphicLayer> ExtraordinaryLayers { get; init; } = [];

    // === Full feature history ===

    /// <summary>
    /// Complete temporal history of the primary land feature (or water feature if no land
    /// feature is present), ordered oldest-first.
    /// </summary>
    public List<FeatureSnapshot> PrimaryFeatureHistory { get; init; } = [];

    // === Nearby notable features ===

    /// <summary>
    /// Up to six named features within 3 000 km, ordered by distance ascending.
    /// Does not include features that contain the inspected cell.
    /// </summary>
    public List<(DetectedFeature Feature, float DistanceKm)> NearbyFeatures { get; init; } = [];
}

// ── API transport models ─────────────────────────────────────────────────────

/// <summary>Request body for POST /api/describe.</summary>
public sealed class DescriptionRequest
{
    public int CellIndex { get; set; }
}

/// <summary>One row in the stratigraphic summary table.</summary>
public sealed class StratigraphicSummaryRow
{
    public string Age     { get; set; } = "";
    public string Thickness { get; set; } = "";
    public string RockType  { get; set; } = "";
    public string EventNote { get; set; } = "";
}

/// <summary>One entry in the history timeline list.</summary>
public sealed class HistoryTimelineEntry
{
    public long   SimTick { get; set; }
    public string Event   { get; set; } = "";
    public string Name    { get; set; } = "";
}

/// <summary>One stat row (label + value) in the description panel.</summary>
public sealed class DescriptionStat
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Full response from POST /api/describe.</summary>
public sealed class DescriptionResponse
{
    public string   Title    { get; set; } = "";
    public string   Subtitle { get; set; } = "";
    public string[] Paragraphs { get; set; } = [];

    public List<DescriptionStat>         Stats                { get; set; } = [];
    public List<StratigraphicSummaryRow> StratigraphicSummary { get; set; } = [];
    public List<HistoryTimelineEntry>    HistoryTimeline      { get; set; } = [];

    /// <summary>Which LLM provider generated the prose (e.g. "Template", "Gemini").</summary>
    public string ProviderUsed { get; set; } = "";
}
