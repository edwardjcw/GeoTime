using GeoTime.Core.Models;

namespace GeoTime.Core.Proc;

/// <summary>
/// Reconciles antimeridian neighbors (column 0 vs column gs-1) so the periodic
/// longitude wrap does not leave a hard HeightMap / plate cliff at the UV seam.
/// </summary>
/// <remarks>
/// Strategy (per row):
/// <list type="bullet">
/// <item>Different plate IDs: leave both cells unchanged (preserve intentional
/// subduction / rift trenches that happen to sit on the antimeridian).</item>
/// <item>Same plate, both land or both ocean: average height and crust thickness;
/// unify rock age; keep rock type / terrane from the higher-|height| cell when they differ.</item>
/// <item>Same plate, land/ocean cliff: prefer land continuity — copy height and
/// crust/rock/terrane attributes from the land cell onto the ocean cell.</item>
/// </list>
/// Climate temperature is not written here; it follows HeightMap on the next climate tick.
/// </remarks>
public static class GridSeamSync
{
    /// <summary>Sea-level threshold separating land from ocean (metres).</summary>
    public const float SeaLevelM = 0f;

    /// <summary>
    /// Soften antimeridian seam pairs on <paramref name="state"/> in place.
    /// </summary>
    public static void SyncAntimeridian(SimulationState state)
    {
        var gs = state.GridSize;
        if (gs < 2) return;

        var lastCol = gs - 1;
        for (var row = 0; row < gs; row++)
        {
            var i0 = row * gs;
            var i1 = row * gs + lastCol;
            ReconcilePair(state, i0, i1);
        }
    }

    private static void ReconcilePair(SimulationState state, int a, int b)
    {
        if (state.PlateMap[a] != state.PlateMap[b])
            return;

        var ha = state.HeightMap[a];
        var hb = state.HeightMap[b];
        var landA = ha > SeaLevelM;
        var landB = hb > SeaLevelM;

        if (landA == landB)
        {
            AverageSameDomain(state, a, b);
            return;
        }

        // Same plate, land/ocean cliff → prefer land continuity.
        var land = landA ? a : b;
        var ocean = landA ? b : a;
        CopyLandOnto(state, land, ocean);
    }

    private static void AverageSameDomain(SimulationState state, int a, int b)
    {
        // Capture donor before averaging heights (both become equal afterward).
        var donor = Math.Abs(state.HeightMap[a]) >= Math.Abs(state.HeightMap[b]) ? a : b;

        var avgH = 0.5f * (state.HeightMap[a] + state.HeightMap[b]);
        state.HeightMap[a] = avgH;
        state.HeightMap[b] = avgH;

        var avgCrust = 0.5f * (state.CrustThicknessMap[a] + state.CrustThicknessMap[b]);
        state.CrustThicknessMap[a] = avgCrust;
        state.CrustThicknessMap[b] = avgCrust;

        var avgAge = 0.5f * (state.RockAgeMap[a] + state.RockAgeMap[b]);
        state.RockAgeMap[a] = avgAge;
        state.RockAgeMap[b] = avgAge;

        if (state.RockTypeMap[a] != state.RockTypeMap[b])
        {
            state.RockTypeMap[a] = state.RockTypeMap[donor];
            state.RockTypeMap[b] = state.RockTypeMap[donor];
        }

        if (state.TerraneMap[a] != state.TerraneMap[b])
        {
            // Prefer non-zero terrane when one side has provenance.
            var terrane = state.TerraneMap[a] != 0 ? state.TerraneMap[a]
                : state.TerraneMap[b] != 0 ? state.TerraneMap[b]
                : state.TerraneMap[donor];
            state.TerraneMap[a] = terrane;
            state.TerraneMap[b] = terrane;
        }
    }

    private static void CopyLandOnto(SimulationState state, int land, int ocean)
    {
        state.HeightMap[ocean] = state.HeightMap[land];
        state.CrustThicknessMap[ocean] = state.CrustThicknessMap[land];
        state.RockTypeMap[ocean] = state.RockTypeMap[land];
        state.RockAgeMap[ocean] = state.RockAgeMap[land];
        state.TerraneMap[ocean] = state.TerraneMap[land];
        // PlateMap already matches; SutureMap left alone (weld markers are intentional).
    }
}
