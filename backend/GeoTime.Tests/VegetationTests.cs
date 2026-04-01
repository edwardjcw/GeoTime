using GeoTime.Core.Engines;

namespace GeoTime.Tests;

public class VegetationTests
{
    [Fact]
    public void ComputeNPP_ZeroWithNoPrecip()
    {
        Assert.Equal(0, VegetationEngine.ComputeNPP(20, 0));
    }

    [Fact]
    public void ComputeNPP_PositiveWithGoodConditions()
    {
        var npp = VegetationEngine.ComputeNPP(25, 1500);
        Assert.True(npp > 0);
        Assert.True(npp <= 3000);
    }

    [Fact]
    public void NppToBiomassRate_ScalesLinearly()
    {
        var r1 = VegetationEngine.NppToBiomassRate(1000);
        var r2 = VegetationEngine.NppToBiomassRate(2000);
        Assert.True(r2 > r1);
    }

    [Fact]
    public void ComputeFireProbability_ZeroWhenLowBiomass()
    {
        Assert.Equal(0, VegetationEngine.ComputeFireProbability(200, 5));
    }

    [Fact]
    public void ComputeFireProbability_IncreasesWithDryness()
    {
        var wet = VegetationEngine.ComputeFireProbability(800, 20);
        var dry = VegetationEngine.ComputeFireProbability(100, 20);
        Assert.True(dry > wet);
    }

    [Fact]
    public void ComputeFireProbability_CappedAtOne()
    {
        var p = VegetationEngine.ComputeFireProbability(10, 40);
        Assert.True(p <= 1.0);
    }
}
