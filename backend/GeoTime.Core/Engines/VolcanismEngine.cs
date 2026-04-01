using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>
/// Models volcanic eruptions at subduction arcs, mid-ocean ridges, and hotspots.
/// </summary>
public sealed class VolcanismEngine
{
    private const double TWO_PI = 2 * Math.PI;
    private const double DEG2RAD = Math.PI / 180;

    private static double RowToLat(int row, int gs) => Math.PI / 2 - (double)row / gs * Math.PI;
    private static double ColToLon(int col, int gs) => (double)col / gs * TWO_PI - Math.PI;

    public static List<EruptionRecord> Tick(double timeMa, double deltaMa,
        List<BoundaryCell> boundaries, List<HotspotInfo> hotspots,
        List<PlateInfo> plates, SimulationState state,
        StratigraphyStack stratigraphy, Xoshiro256ss rng)
    {
        var eruptions = new List<EruptionRecord>();
        ProcessSubduction(timeMa, deltaMa, boundaries, plates, state, stratigraphy, rng, eruptions);
        ProcessRidge(timeMa, deltaMa, boundaries, plates, state, stratigraphy, rng, eruptions);
        ProcessHotspot(timeMa, deltaMa, hotspots, state, stratigraphy, rng, eruptions);
        return eruptions;
    }

    private static void ProcessSubduction(double timeMa, double deltaMa,
        List<BoundaryCell> boundaries, List<PlateInfo> plates, SimulationState state,
        StratigraphyStack stratigraphy, Xoshiro256ss rng, List<EruptionRecord> eruptions)
    {
        var gs = state.GridSize;
        var prob = Math.Min(0.02 * deltaMa, 0.5);
        foreach (var b in boundaries.Where(b => b.Type == BoundaryType.CONVERGENT)
                     .Where(b => plates[b.Plate1].IsOceanic || plates[b.Plate2].IsOceanic)
                     .Where(b => !(rng.Next() > prob)))
        {
            int row = b.CellIndex / gs, col = b.CellIndex % gs;
            double lat = RowToLat(row, gs) / DEG2RAD, lon = ColToLon(col, gs) / DEG2RAD;
            var intensity = 0.3 + rng.Next() * 0.7;
            var rockType = rng.Next() > 0.5 ? RockType.IGN_ANDESITE : RockType.IGN_DACITE;
            var heightAdded = intensity * 200 * deltaMa;
            var co2 = intensity * 0.1 * deltaMa;

            state.HeightMap[b.CellIndex] += (float)heightAdded;
            state.CrustThicknessMap[b.CellIndex] += (float)(heightAdded / 1000);

            stratigraphy.PushLayer(b.CellIndex, new StratigraphicLayer
            {
                RockType = rockType, AgeDeposited = timeMa, Thickness = heightAdded,
                DipAngle = 5 + rng.Next() * 15, DipDirection = rng.NextFloat(0, 360),
            });

            eruptions.Add(new EruptionRecord
            {
                CellIndex = b.CellIndex, VolcanoType = VolcanoType.STRATOVOLCANO,
                Lat = lat, Lon = lon, Intensity = intensity, HeightAdded = heightAdded,
                RockType = rockType, CO2Degassed = co2, SO2Degassed = intensity * 0.05 * deltaMa,
            });
        }
    }

    private static void ProcessRidge(double timeMa, double deltaMa,
        List<BoundaryCell> boundaries, List<PlateInfo> plates, SimulationState state,
        StratigraphyStack stratigraphy, Xoshiro256ss rng, List<EruptionRecord> eruptions)
    {
        var gs = state.GridSize;
        var prob = Math.Min(0.05 * deltaMa, 0.8);
        foreach (var b in boundaries.Where(b => b.Type == BoundaryType.DIVERGENT)
                     .Where(b => plates[b.Plate1].IsOceanic && plates[b.Plate2].IsOceanic)
                     .Where(b => !(rng.Next() > prob)))
        {
            int row = b.CellIndex / gs, col = b.CellIndex % gs;
            double lat = RowToLat(row, gs) / DEG2RAD, lon = ColToLon(col, gs) / DEG2RAD;
            var intensity = 0.1 + rng.Next() * 0.3;
            var heightAdded = intensity * 50 * deltaMa;

            state.HeightMap[b.CellIndex] += (float)heightAdded;
            stratigraphy.PushLayer(b.CellIndex, new StratigraphicLayer
            {
                RockType = RockType.IGN_PILLOW_BASALT, AgeDeposited = timeMa, Thickness = heightAdded,
            });

            eruptions.Add(new EruptionRecord
            {
                CellIndex = b.CellIndex, VolcanoType = VolcanoType.SUBMARINE_RIDGE,
                Lat = lat, Lon = lon, Intensity = intensity, HeightAdded = heightAdded,
                RockType = RockType.IGN_PILLOW_BASALT, CO2Degassed = intensity * 0.02 * deltaMa,
            });
        }
    }

    private static void ProcessHotspot(double timeMa, double deltaMa,
        List<HotspotInfo> hotspots, SimulationState state,
        StratigraphyStack stratigraphy, Xoshiro256ss rng, List<EruptionRecord> eruptions)
    {
        var gs = state.GridSize;
        foreach (var hs in hotspots.Select(hs => new { hs, prob = Math.Min(0.1 * hs.Strength * deltaMa, 0.9) })
                     .Where(@t => !(rng.Next() > @t.prob))
                     .Select(@t => @t.hs))
        {
            double latRad = hs.Lat * DEG2RAD, lonRad = hs.Lon * DEG2RAD;
            var row = Math.Clamp((int)Math.Round((Math.PI / 2 - latRad) / Math.PI * gs), 0, gs - 1);
            var col = Math.Clamp((int)Math.Round((lonRad + Math.PI) / TWO_PI * gs), 0, gs - 1);
            var ci = row * gs + col;

            var isOceanic = state.HeightMap[ci] < 0;
            var intensity = hs.Strength * (0.5 + rng.Next() * 0.5);
            var heightAdded = intensity * 150 * deltaMa;

            state.HeightMap[ci] += (float)heightAdded;
            state.CrustThicknessMap[ci] += (float)(heightAdded / 1000);

            stratigraphy.PushLayer(ci, new StratigraphicLayer
            {
                RockType = RockType.IGN_BASALT, AgeDeposited = timeMa, Thickness = heightAdded,
                DipAngle = 2 + rng.Next() * 5, DipDirection = rng.NextFloat(0, 360),
            });

            eruptions.Add(new EruptionRecord
            {
                CellIndex = ci,
                VolcanoType = isOceanic ? VolcanoType.SUBMARINE_RIDGE : VolcanoType.SHIELD,
                Lat = hs.Lat, Lon = hs.Lon, Intensity = intensity, HeightAdded = heightAdded,
                RockType = RockType.IGN_BASALT, CO2Degassed = intensity * 0.05 * deltaMa,
            });
        }
    }

    public static (double co2, double so2) TotalDegassing(List<EruptionRecord> eruptions)
    {
        double co2 = 0, so2 = 0;
        foreach (var e in eruptions) { co2 += e.CO2Degassed; so2 += e.SO2Degassed; }
        return (co2, so2);
    }
}
