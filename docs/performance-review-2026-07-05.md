# GeoTime Performance Review — Session 2026-07-05

**Log file:** `backend/GeoTime.Api/logs/geotime-session-20260705-110556-pid37200.jsonl`  
**Analysis script:** `backend/GeoTime.Api/logs/_analyze_perf.py`  
**Session:** ~7.3 hours · RTX 2070 (CUDA) · 512×512 grid · adaptive resolution ON · 16 CPU cores · .NET 10.0.6  
**Date of review:** 2026-07-05

---

## Executive Summary — Top 3 Bottlenecks

| Rank | Bottleneck | Evidence | Impact |
|------|-----------|----------|--------|
| **1** | **Tectonic plate advection** | `tectonicAdvectionMs` p95 **2,952 ms**; **93%** of tectonic time, **~85%** of `totalMs` | Dominates every executed tick (~2.3 s mean) |
| **2** | **Advance polling / skip storm** | **90.9%** of advance requests skipped (46,636 / 51,299); frontend polls every **200 ms** while ticks take **~2.4 s** | ~137 GB wasted bundle transfer; ~11× redundant HTTP load |
| **3** | **Feature detection (every 5 ticks)** | +**654 ms** mean tick cost when `featureDetectionRan=true` (2,871 vs 2,217 ms); p95 **1,127 ms** | ~20% of executed ticks; predictable periodic spikes |

**Where to optimize:** Backend compute (tectonic advection), then frontend/API coordination (skipped advances). Transport and rendering are not the steady-state limiter when simulation is running.

---

## Session Overview

| Metric | Value |
|--------|-------|
| Session duration | ~26,336 s (7.32 hr) |
| `simulation_advance` events | 51,299 (1.95 req/s) |
| Executed ticks | 4,663 (0.177 tick/s) |
| Max `tickCount` | 10,277 |
| Skipped advances | 46,636 (**90.9%**) |
| GPU mode | CUDA, `isGpuActive=true` on 99.9% of ticks |
| Active layers | 0 on 99.8% of cycles (`activeLayerCount=0`) |
| FPS | mean 59.6, p95 60 |

**One-time costs:** `planet_generate` **1,667 ms** (11 plates, 262,144 cells).

---

## Evidence Tables

### Engine time breakdown (executed ticks only, n≈4,621)

| Phase | Mean (ms) | Median | **p95** | Max | Share of `totalMs` |
|-------|-----------|--------|---------|-----|-------------------|
| **totalMs** | 2,367 | 2,327 | **3,448** | 13,400 | 100% |
| **tectonicMs** | 2,148 | 2,155 | **3,134** | 13,391 | **91.6%** |
| ↳ tectonicAdvectionMs | 1,999 | 1,980 | **2,952** | 3,584 | **85.3%** |
| ↳ tectonicCollisionMs | 133 | 139 | 246 | 1,096 | 5.7% |
| ↳ tectonicVolcanismMs | 8 | 5 | 26 | 174 | 0.4% |
| ↳ tectonicBoundaryMs | 5 | 0 | 18 | **10,849** | 0.2% |
| featureDetectionMs | 122* | 0 | 830* | 2,101 | 5.2% |
| eventDepositionMs | 32 | 0 | 202 | 1,582 | 1.3% |
| surfaceMs | 13 | 0 | 0 | 1,124 | 0.6% |
| atmosphereMs | 0 | 0 | 0 | 0 | 0.0% |
| vegetationMs | 0 | 0 | 0 | 0 | 0.0% |
| adaptive down/up | 4.4 / 2.6 | — | 7 / 6 | — | 0.3% |

\*Feature detection mean is diluted across all ticks; when it runs (~920 executed ticks), mean **503 ms**, p95 **1,127 ms**.

### Client vs server split (matched executed cycles, n≈4,682)

| Metric | Mean | Median | p95 |
|--------|------|--------|-----|
| Backend `stats.totalMs` | 2,347 ms | 2,325 ms | 3,441 ms |
| Backend `wallMs` | 2,343 ms | 2,323 ms | 3,441 ms |
| Client `advanceWallMs` | 2,338 ms | 2,324 ms | 3,444 ms |
| Client `bundleWallMs` | 14.6 ms | 11 ms | 28 ms |
| Client `overlayWallMs` | 0.1 ms | 0 ms | 0 ms |
| HTTP overhead (`advanceWallMs − totalMs`) | −8.9 ms | +4 ms | +11 ms |

On **executed** ticks, client time ≈ server compute. Bundle + overlay add ~15 ms (~0.6%) vs ~2.3 s advance.

On **skipped** ticks (65% of client cycles with `advanceWallMs < 50 ms`), the client still spends **~15 ms** fetching a 3 MB bundle for stale state.

### Slowest endpoints (by volume × cost)

| Endpoint | Count | Mean wall | p95 wall | Notes |
|----------|-------|-----------|----------|-------|
| `/api/simulation/advance` | 51,299 | 491 ms* | 2,814 ms | *Skewed by 91% instant skips |
| `/api/state/bundle/binary` | 51,299 | 1.9 ms | 3.0 ms | Fixed **3,145,728 B** (3 MB) |
| `/api/state/inspect/{cell}` | 24,527 | 38.4 ms | — | **941 s** total server wall (logged successes only) |
| `/api/state/events` | 24,734 | 0.1 ms | — | Event log polling |
| `/api/diagnostics/client-event` | 51,277 | 0.1 ms | — | Perf telemetry |

**Bundle session total:** ~**150 GB** transferred; ~**137 GB (91%)** on skipped advance cycles.

---

## Findings

### 1. Tectonic advection is the compute ceiling

- `tectonicAdvectionMs` accounts for **93% of tectonic** and **~85% of total** tick time.
- GPU is active (`isGpuActive=true` on 51,232/51,274 logged ticks), but advection still costs ~2 s/tick at 512².
- Aligns with `docs/plan-split.md`: GPU destination map + scatter/collision in `TectonicEngine.AdvectPlates()` remains the hotspot.
- **Outlier tick 9212:** `totalMs=13,400`, `tectonicBoundaryMs` spike to **10,849 ms** — boundary sub-phase, not advection.

### 2. Polling mismatch causes massive skip waste

- Frontend polls every **200 ms** (`SIM_UPDATE_INTERVAL` in `src/main.ts:1029`).
- Backend holds `_advanceLock` for ~2.4 s per tick (`SimulationOrchestrator.cs`).
- **91% of advance HTTP calls are no-ops**, but each still returns 200 OK with stale stats, fetches a **3 MB bundle**, and logs `client_advance_cycle`.
- API response omits `skipped` — `AdvanceResult` in `src/api/backend-client.ts` has no `skipped` field.

### 3. Feature detection adds ~500 ms every 5 ticks

- Runs every 5 ticks (`FeatureDetectionInterval = 5` in `SimulationOrchestrator.cs:94`).
- Executed ticks with detection: mean **2,871 ms** vs **2,217 ms** without (+**654 ms**).

### 4. Adaptive resolution is already cheap

- Downsample/upsample: **<7 ms** combined.
- `atmosphereMs` and `vegetationMs` report **0 ms** on the 128×128 coarse path.
- Toggling adaptive resolution is unlikely to move the needle on this session.

### 5. Transport and rendering are not steady-state bottlenecks

- Bundle server time: **1.9 ms** mean; client decode: **~15 ms**.
- Overlay refresh: **0.1 ms** mean (no active layers 99.8% of session).
- FPS held at **60** throughout.

### 6. Cell inspect panel adds overhead when pinned

- `refreshInspectCell()` runs **every tick** after bundle fetch (`src/main.ts:1386`).
- Two pinned cells drove **23,362+ inspect calls** at ~35–42 ms each (successful requests only).
- See [Backend errors and log impact](#backend-errors-and-log-impact) for concurrency failures not captured in the log.

---

## Prioritized Recommendations

### P0 — High impact, low risk

| # | Change | Expected impact | Files |
|---|--------|-----------------|-------|
| **P0-1** | Expose `skipped` in advance response; skip bundle fetch when skipped | Eliminate ~137 GB/session bundle waste | `Program.cs`, `backend-client.ts`, `main.ts` |
| **P0-2** | Adaptive poll interval based on `lastTickStats.totalMs` | Stops skip storm at source | `main.ts` |
| **P0-3** | Fix `InspectCell` race (snapshot `FeatureRegistry` before enumerate) | Stops console spam; removes unlogged failed inspect overhead | `SimulationOrchestrator.cs:556–574` |

### P1 — High impact, moderate effort

| # | Change | Expected impact | Files |
|---|--------|-----------------|-------|
| **P1-1** | Profile/optimize advection Phase 2 (scatter/collision) | 15–40% tick reduction if partially on CPU | `TectonicEngine.cs`, `GpuComputeService.cs` |
| **P1-2** | Increase feature detection interval 5 → 10 or 15 | ~100 ms/tick amortized savings | `SimulationOrchestrator.cs:94` |
| **P1-3** | Debounce/throttle `refreshInspectCell` | Cuts inspect API volume | `main.ts` |

### P2 — After P0/P1

| # | Change | Expected impact | Files |
|---|--------|-----------------|-------|
| **P2-1** | Inline bundle in advance response on non-skipped ticks | Remove second HTTP round-trip (~15 ms/tick) | `Program.cs`, `backend-client.ts`, `main.ts` |
| **P2-2** | Investigate boundary spike (tick 9212) | Prevent rare 13 s freezes | `TectonicEngine.cs` |
| **P2-3** | Delta/dirty-mask bundle | Reduce 3 MB fixed payload | `Program.cs`, `main.ts` |

---

## Proposed Experiments

Run each for **5 minutes** with layers off; log via `GET /api/diagnostics/session`.

| Experiment | Config | Compare |
|------------|--------|---------|
| A. Skip-aware client | P0-1 patch | Bundle count ≈ executed ticks only |
| B. Adaptive polling | P0-2 patch | `skipped` rate → near 0% |
| C. Feature interval | `FeatureDetectionInterval=15` | Mean `totalMs` on non-FD ticks |
| D. Adaptive resolution off | API toggle | Confirm atm/veg stay negligible |
| E. GPU off | Force CPU fallback | Quantify GPU speedup |
| F. Layers on | Toggle weather + wind | Measure `overlayWallMs` |
| G. Inspect pinned + fix | P0-3 + throttle | Inspect errors → 0; lower call volume |

---

## Implementation Plan (priority order)

1. `SimulationOrchestrator.InspectCell` — snapshot feature registry; optional read lock during advance.
2. `Program.cs` + `backend-client.ts` + `main.ts` — `skipped` in API; guard bundle fetch.
3. `main.ts` — completion-driven scheduling instead of fixed 200 ms interval.
4. `TectonicEngine.cs` / `GpuComputeService.cs` — advection sub-phase instrumentation.
5. `SimulationOrchestrator.cs` — tune `FeatureDetectionInterval`.
6. `main.ts` — throttle `refreshInspectCell`.

---

## Backend Errors and Log Impact

During the session that produced this log, the backend console reported two categories of messages. Below is whether each affects the JSONL performance data.

### Ollama startup probes — **no impact on simulation metrics**

```
GET http://localhost:11434/api/tags  (2054 ms, then 2 ms)
```

- Two HTTP calls at startup checking Ollama availability for LLM providers.
- Not logged as `api_request` unless they hit a `/api/*` route (they do not).
- Do not affect `simulation_advance`, `client_advance_cycle`, or engine timings.
- Only relevant if analyzing `/api/llm/providers` (2 calls, mean 1,044 ms in this session).

### `InspectCell` — `Collection was modified` — **partial impact**

```
System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
  at SimulationOrchestrator.InspectCell(...) line 556 (or 564)
  at Program.cs line 321  (/api/state/inspect/{cellIndex})
```

**Root cause:** Race between simulation advance and cell inspect.

- During `AdvanceSimulationCore`, `FeatureDetectorService.Detect()` rebuilds `State.FeatureRegistry.Features` (a `Dictionary`) every 5 ticks.
- Concurrently, `refreshInspectCell()` in `src/main.ts` calls `/api/state/inspect/{cell}` **every tick** when a cell is pinned.
- `InspectCell` enumerates `State.FeatureRegistry.Features.Values` without synchronization:

```556:559:backend/GeoTime.Core/SimulationOrchestrator.cs
        var featureIds = State.FeatureRegistry.Features.Values
            .Where(f => f.CellIndices.Contains(cellIndex))
            .Select(f => f.Id)
            .ToList();
```

**What the log shows:**

| Event type | Affected? | Details |
|------------|-----------|---------|
| `simulation_advance` | **No** | Separate code path; lock on `_advanceLock` does not cover inspect |
| `client_advance_cycle` | **No** | Advance/bundle timings unaffected |
| Engine phase stats (`tectonicMs`, etc.) | **No** | Recorded inside advance only |
| `api_request` for `/api/state/inspect/*` | **Yes — under-reported** | Log analysis: **24,527 inspect requests, all `statusCode: 200`**, zero 5xx |
| Inspect wall-time totals | **Yes — under-reported** | Logged successes only (~941 s server wall); failed requests missing |

**Why failures are missing from the log:**

`PerformanceLoggingMiddleware` wraps requests with `await next(context)`. When `InspectCell` throws, the exception propagates **before** the middleware writes `api_request`. Failed inspect calls therefore do **not** appear in the JSONL (or appear only if a retry succeeds on a subsequent tick).

**Practical impact on this review:**

- **Core bottleneck conclusions are valid.** Tectonic advection, skip storm, and feature detection findings are unaffected.
- **Inspect overhead is a lower bound.** Console errors indicate additional CPU spent on exception handling and developer exception pages, not counted in `api_request` wall times.
- **Inspect mean wall time (38 ms) reflects successful calls only.** Failed concurrent attempts add unmeasured load during feature-detection ticks.
- **Recommendation P0-3** (fix race + throttle inspect refresh) is validated by these errors.

---

## Open Questions

1. **Advection sub-phase breakdown** — Need instrumentation inside `AdvectPlates()` (GPU dest vs scatter vs gap-fill).
2. **42 ticks with `isGpuActive=false`** — Transient GPU fallback cause unknown.
3. **Boundary spike (tick 9212)** — Sub-tick count not logged.
4. **Layer overlay cost** — Session ran with 0 active layers 99.8% of the time.
5. **No `session_end` event** — Duration computed from first/last event timestamps.

---

## Analysis Commands

```bash
python backend/GeoTime.Api/logs/_analyze_perf.py
```

Manual spot checks:

```bash
# Inspect status codes in log
rg '"path":"/api/state/inspect' backend/GeoTime.Api/logs/geotime-session-20260705-110556-pid37200.jsonl | rg statusCode

# Slowest simulation advances (executed)
rg '"event":"simulation_advance"' backend/GeoTime.Api/logs/*.jsonl | rg '"skipped":false'
```

---

## Bottom Line

Optimize **backend tectonic advection** first (p95 **2,952 ms**). Second win: stop **200 ms polling** against **~2.4 s** ticks (91% skips, ~137 GB redundant bundles). The `InspectCell` race does **not** invalidate simulation timing data but **does** mean inspect API metrics under-count total inspect load; fix the race before using inspect wall times for optimization decisions.
