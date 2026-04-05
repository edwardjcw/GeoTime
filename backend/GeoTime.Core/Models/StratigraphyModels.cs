namespace GeoTime.Core.Models;

/// <summary>
/// Classifies a stratigraphic layer by the extraordinary event (if any) that
/// produced it.  Normal sedimentary/volcanic cycles are <see cref="Normal"/>.
/// Used by <see cref="GeoTime.Core.Engines.EventDepositionEngine"/> to tag
/// horizon layers deposited by discrete simulation events.
/// </summary>
public enum LayerEventType
{
    Normal,               // standard sedimentation or volcanic deposition
    ImpactEjecta,         // distal ejecta blanket from a bolide impact
    VolcanicAsh,          // tephra horizon from a major eruption or flood basalt
    VolcanicSoot,         // carbon-rich horizon from an extinction-scale eruption
    GammaRayBurst,        // cosmogenic isotope spike (10Be, 26Al) from a near-field GRB
    OceanAnoxicEvent,     // black shale, pyrite framboids — ocean oxygen depletion
    SnowballGlacial,      // diamictite horizon from global glaciation
    IronFormation,        // banded iron formation from atmospheric oxygen rise
    MeteoriticIron,       // siderophile-enriched layer from cosmic dust flux anomaly
    MassExtinction,       // composite geochemical anomaly layer
    CarbonIsotopeExcursion, // δ13C shift from carbon cycle perturbation
}

/// <summary>
/// Ordered (oldest-first) stack of stratigraphic layers for a single grid cell.
/// Populated by the geological engines and, for event horizons, by
/// <see cref="GeoTime.Core.Engines.EventDepositionEngine"/>.
/// </summary>
public sealed class StratigraphicColumn
{
    public List<StratigraphicLayer> Layers { get; init; } = new();

    /// <summary>The topmost (most recently deposited) layer, or null if the column is empty.</summary>
    public StratigraphicLayer? Surface => Layers.Count > 0 ? Layers[^1] : null;

    /// <summary>Total modelled thickness of all layers in metres.</summary>
    public double TotalThicknessM => Layers.Sum(l => l.Thickness);

    /// <summary>
    /// Returns only those layers whose <see cref="StratigraphicLayer.EventType"/> is not
    /// <see cref="LayerEventType.Normal"/>, i.e. layers produced by extraordinary events.
    /// </summary>
    public IEnumerable<StratigraphicLayer> ExtraordinaryLayers =>
        Layers.Where(l => l.EventType != LayerEventType.Normal);
}
