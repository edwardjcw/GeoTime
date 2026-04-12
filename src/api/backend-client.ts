// ─── Backend API Client ─────────────────────────────────────────────────────
// Communicates with the C# .NET backend for all simulation calculations.
// The frontend only handles display (Three.js rendering, UI) and sends
// commands to the backend.
// Supports REST, binary (MessagePack) endpoints, and WebSocket streaming.

const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://localhost:5000';

export interface GenerateResult {
  seed: number;
  plateCount: number;
  hotspotCount: number;
  timeMa: number;
}

export interface TickStats {
  tectonicMs: number;
  surfaceMs: number;
  atmosphereMs: number;
  vegetationMs: number;
  biomatterMs: number;
  totalMs: number;
  // Tectonic sub-phase timing
  tectonicAdvectionMs?: number;
  tectonicCollisionMs?: number;
  tectonicBoundaryMs?: number;
  tectonicDynamicsMs?: number;
  tectonicVolcanismMs?: number;
  tectonicTotalMs?: number;
  /** True when at least one GPU kernel ran this tick. */
  isGpuActive?: boolean;
}

export interface AdvanceResult {
  timeMa: number;
  tickCount?: number;
  stats?: TickStats;
}

export interface SimulationTimeResult {
  timeMa: number;
  seed: number;
}

export interface CellInspection {
  cellIndex: number;
  height: number;
  crustThickness: number;
  rockType: number;
  rockAge: number;
  plateId: number;
  soilType: number;
  soilDepth: number;
  temperature: number;
  precipitation: number;
  biomass: number;
  biomatterDensity: number;
  organicCarbon: number;
  reefPresent: boolean;
}

export interface CrossSectionProfile {
  samples: Array<{
    distanceKm: number;
    surfaceElevation: number;
    crustThicknessKm: number;
    soilType: number;
    soilDepthM: number;
    layers: Array<{
      rockType: number;
      ageDeposited: number;
      thickness: number;
      dipAngle: number;
      dipDirection: number;
      deformation: number;
      unconformity: boolean;
      soilHorizon: number;
      formationName: number;
    }>;
  }>;
  totalDistanceKm: number;
  pathPoints: Array<{ lat: number; lon: number }>;
  deepEarthZones: Array<{
    name: string;
    topKm: number;
    bottomKm: number;
    rockType: number;
  }>;
}

export interface GeoLogEntry {
  timeMa: number;
  type: string;
  description: string;
  location?: { lat: number; lon: number };
}

export interface SnapshotInfo {
  count: number;
  times: number[];
}

export interface SimulationTickEvent {
  timeMa: number;
  step: number;
  totalSteps: number;
}

/** A compact label descriptor returned by /api/state/features/labels. */
export interface FeatureLabel {
  id: string;
  name: string;
  /** Feature type enum name, e.g. "Continent", "Ocean", "MountainRange". */
  type: string;
  centerLat: number;
  centerLon: number;
  /** Maximum camera distance (globe radius = 1) at which this label is visible. */
  zoomLevel: number;
  status: string;
  /** Historical names this feature (or its ancestors) was formerly known by. */
  formerNames: string[];
}

async function post<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
    signal,
  });
  if (!res.ok) throw new Error(`API error ${res.status}: ${await res.text()}`);
  return res.json();
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`);
  if (!res.ok) throw new Error(`API error ${res.status}: ${await res.text()}`);
  return res.json();
}

async function getBinary(path: string): Promise<ArrayBuffer> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { Accept: 'application/x-msgpack' },
  });
  if (!res.ok) throw new Error(`API error ${res.status}: ${await res.text()}`);
  return res.arrayBuffer();
}

// ── Public API (REST) ───────────────────────────────────────────────────────

export interface PlanetStatus {
  exists: boolean;
  seed: number;
  timeMa: number;
}

export async function getPlanetStatus(): Promise<PlanetStatus> {
  return get('/api/planet/status');
}

export async function generatePlanet(seed: number = 0): Promise<GenerateResult> {
  return post('/api/planet/generate', { seed });
}

export async function advanceSimulation(deltaMa: number, signal?: AbortSignal): Promise<AdvanceResult> {
  return post('/api/simulation/advance', { deltaMa }, signal);
}

export async function getSimulationStats(): Promise<TickStats & { timeMa: number }> {
  return get('/api/simulation/stats');
}

export async function getSimulationTime(): Promise<SimulationTimeResult> {
  return get('/api/simulation/time');
}

/** Result from /api/simulation/compute-info – describes the active compute backend. */
export interface ComputeInfoResult {
  mode: 'CPU' | 'GPU';
  deviceName: string;
  acceleratorType: string;
  isGpu: boolean;
  /** On-device memory in megabytes; 0 when not available. A dedicated GPU will report much more memory than integrated graphics. */
  memoryMb: number;
}

/** Fetch the active compute backend info (GPU/CPU) from the server. */
export async function getComputeInfo(): Promise<ComputeInfoResult> {
  return get('/api/simulation/compute-info');
}

export async function getHeightMap(): Promise<number[]> {
  return get('/api/state/heightmap');
}

export async function getPlateMap(): Promise<number[]> {
  return get('/api/state/platemap');
}

export async function getTemperatureMap(): Promise<number[]> {
  return get('/api/state/temperaturemap');
}

export async function getPrecipitationMap(): Promise<number[]> {
  return get('/api/state/precipitationmap');
}

export async function getBiomassMap(): Promise<number[]> {
  return get('/api/state/biomassmap');
}

export async function getBiomatterMap(): Promise<number[]> {
  return get('/api/state/biomattermap');
}

export async function getOrganicCarbonMap(): Promise<number[]> {
  return get('/api/state/organiccarbonmap');
}

export async function getSoilMap(): Promise<number[]> {
  return get('/api/state/soilmap');
}

/** Fetch compact label descriptors for all active geographic features. */
export async function fetchFeatureLabels(): Promise<FeatureLabel[]> {
  return get('/api/state/features/labels');
}

export async function getPlates(): Promise<unknown[]> {
  return get('/api/state/plates');
}

export async function getHotspots(): Promise<unknown[]> {
  return get('/api/state/hotspots');
}

export async function getAtmosphere(): Promise<unknown> {
  return get('/api/state/atmosphere');
}

export async function getEvents(count?: number): Promise<GeoLogEntry[]> {
  const params = count ? `?count=${count}` : '';
  return get(`/api/state/events${params}`);
}

export async function inspectCell(cellIndex: number): Promise<CellInspection> {
  return get(`/api/state/inspect/${cellIndex}`);
}

export async function getCrossSection(
  points: Array<{ lat: number; lon: number }>,
): Promise<CrossSectionProfile> {
  return post('/api/crosssection', { points });
}

// ── Binary (MessagePack) Endpoints ──────────────────────────────────────────

export async function getHeightMapBinary(): Promise<ArrayBuffer> {
  return getBinary('/api/state/heightmap/binary');
}

export async function getPlateMapBinary(): Promise<ArrayBuffer> {
  return getBinary('/api/state/platemap/binary');
}

export async function getTemperatureMapBinary(): Promise<ArrayBuffer> {
  return getBinary('/api/state/temperaturemap/binary');
}

export async function getPrecipitationMapBinary(): Promise<ArrayBuffer> {
  return getBinary('/api/state/precipitationmap/binary');
}

export async function getBiomassMapBinary(): Promise<ArrayBuffer> {
  return getBinary('/api/state/biomassmap/binary');
}

export async function getBiomatterMapBinary(): Promise<ArrayBuffer> {
  return getBinary('/api/state/biomattermap/binary');
}

export async function getOrganicCarbonMapBinary(): Promise<ArrayBuffer> {
  return getBinary('/api/state/organiccarbonmap/binary');
}

// ── State Bundle (height + temperature + precipitation in one round-trip) ────

/** Decoded state bundle returned by the /api/state/bundle/binary endpoint. */
export interface StateBundle {
  heightMap: Float32Array;
  temperatureMap: Float32Array;
  precipitationMap: Float32Array;
}

/**
 * Fetch height, temperature, and precipitation maps in a single HTTP request.
 *
 * The response is raw float32 bytes with layout:
 *   [heightMap bytes | temperatureMap bytes | precipitationMap bytes]
 * Each array occupies exactly cellCount * 4 bytes.
 *
 * @param cellCount - Total number of grid cells (gridSize × gridSize).
 */
export async function getStateBundle(cellCount: number): Promise<StateBundle> {
  const buf = await getBinary('/api/state/bundle/binary');
  const floatBytes = cellCount * Float32Array.BYTES_PER_ELEMENT;
  return {
    heightMap:       new Float32Array(buf, 0,            cellCount),
    temperatureMap:  new Float32Array(buf, floatBytes,   cellCount),
    precipitationMap: new Float32Array(buf, floatBytes * 2, cellCount),
  };
}

// ── Weather Pattern ──────────────────────────────────────────────────────────

export interface WeatherPatternResult {
  month: number;
  windU: number[];
  windV: number[];
  oceanCurrentU: number[];
  oceanCurrentV: number[];
  jetStreamIntensity: number[];
  frontIntensity: number[];
  /** 0=none 1=ITCZ 2=polar 3=subtropical 4=orographic */
  frontType: number[];
  cyclonePositions: Array<{ lat: number; lon: number; intensity: number; type: number }>;
}

export async function getWeatherPattern(month: number): Promise<WeatherPatternResult> {
  // Backend expects 1-based month (1 = January … 12 = December)
  return get(`/api/weather/monthly?month=${month + 1}`);
}

// ── Snapshot Management ─────────────────────────────────────────────────────

export async function takeSnapshot(): Promise<{ timeMa: number; snapshotCount: number }> {
  return post('/api/snapshots/take');
}

export async function listSnapshots(): Promise<SnapshotInfo> {
  return get('/api/snapshots');
}

export async function restoreSnapshot(
  targetTimeMa: number,
): Promise<{ restoredTimeMa: number; targetTimeMa: number }> {
  return post('/api/snapshots/restore', { targetTimeMa });
}

export async function getSnapshotDelta(
  fromTimeMa: number,
  toTimeMa: number,
): Promise<ArrayBuffer> {
  return getBinary(`/api/snapshots/delta?fromTimeMa=${fromTimeMa}&toTimeMa=${toTimeMa}`);
}

// ── WebSocket (SignalR) Client ───────────────────────────────────────────────

export type SimulationEventHandler = {
  onTick?: (event: SimulationTickEvent) => void;
  onAdvanceComplete?: (event: { timeMa: number }) => void;
  onPlanetGenerated?: (event: GenerateResult) => void;
  onHeightMapData?: (data: number[]) => void;
  onTemperatureMapData?: (data: number[]) => void;
  onPrecipitationMapData?: (data: number[]) => void;
  onBiomassMapData?: (data: number[]) => void;
  onPlateMapData?: (data: number[]) => void;
  /** Rec 8: raw float32 state bundle pushed after each advance step. */
  onStateBundleData?: (data: ArrayBuffer) => void;
  onConnected?: (event: { timeMa: number; seed: number; computeMode?: string; computeDevice?: string; computeMemoryMb?: number }) => void;
  onDisconnected?: () => void;
  /** Called when the backend reports a simulation engine phase (tectonic, surface, biomatter, complete). */
  onProgress?: (event: { phase: string; step?: number; totalSteps?: number; timeMa?: number }) => void;
  /** Phase L6: called when one or more feature labels changed after a tick. */
  onFeaturesUpdated?: (event: { tick: number; labels: FeatureLabel[] }) => void;
  /** S8: Incremental height-only state data pushed mid-tick for visual feedback. */
  onIncrementalStateData?: (event: { phase: string; heightMap: ArrayBuffer }) => void;
};

/**
 * Creates a WebSocket connection to the simulation hub for real-time streaming.
 * Uses the SignalR text protocol over WebSocket for broad compatibility.
 */
export function createSimulationSocket(handlers: SimulationEventHandler) {
  const WS_BASE = API_BASE.replace(/^http/, 'ws');
  const url = `${WS_BASE}/hubs/simulation`;
  let ws: WebSocket | null = null;
  let connected = false;

  function connect() {
    ws = new WebSocket(url);

    ws.onopen = () => {
      // Send SignalR handshake (JSON protocol)
      ws?.send(JSON.stringify({ protocol: 'json', version: 1 }) + '\x1e');
    };

    ws.onmessage = (event) => {
      const messages = (event.data as string).split('\x1e').filter(Boolean);
      for (const raw of messages) {
        try {
          const msg = JSON.parse(raw);
          if (msg.type === undefined && !connected) {
            // Handshake response
            connected = true;
            return;
          }
          if (msg.type === 1 && msg.target) {
            // Invocation message
            const args = msg.arguments ?? [];
            switch (msg.target) {
              case 'Connected':
                handlers.onConnected?.(args[0]);
                break;
              case 'SimulationTick':
                handlers.onTick?.(args[0]);
                break;
              case 'SimulationAdvanceComplete':
                handlers.onAdvanceComplete?.(args[0]);
                break;
              case 'SimulationProgress':
                handlers.onProgress?.(args[0]);
                break;
              case 'PlanetGenerated':
                handlers.onPlanetGenerated?.(args[0]);
                break;
              case 'HeightMapData':
                handlers.onHeightMapData?.(args[0]);
                break;
              case 'TemperatureMapData':
                handlers.onTemperatureMapData?.(args[0]);
                break;
              case 'PrecipitationMapData':
                handlers.onPrecipitationMapData?.(args[0]);
                break;
              case 'BiomassMapData':
                handlers.onBiomassMapData?.(args[0]);
                break;
              case 'PlateMapData':
                handlers.onPlateMapData?.(args[0]);
                break;
              case 'StateBundleData':
                // Rec 8: binary state bundle pushed after each advance step.
                // args[0] is a Uint8Array from SignalR; wrap in ArrayBuffer.
                if (args[0] instanceof Uint8Array) {
                  const u8 = args[0];
                  handlers.onStateBundleData?.(u8.buffer.slice(
                    u8.byteOffset, u8.byteOffset + u8.byteLength) as ArrayBuffer);
                } else if (args[0] instanceof ArrayBuffer) {
                  handlers.onStateBundleData?.(args[0]);
                }
                break;
              case 'FeaturesUpdated':
                // Phase L6: changed feature labels pushed after each tick.
                handlers.onFeaturesUpdated?.(args[0]);
                break;
              case 'IncrementalStateData':
                // S8: Incremental height-only state data pushed mid-tick.
                // args[0] is { phase: string, heightMap: base64/Uint8Array }
                if (args[0] && args[0].heightMap) {
                  let heightBuf: ArrayBuffer;
                  if (args[0].heightMap instanceof Uint8Array) {
                    const u8 = args[0].heightMap;
                    heightBuf = u8.buffer.slice(u8.byteOffset, u8.byteOffset + u8.byteLength);
                  } else if (args[0].heightMap instanceof ArrayBuffer) {
                    heightBuf = args[0].heightMap;
                  } else {
                    // SignalR JSON protocol sends byte[] as base64 string
                    const b64 = args[0].heightMap as string;
                    const raw = atob(b64);
                    const bytes = new Uint8Array(raw.length);
                    for (let j = 0; j < raw.length; j++) bytes[j] = raw.charCodeAt(j);
                    heightBuf = bytes.buffer as ArrayBuffer;
                  }
                  handlers.onIncrementalStateData?.({
                    phase: args[0].phase ?? '',
                    heightMap: heightBuf,
                  });
                }
                break;
            }
          }
        } catch {
          // Ignore malformed messages
        }
      }
    };

    ws.onclose = () => {
      connected = false;
      handlers.onDisconnected?.();
    };
  }

  function invoke(method: string, ...args: unknown[]) {
    if (!ws || !connected) return;
    ws.send(
      JSON.stringify({ type: 1, target: method, arguments: args }) + '\x1e',
    );
  }

  return {
    connect,
    disconnect: () => {
      ws?.close();
      ws = null;
      connected = false;
    },
    get isConnected() {
      return connected;
    },
    generatePlanet: (seed: number) => invoke('GeneratePlanet', seed),
    advanceSimulation: (deltaMa: number, steps = 1) =>
      invoke('AdvanceSimulation', deltaMa, steps),
    requestHeightMap: () => invoke('RequestHeightMap'),
    requestTemperatureMap: () => invoke('RequestTemperatureMap'),
    requestPrecipitationMap: () => invoke('RequestPrecipitationMap'),
    requestBiomassMap: () => invoke('RequestBiomassMap'),
    requestPlateMap: () => invoke('RequestPlateMap'),
    /** Rec 8: ask the hub to push a binary state bundle (height+temp+precip). */
    requestStateBundleBinary: () => invoke('RequestStateBundleBinary'),
  };
}

// ── LLM Provider API (Phase D3) ─────────────────────────────────────────────

/** A single LLM provider descriptor returned by GET /api/llm/providers. */
export interface LlmProviderInfo {
  name: string;
  displayName: string;
  isAvailable: boolean;
  needsSetup: boolean;
  activeModel: string | null;
  statusMessage: string;
  isActive: boolean;
}

/** The active provider summary returned by GET /api/llm/active. */
export interface LlmActiveInfo {
  provider: string;
  model: string | null;
  baseUrl: string | null;
  hasApiKey: boolean;
}

/** Config payload sent with PUT /api/llm/active. */
export interface LlmProviderSettings {
  apiKey?: string;
  model?: string;
  baseUrl?: string;
}

/** Fetch all registered LLM providers with their current availability status. */
export async function getLlmProviders(): Promise<LlmProviderInfo[]> {
  return get('/api/llm/providers');
}

/** Fetch the current active provider name and its settings. */
export async function getLlmActive(): Promise<LlmActiveInfo> {
  return get('/api/llm/active');
}

/** Change the active provider (and optionally update its config) at runtime. */
export async function setLlmActive(
  provider: string,
  settings?: LlmProviderSettings,
): Promise<{ provider: string }> {
  return post('/api/llm/active', { provider, settings: settings ?? null });
}

/** Trigger the setup flow for a local provider (Ollama or LlamaSharp). */
export async function startLlmSetup(provider: string): Promise<void> {
  await post(`/api/llm/setup/${provider}`);
}

/**
 * Open an EventSource streaming setup progress for a local provider.
 * Returns the EventSource so the caller can close it when done.
 */
export function openLlmSetupProgress(
  provider: string,
  onProgress: (event: LlmSetupProgressEvent) => void,
): EventSource {
  const url = `${API_BASE}/api/llm/setup/${provider}/progress`;
  const es  = new EventSource(url);
  es.onmessage = (e) => {
    try {
      const data = JSON.parse(e.data) as LlmSetupProgressEvent;
      onProgress(data);
      if (data.isComplete || data.isError) es.close();
    } catch {/* ignore malformed events */}
  };
  return es;
}

/** One progress event streamed from GET /api/llm/setup/{provider}/progress. */
export interface LlmSetupProgressEvent {
  step: string;
  percentTotal: number;
  detail: string;
  isComplete: boolean;
  isError: boolean;
  errorMessage: string | null;
}

// ── Description API (Phase D5) ──────────────────────────────────────────────

/** One stat row returned by POST /api/describe. */
export interface DescriptionStat {
  label: string;
  value: string;
}

/** One stratigraphic summary row returned by POST /api/describe. */
export interface StratigraphicSummaryRow {
  age: string;
  thickness: string;
  rockType: string;
  eventNote: string;
}

/** One history timeline entry returned by POST /api/describe. */
export interface HistoryTimelineEntry {
  simTick: number;
  event: string;
  name: string;
}

/** Full response from POST /api/describe. */
export interface DescriptionResponse {
  title: string;
  subtitle: string;
  paragraphs: string[];
  stats: DescriptionStat[];
  stratigraphicSummary: StratigraphicSummaryRow[];
  historyTimeline: HistoryTimelineEntry[];
  providerUsed: string;
}

/** Request a geological description for the given cell index. */
export async function describeCell(cellIndex: number): Promise<DescriptionResponse> {
  return post<DescriptionResponse>('/api/describe', { cellIndex });
}

/**
 * Open a streaming description via SSE. Returns an EventSource.
 * Each event has `{ token: string }`. A final event has `{ done: true }`.
 */
export function describeStream(
  cellIndex: number,
  onToken: (token: string) => void,
  onDone: () => void,
): () => void {
  let es: EventSource | null = null;
  // Use fetch+ReadableStream for POST SSE
  const controller = new AbortController();
  fetch(`${API_BASE}/api/describe/stream`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ cellIndex }),
    signal: controller.signal,
  }).then(async (resp) => {
    if (!resp.body) return onDone();
    const reader = resp.body.getReader();
    const decoder = new TextDecoder();
    let buf = '';
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buf += decoder.decode(value, { stream: true });
      const parts = buf.split('\n\n');
      buf = parts.pop() ?? '';
      for (const part of parts) {
        const line = part.startsWith('data: ') ? part.slice(6) : part;
        try {
          const obj = JSON.parse(line) as { token?: string; done?: boolean };
          if (obj.done) { onDone(); return; }
          if (obj.token) onToken(obj.token);
        } catch {/* ignore */}
      }
    }
    onDone();
  }).catch(() => onDone());
  return () => controller.abort();
}

// ── Event Layer Map API (Phase D6) ─────────────────────────────────────────

/** Fetch a float[] scalar field of event-layer thickness per cell. */
export async function fetchEventLayerMap(
  eventType: string,
): Promise<number[]> {
  return get<number[]>(`/api/state/eventlayermap?eventType=${encodeURIComponent(eventType)}`);
}

/** Fetch the list of LayerEventType values present on the planet. */
export async function fetchEventLayerTypes(): Promise<string[]> {
  return get<string[]>('/api/state/eventlayermap/types');
}
