using GeoTime.Core.Compute;
using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;
using System.Numerics;

namespace GeoTime.Core.Engines;

/// <summary>
/// Main tectonic engine: plate motion, boundary processes, isostasy, volcanism.
/// </summary>
public sealed class TectonicEngine(EventBus bus, EventLog eventLog, uint seed, double minTickInterval = 0.1, GpuComputeService? gpu = null)
{
    private const double ISOSTATIC_RATIO = 2.7 / 3.3;
    private const double DEG2RAD = Math.PI / 180.0;
    private const double TWO_PI = 2 * Math.PI;

    /// <summary>Default deep-ocean floor height for rift gap fill (metres).</summary>
    private const float GAP_FLOOR_HEIGHT = -4000f;

    /// <summary>Thin oceanic crust thickness for newly created rift cells (km).</summary>
    private const float GAP_CRUST_KM = 7f;

    public StratigraphyStack Stratigraphy { get; } = new();
    private readonly BoundaryClassifier _classifier = new();
    private readonly VolcanismEngine _volcanism = new();
    private Xoshiro256ss _rng = new(seed);

    private List<PlateInfo> _plates = [];
    private List<HotspotInfo> _hotspots = [];
    private AtmosphericComposition _atmosphere = new() { N2 = 0.78, O2 = 0.21, CO2 = 0.0004, H2O = 0.01 };
    private SimulationState? _state;

    private double _accumulator;

    /// <summary>S1: Cached boundary cells reused across sub-ticks within a single Tick.</summary>
    private List<BoundaryCell>? _cachedBoundaries;

    public void Initialize(List<PlateInfo> plates, List<HotspotInfo> hotspots,
        AtmosphericComposition atmosphere, SimulationState state)
    {
        _plates = plates;
        _hotspots = hotspots;
        _atmosphere = atmosphere;
        _state = state;
        var cellCount = state.CellCount;
        for (var i = 0; i < cellCount; i++)
        {
            var plate = plates[state.PlateMap[i]];
            Stratigraphy.InitializeBasement(i, plate.IsOceanic, state.RockAgeMap[i]);
        }
    }

    public List<EruptionRecord> Tick(double timeMa, double deltaMa)
    {
        if (_state == null || deltaMa <= 0) return [];
        _accumulator += deltaMa;
        var all = new List<EruptionRecord>();

        // Snapshot heights before sub-ticks to compute dirty mask afterward.
        var cc = _state.CellCount;
        var prevHeight = new float[cc];
        Array.Copy(_state.HeightMap, prevHeight, cc);

        // ── Plate advection: apply once per Tick with the full deltaMa so that
        //    rotation angles are large enough to produce meaningful grid-cell
        //    movement.  Sub-tick boundary processes then operate on the updated
        //    plate configuration.
        AdvectPlates(_state, deltaMa, timeMa);
        UpdatePlateCenters(_state);

        // S1: Cache boundaries once after advection (S3: GPU when available)
        _cachedBoundaries = ClassifyBoundaries(_state.PlateMap, _plates, _state.GridSize);

        while (_accumulator >= minTickInterval)
        {
            _accumulator -= minTickInterval;
            var subTime = timeMa - _accumulator;
            all.AddRange(ProcessTick(subTime, minTickInterval));
        }

        _cachedBoundaries = null;

        // Update dirty mask: cells where height changed by > 0.5 m need reprocessing.
        var heightMap = _state.HeightMap;
        var dirty = _state.DirtyMask;
        const float DirtyThreshold = 0.5f;
        for (var i = 0; i < cc; i++)
            dirty[i] = MathF.Abs(heightMap[i] - prevHeight[i]) > DirtyThreshold;

        return all;
    }

    public async Task<List<EruptionRecord>> TickAsync(double timeMa, double deltaMa, Func<string, Task>? onSubPhase = null)
    {
        if (_state == null || deltaMa <= 0) return [];
        _accumulator += deltaMa;
        var all = new List<EruptionRecord>();

        var cc = _state.CellCount;
        var prevHeight = new float[cc];
        Array.Copy(_state.HeightMap, prevHeight, cc);

        AdvectPlates(_state, deltaMa, timeMa);
        if (onSubPhase != null) await onSubPhase("tectonic:advection");

        UpdatePlateCenters(_state);

        // Cache boundaries once after advection (S1 + S3: GPU when available)
        _cachedBoundaries = ClassifyBoundaries(_state.PlateMap, _plates, _state.GridSize);
        if (onSubPhase != null) await onSubPhase("tectonic:collision");

        while (_accumulator >= minTickInterval)
        {
            _accumulator -= minTickInterval;
            var subTime = timeMa - _accumulator;

            if (onSubPhase != null) await onSubPhase("tectonic:boundaries");

            all.AddRange(ProcessTick(subTime, minTickInterval));

            if (onSubPhase != null) await onSubPhase("tectonic:dynamics");
        }

        _cachedBoundaries = null;

        // Update dirty mask
        var heightMap = _state.HeightMap;
        var dirty = _state.DirtyMask;
        const float DirtyThreshold = 0.5f;
        for (var i = 0; i < cc; i++)
            dirty[i] = MathF.Abs(heightMap[i] - prevHeight[i]) > DirtyThreshold;

        if (onSubPhase != null) await onSubPhase("tectonic:volcanism");

        return all;
    }

    private List<EruptionRecord> ProcessTick(double timeMa, double deltaMa)
    {
        var state = _state!;
        var gs = state.GridSize;

        var boundaries = _cachedBoundaries ?? ClassifyBoundaries(state.PlateMap, _plates, gs);
        ProcessBoundaryDynamics(boundaries, deltaMa, timeMa, state);
        ApplyIsostasy(state, deltaMa);

        var eruptions = VolcanismEngine.Tick(timeMa, deltaMa, boundaries, _hotspots, _plates, state, Stratigraphy, _rng);

        foreach (var e in eruptions.Where(e => e.Intensity > 0.5))
        {
            bus.Emit("VOLCANIC_ERUPTION", new { lat = e.Lat, lon = e.Lon, intensity = e.Intensity });
            eventLog.Record(new GeoLogEntry
            {
                TimeMa = timeMa, Type = "VOLCANIC_ERUPTION",
                Description = $"Eruption at ({e.Lat:F1}°, {e.Lon:F1}°), intensity {e.Intensity:F2}",
                Location = new LatLon(e.Lat, e.Lon),
            });
        }

        var (co2, _) = VolcanismEngine.TotalDegassing(eruptions);
        _atmosphere.CO2 += co2 * 1e-6;
        return eruptions;
    }

    // ── S7: GPU-accelerated boundary dynamics with CPU fallback ──────────────

    /// <summary>
    /// Process convergent and divergent boundary cells using GPU when available,
    /// falling back to the original CPU path.  GPU computes height/crust deltas
    /// in parallel; CPU applies stratigraphy deformation for collision cells and
    /// emits plate-collision events.
    /// </summary>
    private void ProcessBoundaryDynamics(List<BoundaryCell> boundaries, double deltaMa, double timeMa, SimulationState state)
    {
        if (gpu != null)
        {
            try
            {
                ProcessBoundaryDynamicsGpu(boundaries, deltaMa, timeMa, state);
                return;
            }
            catch
            {
                // GPU processing failed — fall back to CPU
            }
        }

        // CPU fallback: original sequential processing
        ProcessConvergent(boundaries, deltaMa, timeMa, state);
        ProcessDivergent(boundaries, deltaMa, timeMa, state);
    }

    /// <summary>
    /// GPU path: offload height/crust delta computation to the GPU, then apply
    /// results on the CPU.  Stratigraphy deformation and event logging run on
    /// the CPU since they involve dictionary operations and side effects.
    /// </summary>
    private void ProcessBoundaryDynamicsGpu(List<BoundaryCell> boundaries, double deltaMa, double timeMa, SimulationState state)
    {
        var result = gpu!.ProcessBoundaryDynamicsGpu(
            boundaries, _plates,
            state.HeightMap, state.CrustThicknessMap,
            (float)deltaMa, (float)timeMa);

        if (result.cellIndices.Length == 0) return;

        // Filter to convergent+divergent once for collision event tracking
        var filtered = boundaries
            .Where(b => b.Type == BoundaryType.CONVERGENT || b.Type == BoundaryType.DIVERGENT)
            .ToList();

        // Apply GPU-computed deltas to state arrays
        for (var i = 0; i < result.cellIndices.Length; i++)
        {
            var ci = result.cellIndices[i];
            state.HeightMap[ci] += result.heightDelta[i];
            state.CrustThicknessMap[ci] += result.crustDelta[i];

            // Divergent: set rock type to basalt if flagged
            if (result.setBasalt[i] == 1)
            {
                state.RockTypeMap[ci] = (byte)RockType.IGN_BASALT;
                state.RockAgeMap[ci] = result.newAge[i];
            }

            // Continent-continent collision: apply stratigraphy deformation on CPU
            // and emit collision events for high-speed boundaries
            if (result.isCollision[i])
            {
                Stratigraphy.ApplyDeformation(ci, 2 * deltaMa, 0, DeformationType.FOLDED);

                var b = filtered[i];
                if (b.RelativeSpeed > 2.0)
                {
                    bus.Emit("PLATE_COLLISION", new { plate1 = b.Plate1, plate2 = b.Plate2 });
                    eventLog.Record(new GeoLogEntry
                    {
                        TimeMa = timeMa, Type = "PLATE_COLLISION",
                        Description = $"Collision between plates {b.Plate1} and {b.Plate2}",
                    });
                }
            }
        }
    }

    // ── Boundary classification (S3: GPU with CPU fallback) ────────────────────

    /// <summary>
    /// Classify plate boundaries using GPU acceleration when available,
    /// falling back to CPU-based <see cref="BoundaryClassifier.Classify"/>.
    /// </summary>
    private List<BoundaryCell> ClassifyBoundaries(ushort[] plateMap, List<PlateInfo> plates, int gridSize)
    {
        if (gpu != null)
        {
            try
            {
                return gpu.ClassifyBoundariesGpu(plateMap, plates, gridSize);
            }
            catch
            {
                // GPU classification failed — fall back to CPU
            }
        }
        return BoundaryClassifier.Classify(plateMap, plates, gridSize);
    }

    // ── Plate advection ─────────────────────────────────────────────────────────

    private static double RowToLat(int row, int gs) => Math.PI / 2.0 - (double)row / gs * Math.PI;
    private static double ColToLon(int col, int gs) => (double)col / gs * TWO_PI - Math.PI;

    /// <summary>
    /// Advect all plate cells using Rodrigues' rotation formula.
    /// Each cell is rotated around its plate's Euler pole by angle = Rate * DEG2RAD * deltaMa.
    /// Collisions (multiple sources → one target) thicken crust and build mountains.
    /// Gaps (no source → target) are filled with fresh oceanic crust (mid-ocean ridge material).
    /// </summary>
    internal void AdvectPlates(SimulationState state, double deltaMa, double timeMa)
    {
        var gs = state.GridSize;
        var cc = state.CellCount;

        // Precompute per-plate rotation parameters (Euler pole unit vector + angle).
        var numPlates = _plates.Count;
        var kx = new double[numPlates];
        var ky = new double[numPlates];
        var kz = new double[numPlates];
        var cosTheta = new double[numPlates];
        var sinTheta = new double[numPlates];

        for (var p = 0; p < numPlates; p++)
        {
            var av = _plates[p].AngularVelocity;
            var poleLat = av.Lat * DEG2RAD;
            var poleLon = av.Lon * DEG2RAD;
            var cosPoleLat = Math.Cos(poleLat);
            kx[p] = cosPoleLat * Math.Cos(poleLon);
            ky[p] = cosPoleLat * Math.Sin(poleLon);
            kz[p] = Math.Sin(poleLat);
            var theta = av.Rate * DEG2RAD * deltaMa;
            cosTheta[p] = Math.Cos(theta);
            sinTheta[p] = Math.Sin(theta);
        }

        // Allocate destination buffers.
        var newHeight    = new float[cc];
        var newCrust     = new float[cc];
        var newRockType  = new byte[cc];
        var newRockAge   = new float[cc];
        var newPlateMap  = new ushort[cc];
        var hitCount     = new int[cc]; // number of source cells that landed here
        // Track source → destination mapping for stratigraphy remapping.
        var stratigraphyMapping = new int[cc]; // sourceIdx → destIdx

        // Sentinel: mark all destinations as empty.
        const float EMPTY = float.NegativeInfinity;
        Array.Fill(newHeight, EMPTY);

        // ── Phase 1: Compute destination indices ─────────────────────────────
        // GPU: Rodrigues' rotation kernel computes destMap[srcIdx] = destIdx.
        // CPU fallback: inline rotation loop.
        int[] destMap;
        if (gpu != null)
        {
            destMap = gpu.ComputeAdvectDestinations(state.PlateMap, kx, ky, kz, cosTheta, sinTheta, gs);
        }
        else
        {
            destMap = new int[cc];
            for (var i = 0; i < cc; i++)
            {
                var row = i / gs;
                var col = i % gs;
                var lat = RowToLat(row, gs);
                var lon = ColToLon(col, gs);

                int plateIdx = state.PlateMap[i];

                var cosLat = Math.Cos(lat);
                var sinLat = Math.Sin(lat);
                var cosLon = Math.Cos(lon);
                var sinLon = Math.Sin(lon);
                var vx = cosLat * cosLon;
                var vy = cosLat * sinLon;
                var vz = sinLat;

                var ct = cosTheta[plateIdx];
                var st = sinTheta[plateIdx];
                var pkx = kx[plateIdx];
                var pky = ky[plateIdx];
                var pkz = kz[plateIdx];

                var cx = pky * vz - pkz * vy;
                var cy = pkz * vx - pkx * vz;
                var cz = pkx * vy - pky * vx;

                var dot = pkx * vx + pky * vy + pkz * vz;
                var oneMinusCos = 1.0 - ct;

                var nx = vx * ct + cx * st + pkx * dot * oneMinusCos;
                var ny = vy * ct + cy * st + pky * dot * oneMinusCos;
                var nz = vz * ct + cz * st + pkz * dot * oneMinusCos;

                var newLat = Math.Asin(Math.Clamp(nz, -1.0, 1.0));
                var newLon = Math.Atan2(ny, nx);

                var newRow = (int)Math.Round((Math.PI / 2.0 - newLat) / Math.PI * gs);
                var newCol = (int)Math.Round((newLon + Math.PI) / TWO_PI * gs);
                newRow = Math.Clamp(newRow, 0, gs - 1);
                newCol = ((newCol % gs) + gs) % gs;
                destMap[i] = newRow * gs + newCol;
            }
        }

        // ── Phase 2: Scatter + collision resolution ─────────────────────────────
        // GPU path uses atomic operations for parallel collision resolution;
        // CPU fallback uses the sequential loop with plate-type branching.
        bool gpuCollisionDone = false;
        if (gpu != null)
        {
            try
            {
                var result = gpu.ResolveCollisionsGpu(destMap,
                    state.HeightMap, state.CrustThicknessMap, state.RockTypeMap,
                    state.RockAgeMap, state.PlateMap, _plates, (float)deltaMa);

                Array.Copy(result.newHeight,   newHeight,   cc);
                Array.Copy(result.newCrust,    newCrust,    cc);
                Array.Copy(result.newRockType, newRockType, cc);
                Array.Copy(result.newRockAge,  newRockAge,  cc);
                Array.Copy(result.newPlateMap, newPlateMap, cc);
                Array.Copy(result.hitCount,    hitCount,    cc);

                // Build stratigraphy mapping from destMap
                for (var i = 0; i < cc; i++)
                    stratigraphyMapping[i] = destMap[i];

                gpuCollisionDone = true;
            }
            catch
            {
                // GPU collision failed — fall through to CPU path
            }
        }

        if (!gpuCollisionDone)
        {
            for (var i = 0; i < cc; i++)
            {
                var destIdx = destMap[i];
                stratigraphyMapping[i] = destIdx;
                hitCount[destIdx]++;

                if (newHeight[destIdx] <= EMPTY)
                {
                    newHeight[destIdx]   = state.HeightMap[i];
                    newCrust[destIdx]    = state.CrustThicknessMap[i];
                    newRockType[destIdx] = state.RockTypeMap[i];
                    newRockAge[destIdx]  = state.RockAgeMap[i];
                    newPlateMap[destIdx] = state.PlateMap[i];
                }
                else
                {
                    int plateIdx = state.PlateMap[i];
                    var srcPlate = _plates[plateIdx];
                    var dstPlate = _plates[newPlateMap[destIdx]];

                    if (!srcPlate.IsOceanic && !dstPlate.IsOceanic)
                    {
                        newHeight[destIdx] = Math.Max(newHeight[destIdx], state.HeightMap[i]);
                        newHeight[destIdx] += (float)(50.0 * deltaMa);
                        newCrust[destIdx] += state.CrustThicknessMap[i] * 0.3f;
                    }
                    else if (srcPlate.IsOceanic && !dstPlate.IsOceanic)
                    {
                        newHeight[destIdx] += (float)(10.0 * deltaMa);
                    }
                    else if (!srcPlate.IsOceanic && dstPlate.IsOceanic)
                    {
                        newHeight[destIdx]   = state.HeightMap[i];
                        newCrust[destIdx]    = state.CrustThicknessMap[i];
                        newRockType[destIdx] = state.RockTypeMap[i];
                        newRockAge[destIdx]  = state.RockAgeMap[i];
                        newPlateMap[destIdx] = state.PlateMap[i];
                    }
                    else
                    {
                        if (state.RockAgeMap[i] < newRockAge[destIdx])
                        {
                            newHeight[destIdx]   = state.HeightMap[i];
                            newCrust[destIdx]    = state.CrustThicknessMap[i];
                            newRockType[destIdx] = state.RockTypeMap[i];
                            newRockAge[destIdx]  = state.RockAgeMap[i];
                            newPlateMap[destIdx] = state.PlateMap[i];
                        }
                    }
                }
            }
        }

        // ── Phase 3: Gap fill ────────────────────────────────────────────────
        // GPU fills the simple per-cell properties; CPU handles plate assignment
        // via neighbor search which is irregular and not suited for GPU.
        if (gpu != null)
        {
            gpu.FillGapCells(newHeight, newCrust, newRockType, newRockAge,
                hitCount, GAP_FLOOR_HEIGHT, GAP_CRUST_KM, (byte)RockType.IGN_BASALT, (float)timeMa);
        }
        else
        {
            for (var i = 0; i < cc; i++)
            {
                if (hitCount[i] > 0) continue;
                newHeight[i]   = GAP_FLOOR_HEIGHT;
                newCrust[i]    = GAP_CRUST_KM;
                newRockType[i] = (byte)RockType.IGN_BASALT;
                newRockAge[i]  = (float)timeMa;
            }
        }

        // Plate assignment for gaps always on CPU (irregular neighbor search).
        for (var i = 0; i < cc; i++)
        {
            if (hitCount[i] > 0) continue;
            newPlateMap[i] = FindNearestPlate(i, newPlateMap, hitCount, gs);
        }

        // ── Commit new buffers to state ──────────────────────────────────────
        Array.Copy(newHeight,   state.HeightMap,          cc);
        Array.Copy(newCrust,    state.CrustThicknessMap,  cc);
        Array.Copy(newRockType, state.RockTypeMap,        cc);
        Array.Copy(newRockAge,  state.RockAgeMap,         cc);
        Array.Copy(newPlateMap, state.PlateMap,            cc);

        // ── Remap stratigraphy columns ───────────────────────────────────────
        Stratigraphy.RemapColumns(stratigraphyMapping, cc, hitCount, timeMa);
    }

    /// <summary>
    /// Find the nearest plate for a gap cell by spiralling outward through neighbours.
    /// </summary>
    private static ushort FindNearestPlate(int cellIdx, ushort[] plateMap, int[] hitCount, int gs)
    {
        var row = cellIdx / gs;
        var col = cellIdx % gs;
        for (var r = 1; r <= gs / 2; r++)
        {
            for (var dr = -r; dr <= r; dr++)
            {
                for (var dc = -r; dc <= r; dc++)
                {
                    if (Math.Abs(dr) != r && Math.Abs(dc) != r) continue; // perimeter only
                    var nr = row + dr;
                    var nc = ((col + dc) % gs + gs) % gs;
                    if (nr < 0 || nr >= gs) continue;
                    var ni = nr * gs + nc;
                    if (hitCount[ni] > 0) return plateMap[ni];
                }
            }
        }
        return 0;
    }

    /// <summary>Recalculate plate centers from the current plate map.</summary>
    private void UpdatePlateCenters(SimulationState state)
    {
        var gs = state.GridSize;
        var numPlates = _plates.Count;

        double[] sumX, sumY, sumZ, count;

        if (gpu != null)
        {
            // ── GPU path: atomic per-cell accumulation ────────────────────
            (sumX, sumY, sumZ, count) = gpu.ComputePlateCenterSums(state.PlateMap, gs, numPlates);
        }
        else
        {
            // ── CPU fallback ──────────────────────────────────────────────
            sumX  = new double[numPlates];
            sumY  = new double[numPlates];
            sumZ  = new double[numPlates];
            count = new double[numPlates];

            for (var row = 0; row < gs; row++)
            {
                var lat = RowToLat(row, gs);
                var cosLat = Math.Cos(lat);
                var sinLat = Math.Sin(lat);
                for (var col = 0; col < gs; col++)
                {
                    var lon = ColToLon(col, gs);
                    int p = state.PlateMap[row * gs + col];
                    if (p >= numPlates) continue;
                    sumX[p] += cosLat * Math.Cos(lon);
                    sumY[p] += cosLat * Math.Sin(lon);
                    sumZ[p] += sinLat;
                    count[p]++;
                }
            }
        }

        for (var p = 0; p < numPlates; p++)
        {
            if (count[p] == 0) continue;
            var mx = sumX[p] / count[p];
            var my = sumY[p] / count[p];
            var mz = sumZ[p] / count[p];
            var r = Math.Sqrt(mx * mx + my * my + mz * mz);
            if (r < 1e-12) continue;
            _plates[p].CenterLat = Math.Asin(Math.Clamp(mz / r, -1, 1)) / DEG2RAD;
            _plates[p].CenterLon = Math.Atan2(my, mx) / DEG2RAD;
            _plates[p].Area = count[p] / state.CellCount;
        }
    }

    private void ProcessConvergent(List<BoundaryCell> boundaries, double deltaMa, double timeMa, SimulationState state)
    {
        foreach (var b in boundaries.Where(b => b.Type == BoundaryType.CONVERGENT))
        {
            var p1 = _plates[b.Plate1];
            var p2 = _plates[b.Plate2];
            if (!p1.IsOceanic && !p2.IsOceanic)
                ApplyCollision(b, deltaMa, timeMa, state);
            else
                ApplySubduction(b.CellIndex, deltaMa, state);
        }
    }

    private static void ApplySubduction(int ci, double deltaMa, SimulationState state)
    {
        state.HeightMap[ci] += (float)(-50 * deltaMa);
        state.CrustThicknessMap[ci] = Math.Max(3, state.CrustThicknessMap[ci] - (float)(0.5 * deltaMa));
    }

    private void ApplyCollision(BoundaryCell b, double deltaMa, double timeMa, SimulationState state)
    {
        var ci = b.CellIndex;
        state.CrustThicknessMap[ci] += (float)(2 * deltaMa * b.RelativeSpeed);
        state.HeightMap[ci] += (float)(100 * deltaMa * b.RelativeSpeed);
        Stratigraphy.ApplyDeformation(ci, 2 * deltaMa, 0, DeformationType.FOLDED);

        if (!(b.RelativeSpeed > 2.0)) return;
        bus.Emit("PLATE_COLLISION", new { plate1 = b.Plate1, plate2 = b.Plate2 });
        eventLog.Record(new GeoLogEntry
        {
            TimeMa = timeMa, Type = "PLATE_COLLISION",
            Description = $"Collision between plates {b.Plate1} and {b.Plate2}",
        });
    }

    private static void ProcessDivergent(List<BoundaryCell> boundaries, double deltaMa, double timeMa, SimulationState state)
    {
        foreach (var b in boundaries.Where(b => b.Type == BoundaryType.DIVERGENT))
        {
            var thinning = 0.3 * deltaMa * b.RelativeSpeed;
            state.CrustThicknessMap[b.CellIndex] = Math.Max(3, state.CrustThicknessMap[b.CellIndex] - (float)thinning);
            if (!(state.CrustThicknessMap[b.CellIndex] < 10)) continue;
            state.RockTypeMap[b.CellIndex] = (byte)RockType.IGN_BASALT;
            state.RockAgeMap[b.CellIndex] = (float)timeMa;
        }
    }

    /// <summary>
    /// Apply isostatic equilibrium adjustment to height.
    /// When a <see cref="GpuComputeService"/> is available the ILGPU kernel is used (GPU or
    /// multi-threaded CPU via ILGPU).  Otherwise falls back to SIMD + Parallel.For.
    /// Equilibrium: eq = crust * 1000 * (1 − ISOSTATIC_RATIO) − 4500
    /// height_new  = height * (1 − relax) + eq * relax
    /// </summary>
    private void ApplyIsostasy(SimulationState state, double deltaMa)
    {
        var relaxF  = (float)Math.Min(1, 0.1 * deltaMa);
        var factor  = (float)(1000.0 * (1 - ISOSTATIC_RATIO));
        const float offset = -4500f;

        if (gpu != null)
        {
            // ── GPU / ILGPU path ──────────────────────────────────────────
            gpu.ApplyIsostasy(state.HeightMap, state.CrustThicknessMap, relaxF, factor, offset);
            return;
        }

        // ── CPU SIMD + Parallel.For fallback ─────────────────────────────
        var cc = state.CellCount;
        var vRelax     = new Vector<float>(relaxF);
        var vRelaxComp = new Vector<float>(1f - relaxF);
        var vFactor    = new Vector<float>(factor);
        var vOffset    = new Vector<float>(offset);
        var height     = state.HeightMap;
        var crust      = state.CrustThicknessMap;
        var vLen       = Vector<float>.Count; // typically 8 on AVX2

        Parallel.For(0, (cc + vLen - 1) / vLen, chunk =>
        {
            var start = chunk * vLen;
            var end   = Math.Min(start + vLen, cc);
            var len   = end - start;

            if (len == vLen)
            {
                var vCrust = new Vector<float>(crust, start);
                var vH     = new Vector<float>(height, start);
                var vEq    = vCrust * vFactor + vOffset;
                var vNew   = vH * vRelaxComp + vEq * vRelax;
                vNew.CopyTo(height, start);
            }
            else
            {
                var relax = (double)relaxF;
                for (var i = start; i < end; i++)
                {
                    var eq = crust[i] * 1000.0 * (1 - ISOSTATIC_RATIO) - 4500;
                    height[i] += (float)((eq - height[i]) * relax);
                }
            }
        });
    }

    public IReadOnlyList<PlateInfo> GetPlates() => _plates;
    public IReadOnlyList<HotspotInfo> GetHotspots() => _hotspots;
    public AtmosphericComposition GetAtmosphere() => _atmosphere;
}
