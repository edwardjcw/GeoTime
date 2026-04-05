using GeoTime.Core.Models;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Services;

/// <summary>
/// Deterministic syllable-assembly name generator for geographic features.
/// Each name is derived from (planetSeed, featureType, featureIndex), guaranteeing
/// the same seed always produces the same name for the same feature slot.
/// </summary>
public static class FeatureNameGenerator
{
    // ── Phoneme banks ──────────────────────────────────────────────────────────

    // Oceanic: flowing, open vowels, liquids (l, r), nasals (m, n)
    private static readonly string[] OceanicOnsets =
        ["M", "N", "V", "L", "R", "Mer", "Vel", "Nar", "Lom", "Sol", "Tor", "Val",
         "Mor", "Sal", "Nel", "Lor", "Mar", "Nor", "Vel", "Rav"];

    private static readonly string[] OceanicNuclei =
        ["a", "e", "o", "ae", "ea", "ia", "ua", "io", "eo", "au", "ei", "ui", "oa"];

    private static readonly string[] OceanicCodas =
        ["n", "r", "l", "m", "nd", "nt", "ns", "rd", "rm", "rn", "ln", "mn", "lm", ""];

    // Continental: short, consonant-heavy, hard stops (k, t, d, b, g)
    private static readonly string[] ContinentOnsets =
        ["K", "T", "D", "B", "G", "Ak", "Tal", "Dor", "Bor", "Gal", "Kar", "Tar",
         "Dal", "Bar", "Gor", "Kol", "Tarn", "Dag", "Bak", "Gard"];

    private static readonly string[] ContinentNuclei =
        ["a", "o", "u", "ar", "or", "al", "ul", "ir", "an", "on", "un", "ok", "ak"];

    private static readonly string[] ContinentCodas =
        ["k", "t", "d", "n", "r", "g", "nd", "nt", "rd", "rg", "ld", "lt", "nk", ""];

    // Mountain: hard stops, fricatives (f, s, sh), resonant endings
    private static readonly string[] MountainOnsets =
        ["Kr", "Tr", "Dr", "Gr", "Sk", "Sp", "St", "Sh", "Fr", "Sv", "Kol", "Tri",
         "Drak", "Grav", "Skarn", "Spir", "Ston", "Shor", "Fros", "Svar"];

    private static readonly string[] MountainNuclei =
        ["a", "e", "i", "o", "u", "ar", "er", "ir", "or", "ur", "al", "el", "ol"];

    private static readonly string[] MountainCodas =
        ["k", "t", "n", "r", "l", "m", "nd", "nt", "rk", "lt", "lm", "rn", "th", ""];

    // River: liquids (l, r), fricatives (f, v, s, z), flowing vowels
    private static readonly string[] RiverOnsets =
        ["R", "L", "V", "F", "S", "Z", "Fl", "Sl", "Vl", "Ral", "Lov", "Ser", "Zen",
         "Far", "Lun", "Var", "Fal", "Sal", "Ran", "Lorn"];

    private static readonly string[] RiverNuclei =
        ["a", "e", "i", "u", "ar", "er", "ir", "al", "el", "il", "an", "en", "in", "un"];

    private static readonly string[] RiverCodas =
        ["n", "r", "l", "m", "nd", "nt", "ns", "rn", "ln", "mn", "lv", "rv", "ls", ""];

    // Generic (fallback)
    private static readonly string[] GenericOnsets =
        ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
         "N", "O", "P", "R", "S", "T", "U", "V", "Z",
         "Al", "Bel", "Cor", "Del", "Elm", "Fer", "Gal", "Hel", "Ith", "Jel"];

    private static readonly string[] GenericNuclei =
        ["a", "e", "i", "o", "u", "ar", "er", "ir", "or", "ur"];

    private static readonly string[] GenericCodas =
        ["", "n", "r", "l", "m", "nd", "nt", "rd", "rn", "ln"];

    // Descriptors appended to compound names
    private static readonly string[] OceanDescriptors =
        ["Sea", "Ocean", "Gulf", "Bay", "Deep", "Basin", "Passage", "Narrows", "Sound", "Reach"];

    private static readonly string[] ContinentDescriptors =
        ["Land", "Plains", "Basin", "Plateau", "Tableland", "Vale", "Lowlands", "Highlands", "Steppe", "Marches"];

    private static readonly string[] MountainDescriptors =
        ["Mountains", "Range", "Ridge", "Peaks", "Massif", "Heights", "Crags", "Spine", "Wall", "Highlands"];

    private static readonly string[] RiverDescriptors =
        ["River", "Creek", "Run", "Stream", "Flow", "Current", "Fork", "Reach", "Bend", "Falls"];

    private static readonly string[] GenericDescriptors =
        ["", "Minor", "Major", "Inner", "Outer", "Far", "Near", "Deep", "High", "Low"];

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate a deterministic name for a feature.
    /// </summary>
    /// <param name="planetSeed">Global planet seed.</param>
    /// <param name="type">Feature type.</param>
    /// <param name="featureIndex">Zero-based index among all features of this type.</param>
    public static string Generate(uint planetSeed, FeatureType type, int featureIndex)
    {
        var rng = MakeRng(planetSeed, type, featureIndex);
        var (onsets, nuclei, codas, descriptors) = GetBanks(type);

        var root = BuildRoot(rng, onsets, nuclei, codas);

        // ~40 % chance to add a descriptor
        if (rng.Next() < 0.40)
        {
            var desc = descriptors[rng.NextInt(0, descriptors.Length - 1)];
            if (!string.IsNullOrEmpty(desc))
                return $"{root} {desc}";
        }

        return root;
    }

    /// <summary>
    /// Generate an evolved name from an existing name given a change reason.
    /// </summary>
    public static string Evolve(string existingName, NameChangeReason reason, uint planetSeed, FeatureType type, int featureIndex)
    {
        var rng = MakeRng(planetSeed ^ 0xDEAD_BEEFu, type, featureIndex + 10_000);

        return reason switch
        {
            NameChangeReason.Split          => AddDirectionalPrefix(rng, existingName),
            NameChangeReason.Merge          => BuildPortmanteau(rng, existingName),
            NameChangeReason.ClimateShift   => AddClimateSuffix(rng, existingName),
            NameChangeReason.RenameByAge    => AddAgeSuffix(rng, existingName),
            NameChangeReason.Submergence    => $"Sunken {existingName}",
            NameChangeReason.Exposure       => Generate(planetSeed, type, featureIndex + 50_000),
            _                               => existingName,
        };
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static Xoshiro256ss MakeRng(uint planetSeed, FeatureType type, int featureIndex)
    {
        uint seed = planetSeed ^ ((uint)type * 2_654_435_761u) ^ ((uint)featureIndex * 40_503u);
        return new Xoshiro256ss(seed == 0 ? 1u : seed);
    }

    private static (string[] onsets, string[] nuclei, string[] codas, string[] descriptors)
        GetBanks(FeatureType type) => type switch
        {
            FeatureType.Ocean or FeatureType.Sea or FeatureType.InlandSea =>
                (OceanicOnsets, OceanicNuclei, OceanicCodas, OceanDescriptors),
            FeatureType.Continent or FeatureType.Island or FeatureType.IslandChain
                or FeatureType.TectonicPlate =>
                (ContinentOnsets, ContinentNuclei, ContinentCodas, ContinentDescriptors),
            FeatureType.MountainRange or FeatureType.MountainPeak or FeatureType.Rift
                or FeatureType.SubductionZone or FeatureType.ImpactBasin =>
                (MountainOnsets, MountainNuclei, MountainCodas, MountainDescriptors),
            FeatureType.River or FeatureType.RiverDelta or FeatureType.Lake =>
                (RiverOnsets, RiverNuclei, RiverCodas, RiverDescriptors),
            _ =>
                (GenericOnsets, GenericNuclei, GenericCodas, GenericDescriptors),
        };

    private static string BuildRoot(Xoshiro256ss rng, string[] onsets, string[] nuclei, string[] codas)
    {
        // 1–2 syllables
        var syllableCount = rng.NextInt(1, 2);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < syllableCount; i++)
        {
            sb.Append(i == 0
                ? onsets[rng.NextInt(0, onsets.Length - 1)]
                : onsets[rng.NextInt(0, onsets.Length - 1)].ToLowerInvariant());
            sb.Append(nuclei[rng.NextInt(0, nuclei.Length - 1)]);
            if (i == syllableCount - 1)
                sb.Append(codas[rng.NextInt(0, codas.Length - 1)]);
        }
        return sb.ToString();
    }

    private static string AddDirectionalPrefix(Xoshiro256ss rng, string name)
    {
        string[] prefixes = ["North ", "South ", "East ", "West ", "Greater ", "Lesser ", "New ", "Upper ", "Lower "];
        return prefixes[rng.NextInt(0, prefixes.Length - 1)] + name;
    }

    private static string BuildPortmanteau(Xoshiro256ss rng, string name)
    {
        // Simple approach: append a short suffix derived from RNG
        string[] suffixes = ["-ath", "-on", "-ora", "-eld", "-and", "-orn", "-al", "-ix"];
        var half = name.Length / 2;
        return name[..half] + suffixes[rng.NextInt(0, suffixes.Length - 1)];
    }

    private static string AddClimateSuffix(Xoshiro256ss rng, string name)
    {
        string[] suffixes = [" Wastes", " Wetlands", " Barrens", " Reach", " Expanse"];
        return name + suffixes[rng.NextInt(0, suffixes.Length - 1)];
    }

    private static string AddAgeSuffix(Xoshiro256ss rng, string name)
    {
        string[] suffixes = [" Ancient", " Deep", " Old", " Far", " Great"];
        return name + suffixes[rng.NextInt(0, suffixes.Length - 1)];
    }
}

/// <summary>Reason for a feature name change.</summary>
public enum NameChangeReason
{
    Split,
    Merge,
    ClimateShift,
    RenameByAge,
    Submergence,
    Exposure,
}
