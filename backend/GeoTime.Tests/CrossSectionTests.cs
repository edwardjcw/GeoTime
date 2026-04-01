using GeoTime.Core.Models;
using GeoTime.Core.Engines;

namespace GeoTime.Tests;

public class CrossSectionTests
{
    [Fact]
    public void CentralAngle_SamePoint_ReturnsZero()
    {
        var angle = CrossSectionEngine.CentralAngle(45, 90, 45, 90);
        Assert.Equal(0, angle, 10);
    }

    [Fact]
    public void CentralAngle_OppositePoints()
    {
        var angle = CrossSectionEngine.CentralAngle(0, 0, 0, 180);
        Assert.InRange(angle, Math.PI - 0.01, Math.PI + 0.01);
    }

    [Fact]
    public void GreatCircleInterpolate_Endpoints()
    {
        var start = CrossSectionEngine.GreatCircleInterpolate(0, 0, 45, 90, 0);
        Assert.Equal(0, start.Lat, 5);
        Assert.Equal(0, start.Lon, 5);

        var end = CrossSectionEngine.GreatCircleInterpolate(0, 0, 45, 90, 1);
        Assert.Equal(45, end.Lat, 5);
        Assert.Equal(90, end.Lon, 5);
    }

    [Fact]
    public void LatToRow_BoundaryCases()
    {
        var gs = 512;
        Assert.Equal(0, CrossSectionEngine.LatToRow(90, gs));
        Assert.Equal(gs - 1, CrossSectionEngine.LatToRow(-90, gs));
    }

    [Fact]
    public void LonToCol_BoundaryCases()
    {
        var gs = 512;
        Assert.Equal(0, CrossSectionEngine.LonToCol(-180, gs));
        Assert.Equal(gs - 1, CrossSectionEngine.LonToCol(180, gs));
    }

    [Fact]
    public void ComputePathDistanceKm_ReturnsPositive()
    {
        var pts = new List<LatLon>
        {
            new(0, 0), new(0, 90),
        };
        var dist = CrossSectionEngine.ComputePathDistanceKm(pts);
        Assert.True(dist > 0);
        // Quarter of Earth circumference ≈ 10_018 km
        Assert.InRange(dist, 9000, 11000);
    }

    [Fact]
    public void SamplePathPoints_CorrectCount()
    {
        var pts = new List<LatLon> { new(0, 0), new(45, 90) };
        var samples = CrossSectionEngine.SamplePathPoints(pts, 100);
        Assert.Equal(100, samples.Count);
    }

    [Fact]
    public void SamplePathPoints_SinglePoint_RepeatsIt()
    {
        var pts = new List<LatLon> { new(10, 20) };
        var samples = CrossSectionEngine.SamplePathPoints(pts, 5);
        Assert.Equal(5, samples.Count);
        Assert.All(samples, s => Assert.Equal(10, s.Lat));
    }

    [Fact]
    public void GetDeepEarthZones_Returns7Zones()
    {
        var zones = CrossSectionEngine.GetDeepEarthZones();
        Assert.Equal(7, zones.Count);
        Assert.Equal("Inner Core", zones[^1].Name);
        Assert.Equal(6371, zones[^1].BottomKm);
    }
}
