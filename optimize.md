# GeoTime Optimization Plan

This document surveys computational bottlenecks in GeoTime and outlines a prioritized set of optimization strategies an implementation agent can work through. GPU matrix math is given particular attention.

---

## Status: Recommendations 1–4 Implemented ✅

The four highest-priority recommendations from Section 8 have been implemented:

| Priority | Strategy | Status |
|---|---|---|
| 1 | D – MessagePack/binary state bundle | ✅ Done — `/api/state/bundle/binary` endpoint + frontend |
| 2 | A.1 – `Parallel.For` per-cell loops | ✅ Done — ClimateEngine, VegetationEngine, WeatherEngine, TectonicEngine (isostasy) |
| 3 | A.2 – SIMD `Vector<float>` isostasy | ✅ Done — TectonicEngine.ApplyIsostasy |
| 4 | E – Dirty mask incremental updates | ✅ Done — DirtyMask on SimulationState; used in VegetationEngine + BiomatterEngine |

### Bug fixes (also addressed)

- **First-person view height**: Camera is now positioned exactly 6 feet (1.8288 m) above the terrain elevation at the current lat/lon.  Ocean cells use sea level (0 m) as the base.  The CPU-side height map is sampled from `_fpHeightMap` which is kept in sync with the GPU texture by `updateHeightMap()`.
- **Wind animation invisible**: The trail-fade compositing was changed from `source-over` (black fill that made the canvas opaque) to `destination-out` (reduces existing pixel alpha), so the WebGL terrain always shows through the wind canvas.

---

## 1. Current Bottlenecks

### 1.1 Tectonic Engine (most expensive)
- `BoundaryClassifier.Classify` iterates every cell to find plate boundaries — O(N) each tick.
- `ApplyIsostasy` iterates all 262 144 cells (512×512) every sub-tick, writing back `HeightMap`.
- The tectonic engine runs multiple sub-ticks per `AdvanceSimulation` call (`minTickInterval = 0.1` Ma).

### 1.2 Surface Engine
- `ErosionEngine` D∞ flow routing: multiple passes over all cells, pointer-chasing through neighbor indices.
- `WeatheringEngine` per-cell loops with conditional branching on rock type.
- `GlacialEngine` iterates cells for ice mass balance each sub-tick.

### 1.3 Atmosphere / Vegetation / Biomatter Engines
- Each engine runs per-cell loops (O(N)) on the 262 K cell grid.
- All three run in parallel via `Task.WhenAll`, but each is still single-threaded internally.

### 1.4 Planet Generator
- `PlanetGenerator.GenerateHeightMap` evaluates fractional Brownian motion (fBm) Simplex noise at every cell — CPU-bound, embarrassingly parallel.

### 1.5 Serialization / Network
- REST endpoints serialize full float arrays (`float[262144]`) as JSON on every advance cycle.
- Each cycle fetches height-map, temperature-map, and precipitation-map separately.

---

## 2. Strategy A — SIMD / Parallelism on CPU

**Effort:** Low–Medium  
**Expected speedup:** 2–8×

### 2.1 `Parallel.For` for per-cell loops
Replace the inner `for (var i = 0; i < cellCount; i++)` loops in `ApplyIsostasy`, `WeatheringEngine`, `AtmosphereEngine`, and `VegetationEngine` with `Parallel.For` (or `Parallel.ForEach` over batched ranges). These loops are almost entirely data-parallel — each cell reads a small neighbourhood and writes back a single field.

```csharp
// Before
for (var i = 0; i < cc; i++) { /* ... */ }

// After
Parallel.For(0, cc, i => { /* thread-safe per-cell work */ });
```

**Caution:** Boundary cells interact with neighbours. Split the loop into two phases: read-phase into a temporary buffer, write-phase from the buffer, so threads never write to locations read by other threads in the same pass.

### 2.2 `System.Numerics.Vector<float>` SIMD
The isostasy inner loop
```csharp
state.HeightMap[i] += (float)((eq - state.HeightMap[i]) * relax);
```
can be vectorized with `Vector<float>` (AVX2/SSE on x64 = 8 floats per instruction):

```csharp
var vRelax = new Vector<float>((float)relax);
var vOffset = new Vector<float>(-4500f);
// Process 8 cells per loop iteration
for (int i = 0; i <= cc - Vector<float>.Count; i += Vector<float>.Count) {
    var vCrust = new Vector<float>(state.CrustThicknessMap, i);
    var vEq   = vCrust * new Vector<float>(1000f * (1 - ISOSTATIC_RATIO_F)) + vOffset;
    var vH    = new Vector<float>(state.HeightMap, i);
    var vNew  = vH + (vEq - vH) * vRelax;
    vNew.CopyTo(state.HeightMap, i);
}
```

Estimated gain: 6–8× for isostasy alone.

### 2.3 Boundary Classifier caching
`BoundaryClassifier.Classify` rebuilds the boundary list every tick. Cache the result and only rebuild when a plate moves more than a threshold distance (e.g., 0.5° per Ma).

---

## 3. Strategy B — GPU Compute (HLSL/WGSL via compute shaders)

**Effort:** High  
**Expected speedup:** 20–200×

This is the highest-payoff direction for the tectonic and surface engines. The globe grid is a perfect GPU workload: rectangular, large, and embarrassingly parallel.

### 3.1 Technology choices

| Option | Language | Runtime | Notes |
|---|---|---|---|
| **WGPU / Dawn** | WGSL | WebGPU (browser or native) | Future-proof; available in modern browsers |
| **CUDA** | CUDA C | NVIDIA only | Highest performance but locks platform |
| **OpenCL** | OpenCL C | Cross-platform | Mature but verbose |
| **Metal Compute** | MSL | macOS/iOS | macOS only |
| **Vulkan Compute** | GLSL/SPIR-V | Cross-platform | Powerful, complex |
| **DirectCompute** | HLSL | Windows | Good with .NET interop via SharpDX |
| **ShaderToy-style WebGL** | GLSL | Browser | Easiest to integrate with existing Three.js frontend |

**Recommended path for GeoTime:** WebGPU (WGSL) for the frontend compute, or a .NET GPU library for backend.

### 3.2 .NET GPU libraries

| Library | Compute model | Status |
|---|---|---|
| **ILGPU** | C# LINQ-to-GPU, JIT-compiles .NET to PTX/OpenCL/Vulkan | Active, MIT |
| **ComputeSharp** | C# → HLSL, DirectX 12, GPU-accelerated | Active, maintained by Microsoft |
| **Hybridizer** | .NET → CUDA | Commercial |
| **TorchSharp** | PyTorch bindings | Good for tensor math |

**ILGPU example** for isostasy:
```csharp
static void IsostasyKernel(
    Index1D idx,
    ArrayView<float> heightMap,
    ArrayView<float> crustMap,
    float relax,
    float isoRatio)
{
    float eq = crustMap[idx] * 1000f * (1 - isoRatio) - 4500f;
    heightMap[idx] += (eq - heightMap[idx]) * relax;
}

// dispatch
using var context = Context.CreateDefault();
using var accel = context.GetPreferredDevice(preferCuda: true)
                         .CreateAccelerator(context);
var kernel = accel.LoadAutoGroupedStreamKernel<...>(IsostasyKernel);
kernel(cc, heightMapView, crustMapView, (float)relax, (float)ISOSTATIC_RATIO);
accel.Synchronize();
```

This offloads 262 144 iterations to GPU thread groups in < 1 ms vs. ~15 ms on CPU.

### 3.3 Matrix/tensor operations for climate

The atmosphere engine (`AtmosphereEngine`, `WeatherEngine`) applies 2D convolution-like operations across the grid (wind advection, diffusion). These map naturally to matrix multiplications:

- **Wind advection** = sparse matrix × state vector (CSR or banded)
- **Temperature diffusion** = Laplacian matrix × temperature vector (tridiagonal, separable)

Using a BLAS/LAPACK library or GPU GEMM:
```
temperature_new = temperature_old + dt * (D * temperature_old + sources)
```
where `D` is the discrete Laplacian (512×512 banded matrix). On GPU via cuBLAS: ~0.1 ms per step.

**TorchSharp** provides a clean API:
```csharp
using var t = torch.tensor(state.TemperatureMap).view(512, 512);
using var laplacian = compute_laplacian(t);      // once-built sparse tensor
using var result = t + dt * torch.mm(laplacian_dense, t.flatten()).view(512, 512);
```

### 3.4 Planet generation on GPU

The `PlanetGenerator.GenerateHeightMap` fBm noise loop is embarrassingly parallel. Replace with a GPU-side noise evaluation:

- Use a pre-computed permutation table uploaded to GPU.
- Each thread computes the Simplex fBm for one cell.
- Expected speedup: 100× (from ~500 ms to ~5 ms for 512×512 at 4 octaves).

On the frontend (Three.js), this is already partially done: the height-displacement vertex shader runs on GPU.

---

## 4. Strategy C — MessagePack Binary Transport

**Effort:** Low  
**Expected speedup:** 2–3× on data transfer

The binary endpoints (`/api/state/heightmap/binary`) already exist but the main render loop still uses the JSON endpoints. Switching to binary + a single combined "state bundle" endpoint would reduce transfer size and parsing time.

```csharp
// Add combined binary endpoint
app.MapGet("/api/state/bundle/binary", (SimulationOrchestrator sim) => {
    var bundle = new StateBundleDto(
        sim.State.HeightMap,
        sim.State.TemperatureMap,
        sim.State.PrecipitationMap);
    return Results.Bytes(MessagePackSerializer.Serialize(bundle), "application/x-msgpack");
});
```

Frontend reads a single `ArrayBuffer` and copies three `Float32Array` views into it — one fetch instead of three.

---

## 5. Strategy D — Adaptive Resolution

**Effort:** Medium  
**Expected speedup:** 4–16× depending on zoom

When the camera is at orbital distance, full 512×512 resolution is overkill for tectonic simulation. Use a coarse 128×128 grid for far-out views and a full-resolution patch around the camera focus for close views.

- Maintain two resolution levels in `SimulationState`.
- The tectonic engine runs at full resolution always (geological accuracy requires it).
- The atmosphere/vegetation engines can run at 128×128 and upsample for display.

---

## 6. Strategy E — Incremental / Event-driven Updates

**Effort:** Medium  
**Expected speedup:** 3–10× at late simulation times

At 4 Ga the planet has stabilized: plates move slowly, temperatures converge. Most cells change by < 0.1% per tick. Track a "dirty mask" (bitmask of changed cells) and skip unchanged cells in:

- `BiomatterEngine` (most stable at old ages)
- `VegetationEngine`
- `WeatherEngine` precipitation accumulation

```csharp
var dirty = new BitArray(cellCount);
for (int i = 0; i < cellCount; i++) {
    if (Math.Abs(state.HeightMap[i] - prevHeight[i]) > 0.5f)
        dirty.Set(i, true);
}
// Only process dirty cells in subsequent engines
```

This directly addresses the "stuck at 4.26 Ga" symptom — if the simulation rarely produces changes, ticks can be batched or skipped.

---

## 7. Strategy F — SignalR Streaming Replace REST Poll

**Effort:** Low  
**Expected speedup:** Subjective (latency improvement)

Replace the `POST /api/simulation/advance` REST pattern with a pure SignalR streaming approach:
1. Client sends `StartSimulation(deltaMa, steps)` via SignalR.
2. Backend runs the full advance, streaming `SimulationProgress` events per phase.
3. On completion, backend pushes compressed state delta via SignalR binary channel.
4. Client applies the delta to local buffers.

Eliminates HTTP round-trip overhead and allows the backend to push state only when it actually changes.

---

## 8. Recommended Implementation Order

| Priority | Strategy | Effort | Gain |
|---|---|---|---|
| 1 | D (MessagePack bundle) | Low | 2–3× I/O |
| 2 | A.1 (Parallel.For) | Low | 2–4× CPU |
| 3 | A.2 (SIMD isostasy) | Low | 6–8× isostasy |
| 4 | E (dirty mask) | Medium | 3–10× late-stage |
| 5 | B.1 (ILGPU tectonic) | High | 20–50× |
| 6 | B.3 (GPU climate) | High | 50–200× |
| 7 | D (adaptive resolution) | Medium | 4–16× |
| 8 | F (SignalR streaming) | Low | latency |

---

## 9. Measurement Plan

Before any optimization:
1. Add `Stopwatch`-based timing around each engine `Tick()` call in `SimulationOrchestrator.AdvanceSimulation`.
2. Log timing to a structured log (or expose via `/api/simulation/perf`).
3. Baseline at 1 Ma, 1 Ga, 4 Ga.

After each optimization, re-measure and compare. Aim for the tectonic tick to run < 100 ms at 4 Ga.

---

## 10. Notes on GPU Suitability

| Engine | GPU suitable? | Reason |
|---|---|---|
| TectonicEngine (isostasy) | ✅ Yes | Pure per-cell arithmetic |
| TectonicEngine (boundary detection) | ⚠️ Partial | Needs prefix-sum for boundary list |
| SurfaceEngine (erosion D∞) | ✅ Yes | Single-pass per-cell after sorting |
| AtmosphereEngine | ✅ Yes | Advection = sparse matrix–vector |
| WeatherEngine | ✅ Yes | Per-cell, stateless |
| VegetationEngine | ✅ Yes | Per-cell NPP formula |
| BiomatterEngine | ⚠️ Partial | Reef/petroleum need conditional |
| PlanetGenerator | ✅ Yes | Noise is embarrassingly parallel |
| CrossSectionEngine | ❌ No | Sequential path traversal |
