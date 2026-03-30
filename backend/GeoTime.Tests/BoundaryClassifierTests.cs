using GeoTime.Core.Models;
using GeoTime.Core.Engines;

namespace GeoTime.Tests;

public class BoundaryClassifierTests
{
    [Fact]
    public void Classify_FindsBoundaries()
    {
        int gs = 8;
        var plateMap = new ushort[gs * gs];
        // Left half = plate 0, right half = plate 1
        for (int row = 0; row < gs; row++)
            for (int col = 0; col < gs; col++)
                plateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

        var plates = new List<PlateInfo>
        {
            new() { Id = 0, AngularVelocity = new() { Lat = 0, Lon = 0, Rate = 1 } },
            new() { Id = 1, AngularVelocity = new() { Lat = 0, Lon = 0, Rate = 1 } },
        };

        var classifier = new BoundaryClassifier();
        var boundaries = classifier.Classify(plateMap, plates, gs);
        Assert.NotEmpty(boundaries);
        Assert.All(boundaries, b => Assert.True(b.Plate1 != b.Plate2));
    }

    [Fact]
    public void GetNeighborIndices_ReturnsValidIndices()
    {
        int gs = 8;
        var neighbors = BoundaryClassifier.GetNeighborIndices(0, 0, gs);
        Assert.NotEmpty(neighbors);
        Assert.All(neighbors, n => Assert.InRange(n, 0, gs * gs - 1));
    }

    [Fact]
    public void GetNeighborIndices_WrapsLongitude()
    {
        int gs = 8;
        var neighbors = BoundaryClassifier.GetNeighborIndices(4, 0, gs);
        // Should include column gs-1 (left wrap)
        Assert.Contains(4 * gs + (gs - 1), neighbors);
    }

    [Fact]
    public void PlateVelocityAt_ReturnsFiniteValues()
    {
        var plate = new PlateInfo
        {
            Id = 0, CenterLat = 0, CenterLon = 0,
            AngularVelocity = new() { Lat = 45, Lon = 90, Rate = 2 },
        };
        var (vLat, vLon) = BoundaryClassifier.PlateVelocityAt(plate, 0.5, 0.5);
        Assert.False(double.IsNaN(vLat));
        Assert.False(double.IsNaN(vLon));
    }
}
