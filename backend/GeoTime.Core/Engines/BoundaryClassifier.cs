using GeoTime.Core.Models;

namespace GeoTime.Core.Engines;

/// <summary>
/// Classifies plate boundaries as convergent, divergent, or transform
/// based on relative plate velocity at each boundary cell.
/// </summary>
public sealed class BoundaryClassifier
{
    private const double TWO_PI = 2 * Math.PI;
    private const double DEG2RAD = Math.PI / 180;

    private static double RowToLat(int row, int gs) => Math.PI / 2 - (double)row / gs * Math.PI;
    private static double ColToLon(int col, int gs) => (double)col / gs * TWO_PI - Math.PI;

    public static (double vLat, double vLon) PlateVelocityAt(PlateInfo plate, double lat, double lon)
    {
        double poleLat = plate.AngularVelocity.Lat * DEG2RAD;
        double poleLon = plate.AngularVelocity.Lon * DEG2RAD;
        double omega = plate.AngularVelocity.Rate;
        double dLon = lon - poleLon;
        double vLat = omega * Math.Cos(poleLat) * Math.Sin(dLon);
        double vLon = omega * (Math.Sin(poleLat) * Math.Cos(lat)
                     - Math.Cos(poleLat) * Math.Sin(lat) * Math.Cos(dLon));
        return (vLat, vLon);
    }

    public static int[] GetNeighborIndices(int row, int col, int gs)
    {
        var n = new List<int>(4);
        if (row > 0) n.Add((row - 1) * gs + col);
        if (row < gs - 1) n.Add((row + 1) * gs + col);
        n.Add(row * gs + ((col - 1 + gs) % gs));
        n.Add(row * gs + ((col + 1) % gs));
        return n.ToArray();
    }

    public List<BoundaryCell> Classify(ushort[] plateMap, List<PlateInfo> plates, int gridSize)
    {
        var boundaries = new List<BoundaryCell>();
        for (int row = 0; row < gridSize; row++)
        {
            for (int col = 0; col < gridSize; col++)
            {
                int idx = row * gridSize + col;
                int myPlate = plateMap[idx];
                var neighbors = GetNeighborIndices(row, col, gridSize);

                foreach (int nIdx in neighbors)
                {
                    int neighborPlate = plateMap[nIdx];
                    if (neighborPlate == myPlate) continue;

                    double lat = RowToLat(row, gridSize);
                    double lon = ColToLon(col, gridSize);
                    var (v1Lat, v1Lon) = PlateVelocityAt(plates[myPlate], lat, lon);
                    var (v2Lat, v2Lon) = PlateVelocityAt(plates[neighborPlate], lat, lon);

                    double dvLat = v1Lat - v2Lat, dvLon = v1Lon - v2Lon;
                    double relSpeed = Math.Sqrt(dvLat * dvLat + dvLon * dvLon);

                    int nRow = nIdx / gridSize, nCol = nIdx % gridSize;
                    double nLat = RowToLat(nRow, gridSize), nLon = ColToLon(nCol, gridSize);
                    double normalLat = nLat - lat, normalLon = nLon - lon;
                    double normalLen = Math.Sqrt(normalLat * normalLat + normalLon * normalLon);

                    var boundaryType = BoundaryType.TRANSFORM;
                    if (normalLen > 1e-10)
                    {
                        double dot = (dvLat * normalLat + dvLon * normalLon) / normalLen;
                        double threshold = relSpeed * 0.3;
                        if (dot < -threshold) boundaryType = BoundaryType.CONVERGENT;
                        else if (dot > threshold) boundaryType = BoundaryType.DIVERGENT;
                    }

                    boundaries.Add(new BoundaryCell
                    {
                        CellIndex = idx, Type = boundaryType,
                        Plate1 = myPlate, Plate2 = neighborPlate,
                        RelativeSpeed = relSpeed,
                    });
                    break;
                }
            }
        }
        return boundaries;
    }
}
