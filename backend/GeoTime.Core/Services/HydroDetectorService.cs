using GeoTime.Core.Engines;
using GeoTime.Core.Models;

namespace GeoTime.Core.Services;

/// <summary>
/// Phase L3: Detects hydrological features (rivers, lakes, inland seas) and
/// atmospheric circulation features (ITCZ, jet streams, monsoon belts, hurricane
/// corridors) from the simulation state.
/// <para>
/// River detection uses a D8 flow-routing algorithm on the elevation grid.
/// Detected river channels are written to <see cref="SimulationState.RiverChannelMap"/>
/// so that <see cref="ErosionEngine"/> can apply enhanced channel erosion each tick.
/// </para>
/// </summary>
public sealed class HydroDetectorService
{
    // ── D8 constants ──────────────────────────────────────────────────────────

    /// <summary>
    /// Minimum flow accumulation (number of upstream land cells) for a cell to
    /// be considered part of a named river channel.
    /// </summary>
    private const float RiverAccumulationThreshold = 500f;

    /// <summary>
    /// Minimum river main-stem length in cells to be registered as a named
    /// feature (filters tiny rills).
    /// </summary>
    private const int MinRiverStemCells = 8;

    // ── Lake / inland-sea thresholds (km²) ────────────────────────────────────

    private const float LakeMinKm2      =    1_000f;
    private const float InlandSeaMinKm2 =  100_000f;
    private const double EarthRadiusKm  =    6_371.0;

    // ── Atmospheric-feature constants ─────────────────────────────────────────

    /// <summary>Latitude belt half-width (degrees) for the ITCZ search.</summary>
    private const float ItczSearchBelt = 30f;

    /// <summary>Latitude range (absolute) for hurricane corridor genesis zones.</summary>
    private const float HurricaneLatMin = 5f;
    private const float HurricaneLatMax = 20f;

    /// <summary>Sea-surface temperature threshold (°C) for hurricane genesis.</summary>
    private const float HurricaneSstThreshold = 26f;

    /// <summary>Latitude range (absolute) for jet-stream detection.</summary>
    private const float JetStreamLatMin = 30f;
    private const float JetStreamLatMax = 70f;

    /// <summary>
    /// Precipitation threshold (mm/yr) to qualify a region as monsoon-influenced.
    /// </summary>
    private const float MonsoonPrecipThreshold = 500f;

    // ─────────────────────────────────────────────────────────────────────────

    private uint _planetSeed;

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Run D8 flow routing, detect rivers / lakes / atmospheric features, and
    /// append results to <paramref name="registry"/>.  Also writes the computed
    /// flow-accumulation array into <see cref="SimulationState.RiverChannelMap"/>
    /// so that <see cref="ErosionEngine"/> can use it on the next erosion tick.
    /// </summary>
    public void Detect(
        SimulationState state,
        FeatureRegistry registry,
        uint planetSeed,
        long currentTick)
    {
        _planetSeed = planetSeed;
        var gs = state.GridSize;

        // ── D8 flow routing ──────────────────────────────────────────────────
        var flowDir  = ComputeFlowDirection(state.HeightMap, gs);
        var accumulation = ComputeFlowAccumulation(flowDir, gs);

        // Persist flow accumulation so ErosionEngine can boost channel erosion.
        Array.Copy(accumulation, state.RiverChannelMap, state.CellCount);

        // ── River features ───────────────────────────────────────────────────
        DetectRivers(state, flowDir, accumulation, registry, currentTick);

        // ── Lake / inland-sea features ───────────────────────────────────────
        DetectLakes(state, flowDir, accumulation, registry, currentTick);

        // ── Atmospheric features ─────────────────────────────────────────────
        DetectItcz(state, registry, currentTick);
        DetectJetStreams(state, registry, currentTick);
        DetectMonsoonBelts(state, registry, currentTick);
        DetectHurricaneCorridors(state, registry, currentTick);
    }

    // ── D8 flow direction ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the D8 flow-direction array.  Each element is the index of the
    /// steepest-descent neighbour (8-connected), or -1 for pit/ocean cells.
    /// Land cells only; ocean cells (height &lt; 0) are assigned -1.
    /// </summary>
    public static int[] ComputeFlowDirection(float[] heightMap, int gs)
    {
        var cc  = gs * gs;
        var dir = new int[cc];
        Array.Fill(dir, -1);

        for (var i = 0; i < cc; i++)
        {
            // Water flows from land into sea or to lower land cells.
            int row = i / gs, col = i % gs;
            double h   = heightMap[i];
            var best   = -1;
            var bestDh = 0.0; // must be positive (flowing downhill)

            for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = row + dr;
                if (nr < 0 || nr >= gs) continue;
                int nc = (col + dc + gs) % gs;
                int ni = nr * gs + nc;
                var dh = h - heightMap[ni]; // positive = downhill
                if (dh > bestDh) { bestDh = dh; best = ni; }
            }
            dir[i] = best;
        }
        return dir;
    }

    /// <summary>
    /// Computes flow accumulation (number of upstream cells that drain through
    /// each cell) using a topological upstream-to-downstream sweep.
    /// </summary>
    public static float[] ComputeFlowAccumulation(int[] flowDir, int gs)
    {
        var cc    = gs * gs;
        var accum = new float[cc];
        Array.Fill(accum, 1f); // every cell contributes at least itself

        // Count in-degrees so we can do a proper topological sort.
        var inDeg = new int[cc];
        for (var i = 0; i < cc; i++)
            if (flowDir[i] >= 0) inDeg[flowDir[i]]++;

        // Enqueue cells with no upstream cells (sources).
        var queue = new Queue<int>();
        for (var i = 0; i < cc; i++)
            if (inDeg[i] == 0) queue.Enqueue(i);

        while (queue.Count > 0)
        {
            var ci = queue.Dequeue();
            var ds = flowDir[ci];
            if (ds < 0) continue;
            accum[ds] += accum[ci];
            if (--inDeg[ds] == 0) queue.Enqueue(ds);
        }
        return accum;
    }

    // ── River detection ───────────────────────────────────────────────────────

    private void DetectRivers(
        SimulationState state,
        int[] flowDir,
        float[] accumulation,
        FeatureRegistry registry,
        long tick)
    {
        var gs = state.GridSize;
        var cc = state.CellCount;

        // Find all river outlet cells: land cells with accumulation above threshold
        // that drain into the sea (their downstream neighbour is ocean or -1/edge).
        var isOutlet = new bool[cc];
        for (var i = 0; i < cc; i++)
        {
            if (accumulation[i] < RiverAccumulationThreshold) continue;
            var ds = flowDir[i];
            // An outlet drains directly into the ocean (next cell is ocean) or has no outflow.
            bool drainsToSea = ds < 0 || state.HeightMap[ds] < 0f;
            if (drainsToSea && state.HeightMap[i] >= 0f)
                isOutlet[i] = true;
        }

        // Also treat cells as outlets if their downstream is ocean even if they
        // don't meet the full accumulation threshold but DO have high accumulation
        // (handles river mouths that span coastal cells).
        var visited = new bool[cc];
        int riverIdx = 0;

        foreach (var outlet in Enumerable.Range(0, cc).Where(i => isOutlet[i]))
        {
            if (visited[outlet]) continue;

            // Trace the main stem upstream: always follow the highest-accumulation upstream cell.
            var stem = TraceStem(outlet, flowDir, accumulation, visited, gs);
            if (stem.Count < MinRiverStemCells) continue;

            var (cLat, cLon) = ComputeCentroid(stem, gs);
            var lengthKm     = ComputePathLengthKm(stem, gs);

            // Discharge proxy: mean accumulation × mean precipitation in catchment.
            float meanAccum  = (float)stem.Average(i => accumulation[i]);
            float meanPrecip = (float)stem.Average(i => state.PrecipitationMap[i]);
            float discharge  = meanAccum * meanPrecip * 1e-6f; // normalised proxy

            // Delta classification at the outlet (first element of the stem list).
            int outletCell = stem[0];
            float gradient = stem.Count > 1
                ? MathF.Abs(state.HeightMap[stem[^1]] - state.HeightMap[outletCell]) / (float)stem.Count
                : 0f;
            string deltaType = gradient > 5f ? "fan_delta"
                             : meanPrecip > 800f ? "birdfoot_delta"
                             : "estuarine";

            var id   = MakeId(FeatureType.River, riverIdx);
            var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.River, riverIdx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon,
                0f, FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature
            {
                Id         = id,
                Type       = FeatureType.River,
                CellIndices = [.. stem],
            };
            feature.History.Add(snap);
            feature.Metrics["river_length_km"] = lengthKm;
            feature.Metrics["discharge_m3s"]   = discharge;
            feature.Metrics["gradient"]        = gradient;
            feature.Metrics[deltaType]         = 1f;

            registry.Features[id] = feature;
            riverIdx++;
        }
    }

    /// <summary>
    /// Traces the main stem of a river upstream from <paramref name="outlet"/>
    /// by always following the upstream cell with the highest flow accumulation.
    /// </summary>
    private static List<int> TraceStem(int outlet, int[] flowDir, float[] accumulation,
        bool[] visited, int gs)
    {
        var cc   = gs * gs;
        var stem = new List<int> { outlet };
        visited[outlet] = true;

        // Build reverse-flow map: for each cell, which cells drain into it?
        // This is O(N) and reusable per outlet via a local scan.
        var current = outlet;
        while (true)
        {
            // Find the unvisited upstream neighbour with the highest accumulation.
            int row = current / gs, col = current % gs;
            var best    = -1;
            var bestAcc = 0f;

            for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = row + dr;
                if (nr < 0 || nr >= gs) continue;
                int nc  = (col + dc + gs) % gs;
                int ni  = nr * gs + nc;
                if (visited[ni]) continue;
                if (flowDir[ni] != current) continue; // must actually drain here
                if (accumulation[ni] > bestAcc) { bestAcc = accumulation[ni]; best = ni; }
            }

            if (best < 0) break; // no more upstream cells
            visited[best] = true;
            stem.Add(best);
            current = best;
        }
        return stem;
    }

    // ── Lake / Inland Sea detection ───────────────────────────────────────────

    private void DetectLakes(
        SimulationState state,
        int[] flowDir,
        float[] accumulation,
        FeatureRegistry registry,
        long tick)
    {
        var gs       = state.GridSize;
        var cc       = state.CellCount;
        var visitedL = new bool[cc];

        // Endorheic basin sinks: land cells with no outflow (flowDir == -1) but
        // surrounded by land cells (i.e., not coastal ocean pits).
        int lakeIdx = 0;
        for (var sink = 0; sink < cc; sink++)
        {
            if (visitedL[sink]) continue;
            if (flowDir[sink] != -1) continue;
            if (state.HeightMap[sink] < 0f) continue; // skip ocean pits

            // BFS: expand to all land cells that drain into this sink.
            var basin = FloodFillUpstream(sink, flowDir, state.HeightMap, visitedL, gs);
            if (basin.Count == 0) continue;

            var areaKm2 = basin.Sum(i => CellAreaKm2(i, gs));
            if (areaKm2 < LakeMinKm2) continue;

            var (cLat, cLon) = ComputeCentroid(basin, gs);
            float meanPrecip = (float)basin.Average(i => state.PrecipitationMap[i]);
            float meanTemp   = (float)basin.Average(i => state.TemperatureMap[i]);
            // Salinity proxy: hot/dry basins → saline
            float evapProxy  = meanTemp > 15f ? (meanTemp - 15f) * 50f : 0f;
            bool isSaline    = evapProxy > meanPrecip;

            var type = areaKm2 >= InlandSeaMinKm2 ? FeatureType.InlandSea : FeatureType.Lake;
            var id   = MakeId(type, lakeIdx);
            var name = FeatureNameGenerator.Generate(_planetSeed, type, lakeIdx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon,
                areaKm2, FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature
            {
                Id          = id,
                Type        = type,
                CellIndices = [.. basin],
            };
            feature.History.Add(snap);
            feature.Metrics["area_km2"]     = areaKm2;
            feature.Metrics["is_saline"]    = isSaline ? 1f : 0f;
            feature.Metrics["mean_precip_mm"] = meanPrecip;

            registry.Features[id] = feature;
            lakeIdx++;
        }
    }

    /// <summary>
    /// BFS from <paramref name="sink"/> collecting all land cells whose flow
    /// paths ultimately lead to this sink (i.e., the drainage basin).
    /// </summary>
    private static List<int> FloodFillUpstream(
        int sink, int[] flowDir, float[] heightMap, bool[] visited, int gs)
    {
        var result = new List<int>();
        var queue  = new Queue<int>();

        if (!visited[sink] && heightMap[sink] >= 0f)
        {
            visited[sink] = true;
            queue.Enqueue(sink);
        }

        var cc = gs * gs;
        while (queue.Count > 0)
        {
            var ci  = queue.Dequeue();
            result.Add(ci);
            int row = ci / gs, col = ci % gs;

            for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = row + dr;
                if (nr < 0 || nr >= gs) continue;
                int nc = (col + dc + gs) % gs;
                int ni = nr * gs + nc;
                if (visited[ni]) continue;
                if (heightMap[ni] < 0f) continue;
                if (flowDir[ni] != ci) continue; // only cells that drain INTO ci
                visited[ni] = true;
                queue.Enqueue(ni);
            }
        }
        return result;
    }

    // ── Atmospheric features ──────────────────────────────────────────────────

    /// <summary>
    /// Detects the Inter-Tropical Convergence Zone (ITCZ): the latitude band with
    /// highest surface precipitation within ±30° of the equator.
    /// </summary>
    private void DetectItcz(SimulationState state, FeatureRegistry registry, long tick)
    {
        var gs = state.GridSize;

        // Compute mean precipitation per latitude row.
        var rowPrecip = new float[gs];
        for (int row = 0; row < gs; row++)
        {
            float sum = 0f;
            for (int col = 0; col < gs; col++)
                sum += state.PrecipitationMap[row * gs + col];
            rowPrecip[row] = sum / gs;
        }

        // Restrict to the equatorial belt (±ItczSearchBelt degrees).
        // Row mapping: row = (90 - lat) / 180 * gs  (row 0 = north pole, row gs/2 = equator).
        // lat = +(ItczSearchBelt) → rowMin = (90 - ItczSearchBelt) / 180 * gs
        // lat = -(ItczSearchBelt) → rowMax = (90 + ItczSearchBelt) / 180 * gs
        int rowMin = (int)((90f - ItczSearchBelt) / 180f * gs);
        int rowMax = (int)((90f + ItczSearchBelt) / 180f * gs);
        rowMin = Math.Clamp(rowMin, 0, gs - 1);
        rowMax = Math.Clamp(rowMax, 0, gs - 1);

        int   bestRow   = rowMin;
        float bestPrecip = rowPrecip[rowMin];
        for (int r = rowMin + 1; r <= rowMax; r++)
        {
            if (rowPrecip[r] > bestPrecip)
            {
                bestPrecip = rowPrecip[r];
                bestRow    = r;
            }
        }

        float itczLat = 90f - bestRow * (180f / gs);

        var id   = MakeId(FeatureType.ITCZ, 0);
        var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.ITCZ, 0);
        var snap = new FeatureSnapshot(tick, long.MaxValue, name, itczLat, 0f,
            0f, FeatureStatus.Active, null, null, null);
        var feature = new DetectedFeature { Id = id, Type = FeatureType.ITCZ };
        feature.History.Add(snap);
        feature.Metrics["latitude"]        = itczLat;
        feature.Metrics["mean_precip_mm"]  = bestPrecip;

        registry.Features[id] = feature;
    }

    /// <summary>
    /// Detects polar and subtropical jet streams as latitude bands with maximal
    /// zonal (east-west) wind speed at mid-latitudes (30°–70°).
    /// </summary>
    private void DetectJetStreams(SimulationState state, FeatureRegistry registry, long tick)
    {
        var gs = state.GridSize;

        // Compute mean |windU| per latitude row in the 30–70° bands (both hemispheres).
        // North hemisphere rows have row < gs/2; south hemisphere rows > gs/2.
        int jetIdx = 0;
        foreach (int hemisphere in new[] { 1, -1 }) // north, south
        {
            int rStart = hemisphere > 0
                ? (int)((90f - JetStreamLatMax) / 180f * gs)
                : (int)((90f + JetStreamLatMin) / 180f * gs);
            int rEnd = hemisphere > 0
                ? (int)((90f - JetStreamLatMin) / 180f * gs)
                : (int)((90f + JetStreamLatMax) / 180f * gs);

            rStart = Math.Clamp(rStart, 0, gs - 1);
            rEnd   = Math.Clamp(rEnd,   0, gs - 1);
            if (rStart > rEnd) (rStart, rEnd) = (rEnd, rStart);

            int bestRow = rStart;
            float bestWind = 0f;
            for (int row = rStart; row <= rEnd; row++)
            {
                float windRow = 0f;
                for (int col = 0; col < gs; col++)
                    windRow += MathF.Abs(state.WindUMap[row * gs + col]);
                windRow /= gs;
                if (windRow > bestWind) { bestWind = windRow; bestRow = row; }
            }

            float jetLat = 90f - bestRow * (180f / gs);
            var id   = MakeId(FeatureType.JetStream, jetIdx);
            var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.JetStream, jetIdx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, jetLat, 0f,
                0f, FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature { Id = id, Type = FeatureType.JetStream };
            feature.History.Add(snap);
            feature.Metrics["latitude"]       = jetLat;
            feature.Metrics["mean_wind_ms"]   = bestWind;

            registry.Features[id] = feature;
            jetIdx++;
        }
    }

    /// <summary>
    /// Detects monsoon belts: land regions with high mean annual precipitation
    /// (proxy for strong seasonal moisture delivery by prevailing winds).
    /// </summary>
    private void DetectMonsoonBelts(SimulationState state, FeatureRegistry registry, long tick)
    {
        var gs = state.GridSize;
        var cc = state.CellCount;

        // Collect land cells exceeding the monsoon precipitation threshold.
        var monsoonCells = new List<int>();
        for (var i = 0; i < cc; i++)
        {
            if (state.HeightMap[i] < 0f) continue;
            if (state.PrecipitationMap[i] >= MonsoonPrecipThreshold)
                monsoonCells.Add(i);
        }

        if (monsoonCells.Count == 0) return;

        var (cLat, cLon) = ComputeCentroid(monsoonCells, gs);
        float meanPrecip = (float)monsoonCells.Average(i => state.PrecipitationMap[i]);

        var id   = MakeId(FeatureType.MonsoonBelt, 0);
        var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.MonsoonBelt, 0);
        var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon,
            0f, FeatureStatus.Active, null, null, null);
        var feature = new DetectedFeature { Id = id, Type = FeatureType.MonsoonBelt };
        feature.History.Add(snap);
        feature.Metrics["cell_count"]      = monsoonCells.Count;
        feature.Metrics["mean_precip_mm"]  = meanPrecip;

        registry.Features[id] = feature;
    }

    /// <summary>
    /// Detects hurricane corridors: ocean regions with SST > 26 °C at latitudes
    /// 5°–20° (both hemispheres) where Coriolis force is non-negligible.
    /// </summary>
    private void DetectHurricaneCorridors(SimulationState state, FeatureRegistry registry, long tick)
    {
        var gs = state.GridSize;
        var cc = state.CellCount;

        int corrIdx = 0;
        // Check both hemispheres.
        foreach (int hemisphere in new[] { 1, -1 })
        {
            var corridorCells = new List<int>();
            for (var i = 0; i < cc; i++)
            {
                if (state.HeightMap[i] >= 0f) continue; // ocean only
                float lat = 90f - (i / gs) * (180f / gs);
                float absLat = MathF.Abs(lat);
                if (absLat < HurricaneLatMin || absLat > HurricaneLatMax) continue;
                if (hemisphere > 0 && lat < 0) continue;
                if (hemisphere < 0 && lat > 0) continue;
                if (state.TemperatureMap[i] < HurricaneSstThreshold) continue;
                corridorCells.Add(i);
            }

            if (corridorCells.Count == 0) continue;

            var (cLat, cLon) = ComputeCentroid(corridorCells, gs);
            float meanSst    = (float)corridorCells.Average(i => state.TemperatureMap[i]);

            var id   = MakeId(FeatureType.HurricaneCorridor, corrIdx);
            var name = FeatureNameGenerator.Generate(_planetSeed, FeatureType.HurricaneCorridor, corrIdx);
            var snap = new FeatureSnapshot(tick, long.MaxValue, name, cLat, cLon,
                0f, FeatureStatus.Active, null, null, null);
            var feature = new DetectedFeature { Id = id, Type = FeatureType.HurricaneCorridor };
            feature.History.Add(snap);
            feature.Metrics["cell_count"]  = corridorCells.Count;
            feature.Metrics["mean_sst_c"]  = meanSst;

            registry.Features[id] = feature;
            corrIdx++;
        }
    }

    // ── Geometry helpers ──────────────────────────────────────────────────────

    private static (float lat, float lon) CellToLatLon(int ci, int gs)
    {
        int row = ci / gs, col = ci % gs;
        return (90f - row * (180f / gs), -180f + col * (360f / gs));
    }

    private static float CellAreaKm2(int ci, int gs)
    {
        var (lat, _) = CellToLatLon(ci, gs);
        double latRad  = lat * Math.PI / 180.0;
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
            sumLat += lat; sumLon += lon;
        }
        return ((float)(sumLat / cells.Count), (float)(sumLon / cells.Count));
    }

    /// <summary>Approximate path length in km along a list of grid-cell indices.</summary>
    private static float ComputePathLengthKm(List<int> stem, int gs)
    {
        if (stem.Count < 2) return 0f;
        double totalKm = 0;
        for (int k = 1; k < stem.Count; k++)
        {
            var (lat1, lon1) = CellToLatLon(stem[k - 1], gs);
            var (lat2, lon2) = CellToLatLon(stem[k],     gs);
            totalKm += GreatCircleKm(lat1, lon1, lat2, lon2);
        }
        return (float)totalKm;
    }

    private static double GreatCircleKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double Deg2Rad = Math.PI / 180.0;
        double dLat = (lat2 - lat1) * Deg2Rad;
        double dLon = (lon2 - lon1) * Deg2Rad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Deg2Rad) * Math.Cos(lat2 * Deg2Rad)
                   * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string MakeId(FeatureType type, int index)
        => $"{type.ToString().ToLowerInvariant()}_{index:D4}";
}
