using GeoTime.Core.Models;
using GeoTime.Core.Services;

namespace GeoTime.Tests;

/// <summary>Tests for the L2 feature detector service.</summary>
public class FeatureDetectorTests
{
    private const uint TestSeed = 42u;

    // ── Continent / Ocean split ───────────────────────────────────────────────

    [Fact]
    public void Detector_32x32_DetectsLandAndOcean()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        // Fill top half with land (height 500 m), bottom half with ocean (-3000 m)
        for (int row = 0; row < gs / 2; row++)
        for (int col = 0; col < gs; col++)
            state.HeightMap[row * gs + col] = 500f;
        for (int row = gs / 2; row < gs; row++)
        for (int col = 0; col < gs; col++)
            state.HeightMap[row * gs + col] = -3000f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        var registry = state.FeatureRegistry;
        Assert.NotEmpty(registry.Features);

        var continents = registry.Features.Values
            .Where(f => f.Type is FeatureType.Continent or FeatureType.Island).ToList();
        var oceans = registry.Features.Values
            .Where(f => f.Type is FeatureType.Ocean or FeatureType.Sea).ToList();

        Assert.NotEmpty(continents);
        Assert.NotEmpty(oceans);
    }

    [Fact]
    public void Detector_32x32_LandAreaCalculationIsPositive()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        // Single land blob in center
        for (int row = 10; row < 22; row++)
        for (int col = 10; col < 22; col++)
            state.HeightMap[row * gs + col] = 500f;
        // Rest is ocean
        for (int i = 0; i < state.CellCount; i++)
            if (state.HeightMap[i] == 0f) state.HeightMap[i] = -3000f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        var landFeature = state.FeatureRegistry.Features.Values
            .FirstOrDefault(f => f.Type is FeatureType.Continent or FeatureType.Island &&
                                 f.Current.AreaKm2 > 0f);
        Assert.NotNull(landFeature);
        Assert.True(landFeature.Current.AreaKm2 > 0f);
    }

    [Fact]
    public void Detector_32x32_CentroidIsInsideLandArea()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        // Land blob at rows 0-4 (near north pole)
        for (int row = 0; row < 5; row++)
        for (int col = 0; col < gs; col++)
            state.HeightMap[row * gs + col] = 500f;
        for (int i = 0; i < state.CellCount; i++)
            if (state.HeightMap[i] == 0f) state.HeightMap[i] = -3000f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        var landFeature = state.FeatureRegistry.Features.Values
            .FirstOrDefault(f => f.Type is FeatureType.Continent or FeatureType.Island);
        Assert.NotNull(landFeature);
        // North-pole land blob should have centroid with lat > 60°
        Assert.True(landFeature.Current.CenterLat > 60f,
            $"Expected CenterLat > 60 but got {landFeature.Current.CenterLat}");
    }

    [Fact]
    public void Detector_32x32_OceanAreaLargerThanLandArea()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        // ~30 % land
        int landCutoffRow = gs * 3 / 10;
        for (int row = 0; row < landCutoffRow; row++)
        for (int col = 0; col < gs; col++)
            state.HeightMap[row * gs + col] = 500f;
        for (int i = 0; i < state.CellCount; i++)
            if (state.HeightMap[i] == 0f) state.HeightMap[i] = -3000f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        float landArea  = state.FeatureRegistry.Features.Values
            .Where(f => f.Type is FeatureType.Continent or FeatureType.Island)
            .Sum(f => f.Current.AreaKm2);
        float oceanArea = state.FeatureRegistry.Features.Values
            .Where(f => f.Type is FeatureType.Ocean or FeatureType.Sea)
            .Sum(f => f.Current.AreaKm2);

        Assert.True(oceanArea > landArea,
            $"Ocean area {oceanArea} should exceed land area {landArea}");
    }

    // ── Mountain ranges ───────────────────────────────────────────────────────

    [Fact]
    public void Detector_32x32_DetectsMountainRange()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        // Ocean everywhere
        for (int i = 0; i < state.CellCount; i++) state.HeightMap[i] = -2000f;
        // Mountain cluster in center
        for (int row = 12; row < 20; row++)
        for (int col = 12; col < 20; col++)
            state.HeightMap[row * gs + col] = 3000f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        var mountains = state.FeatureRegistry.Features.Values
            .Where(f => f.Type == FeatureType.MountainRange).ToList();
        Assert.NotEmpty(mountains);
        Assert.True(mountains[0].Metrics["max_elevation_m"] >= 3000f);
    }

    [Fact]
    public void Detector_32x32_RainShadowFlagSetWhenPrecipDeltaHigh()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        for (int i = 0; i < state.CellCount; i++) state.HeightMap[i] = -2000f;

        // Mountain cluster rows 14-18, cols 14-18
        for (int row = 14; row < 19; row++)
        for (int col = 14; col < 19; col++)
            state.HeightMap[row * gs + col] = 3000f;

        // High precip on west side (col 13)
        for (int row = 14; row < 19; row++)
            state.PrecipitationMap[row * gs + 13] = 2000f;
        // Low precip on east side (col 19)
        for (int row = 14; row < 19; row++)
            state.PrecipitationMap[row * gs + 19] = 100f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        var mountains = state.FeatureRegistry.Features.Values
            .Where(f => f.Type == FeatureType.MountainRange).ToList();
        Assert.NotEmpty(mountains);
        // At least one mountain range should have a non-zero precip delta
        Assert.Contains(mountains, m => m.Metrics.TryGetValue("precip_delta_windward_mm", out var v) && v > 0f);
    }

    // ── Tectonic plates ───────────────────────────────────────────────────────

    [Fact]
    public void Detector_32x32_DetectsTectonicPlates()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        var plates = new List<PlateInfo>
        {
            new() { Id = 0, IsOceanic = true,  Area = 500, CenterLat = 0, CenterLon = 0,
                    AngularVelocity = new AngularVelocity { Lat = 0, Lon = 0, Rate = 0 } },
            new() { Id = 1, IsOceanic = false, Area = 500, CenterLat = 45, CenterLon = 90,
                    AngularVelocity = new AngularVelocity { Lat = 0, Lon = 0, Rate = 0 } },
        };
        // Half grid each plate
        for (int i = 0; i < state.CellCount / 2; i++) state.PlateMap[i] = 0;
        for (int i = state.CellCount / 2; i < state.CellCount; i++) state.PlateMap[i] = 1;

        var detector = new FeatureDetectorService();
        detector.Detect(state, plates, [], [], TestSeed, 1L);

        var tectonicPlates = state.FeatureRegistry.Features.Values
            .Where(f => f.Type == FeatureType.TectonicPlate).ToList();
        Assert.Equal(2, tectonicPlates.Count);
    }

    // ── Hotspot chains ────────────────────────────────────────────────────────

    [Fact]
    public void Detector_HotspotChain_GroupsNearbyHotspots()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        var hotspots = new List<HotspotInfo>
        {
            new() { Lat = 10, Lon = 10, Strength = 0.8 },
            new() { Lat = 15, Lon = 12, Strength = 0.7 }, // close to first
            new() { Lat = -50, Lon = -120, Strength = 0.6 }, // far away
        };

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], hotspots, [], TestSeed, 1L);

        var chains = state.FeatureRegistry.Features.Values
            .Where(f => f.Type == FeatureType.HotspotChain).ToList();
        Assert.Equal(2, chains.Count); // one for the close pair, one for the distant single
    }

    [Fact]
    public void Detector_HotspotChain_CountIsCorrect()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        var hotspots = new List<HotspotInfo>
        {
            new() { Lat = 0, Lon = 0,   Strength = 1.0 },
            new() { Lat = 5, Lon = 5,   Strength = 0.9 },
            new() { Lat = 10, Lon = 10, Strength = 0.8 },
        };

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], hotspots, [], TestSeed, 1L);

        var chain = state.FeatureRegistry.Features.Values
            .First(f => f.Type == FeatureType.HotspotChain);
        Assert.Equal(3f, chain.Metrics["hotspot_count"]);
    }

    // ── Impact basins ─────────────────────────────────────────────────────────

    [Fact]
    public void Detector_ImpactBasin_RegisteredFromEventLog()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        var events = new List<GeoLogEntry>
        {
            new() { TimeMa = 100.0, Type = "IMPACT", Description = "Large impactor",
                    Location = new LatLon(20.0, 45.0) },
        };

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], events, TestSeed, 1L);

        var basins = state.FeatureRegistry.Features.Values
            .Where(f => f.Type == FeatureType.ImpactBasin).ToList();
        Assert.Single(basins);
        Assert.InRange(basins[0].Current.CenterLat, 19.9f, 20.1f);
    }

    // ── Registry update ───────────────────────────────────────────────────────

    [Fact]
    public void Detector_UpdatesTickOnRegistry()
    {
        const int gs = 32;
        var state = new SimulationState(gs);

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 7L);

        Assert.Equal(7L, state.FeatureRegistry.LastUpdatedTick);
    }

    [Fact]
    public void Detector_AllFeaturesHaveNonEmptyName()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        for (int i = 0; i < state.CellCount; i++)
            state.HeightMap[i] = i < state.CellCount / 3 ? 500f : -2000f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        foreach (var feat in state.FeatureRegistry.Features.Values)
            Assert.False(string.IsNullOrWhiteSpace(feat.Current.Name),
                $"Feature {feat.Id} has empty name");
    }

    [Fact]
    public void Detector_AllFeaturesHaveValidId()
    {
        const int gs = 32;
        var state = new SimulationState(gs);
        for (int i = 0; i < state.CellCount; i++)
            state.HeightMap[i] = i < state.CellCount / 2 ? 0f : -2000f;

        var detector = new FeatureDetectorService();
        detector.Detect(state, [], [], [], TestSeed, 1L);

        foreach (var (id, feat) in state.FeatureRegistry.Features)
        {
            Assert.Equal(id, feat.Id);
            Assert.False(string.IsNullOrWhiteSpace(id));
        }
    }
}
