using System.Text;
using GeoTime.Core.Models;

namespace GeoTime.Core.Services;

/// <summary>
/// Generates expert-level geological prose from a <see cref="GeologicalContext"/>
/// without any external LLM.  Used as the always-available fallback in
/// <c>TemplateFallbackProvider</c> and as the default when no LLM is configured.
///
/// Each feature type has its own template method that builds 4–6 paragraphs
/// matching the structure requested by <see cref="DescriptionPromptComposer"/>.
/// </summary>
public static class DescriptionTemplateEngine
{
    /// <summary>
    /// Generate a geological description for the given context.
    /// Returns a list of non-empty paragraph strings.
    /// </summary>
    public static List<string> Generate(GeologicalContext ctx)
    {
        var primaryName = ctx.PrimaryLandFeature?.Current.Name ?? ctx.PrimaryWaterFeature?.Current.Name ?? "this location";
        var primaryType = ctx.PrimaryLandFeature?.Type ?? ctx.PrimaryWaterFeature?.Type;

        var paragraphs = primaryType switch
        {
            FeatureType.MountainRange  => MountainRangeParagraphs(ctx, primaryName),
            FeatureType.River          => RiverParagraphs(ctx, primaryName),
            FeatureType.Ocean     => OceanBasinParagraphs(ctx, primaryName),
            FeatureType.Continent      => ContinentParagraphs(ctx, primaryName),
            FeatureType.ImpactBasin    => ImpactBasinParagraphs(ctx, primaryName),
            _                          => GenericParagraphs(ctx, primaryName),
        };

        // Append extraordinary-layer sentences to paragraph 2 (stratigraphy)
        var layers = ctx.ExtraordinaryLayers;
        if (layers.Count > 0 && paragraphs.Count >= 2)
        {
            var sb = new StringBuilder(paragraphs[1]);
            foreach (var layer in layers)
            {
                sb.Append(' ');
                sb.Append(ExtraordinaryLayerSentence(layer));
            }
            paragraphs[1] = sb.ToString();
        }

        // Paragraph 6: historical biography if > 5 major changes
        if (ctx.PrimaryFeatureHistory.Count > 5)
        {
            paragraphs.Add(HistoryParagraph(ctx, primaryName));
        }

        // Summary sentence
        paragraphs.Add(SummarySentence(ctx, primaryName, primaryType));

        return paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    // ── Mountain Range ───────────────────────────────────────────────────────

    private static List<string> MountainRangeParagraphs(GeologicalContext ctx, string name)
    {
        var paras = new List<string>();

        // Paragraph 1: tectonic origin
        var origin = ctx.MountainOriginType switch
        {
            "volcanic arc" =>
                $"The {name} arc rises above a subducting slab" +
                (ctx.SubductingPlate != null ? $" — the {ctx.SubductingPlate.Id.ToString()} plate descending" : "") +
                $" beneath the {ctx.CurrentPlate?.Id.ToString() ?? "host"} plate. " +
                $"Magmatic differentiation and crustal thickening during this {ctx.SimAgeDescription}-long " +
                $"subduction event have built a chain of composite stratovolcanoes and calc-alkaline " +
                $"intrusive bodies along the arc axis. Convergence at roughly " +
                $"{ctx.ConvergenceRateCmPerYear:F1} cm/yr drives ongoing uplift and seismicity.",
            "hotspot shield" =>
                $"The broad shield upland of {name} reflects sustained mantle plume magmatism " +
                $"beneath the {ctx.CurrentPlate?.Id.ToString() ?? "host"} plate. Unlike compressional " +
                $"mountain belts, this range grew by repeated flood-basalt effusions and shield " +
                $"construction over {ctx.SimAgeDescription}, producing a low-angle topographic " +
                $"dome underlain by dense, mafic lower crust.",
            _ => // fold-belt (default)
                $"The {name} orogen records a Himalayan-style continental collision" +
                (ctx.CollidingPlate != null ? $" between the {ctx.CurrentPlate?.Id.ToString()} and {ctx.CollidingPlate.Id.ToString()} plates" : "") +
                $". Crustal shortening over {ctx.SimAgeDescription} stacked thrust sheets " +
                $"into a fold-and-thrust belt, thickening the crust to isostatically support " +
                $"elevations reaching {ctx.RangeMaxElevationM?.ToString("F0") ?? "several thousand"} m. " +
                $"Convergence rates of {ctx.ConvergenceRateCmPerYear:F1} cm/yr indicate active deformation.",
        };
        paras.Add(origin);

        // Paragraph 2: lithology and stratigraphy
        var rock = ctx.Cell.RockType;
        paras.Add(
            $"The exposed lithology at this sampling point is {RockTypeLabel((int)rock)}, representative " +
            $"of the {(ctx.Cell.RockAge > 100 ? "ancient" : "younger")} " +
            $"{ctx.Cell.RockAge:F0}-Ma basement of the range. " +
            $"Crustal thickness reaches {ctx.Cell.CrustThickness:F1} km, consistent with a " +
            $"thickened orogenic root. Deformation fabrics include axial planar cleavage in " +
            $"metasedimentary units and ductile shear zones at mid-crustal depths."
        );

        // Paragraph 3: erosion and hydrology
        var hydro = new StringBuilder();
        hydro.Append($"Erosion of {name} supplies sediment to the surrounding basins via ");
        if (ctx.RiverName != null)
        {
            hydro.Append($"the {ctx.RiverName} river system (catchment {ctx.CatchmentAreaKm2?.ToString("F0") ?? "unknown"} km²");
            if (ctx.RiverOutletOcean != null) hydro.Append($", draining to {ctx.RiverOutletOcean}");
            hydro.Append("). ");
        }
        else
        {
            hydro.Append("multiple short drainages. ");
        }
        hydro.Append($"Mean annual precipitation at this elevation is {ctx.MeanPrecipMm:F0} mm/yr, " +
                     $"with glacial cirques visible on north-facing slopes above the permanent snowline.");
        paras.Add(hydro.ToString());

        // Paragraph 4: climate coupling
        var climate = new StringBuilder();
        climate.Append($"The range exerts a dominant orographic control on regional climate. ");
        if (ctx.HasRainShadow)
        {
            climate.Append($"A pronounced rain shadow extends to the leeward side, where annual " +
                           $"precipitation falls below 250 mm and desert or steppe vegetation dominates. ");
        }
        if (ctx.IsOnWindwardSide)
        {
            climate.Append($"This windward flank intercepts the prevailing moisture flux, sustaining " +
                           $"heavy orographic precipitation that feeds perennial river systems. ");
        }
        if (ctx.IsInMonsoonZone)
            climate.Append("The range also deflects monsoon circulation, intensifying seasonal rainfall on its southern slopes. ");
        if (ctx.IsInJetStreamZone)
            climate.Append("Jet-stream interactions accelerate surface winds and enhance snow deposition at the crest. ");
        climate.Append($"Mean temperature at this point is {ctx.MeanTempC:F1} °C.");
        paras.Add(climate.ToString());

        // Paragraph 5: biome
        paras.Add(
            $"The {ctx.BiomeType} biome characterises this elevation zone, grading downslope " +
            $"into {(ctx.MeanPrecipMm > 800 ? "temperate forest" : "grassland or steppe")} as " +
            $"precipitation decreases with distance from the orographic barrier. Above the treeline " +
            $"alpine meadows and scree fields are typical, transitioning to permanent ice fields at the " +
            $"highest elevations. The range thus creates a steep biodiversity gradient across its flanks."
        );

        return paras;
    }

    // ── River ────────────────────────────────────────────────────────────────

    private static List<string> RiverParagraphs(GeologicalContext ctx, string name)
    {
        var paras = new List<string>();

        // Paragraph 1: tectonic/hydrological origin
        paras.Add(
            $"The {name} river originates within the {ctx.CurrentPlate?.Id.ToString() ?? "host"} plate " +
            $"on terrain shaped by {ctx.SimAgeDescription} of tectonic evolution. " +
            $"Its headwaters are fed by orographic precipitation " +
            (ctx.IsInMountainRange ? $"in the {ctx.RangeName ?? "adjacent"} highlands, " : "on upland slopes, ") +
            $"with the drainage gradient of {ctx.DrainageGradient?.ToString("F2") ?? "variable"} m/km " +
            $"reflecting the tectonic tilt inherited from the regional structural fabric."
        );

        // Paragraph 2: lithology
        paras.Add(
            $"The valley floor is incised into {RockTypeLabel((int)ctx.Cell.RockType)}, " +
            $"exposing {ctx.Cell.RockAge:F0}-Ma basement rocks in river-cut gorges. " +
            $"Crustal thickness of {ctx.Cell.CrustThickness:F1} km and the absence of active " +
            $"subduction in the immediate vicinity suggests the floodplain is underlain by " +
            $"stable continental lithosphere."
        );

        // Paragraph 3: channel and drainage
        var drainage = new StringBuilder();
        drainage.Append($"The {name} drains a catchment of approximately " +
                        $"{ctx.CatchmentAreaKm2?.ToString("F0") ?? "unknown"} km² ");
        if (ctx.RiverOutletOcean != null)
            drainage.Append($"before discharging into {ctx.RiverOutletOcean}. ");
        else if (ctx.IsInEndorheicBasin)
            drainage.Append($"into an endorheic (closed) basin with no ocean outlet. " +
                            $"Evaporite deposits likely accumulate at the terminus. ");
        drainage.Append($"Total channel length of approximately {ctx.RiverLengthKm?.ToString("F0") ?? "unknown"} km " +
                        $"gives the system a {(ctx.DrainageGradient > 5 ? "steep, youthful" : "mature, low-gradient")} " +
                        $"profile. Valley morphology reflects the interplay of bedrock erodibility, " +
                        $"glacial legacy, and modern fluvial processes.");
        paras.Add(drainage.ToString());

        // Paragraph 4: climate
        paras.Add(
            $"The catchment lies in the {ctx.BiomeType} climate zone " +
            $"(mean {ctx.MeanTempC:F1} °C, {ctx.MeanPrecipMm:F0} mm/yr). " +
            (ctx.IsInMonsoonZone
                ? $"Monsoon seasonality produces a pronounced annual flood pulse that drives lateral erosion " +
                  $"and point-bar migration in the lower reaches. "
                : $"Precipitation is relatively uniform year-round, sustaining perennial baseflow. ") +
            (ctx.NearestOceanCurrentName != null
                ? $"The {ctx.NearestOceanCurrentName} ocean current moderates coastal temperatures " +
                  $"near the river mouth, influencing moisture availability in the catchment. "
                : "")
        );

        // Paragraph 5: biome
        paras.Add(
            $"Riparian {ctx.BiomeType.ToLower()} corridors trace the floodplain, providing " +
            $"habitat connectivity across the otherwise drier interfluve. The delta, " +
            $"where the {name} enters the receiving basin, hosts " +
            $"{(ctx.MeanTempC > 20 ? "mangrove-analogues and coastal wetlands" : "deltaic marshes and estuarine mudflats")}, " +
            $"key sites for organic carbon accumulation and burial."
        );

        return paras;
    }

    // ── Ocean Basin ──────────────────────────────────────────────────────────

    private static List<string> OceanBasinParagraphs(GeologicalContext ctx, string name)
    {
        var paras = new List<string>();

        paras.Add(
            $"The {name} ocean basin opened by seafloor spreading " +
            $"during the {ctx.SimAgeDescription} interval of plate divergence. " +
            $"Its spreading ridge, currently {ctx.DistanceToPlateMarginKm:F0} km from this point, " +
            $"generated the oceanic crust now preserved at depths of up to several kilometres. " +
            $"The basin's {ctx.NearestMarginType} margins record the full Wilson Cycle arc from rifting to mature oceanic spreading."
        );

        paras.Add(
            $"The abyssal plain is underlain by {RockTypeLabel((int)ctx.Cell.RockType)} " +
            $"of approximate age {ctx.Cell.RockAge:F0} Ma. " +
            $"Pelagic sedimentation has draped the basement with carbonate oozes above the CCD " +
            $"and siliceous muds below. Crustal thickness of {ctx.Cell.CrustThickness:F1} km " +
            $"is typical of oceanic lithosphere at this crustal age."
        );

        paras.Add(
            $"The {name} receives riverine input from adjacent continental margins. " +
            $"Turbidity currents periodically rework shelf-edge sediments into deep abyssal fans. " +
            $"Hydrothermal circulation at the spreading axis locally alters basaltic basement " +
            $"and contributes metal-rich muds to the sedimentary budget."
        );

        paras.Add(
            $"Surface waters in the {ctx.BiomeType.ToLower()} zone average {ctx.MeanTempC:F1} °C, " +
            $"driving thermohaline overturning that ventilates the deep water column. " +
            (ctx.NearestOceanCurrentName != null
                ? $"The {ctx.NearestOceanCurrentName} " +
                  $"{(ctx.NearestCurrentIsWarm ? "warm" : "cold")} current transports heat " +
                  $"along the basin margins, moderating coastal climates on adjacent continents. "
                : "") +
            (ctx.IsInHurricaneCorridor
                ? $"The warm surface temperatures of this basin support a major hurricane corridor, " +
                  $"channelling cyclonic storms toward surrounding landmasses. "
                : "")
        );

        paras.Add(
            $"The ocean basin exerts a planet-scale climate role, redistributing heat and moisture " +
            $"via surface currents and evaporative flux. Its biological productivity varies with " +
            $"upwelling intensity along the eastern margin, driving carbon export to the deep seafloor " +
            $"and influencing atmospheric CO₂ concentrations over geological timescales."
        );

        return paras;
    }

    // ── Continent ────────────────────────────────────────────────────────────

    private static List<string> ContinentParagraphs(GeologicalContext ctx, string name)
    {
        var paras = new List<string>();

        paras.Add(
            $"{name} is an ancient crustal fragment that cratonised approximately {ctx.Cell.RockAge:F0} Ma. " +
            $"Its stable shield nucleus, underlain by thick ({ctx.Cell.CrustThickness:F1} km) " +
            $"lithospheric keel, has survived multiple supercontinent assemblies and dispersals " +
            $"over {ctx.SimAgeDescription}. Active orogenic belts along its margins record " +
            $"ongoing plate convergence at {ctx.ConvergenceRateCmPerYear:F1} cm/yr."
        );

        paras.Add(
            $"Exposed basement rocks at this sampling point are {RockTypeLabel((int)ctx.Cell.RockType)}, " +
            $"representative of the {ctx.Cell.RockAge:F0}-Ma continental crust. " +
            $"Multiple generations of deformation fabrics overprint the shield, recording " +
            $"successive collisional orogenies that welded terranes onto the cratonic margin."
        );

        paras.Add(
            $"Continental drainage divides define catchments that route runoff to surrounding ocean basins. " +
            (ctx.RiverName != null
                ? $"The {ctx.RiverName} system (catchment ~{ctx.CatchmentAreaKm2?.ToString("F0") ?? "unknown"} km²) " +
                  $"is the dominant trunk drainage on this part of the continent. "
                : "") +
            $"Mean elevation of the exposed shield is modest, with deeply weathered regolith " +
            $"indicating long periods of relative tectonic stability."
        );

        paras.Add(
            $"The continental interior experiences {ctx.BiomeType.ToLower()} conditions " +
            $"(mean {ctx.MeanTempC:F1} °C, {ctx.MeanPrecipMm:F0} mm/yr), with precipitation " +
            $"declining away from the ocean margins. " +
            (ctx.IsInMonsoonZone ? $"Monsoon circulation delivers intense seasonal rainfall to the southern margins. " : "") +
            (ctx.HasRainShadow ? $"Orographic rain shadows cast by marginal highlands create an arid interior. " : "")
        );

        paras.Add(
            $"The {ctx.BiomeType} biome dominates the sampling point, forming part of a " +
            $"latitudinally zoned biome pattern that spans the full extent of {name}. " +
            $"Biodiversity gradients follow the precipitation and temperature contours, with " +
            $"refugia concentrating in topographically complex marginal belts."
        );

        return paras;
    }

    // ── Impact Basin ─────────────────────────────────────────────────────────

    private static List<string> ImpactBasinParagraphs(GeologicalContext ctx, string name)
    {
        var paras = new List<string>();

        paras.Add(
            $"The {name} basin records a hypervelocity bolide impact " +
            $"during the {ctx.SimAgeDescription} interval of planetary history. " +
            $"The impactor excavated a transient cavity subsequently modified by " +
            $"gravitational collapse into a complex crater structure with a central uplift, " +
            $"an annular trough, and an outer rim of brecciated ejecta. " +
            $"The basin now lies within the {ctx.CurrentPlate?.Id.ToString() ?? "host"} plate " +
            $"at approximately {ctx.DistanceToPlateMarginKm:F0} km from the nearest plate margin."
        );

        paras.Add(
            $"The impact melt sheet, now crystallised as {RockTypeLabel((int)ctx.Cell.RockType)}, " +
            $"preserves planar deformation features, shocked quartz, and suevite breccia " +
            $"that fingerprint the hypershock pressure environment. " +
            $"Crustal thickness here ({ctx.Cell.CrustThickness:F1} km) is reduced relative to " +
            $"regional background by post-impact isostatic adjustments and tectonic thinning."
        );

        paras.Add(
            $"Post-impact erosion has progressively infilled the basin with lacustrine and fluvial sediments. " +
            (ctx.RiverName != null
                ? $"The {ctx.RiverName} presently drains the basin floor, " +
                  $"delivering suspended load to {ctx.RiverOutletOcean ?? "adjacent lowlands"}. "
                : $"Ephemeral drainage now occupies the radial fracture network. ") +
            $"Sediment accumulation rates within the basin exceed regional background, " +
            $"providing a high-resolution record of post-impact climate and biosphere recovery."
        );

        paras.Add(
            $"The {name} basin lies in the {ctx.BiomeType} climate zone " +
            $"(mean {ctx.MeanTempC:F1} °C, {ctx.MeanPrecipMm:F0} mm/yr). " +
            $"Topographic focussing of precipitation by the basin rim creates a distinct " +
            $"microclimate inside the structure, often more humid than the surrounding plain. " +
            (ctx.IsInHurricaneCorridor ? $"Proximity to the hurricane corridor periodically delivers intense rainfall events. " : "")
        );

        paras.Add(
            $"The {ctx.BiomeType.ToLower()} vegetation of the basin floor grades into more " +
            $"xeric assemblages on the rim, where thin soils over impact breccia limit water retention. " +
            $"The unique geochemical substrate — metal-enriched impact melt rocks — may support " +
            $"adapted flora analogous to Earth's serpentinite barrens."
        );

        return paras;
    }

    // ── Generic fallback ─────────────────────────────────────────────────────

    private static List<string> GenericParagraphs(GeologicalContext ctx, string name)
    {
        var paras = new List<string>();

        paras.Add(
            $"{name} lies on the {ctx.CurrentPlate?.Id.ToString() ?? "host"} tectonic plate " +
            $"approximately {ctx.DistanceToPlateMarginKm:F0} km from the nearest " +
            $"{ctx.NearestMarginType} margin. The feature has evolved over {ctx.SimAgeDescription} " +
            $"of planetary history under the combined influence of plate tectonics, " +
            $"erosion, and climate forcing."
        );

        paras.Add(
            $"The surface lithology at this point is {RockTypeLabel((int)ctx.Cell.RockType)}, " +
            $"approximately {ctx.Cell.RockAge:F0} Ma old, resting on a crustal column " +
            $"{ctx.Cell.CrustThickness:F1} km thick. " +
            $"The rock record preserves a conformable sedimentary succession interrupted by " +
            $"unconformities that correlate with major tectonic events."
        );

        paras.Add(
            (ctx.RiverName != null
                ? $"The {ctx.RiverName} river traverses this area, draining " +
                  $"~{ctx.CatchmentAreaKm2?.ToString("F0") ?? "unknown"} km² " +
                  $"{(ctx.RiverOutletOcean != null ? $"into {ctx.RiverOutletOcean}" : "(endorheic basin)")}. "
                : "No named river presently traverses this location. ") +
            $"Erosion rates are modulated by the {ctx.BiomeType.ToLower()} vegetation cover " +
            $"and the precipitation regime of {ctx.MeanPrecipMm:F0} mm/yr."
        );

        paras.Add(
            $"The local climate is {ctx.BiomeType.ToLower()} with a mean surface temperature " +
            $"of {ctx.MeanTempC:F1} °C. " +
            (ctx.IsInMonsoonZone ? "Monsoon seasonality drives strong wet–dry precipitation cycles. " : "") +
            (ctx.HasRainShadow ? $"A rain shadow effect suppresses precipitation on this side of the adjacent high terrain. " : "") +
            (ctx.NearestOceanCurrentName != null
                ? $"The nearby {ctx.NearestOceanCurrentName} " +
                  $"{(ctx.NearestCurrentIsWarm ? "warm" : "cold")} current modulates regional temperature. "
                : "")
        );

        paras.Add(
            $"The {ctx.BiomeType} biome supports typical plant communities for this temperature–precipitation regime. " +
            $"Soil formation has proceeded over {ctx.SimAgeDescription}, producing a " +
            $"{ctx.Cell.SoilDepth:F2}-m profile with organic-horizon development proportional " +
            $"to biomass input ({ctx.Cell.Biomass:F1} kg/m²)."
        );

        return paras;
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static string ExtraordinaryLayerSentence(StratigraphicLayer layer)
    {
        var typeDesc = layer.EventType switch
        {
            LayerEventType.ImpactEjecta         => "bolide impact ejecta horizon",
            LayerEventType.VolcanicAsh          => "tephra (volcanic ash) horizon",
            LayerEventType.VolcanicSoot         => "carbon-rich volcanic soot horizon",
            LayerEventType.GammaRayBurst        => "cosmogenic isotope spike from a near-field gamma-ray burst",
            LayerEventType.OceanAnoxicEvent     => "black shale horizon recording an ocean anoxic event",
            LayerEventType.SnowballGlacial      => "diamictite horizon from a global glaciation event",
            LayerEventType.IronFormation        => "banded iron formation recording atmospheric oxygen rise",
            LayerEventType.MeteoriticIron       => "siderophile-enriched layer from a cosmic dust flux anomaly",
            LayerEventType.MassExtinction       => "composite geochemical anomaly layer correlating with a mass extinction",
            LayerEventType.CarbonIsotopeExcursion => "δ¹³C excursion layer from a carbon cycle perturbation",
            _                                   => "extraordinary event horizon",
        };

        var sb = new StringBuilder();
        sb.Append($"The stratigraphic column at this location preserves a {layer.Thickness:F2}-m {typeDesc}");
        if (!string.IsNullOrWhiteSpace(layer.EventId))
            sb.Append($" associated with event {layer.EventId}");
        if (layer.IsotopeAnomaly != 0f)
            sb.Append($", characterised by an isotopic anomaly of {layer.IsotopeAnomaly:+0.000;-0.000}");
        if (layer.SootConcentrationPpm > 0f)
            sb.Append($" and soot concentrations of {layer.SootConcentrationPpm:F1} ppm");
        if (layer.IsGlobal)
            sb.Append(" (this is a globally distributed horizon)");
        sb.Append('.');
        return sb.ToString();
    }

    private static string HistoryParagraph(GeologicalContext ctx, string name)
    {
        if (ctx.PrimaryFeatureHistory.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.Append($"The biographical record of {name} spans {ctx.PrimaryFeatureHistory.Count} catalogued events: ");
        foreach (var snap in ctx.PrimaryFeatureHistory.Take(6))
        {
            sb.Append($"at tick {snap.SimTickCreated}, ");
            if (!string.IsNullOrWhiteSpace(snap.SplitFromId ?? snap.MergedIntoId ?? "—"))
                sb.Append($"{snap.SplitFromId ?? snap.MergedIntoId ?? "—".ToLower()} ({snap.Name}); ");
        }
        sb.Append("among other transitions. This long evolutionary history makes it one of the more " +
                  "dynamically significant features in the planetary geological record.");
        return sb.ToString();
    }

    private static string SummarySentence(GeologicalContext ctx, string name, FeatureType? type)
    {
        var significance = type switch
        {
            FeatureType.MountainRange  => "a major topographic and climatic barrier",
            FeatureType.River          => "a primary drainage artery and sediment conveyor",
            FeatureType.Ocean     => "a fundamental heat-redistribution engine in the global climate system",
            FeatureType.Continent      => "a cratonically stable landmass anchoring regional geological history",
            FeatureType.ImpactBasin    => "a crater structure preserving a unique record of hypervelocity impact",
            _                          => "a geologically complex feature",
        };
        return $"{name} is {significance}, shaped by {ctx.SimAgeDescription} of planetary evolution. " +
               $"Its tectonic setting, stratigraphic record, and climate interactions make it a key " +
               $"reference point for understanding the geological history of this world.";
    }

    private static string RockTypeLabel(int rockType) => rockType switch
    {
        0  => "basalt",
        1  => "granite",
        2  => "sandstone",
        3  => "limestone",
        4  => "shale",
        5  => "quartzite",
        6  => "schist",
        7  => "gneiss",
        8  => "rhyolite",
        9  => "andesite",
        10 => "obsidian",
        11 => "peridotite",
        12 => "dolomite",
        13 => "coal",
        14 => "oil shale",
        _  => $"rock type {rockType}",
    };
}
