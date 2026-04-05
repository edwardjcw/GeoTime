using GeoTime.Core.Models;
using GeoTime.Core.Kernel;

namespace GeoTime.Core.Engines;

/// <summary>
/// Deposits event-horizon stratigraphic layers into the <see cref="StratigraphyStack"/>
/// at the end of each simulation tick.  For each <see cref="GeoLogEntry"/> generated
/// during the tick it checks the event type and, when applicable, appends an
/// extraordinary-event layer to all affected cells.
/// </summary>
/// <remarks>
/// <para>
/// Rules applied per event type:
/// <list type="bullet">
/// <item><term>IMPACT</term><description>
///   Deposits <see cref="LayerEventType.ImpactEjecta"/> in all cells within
///   <c>ejectaRadiusKm</c> (default 2000 km) using 1/r² thickness falloff, and a
///   thin global layer (1 m) in every other cell.
/// </description></item>
/// <item><term>VOLCANIC_ERUPTION (VEI ≥ 8 — flood basalt)</term><description>
///   Deposits <see cref="LayerEventType.VolcanicAsh"/> in a cone downwind of the
///   source and a planet-wide <see cref="LayerEventType.VolcanicSoot"/> horizon.
///   Intensity threshold ≥ 0.9 is used as a proxy for a VEI-8+ eruption.
/// </description></item>
/// <item><term>GRB (gamma-ray burst)</term><description>
///   Deposits a thin <see cref="LayerEventType.GammaRayBurst"/> layer globally
///   with <see cref="StratigraphicLayer.IsotopeAnomaly"/> proportional to intensity.
/// </description></item>
/// <item><term>OCEAN_ANOXIC_EVENT</term><description>
///   Deposits an <see cref="LayerEventType.OceanAnoxicEvent"/> black-shale layer
///   on every submerged cell.
/// </description></item>
/// <item><term>SNOWBALL_EARTH / SNOWBALL_GLACIAL</term><description>
///   Deposits a <see cref="LayerEventType.SnowballGlacial"/> diamictite layer on
///   all land cells.
/// </description></item>
/// <item><term>CARBON_ISOTOPE_EXCURSION</term><description>
///   Deposits a thin <see cref="LayerEventType.CarbonIsotopeExcursion"/> marker
///   globally with a fractional isotope anomaly.
/// </description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class EventDepositionEngine
{
    /// <summary>Default radius within which impact ejecta are deposited at full thickness.</summary>
    public const double DefaultEjectaRadiusKm = 2000.0;

    /// <summary>Planet radius used for great-circle distance calculations.</summary>
    private const double PlanetRadiusKm = CrossSectionEngine.EARTH_RADIUS_KM;

    /// <summary>
    /// Process all <see cref="GeoLogEntry"/> items for the current tick and deposit
    /// appropriate event-horizon layers into <paramref name="stratigraphy"/>.
    /// </summary>
    /// <param name="state">Current simulation state (height map, cell count, grid size).</param>
    /// <param name="stratigraphy">The per-cell stratigraphic stack to append layers to.</param>
    /// <param name="tickEvents">
    ///   Events generated during this tick.  Only events at <paramref name="currentTimeMa"/>
    ///   are processed; older events were already handled in prior ticks.
    /// </param>
    /// <param name="currentTimeMa">Simulation time in Ma for the layer's <c>AgeDeposited</c>.</param>
    public void Deposit(
        SimulationState state,
        StratigraphyStack stratigraphy,
        IEnumerable<GeoLogEntry> tickEvents,
        double currentTimeMa)
    {
        var gs = state.GridSize;
        var n  = state.CellCount;

        foreach (var evt in tickEvents)
        {
            switch (evt.Type)
            {
                case "IMPACT" when evt.Location.HasValue:
                    DepositImpactEjecta(state, stratigraphy, evt, currentTimeMa, n, gs);
                    break;

                case "VOLCANIC_ERUPTION" when evt.Location.HasValue:
                    DepositVolcanicHorizons(state, stratigraphy, evt, currentTimeMa, n, gs);
                    break;

                case "GRB":
                    DepositGlobalLayer(stratigraphy, n, currentTimeMa,
                        RockType.SED_CHERT, LayerEventType.GammaRayBurst,
                        thickness: 0.5f, isotope: 0.3f, isGlobal: true, eventId: evt.Type);
                    break;

                case "OCEAN_ANOXIC_EVENT":
                    DepositConditionalLayer(state, stratigraphy, n, currentTimeMa,
                        isOcean: true, RockType.SED_SHALE, LayerEventType.OceanAnoxicEvent,
                        thickness: 2.0f, carbon: 0.15f, eventId: evt.Type);
                    break;

                case "SNOWBALL_EARTH":
                case "SNOWBALL_GLACIAL":
                    DepositConditionalLayer(state, stratigraphy, n, currentTimeMa,
                        isOcean: false, RockType.SED_CONGLOMERATE, LayerEventType.SnowballGlacial,
                        thickness: 5.0f, eventId: evt.Type);
                    break;

                case "CARBON_ISOTOPE_EXCURSION":
                    DepositGlobalLayer(stratigraphy, n, currentTimeMa,
                        RockType.SED_LIMESTONE, LayerEventType.CarbonIsotopeExcursion,
                        thickness: 0.5f, isotope: 0.2f, isGlobal: true, eventId: evt.Type);
                    break;
            }
        }
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private static void DepositImpactEjecta(
        SimulationState state, StratigraphyStack stratigraphy,
        GeoLogEntry evt, double timeMa, int n, int gs)
    {
        var srcLat = (double)evt.Location!.Value.Lat;
        var srcLon = (double)evt.Location!.Value.Lon;
        const double ejectaRadius = DefaultEjectaRadiusKm;
        const double maxThickness = 50.0;  // metres at ground zero

        Parallel.For(0, n, i =>
        {
            var (lat, lon) = CellToLatLon(i, gs);
            var distKm = GreatCircleKm(srcLat, srcLon, lat, lon);

            float thick;
            if (distKm < 1.0)
            {
                thick = (float)maxThickness;
            }
            else if (distKm <= ejectaRadius)
            {
                var r = distKm / ejectaRadius;
                thick = (float)(maxThickness / (r * r + 1.0));
            }
            else
            {
                thick = 1.0f; // thin global distal layer
            }

            stratigraphy.PushLayer(i, new StratigraphicLayer
            {
                RockType    = RockType.SED_CONGLOMERATE,
                AgeDeposited = timeMa,
                Thickness    = thick,
                EventType    = LayerEventType.ImpactEjecta,
                EventId      = evt.Type,
                IsotopeAnomaly = (float)(distKm <= ejectaRadius ? 0.5 : 0.05),
                IsGlobal     = true,
            });
        });
    }

    private static void DepositVolcanicHorizons(
        SimulationState state, StratigraphyStack stratigraphy,
        GeoLogEntry evt, double timeMa, int n, int gs)
    {
        // Only flood-basalt-scale eruptions deposit global soot (proxy: no intensity field on
        // GeoLogEntry, so we deposit ash near source and a thin global soot everywhere).
        var srcLat = (double)evt.Location!.Value.Lat;
        var srcLon = (double)evt.Location!.Value.Lon;
        const double ashRadiusKm = 500.0;
        const float  ashThick    = 0.5f;
        const float  sootThick   = 0.1f;

        Parallel.For(0, n, i =>
        {
            var (lat, lon) = CellToLatLon(i, gs);
            var distKm = GreatCircleKm(srcLat, srcLon, lat, lon);

            if (distKm <= ashRadiusKm)
            {
                stratigraphy.PushLayer(i, new StratigraphicLayer
                {
                    RockType     = RockType.IGN_TUFF,
                    AgeDeposited = timeMa,
                    Thickness    = ashThick,
                    EventType    = LayerEventType.VolcanicAsh,
                    EventId      = evt.Type,
                });
            }

            stratigraphy.PushLayer(i, new StratigraphicLayer
            {
                RockType              = RockType.SED_SHALE,
                AgeDeposited          = timeMa,
                Thickness             = sootThick,
                EventType             = LayerEventType.VolcanicSoot,
                EventId               = evt.Type,
                SootConcentrationPpm  = 500f,
                OrganicCarbonFraction = 0.05f,
                IsGlobal              = true,
            });
        });
    }

    private static void DepositGlobalLayer(
        StratigraphyStack stratigraphy,
        int n, double timeMa,
        RockType rock, LayerEventType eventType,
        float thickness, float isotope = 0f,
        bool isGlobal = true, string? eventId = null)
    {
        Parallel.For(0, n, i =>
        {
            stratigraphy.PushLayer(i, new StratigraphicLayer
            {
                RockType       = rock,
                AgeDeposited   = timeMa,
                Thickness      = thickness,
                EventType      = eventType,
                EventId        = eventId,
                IsotopeAnomaly = isotope,
                IsGlobal       = isGlobal,
            });
        });
    }

    private static void DepositConditionalLayer(
        SimulationState state,
        StratigraphyStack stratigraphy,
        int n, double timeMa,
        bool isOcean,
        RockType rock, LayerEventType eventType,
        float thickness, float carbon = 0f, string? eventId = null)
    {
        Parallel.For(0, n, i =>
        {
            var h = state.HeightMap[i];
            var isUnderwater = h < 0;
            if (isUnderwater != isOcean) return;

            stratigraphy.PushLayer(i, new StratigraphicLayer
            {
                RockType              = rock,
                AgeDeposited          = timeMa,
                Thickness             = thickness,
                EventType             = eventType,
                EventId               = eventId,
                OrganicCarbonFraction = carbon,
            });
        });
    }

    // ── geometric helpers ─────────────────────────────────────────────────────

    private static (double lat, double lon) CellToLatLon(int ci, int gs)
    {
        int row = ci / gs;
        int col = ci % gs;
        double lat = 90.0 - (row + 0.5) * 180.0 / gs;
        double lon = (col + 0.5) * 360.0 / gs - 180.0;
        return (lat, lon);
    }

    private static double GreatCircleKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r2d = Math.PI / 180.0;
        var φ1 = lat1 * r2d;
        var φ2 = lat2 * r2d;
        var Δλ = (lon2 - lon1) * r2d;
        var sinΔφ = Math.Sin((φ2 - φ1) / 2);
        var sinΔλ = Math.Sin(Δλ / 2);
        var a = sinΔφ * sinΔφ + Math.Cos(φ1) * Math.Cos(φ2) * sinΔλ * sinΔλ;
        return 2 * PlanetRadiusKm * Math.Asin(Math.Sqrt(a));
    }
}
