using GeoTime.Core.Engines;
using GeoTime.Core.Models;

namespace GeoTime.Core.Services;

/// <summary>
/// Detects primary geographic features from the simulation state and populates
/// the <see cref="FeatureRegistry"/> on <see cref="SimulationState"/>.
/// </summary>
public sealed class FeatureDetectorService
{
    // Earth radius for area calculations
    private const double EarthRadiusKm = 6371.0;

    // Detection thresholds
    private const float LandThresholdM   = -200f;  // height ≥ -200 m → land
    private const float MountainThresholdM = 1500f; // height ≥ 1500 m → mountain candidate

    // Area thresholds in km²
    private const float ContinentMinKm2  = 1_000_000f;
    private const float LargeIslandMinKm2 = 10_000f;
    private const float OceanMinKm2      = 50_000_000f;
    private const float SeaMinKm2        = 2_000_000f;

    // Minimum mountain range area to register (filters single-cell spikes)
    private const float MountainMinKm2   = 5_000f;

    // Hotspot chain grouping: hotspots within this distance (degrees) are chained
    private const double HotspotChainThresholdDeg = 20.0;

    private uint _planetSeed;

    private readonly HydroDetectorService _hydro = new();

    /// <summary>
    /// Run full feature detection and populate <see cref="SimulationState.FeatureRegistry"/>.
    /// </summary>
    public void Detect(
        SimulationState state,
        IReadOnlyList<PlateInfo> plates,
        IReadOnlyList<HotspotInfo> hotspots,
        IEnumerable<GeoLogEntry> events,
        uint planetSeed,
        long currentTick)
    {
        _planetSeed = planetSeed;
        var registry = new FeatureRegistry { LastUpdatedTick = currentTick };

        DetectTectonicPlates(state, plates, registry, currentTick);
        DetectLandAndOcean(state, registry, currentTick);
        DetectMountainRanges(state, registry, currentTick);
        DetectBoundaryFeatures(state, plates, registry, currentTick);
        DetectHotspotChains(hotspots, registry, currentTick);
        DetectImpactBasins(events, registry, currentTick);

        // Phase L3: hydrological and atmospheric feature detection.
        // Also populates state.RiverChannelMap for the ErosionEngine.
        _hydro.Detect(state, registry, planetSeed, currentTick);

        state.FeatureRegistry = registry;
    }

    // ── Tectonic Plates ───────────────────────────────────────────────────────

    private void DetectTectonicPlates(SimulationState state, IReadOnlyList<PlateInfo> plates, FeatureRegistry registry, long tick)
    {
        var gs = state.GridSize;
        var plateAreaCells = new Dictionary<int, List<int>>();

        for (int i = 0; i < state.CellCount; i++)
        {
            var pid = state.PlateMap[i];
            if (!plateAreaCells.TryGetValue(pid, out var list))
                plateAreaCells[pid] = list = [];
            list.Add(i);
        }

        var idx = 0;
        foreach (var (pid, cells) in plateAreaCells)
        {
            if (pid >= plates.Count) continue;
            var plate = plates[pid];
            var (cLat, cLon) = ComputeCentroid(cells, gs);
            var areaKm2 = ComputeAreaKm2(cells, gs);

            var id = MakeId(FeatureType.TectonicPlate, idx);
            var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.TectonicPlate, idx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon, areaKm2,
                FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature
            {
                Id = id,
                Type = FeatureType.TectonicPlate,
                AssociatedPlateIds = { pid.ToString() },
                CellIndices = [.. cells],
            };
            feature.History.Add(snap);
            feature.Metrics["is_oceanic"] = plate.IsOceanic ? 1f : 0f;
            feature.Metrics["area_km2"] = areaKm2;
            registry.Features[id] = feature;
            idx++;
        }
    }

    // ── Continents and Oceans ─────────────────────────────────────────────────

    private void DetectLandAndOcean(SimulationState state, FeatureRegistry registry, long tick)
    {
        var gs = state.GridSize;
        var visited = new bool[state.CellCount];

        int landIdx = 0, oceanIdx = 0;

        for (int start = 0; start < state.CellCount; start++)
        {
            if (visited[start]) continue;

            bool isLand = state.HeightMap[start] >= LandThresholdM;
            var cells = FloodFill8(start, state.HeightMap, visited, gs,
                i => (state.HeightMap[i] >= LandThresholdM) == isLand);

            if (cells.Count == 0) continue;

            var (cLat, cLon) = ComputeCentroid(cells, gs);
            var areaKm2 = ComputeAreaKm2(cells, gs);

            FeatureType type;
            FeatureStatus status = FeatureStatus.Active;
            int typeIndex;

            if (isLand)
            {
                type = areaKm2 >= ContinentMinKm2 ? FeatureType.Continent
                     : areaKm2 >= LargeIslandMinKm2 ? FeatureType.Island
                     : FeatureType.Island;
                typeIndex = landIdx++;
            }
            else
            {
                type = areaKm2 >= OceanMinKm2 ? FeatureType.Ocean
                     : areaKm2 >= SeaMinKm2   ? FeatureType.Sea
                     : FeatureType.Sea;
                typeIndex = oceanIdx++;
            }

            var id = MakeId(type, typeIndex);
            var name = FeatureNameGenerator.Generate(_planetSeed, type, typeIndex);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon, areaKm2,
                status, null, null, null);
            var feature = new DetectedFeature
            {
                Id = id,
                Type = type,
                CellIndices = [.. cells],
            };
            feature.History.Add(snap);
            feature.Metrics["area_km2"] = areaKm2;
            if (!isLand)
                feature.Metrics["mean_depth_m"] = cells.Average(i => state.HeightMap[i]);
            else
                feature.Metrics["mean_elevation_m"] = cells.Average(i => state.HeightMap[i]);

            registry.Features[id] = feature;
        }
    }

    // ── Mountain Ranges ───────────────────────────────────────────────────────

    private void DetectMountainRanges(SimulationState state, FeatureRegistry registry, long tick)
    {
        var gs = state.GridSize;
        var visited = new bool[state.CellCount];

        int rangeIdx = 0;
        for (int start = 0; start < state.CellCount; start++)
        {
            if (visited[start]) continue;
            if (state.HeightMap[start] < MountainThresholdM) continue;

            var cells = FloodFill8(start, state.HeightMap, visited, gs,
                i => state.HeightMap[i] >= MountainThresholdM);

            if (cells.Count == 0) continue;

            var areaKm2 = ComputeAreaKm2(cells, gs);
            if (areaKm2 < MountainMinKm2) continue;

            var (cLat, cLon) = ComputeCentroid(cells, gs);
            float maxElev = cells.Max(i => state.HeightMap[i]);
            float meanElev = (float)cells.Average(i => state.HeightMap[i]);

            // Determine if "Range" (elongated) or "Ridge" (compact) based on aspect ratio
            var type = FeatureType.MountainRange;

            var id = MakeId(type, rangeIdx);
            var name = FeatureNameGenerator.Generate(_planetSeed, type, rangeIdx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon, areaKm2,
                FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature
            {
                Id = id,
                Type = type,
                CellIndices = [.. cells],
            };
            feature.History.Add(snap);
            feature.Metrics["max_elevation_m"] = maxElev;
            feature.Metrics["mean_elevation_m"] = meanElev;
            feature.Metrics["area_km2"] = areaKm2;

            // Rain-shadow detection: compare precipitation on west vs. east flanks
            float westPrecip = ComputeFlanksAvgPrecip(cells, state, gs, flank: -1);
            float eastPrecip = ComputeFlanksAvgPrecip(cells, state, gs, flank: +1);
            float precipDelta = MathF.Abs(westPrecip - eastPrecip);
            feature.Metrics["precip_delta_windward_mm"] = precipDelta;
            if (precipDelta > 500f)
                feature.Metrics["rain_shadow_source"] = 1f;

            registry.Features[id] = feature;
            rangeIdx++;
        }
    }

    /// <summary>Average precipitation of cells one column west (flank=-1) or east (flank=+1) of the range.</summary>
    private static float ComputeFlanksAvgPrecip(List<int> cells, SimulationState state, int gs, int flank)
    {
        var sum = 0.0;
        var count = 0;
        foreach (var ci in cells)
        {
            int row = ci / gs;
            int col = (ci % gs + flank + gs) % gs;
            int neighbour = row * gs + col;
            sum += state.PrecipitationMap[neighbour];
            count++;
        }
        return count > 0 ? (float)(sum / count) : 0f;
    }

    // ── Subduction Zones and Rifts ────────────────────────────────────────────

    private void DetectBoundaryFeatures(SimulationState state, IReadOnlyList<PlateInfo> plates, FeatureRegistry registry, long tick)
    {
        if (plates.Count < 2) return;

        var gs = state.GridSize;
        var boundaries = BoundaryClassifier.Classify(state.PlateMap, plates.ToList(), gs);

        var subductionCells = new List<int>();
        var riftCells = new List<int>();
        var collisionCells = new List<int>();

        foreach (var b in boundaries)
        {
            if (b.Type == BoundaryType.CONVERGENT)
            {
                bool p1Oceanic = b.Plate1 < plates.Count && plates[b.Plate1].IsOceanic;
                bool p2Oceanic = b.Plate2 < plates.Count && plates[b.Plate2].IsOceanic;

                if (p1Oceanic != p2Oceanic)
                    subductionCells.Add(b.CellIndex);
                else if (!p1Oceanic && !p2Oceanic)
                    collisionCells.Add(b.CellIndex);
            }
            else if (b.Type == BoundaryType.DIVERGENT)
            {
                riftCells.Add(b.CellIndex);
            }
        }

        AddBoundaryFeature(subductionCells, FeatureType.SubductionZone, 0, state, gs, registry, tick);
        AddBoundaryFeature(riftCells,       FeatureType.Rift,           0, state, gs, registry, tick);
        // Collision orogens are already captured as mountain ranges; skip to avoid duplication.
    }

    private void AddBoundaryFeature(List<int> cells, FeatureType type, int baseIdx, SimulationState state, int gs, FeatureRegistry registry, long tick)
    {
        if (cells.Count == 0) return;

        var (cLat, cLon) = ComputeCentroid(cells, gs);
        var areaKm2 = ComputeAreaKm2(cells, gs);
        var id = MakeId(type, baseIdx);
        var name = FeatureNameGenerator.Generate(_planetSeed, type, baseIdx);
        var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon, areaKm2,
            FeatureStatus.Active, null, null, null);
        var feature = new DetectedFeature
        {
            Id = id,
            Type = type,
            CellIndices = [.. cells],
        };
        feature.History.Add(snap);
        feature.Metrics["cell_count"] = cells.Count;
        registry.Features[id] = feature;
    }

    // ── Hotspot Chains ────────────────────────────────────────────────────────

    private void DetectHotspotChains(IReadOnlyList<HotspotInfo> hotspots, FeatureRegistry registry, long tick)
    {
        if (hotspots.Count == 0) return;

        // Simple grouping: put all hotspots within threshold distance into the same chain
        var assigned = new bool[hotspots.Count];
        int chainIdx = 0;

        for (int i = 0; i < hotspots.Count; i++)
        {
            if (assigned[i]) continue;

            var group = new List<int> { i };
            assigned[i] = true;

            for (int j = i + 1; j < hotspots.Count; j++)
            {
                if (assigned[j]) continue;
                double dist = GreatCircleDeg(hotspots[i].Lat, hotspots[i].Lon,
                                              hotspots[j].Lat, hotspots[j].Lon);
                if (dist <= HotspotChainThresholdDeg)
                {
                    group.Add(j);
                    assigned[j] = true;
                }
            }

            float cLat = (float)group.Average(k => hotspots[k].Lat);
            float cLon = (float)group.Average(k => hotspots[k].Lon);
            float meanStrength = (float)group.Average(k => hotspots[k].Strength);

            var id = MakeId(FeatureType.HotspotChain, chainIdx);
            var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.HotspotChain, chainIdx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon, 0f,
                FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature { Id = id, Type = FeatureType.HotspotChain };
            feature.History.Add(snap);
            feature.Metrics["hotspot_count"] = group.Count;
            feature.Metrics["mean_strength"] = meanStrength;
            registry.Features[id] = feature;
            chainIdx++;
        }
    }

    // ── Impact Basins ─────────────────────────────────────────────────────────

    private void DetectImpactBasins(IEnumerable<GeoLogEntry> events, FeatureRegistry registry, long tick)
    {
        int basinIdx = 0;
        foreach (var e in events.Where(ev => ev.Type == "IMPACT" && ev.Location.HasValue))
        {
            var loc = e.Location!.Value;
            var id = MakeId(FeatureType.ImpactBasin, basinIdx);
            var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.ImpactBasin, basinIdx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name,
                (float)loc.Lat, (float)loc.Lon, 0f, FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature { Id = id, Type = FeatureType.ImpactBasin };
            feature.History.Add(snap);
            registry.Features[id] = feature;
            basinIdx++;
        }
    }

    // ── Flood-fill helpers ────────────────────────────────────────────────────

    /// <summary>8-connected BFS flood fill. Returns the connected component cells.</summary>
    private static List<int> FloodFill8(int start, float[] heightMap, bool[] visited, int gs, Predicate<int> condition)
    {
        if (visited[start] || !condition(start)) return [];

        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(start);
        visited[start] = true;

        while (queue.Count > 0)
        {
            var ci = queue.Dequeue();
            result.Add(ci);

            int row = ci / gs;
            int col = ci % gs;

            for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = row + dr;
                if (nr < 0 || nr >= gs) continue;
                int nc = (col + dc + gs) % gs; // wrap longitude
                int ni = nr * gs + nc;
                if (visited[ni] || !condition(ni)) continue;
                visited[ni] = true;
                queue.Enqueue(ni);
            }
        }

        return result;
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static (float lat, float lon) CellToLatLon(int ci, int gs)
    {
        int row = ci / gs;
        int col = ci % gs;
        float lat = 90f - row * (180f / gs);
        float lon = -180f + col * (360f / gs);
        return (lat, lon);
    }

    private static float CellAreaKm2(int ci, int gs)
    {
        var (lat, _) = CellToLatLon(ci, gs);
        double latRad = lat * Math.PI / 180.0;
        double dLatRad = Math.PI / gs;
        double dLonRad = 2 * Math.PI / gs;
        return (float)(EarthRadiusKm * EarthRadiusKm * dLatRad * dLonRad * Math.Abs(Math.Cos(latRad)));
    }

    private static (float lat, float lon) ComputeCentroid(List<int> cells, int gs)
    {
        if (cells.Count == 0) return (0f, 0f);
        double sumLat = 0, sumLon = 0;
        foreach (var ci in cells)
        {
            var (lat, lon) = CellToLatLon(ci, gs);
            sumLat += lat;
            sumLon += lon;
        }
        return ((float)(sumLat / cells.Count), (float)(sumLon / cells.Count));
    }

    private static float ComputeAreaKm2(List<int> cells, int gs)
        => cells.Sum(ci => CellAreaKm2(ci, gs));

    private static double GreatCircleDeg(double lat1, double lon1, double lat2, double lon2)
    {
        const double Deg2Rad = Math.PI / 180.0;
        double dLat = (lat2 - lat1) * Deg2Rad;
        double dLon = (lon2 - lon1) * Deg2Rad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Deg2Rad) * Math.Cos(lat2 * Deg2Rad)
                   * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)) * (180.0 / Math.PI);
    }

    private static string MakeId(FeatureType type, int index)
        => $"{type.ToString().ToLowerInvariant()}_{index:D4}";
}
