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
            WeatheringEngine.GetWeatheringProduct(RockType.IGN_GRANITE, 25, 1500));
    }

    [Fact]
    public void GetWeatheringProduct_Arid_ReturnsCaliche()
    {
        var engine = new WeatheringEngine(8);
        Assert.Equal(RockType.SED_CALICHE,
            WeatheringEngine.GetWeatheringProduct(RockType.IGN_GRANITE, 30, 100));
    }

    [Fact]
    public void GetWeatheringProduct_Limestone_ReturnsRegolith()
    {
        var engine = new WeatheringEngine(8);
        Assert.Equal(RockType.SED_REGOLITH,
            WeatheringEngine.GetWeatheringProduct(RockType.SED_LIMESTONE, 15, 800));
    }

    [Fact]
    public void ChemicalWeatheringRate_BelowMinTemp_ReturnsZero()
    {
        var engine = new WeatheringEngine(8);
        Assert.Equal(0, WeatheringEngine.ChemicalWeatheringRate(-15, 500));
    }

    [Fact]
    public void ChemicalWeatheringRate_WarmWet_HigherThanColdDry()
    {
        var engine = new WeatheringEngine(8);
        var warmWet = WeatheringEngine.ChemicalWeatheringRate(25, 1500);
        var coldDry = WeatheringEngine.ChemicalWeatheringRate(5, 200);
        Assert.True(warmWet > coldDry);
    }
}
