// ─── GeoTime Shared Types ───────────────────────────────────────────────────
// Single source of truth for all type definitions used across agents/workers.

// ─── Rock Types ─────────────────────────────────────────────────────────────

export enum RockType {
  // Igneous
  IGN_BASALT = 0,
  IGN_GABBRO,
  IGN_RHYOLITE,
  IGN_GRANITE,
  IGN_ANDESITE,
  IGN_DACITE,
  IGN_OBSIDIAN,
  IGN_PUMICE,
  IGN_PERIDOTITE,
  IGN_KOMATIITE,
  IGN_SYENITE,
  IGN_DIORITE,
  IGN_PYROCLASTIC,
  IGN_TUFF,
  IGN_PILLOW_BASALT,

  // Sedimentary
  SED_SANDSTONE,
  SED_SHALE,
  SED_LIMESTONE,
  SED_DOLOSTONE,
  SED_CONGLOMERATE,
  SED_BRECCIA,
  SED_COAL,
  SED_CHALK,
  SED_CHERT,
  SED_EVAPORITE,
  SED_TURBIDITE,
  SED_TILLITE,
  SED_LOESS,
  SED_IRONSTONE,
  SED_PHOSPHORITE,
  SED_MUDSTONE,
  SED_SILTSTONE,
  SED_ARKOSE,
  SED_GREYWACKE,
  SED_DIATOMITE,
  SED_PEAT,
  SED_LATERITE,
  SED_CALICHE,
  SED_REGOLITH,

  // Metamorphic
  MET_SLATE,
  MET_PHYLLITE,
  MET_SCHIST,
  MET_GNEISS,
  MET_QUARTZITE,
  MET_MARBLE,
  MET_AMPHIBOLITE,
  MET_ECLOGITE,
  MET_BLUESCHIST,
  MET_HORNFELS,
  MET_SERPENTINITE,
  MET_MYLONITE,

  // Deep Earth
  DEEP_LITHMAN,
  DEEP_ASTHEN,
  DEEP_TRANS,
  DEEP_LOWMAN,
  DEEP_CMB,
  DEEP_OUTCORE,
  DEEP_INCORE,
}

// ─── Soil Orders (USDA) ────────────────────────────────────────────────────

export enum SoilOrder {
  NONE = 0,
  ENTISOL,
  INCEPTISOL,
  MOLLISOL,
  ALFISOL,
  ULTISOL,
  OXISOL,
  SPODOSOL,
  HISTOSOL,
  ARIDISOL,
  VERTISOL,
  ANDISOL,
  GELISOL,
}

// ─── Cloud Genera ───────────────────────────────────────────────────────────

export enum CloudGenus {
  NONE = 0,
  CIRRUS,
  CIRROCUMULUS,
  CIRROSTRATUS,
  ALTOCUMULUS,
  ALTOSTRATUS,
  NIMBOSTRATUS,
  STRATOCUMULUS,
  STRATUS,
  CUMULUS,
  CUMULONIMBUS,
}

// ─── Deformation Types ─────────────────────────────────────────────────────

export enum DeformationType {
  UNDEFORMED = 0,
  FOLDED,
  FAULTED,
  METAMORPHOSED,
  OVERTURNED,
}

// ─── Stratigraphic Layer ────────────────────────────────────────────────────

export interface StratigraphicLayer {
  rockType: RockType;
  ageDeposited: number;     // Ma when deposited
  thickness: number;        // current thickness in meters
  dipAngle: number;         // degrees from horizontal
  dipDirection: number;     // azimuth 0-360
  deformation: DeformationType;
  unconformity: boolean;
  soilHorizon: SoilOrder;
  formationName: number;    // index into string table
}

// ─── SharedArrayBuffer Layout ───────────────────────────────────────────────

export const GRID_SIZE = 512;
const CELL_COUNT = GRID_SIZE * GRID_SIZE; // 262 144

const BYTES_F32 = Float32Array.BYTES_PER_ELEMENT; // 4
const BYTES_U16 = Uint16Array.BYTES_PER_ELEMENT;  // 2
const BYTES_U8 = Uint8Array.BYTES_PER_ELEMENT;    // 1

// Byte offsets — ordered to keep typed-array alignment natural.
// Every Float32 offset is divisible by 4; every Uint16 offset by 2.
const HEIGHT_MAP_OFFSET           = 0;
const HEIGHT_MAP_BYTES            = CELL_COUNT * BYTES_F32;                          // 1 048 576

const CRUST_THICKNESS_MAP_OFFSET  = HEIGHT_MAP_OFFSET + HEIGHT_MAP_BYTES;            // 1 048 576
const CRUST_THICKNESS_MAP_BYTES   = CELL_COUNT * BYTES_F32;                          // 1 048 576

const ROCK_TYPE_MAP_OFFSET        = CRUST_THICKNESS_MAP_OFFSET + CRUST_THICKNESS_MAP_BYTES; // 2 097 152
const ROCK_TYPE_MAP_BYTES         = CELL_COUNT * BYTES_U8;                           // 262 144

const ROCK_AGE_MAP_OFFSET         = ROCK_TYPE_MAP_OFFSET + ROCK_TYPE_MAP_BYTES;      // 2 359 296
const ROCK_AGE_MAP_BYTES          = CELL_COUNT * BYTES_F32;                          // 1 048 576

const PLATE_MAP_OFFSET            = ROCK_AGE_MAP_OFFSET + ROCK_AGE_MAP_BYTES;        // 3 407 872
const PLATE_MAP_BYTES             = CELL_COUNT * BYTES_U16;                          // 524 288

const SOIL_TYPE_MAP_OFFSET        = PLATE_MAP_OFFSET + PLATE_MAP_BYTES;              // 3 932 160
const SOIL_TYPE_MAP_BYTES         = CELL_COUNT * BYTES_U8;                           // 262 144

const SOIL_DEPTH_MAP_OFFSET       = SOIL_TYPE_MAP_OFFSET + SOIL_TYPE_MAP_BYTES;      // 4 194 304
const SOIL_DEPTH_MAP_BYTES        = CELL_COUNT * BYTES_F32;                          // 1 048 576

const TEMPERATURE_MAP_OFFSET      = SOIL_DEPTH_MAP_OFFSET + SOIL_DEPTH_MAP_BYTES;    // 5 242 880
const TEMPERATURE_MAP_BYTES       = CELL_COUNT * BYTES_F32;                          // 1 048 576

const PRECIPITATION_MAP_OFFSET    = TEMPERATURE_MAP_OFFSET + TEMPERATURE_MAP_BYTES;  // 6 291 456
const PRECIPITATION_MAP_BYTES     = CELL_COUNT * BYTES_F32;                          // 1 048 576

const WIND_U_MAP_OFFSET           = PRECIPITATION_MAP_OFFSET + PRECIPITATION_MAP_BYTES; // 7 340 032
const WIND_U_MAP_BYTES            = CELL_COUNT * BYTES_F32;                          // 1 048 576

const WIND_V_MAP_OFFSET           = WIND_U_MAP_OFFSET + WIND_U_MAP_BYTES;            // 8 388 608
const WIND_V_MAP_BYTES            = CELL_COUNT * BYTES_F32;                          // 1 048 576

const CLOUD_TYPE_MAP_OFFSET       = WIND_V_MAP_OFFSET + WIND_V_MAP_BYTES;            // 9 437 184
const CLOUD_TYPE_MAP_BYTES        = CELL_COUNT * BYTES_U8;                           // 262 144

const CLOUD_COVER_MAP_OFFSET      = CLOUD_TYPE_MAP_OFFSET + CLOUD_TYPE_MAP_BYTES;    // 9 699 328
const CLOUD_COVER_MAP_BYTES       = CELL_COUNT * BYTES_F32;                          // 1 048 576

export const TOTAL_BUFFER_SIZE    = CLOUD_COVER_MAP_OFFSET + CLOUD_COVER_MAP_BYTES;  // 10 747 904

export const SharedStateLayout = {
  GRID_SIZE,
  CELL_COUNT,

  HEIGHT_MAP_OFFSET,
  HEIGHT_MAP_BYTES,
  CRUST_THICKNESS_MAP_OFFSET,
  CRUST_THICKNESS_MAP_BYTES,
  ROCK_TYPE_MAP_OFFSET,
  ROCK_TYPE_MAP_BYTES,
  ROCK_AGE_MAP_OFFSET,
  ROCK_AGE_MAP_BYTES,
  PLATE_MAP_OFFSET,
  PLATE_MAP_BYTES,
  SOIL_TYPE_MAP_OFFSET,
  SOIL_TYPE_MAP_BYTES,
  SOIL_DEPTH_MAP_OFFSET,
  SOIL_DEPTH_MAP_BYTES,
  TEMPERATURE_MAP_OFFSET,
  TEMPERATURE_MAP_BYTES,
  PRECIPITATION_MAP_OFFSET,
  PRECIPITATION_MAP_BYTES,
  WIND_U_MAP_OFFSET,
  WIND_U_MAP_BYTES,
  WIND_V_MAP_OFFSET,
  WIND_V_MAP_BYTES,
  CLOUD_TYPE_MAP_OFFSET,
  CLOUD_TYPE_MAP_BYTES,
  CLOUD_COVER_MAP_OFFSET,
  CLOUD_COVER_MAP_BYTES,

  TOTAL_BUFFER_SIZE,
} as const;

export interface StateBufferViews {
  heightMap: Float32Array;
  crustThicknessMap: Float32Array;
  rockTypeMap: Uint8Array;
  rockAgeMap: Float32Array;
  plateMap: Uint16Array;
  soilTypeMap: Uint8Array;
  soilDepthMap: Float32Array;
  temperatureMap: Float32Array;
  precipitationMap: Float32Array;
  windUMap: Float32Array;
  windVMap: Float32Array;
  cloudTypeMap: Uint8Array;
  cloudCoverMap: Float32Array;
}

export function createStateBufferLayout(
  buffer: SharedArrayBuffer,
): StateBufferViews {
  return {
    heightMap:          new Float32Array(buffer, HEIGHT_MAP_OFFSET,          CELL_COUNT),
    crustThicknessMap:  new Float32Array(buffer, CRUST_THICKNESS_MAP_OFFSET, CELL_COUNT),
    rockTypeMap:        new Uint8Array(buffer,   ROCK_TYPE_MAP_OFFSET,       CELL_COUNT),
    rockAgeMap:         new Float32Array(buffer, ROCK_AGE_MAP_OFFSET,        CELL_COUNT),
    plateMap:           new Uint16Array(buffer,  PLATE_MAP_OFFSET,           CELL_COUNT),
    soilTypeMap:        new Uint8Array(buffer,   SOIL_TYPE_MAP_OFFSET,       CELL_COUNT),
    soilDepthMap:       new Float32Array(buffer, SOIL_DEPTH_MAP_OFFSET,      CELL_COUNT),
    temperatureMap:     new Float32Array(buffer, TEMPERATURE_MAP_OFFSET,     CELL_COUNT),
    precipitationMap:   new Float32Array(buffer, PRECIPITATION_MAP_OFFSET,   CELL_COUNT),
    windUMap:           new Float32Array(buffer, WIND_U_MAP_OFFSET,          CELL_COUNT),
    windVMap:           new Float32Array(buffer, WIND_V_MAP_OFFSET,          CELL_COUNT),
    cloudTypeMap:       new Uint8Array(buffer,   CLOUD_TYPE_MAP_OFFSET,      CELL_COUNT),
    cloudCoverMap:      new Float32Array(buffer, CLOUD_COVER_MAP_OFFSET,     CELL_COUNT),
  };
}

// ─── Event System ───────────────────────────────────────────────────────────

export type GeoEventType =
  | 'TICK'
  | 'VOLCANIC_ERUPTION'
  | 'PLATE_COLLISION'
  | 'PLATE_RIFT'
  | 'ICE_AGE_ONSET'
  | 'ICE_AGE_END'
  | 'EROSION_CYCLE'
  | 'GLACIATION_ADVANCE'
  | 'GLACIATION_RETREAT'
  | 'MAJOR_RIVER_FORMED'
  | 'CROSS_SECTION_PATH'
  | 'CROSS_SECTION_READY'
  | 'LABEL_TOGGLE'
  | 'SEEK_TO'
  | 'SNAPSHOT_READY'
  | 'PLANET_GENERATED'
  | 'CLIMATE_UPDATE'
  | 'TROPICAL_CYCLONE_FORMED'
  | 'SNOWBALL_EARTH';

export interface LatLon {
  lat: number;
  lon: number;
}

export interface TickPayload {
  timeMa: number;
  deltaMa: number;
}

export interface VolcanicEruptionPayload {
  lat: number;
  lon: number;
  intensity: number;
}

export interface PlateCollisionPayload {
  plate1: number;
  plate2: number;
  boundaryPoints: LatLon[];
}

export interface PlateRiftPayload {
  plate1: number;
  plate2: number;
  boundaryPoints: LatLon[];
}

export interface IceAgeOnsetPayload {
  severity: number;
}

export interface IceAgeEndPayload {}

export interface ErosionCyclePayload {
  totalEroded: number;     // total meters of material eroded this cycle
  totalDeposited: number;  // total meters deposited
  cellsAffected: number;   // number of cells modified
}

export interface GlaciationAdvancePayload {
  glaciatedCells: number;
  equilibriumLineAltitude: number;
}

export interface GlaciationRetreatPayload {
  glaciatedCells: number;
}

export interface MajorRiverFormedPayload {
  cellIndex: number;
  drainageArea: number;  // in grid cells
}

export interface CrossSectionPathPayload {
  points: LatLon[];
}

/** A single sample column along the cross-section path. */
export interface CrossSectionSample {
  /** Distance along the path in km. */
  distanceKm: number;
  /** Surface elevation in meters (from heightMap). */
  surfaceElevation: number;
  /** Crust thickness in km (from crustThicknessMap). */
  crustThicknessKm: number;
  /** Soil type at surface. */
  soilType: SoilOrder;
  /** Soil depth in meters. */
  soilDepthM: number;
  /** Stratigraphic layers at this point (bottom → top). */
  layers: StratigraphicLayer[];
}

/** Deep earth zone definition for cross-section rendering. */
export interface DeepEarthZone {
  name: string;
  topKm: number;
  bottomKm: number;
  rockType: RockType;
}

/** Full cross-section profile data. */
export interface CrossSectionProfile {
  /** Sample columns along the path. */
  samples: CrossSectionSample[];
  /** Total path distance in km. */
  totalDistanceKm: number;
  /** Original path points used. */
  pathPoints: LatLon[];
  /** Deep earth zones to render below the crust. */
  deepEarthZones: DeepEarthZone[];
}

export interface CrossSectionReadyPayload {
  profile: CrossSectionProfile;
}

export interface LabelTogglePayload {
  visible: boolean;
}

export interface SeekToPayload {
  timeMa: number;
}

export interface SnapshotReadyPayload {
  timeMa: number;
}

export interface PlanetGeneratedPayload {
  seed: number;
  timeMa: number;
}

export interface ClimateUpdatePayload {
  meanTemperature: number;
  co2Ppm: number;
  iceAlbedoFeedback: number;
}

export interface TropicalCycloneFormedPayload {
  lat: number;
  lon: number;
  intensity: number; // 1-5 Saffir-Simpson
}

export interface SnowballEarthPayload {
  equatorialTemp: number;
}

export interface GeoEventPayloadMap {
  TICK: TickPayload;
  VOLCANIC_ERUPTION: VolcanicEruptionPayload;
  PLATE_COLLISION: PlateCollisionPayload;
  PLATE_RIFT: PlateRiftPayload;
  ICE_AGE_ONSET: IceAgeOnsetPayload;
  ICE_AGE_END: IceAgeEndPayload;
  EROSION_CYCLE: ErosionCyclePayload;
  GLACIATION_ADVANCE: GlaciationAdvancePayload;
  GLACIATION_RETREAT: GlaciationRetreatPayload;
  MAJOR_RIVER_FORMED: MajorRiverFormedPayload;
  CROSS_SECTION_PATH: CrossSectionPathPayload;
  CROSS_SECTION_READY: CrossSectionReadyPayload;
  LABEL_TOGGLE: LabelTogglePayload;
  SEEK_TO: SeekToPayload;
  SNAPSHOT_READY: SnapshotReadyPayload;
  PLANET_GENERATED: PlanetGeneratedPayload;
  CLIMATE_UPDATE: ClimateUpdatePayload;
  TROPICAL_CYCLONE_FORMED: TropicalCycloneFormedPayload;
  SNOWBALL_EARTH: SnowballEarthPayload;
}

export interface GeoEvent<T extends GeoEventType = GeoEventType> {
  type: T;
  payload: GeoEventPayloadMap[T];
}

// ─── Atmospheric Composition ────────────────────────────────────────────────

export interface AtmosphericComposition {
  n2: number;   // fraction 0-1
  o2: number;   // fraction 0-1
  co2: number;  // fraction 0-1
  h2o: number;  // fraction 0-1
}

// ─── Plate Info ─────────────────────────────────────────────────────────────

export interface PlateInfo {
  id: number;
  centerLat: number;
  centerLon: number;
  angularVelocity: {
    lat: number;
    lon: number;
    rate: number;
  };
  isOceanic: boolean;
  area: number;
}

// ─── Hotspot Info ───────────────────────────────────────────────────────────

export interface HotspotInfo {
  lat: number;
  lon: number;
  strength: number;
}
