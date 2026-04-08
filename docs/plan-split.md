# GeoTime — Tectonic Engine Split & Performance Plan

## 1. Problem Statement

The tectonic engine ("tectonic agent") dominates each simulation tick. While it runs, the UI receives no intermediate updates — the app appears frozen for the entire duration of `TectonicEngine.Tick()`. This document analyzes the root cause, confirms whether it is a computation issue or a UI-update issue, and provides a step-by-step plan for splitting the tectonic engine into smaller, GPU-accelerated sub-agents that yield control back to the UI between phases.

---

## 2. Root Cause Analysis: Computation vs. UI Blocking

### 2.1 How the simulation loop works today

The frontend calls `POST /api/simulation/advance` (REST) every 200 ms (`SIM_UPDATE_INTERVAL`). This call is **synchronous on the server**: `SimulationOrchestrator.AdvanceSimulation` acquires a `SemaphoreSlim(1,1)` lock and calls `AdvanceSimulationCore`, which executes **all** engine phases in sequence before returning the HTTP response:

```
AdvanceSimulationCore(deltaMa)
├── onProgress("tectonic")          ← label only, no state push
├── TectonicEngine.Tick()           ← BLOCKING (longest phase)
│   ├── AdvectPlates()
│   │   ├── Phase 1: Rodrigues' rotation (GPU or CPU) — 262K cells
│   │   ├── Phase 2: Scatter + collision (CPU) — sequential
│   │   └── Phase 3: Gap fill (GPU or CPU) + nearest-plate search (CPU)
│   ├── UpdatePlateCenters() (GPU or CPU)
│   └── while(sub-ticks remain):
│       ├── BoundaryClassifier.Classify() — O(N) CPU scan
│       ├── ProcessConvergent() — per-boundary-cell
│       ├── ProcessDivergent() — per-boundary-cell
│       ├── ApplyIsostasy() — GPU or SIMD+Parallel.For
│       └── VolcanismEngine.Tick() — per-boundary-cell
├── onProgress("surface")
├── Surface + Atmosphere + Vegetation (parallel Tasks)
├── onProgress("biomatter")
├── BiomatterEngine.Tick()
├── FeatureDetectorService.Detect()  ← flood-fill, O(N)
├── FeatureEvolutionTracker.Track()
└── onProgress("complete")
```

### 2.2 Why the UI freezes

The `onProgress` callback collects phase names into a list; they are sent via SignalR **after** the entire `AdvanceSimulation` call returns:

```csharp
// Program.cs — advance endpoint
var phases = new List<string>();
sim.AdvanceSimulation(req.DeltaMa, phase => phases.Add(phase));
// Phases are sent AFTER the simulation is done:
foreach (var phase in phases)
    await hubContext.Clients.All.SendAsync("SimulationProgress", ...);
```

The SignalR hub's `AdvanceSimulation` method is slightly better — it fires `SendAsync` from the `onProgress` lambda — but even there, the `sim.AdvanceSimulation(stepSize, phase => { ... })` call is **synchronous and blocking**. The tectonic phase runs first and occupies the thread for the entire duration. No `await` yields occur during `TectonicEngine.Tick()`, so no SignalR messages are actually flushed until the method returns.

**Verdict: This is a computation-blocking issue, not a UI rendering issue.** The server thread is held by the tectonic engine's CPU work, preventing any SignalR messages from being delivered until the entire tick completes.

### 2.3 Sub-tick amplification

`TectonicEngine.Tick()` has an internal accumulator with `minTickInterval = 0.1` Ma. If `deltaMa = 0.5`, it runs **5 sub-ticks**, each calling:
- `BoundaryClassifier.Classify()` — scans all 262K cells
- `ProcessConvergent()` / `ProcessDivergent()` — iterates all boundary cells
- `ApplyIsostasy()` — 262K cells (GPU-offloaded when available)
- `VolcanismEngine.Tick()` — iterates boundary/hotspot cells

Plus one call to `AdvectPlates()` which itself has three phases over 262K cells.

### 2.4 Additional CPU-bound bottlenecks

Beyond the core tectonic work, these operations run every tick and are not GPU-accelerated:

1. **BoundaryClassifier.Classify()** — O(N) with neighbor lookups per cell, called every sub-tick
2. **Scatter + collision resolution** (AdvectPlates Phase 2) — sequential loop, cannot be parallelized due to write conflicts
3. **FindNearestPlate()** — spiraling neighbor search for each gap cell
4. **StratigraphyStack.RemapColumns()** — dictionary-based column remapping under a global lock
5. **FeatureDetectorService.Detect()** — flood-fill over 262K cells, called every tick
6. **HydroDetectorService** — D8 flow routing, river tracing, called every tick via FeatureDetectorService

---

## 3. Split Architecture: Sub-Agents

The current monolithic `TectonicEngine.Tick()` should be broken into **5 independent sub-agents** that can each yield control, report progress, and offload more work to the GPU.

### 3.1 Sub-Agent Definitions

| # | Sub-Agent | Current Code | GPU Opportunity | Depends On |
|---|-----------|-------------|-----------------|------------|
| 1 | **AdvectionAgent** | `AdvectPlates()` Phase 1 (Rodrigues' rotation) | ✅ Already GPU | — |
| 2 | **CollisionAgent** | `AdvectPlates()` Phase 2 (scatter + collision) + Phase 3 (gap fill) | ⚠️ Partial (see §4.2) | AdvectionAgent |
| 3 | **BoundaryAgent** | `BoundaryClassifier.Classify()` + cache | ✅ New GPU kernel (see §4.3) | CollisionAgent |
| 4 | **PlateDynamicsAgent** | `ProcessConvergent()`, `ProcessDivergent()`, `ApplyIsostasy()` | ✅ Isostasy already GPU; convergent/divergent can be GPU | BoundaryAgent |
| 5 | **VolcanismAgent** | `VolcanismEngine.Tick()` + atmosphere CO2 update | ⚠️ Low cell count, CPU fine | PlateDynamicsAgent |

### 3.2 Orchestration Pattern

Replace the current synchronous `Tick()` loop with an async pipeline:

```
TectonicEngine.TickAsync(timeMa, deltaMa, progressCallback)
│
├── await AdvectionAgent.RunAsync()
│   └── progressCallback("tectonic:advection")
│
├── await CollisionAgent.RunAsync()
│   └── progressCallback("tectonic:collision")
│
├── UpdatePlateCenters()  [GPU]
│   └── progressCallback("tectonic:centers")
│
└── for each sub-tick:
    ├── await BoundaryAgent.RunAsync()
    │   └── progressCallback("tectonic:boundaries")
    │
    ├── await PlateDynamicsAgent.RunAsync()
    │   └── progressCallback("tectonic:dynamics")
    │
    └── await VolcanismAgent.RunAsync()
        └── progressCallback("tectonic:volcanism")
```

The `await` at each step allows the async context to yield, enabling SignalR to flush queued messages and the UI to update.

### 3.3 Progress Granularity for the Frontend

Update the frontend `PHASE_LABELS` and `agentStatuses` to show tectonic sub-phases:

```typescript
const agentStatuses = {
  'tectonic:advection':  'idle',
  'tectonic:collision':  'idle',
  'tectonic:boundaries': 'idle',
  'tectonic:dynamics':   'idle',
  'tectonic:volcanism':  'idle',
  surface:    'idle',
  atmosphere: 'idle',
  vegetation: 'idle',
  biomatter:  'idle',
};
```

The `SimulationTickStats` class should also report sub-phase timing:

```csharp
public sealed class SimulationTickStats
{
    // Tectonic sub-phases
    public long TectonicAdvectionMs  { get; set; }
    public long TectonicCollisionMs  { get; set; }
    public long TectonicBoundaryMs   { get; set; }
    public long TectonicDynamicsMs   { get; set; }
    public long TectonicVolcanismMs  { get; set; }
    public long TectonicTotalMs      { get; set; }
    // Other phases
    public long SurfaceMs   { get; set; }
    public long AtmosphereMs { get; set; }
    public long VegetationMs { get; set; }
    public long BiomatterMs  { get; set; }
    public long TotalMs      { get; set; }
    public double TimeMa     { get; set; }
}
```

---

## 4. GPU Offloading Opportunities

### 4.1 AdvectionAgent — Already GPU (No Change Needed)

`GpuComputeService.ComputeAdvectDestinations()` runs the Rodrigues' rotation kernel on the GPU. The `GpuComputeService.FillGapCells()` kernel handles gap-cell property fill. These are already well-optimized.

### 4.2 CollisionAgent — Partial GPU Offload

**Current bottleneck:** The scatter + collision loop (Phase 2) is sequential because multiple source cells may map to the same destination, requiring conflict resolution.

**GPU strategy — Atomic collision kernel:**

Create a new GPU kernel that uses atomic operations to resolve collisions:

```
CollisionKernel(idx, destMap, srcHeight, srcCrust, srcRockType, srcRockAge, srcPlateMap,
                newHeight, newCrust, newRockType, newRockAge, newPlateMap, hitCount,
                plateIsOceanic, deltaMa)
{
    dest = destMap[idx];
    Atomic.Add(ref hitCount[dest], 1);

    // Use AtomicMax for continental collision (highest height wins)
    // Use atomic compare-exchange for plate type priority
    // Continental over oceanic, younger rock over older for oceanic-oceanic
}
```

**Complexity:** ILGPU supports `Atomic.Add` for int/float/double and `Atomic.CompareExchange`. The collision priority logic (continental > oceanic, youngest rock wins for oceanic-oceanic) can be encoded as an atomic max on a packed priority value.

**Implementation steps:**
1. Pack priority = (isContinental ? 1 : 0) << 31 | floatBitsToInt(height) into an int for atomic comparison
2. Use `Atomic.Max` on the packed priority to select the winning source
3. After the kernel, a second pass copies the winning source's full data to the destination

**Expected speedup:** 3–5× for the scatter phase (262K cells → ~1 ms on GPU vs. ~5 ms sequential CPU).

### 4.3 BoundaryAgent — New GPU Kernel

**Current bottleneck:** `BoundaryClassifier.Classify()` iterates all 262K cells, checking 4 neighbors each, computing velocity at boundary cells. Called every sub-tick (up to 5× per tick).

**GPU strategy — Per-cell boundary classification kernel:**

```
BoundaryClassifyKernel(idx, plateMap, plateVelocityParams, gs,
                       boundaryType, boundaryPlate1, boundaryPlate2, relativeSpeed)
{
    int myPlate = plateMap[idx];
    // Check 4 neighbors
    for each neighbor:
        if plateMap[neighbor] != myPlate:
            compute relative velocity
            classify as CONVERGENT / DIVERGENT / TRANSFORM
            write to output arrays
            return
    // Not a boundary cell
    boundaryType[idx] = NONE;
}
```

Output: four flat arrays (`boundaryType[N]`, `plate1[N]`, `plate2[N]`, `relSpeed[N]`), one entry per cell. The CPU then compacts non-NONE entries into the boundary list using a simple scan.

**Caching optimization:** Cache the boundary list and only recompute when the plate map changes (i.e., after advection). Between advection events, sub-ticks operate on the same plate boundaries.

**Expected speedup:** 10–20× (262K neighbor checks → ~0.5 ms on GPU; CPU scan for compaction ~1 ms).

### 4.4 PlateDynamicsAgent — Convergent/Divergent GPU Kernels

**Current bottleneck:** `ProcessConvergent()` and `ProcessDivergent()` iterate boundary cell lists. These are typically 5K–20K cells, which is small for the GPU but can still benefit.

**GPU strategy:**
- Upload the compacted boundary list (type, cellIndex, plate1, plate2, relSpeed) to the GPU.
- Run a kernel over boundary cells only (not all 262K cells).
- For convergent boundaries: update height, crust, and stratigraphy deformation.
- For divergent boundaries: thin crust, update rock type/age.

**Complication:** `ProcessConvergent()` calls `Stratigraphy.ApplyDeformation()`, which modifies the `StratigraphyStack` under a lock. This cannot run on the GPU directly.

**Hybrid approach:**
1. GPU kernel computes height and crust updates for all boundary cells into delta buffers.
2. CPU applies the delta buffers to state arrays (single memcpy-like pass).
3. CPU applies stratigraphy deformation only for convergent cells (batch operation).

**Expected speedup:** 2–3× for the dynamics sub-phase. The main gain is from eliminating per-cell locking in the stratigraphy stack.

### 4.5 StratigraphyStack Lock Contention

The `StratigraphyStack` uses a single global `Lock` object for all operations. This serializes all stratigraphy access and prevents any parallelism.

**Improvement:** Replace the single global lock with per-cell or per-stripe locking:

```csharp
// Instead of one lock for all cells:
private readonly Lock _lockObject = new();

// Use sharded locks (e.g., 256 stripes):
private readonly Lock[] _stripeLocks = Enumerable.Range(0, 256)
    .Select(_ => new Lock()).ToArray();

private Lock GetLock(int cellIndex) => _stripeLocks[cellIndex & 0xFF];
```

This allows parallel stratigraphy operations on non-colliding cells, which is the common case during convergent/divergent processing.

---

## 5. Async Plumbing Changes

### 5.1 Make TectonicEngine Async

Convert `TectonicEngine.Tick()` from synchronous to async:

```csharp
public async Task<List<EruptionRecord>> TickAsync(
    double timeMa, double deltaMa,
    Func<string, Task>? onSubPhase = null)
{
    // ... same accumulator logic ...

    await RunAdvectionAsync(state, deltaMa, timeMa);
    if (onSubPhase != null) await onSubPhase("tectonic:advection");

    await RunCollisionResolutionAsync(state);
    if (onSubPhase != null) await onSubPhase("tectonic:collision");

    UpdatePlateCenters(state);
    if (onSubPhase != null) await onSubPhase("tectonic:centers");

    while (_accumulator >= minTickInterval)
    {
        _accumulator -= minTickInterval;
        var subTime = timeMa - _accumulator;

        var boundaries = await ClassifyBoundariesAsync(state);
        if (onSubPhase != null) await onSubPhase("tectonic:boundaries");

        await ProcessBoundariesAsync(boundaries, minTickInterval, subTime, state);
        if (onSubPhase != null) await onSubPhase("tectonic:dynamics");

        var eruptions = VolcanismEngine.Tick(...);
        if (onSubPhase != null) await onSubPhase("tectonic:volcanism");

        all.AddRange(eruptions);
    }

    return all;
}
```

### 5.2 Make SimulationOrchestrator Async

Convert `AdvanceSimulationCore` to `async Task`:

```csharp
private async Task AdvanceSimulationCoreAsync(
    double deltaMa, Func<string, Task>? onProgress)
{
    // ...
    await _tectonic.TickAsync(Clock.T, deltaMa, onProgress);
    // ...
    if (onProgress != null) await onProgress("surface");
    // ... parallel surface/atmosphere/vegetation ...
    if (onProgress != null) await onProgress("biomatter");
    // ...
    if (onProgress != null) await onProgress("complete");
}
```

### 5.3 Update REST and SignalR Endpoints

The REST advance endpoint should become async and flush SignalR messages between phases:

```csharp
app.MapPost("/api/simulation/advance", async (AdvanceRequest req, ...) =>
{
    await sim.AdvanceSimulationAsync(req.DeltaMa, async phase =>
    {
        await hubContext.Clients.All.SendAsync("SimulationProgress", new { phase, ... });
    });
    // ...
});
```

The SignalR hub's `AdvanceSimulation` already uses async lambdas but wraps a synchronous call; converting the engine to async makes the SignalR delivery truly incremental.

### 5.4 Frontend: Incremental State Updates

Currently the frontend fetches the full state bundle after the advance completes. With sub-phase progress, the frontend can optionally request partial state updates:

- After `tectonic:collision` — push height map (terrain has changed)
- After `surface` — push height map again (erosion/glacial)
- After `complete` — push full bundle (temp + precip + height)

This gives the user visual feedback during long ticks. The `StateBundleData` SignalR message can include a `phase` field so the frontend knows which data to update.

---

## 6. FeatureDetectorService Optimization

`FeatureDetectorService.Detect()` runs every tick and includes:
- Flood-fill for land/ocean detection (O(N))
- Mountain range detection (O(N))
- `HydroDetectorService` D8 routing (O(N) × 2 passes)

### 6.1 Run Feature Detection Less Frequently

Feature detection does not need to run every tick. Run it every N ticks (e.g., every 5 ticks or every 0.5 Ma) and cache the result:

```csharp
private int _featureDetectionInterval = 5;
private int _ticksSinceLastDetection = 0;

// In AdvanceSimulationCore:
_ticksSinceLastDetection++;
if (_ticksSinceLastDetection >= _featureDetectionInterval)
{
    _featureDetector.Detect(...);
    _featureEvolution.Track(...);
    _ticksSinceLastDetection = 0;
}
```

### 6.2 GPU-Accelerated Flood Fill

The flood-fill used for land/ocean detection can be replaced with a GPU-based connected-component labeling algorithm (e.g., union-find with path compression on GPU). This is a well-studied GPU problem. ILGPU can express the iterative label propagation:

1. Initialize: `label[i] = (height[i] >= threshold) ? i : -1`
2. Iterate: each cell adopts the minimum label of its same-category neighbors
3. Converge when no labels change (typically 10–20 iterations for a 512×512 grid)

**Expected speedup:** 5–10× for the flood-fill portion of feature detection.

---

## 7. Boundary Classifier Caching

`BoundaryClassifier.Classify()` is called every sub-tick but only needs to be recomputed when the plate map changes (which happens once per tick during advection). Caching it across sub-ticks is a simple win:

```csharp
private List<BoundaryCell>? _cachedBoundaries;
private int _cachedPlateMapHash;

private List<BoundaryCell> GetOrClassifyBoundaries(SimulationState state)
{
    var hash = ComputePlateMapHash(state.PlateMap);
    if (_cachedBoundaries != null && hash == _cachedPlateMapHash)
        return _cachedBoundaries;

    _cachedBoundaries = BoundaryClassifier.Classify(state.PlateMap, _plates, state.GridSize);
    _cachedPlateMapHash = hash;
    return _cachedBoundaries;
}
```

Since `AdvectPlates()` runs once per tick and sub-ticks do not move plates, the boundaries are identical across all sub-ticks within a single tick. This eliminates 4 out of 5 boundary classification calls for a typical 0.5 Ma tick.

**Expected speedup:** 4–5× for the boundary classification portion of the tick.

---

## 8. Implementation Plan (Ordered Steps)

### Phase S1 — Boundary Classifier Caching (Quick Win)
**Effort:** Low | **Impact:** 4–5× on boundary classification | **Files:** `TectonicEngine.cs`

- [ ] Cache the `BoundaryClassifier.Classify()` result after advection
- [ ] Reuse the cached result for all sub-ticks within the same tick
- [ ] Clear the cache when the plate map changes (on `AdvectPlates()`)
- [ ] Add unit test verifying cache hit across sub-ticks

### Phase S2 — Async Pipeline
**Effort:** Medium | **Impact:** Eliminates UI freeze | **Files:** `TectonicEngine.cs`, `SimulationOrchestrator.cs`, `Program.cs`, `SimulationHub.cs`, `main.ts`

- [ ] Convert `TectonicEngine.Tick()` to `TickAsync()` with `Func<string, Task>` callback
- [ ] Split the tectonic tick body into named sub-phase methods
- [ ] Convert `SimulationOrchestrator.AdvanceSimulationCore()` to `async Task`
- [ ] Update `AdvanceSimulation()` to call `AdvanceSimulationCoreAsync()`
- [ ] Update REST endpoint to `await` the async advance
- [ ] Update `SimulationHub.AdvanceSimulation` to use the async path
- [ ] Extend `SimulationTickStats` with per-sub-phase timing fields
- [ ] Update frontend `agentStatuses` to include tectonic sub-phases
- [ ] Update frontend `PHASE_LABELS` for tectonic sub-phase names
- [ ] Test that SignalR progress messages arrive during the tick (not only after)
- [ ] Add backend unit test for async tick stats

### Phase S3 — GPU Boundary Classification Kernel
**Effort:** Medium | **Impact:** 10–20× on boundary classification | **Files:** `GpuComputeService.cs`, `BoundaryClassifier.cs`, `TectonicEngine.cs`

- [ ] Add `BoundaryClassifyKernel` to `GpuComputeService`
- [ ] Kernel outputs flat arrays: `boundaryType[N]`, `plate1[N]`, `plate2[N]`, `relSpeed[N]`
- [ ] Add `ClassifyBoundariesGpu()` method to `GpuComputeService`
- [ ] CPU compaction pass: filter non-NONE entries into `List<BoundaryCell>`
- [ ] Update `TectonicEngine` to use GPU classification when available
- [ ] Retain CPU fallback in `BoundaryClassifier.Classify()`
- [ ] Add unit tests comparing GPU and CPU classification results

### Phase S4 — StratigraphyStack Lock Optimization
**Effort:** Medium | **Impact:** Enables parallel stratigraphy access | **Files:** `StratigraphyStack.cs`

- [ ] Replace single global `Lock` with striped locks (256 stripes)
- [ ] Update all methods (`PushLayer`, `ErodeTop`, `ApplyDeformation`, etc.) to use per-cell stripe locks
- [ ] `RemapColumns()` can use a write-lock pattern: build the new dictionary, then swap atomically
- [ ] Benchmark stratigraphy throughput with parallel boundary processing
- [ ] Add unit tests for concurrent stratigraphy access

### Phase S5 — GPU Collision Resolution Kernel
**Effort:** High | **Impact:** 3–5× on scatter phase | **Files:** `GpuComputeService.cs`, `TectonicEngine.cs`

- [ ] Design packed-priority representation for collision resolution
- [ ] Add `CollisionScatterKernel` to `GpuComputeService`
- [ ] Kernel uses `Atomic.Max` on packed priority to select winning source per destination
- [ ] Second-pass kernel copies winning source data to destination arrays
- [ ] CPU handles stratigraphy remapping (cannot be done on GPU due to dictionary structure)
- [ ] Retain CPU fallback for scatter + collision
- [ ] Add unit tests comparing GPU and CPU scatter results

### Phase S6 — Feature Detection Throttling
**Effort:** Low | **Impact:** 4–5× reduction in feature detection overhead | **Files:** `SimulationOrchestrator.cs`

- [ ] Add `_featureDetectionInterval` field (default: 5 ticks)
- [ ] Skip feature detection on intermediate ticks
- [ ] Ensure feature registry is still populated on the first tick after planet generation
- [ ] Add unit test verifying features still update at the correct cadence

### Phase S7 — GPU Convergent/Divergent Processing
**Effort:** Medium | **Impact:** 2–3× on dynamics sub-phase | **Files:** `GpuComputeService.cs`, `TectonicEngine.cs`

- [ ] Add kernel for convergent boundary height/crust updates
- [ ] Add kernel for divergent boundary crust thinning
- [ ] GPU computes delta buffers; CPU applies stratigraphy deformation
- [ ] Retain CPU fallback
- [ ] Add unit tests

### Phase S8 — Frontend Incremental Rendering
**Effort:** Low | **Impact:** Better visual feedback during ticks | **Files:** `SimulationHub.cs`, `main.ts`, `backend-client.ts`

- [ ] After tectonic:collision, push height-only state update via SignalR
- [ ] Frontend handles incremental height-map updates mid-tick
- [ ] Add `phase` field to `StateBundleData` so frontend knows what changed
- [ ] Test that the globe visually updates during long ticks

---

## 9. Expected Impact Summary

| Phase | What | Speed Gain | UI Responsiveness |
|-------|------|-----------|-------------------|
| S1 | Boundary cache | ~20% total tick | No change |
| S2 | Async pipeline | None (same total time) | **UI no longer freezes** |
| S3 | GPU boundaries | ~10% total tick | Better (fewer CPU stalls) |
| S4 | Stratigraphy locks | ~5% total tick | Enables S5/S7 parallelism |
| S5 | GPU collisions | ~15% total tick | Better |
| S6 | Feature throttle | ~10% total tick | Better (less post-tick work) |
| S7 | GPU convergent/divergent | ~5% total tick | Better |
| S8 | Incremental rendering | None | **Visual updates mid-tick** |

**Combined effect:** The tectonic tick should complete ~40–50% faster, and the UI should remain responsive throughout, showing sub-phase progress and updated terrain mid-tick.

---

## 10. Testing Strategy

### 10.1 Correctness Tests

Each GPU kernel must produce results identical (within floating-point tolerance) to the CPU fallback:
- Compare `BoundaryClassifyKernel` output to `BoundaryClassifier.Classify()` — exact match for boundary types, ε=1e-6 for relative speed
- Compare `CollisionScatterKernel` output to CPU scatter loop — ε=0.01 for heights
- Compare async tick output to synchronous tick output — full state comparison

### 10.2 Performance Tests

Add `Stopwatch`-based benchmarks to `TectonicEngine` for each sub-phase:
- Measure advection, collision, boundary classification, dynamics, volcanism separately
- Log sub-phase timings alongside total tectonic time in `SimulationTickStats`
- Use existing `TICK_STATS` event log entries for regression tracking

### 10.3 UI Integration Tests

- Verify that the frontend receives `SimulationProgress` events during the tick, not only after
- Verify that tectonic sub-phase status indicators update in the agent panel
- Verify that incremental height-map updates render correctly mid-tick

---

## 11. Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| GPU atomic operations differ across CUDA/OpenCL/CPU backends | Test on all three ILGPU backends; use CPU fallback when atomics fail |
| Async pipeline adds overhead from `Task`/`await` scheduling | Measure overhead; batch small sub-phases if < 1 ms each |
| StratigraphyStack sharded locks may cause deadlocks | Ensure operations only acquire one stripe lock at a time; no nested locking |
| Feature detection throttling causes stale labels | Run detection on first tick, on planet generation, and on user request |
| Incremental state pushes increase SignalR bandwidth | Only push height map (1 MB) mid-tick, not full bundle (3 MB); make optional |

---

## 12. Dependencies

- **ILGPU 1.5.3** with `EnableAlgorithms()` — already in use for `XMath` and `Atomic.Add(double)`
- **`Atomic.Max(int)` / `Atomic.CompareExchange`** — available in ILGPU core
- No new NuGet packages required
- No frontend package changes required

---

## 13. Out of Scope

- Rewriting the equirectangular grid to an icosahedral/HEALPix grid (would help performance but is a much larger change)
- WebGPU frontend compute shaders (the GPU work described here is backend-only via ILGPU)
- Multi-step simulation advances with intermediate renders (the async pipeline handles single-step advances with sub-phase yields)
