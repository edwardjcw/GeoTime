using GeoTime.Core;
using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Tests;

/// <summary>P2 — Plate-coupled continental crust generation.</summary>
public class PlateLandmassRealismP2Tests
{
    private const double LandCoherenceThreshold = 0.95;
    private const double BoundaryOceanicFraction = 0.80;

    private static SimulationState GenerateWithConfig(uint seed, int gridSize, SimulationPlanetConfig config)
    {
        var state = new SimulationState(gridSize);
        new PlanetGenerator(seed).Generate(state, config);
        return state;
    }

    private static List<List<int>> FindLandComponents(SimulationState state)
    {
        var gs = state.GridSize;
        var visited = new bool[state.CellCount];
        var components = new List<List<int>>();

        for (var start = 0; start < state.CellCount; start++)
        {
            if (visited[start] || state.HeightMap[start] <= 0) continue;

            var cells = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                cells.Add(cell);
                var row = cell / gs;
                var col = cell % gs;

                if (row > 0) TryEnqueue(row - 1, col);
                if (row < gs - 1) TryEnqueue(row + 1, col);
                TryEnqueue(row, (col - 1 + gs) % gs);
                TryEnqueue(row, (col + 1) % gs);

                void TryEnqueue(int r, int c)
                {
                    var i = r * gs + c;
                    if (visited[i] || state.HeightMap[i] <= 0) return;
                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }

            if (cells.Count > 0)
                components.Add(cells);
        }

        return components;
    }

    private static double DominantPlateFraction(IReadOnlyList<int> cells, ushort[] plateMap)
    {
        var counts = new Dictionary<ushort, int>();
        foreach (var cell in cells)
        {
            var plate = plateMap[cell];
            counts.TryGetValue(plate, out var n);
            counts[plate] = n + 1;
        }

        return (double)counts.Values.Max() / cells.Count;
    }

    private static List<int> GetBoundaryCells(ushort[] plateMap, int gs)
    {
        var boundaries = new List<int>();
        for (var row = 0; row < gs; row++)
        {
            for (var col = 0; col < gs; col++)
            {
                var i = row * gs + col;
                var plateId = plateMap[i];
                var isBoundary = false;

                if (row > 0 && plateMap[(row - 1) * gs + col] != plateId) isBoundary = true;
                if (row < gs - 1 && plateMap[(row + 1) * gs + col] != plateId) isBoundary = true;
                if (plateMap[row * gs + (col - 1 + gs) % gs] != plateId) isBoundary = true;
                if (plateMap[row * gs + (col + 1) % gs] != plateId) isBoundary = true;

                if (isBoundary) boundaries.Add(i);
            }
        }

        return boundaries;
    }

    [Fact]
    public void P2_PostOnset_LandComponentsArePlateCoherent()
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 };
        var state = GenerateWithConfig(42, 64, config);

        Assert.True(config.IsPlateTectonicsActive(SimClock.INITIAL_TIME));

        var landComponents = FindLandComponents(state);
        Assert.NotEmpty(landComponents);

        foreach (var component in landComponents)
        {
            var dominantFraction = DominantPlateFraction(component, state.PlateMap);
            Assert.True(
                dominantFraction >= LandCoherenceThreshold,
                $"Land component of {component.Count} cells has only {dominantFraction:P1} on dominant plate");
        }
    }

    [Fact]
    public void P2_PostOnset_PlateBoundariesArePredominantlyOceanic()
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 };
        var state = GenerateWithConfig(77, 64, config);
        var boundaries = GetBoundaryCells(state.PlateMap, state.GridSize);

        Assert.NotEmpty(boundaries);

        var oceanicCount = boundaries.Count(i => state.HeightMap[i] <= 0);
        var oceanicFraction = (double)oceanicCount / boundaries.Count;

        Assert.True(
            oceanicFraction >= BoundaryOceanicFraction,
            $"Only {oceanicFraction:P1} of plate-boundary cells are at or below sea level");
    }

    [Fact]
    public void P2_PreOnset_AllowsCrossPlateLandmasses()
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -3800 };
        Assert.False(config.IsPlateTectonicsActive(SimClock.INITIAL_TIME));

        var state = GenerateWithConfig(42, 64, config);
        var landComponents = FindLandComponents(state);
        Assert.NotEmpty(landComponents);

        var spansMultiplePlates = landComponents.Any(component =>
        {
            var plates = component.Select(i => state.PlateMap[i]).Distinct().Count();
            return plates > 1;
        });

        Assert.True(spansMultiplePlates, "Pre-onset noise crust should allow land spanning multiple plates");
    }

    [Fact]
    public void P2_PostOnset_ContinentalCrustStaysWithinPlateDomain()
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 };
        var state = GenerateWithConfig(123, 64, config);

        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.CrustThicknessMap[i] < 20) continue; // oceanic crust

            var plateId = state.PlateMap[i];
            var row = i / state.GridSize;
            var col = i % state.GridSize;
            var gs = state.GridSize;

            var touchesOtherPlate =
                (row > 0 && state.PlateMap[(row - 1) * gs + col] != plateId) ||
                (row < gs - 1 && state.PlateMap[(row + 1) * gs + col] != plateId) ||
                state.PlateMap[row * gs + (col - 1 + gs) % gs] != plateId ||
                state.PlateMap[row * gs + (col + 1) % gs] != plateId;

            // Continental interior should not sit on a plate boundary cell.
            if (!touchesOtherPlate) continue;

            Assert.True(
                state.HeightMap[i] <= 0,
                $"Continental-thickness cell {i} on plate boundary should be oceanic/lowland");
        }
    }

    [Fact]
    public void P2_PostOnset_IsDeterministic()
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 };
        var state1 = GenerateWithConfig(55, 64, config);
        var state2 = GenerateWithConfig(55, 64, config);

        for (var i = 0; i < state1.CellCount; i++)
            Assert.Equal(state1.HeightMap[i], state2.HeightMap[i]);
    }

    [Fact]
    public void P2_Orchestrator_PostOnset_UsesPlateCoupledGeneration()
    {
        var sim = new SimulationOrchestrator(64);
        sim.GeneratePlanet(42, new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 });

        var landComponents = FindLandComponents(sim.State);
        Assert.NotEmpty(landComponents);

        foreach (var component in landComponents)
        {
            var dominantFraction = DominantPlateFraction(component, sim.State.PlateMap);
            Assert.True(dominantFraction >= LandCoherenceThreshold);
        }
    }
}
