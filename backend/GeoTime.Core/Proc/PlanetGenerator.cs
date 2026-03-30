using GeoTime.Core.Models;

namespace GeoTime.Core.Proc;

/// <summary>
/// Generates initial conditions for a terrestrial planet: tectonic plates,
/// heightfield, crust properties, mantle plume hotspots, and atmosphere.
/// </summary>
public sealed class PlanetGenerator
{
    private const double TWO_PI = 2 * Math.PI;
    private const double DEG2RAD = Math.PI / 180.0;

    private readonly uint _seed;

    public PlanetGenerator(uint seed) => _seed = seed;

    public PlanetGeneratorResult Generate(SimulationState state)
    {
        var rng = new Xoshiro256ss(_seed);
        var noise = new SimplexNoise(rng);
        int gs = state.GridSize;

        int numPlates = rng.NextInt(10, 16);
        var plates = GeneratePlates(rng, numPlates, state);
        GenerateHeightMap(noise, state);
        double seaLevel = FindSeaLevel(state.HeightMap, 0.70);
        NormaliseHeightMap(state.HeightMap, seaLevel);
        ClassifyPlatesAndCrust(plates, state, rng);
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
            Seed = _seed,
        };
    }

    private static double RowToLat(int row, int gs) => Math.PI / 2 - (double)row / gs * Math.PI;
    private static double ColToLon(int col, int gs) => (double)col / gs * TWO_PI - Math.PI;

    private static (double x, double y, double z) LatLonToXYZ(double lat, double lon)
    {
        double cosLat = Math.Cos(lat);
        return (cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
    }

    private static double GreatCircleDist(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = lat2 - lat1, dLon = lon2 - lon1;
        double a = Math.Pow(Math.Sin(dLat / 2), 2)
                 + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private List<PlateInfo> GeneratePlates(Xoshiro256ss rng, int numPlates, SimulationState state)
    {
        int gs = state.GridSize;
        int cellCount = gs * gs;
        var centersLat = new double[numPlates];
        var centersLon = new double[numPlates];

        for (int p = 0; p < numPlates; p++)
        {
            centersLat[p] = Math.Asin(rng.NextFloat(-1, 1));
            centersLon[p] = rng.NextFloat(-Math.PI, Math.PI);
        }

        // Lloyd relaxation — 3 iterations
        for (int iter = 0; iter < 3; iter++)
        {
            for (int row = 0; row < gs; row++)
            {
                double lat = RowToLat(row, gs);
                for (int col = 0; col < gs; col++)
                {
                    double lon = ColToLon(col, gs);
                    int bestPlate = 0;
                    double bestDist = double.PositiveInfinity;
                    for (int p = 0; p < numPlates; p++)
                    {
                        double d = GreatCircleDist(lat, lon, centersLat[p], centersLon[p]);
                        if (d < bestDist) { bestDist = d; bestPlate = p; }
                    }
                    state.PlateMap[row * gs + col] = (ushort)bestPlate;
                }
            }

            var sumX = new double[numPlates];
            var sumY = new double[numPlates];
            var sumZ = new double[numPlates];
            var count = new double[numPlates];

            for (int row = 0; row < gs; row++)
            {
                double lat = RowToLat(row, gs);
                for (int col = 0; col < gs; col++)
                {
                    double lon = ColToLon(col, gs);
                    int p = state.PlateMap[row * gs + col];
                    var (cx, cy, cz) = LatLonToXYZ(lat, lon);
                    sumX[p] += cx; sumY[p] += cy; sumZ[p] += cz;
                    count[p]++;
                }
            }

            for (int p = 0; p < numPlates; p++)
            {
                if (count[p] == 0) continue;
                double mx = sumX[p] / count[p];
                double my = sumY[p] / count[p];
                double mz = sumZ[p] / count[p];
                double r = Math.Sqrt(mx * mx + my * my + mz * mz);
                if (r < 1e-12) continue;
                centersLat[p] = Math.Asin(Math.Clamp(mz / r, -1, 1));
                centersLon[p] = Math.Atan2(my, mx);
            }
        }

        // Final plate assignment
        for (int row = 0; row < gs; row++)
        {
            double lat = RowToLat(row, gs);
            for (int col = 0; col < gs; col++)
            {
                double lon = ColToLon(col, gs);
                int bestPlate = 0;
                double bestDist = double.PositiveInfinity;
                for (int p = 0; p < numPlates; p++)
                {
                    double d = GreatCircleDist(lat, lon, centersLat[p], centersLon[p]);
                    if (d < bestDist) { bestDist = d; bestPlate = p; }
                }
                state.PlateMap[row * gs + col] = (ushort)bestPlate;
            }
        }

        var areaCount = new double[numPlates];
        for (int i = 0; i < cellCount; i++) areaCount[state.PlateMap[i]]++;

        var plates = new List<PlateInfo>();
        for (int p = 0; p < numPlates; p++)
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

    private void GenerateHeightMap(SimplexNoise noise, SimulationState state)
    {
        int gs = state.GridSize;
        const double scale = 3.0;
        for (int row = 0; row < gs; row++)
        {
            double lat = RowToLat(row, gs);
            for (int col = 0; col < gs; col++)
            {
                double lon = ColToLon(col, gs);
                var (nx, ny, nz) = LatLonToXYZ(lat, lon);
                state.HeightMap[row * gs + col] = (float)noise.Fbm(nx * scale, ny * scale, nz * scale, 4);
            }
        }
    }

    private static double FindSeaLevel(float[] heightMap, double targetFraction)
    {
        double lo = -1, hi = 1;
        int total = heightMap.Length;
        for (int i = 0; i < 32; i++)
        {
            double mid = (lo + hi) / 2;
            int below = 0;
            for (int j = 0; j < total; j++) if (heightMap[j] <= mid) below++;
            if ((double)below / total < targetFraction) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    private static void NormaliseHeightMap(float[] heightMap, double seaLevel)
    {
        for (int i = 0; i < heightMap.Length; i++)
            heightMap[i] -= (float)seaLevel;
    }

    private static void ClassifyPlatesAndCrust(List<PlateInfo> plates, SimulationState state, Xoshiro256ss rng)
    {
        int cellCount = state.CellCount;
        var sumH = new double[plates.Count];
        var countH = new double[plates.Count];

        for (int i = 0; i < cellCount; i++)
        {
            sumH[state.PlateMap[i]] += state.HeightMap[i];
            countH[state.PlateMap[i]]++;
        }
        foreach (var plate in plates)
            plate.IsOceanic = countH[plate.Id] > 0 && sumH[plate.Id] / countH[plate.Id] < 0;

        for (int i = 0; i < cellCount; i++)
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
    }

    private static List<HotspotInfo> GenerateHotspots(Xoshiro256ss rng)
    {
        int count = rng.NextInt(2, 5);
        var hotspots = new List<HotspotInfo>();
        for (int i = 0; i < count; i++)
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
