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

export interface AdvanceResult {
  timeMa: number;
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

async function post<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined,
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

export async function generatePlanet(seed: number = 0): Promise<GenerateResult> {
  return post('/api/planet/generate', { seed });
}

export async function advanceSimulation(deltaMa: number): Promise<AdvanceResult> {
  return post('/api/simulation/advance', { deltaMa });
}

export async function getSimulationTime(): Promise<SimulationTimeResult> {
  return get('/api/simulation/time');
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
  onConnected?: (event: { timeMa: number; seed: number }) => void;
  onDisconnected?: () => void;
  /** Called when the backend reports a simulation engine phase (tectonic, surface, biomatter, complete). */
  onProgress?: (event: { phase: string; step?: number; totalSteps?: number; timeMa?: number }) => void;
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
  };
}
