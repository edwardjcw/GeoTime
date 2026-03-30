// ─── Backend API Client ─────────────────────────────────────────────────────
// Communicates with the C# .NET backend for all simulation calculations.
// The frontend only handles display (Three.js rendering, UI) and sends
// commands to the backend.

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

// ── Public API ──────────────────────────────────────────────────────────────

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
