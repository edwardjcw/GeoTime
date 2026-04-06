using System.Text;
using System.Text.Json;
using GeoTime.Core.Models;

namespace GeoTime.Core.Services;

/// <summary>
/// Builds the system and user prompts that are sent to an LLM provider to
/// generate a geological description for a grid cell.  Consumed by the
/// <c>POST /api/describe</c> endpoint in Phase D5.
/// </summary>
public static class DescriptionPromptComposer
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Returns the fixed system prompt that establishes the LLM persona as a
    /// senior planetary geologist.
    /// </summary>
    public static string ComposeSystemPrompt() =>
        "You are a senior planetary geologist writing encyclopedia entries for a fictional alien world. " +
        "Your writing is precise, technical, and vivid — the style of a Nature article combined with a " +
        "National Geographic narrative. You describe geological formations in the same depth a field " +
        "geologist would: origin, deformation history, lithology, erosional history, drainage influence, " +
        "and climate coupling. You reference all features, plates, rivers, and oceans by their proper " +
        "names as given. You explicitly discuss any extraordinary events visible in the stratigraphic " +
        "record. You describe how the formation evolved through geological time and how it is changing " +
        "today.";

    /// <summary>
    /// Builds the user prompt from a <see cref="GeologicalContext"/>.  The prompt
    /// serialises the context as compact JSON followed by explicit paragraph instructions.
    /// </summary>
    public static string ComposeUserPrompt(GeologicalContext ctx)
    {
        // Serialise a pruned context snapshot (avoid sending huge lists of cell indices)
        var snapshot = BuildContextSnapshot(ctx);
        var json = JsonSerializer.Serialize(snapshot, _jsonOpts);

        var sb = new StringBuilder();
        sb.AppendLine("GEOLOGICAL CONTEXT (JSON):");
        sb.AppendLine(json);
        sb.AppendLine();
        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine("Write a 4–6 paragraph encyclopedia entry for the primary feature at this location.");
        sb.AppendLine("Paragraph 1: Tectonic origin — how the feature formed, plate kinematics, timescale.");
        sb.AppendLine("Paragraph 2: Lithology and stratigraphy — rock types, deformation, any extraordinary event layers visible in the record (name them: \"the {EventId} impact ejecta layer, deposited at simulation year {year}\").");
        sb.AppendLine("Paragraph 3: Erosion, drainage, and river systems — how water and ice shaped the feature; name any rivers, their catchment area, and outlet seas.");
        sb.AppendLine("Paragraph 4: Climate coupling — orographic effects, rain shadow, monsoon, hurricane corridor, ocean current influence.");
        sb.AppendLine("Paragraph 5: Biome and ecology cascade — how the feature determines surrounding biome distribution.");
        if (ctx.PrimaryFeatureHistory.Count > 5)
        {
            sb.AppendLine("Paragraph 6: Historical biography — major changes in the feature's identity (splits, merges, submergences, renames), with simulation-year dates.");
        }
        sb.AppendLine("End with a 2-sentence summary of what makes this feature geologically significant on this planet.");
        sb.AppendLine("Return ONLY the paragraphs as plain prose. Do not include headings or JSON.");

        return sb.ToString();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static object BuildContextSnapshot(GeologicalContext ctx)
    {
        return new
        {
            location = new
            {
                lat             = ctx.Lat,
                lon             = ctx.Lon,
                simAge          = ctx.SimAgeDescription,
            },
            tectonic = new
            {
                plateId         = ctx.CurrentPlate?.Id,
                plateName       = ctx.CurrentPlate?.Id.ToString(),
                marginType      = ctx.NearestMarginType.ToString(),
                distanceKm      = ctx.DistanceToPlateMarginKm,
                collidingPlate  = ctx.CollidingPlate?.Id.ToString(),
                subductingPlate = ctx.SubductingPlate?.Id.ToString(),
                convergenceCmPer= ctx.ConvergenceRateCmPerYear,
            },
            hydrology = new
            {
                riverName       = ctx.RiverName,
                riverLengthKm   = ctx.RiverLengthKm,
                catchmentKm2    = ctx.CatchmentAreaKm2,
                outletOcean     = ctx.RiverOutletOcean,
                watershed       = ctx.WatershedName,
                endorheic       = ctx.IsInEndorheicBasin,
                gradient        = ctx.DrainageGradient,
            },
            orography = new
            {
                inMountainRange = ctx.IsInMountainRange,
                rangeName       = ctx.RangeName,
                rangeMaxElev    = ctx.RangeMaxElevationM,
                windward        = ctx.IsOnWindwardSide,
                rainShadow      = ctx.HasRainShadow,
                originType      = ctx.MountainOriginType,
            },
            climate = new
            {
                biome           = ctx.BiomeType,
                tempC           = ctx.MeanTempC,
                precipMm        = ctx.MeanPrecipMm,
                monsoon         = ctx.IsInMonsoonZone,
                hurricane       = ctx.IsInHurricaneCorridor,
                jetStream       = ctx.IsInJetStreamZone,
                oceanCurrent    = ctx.NearestOceanCurrentName,
                warmCurrent     = ctx.NearestCurrentIsWarm,
            },
            primaryFeature = new
            {
                name            = ctx.PrimaryLandFeature?.Current.Name ?? ctx.PrimaryWaterFeature?.Current.Name,
                type            = (ctx.PrimaryLandFeature?.Type ?? ctx.PrimaryWaterFeature?.Type)?.ToString(),
            },
            extraordinaryLayers = ctx.ExtraordinaryLayers.Select(l => new
            {
                eventType     = l.EventType.ToString(),
                eventId       = l.EventId,
                thicknessM    = l.Thickness,
                isotopeAnom   = l.IsotopeAnomaly,
                sootPpm       = l.SootConcentrationPpm,
                isGlobal      = l.IsGlobal,
            }),
            historyLength = ctx.PrimaryFeatureHistory.Count,
            nearbyFeatures = ctx.NearbyFeatures.Take(6).Select(n => new
            {
                name        = n.Feature.Current.Name,
                type        = n.Feature.Type.ToString(),
                distanceKm  = n.DistanceKm,
            }),
        };
    }
}
