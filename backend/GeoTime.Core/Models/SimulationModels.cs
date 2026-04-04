namespace GeoTime.Core.Models;

/// <summary>Grid constants and state buffer layout.</summary>
public static class GridConstants
{
    public const int GRID_SIZE = 512;
    public const int CELL_COUNT = GRID_SIZE * GRID_SIZE; // 262_144
}

/// <summary>Holds all simulation state arrays for a planet.</summary>
public sealed class SimulationState
{
    public int GridSize { get; }
    public int CellCount { get; }

    public float[] HeightMap { get; }
    public float[] CrustThicknessMap { get; }
    public byte[] RockTypeMap { get; }
    public float[] RockAgeMap { get; }
    public ushort[] PlateMap { get; }
    public byte[] SoilTypeMap { get; }
    public float[] SoilDepthMap { get; }
    public float[] TemperatureMap { get; }
    public float[] PrecipitationMap { get; }
    public float[] WindUMap { get; }
    public float[] WindVMap { get; }
    public byte[] CloudTypeMap { get; }
    public float[] CloudCoverMap { get; }
    public float[] BiomassMap { get; }
    public float[] BiomatterMap { get; }
    public float[] OrganicCarbonMap { get; }

    /// <summary>
    /// Dirty mask: true for cells that changed significantly in the last tick and
    /// need full reprocessing in vegetation/biomatter engines.  Initialized to true
    /// (all dirty) so the first tick processes every cell.
    /// </summary>
    public bool[] DirtyMask { get; }

    public SimulationState(int gridSize = GridConstants.GRID_SIZE)
    {
        GridSize = gridSize;
        CellCount = gridSize * gridSize;

        HeightMap = new float[CellCount];
        CrustThicknessMap = new float[CellCount];
        RockTypeMap = new byte[CellCount];
        RockAgeMap = new float[CellCount];
        PlateMap = new ushort[CellCount];
        SoilTypeMap = new byte[CellCount];
        SoilDepthMap = new float[CellCount];
        TemperatureMap = new float[CellCount];
        PrecipitationMap = new float[CellCount];
        WindUMap = new float[CellCount];
        WindVMap = new float[CellCount];
        CloudTypeMap = new byte[CellCount];
        CloudCoverMap = new float[CellCount];
        BiomassMap = new float[CellCount];
        BiomatterMap = new float[CellCount];
        OrganicCarbonMap = new float[CellCount];

        // All cells start dirty so the first tick fully processes every cell.
        DirtyMask = new bool[CellCount];
        Array.Fill(DirtyMask, true);
    }
}

/// <summary>Stratigraphic layer at a cell.</summary>
public sealed class StratigraphicLayer
{
    public RockType RockType { get; set; }
    public double AgeDeposited { get; set; }
    public double Thickness { get; set; }
    public double DipAngle { get; set; }
    public double DipDirection { get; set; }
    public DeformationType Deformation { get; set; }
    public bool Unconformity { get; set; }
    public SoilOrder SoilHorizon { get; set; }
    public int FormationName { get; set; }

    public StratigraphicLayer Clone() => new()
    {
        RockType = RockType,
        AgeDeposited = AgeDeposited,
        Thickness = Thickness,
        DipAngle = DipAngle,
        DipDirection = DipDirection,
        Deformation = Deformation,
        Unconformity = Unconformity,
        SoilHorizon = SoilHorizon,
        FormationName = FormationName,
    };
}

/// <summary>Tectonic plate information.</summary>
public sealed class PlateInfo
{
    public int Id { get; set; }
    public double CenterLat { get; set; }
    public double CenterLon { get; set; }
    public AngularVelocity AngularVelocity { get; set; } = new();
    public bool IsOceanic { get; set; }
    public double Area { get; set; }
}

public sealed class AngularVelocity
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Rate { get; set; }
}

/// <summary>Mantle plume hotspot.</summary>
public sealed class HotspotInfo
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Strength { get; set; }
}

/// <summary>Atmospheric gas fractions.</summary>
public sealed class AtmosphericComposition
{
    public double N2 { get; set; }
    public double O2 { get; set; }
    public double CO2 { get; set; }
    public double H2O { get; set; }
    public double CH4 { get; set; }
}

/// <summary>Boundary cell between two plates.</summary>
public sealed class BoundaryCell
{
    public int CellIndex { get; set; }
    public BoundaryType Type { get; set; }
    public int Plate1 { get; set; }
    public int Plate2 { get; set; }
    public double RelativeSpeed { get; set; }
}

/// <summary>Record of a volcanic eruption.</summary>
public sealed class EruptionRecord
{
    public int CellIndex { get; set; }
    public VolcanoType VolcanoType { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Intensity { get; set; }
    public double HeightAdded { get; set; }
    public RockType RockType { get; set; }
    public double CO2Degassed { get; set; }
    public double SO2Degassed { get; set; }
}

/// <summary>Result of planet generation.</summary>
public sealed class PlanetGeneratorResult
{
    public List<PlateInfo> Plates { get; set; } = [];
    public List<HotspotInfo> Hotspots { get; set; } = [];
    public AtmosphericComposition Atmosphere { get; set; } = new();
    public uint Seed { get; set; }
}

/// <summary>Lat/lon coordinate in degrees.</summary>
public record struct LatLon(double Lat, double Lon);

/// <summary>Cross-section sample column.</summary>
public sealed class CrossSectionSample
{
    public double DistanceKm { get; set; }
    public double SurfaceElevation { get; set; }
    public double CrustThicknessKm { get; set; }
    public SoilOrder SoilType { get; set; }
    public double SoilDepthM { get; set; }
    public List<StratigraphicLayer> Layers { get; set; } = [];
}

/// <summary>Deep earth zone.</summary>
public sealed class DeepEarthZone
{
    public string Name { get; set; } = "";
    public double TopKm { get; set; }
    public double BottomKm { get; set; }
    public RockType RockType { get; set; }
}

/// <summary>Cross-section profile.</summary>
public sealed class CrossSectionProfile
{
    public List<CrossSectionSample> Samples { get; set; } = [];
    public double TotalDistanceKm { get; set; }
    public List<LatLon> PathPoints { get; set; } = [];
    public List<DeepEarthZone> DeepEarthZones { get; set; } = [];
}

/// <summary>Geological event log entry.</summary>
public sealed class GeoLogEntry
{
    public double TimeMa { get; set; }
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public LatLon? Location { get; set; }
}
