using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;
using GeoTime.Core.Engines;

namespace GeoTime.Core;

/// <summary>
/// Top-level simulation orchestrator that owns all engines and state.
/// Replaces the TypeScript main.ts game loop logic on the backend.
/// </summary>
public sealed class SimulationOrchestrator
{
    public SimulationState State { get; private set; }
    public SimClock Clock { get; }
    public EventBus Bus { get; }
    public EventLog EventLog { get; }
    public SnapshotManager Snapshots { get; }

    private TectonicEngine? _tectonic;
    private SurfaceEngine? _surface;
    private AtmosphereEngine? _atmosphere;
    private VegetationEngine? _vegetation;
    private CrossSectionEngine? _crossSection;

    private PlanetGeneratorResult? _planetResult;
    private uint _currentSeed;
    private readonly int _gridSize;

    public SimulationOrchestrator(int gridSize = GridConstants.GRID_SIZE)
    {
        _gridSize = gridSize;
        Bus = new EventBus();
        Clock = new SimClock(Bus, 0.05);
        EventLog = new EventLog();
        Snapshots = new SnapshotManager(10, 500);
        State = new SimulationState(gridSize);
    }

    /// <summary>Generate a new planet with the given seed.</summary>
    public PlanetGeneratorResult GeneratePlanet(uint seed)
    {
        _currentSeed = seed;
        State = new SimulationState(_gridSize);

        var gen = new PlanetGenerator(seed);
        var result = gen.Generate(State);
        _planetResult = result;

        EventLog.Clear();
        Snapshots.Clear();

        _tectonic = new TectonicEngine(Bus, EventLog, seed, 0.1);
        _tectonic.Initialize(result.Plates, result.Hotspots, result.Atmosphere, State);

        _surface = new SurfaceEngine(Bus, EventLog, seed, _gridSize, 0.5);
        _surface.Initialize(State, _tectonic.Stratigraphy);

        _atmosphere = new AtmosphereEngine(Bus, EventLog, seed, _gridSize, 1.0);
        _atmosphere.Initialize(State, result.Atmosphere);

        _crossSection = new CrossSectionEngine();
        _crossSection.Initialize(State, _tectonic.Stratigraphy);

        _vegetation = new VegetationEngine(Bus, EventLog, seed, _gridSize, 1.0);
        _vegetation.Initialize(State);

        Clock.SeekTo(SimClock.INITIAL_TIME);

        Bus.Emit("PLANET_GENERATED", new { seed, timeMa = Clock.T });
        return result;
    }

    /// <summary>Advance the simulation by deltaMa million years.</summary>
    public void AdvanceSimulation(double deltaMa)
    {
        if (_tectonic == null || deltaMa <= 0) return;

        Clock.T += deltaMa;

        // Tectonic must run first (it updates boundaries, heights, plate map)
        _tectonic.Tick(Clock.T, deltaMa);

        // Surface, Atmosphere, and Vegetation can run in parallel since they
        // read from state written by tectonic but write to independent fields.
        var tasks = new List<Task>();
        if (_surface != null)
            tasks.Add(Task.Run(() => _surface.Tick(Clock.T, deltaMa)));
        if (_atmosphere != null)
            tasks.Add(Task.Run(() => _atmosphere.Tick(Clock.T, deltaMa)));
        if (_vegetation != null)
            tasks.Add(Task.Run(() => _vegetation.Tick(Clock.T, deltaMa)));

        if (tasks.Count > 0)
            Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    /// <summary>Build a cross-section profile along the given path.</summary>
    public CrossSectionProfile? GetCrossSection(List<LatLon> pathPoints)
        => _crossSection?.BuildProfile(pathPoints);

    /// <summary>Get current simulation time.</summary>
    public double GetCurrentTime() => Clock.T;

    /// <summary>Get the current seed.</summary>
    public uint GetCurrentSeed() => _currentSeed;

    /// <summary>Get plates info.</summary>
    public IReadOnlyList<PlateInfo>? GetPlates() => _tectonic?.GetPlates();

    /// <summary>Get hotspots info.</summary>
    public IReadOnlyList<HotspotInfo>? GetHotspots() => _tectonic?.GetHotspots();

    /// <summary>Get atmosphere info.</summary>
    public AtmosphericComposition? GetAtmosphere() => _tectonic?.GetAtmosphere();

    /// <summary>Inspect a cell by grid index.</summary>
    public CellInspection? InspectCell(int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= State.CellCount) return null;
        return new CellInspection
        {
            CellIndex = cellIndex,
            Height = State.HeightMap[cellIndex],
            CrustThickness = State.CrustThicknessMap[cellIndex],
            RockType = (RockType)State.RockTypeMap[cellIndex],
            RockAge = State.RockAgeMap[cellIndex],
            PlateId = State.PlateMap[cellIndex],
            SoilType = (SoilOrder)State.SoilTypeMap[cellIndex],
            SoilDepth = State.SoilDepthMap[cellIndex],
            Temperature = State.TemperatureMap[cellIndex],
            Precipitation = State.PrecipitationMap[cellIndex],
            Biomass = State.BiomassMap[cellIndex],
        };
    }

    /// <summary>
    /// Serialize the current simulation state arrays into a single byte array
    /// for snapshot storage. Includes the current clock time.
    /// </summary>
    public byte[] SerializeState()
    {
        int cellCount = State.CellCount;
        // Layout: [8 bytes timeMa] + float arrays (HeightMap, CrustThickness, RockAge,
        //   SoilDepth, Temperature, Precipitation, WindU, WindV, CloudCover, Biomass)
        //   + byte arrays (RockType, SoilType, CloudType)
        //   + ushort array (PlateMap)
        int floatArrays = 10; // HeightMap, CrustThickness, RockAge, SoilDepth, Temperature, Precipitation, WindU, WindV, CloudCover, Biomass
        int byteArrays = 3;   // RockType, SoilType, CloudType
        int ushortArrays = 1; // PlateMap
        int totalSize = 8 + (floatArrays * cellCount * 4) + (byteArrays * cellCount) + (ushortArrays * cellCount * 2);

        var data = new byte[totalSize];
        int offset = 0;

        // Time
        BitConverter.TryWriteBytes(data.AsSpan(offset), Clock.T);
        offset += 8;

        // Float arrays
        WriteFloatArray(data, ref offset, State.HeightMap);
        WriteFloatArray(data, ref offset, State.CrustThicknessMap);
        WriteFloatArray(data, ref offset, State.RockAgeMap);
        WriteFloatArray(data, ref offset, State.SoilDepthMap);
        WriteFloatArray(data, ref offset, State.TemperatureMap);
        WriteFloatArray(data, ref offset, State.PrecipitationMap);
        WriteFloatArray(data, ref offset, State.WindUMap);
        WriteFloatArray(data, ref offset, State.WindVMap);
        WriteFloatArray(data, ref offset, State.CloudCoverMap);
        WriteFloatArray(data, ref offset, State.BiomassMap);

        // Byte arrays
        Buffer.BlockCopy(State.RockTypeMap, 0, data, offset, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(State.SoilTypeMap, 0, data, offset, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(State.CloudTypeMap, 0, data, offset, cellCount);
        offset += cellCount;

        // Ushort array (PlateMap)
        Buffer.BlockCopy(State.PlateMap, 0, data, offset, cellCount * 2);

        return data;
    }

    /// <summary>
    /// Deserialize state arrays from a byte buffer, restoring the simulation state.
    /// </summary>
    public void DeserializeState(byte[] data)
    {
        int cellCount = State.CellCount;
        int offset = 0;

        // Time
        Clock.T = BitConverter.ToDouble(data, offset);
        offset += 8;

        // Float arrays
        ReadFloatArray(data, ref offset, State.HeightMap);
        ReadFloatArray(data, ref offset, State.CrustThicknessMap);
        ReadFloatArray(data, ref offset, State.RockAgeMap);
        ReadFloatArray(data, ref offset, State.SoilDepthMap);
        ReadFloatArray(data, ref offset, State.TemperatureMap);
        ReadFloatArray(data, ref offset, State.PrecipitationMap);
        ReadFloatArray(data, ref offset, State.WindUMap);
        ReadFloatArray(data, ref offset, State.WindVMap);
        ReadFloatArray(data, ref offset, State.CloudCoverMap);
        ReadFloatArray(data, ref offset, State.BiomassMap);

        // Byte arrays
        Buffer.BlockCopy(data, offset, State.RockTypeMap, 0, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(data, offset, State.SoilTypeMap, 0, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(data, offset, State.CloudTypeMap, 0, cellCount);
        offset += cellCount;

        // Ushort array (PlateMap)
        Buffer.BlockCopy(data, offset, State.PlateMap, 0, cellCount * 2);
    }

    private static void WriteFloatArray(byte[] dest, ref int offset, float[] src)
    {
        Buffer.BlockCopy(src, 0, dest, offset, src.Length * 4);
        offset += src.Length * 4;
    }

    private static void ReadFloatArray(byte[] src, ref int offset, float[] dest)
    {
        Buffer.BlockCopy(src, offset, dest, 0, dest.Length * 4);
        offset += dest.Length * 4;
    }
}

/// <summary>Cell inspection result.</summary>
public sealed class CellInspection
{
    public int CellIndex { get; set; }
    public float Height { get; set; }
    public float CrustThickness { get; set; }
    public RockType RockType { get; set; }
    public float RockAge { get; set; }
    public int PlateId { get; set; }
    public SoilOrder SoilType { get; set; }
    public float SoilDepth { get; set; }
    public float Temperature { get; set; }
    public float Precipitation { get; set; }
    public float Biomass { get; set; }
}
