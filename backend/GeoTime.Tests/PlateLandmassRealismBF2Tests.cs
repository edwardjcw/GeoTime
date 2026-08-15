using GeoTime.Core;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Tests;

/// <summary>BF-2 — Continent coalescence after plate-tectonics onset.</summary>
public class PlateLandmassRealismBF2Tests
{
    private const double EarthRadiusKm = 6371.0;
    private const float ContinentMinKm2 = 1_000_000f;

    private static float CellAreaKm2(int ci, int gs)
    {
        var row = ci / gs;
        var lat = 90.0 - (double)row / gs * 180.0;
        var latRad = lat * Math.PI / 180.0;
        var dLatRad = Math.PI / gs;
        var dLonRad = 2 * Math.PI / gs;
        return (float)(EarthRadiusKm * EarthRadiusKm * dLatRad * dLonRad * Math.Abs(Math.Cos(latRad)));
    }

    private static List<(int cells, float km2)> FindLandComponents(SimulationState state)
    {
        var gs = state.GridSize;
        var visited = new bool[state.CellCount];
        var result = new List<(int, float)>();

        for (var start = 0; start < state.CellCount; start++)
        {
            if (visited[start] || state.HeightMap[start] <= 0) continue;

            var cells = 0;
            float km2 = 0;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                cells++;
                km2 += CellAreaKm2(cell, gs);
                var row = cell / gs;
                var col = cell % gs;

                Try(row - 1, col);
                Try(row + 1, col);
                Try(row, (col - 1 + gs) % gs);
                Try(row, (col + 1) % gs);

                void Try(int r, int c)
                {
                    if (r < 0 || r >= gs) return;
                    var i = r * gs + c;
                    if (visited[i] || state.HeightMap[i] <= 0) return;
                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }

            result.Add((cells, km2));
        }

        return result;
    }

    private static double DominantPlateFraction(IReadOnlyList<int> cells, ushort[] plateMap)
    {
        var counts = new Dictionary<ushort, int>();
        foreach (var cell in cells)
        {
            counts.TryGetValue(plateMap[cell], out var n);
            counts[plateMap[cell]] = n + 1;
        }
        return (double)counts.Values.Max() / cells.Count;
    }

    private static List<List<int>> FindLandComponentCells(SimulationState state)
    {
        var gs = state.GridSize;
        var visited = new bool[state.CellCount];
        var components = new List<List<int>>();

        for (var start = 0; start < state.CellCount; start++)
        {
            if (visited[start] || state.HeightMap[start] <= 0) continue;
            var cells = new List<int>();
            var q = new Queue<int>();
            q.Enqueue(start);
            visited[start] = true;
            while (q.Count > 0)
            {
                var cell = q.Dequeue();
                cells.Add(cell);
                var row = cell / gs;
                var col = cell % gs;
                void Try(int r, int c)
                {
                    if (r < 0 || r >= gs) return;
                    var i = r * gs + c;
                    if (visited[i] || state.HeightMap[i] <= 0) return;
                    visited[i] = true;
                    q.Enqueue(i);
                }
                Try(row - 1, col);
                Try(row + 1, col);
                Try(row, (col - 1 + gs) % gs);
                Try(row, (col + 1) % gs);
            }
            components.Add(cells);
        }

        return components;
    }

    [Fact]
    public void BF2_OnsetTransition_RepartitionsCrossPlateLand_ToPlateCoherentTerranes()
    {
        var config = new SimulationPlanetConfig(); // default onset -3800; INITIAL_TIME -4500 → pre-onset gen
        Assert.False(config.IsPlateTectonicsActive(SimClock.INITIAL_TIME));

        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42, config);

        // Pre-onset noise land may span multiple plates.
        var preComponents = FindLandComponentCells(sim.State);
        Assert.NotEmpty(preComponents);

        // Jump past onset so the one-time transition runs before advection.
        sim.AdvanceSimulation(720); // -4500 → -3780
        Assert.True(sim.Clock.T >= config.PlateTectonicsOnsetMa);
        Assert.Contains(sim.EventLog.GetAll(), e => e.Type == "PLATE_TECTONICS_ONSET");

        var postComponents = FindLandComponentCells(sim.State);
        Assert.NotEmpty(postComponents);

        foreach (var component in postComponents)
        {
            if (component.Count < 4) continue; // ignore tiny coastal flecks
            var dominant = DominantPlateFraction(component, sim.State.PlateMap);
            Assert.True(
                dominant >= 0.90,
                $"Post-onset land component of {component.Count} cells is only {dominant:P1} plate-coherent");
        }
    }

    [Fact]
    public void BF2_DefaultStart_After500MaPostOnset_HasContinentScaleLand()
    {
        // Diagnosis (128-grid, seed 42): at t≈-3000 collapsed to 1×≥1M (max 1.9M), then 0 by -2500.
        // Acceptance: ~500 Ma post-onset on a modest test grid keeps meaningful continent-scale land.
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42, new SimulationPlanetConfig());

        var target = -3300.0; // 500 Ma after default onset -3800
        while (sim.Clock.T < target)
        {
            var step = Math.Min(25.0, target - sim.Clock.T);
            sim.AdvanceSimulation(step);
        }

        var components = FindLandComponents(sim.State);
        Assert.NotEmpty(components);

        var continentScale = components.Count(c => c.km2 >= ContinentMinKm2);
        var largestKm2 = components.Max(c => c.km2);
        var totalLandKm2 = components.Sum(c => c.km2);

        Assert.True(
            continentScale >= 2 || largestKm2 >= 2_000_000f,
            $"Expected ≥2 components ≥1M km² or largest ≥2M km² after 500 Ma post-onset; " +
            $"got continentScale={continentScale}, largest={largestKm2:F0} km², totalLand={totalLandKm2:F0} km², " +
            $"components={components.Count}");
    }

    [Fact]
    public void BF2_ContinentalCollision_FillsOceanGap_MergesElevationComponent()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        var log = new EventLog();

        // Two continental blocks separated by a mid-longitude ocean strip.
        // Keep col 0 / gs-1 oceanic so lon-wrap does not join the two blocks.
        // Single-cell gap at col 15 so welding can bridge plate0 land ↔ plate1 land.
        for (var i = 0; i < state.CellCount; i++)
        {
            var col = i % gs;
            var inLeftBlock = col is >= 5 and <= 14;
            var inRightBlock = col is >= 16 and <= 25;
            var inGap = col == 15;

            if (inLeftBlock)
            {
                state.PlateMap[i] = 0;
                state.HeightMap[i] = 800f;
                state.CrustThicknessMap[i] = 35f;
                state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
                state.TerraneMap[i] = 1;
            }
            else if (inRightBlock)
            {
                state.PlateMap[i] = 1;
                state.HeightMap[i] = 800f;
                state.CrustThicknessMap[i] = 35f;
                state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
                state.TerraneMap[i] = 2;
            }
            else
            {
                state.PlateMap[i] = (ushort)(col < 16 ? 0 : 1);
                state.HeightMap[i] = -800f;
                state.CrustThicknessMap[i] = 7f;
                state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
                if (inGap)
                    state.PlateMap[i] = 0; // gap cell on plate 0, neighbor col16 is plate 1 → boundary
            }
            state.RockAgeMap[i] = 2000f;
        }

        var before = FindLandComponents(state);
        Assert.True(before.Count >= 2, $"Setup must start as separate landmasses (got {before.Count})");

        var plates = new List<PlateInfo>
        {
            new()
            {
                Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = false, Area = 0.5,
                // Opposite sense to Rate on plate 1 so the mid-lon contact is convergent.
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = -8 },
            },
            new()
            {
                Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = 8 },
            },
        };

        var engine = new TectonicEngine(new EventBus(), log, 7, 0.1);
        engine.PlateTectonicsOnsetMa = -5000;
        engine.Initialize(plates, [], new AtmosphericComposition(), state);

        // Short ticks keep advection from destroying the synthetic gap before welding runs.
        for (var step = 0; step < 6; step++)
            engine.Tick(-3000 + step * 2, 2.0);

        var after = FindLandComponents(state);
        var largestAfter = after.Max(c => c.cells);
        var largestBefore = before.Max(c => c.cells);

        Assert.True(
            after.Count < before.Count || largestAfter > largestBefore * 1.4,
            $"Collision welding should merge or grow land components; before={before.Count}/{largestBefore}, after={after.Count}/{largestAfter}");
        Assert.Contains(state.SutureMap, s => s > 0);
    }

    [Fact]
    public void BF2_P2Gen_TerraneScale_LargerThanLegacySkipBand()
    {
        // With raised terrane parameters, post-onset generation should plant substantial continental mask.
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 };
        var state = new SimulationState(64);
        new PlanetGenerator(99).Generate(state, config);

        var landCells = state.HeightMap.Count(h => h > 0);
        var landFraction = (double)landCells / state.CellCount;

        // ~70% ocean target ⇒ ~30% land; with larger terranes expect a healthy land fraction.
        Assert.True(landFraction >= 0.18, $"Expected land fraction ≥18%, got {landFraction:P1}");

        var continents = FindLandComponents(state).Count(c => c.km2 >= ContinentMinKm2);
        Assert.True(continents >= 3, $"Expected ≥3 continent-scale components at P2 gen, got {continents}");
    }

    [Fact]
    public void BF2_OnsetTransition_IsDeterministic()
    {
        static float[] RunHeights()
        {
            var state = new SimulationState(32);
            var config = new SimulationPlanetConfig(); // pre-onset gen
            var result = new PlanetGenerator(55).Generate(state, config);
            PlanetGenerator.ApplyPlateTectonicsOnsetTransition(state, result.Plates, 55);
            return (float[])state.HeightMap.Clone();
        }

        var a = RunHeights();
        var b = RunHeights();
        Assert.Equal(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++)
            Assert.Equal(a[i], b[i]);
    }
}
