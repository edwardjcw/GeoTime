using GeoTime.Core.Kernel;
using GeoTime.Core.Models;

namespace GeoTime.Core.Proc;

/// <summary>
/// Generates initial conditions for a terrestrial planet: tectonic plates,
/// heightfield, crust properties, mantle plume hotspots, and atmosphere.
/// </summary>
public sealed class PlanetGenerator(uint seed)
{
    private const double TWO_PI = 2 * Math.PI;
    private const double DEG2RAD = Math.PI / 180.0;

    public PlanetGeneratorResult Generate(SimulationState state, SimulationPlanetConfig? config = null)
    {
        var planetConfig = config ?? new SimulationPlanetConfig();
        var rng = new Xoshiro256ss(seed);
        var noise = new SimplexNoise(rng);
        var gs = state.GridSize;

        var numPlates = rng.NextInt(10, 16);
        var plates = GeneratePlates(rng, numPlates, state);
        bool[]? continentalMask = null;
        if (planetConfig.IsPlateTectonicsActive(SimClock.INITIAL_TIME))
        {
            continentalMask = BuildContinentalMask(rng, plates, state, out _);
            GeneratePlateCoupledHeightMap(noise, continentalMask, state);
        }
        else
        {
            GenerateHeightMap(noise, state);
        }

        var seaLevel = FindSeaLevel(state.HeightMap, 0.70);
        NormaliseHeightMap(state.HeightMap, seaLevel);
        if (continentalMask != null)
        {
            var boundaryMask = ComputePlateBoundaryMask(state.PlateMap, state.GridSize);
            EnforcePlateCoupledHeights(state, continentalMask, boundaryMask);
        }

        ClassifyPlatesAndCrust(plates, state, rng);
        ConsolidateLandPlateCoherence(state);
        // Soften col-0 / col-(gs-1) cliffs so the periodic lon wrap matches UV seam.
        GridSeamSync.SyncAntimeridian(state);
        var hotspots = GenerateHotspots(rng);

        var atmosphere = new AtmosphericComposition
        {
            N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01
        };

        return new PlanetGeneratorResult
        {
            Plates = plates,
            Hotspots = hotspots,
            Atmosphere = atmosphere,
            Seed = seed,
        };
    }

    private static double RowToLat(int row, int gs) => Math.PI / 2 - (double)row / gs * Math.PI;
    private static double ColToLon(int col, int gs) => (double)col / gs * TWO_PI - Math.PI;

    private static (double x, double y, double z) LatLonToXYZ(double lat, double lon)
    {
        var cosLat = Math.Cos(lat);
        return (cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
    }

    private static double GreatCircleDist(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = lat2 - lat1, dLon = lon2 - lon1;
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static List<PlateInfo> GeneratePlates(Xoshiro256ss rng, int numPlates, SimulationState state)
    {
        var gs = state.GridSize;
        var cellCount = gs * gs;
        var centersLat = new double[numPlates];
        var centersLon = new double[numPlates];

        for (var p = 0; p < numPlates; p++)
        {
            centersLat[p] = Math.Asin(rng.NextFloat(-1, 1));
            centersLon[p] = rng.NextFloat(-Math.PI, Math.PI);
        }

        // Lloyd relaxation — 3 iterations
        for (var iter = 0; iter < 3; iter++)
        {
            for (var row = 0; row < gs; row++)
            {
                var lat = RowToLat(row, gs);
                for (var col = 0; col < gs; col++)
                {
                    var lon = ColToLon(col, gs);
                    var bestPlate = 0;
                    var bestDist = double.PositiveInfinity;
                    for (var p = 0; p < numPlates; p++)
                    {
                        var d = GreatCircleDist(lat, lon, centersLat[p], centersLon[p]);
                        if (d < bestDist) { bestDist = d; bestPlate = p; }
                    }
                    state.PlateMap[row * gs + col] = (ushort)bestPlate;
                }
            }

            var sumX = new double[numPlates];
            var sumY = new double[numPlates];
            var sumZ = new double[numPlates];
            var count = new double[numPlates];

            for (var row = 0; row < gs; row++)
            {
                var lat = RowToLat(row, gs);
                for (var col = 0; col < gs; col++)
                {
                    var lon = ColToLon(col, gs);
                    int p = state.PlateMap[row * gs + col];
                    var (cx, cy, cz) = LatLonToXYZ(lat, lon);
                    sumX[p] += cx; sumY[p] += cy; sumZ[p] += cz;
                    count[p]++;
                }
            }

            for (var p = 0; p < numPlates; p++)
            {
                if (count[p] == 0) continue;
                var mx = sumX[p] / count[p];
                var my = sumY[p] / count[p];
                var mz = sumZ[p] / count[p];
                var r = Math.Sqrt(mx * mx + my * my + mz * mz);
                if (r < 1e-12) continue;
                centersLat[p] = Math.Asin(Math.Clamp(mz / r, -1, 1));
                centersLon[p] = Math.Atan2(my, mx);
            }
        }

        // Final plate assignment
        for (var row = 0; row < gs; row++)
        {
            var lat = RowToLat(row, gs);
            for (var col = 0; col < gs; col++)
            {
                var lon = ColToLon(col, gs);
                var bestPlate = 0;
                var bestDist = double.PositiveInfinity;
                for (var p = 0; p < numPlates; p++)
                {
                    var d = GreatCircleDist(lat, lon, centersLat[p], centersLon[p]);
                    if (d < bestDist) { bestDist = d; bestPlate = p; }
                }
                state.PlateMap[row * gs + col] = (ushort)bestPlate;
            }
        }

        var areaCount = new double[numPlates];
        for (var i = 0; i < cellCount; i++) areaCount[state.PlateMap[i]]++;

        var plates = new List<PlateInfo>();
        for (var p = 0; p < numPlates; p++)
        {
            plates.Add(new PlateInfo
            {
                Id = p,
                CenterLat = centersLat[p] / DEG2RAD,
                CenterLon = centersLon[p] / DEG2RAD,
                AngularVelocity = new AngularVelocity
                {
                    Lat = rng.NextFloat(-1, 1),
                    Lon = rng.NextFloat(-1, 1),
                    Rate = rng.NextFloat(0.5, 4),
                },
                IsOceanic = false,
                Area = areaCount[p] / cellCount,
            });
        }
        return plates;
    }

    private static void GenerateHeightMap(SimplexNoise noise, SimulationState state)
    {
        var gs = state.GridSize;
        const double scale = 3.0;
        for (var row = 0; row < gs; row++)
        {
            var lat = RowToLat(row, gs);
            for (var col = 0; col < gs; col++)
            {
                var lon = ColToLon(col, gs);
                var (nx, ny, nz) = LatLonToXYZ(lat, lon);
                state.HeightMap[row * gs + col] = (float)noise.Fbm(nx * scale, ny * scale, nz * scale, 4);
            }
        }
    }

    /// <summary>
    /// BF-2: Fraction of plates skipped when planting continental seeds (was 0.30).
    /// Lower → more plates host continental crust.
    /// </summary>
    internal const float ContinentalPlateSkipFraction = 0.12f;

    /// <summary>BF-2: Minimum continental area as a fraction of plate cells (was 0.10).</summary>
    internal const float ContinentalAreaFractionMin = 0.20f;

    /// <summary>BF-2: Maximum continental area as a fraction of plate cells (was 0.38).</summary>
    internal const float ContinentalAreaFractionMax = 0.48f;

    /// <summary>
    /// Plates at or above this share of the globe may receive a second continental seed.
    /// </summary>
    internal const double LargePlateAreaThreshold = 0.08;

    /// <summary>
    /// Plate-tectonic-era continental mask: seeds in plate interiors, flood-filled within plate ID.
    /// Large plates may receive two terranes.
    /// </summary>
    private static bool[] BuildContinentalMask(
        Xoshiro256ss rng, List<PlateInfo> plates, SimulationState state, out int terraneCount)
    {
        var gs = state.GridSize;
        var cellCount = state.CellCount;
        Array.Clear(state.TerraneMap);
        Array.Clear(state.SutureMap);
        var isBoundary = ComputePlateBoundaryMask(state.PlateMap, gs);
        var isContinental = new bool[cellCount];
        var plateCellCount = new int[plates.Count];
        for (var i = 0; i < cellCount; i++)
            plateCellCount[state.PlateMap[i]]++;

        ushort nextTerraneId = 1;
        foreach (var plate in plates)
        {
            if (rng.NextFloat(0, 1) < ContinentalPlateSkipFraction) continue;

            var seedCount = plate.Area >= LargePlateAreaThreshold ? 2 : 1;
            for (var s = 0; s < seedCount; s++)
            {
                var seed = s == 0
                    ? FindPlateInteriorSeed(plate, state, isBoundary)
                    : FindSecondaryPlateInteriorSeed(plate, state, isBoundary, isContinental);
                if (seed < 0) continue;

                var areaFraction = rng.NextFloat(ContinentalAreaFractionMin, ContinentalAreaFractionMax);
                // Second seed on a large plate is smaller so the two terranes do not fuse at gen.
                if (s > 0) areaFraction *= 0.55f;
                var targetCells = Math.Max(1, (int)(plateCellCount[plate.Id] * areaFraction));
                var domain = GrowContinentalDomain(
                    seed, plate.Id, targetCells, state.PlateMap, gs, isBoundary, isContinental);
                if (domain.Count == 0) continue;

                var terraneId = nextTerraneId++;
                foreach (var cell in domain)
                {
                    isContinental[cell] = true;
                    state.TerraneMap[cell] = terraneId;
                }
            }
        }

        terraneCount = nextTerraneId - 1;
        return isContinental;
    }

    /// <summary>
    /// One-time Hadean→plate-tectonic transition: repartition cross-plate Simplex crust into
    /// plate-interior continental terranes (metre-space heights). Preserves P1 stagnant-lid
    /// generation, then couples crust before advection can shred it.
    /// </summary>
    internal static void ApplyPlateTectonicsOnsetTransition(
        SimulationState state, List<PlateInfo> plates, uint seed)
    {
        var rng = new Xoshiro256ss(seed ^ 0x0BF2_0001u);
        var mask = BuildContinentalMask(rng, plates, state, out _);
        var isBoundary = ComputePlateBoundaryMask(state.PlateMap, state.GridSize);

        const float continentalFloorM = 250f;
        const float oceanicCeilingM = -400f;
        const float continentalCrustKm = 35f;
        const float oceanicCrustKm = 7f;

        for (var i = 0; i < state.CellCount; i++)
        {
            if (mask[i] && !isBoundary[i])
            {
                // Keep existing elevated crust when possible; otherwise raise to continental floor.
                state.HeightMap[i] = state.HeightMap[i] > continentalFloorM
                    ? state.HeightMap[i]
                    : continentalFloorM + Math.Max(0f, state.HeightMap[i] * 0.05f);
                state.CrustThicknessMap[i] = continentalCrustKm;
                state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
            }
            else
            {
                // Submerge boundary and non-mask cells so advection gaps stay oceanic.
                if (state.HeightMap[i] > oceanicCeilingM)
                    state.HeightMap[i] = oceanicCeilingM;
                state.CrustThicknessMap[i] = oceanicCrustKm;
                state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
                if (!mask[i])
                    state.TerraneMap[i] = 0;
            }
        }

        var sumH = new double[plates.Count];
        var countH = new double[plates.Count];
        for (var i = 0; i < state.CellCount; i++)
        {
            var p = state.PlateMap[i];
            if (p >= plates.Count) continue;
            sumH[p] += state.HeightMap[i];
            countH[p]++;
        }
        foreach (var plate in plates)
            plate.IsOceanic = countH[plate.Id] > 0 && sumH[plate.Id] / countH[plate.Id] < 0;

        ConsolidateLandPlateCoherence(state);
        GridSeamSync.SyncAntimeridian(state);
    }

    /// <summary>
    /// Assign noise heights from a plate-coupled continental mask; boundaries remain oceanic.
    /// </summary>
    private static void GeneratePlateCoupledHeightMap(SimplexNoise noise, bool[] isContinental, SimulationState state)
    {
        var gs = state.GridSize;
        const double scale = 3.0;
        var isBoundary = ComputePlateBoundaryMask(state.PlateMap, gs);

        for (var row = 0; row < gs; row++)
        {
            var lat = RowToLat(row, gs);
            for (var col = 0; col < gs; col++)
            {
                var i = row * gs + col;
                var lon = ColToLon(col, gs);
                var (nx, ny, nz) = LatLonToXYZ(lat, lon);
                var perturbation = noise.Fbm(nx * scale, ny * scale, nz * scale, 4);

                if (isBoundary[i] || !isContinental[i])
                    state.HeightMap[i] = (float)(-0.25 - 0.55 * Math.Abs(perturbation));
                else
                    state.HeightMap[i] = (float)(0.25 + 0.55 * Math.Abs(perturbation));
            }
        }
    }

    /// <summary>
    /// Re-assert oceanic vs continental signs after sea-level normalization.
    /// </summary>
    private static void EnforcePlateCoupledHeights(SimulationState state, bool[] isContinental, bool[] isBoundary)
    {
        const float continentalMin = 0.05f;
        const float oceanicMax = -0.05f;
        for (var i = 0; i < state.CellCount; i++)
        {
            if (isContinental[i] && !isBoundary[i])
                state.HeightMap[i] = Math.Max(state.HeightMap[i], continentalMin);
            else
                state.HeightMap[i] = Math.Min(state.HeightMap[i], oceanicMax);
        }
    }

    private static bool[] ComputePlateBoundaryMask(ushort[] plateMap, int gs)
    {
        var mask = new bool[plateMap.Length];
        for (var row = 0; row < gs; row++)
        {
            for (var col = 0; col < gs; col++)
            {
                var i = row * gs + col;
                var plateId = plateMap[i];
                foreach (var ni in GetNeighborIndices(row, col, gs))
                {
                    if (plateMap[ni] != plateId)
                    {
                        mask[i] = true;
                        break;
                    }
                }
            }
        }
        return mask;
    }

    private static int[] GetNeighborIndices(int row, int col, int gs)
    {
        var n = new List<int>(4);
        if (row > 0) n.Add((row - 1) * gs + col);
        if (row < gs - 1) n.Add((row + 1) * gs + col);
        n.Add(row * gs + (col - 1 + gs) % gs);
        n.Add(row * gs + (col + 1) % gs);
        return n.ToArray();
    }

    private static int FindPlateInteriorSeed(PlateInfo plate, SimulationState state, bool[] isBoundary)
    {
        var gs = state.GridSize;
        var centerLat = plate.CenterLat * DEG2RAD;
        var centerLon = plate.CenterLon * DEG2RAD;
        var bestCell = -1;
        var bestDist = double.PositiveInfinity;

        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] != plate.Id || isBoundary[i]) continue;
            var row = i / gs;
            var col = i % gs;
            var lat = RowToLat(row, gs);
            var lon = ColToLon(col, gs);
            var d = GreatCircleDist(lat, lon, centerLat, centerLon);
            if (d < bestDist)
            {
                bestDist = d;
                bestCell = i;
            }
        }

        return bestCell;
    }

    /// <summary>
    /// Second seed on a large plate: interior cell farthest from the plate center
    /// (and not already continental), so the two terranes start separated.
    /// </summary>
    private static int FindSecondaryPlateInteriorSeed(
        PlateInfo plate, SimulationState state, bool[] isBoundary, bool[] isContinental)
    {
        var gs = state.GridSize;
        var centerLat = plate.CenterLat * DEG2RAD;
        var centerLon = plate.CenterLon * DEG2RAD;
        var bestCell = -1;
        var bestDist = double.NegativeInfinity;

        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] != plate.Id || isBoundary[i] || isContinental[i]) continue;
            var row = i / gs;
            var col = i % gs;
            var d = GreatCircleDist(RowToLat(row, gs), ColToLon(col, gs), centerLat, centerLon);
            if (d > bestDist)
            {
                bestDist = d;
                bestCell = i;
            }
        }

        return bestCell;
    }

    private static HashSet<int> GrowContinentalDomain(
        int seed, int plateId, int targetCells, ushort[] plateMap, int gs, bool[] isBoundary,
        bool[]? alreadyContinental = null)
    {
        var domain = new HashSet<int>();
        if (targetCells <= 0 || isBoundary[seed] || plateMap[seed] != plateId)
            return domain;
        if (alreadyContinental != null && alreadyContinental[seed])
            return domain;

        var queue = new Queue<int>();
        queue.Enqueue(seed);
        domain.Add(seed);

        while (queue.Count > 0 && domain.Count < targetCells)
        {
            var cell = queue.Dequeue();
            var row = cell / gs;
            var col = cell % gs;

            foreach (var ni in GetNeighborIndices(row, col, gs))
            {
                if (domain.Contains(ni)) continue;
                if (plateMap[ni] != plateId || isBoundary[ni]) continue;
                if (alreadyContinental != null && alreadyContinental[ni]) continue;
                domain.Add(ni);
                queue.Enqueue(ni);
                if (domain.Count >= targetCells) break;
            }
        }

        return domain;
    }

    private static double FindSeaLevel(float[] heightMap, double targetFraction)
    {
        double lo = -1, hi = 1;
        var total = heightMap.Length;
        for (var i = 0; i < 32; i++)
        {
            var mid = (lo + hi) / 2;
            var below = 0;
            for (var j = 0; j < total; j++) if (heightMap[j] <= mid) below++;
            if ((double)below / total < targetFraction) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    private static void NormaliseHeightMap(float[] heightMap, double seaLevel)
    {
        for (var i = 0; i < heightMap.Length; i++)
            heightMap[i] -= (float)seaLevel;
    }

    private static void ClassifyPlatesAndCrust(List<PlateInfo> plates, SimulationState state, Xoshiro256ss rng)
    {
        var cellCount = state.CellCount;
        var sumH = new double[plates.Count];
        var countH = new double[plates.Count];

        for (var i = 0; i < cellCount; i++)
        {
            sumH[state.PlateMap[i]] += state.HeightMap[i];
            countH[state.PlateMap[i]]++;
        }
        foreach (var plate in plates)
            plate.IsOceanic = countH[plate.Id] > 0 && sumH[plate.Id] / countH[plate.Id] < 0;

        for (var i = 0; i < cellCount; i++)
        {
            var plate = plates[state.PlateMap[i]];
            if (plate.IsOceanic)
            {
                state.CrustThicknessMap[i] = 7;
                state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
            }
            else
            {
                state.CrustThicknessMap[i] = 35;
                state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
            }
            state.RockAgeMap[i] = (float)rng.NextFloat(100, 4000);
        }

        // Convert noise-unit heights to metre-scale by applying isostatic equilibrium.
        // Use the sign of each cell's noise height (positive = above sea level = continental crust,
        // negative = below sea level = oceanic crust) so that the correct equilibrium depth is
        // applied even when most plates ended up with average heights < 0.
        // The noise perturbation (±NoiseAmplitudeM) preserves topographic variation within each domain.
        // Isostatic ratio matches TectonicEngine.ApplyIsostasy constant (2.7/3.3).
        const double isostaticRatio   = 2.7 / 3.3;
        const double oceanicCrustKm   = 7.0;    // typical oceanic crust thickness (km)
        const double continentalCrustKm = 35.0; // typical continental crust thickness (km)
        const double kmToM            = 1000.0; // km → m conversion
        const double isostaticOffsetM = 4500.0; // baseline depth correction for isostasy (m)
        const double noiseAmplitudeM  = 500.0;  // noise perturbation amplitude (m)
        for (var i = 0; i < cellCount; i++)
        {
            var h = state.HeightMap[i]; // normalized noise: negative = ocean, positive = land
            var crustKm = h < 0 ? oceanicCrustKm : continentalCrustKm;
            var eq = crustKm * kmToM * (1 - isostaticRatio) - isostaticOffsetM;
            state.HeightMap[i] = (float)(eq + h * noiseAmplitudeM);
            // Correct crust thickness to match cell type for physical consistency
            state.CrustThicknessMap[i] = (float)crustKm;
        }
    }

    /// <summary>
    /// Post-generation safety net: for each elevation-connected land component, if a
    /// dominant plate holds more than <see cref="LandCoherenceDominantFraction"/> of
    /// cells, reassign outlier plates to that dominant plate. Coastal outliers
    /// (touching ocean) are lowered to lowland transition elevation.
    /// Mirrors <c>TectonicEngine.ConsolidatePlateFragments</c> but keyed on land
    /// components rather than plate fragments.
    /// </summary>
    internal const double LandCoherenceDominantFraction = 0.70;

    internal static void ConsolidateLandPlateCoherence(SimulationState state)
    {
        var gs = state.GridSize;
        var cc = state.CellCount;
        var plateMap = state.PlateMap;
        var heightMap = state.HeightMap;

        var visited = new bool[cc];
        var component = new List<int>(256);
        var plateCounts = new Dictionary<ushort, int>();
        var queue = new Queue<int>();

        for (var start = 0; start < cc; start++)
        {
            if (visited[start] || heightMap[start] <= 0) continue;

            component.Clear();
            plateCounts.Clear();
            queue.Clear();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                component.Add(cell);

                var plate = plateMap[cell];
                plateCounts.TryGetValue(plate, out var count);
                plateCounts[plate] = count + 1;

                var row = cell / gs;
                var col = cell % gs;
                EnqueueLandNeighbor(row - 1, col);
                EnqueueLandNeighbor(row + 1, col);
                EnqueueLandNeighbor(row, (col - 1 + gs) % gs);
                EnqueueLandNeighbor(row, (col + 1) % gs);
            }

            if (component.Count == 0) continue;

            ushort dominantPlate = 0;
            var dominantCount = 0;
            foreach (var (plate, n) in plateCounts)
            {
                if (n <= dominantCount) continue;
                dominantCount = n;
                dominantPlate = plate;
            }

            if ((double)dominantCount / component.Count < LandCoherenceDominantFraction)
                continue;

            foreach (var cell in component)
            {
                if (plateMap[cell] == dominantPlate) continue;

                if (TouchesOcean(cell, gs, heightMap))
                    heightMap[cell] = 50f;

                plateMap[cell] = dominantPlate;
            }
        }

        void EnqueueLandNeighbor(int row, int col)
        {
            if (row < 0 || row >= gs) return;
            var i = row * gs + col;
            if (visited[i] || heightMap[i] <= 0) return;
            visited[i] = true;
            queue.Enqueue(i);
        }
    }

    private static bool TouchesOcean(int cell, int gs, float[] heightMap)
    {
        var row = cell / gs;
        var col = cell % gs;

        if (row > 0 && heightMap[(row - 1) * gs + col] <= 0) return true;
        if (row < gs - 1 && heightMap[(row + 1) * gs + col] <= 0) return true;
        if (heightMap[row * gs + (col - 1 + gs) % gs] <= 0) return true;
        if (heightMap[row * gs + (col + 1) % gs] <= 0) return true;
        return false;
    }

    private static List<HotspotInfo> GenerateHotspots(Xoshiro256ss rng)
    {
        var count = rng.NextInt(2, 5);
        var hotspots = new List<HotspotInfo>();
        for (var i = 0; i < count; i++)
        {
            hotspots.Add(new HotspotInfo
            {
                Lat = Math.Asin(rng.NextFloat(-1, 1)) / DEG2RAD,
                Lon = rng.NextFloat(-180, 180),
                Strength = rng.NextFloat(0.5, 1),
            });
        }
        return hotspots;
    }
}
