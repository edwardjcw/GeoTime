using GeoTime.Core.Models;
using GeoTime.Core.Engines;

namespace GeoTime.Tests;

public class PedogenesisTests
{
    [Fact]
    public void ClassifySoil_Gelisol_WhenFreezing()
    {
        var engine = new PedogenesisEngine(8);
        Assert.Equal(SoilOrder.GELISOL, engine.ClassifySoil(-5, 500, RockType.SED_SANDSTONE, 1, 500));
    }

    [Fact]
    public void ClassifySoil_Andisol_VolcanicParent()
    {
        var engine = new PedogenesisEngine(8);
        Assert.Equal(SoilOrder.ANDISOL, engine.ClassifySoil(15, 500, RockType.IGN_BASALT, 1, 500));
    }

    [Fact]
    public void ClassifySoil_Entisol_ShallowSoil()
    {
        var engine = new PedogenesisEngine(8);
        Assert.Equal(SoilOrder.ENTISOL, engine.ClassifySoil(15, 500, RockType.SED_SANDSTONE, 0.1, 500));
    }

    [Fact]
    public void ClassifySoil_Aridisol_LowPrecip()
    {
        var engine = new PedogenesisEngine(8);
        Assert.Equal(SoilOrder.ARIDISOL, engine.ClassifySoil(25, 100, RockType.SED_SANDSTONE, 1, 500));
    }

    [Fact]
    public void ClassifySoil_Oxisol_TropicalWet()
    {
        var engine = new PedogenesisEngine(8);
        Assert.Equal(SoilOrder.OXISOL, engine.ClassifySoil(25, 2000, RockType.SED_SANDSTONE, 1, 500));
    }

    [Fact]
    public void ClassifySoil_Mollisol_TemperateGrassland()
    {
        var engine = new PedogenesisEngine(8);
        // precip 500 is in Mollisol range (400-800) but below Alfisol min (600)
        Assert.Equal(SoilOrder.MOLLISOL, engine.ClassifySoil(3, 500, RockType.SED_SANDSTONE, 1, 500));
    }

    [Fact]
    public void SoilFormationRate_ZeroBelowMinTemp()
    {
        var engine = new PedogenesisEngine(8);
        Assert.Equal(0, engine.SoilFormationRate(-25, 500, RockType.SED_SANDSTONE));
    }

    [Fact]
    public void SoilFormationRate_ZeroBelowMinPrecip()
    {
        var engine = new PedogenesisEngine(8);
        Assert.Equal(0, engine.SoilFormationRate(20, 5, RockType.SED_SANDSTONE));
    }

    [Fact]
    public void SoilFormationRate_PositiveForFavorableConditions()
    {
        var engine = new PedogenesisEngine(8);
        double rate = engine.SoilFormationRate(20, 1000, RockType.SED_SANDSTONE);
        Assert.True(rate > 0);
    }
}
