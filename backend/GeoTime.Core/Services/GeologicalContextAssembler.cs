using GeoTime.Core.Engines;
using GeoTime.Core.Models;

namespace GeoTime.Core.Services;

/// <summary>
/// Assembles a fully-populated <see cref="GeologicalContext"/> for an arbitrary grid cell
/// by pulling together data from the simulation state, feature registry, plate-boundary
/// classifier, and climate maps.
///
/// Phase D2 of the Feature Description Engine.  The returned context is ready for
/// consumption by either the template-based description engine (Phase D4) or an LLM
/// provider (Phase D3+).
/// </summary>
public sealed class GeologicalContextAssembler
{
    private readonly SimulationOrchestrator _orchestrator;

    /// <param name="orchestrator">The live orchestrator that owns all simulation state.</param>
    public GeologicalContextAssembler(SimulationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Assembles geological context for <paramref name="cellIndex"/>.
    /// Returns <c>null</c> when the index is out of range or the planet has not been generated yet.
    /// </summary>
    public Task<GeologicalContext?> AssembleAsync(int cellIndex)
        => Task.FromResult(Assemble(cellIndex));

    // ─────────────────────────────────────────────────────────────────────────
    // Core assembly
    // ─────────────────────────────────────────────────────────────────────────

    private GeologicalContext? Assemble(int cellIndex)
    {
        var state = _orchestrator.State;
        var gs    = state.GridSize;

        if (cellIndex < 0 || cellIndex >= state.CellCount) return null;

        // Step 1 — Fetch CellInspection (includes column, featureIds, margin data).
        var cell = _orchestrator.InspectCell(cellIndex);
        if (cell == null) return null;

        var column = cell.Column ?? new StratigraphicColumn();

        // Compute lat/lon for this cell.
        int cellRow = cellIndex / gs;
        int cellCol = cellIndex % gs;
        float lat = (float)(90.0 - (cellRow + 0.5) * 180.0 / gs);
        float lon = (float)((cellCol + 0.5) * 360.0 / gs - 180.0);

        long currentTick = (long)_orchestrator.Clock.T;
        string simAgeDesc = FormatSimAge(_orchestrator.Clock.T);

        // Step 2 — Find all features containing cellIndex; sort by scale.
        var registry = _orchestrator.GetFeatureRegistry();
        var containingFeatures = registry.Features.Values
            .Where(f => f.CellIndices.Contains(cellIndex))
            .OrderBy(f => FeatureScalePriority(f.Type))
            .ToList();

        var primaryLand  = containingFeatures.FirstOrDefault(f => IsLandFeature(f.Type));
        var primaryWater = containingFeatures.FirstOrDefault(f => IsWaterFeature(f.Type));

        // Step 3 — Tectonic context.
        var plates      = _orchestrator.GetPlates() ?? Array.Empty<PlateInfo>();
        var currentPlate = plates.FirstOrDefault(p => p.Id == cell.PlateId) ?? new PlateInfo { Id = cell.PlateId };

        var (collidingPlate, subductingPlate, convergenceRate) =
            FindConvergingPlates(cellIndex, lat, lon, cell.PlateId, currentPlate, plates, state, gs);

        // Step 4 — Hydrological context.
        var (riverName, riverLengthKm, catchmentAreaKm2, riverOutletOcean,
             watershedName, isEndorheic, drainageGradient) =
            AssembleHydroContext(cellIndex, cell, containingFeatures, registry);

        // Step 5 — Orographic context.
        var (isInMountain, rangeName, rangeMaxElevationM, isWindward,
             hasRainShadow, mountainOriginType) =
            AssembleOrographicContext(cellIndex, containingFeatures, state);

        // Step 6 — Climate zone membership.
        bool isInMonsoon    = containingFeatures.Any(f => f.Type == FeatureType.MonsoonBelt);
        bool isInHurricane  = containingFeatures.Any(f => f.Type == FeatureType.HurricaneCorridor);
        bool isInJetStream  = containingFeatures.Any(f => f.Type == FeatureType.JetStream);

        var (nearestCurrentName, nearestCurrentIsWarm) =
            FindNearestOceanCurrent(lat, lon, registry, state);

        string biomeType = InferBiome(
            state.TemperatureMap[cellIndex],
            state.PrecipitationMap[cellIndex],
            state.HeightMap[cellIndex]);

        // Step 7 — Extraordinary layers.
        var extraordinaryLayers = column.ExtraordinaryLayers.ToList();

        // Step 8 — Primary feature history.
        var primaryFeatureHistory =
            (primaryLand?.History ?? primaryWater?.History)?.ToList() ?? [];

        // Step 9 — Nearby features (up to 6, within 3 000 km, not containing this cell).
        var nearbyFeatures = FindNearbyFeatures(lat, lon, cellIndex, registry);

        // Step 10 — Assemble and return.
        return new GeologicalContext
        {
            Lat                  = lat,
            Lon                  = lon,
            CurrentTick          = currentTick,
            SimAgeDescription    = simAgeDesc,
            Cell                 = cell,
            Column               = column,
            ContainingFeatures   = containingFeatures,
            PrimaryLandFeature   = primaryLand,
            PrimaryWaterFeature  = primaryWater,
            CurrentPlate         = currentPlate,
            DistanceToPlateMarginKm  = cell.DistanceToPlateMarginKm,
            NearestMarginType        = cell.NearestMarginType,
            CollidingPlate           = collidingPlate,
            SubductingPlate          = subductingPlate,
            ConvergenceRateCmPerYear = convergenceRate,
            RiverName            = riverName,
            RiverLengthKm        = riverLengthKm,
            CatchmentAreaKm2     = catchmentAreaKm2,
            RiverOutletOcean     = riverOutletOcean,
            WatershedName        = watershedName,
            IsInEndorheicBasin   = isEndorheic,
            DrainageGradient     = drainageGradient,
            IsInMountainRange    = isInMountain,
            RangeName            = rangeName,
            RangeMaxElevationM   = rangeMaxElevationM,
            IsOnWindwardSide     = isWindward,
            HasRainShadow        = hasRainShadow,
            MountainOriginType   = mountainOriginType,
            BiomeType            = biomeType,
            MeanTempC            = state.TemperatureMap[cellIndex],
            MeanPrecipMm         = state.PrecipitationMap[cellIndex],
            IsInMonsoonZone      = isInMonsoon,
            IsInHurricaneCorridor = isInHurricane,
            IsInJetStreamZone    = isInJetStream,
            NearestOceanCurrentName = nearestCurrentName,
            NearestCurrentIsWarm    = nearestCurrentIsWarm,
            ExtraordinaryLayers     = extraordinaryLayers,
            PrimaryFeatureHistory   = primaryFeatureHistory,
            NearbyFeatures          = nearbyFeatures,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 3: Tectonic context helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (PlateInfo? colliding, PlateInfo? subducting, float convergenceRateCmPerYear)
        FindConvergingPlates(
            int cellIndex, float lat, float lon, int cellPlateId,
            PlateInfo currentPlate,
            IReadOnlyList<PlateInfo> plates,
            SimulationState state, int gs)
    {
        if (plates.Count == 0) return (null, null, 0f);

        var boundaries = BoundaryClassifier.Classify(
            state.PlateMap, plates.ToList(), gs);

        PlateInfo? collidingPlate  = null;
        PlateInfo? subductingPlate = null;
        float      convergenceRate = 0f;

        foreach (var b in boundaries)
        {
            if (b.Type != BoundaryType.CONVERGENT) continue;

            int bRow = b.CellIndex / gs;
            int bCol = b.CellIndex % gs;
            double bLat = 90.0 - (bRow + 0.5) * 180.0 / gs;
            double bLon = (bCol + 0.5) * 360.0 / gs - 180.0;
            float dKm = (float)(CrossSectionEngine.CentralAngle(lat, lon, bLat, bLon)
                                * CrossSectionEngine.EARTH_RADIUS_KM);
            if (dKm > 500f) continue;

            // Identify the plate on the other side of this boundary.
            int otherPlateId = b.Plate1 == cellPlateId ? b.Plate2 : b.Plate1;
            if (otherPlateId < 0 || otherPlateId >= plates.Count) continue;
            var otherPlate = plates[otherPlateId];

            // An oceanic plate subducting beneath a continental plate.
            if (otherPlate.IsOceanic && !currentPlate.IsOceanic && subductingPlate == null)
            {
                subductingPlate = otherPlate;
            }
            else if (collidingPlate == null)
            {
                collidingPlate = otherPlate;
            }

            // Convergence rate: convert angular-velocity units (rad/yr) to cm/yr.
            if (convergenceRate == 0f)
                convergenceRate = (float)(b.RelativeSpeed * CrossSectionEngine.EARTH_RADIUS_KM * 1e5);

            if (subductingPlate != null && collidingPlate != null) break;
        }

        return (collidingPlate, subductingPlate, convergenceRate);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 4: Hydrological context helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (string? riverName, float? riverLengthKm, float? catchmentAreaKm2,
                    string? riverOutletOcean, string? watershedName,
                    bool isEndorheic, float? drainageGradient)
        AssembleHydroContext(
            int cellIndex, CellInspection cell,
            List<DetectedFeature> containingFeatures,
            FeatureRegistry registry)
    {
        string? riverName      = cell.RiverName;
        float?  riverLengthKm  = null;
        float?  catchmentAreaKm2 = null;
        string? riverOutletOcean = null;
        string? watershedName  = null;
        bool    isEndorheic    = false;
        float?  drainageGradient = null;

        var riverFeature = containingFeatures.FirstOrDefault(f => f.Type == FeatureType.River);
        if (riverFeature != null)
        {
            riverName ??= riverFeature.Current.Name;
            if (riverFeature.Metrics.TryGetValue("river_length_km", out float len))
                riverLengthKm = len;
            if (riverFeature.Metrics.TryGetValue("gradient", out float grad))
                drainageGradient = grad;

            // Use area from the river feature itself as a catchment proxy.
            catchmentAreaKm2 = riverFeature.Current.AreaKm2 > 0f
                ? riverFeature.Current.AreaKm2
                : null;
        }

        // Watershed info from the cell's recorded watershed feature.
        var watershedId = cell.WatershedFeatureId;
        if (watershedId != null && registry.Features.TryGetValue(watershedId, out var wsFeature))
        {
            watershedName = wsFeature.Current.Name;
            isEndorheic   = wsFeature.Type is FeatureType.Lake or FeatureType.InlandSea;
            if (wsFeature.Type is FeatureType.Ocean or FeatureType.Sea)
                riverOutletOcean = wsFeature.Current.Name;
        }
        else if (riverFeature != null)
        {
            // Fall back: check features containing the river cell list for an outlet ocean.
            var outletOcean = registry.Features.Values
                .FirstOrDefault(f => f.Type is FeatureType.Ocean or FeatureType.Sea
                                     && riverFeature.CellIndices.Any(ci => f.CellIndices.Contains(ci)));
            if (outletOcean != null)
            {
                riverOutletOcean = outletOcean.Current.Name;
                watershedName    ??= outletOcean.Current.Name;
            }

            var endorheicSink = registry.Features.Values
                .FirstOrDefault(f => f.Type is FeatureType.Lake or FeatureType.InlandSea
                                     && riverFeature.CellIndices.Any(ci => f.CellIndices.Contains(ci)));
            if (endorheicSink != null)
            {
                isEndorheic   = true;
                watershedName ??= endorheicSink.Current.Name;
            }
        }

        return (riverName, riverLengthKm, catchmentAreaKm2, riverOutletOcean,
                watershedName, isEndorheic, drainageGradient);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 5: Orographic context helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (bool isInMountain, string? rangeName, float? rangeMaxElevationM,
                    bool isWindward, bool hasRainShadow, string? mountainOriginType)
        AssembleOrographicContext(
            int cellIndex,
            List<DetectedFeature> containingFeatures,
            SimulationState state)
    {
        var mountainFeature = containingFeatures.FirstOrDefault(f => f.Type == FeatureType.MountainRange);
        if (mountainFeature == null)
            return (false, null, null, false, false, null);

        mountainFeature.Metrics.TryGetValue("max_elevation_m", out float maxElev);
        mountainFeature.Metrics.TryGetValue("rain_shadow_intensity", out float rainShadow);
        bool hasRainShadow = rainShadow > 0.1f;

        // Mountain origin type: check for nearby tectonic-context features.
        string? originType;
        bool hasHotspot    = containingFeatures.Any(f => f.Type == FeatureType.HotspotChain);
        bool hasSubduction = containingFeatures.Any(f => f.Type == FeatureType.SubductionZone);

        if (hasHotspot)         originType = "hotspot shield";
        else if (hasSubduction) originType = "volcanic arc";
        else                    originType = "fold-belt";

        // Windward classification: cells with above-average range precipitation are windward.
        bool isWindward = false;
        if (mountainFeature.CellIndices.Count > 0)
        {
            float rangeMeanPrecip = mountainFeature.CellIndices
                .Average(ci => state.PrecipitationMap[ci]);
            isWindward = state.PrecipitationMap[cellIndex] > rangeMeanPrecip;
        }

        return (true,
                mountainFeature.Current.Name,
                maxElev > 0f ? maxElev : null,
                isWindward,
                hasRainShadow,
                originType);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 6: Ocean current helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (string? name, bool isWarm) FindNearestOceanCurrent(
        float lat, float lon, FeatureRegistry registry, SimulationState state)
    {
        DetectedFeature? nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var f in registry.Features.Values)
        {
            if (f.Type != FeatureType.OceanCurrentSystem) continue;
            float dKm = (float)(CrossSectionEngine.CentralAngle(lat, lon,
                                    f.Current.CenterLat, f.Current.CenterLon)
                                * CrossSectionEngine.EARTH_RADIUS_KM);
            if (dKm < nearestDist)
            {
                nearestDist = dKm;
                nearest = f;
            }
        }

        if (nearest == null) return (null, false);

        bool isWarm = nearest.CellIndices.Count > 0
            && nearest.CellIndices.Average(ci => state.TemperatureMap[ci]) > 20f;

        return (nearest.Current.Name, isWarm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 9: Nearby features
    // ─────────────────────────────────────────────────────────────────────────

    private static List<(DetectedFeature Feature, float DistanceKm)> FindNearbyFeatures(
        float lat, float lon, int cellIndex, FeatureRegistry registry)
    {
        var result = new List<(DetectedFeature Feature, float DistanceKm)>();
        foreach (var f in registry.Features.Values)
        {
            if (f.CellIndices.Contains(cellIndex)) continue; // already in ContainingFeatures
            float dKm = (float)(CrossSectionEngine.CentralAngle(lat, lon,
                                    f.Current.CenterLat, f.Current.CenterLon)
                                * CrossSectionEngine.EARTH_RADIUS_KM);
            if (dKm <= 3000f) result.Add((f, dKm));
        }
        result.Sort((a, b) => a.DistanceKm.CompareTo(b.DistanceKm));
        return result.Count > 6 ? result[..6] : result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a numeric priority for sorting features by spatial scale.
    /// Lower value = larger / coarser scale.
    /// </summary>
    private static int FeatureScalePriority(FeatureType type) => type switch
    {
        FeatureType.TectonicPlate                                  => 0,
        FeatureType.Continent or FeatureType.Ocean                 => 1,
        FeatureType.Sea or FeatureType.IslandChain                 => 2,
        FeatureType.MountainRange or FeatureType.Rift
            or FeatureType.SubductionZone or FeatureType.HotspotChain
            or FeatureType.ImpactBasin                             => 3,
        FeatureType.Island                                         => 4,
        FeatureType.River or FeatureType.Lake
            or FeatureType.InlandSea or FeatureType.RiverDelta     => 5,
        FeatureType.JetStream or FeatureType.MonsoonBelt
            or FeatureType.HurricaneCorridor or FeatureType.ITCZ
            or FeatureType.OceanCurrentSystem                      => 6,
        _                                                          => 7,
    };

    private static bool IsLandFeature(FeatureType type) => type switch
    {
        FeatureType.Continent or FeatureType.Island or FeatureType.IslandChain
            or FeatureType.MountainRange or FeatureType.MountainPeak
            or FeatureType.Desert or FeatureType.Rainforest or FeatureType.Savanna
            or FeatureType.Tundra or FeatureType.PolarIceCap => true,
        _ => false,
    };

    private static bool IsWaterFeature(FeatureType type) => type switch
    {
        FeatureType.Ocean or FeatureType.Sea or FeatureType.River
            or FeatureType.Lake or FeatureType.InlandSea
            or FeatureType.RiverDelta => true,
        _ => false,
    };

    /// <summary>Infer a coarse biome label from temperature, precipitation, and elevation.</summary>
    private static string InferBiome(float tempC, float precipMm, float elevM)
    {
        if (elevM < -200f) return "Ocean";
        if (elevM > 3000f) return "Alpine";
        if (tempC < -15f)  return "Ice Sheet";
        if (tempC < 0f)    return "Tundra";
        if (tempC > 25f && precipMm > 2000f) return "Tropical Rainforest";
        if (tempC > 20f && precipMm > 1000f) return "Tropical Seasonal Forest";
        if (tempC > 15f && precipMm > 1500f) return "Temperate Rainforest";
        if (tempC > 10f && precipMm > 500f)  return "Temperate Forest";
        if (precipMm < 250f) return "Desert";
        if (precipMm < 500f) return "Grassland / Steppe";
        return "Boreal / Taiga";
    }

    /// <summary>Format a simulation time in Ma as a human-readable age string.</summary>
    private static string FormatSimAge(double timeMa)
    {
        var absT = Math.Abs(timeMa);
        if (absT < 1) return "< 1 million years";
        if (absT < 1000) return $"~{absT:F0} million years";
        return $"~{absT / 1000:F1} billion years";
    }
}
