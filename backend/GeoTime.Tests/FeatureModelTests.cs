using GeoTime.Core.Models;
using GeoTime.Core.Services;
using System.Text.Json;

namespace GeoTime.Tests;

/// <summary>Tests for the L1 feature data models and name generator.</summary>
public class FeatureModelTests
{
    // ── FeatureModels ─────────────────────────────────────────────────────────

    [Fact]
    public void FeatureRegistry_StartsEmpty()
    {
        var reg = new FeatureRegistry();
        Assert.Empty(reg.Features);
        Assert.Equal(0L, reg.LastUpdatedTick);
    }

    [Fact]
    public void DetectedFeature_CurrentReturnsLastSnapshot()
    {
        var snap1 = new FeatureSnapshot(0, long.MaxValue, "Alpha", 10f, 20f, 5000f,
            FeatureStatus.Active, null, null, null);
        var snap2 = new FeatureSnapshot(5, long.MaxValue, "Greater Alpha", 10f, 20f, 7000f,
            FeatureStatus.Active, null, null, null);
        var feature = new DetectedFeature { Id = "test_0001", Type = FeatureType.Continent };
        feature.History.Add(snap1);
        feature.History.Add(snap2);

        Assert.Equal("Greater Alpha", feature.Current.Name);
        Assert.Equal(2, feature.History.Count);
    }

    [Fact]
    public void FeatureSnapshot_IsRecord_ValueEquality()
    {
        var a = new FeatureSnapshot(0, long.MaxValue, "Sea of X", 5f, 10f, 1_000_000f,
            FeatureStatus.Active, null, null, null);
        var b = new FeatureSnapshot(0, long.MaxValue, "Sea of X", 5f, 10f, 1_000_000f,
            FeatureStatus.Active, null, null, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void SimulationState_HasFeatureRegistry()
    {
        var state = new SimulationState(32);
        Assert.NotNull(state.FeatureRegistry);
        Assert.Empty(state.FeatureRegistry.Features);
    }

    [Fact]
    public void FeatureRegistry_CanBeReplacedOnState()
    {
        var state = new SimulationState(32);
        var newReg = new FeatureRegistry { LastUpdatedTick = 42L };
        newReg.Features["ocean_0000"] = new DetectedFeature { Id = "ocean_0000", Type = FeatureType.Ocean };
        state.FeatureRegistry = newReg;

        Assert.Equal(42L, state.FeatureRegistry.LastUpdatedTick);
        Assert.Single(state.FeatureRegistry.Features);
    }

    [Fact]
    public void FeatureRegistry_SerializesToJson()
    {
        var reg = new FeatureRegistry { LastUpdatedTick = 10L };
        var feature = new DetectedFeature { Id = "continent_0000", Type = FeatureType.Continent };
        var snap = new FeatureSnapshot(0, long.MaxValue, "Kaldor", 15f, -30f, 2_000_000f,
            FeatureStatus.Active, null, null, null);
        feature.History.Add(snap);
        reg.Features["continent_0000"] = feature;

        var json = JsonSerializer.Serialize(reg);
        Assert.Contains("Kaldor", json);
        Assert.Contains("continent_0000", json);
    }

    // ── FeatureNameGenerator ─────────────────────────────────────────────────

    [Fact]
    public void NameGenerator_IsDeterministic()
    {
        var name1 = FeatureNameGenerator.Generate(42u, FeatureType.Ocean, 0);
        var name2 = FeatureNameGenerator.Generate(42u, FeatureType.Ocean, 0);
        Assert.Equal(name1, name2);
    }

    [Fact]
    public void NameGenerator_DifferentSeeds_ProduceDifferentNames()
    {
        var name1 = FeatureNameGenerator.Generate(1u, FeatureType.Continent, 0);
        var name2 = FeatureNameGenerator.Generate(2u, FeatureType.Continent, 0);
        // Seeds 1 and 2 should produce different names
        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void NameGenerator_UniqueNamesFor200Oceans()
    {
        const uint seed = 999u;
        var names = Enumerable.Range(0, 200)
            .Select(i => FeatureNameGenerator.Generate(seed, FeatureType.Ocean, i))
            .ToList();
        var unique = new HashSet<string>(names);
        // Allow a small collision rate (≤ 5%) for the 200-name set
        Assert.True(unique.Count >= 190, $"Only {unique.Count} unique names out of 200 oceans");
    }

    [Fact]
    public void NameGenerator_UniqueNamesFor200Continents()
    {
        const uint seed = 777u;
        var names = Enumerable.Range(0, 200)
            .Select(i => FeatureNameGenerator.Generate(seed, FeatureType.Continent, i))
            .ToList();
        var unique = new HashSet<string>(names);
        Assert.True(unique.Count >= 190, $"Only {unique.Count} unique names out of 200 continents");
    }

    [Fact]
    public void NameGenerator_UniqueNamesFor200Mountains()
    {
        const uint seed = 123u;
        var names = Enumerable.Range(0, 200)
            .Select(i => FeatureNameGenerator.Generate(seed, FeatureType.MountainRange, i))
            .ToList();
        var unique = new HashSet<string>(names);
        Assert.True(unique.Count >= 190, $"Only {unique.Count} unique mountain names out of 200");
    }

    [Fact]
    public void NameGenerator_UniqueNamesFor200Rivers()
    {
        const uint seed = 456u;
        var names = Enumerable.Range(0, 200)
            .Select(i => FeatureNameGenerator.Generate(seed, FeatureType.River, i))
            .ToList();
        var unique = new HashSet<string>(names);
        Assert.True(unique.Count >= 190, $"Only {unique.Count} unique river names out of 200");
    }

    [Fact]
    public void NameGenerator_ProducesNonEmptyNames()
    {
        foreach (FeatureType type in Enum.GetValues<FeatureType>())
        {
            var name = FeatureNameGenerator.Generate(42u, type, 0);
            Assert.False(string.IsNullOrWhiteSpace(name), $"Empty name for type {type}");
        }
    }

    [Fact]
    public void NameGenerator_OceanNames_StartWithCapitalLetter()
    {
        for (int i = 0; i < 20; i++)
        {
            var name = FeatureNameGenerator.Generate(42u, FeatureType.Ocean, i);
            Assert.True(char.IsUpper(name[0]), $"Ocean name '{name}' does not start with capital");
        }
    }

    // ── Name Evolution ────────────────────────────────────────────────────────

    [Fact]
    public void NameEvolution_Split_AddsDirectionalPrefix()
    {
        var evolved = FeatureNameGenerator.Evolve("Kaldor", NameChangeReason.Split, 42u, FeatureType.Continent, 0);
        Assert.True(
            evolved.StartsWith("North ") || evolved.StartsWith("South ") ||
            evolved.StartsWith("East ")  || evolved.StartsWith("West ")  ||
            evolved.StartsWith("Greater ") || evolved.StartsWith("Lesser ") ||
            evolved.StartsWith("New ") || evolved.StartsWith("Upper ") || evolved.StartsWith("Lower "),
            $"Split name '{evolved}' does not have expected directional prefix");
    }

    [Fact]
    public void NameEvolution_Submergence_AddsPrefix()
    {
        var evolved = FeatureNameGenerator.Evolve("Kaldor", NameChangeReason.Submergence, 42u, FeatureType.Continent, 0);
        Assert.StartsWith("Sunken ", evolved);
    }

    [Fact]
    public void NameEvolution_IsDeterministic()
    {
        var e1 = FeatureNameGenerator.Evolve("Kaldor", NameChangeReason.Merge, 42u, FeatureType.Continent, 5);
        var e2 = FeatureNameGenerator.Evolve("Kaldor", NameChangeReason.Merge, 42u, FeatureType.Continent, 5);
        Assert.Equal(e1, e2);
    }
}
