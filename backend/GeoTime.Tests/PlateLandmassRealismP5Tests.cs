using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Tests;

/// <summary>P5 — Post-generation land/plate coherence pass.</summary>
public class PlateLandmassRealismP5Tests
{
    private static SimulationState MakeLandComponentState(int gs = 16)
    {
        var state = new SimulationState(gs);
        for (var i = 0; i < state.CellCount; i++)
        {
            state.HeightMap[i] = -4000f;
            state.PlateMap[i] = 0;
        }

        // One elevation-connected land blob in the interior (rows 5-10, cols 5-10).
        for (var row = 5; row <= 10; row++)
        {
            for (var col = 5; col <= 10; col++)
            {
                var i = row * gs + col;
                state.HeightMap[i] = 500f;
                state.PlateMap[i] = 0;
            }
        }

        // Three outlier cells on plate 1 inside the same land component (33/36 ≈ 92% plate 0).
        state.PlateMap[5 * gs + 5] = 1;
        state.PlateMap[5 * gs + 6] = 1;
        state.PlateMap[6 * gs + 5] = 1;

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

                TryEnqueue(row - 1, col);
                TryEnqueue(row + 1, col);
                TryEnqueue(row, (col - 1 + gs) % gs);
                TryEnqueue(row, (col + 1) % gs);

                void TryEnqueue(int r, int c)
                {
                    if (r < 0 || r >= gs) return;
                    var i = r * gs + c;
                    if (visited[i] || state.HeightMap[i] <= 0) return;
                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }

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

    [Fact]
    public void P5_ReassignsLandOutliers_ToDominantPlate()
    {
        var state = MakeLandComponentState();
        var before = DominantPlateFraction(
            FindLandComponents(state).Single(),
            state.PlateMap);
        Assert.True(before < 1.0, "Fixture should start with cross-plate outliers");

        PlanetGenerator.ConsolidateLandPlateCoherence(state);

        var component = FindLandComponents(state).Single();
        Assert.Equal(1, component.Select(i => state.PlateMap[i]).Distinct().Count());
        Assert.All(component, i => Assert.Equal(0, state.PlateMap[i]));
    }

    [Fact]
    public void P5_SkipsEvenlySplitLandComponents()
    {
        var gs = 16;
        var state = new SimulationState(gs);
        for (var i = 0; i < state.CellCount; i++)
            state.HeightMap[i] = -4000f;

        // Checkerboard land on two plates — no plate reaches 70%.
        for (var row = 4; row <= 11; row++)
        {
            for (var col = 4; col <= 11; col++)
            {
                var i = row * gs + col;
                state.HeightMap[i] = 400f;
                state.PlateMap[i] = (ushort)(((row + col) & 1) == 0 ? 0 : 1);
            }
        }

        var original = (ushort[])state.PlateMap.Clone();
        PlanetGenerator.ConsolidateLandPlateCoherence(state);

        Assert.Equal(original, state.PlateMap);
        Assert.Equal(2, FindLandComponents(state).Single()
            .Select(i => state.PlateMap[i]).Distinct().Count());
    }

    [Fact]
    public void P5_CoastalOutliers_TransitionToLowland()
    {
        var gs = 16;
        var state = new SimulationState(gs);
        for (var i = 0; i < state.CellCount; i++)
        {
            state.HeightMap[i] = -4000f;
            state.PlateMap[i] = 0;
        }

        // Coastal strip of land with one outlier on plate 1 at the ocean edge.
        for (var col = 6; col <= 9; col++)
        {
            state.HeightMap[8 * gs + col] = 600f;
            state.PlateMap[8 * gs + col] = 0;
        }

        var outlier = 8 * gs + 6;
        state.PlateMap[outlier] = 1;
        Assert.True(state.HeightMap[outlier - gs] <= 0, "Fixture needs ocean neighbor above outlier");

        PlanetGenerator.ConsolidateLandPlateCoherence(state);

        Assert.Equal(0, state.PlateMap[outlier]);
        Assert.Equal(50f, state.HeightMap[outlier]);
    }

    [Fact]
    public void P5_PreOnsetGeneration_FixesDominantPlateOutliers()
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -3800 };
        var state = new SimulationState(64);
        new PlanetGenerator(901).Generate(state, config);

        var landComponents = FindLandComponents(state);
        Assert.NotEmpty(landComponents);

        foreach (var component in landComponents)
        {
            var dominantFraction = DominantPlateFraction(component, state.PlateMap);
            if (dominantFraction < PlanetGenerator.LandCoherenceDominantFraction)
                continue;

            Assert.Equal(1, component.Select(i => state.PlateMap[i]).Distinct().Count());
        }
    }

    [Fact]
    public void P5_PostOnsetGeneration_LandComponentsAreCoherent()
    {
        var config = new SimulationPlanetConfig { PlateTectonicsOnsetMa = -5000 };
        var state = new SimulationState(64);
        new PlanetGenerator(42).Generate(state, config);

        var landComponents = FindLandComponents(state);
        Assert.NotEmpty(landComponents);

        foreach (var component in landComponents)
        {
            var dominantFraction = DominantPlateFraction(component, state.PlateMap);
            Assert.True(
                dominantFraction >= 0.95,
                $"Land component of {component.Count} cells has only {dominantFraction:P1} on dominant plate");
        }
    }
}
