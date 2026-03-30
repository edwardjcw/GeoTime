using GeoTime.Core.Models;
using GeoTime.Core.Engines;

namespace GeoTime.Tests;

public class WeatheringTests
{
    [Fact]
    public void GetWeatheringProduct_TropicalWet_ReturnsLaterite()
    {
        var engine = new WeatheringEngine(8);
        Assert.Equal(RockType.SED_LATERITE,
            engine.GetWeatheringProduct(RockType.IGN_GRANITE, 25, 1500));
    }

    [Fact]
    public void GetWeatheringProduct_Arid_ReturnsCaliche()
    {
        var engine = new WeatheringEngine(8);
        Assert.Equal(RockType.SED_CALICHE,
            engine.GetWeatheringProduct(RockType.IGN_GRANITE, 30, 100));
    }

    [Fact]
    public void GetWeatheringProduct_Limestone_ReturnsRegolith()
    {
        var engine = new WeatheringEngine(8);
        Assert.Equal(RockType.SED_REGOLITH,
            engine.GetWeatheringProduct(RockType.SED_LIMESTONE, 15, 800));
    }

    [Fact]
    public void ChemicalWeatheringRate_BelowMinTemp_ReturnsZero()
    {
        var engine = new WeatheringEngine(8);
        Assert.Equal(0, engine.ChemicalWeatheringRate(-15, 500));
    }

    [Fact]
    public void ChemicalWeatheringRate_WarmWet_HigherThanColdDry()
    {
        var engine = new WeatheringEngine(8);
        double warmWet = engine.ChemicalWeatheringRate(25, 1500);
        double coldDry = engine.ChemicalWeatheringRate(5, 200);
        Assert.True(warmWet > coldDry);
    }
}
