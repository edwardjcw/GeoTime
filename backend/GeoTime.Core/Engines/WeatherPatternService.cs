using GeoTime.Core.Models;

namespace GeoTime.Core.Engines;

/// <summary>
/// Computes long-term climatological weather patterns for a given month.
/// Returns per-cell data for: prevailing winds, ocean currents, jet streams,
/// frontal boundaries (ITCZ, polar front), and cyclone positions.
/// </summary>
public static class WeatherPatternService
{
    public static WeatherPatternResult ComputeMonthly(int month, SimulationState state)
    {
        month = Math.Clamp(month, 1, 12);

        var gs = state.GridSize;
        var cc = state.CellCount;

        // ITCZ seasonal latitude: peaks ~10°N in June, ~0° in Jan/Jul shoulder months.
        // itczLat = 10 * sin(2π*(month-3)/12) → +10 at month 6, −10 at month 12.
        double itczLat = 10.0 * Math.Sin(2.0 * Math.PI * (month - 3) / 12.0);

        // Atmospheric circulation cells shift half as much as the ITCZ.
        double cellShift = itczLat * 0.5;

        // nhWinterFactor: +1 in Jan (NH winter), −1 in Jul (SH winter).
        double nhWinterFactor = Math.Cos(2.0 * Math.PI * (month - 1) / 12.0);

        // Jet stream poleward shift in summer hemisphere (+poleward in NH summer).
        double jetShift = 5.0 * (-nhWinterFactor);

        var windU          = new float[cc];
        var windV          = new float[cc];
        var oceanCurrentU  = new float[cc];
        var oceanCurrentV  = new float[cc];
        var jetIntensity   = new float[cc];
        var frontIntensity = new float[cc];
        var frontType      = new int[cc];
        var cyclones       = new List<CyclonePosition>();

        for (int row = 0; row < gs; row++)
        {
            double lat    = 90.0 - (double)row / (gs - 1) * 180.0;
            double latRad = lat * Math.PI / 180.0;
            double absLat = Math.Abs(lat);

            // Latitude in the seasonally shifted circulation frame.
            double shiftedLat    = lat - cellShift;
            double absShiftedLat = Math.Abs(shiftedLat);
            double signShifted   = shiftedLat >= 0 ? 1.0 : -1.0;

            // Winter-strength factor for this hemisphere (0..1, 1 = peak winter).
            double localWinterFactor = lat >= 0
                ? (nhWinterFactor + 1.0) / 2.0
                : (1.0 - nhWinterFactor) / 2.0;

            for (int col = 0; col < gs; col++)
            {
                int    i   = row * gs + col;
                double lon = (double)col / gs * 360.0 - 180.0;
                double h   = state.HeightMap[i];
                bool isOcean = h < 0;

                // ── Prevailing winds ──────────────────────────────────────────────────
                // Convention: WindU westward-positive, WindV northward-positive.
                //   Trade winds (0-30°):   easterly → westward → +U, equatorward → −signShifted * V
                //   Westerlies (30-60°):   westerly → eastward → −U, poleward → +signShifted * V
                //   Polar easterlies(60+): easterly → +U, slight equatorward → −signShifted * V
                double u, v;

                if (absShiftedLat < 30.0)
                {
                    double strength = 2.0 + 5.0 * (1.0 - absShiftedLat / 30.0);
                    u = strength;                           // easterly → westward
                    v = -signShifted * strength * 0.3;     // equatorward convergence
                }
                else if (absShiftedLat < 60.0)
                {
                    double t        = (absShiftedLat - 30.0) / 30.0;
                    double strength = 3.0 + 5.0 * Math.Sin(t * Math.PI);
                    u = -strength;                          // westerlies → eastward
                    v = signShifted * strength * 0.2;       // poleward drift
                }
                else
                {
                    double strength = 2.0 + 2.0 * (absShiftedLat - 60.0) / 30.0;
                    u = strength;                           // polar easterlies → westward
                    v = -signShifted * strength * 0.2;     // equatorward
                }

                windU[i] = (float)u;
                windV[i] = (float)v;

                // ── Ocean currents ────────────────────────────────────────────────────
                if (isOcean)
                {
                    // Wind-driven surface layer (≈10 % of wind speed).
                    double cu = u * 0.1;
                    double cv = v * 0.1;

                    // Subtropical gyres centred ~25°N/S (clockwise NH, counter-clockwise SH).
                    if (absLat is > 10 and < 45)
                    {
                        double signLat     = lat >= 0 ? 1.0 : -1.0;
                        double gyreCenter  = signLat * 25.0;
                        double gyreStr     = 0.3 * Math.Exp(-Math.Pow((lat - gyreCenter) / 15.0, 2));
                        cu += -gyreStr * signLat * Math.Sin(latRad);
                        cv +=  gyreStr * 0.5 * Math.Cos((lat - gyreCenter) * Math.PI / 30.0);
                    }

                    // Gulf Stream / North Atlantic thermohaline: 30-55°N, 80-20°W.
                    if (lat is > 30 and < 55 && lon is > -80 and < -20)
                    {
                        double gsStr = 1.5
                            * Math.Exp(-Math.Pow((lon + 50.0) / 30.0, 2))   // centred at ~50°W
                            * Math.Exp(-Math.Pow((lat - 40.0) / 10.0, 2));  // centred at ~40°N
                        cu += -gsStr;           // flows eastward
                        cv +=  gsStr * 0.3;     // slight poleward component
                    }

                    // Antarctic Circumpolar Current (lat < −50°).
                    if (lat < -50)
                    {
                        double accStr = 1.0 + 0.5 * Math.Exp(-Math.Pow((lat + 60.0) / 10.0, 2));
                        cu += -accStr;          // strong eastward flow
                    }

                    oceanCurrentU[i] = (float)cu;
                    oceanCurrentV[i] = (float)cv;
                }

                // ── Jet streams ───────────────────────────────────────────────────────
                // Subtropical jet ~30°, polar jet ~58°; both shift poleward in summer.
                const double SubJetBase   = 30.0;
                const double PolarJetBase = 58.0;

                double signLat2 = lat >= 0 ? 1.0 : -1.0;
                double subJetCenter   = SubJetBase   + jetShift * signLat2;
                double polarJetCenter = PolarJetBase + jetShift * signLat2;

                double subJetDist   = Math.Abs(absLat - Math.Abs(subJetCenter));
                double polarJetDist = Math.Abs(absLat - Math.Abs(polarJetCenter));

                double subJetRaw   = Math.Exp(-Math.Pow(subJetDist   / 4.0, 2))
                                   * (0.3 + 0.4 * localWinterFactor);
                double polarJetRaw = Math.Exp(-Math.Pow(polarJetDist / 4.0, 2))
                                   * (0.4 + 0.5 * localWinterFactor);

                jetIntensity[i] = (float)Math.Clamp(Math.Max(subJetRaw, polarJetRaw), 0.0, 1.0);

                // ── Frontal systems ───────────────────────────────────────────────────
                double bestFront = 0;
                byte   bestType  = 0;

                // 1 – ITCZ band (±5° of itczLat).
                double itczDist = Math.Abs(lat - itczLat);
                if (itczDist < 5.0)
                {
                    double val = 1.0 - itczDist / 5.0;
                    if (val > bestFront) { bestFront = val; bestType = 1; }
                }

                // 2 – Polar front (55-65° both hemispheres); stronger in local winter.
                if (absLat is > 50 and < 70)
                {
                    double val = (1.0 - Math.Abs(absLat - 60.0) / 10.0)
                               * (0.5 + 0.5 * localWinterFactor);
                    if (val > bestFront) { bestFront = val; bestType = 2; }
                }

                // 3 – Subtropical high / subsidence (22-38°).
                if (absLat is > 22 and < 38)
                {
                    double val = 1.0 - Math.Abs(absLat - 30.0) / 8.0;
                    if (val > bestFront) { bestFront = val; bestType = 3; }
                }

                // 4 – Orographic front: steep terrain gradient on high ground.
                if (h > 1500 && row > 0 && row < gs - 1 && col > 0 && col < gs - 1)
                {
                    float hN   = state.HeightMap[(row - 1) * gs + col];
                    float hS   = state.HeightMap[(row + 1) * gs + col];
                    float hW   = state.HeightMap[row * gs + (col - 1)];
                    float hE   = state.HeightMap[row * gs + (col + 1)];
                    double grad = Math.Sqrt(Math.Pow(hE - hW, 2) + Math.Pow(hS - hN, 2)) / 2000.0;
                    if (grad > 0.3)
                    {
                        double val = Math.Clamp(grad - 0.3, 0.0, 0.7);
                        if (val > bestFront) { bestFront = val; bestType = 4; }
                    }
                }

                frontIntensity[i] = (float)Math.Clamp(bestFront, 0.0, 1.0);
                frontType[i]      = (int)bestType;
            }
        }

        // ── Lake effect snow ──────────────────────────────────────────────────────
        // Land cells adjacent to ocean at 35-55° latitude during local winter.
        // NH winter: Oct–Feb; SH winter: Apr–Aug.
        bool nhWinter = month is 10 or 11 or 12 or 1 or 2;
        bool shWinter = month is 4 or 5 or 6 or 7 or 8;

        for (int row = 1; row < gs - 1; row++)
        {
            double lat    = 90.0 - (double)row / (gs - 1) * 180.0;
            double absLat = Math.Abs(lat);
            if (absLat is < 35 or > 55) continue;

            bool inWinter = (lat > 0 && nhWinter) || (lat < 0 && shWinter);
            if (!inWinter) continue;

            for (int col = 1; col < gs - 1; col++)
            {
                int i = row * gs + col;
                if (state.HeightMap[i] <= 0) continue;   // must be land

                bool adjOcean = state.HeightMap[(row - 1) * gs + col]     < 0
                             || state.HeightMap[(row + 1) * gs + col]     < 0
                             || state.HeightMap[row * gs + (col - 1)]     < 0
                             || state.HeightMap[row * gs + (col + 1)]     < 0;
                if (!adjOcean) continue;

                // Colder land = stronger lake-effect snow potential.
                double temp              = state.TemperatureMap[i];
                double lakeEffectVal     = Math.Clamp((10.0 - temp) / 30.0, 0.0, 1.0);

                if (lakeEffectVal > frontIntensity[i])
                {
                    frontIntensity[i] = (float)lakeEffectVal;
                    if (frontType[i] == 0) frontType[i] = 2; // classify as polar front
                }
            }
        }

        // ── Cyclone positions ─────────────────────────────────────────────────────
        AppendTropicalCyclones(cyclones, month, state, gs);
        AppendExtratropicalCyclones(cyclones, month);

        return new WeatherPatternResult
        {
            Month            = month,
            WindU            = windU,
            WindV            = windV,
            OceanCurrentU    = oceanCurrentU,
            OceanCurrentV    = oceanCurrentV,
            JetStreamIntensity = jetIntensity,
            FrontIntensity   = frontIntensity,
            FrontType        = frontType,
            CyclonePositions = cyclones,
        };
    }

    // Stride-sample the grid for warm tropical ocean cells in active season.
    private static void AppendTropicalCyclones(
        List<CyclonePosition> cyclones, int month, SimulationState state, int gs)
    {
        const int Stride  = 16;
        const double SstThreshold = 26.0;

        for (int row = Stride; row < gs - Stride; row += Stride)
        {
            double lat    = 90.0 - (double)row / (gs - 1) * 180.0;
            double absLat = Math.Abs(lat);
            if (absLat is < 5 or > 20) continue;

            bool isNH      = lat > 0;
            bool inSeason  = isNH ? (month >= 6 && month <= 11)
                                  : month is >= 11 or <= 4;
            if (!inSeason) continue;

            for (int col = Stride; col < gs - Stride; col += Stride)
            {
                int i = row * gs + col;
                if (state.HeightMap[i] >= 0) continue;         // ocean only
                double temp = state.TemperatureMap[i];
                if (temp < SstThreshold) continue;

                double lon       = (double)col / gs * 360.0 - 180.0;
                double intensity = Math.Clamp((temp - SstThreshold) / 6.0, 0.1, 1.0);
                cyclones.Add(new CyclonePosition { Lat = lat, Lon = lon, Intensity = intensity, Type = 1 });
            }
        }
    }

    // Climatological extratropical storm-track positions for each hemisphere.
    private static void AppendExtratropicalCyclones(List<CyclonePosition> cyclones, int month)
    {
        double nhWinter = Math.Max(0.0,  Math.Cos(2.0 * Math.PI * (month - 1) / 12.0));
        double shWinter = Math.Max(0.0, -Math.Cos(2.0 * Math.PI * (month - 1) / 12.0));

        // Northern Hemisphere storm tracks (N Atlantic and N Pacific).
        if (nhWinter > 0.2)
        {
            cyclones.Add(new CyclonePosition { Lat =  55, Lon =  -30, Intensity = nhWinter,        Type = 2 });
            cyclones.Add(new CyclonePosition { Lat =  50, Lon = -160, Intensity = nhWinter * 0.85, Type = 2 });
            cyclones.Add(new CyclonePosition { Lat =  58, Lon =   10, Intensity = nhWinter * 0.65, Type = 2 });
        }

        // Southern Hemisphere circumpolar storm tracks.
        if (shWinter > 0.2)
        {
            cyclones.Add(new CyclonePosition { Lat = -57, Lon =  -60, Intensity = shWinter,        Type = 2 });
            cyclones.Add(new CyclonePosition { Lat = -55, Lon =   40, Intensity = shWinter * 0.90, Type = 2 });
            cyclones.Add(new CyclonePosition { Lat = -58, Lon =  140, Intensity = shWinter * 0.85, Type = 2 });
        }
    }
}

public sealed class WeatherPatternResult
{
    public int Month { get; set; }

    /// <summary>Zonal wind component (westward positive) per cell.</summary>
    public float[] WindU { get; set; } = [];

    /// <summary>Meridional wind component (northward positive) per cell.</summary>
    public float[] WindV { get; set; } = [];

    /// <summary>Zonal ocean-current component (westward positive); 0 on land.</summary>
    public float[] OceanCurrentU { get; set; } = [];

    /// <summary>Meridional ocean-current component (northward positive); 0 on land.</summary>
    public float[] OceanCurrentV { get; set; } = [];

    /// <summary>Jet-stream intensity [0–1] per cell; non-zero only in jet-stream bands.</summary>
    public float[] JetStreamIntensity { get; set; } = [];

    /// <summary>Frontal intensity [0–1] per cell.</summary>
    public float[] FrontIntensity { get; set; } = [];

    /// <summary>Front type per cell: 0=none, 1=ITCZ, 2=polar_front, 3=subtropical_high, 4=orographic.</summary>
    public int[] FrontType { get; set; } = [];

    public List<CyclonePosition> CyclonePositions { get; set; } = [];
}

public sealed class CyclonePosition
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Intensity { get; set; }

    /// <summary>1 = tropical, 2 = extratropical.</summary>
    public int Type { get; set; }
}
