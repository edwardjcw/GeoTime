using System.Diagnostics;
using System.Text.Json;
using GeoTime.Core.Compute;
using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;
using GeoTime.Core.Engines;
using GeoTime.Core.Services;

namespace GeoTime.Core;

/// <summary>Per-phase timing breakdown for the most recent simulation tick.</summary>
public sealed class SimulationTickStats
{
    public long TectonicMs  { get; set; }
    public long SurfaceMs   { get; set; }
    public long AtmosphereMs { get; set; }
    public long VegetationMs { get; set; }
    public long BiomatterMs  { get; set; }
    public long TotalMs      { get; set; }
    public double TimeMa     { get; set; }
}

/// <summary>
/// Top-level simulation orchestrator that owns all engines and state.
/// Replaces the TypeScript main.ts game loop logic on the backend.
/// </summary>
public sealed class SimulationOrchestrator : IDisposable
{
    public SimulationState State { get; private set; }
    public SimClock Clock { get; }
    public EventBus Bus { get; }
    public EventLog EventLog { get; }
    public SnapshotManager Snapshots { get; }

    /// <summary>Timing breakdown of the most recent completed tick.</summary>
    public SimulationTickStats LastTickStats { get; private set; } = new();

    /// <summary>Total number of simulation ticks completed since planet generation.</summary>
    public int TickCount { get; private set; }

    /// <summary>Prevents concurrent AdvanceSimulation calls from corrupting shared state.</summary>
    private readonly SemaphoreSlim _advanceLock = new(1, 1);

    /// <summary>
    /// Strategy D (Rec 7): When true, the atmosphere and vegetation engines run at
    /// 128×128 coarse resolution and upsample results to the full 512×512 grid.
    /// Enabled by default; can be toggled by the REST API for benchmarking.
    /// </summary>
    public bool AdaptiveResolutionEnabled { get; set; } = true;

    private readonly GpuComputeService _gpu;
    private TectonicEngine? _tectonic;
    private SurfaceEngine? _surface;
    private AtmosphereEngine? _atmosphere;
    private VegetationEngine? _vegetation;
    private BiomatterEngine? _biomatter;
    private CrossSectionEngine? _crossSection;
    private readonly FeatureDetectorService _featureDetector = new();
    private readonly FeatureEvolutionTracker _featureEvolution = new();
    private readonly EventDepositionEngine _eventDeposition = new();

    private PlanetGeneratorResult? _planetResult;
    private uint _currentSeed;
    private readonly int _gridSize;

    public SimulationOrchestrator(int gridSize = GridConstants.GRID_SIZE)
    {
        _gridSize = gridSize;
        _gpu = new GpuComputeService();
        Bus = new EventBus();
        Clock = new SimClock(Bus, 0.05);
        EventLog = new EventLog();
        Snapshots = new SnapshotManager(10, 500);
        State = new SimulationState(gridSize);
    }

    /// <summary>Returns the active compute backend information for the UI toolbar.</summary>
    public ComputeInfo GetComputeInfo() => _gpu.Info;

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
        TickCount = 0;

        _tectonic = new TectonicEngine(Bus, EventLog, seed, 0.1, _gpu);
        _tectonic.Initialize(result.Plates, result.Hotspots, result.Atmosphere, State);

        _surface = new SurfaceEngine(Bus, EventLog, seed, _gridSize, 0.5);
        _surface.Initialize(State, _tectonic.Stratigraphy);

        _atmosphere = new AtmosphereEngine(Bus, EventLog, seed, _gridSize, 1.0, _gpu);
        _atmosphere.Initialize(State, result.Atmosphere);

        _crossSection = new CrossSectionEngine();
        _crossSection.Initialize(State, _tectonic.Stratigraphy);

        _vegetation = new VegetationEngine(Bus, EventLog, seed, _gridSize, 1.0);
        _vegetation.Initialize(State);

        _biomatter = new BiomatterEngine(Bus, EventLog, seed, _gridSize, 1.0);
        _biomatter.Initialize(State, result.Atmosphere, _tectonic.Stratigraphy);

        Clock.SeekTo(SimClock.INITIAL_TIME);

        // Detect features after generation so the registry is populated immediately.
        _featureDetector.Detect(State, result.Plates, result.Hotspots,
            EventLog.GetAll(), seed, 0L);

        Bus.Emit("PLANET_GENERATED", new { seed, timeMa = Clock.T });
        return result;
    }

    /// <summary>Advance the simulation by deltaMa million years.</summary>
    /// <param name="deltaMa">Simulation time step in millions of years.</param>
    /// <param name="onProgress">Optional callback invoked at each engine phase with the phase name.</param>
    public void AdvanceSimulation(double deltaMa, Action<string>? onProgress = null)
    {
        if (_tectonic == null || deltaMa <= 0) return;

        // Prevent concurrent advance calls from corrupting simulation state.
        if (!_advanceLock.Wait(0))
        {
            System.Diagnostics.Debug.WriteLine("[SimulationOrchestrator] Concurrent advance skipped — previous tick still running.");
            return;
        }
        try
        {
            AdvanceSimulationCore(deltaMa, onProgress);
        }
        finally
        {
            _advanceLock.Release();
        }
    }

    private void AdvanceSimulationCore(double deltaMa, Action<string>? onProgress)
    {
        var total = Stopwatch.StartNew();
        var sw    = new Stopwatch();
        var stats = new SimulationTickStats { TimeMa = Clock.T + deltaMa };

        // Capture current log length so we can identify events added this tick.
        var logLengthBefore = EventLog.Length;

        Clock.T += deltaMa;
        TickCount++;

        // Tectonic must run first (it updates boundaries, heights, plate map)
        onProgress?.Invoke("tectonic");
        sw.Restart();
        _tectonic!.Tick(Clock.T, deltaMa);
        stats.TectonicMs = sw.ElapsedMilliseconds;

        // ── Strategy D: Adaptive Resolution ──────────────────────────────────
        // When AdaptiveResolutionEnabled, downsample temperature, precipitation, and
        // biomass maps to 128×128 before running the atmosphere and vegetation engines,
        // then upsample results back to 512×512.  The surface and tectonic engines
        // always operate at full resolution.
        if (AdaptiveResolutionEnabled && _gridSize == GridConstants.GRID_SIZE)
        {
            ApplyAdaptiveResolution(deltaMa, onProgress, stats);
        }
        else
        {
            // Full-resolution path (used when gridSize != 512, e.g., in unit tests)
            RunFullResolutionEngines(deltaMa, onProgress, stats);
        }

        // Biomatter runs after Surface to avoid concurrent modification of StratigraphyStack.
        onProgress?.Invoke("biomatter");
        sw.Restart();
        _biomatter?.Tick(Clock.T, deltaMa);
        stats.BiomatterMs = sw.ElapsedMilliseconds;

        // Update the feature registry after all engines have processed this tick.
        if (_tectonic != null)
        {
            var plates   = _tectonic.GetPlates();
            var hotspots = _tectonic.GetHotspots();
            var prevRegistry = State.FeatureRegistry;
            _featureDetector.Detect(State, plates, hotspots,
                EventLog.GetAll(), _currentSeed, State.FeatureRegistry.LastUpdatedTick + 1);
            // Phase L4: carry forward history and detect change events.
            _featureEvolution.Track(State, prevRegistry, State.FeatureRegistry,
                State.FeatureRegistry.LastUpdatedTick);
        }

        // Phase D1: deposit event-horizon layers for events raised this tick.
        if (_tectonic != null)
        {
            var tickEvents = EventLog.GetAll()
                .Skip(logLengthBefore)
                .ToList();
            if (tickEvents.Count > 0)
                _eventDeposition.Deposit(State, _tectonic.Stratigraphy, tickEvents, Clock.T);
        }

        stats.TotalMs = total.ElapsedMilliseconds;
        LastTickStats = stats;

        // Log per-tick timing so the event log helps diagnose slow ticks.
        if (stats.TotalMs > 0)
        {
            EventLog.Record(new GeoLogEntry
            {
                TimeMa = Clock.T,
                Type = "TICK_STATS",
                Description = $"Total={stats.TotalMs}ms | Tectonic={stats.TectonicMs}ms | Surface={stats.SurfaceMs}ms | Atmo={stats.AtmosphereMs}ms | Veg={stats.VegetationMs}ms | Bio={stats.BiomatterMs}ms",
            });
        }

        onProgress?.Invoke("complete");
    }

    private void RunFullResolutionEngines(double deltaMa, Action<string>? onProgress, SimulationTickStats stats)
    {
        // Surface, Atmosphere, and Vegetation can run in parallel since they
        // read from state written by tectonic but write to independent fields.
        onProgress?.Invoke("surface");

        long surfaceMs = 0, atmoMs = 0, vegMs = 0;
        var tasks = new List<Task>();
        if (_surface != null)
            tasks.Add(Task.Run(() => { var t = Stopwatch.StartNew(); _surface.Tick(Clock.T, deltaMa); Interlocked.Exchange(ref surfaceMs, t.ElapsedMilliseconds); }));
        if (_atmosphere != null)
            tasks.Add(Task.Run(() => { var t = Stopwatch.StartNew(); _atmosphere.Tick(Clock.T, deltaMa); Interlocked.Exchange(ref atmoMs, t.ElapsedMilliseconds); }));
        if (_vegetation != null)
            tasks.Add(Task.Run(() => { var t = Stopwatch.StartNew(); _vegetation.Tick(Clock.T, deltaMa); Interlocked.Exchange(ref vegMs, t.ElapsedMilliseconds); }));

        if (tasks.Count > 0)
        {
            try { Task.WhenAll(tasks).GetAwaiter().GetResult(); }
            catch (AggregateException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Parallel engine tick error: {ex.Flatten().InnerExceptions.Count} engine(s) failed");
            }
        }

        stats.SurfaceMs    = surfaceMs;
        stats.AtmosphereMs = atmoMs;
        stats.VegetationMs = vegMs;
    }

    private void ApplyAdaptiveResolution(double deltaMa, Action<string>? onProgress, SimulationTickStats stats)
    {
        const int CoarseSize = GridConstants.COARSE_GRID_SIZE;
        var fullSize = _gridSize; // 512

        // Downsample inputs to coarse grid
        var coarseTemp    = AdaptiveResolutionService.Downsample(State.TemperatureMap,   fullSize, CoarseSize);
        var coarsePrecip  = AdaptiveResolutionService.Downsample(State.PrecipitationMap, fullSize, CoarseSize);
        var coarseBiomass = AdaptiveResolutionService.Downsample(State.BiomassMap,       fullSize, CoarseSize);

        // Build a lightweight coarse SimulationState (shares height with full-res via downsampled copy)
        var coarseState = new SimulationState(CoarseSize);
        Array.Copy(coarseTemp,    coarseState.TemperatureMap,   CoarseSize * CoarseSize);
        Array.Copy(coarsePrecip,  coarseState.PrecipitationMap, CoarseSize * CoarseSize);
        Array.Copy(coarseBiomass, coarseState.BiomassMap,       CoarseSize * CoarseSize);
        // Downsample height for lapse-rate calculations in the climate engine
        var coarseHeight = AdaptiveResolutionService.Downsample(State.HeightMap, fullSize, CoarseSize);
        Array.Copy(coarseHeight, coarseState.HeightMap, CoarseSize * CoarseSize);
        Array.Fill(coarseState.DirtyMask, true);

        // Build coarse engines that share the GPU service
        var coarseAtmo = new AtmosphereEngine(Bus, EventLog, _currentSeed, CoarseSize, 1.0, _gpu);
        var coarseVeg  = new VegetationEngine(Bus, EventLog, _currentSeed, CoarseSize, 1.0);
        coarseAtmo.Initialize(coarseState, _tectonic!.GetAtmosphere()!);
        coarseVeg.Initialize(coarseState);

        // Surface runs at full resolution; atmosphere and vegetation run on coarse grid.
        onProgress?.Invoke("surface");
        long surfaceMs = 0, atmoMs = 0, vegMs = 0;
        var tasks = new List<Task>();
        if (_surface != null)
            tasks.Add(Task.Run(() => { var t = Stopwatch.StartNew(); _surface.Tick(Clock.T, deltaMa); Interlocked.Exchange(ref surfaceMs, t.ElapsedMilliseconds); }));
        tasks.Add(Task.Run(() => { var t = Stopwatch.StartNew(); coarseAtmo.Tick(Clock.T, deltaMa); Interlocked.Exchange(ref atmoMs, t.ElapsedMilliseconds); }));
        tasks.Add(Task.Run(() => { var t = Stopwatch.StartNew(); coarseVeg.Tick(Clock.T, deltaMa); Interlocked.Exchange(ref vegMs, t.ElapsedMilliseconds); }));

        try { Task.WhenAll(tasks).GetAwaiter().GetResult(); }
        catch (AggregateException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Adaptive parallel tick error: {ex.Flatten().InnerExceptions.Count} engine(s) failed");
        }

        stats.SurfaceMs    = surfaceMs;
        stats.AtmosphereMs = atmoMs;
        stats.VegetationMs = vegMs;

        // Upsample coarse results back to full resolution
        AdaptiveResolutionService.UpsampleInto(coarseState.TemperatureMap,   CoarseSize, State.TemperatureMap,   fullSize);
        AdaptiveResolutionService.UpsampleInto(coarseState.PrecipitationMap, CoarseSize, State.PrecipitationMap, fullSize);
        AdaptiveResolutionService.UpsampleInto(coarseState.BiomassMap,       CoarseSize, State.BiomassMap,       fullSize);
        AdaptiveResolutionService.UpsampleInto(coarseState.WindUMap,         CoarseSize, State.WindUMap,         fullSize);
        AdaptiveResolutionService.UpsampleInto(coarseState.WindVMap,         CoarseSize, State.WindVMap,         fullSize);
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

    /// <summary>Get the current feature registry.</summary>
    public FeatureRegistry GetFeatureRegistry() => State.FeatureRegistry;

    /// <summary>
    /// Return a snapshot of all stratigraphic columns as a StratigraphicColumn?[] array
    /// indexed by cell index.  Returns null if the planet has not been generated yet.
    /// </summary>
    public StratigraphicColumn?[]? GetStratigraphicColumns()
    {
        if (_tectonic == null) return null;
        var cellCount = State.CellCount;
        var result = new StratigraphicColumn?[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            var layers = _tectonic.Stratigraphy.GetLayers(i);
            if (layers.Count > 0)
                result[i] = new StratigraphicColumn { Layers = [..layers] };
        }
        return result;
    }

    /// <summary>Inspect a cell by grid index.</summary>
    public CellInspection? InspectCell(int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= State.CellCount) return null;

        double h = State.HeightMap[cellIndex];
        double temp = State.TemperatureMap[cellIndex];
        var reefPresent = h is < 0 and >= BiomatterEngine.REEF_MAX_DEPTH
                          && temp is >= BiomatterEngine.REEF_MIN_TEMP and <= BiomatterEngine.REEF_MAX_TEMP
                          && State.BiomatterMap[cellIndex] > 0;

        // ── Phase D1: extended fields ─────────────────────────────────────────

        // Build StratigraphicColumn from the existing stratigraphy stack.
        StratigraphicColumn? column = null;
        if (_tectonic != null)
        {
            var layers = _tectonic.Stratigraphy.GetLayers(cellIndex);
            column = new StratigraphicColumn { Layers = [..layers] };
        }

        // Collect feature IDs that contain this cell.
        var featureIds = State.FeatureRegistry.Features.Values
            .Where(f => f.CellIndices.Contains(cellIndex))
            .Select(f => f.Id)
            .ToList();

        // River name: find a river feature whose cells include this cell.
        string? riverName = null;
        string? watershedId = null;
        foreach (var feat in State.FeatureRegistry.Features.Values)
        {
            if (feat.CellIndices.Contains(cellIndex))
            {
                if (feat.Type == FeatureType.River && riverName == null)
                    riverName = feat.Current.Name;
                if (feat.Type is FeatureType.Lake or FeatureType.Ocean or FeatureType.Sea
                    && watershedId == null)
                    watershedId = feat.Id;
            }
        }

        // Nearest plate boundary distance and type.
        float distToMarginKm = float.MaxValue;
        var   nearestMarginType = BoundaryType.NONE;
        if (_tectonic != null)
        {
            var plates = _tectonic.GetPlates().ToList();
            var boundaries = BoundaryClassifier.Classify(State.PlateMap, plates, State.GridSize);
            int row = cellIndex / State.GridSize;
            int col = cellIndex % State.GridSize;
            double cellLat = 90.0 - (row + 0.5) * 180.0 / State.GridSize;
            double cellLon = (col + 0.5) * 360.0 / State.GridSize - 180.0;

            foreach (var b in boundaries)
            {
                int br = b.CellIndex / State.GridSize;
                int bc = b.CellIndex % State.GridSize;
                double bLat = 90.0 - (br + 0.5) * 180.0 / State.GridSize;
                double bLon = (bc + 0.5) * 360.0 / State.GridSize - 180.0;
                float dKm = (float)(CrossSectionEngine.CentralAngle(cellLat, cellLon, bLat, bLon)
                             * CrossSectionEngine.EARTH_RADIUS_KM);
                if (dKm < distToMarginKm)
                {
                    distToMarginKm     = dKm;
                    nearestMarginType  = b.Type;
                }
            }
            if (distToMarginKm == float.MaxValue) distToMarginKm = 0f;
        }

        // Estimated rock age in million years (current time − deposition time).
        float estimatedRockAgeMy = 0f;
        if (column?.Surface != null)
            estimatedRockAgeMy = (float)(Clock.T - column.Surface.AgeDeposited);

        // Local events: all log entries that have a location within one equatorial cell-width.
        // Cell width = full equatorial circumference / grid columns = 2π R / gs.
        // We accept events within 1 cell-width radius to capture immediate-vicinity geology.
        var cellWidthKm = (float)(2.0 * Math.PI * CrossSectionEngine.EARTH_RADIUS_KM / State.GridSize);
        int cellRow = cellIndex / State.GridSize;
        int cellCol = cellIndex % State.GridSize;
        double lat0 = 90.0 - (cellRow + 0.5) * 180.0 / State.GridSize;
        double lon0 = (cellCol + 0.5) * 360.0 / State.GridSize - 180.0;
        var localEvents = EventLog.GetAll()
            .Where(e => e.Location.HasValue
                && (float)(CrossSectionEngine.CentralAngle(lat0, lon0,
                    e.Location.Value.Lat, e.Location.Value.Lon)
                    * CrossSectionEngine.EARTH_RADIUS_KM) <= cellWidthKm)
            .OrderBy(e => e.TimeMa)
            .ToList();

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
            BiomatterDensity = State.BiomatterMap[cellIndex],
            OrganicCarbon = State.OrganicCarbonMap[cellIndex],
            ReefPresent = reefPresent,
            // D1 fields:
            Column = column,
            FeatureIds = featureIds,
            RiverName = riverName,
            WatershedFeatureId = watershedId,
            DistanceToPlateMarginKm = distToMarginKm,
            NearestMarginType = nearestMarginType,
            EstimatedRockAgeMyears = estimatedRockAgeMy,
            LocalEvents = localEvents,
        };
    }

    /// <summary>
    /// Serialize the current simulation state arrays into a single byte array
    /// for snapshot storage. Includes the current clock time.
    /// </summary>
    public byte[] SerializeState()
    {
        var cellCount = State.CellCount;
        // Layout: [8 bytes timeMa] + float arrays (HeightMap, CrustThickness, RockAge,
        //   SoilDepth, Temperature, Precipitation, WindU, WindV, CloudCover, Biomass,
        //   BiomatterMap, OrganicCarbonMap)
        //   + byte arrays (RockType, SoilType, CloudType)
        //   + ushort array (PlateMap)
        const int floatArrays = 12; // HeightMap, CrustThickness, RockAge, SoilDepth, Temperature, Precipitation, WindU, WindV, CloudCover, Biomass, BiomatterMap, OrganicCarbonMap
        const int byteArrays = 3;   // RockType, SoilType, CloudType
        const int ushortArrays = 1; // PlateMap
        var totalSize = 8 + floatArrays * cellCount * 4 + byteArrays * cellCount + ushortArrays * cellCount * 2;

        var data = new byte[totalSize];
        var offset = 0;

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
        WriteFloatArray(data, ref offset, State.BiomatterMap);
        WriteFloatArray(data, ref offset, State.OrganicCarbonMap);

        // Byte arrays
        Buffer.BlockCopy(State.RockTypeMap, 0, data, offset, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(State.SoilTypeMap, 0, data, offset, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(State.CloudTypeMap, 0, data, offset, cellCount);
        offset += cellCount;

        // Ushort array (PlateMap)
        Buffer.BlockCopy(State.PlateMap, 0, data, offset, cellCount * 2);

        // ── Phase L6: append FeatureRegistry as JSON after the binary block ──
        // Layout: [existing binary][4 bytes: JSON length][JSON bytes]
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(State.FeatureRegistry);
        var finalData = new byte[data.Length + 4 + jsonBytes.Length];
        Buffer.BlockCopy(data, 0, finalData, 0, data.Length);
        BitConverter.TryWriteBytes(finalData.AsSpan(data.Length), jsonBytes.Length);
        Buffer.BlockCopy(jsonBytes, 0, finalData, data.Length + 4, jsonBytes.Length);

        return finalData;
    }

    /// <summary>
    /// Deserialize state arrays from a byte buffer, restoring the simulation state.
    /// </summary>
    public void DeserializeState(byte[] data)
    {
        var cellCount = State.CellCount;
        var offset = 0;

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
        ReadFloatArray(data, ref offset, State.BiomatterMap);
        ReadFloatArray(data, ref offset, State.OrganicCarbonMap);

        // Byte arrays
        Buffer.BlockCopy(data, offset, State.RockTypeMap, 0, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(data, offset, State.SoilTypeMap, 0, cellCount);
        offset += cellCount;
        Buffer.BlockCopy(data, offset, State.CloudTypeMap, 0, cellCount);
        offset += cellCount;

        // Ushort array (PlateMap)
        Buffer.BlockCopy(data, offset, State.PlateMap, 0, cellCount * 2);
        offset += cellCount * 2;

        // ── Phase L6: optional FeatureRegistry JSON appended after binary block ──
        if (offset + 4 <= data.Length)
        {
            var jsonLen = BitConverter.ToInt32(data, offset);
            offset += 4;
            if (jsonLen > 0 && offset + jsonLen <= data.Length)
            {
                try
                {
                    var registry = JsonSerializer.Deserialize<FeatureRegistry>(
                        data.AsSpan(offset, jsonLen));
                    if (registry != null)
                        State.FeatureRegistry = registry;
                }
                catch
                {
                    // Ignore deserialization errors — registry is non-critical.
                }
            }
        }
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

    public void Dispose() { _gpu.Dispose(); _advanceLock.Dispose(); }
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
    public float BiomatterDensity { get; set; }
    public float OrganicCarbon { get; set; }
    public bool ReefPresent { get; set; }

    // ── Phase D1: extended geological context ─────────────────────────────────

    /// <summary>Full stratigraphic column for this cell (oldest layer first).</summary>
    public StratigraphicColumn? Column { get; set; }

    /// <summary>IDs of all detected geographic features that contain this cell.</summary>
    public List<string> FeatureIds { get; set; } = [];

    /// <summary>Name of the river flowing through this cell, or null.</summary>
    public string? RiverName { get; set; }

    /// <summary>Feature ID of the watershed basin this cell drains into, or null.</summary>
    public string? WatershedFeatureId { get; set; }

    /// <summary>Distance in km to the nearest tectonic plate boundary.</summary>
    public float DistanceToPlateMarginKm { get; set; }

    /// <summary>Type of the nearest plate boundary (CONVERGENT, DIVERGENT, TRANSFORM, or NONE).</summary>
    public BoundaryType NearestMarginType { get; set; }

    /// <summary>
    /// Estimated age of the surface rock in millions of sim-years, derived from
    /// <see cref="RockAge"/> and the current simulation time.
    /// </summary>
    public float EstimatedRockAgeMyears { get; set; }

    /// <summary>All geologic events that have affected this cell, ordered by simulation time.</summary>
    public List<GeoLogEntry> LocalEvents { get; set; } = [];
}
