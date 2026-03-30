// ─── Cross-Section Renderer ─────────────────────────────────────────────────
// Canvas 2D renderer for the cross-section profile.  Takes a
// CrossSectionProfile and renders a geologically accurate vertical slice from
// surface to core, with split vertical scale, rock-type colours, labels,
// unconformity markers, deep-earth zones, and a legend panel.

import type {
  CrossSectionProfile,
  CrossSectionSample,
  StratigraphicLayer,
  DeepEarthZone,
} from '../shared/types';
import { RockType, DeformationType, SoilOrder } from '../shared/types';
import { EARTH_RADIUS_KM } from '../geo/cross-section-engine';

// ─── Constants ──────────────────────────────────────────────────────────────

/** Vertical split: linear scale from 0 to this depth (km). */
const LINEAR_DEPTH_KM = 100;

/** Total depth of the planet (km). */
const TOTAL_DEPTH_KM = 6371;

/** Fraction of the vertical canvas used by the linear (upper) section. */
const LINEAR_FRACTION = 0.45;

/** Minimum layer pixel thickness to render a label. */
const MIN_LABEL_PX = 3;

/** Legend swatch size. */
const LEGEND_SWATCH = 14;
const LEGEND_GAP = 4;
const LEGEND_WIDTH = 180;

// ─── Rock Type Colors ───────────────────────────────────────────────────────

const ROCK_COLORS: Record<number, string> = {
  // Igneous
  [RockType.IGN_BASALT]:       '#3a3a3a',
  [RockType.IGN_GABBRO]:       '#2d2d2d',
  [RockType.IGN_RHYOLITE]:     '#d4a5a5',
  [RockType.IGN_GRANITE]:      '#e8c8c8',
  [RockType.IGN_ANDESITE]:     '#7a7a7a',
  [RockType.IGN_DACITE]:       '#a89888',
  [RockType.IGN_OBSIDIAN]:     '#111111',
  [RockType.IGN_PUMICE]:       '#f0e8d8',
  [RockType.IGN_PERIDOTITE]:   '#6b8e23',
  [RockType.IGN_KOMATIITE]:    '#355e20',
  [RockType.IGN_SYENITE]:      '#c8a8b0',
  [RockType.IGN_DIORITE]:      '#888888',
  [RockType.IGN_PYROCLASTIC]:  '#b08070',
  [RockType.IGN_TUFF]:         '#c0b8a8',
  [RockType.IGN_PILLOW_BASALT]:'#2a3a3a',

  // Sedimentary
  [RockType.SED_SANDSTONE]:    '#d4a050',
  [RockType.SED_SHALE]:        '#6a5a4a',
  [RockType.SED_LIMESTONE]:    '#d8d0b8',
  [RockType.SED_DOLOSTONE]:    '#c8c0a0',
  [RockType.SED_CONGLOMERATE]: '#b09070',
  [RockType.SED_BRECCIA]:      '#a07060',
  [RockType.SED_COAL]:         '#1a1a1a',
  [RockType.SED_CHALK]:        '#f8f8f0',
  [RockType.SED_CHERT]:        '#8b2500',
  [RockType.SED_EVAPORITE]:    '#f0d8e0',
  [RockType.SED_TURBIDITE]:    '#7a6a5a',
  [RockType.SED_TILLITE]:      '#8a8070',
  [RockType.SED_LOESS]:        '#e8d8a0',
  [RockType.SED_IRONSTONE]:    '#8b3a3a',
  [RockType.SED_PHOSPHORITE]:  '#5a4a3a',
  [RockType.SED_MUDSTONE]:     '#5a5050',
  [RockType.SED_SILTSTONE]:    '#908070',
  [RockType.SED_ARKOSE]:       '#c8a098',
  [RockType.SED_GREYWACKE]:    '#505050',
  [RockType.SED_DIATOMITE]:    '#f0f0e8',
  [RockType.SED_PEAT]:         '#302010',
  [RockType.SED_LATERITE]:     '#c04020',
  [RockType.SED_CALICHE]:      '#f0e8d0',
  [RockType.SED_REGOLITH]:     '#b8a888',

  // Metamorphic
  [RockType.MET_SLATE]:        '#4a4a5a',
  [RockType.MET_PHYLLITE]:     '#5a5a6a',
  [RockType.MET_SCHIST]:       '#6a6a7a',
  [RockType.MET_GNEISS]:       '#a0a0b8',
  [RockType.MET_QUARTZITE]:    '#e0d8d0',
  [RockType.MET_MARBLE]:       '#e8e0e0',
  [RockType.MET_AMPHIBOLITE]:  '#3a4a3a',
  [RockType.MET_ECLOGITE]:     '#4a6a4a',
  [RockType.MET_BLUESCHIST]:   '#4a5a8a',
  [RockType.MET_HORNFELS]:     '#5a5050',
  [RockType.MET_SERPENTINITE]:  '#3a6a3a',
  [RockType.MET_MYLONITE]:     '#6a6060',

  // Deep Earth
  [RockType.DEEP_LITHMAN]:     '#7a9a60',
  [RockType.DEEP_ASTHEN]:      '#c87040',
  [RockType.DEEP_TRANS]:       '#d09050',
  [RockType.DEEP_LOWMAN]:      '#e0a060',
  [RockType.DEEP_CMB]:         '#d04020',
  [RockType.DEEP_OUTCORE]:     '#f0a020',
  [RockType.DEEP_INCORE]:      '#f0c040',
};

/** Get the colour for a rock type (hex string). */
export function getRockColor(rt: RockType): string {
  return ROCK_COLORS[rt] ?? '#999999';
}

// ─── Rock Type Names ────────────────────────────────────────────────────────

const ROCK_NAMES: Record<number, string> = {
  [RockType.IGN_BASALT]:       'Basalt',
  [RockType.IGN_GABBRO]:       'Gabbro',
  [RockType.IGN_RHYOLITE]:     'Rhyolite',
  [RockType.IGN_GRANITE]:      'Granite',
  [RockType.IGN_ANDESITE]:     'Andesite',
  [RockType.IGN_DACITE]:       'Dacite',
  [RockType.IGN_OBSIDIAN]:     'Obsidian',
  [RockType.IGN_PUMICE]:       'Pumice',
  [RockType.IGN_PERIDOTITE]:   'Peridotite',
  [RockType.IGN_KOMATIITE]:    'Komatiite',
  [RockType.IGN_SYENITE]:      'Syenite',
  [RockType.IGN_DIORITE]:      'Diorite',
  [RockType.IGN_PYROCLASTIC]:  'Pyroclastic',
  [RockType.IGN_TUFF]:         'Tuff',
  [RockType.IGN_PILLOW_BASALT]:'Pillow Basalt',
  [RockType.SED_SANDSTONE]:    'Sandstone',
  [RockType.SED_SHALE]:        'Shale',
  [RockType.SED_LIMESTONE]:    'Limestone',
  [RockType.SED_DOLOSTONE]:    'Dolostone',
  [RockType.SED_CONGLOMERATE]: 'Conglomerate',
  [RockType.SED_BRECCIA]:      'Breccia',
  [RockType.SED_COAL]:         'Coal',
  [RockType.SED_CHALK]:        'Chalk',
  [RockType.SED_CHERT]:        'Chert',
  [RockType.SED_EVAPORITE]:    'Evaporite',
  [RockType.SED_TURBIDITE]:    'Turbidite',
  [RockType.SED_TILLITE]:      'Tillite',
  [RockType.SED_LOESS]:        'Loess',
  [RockType.SED_IRONSTONE]:    'Ironstone',
  [RockType.SED_PHOSPHORITE]:  'Phosphorite',
  [RockType.SED_MUDSTONE]:     'Mudstone',
  [RockType.SED_SILTSTONE]:    'Siltstone',
  [RockType.SED_ARKOSE]:       'Arkose',
  [RockType.SED_GREYWACKE]:    'Greywacke',
  [RockType.SED_DIATOMITE]:    'Diatomite',
  [RockType.SED_PEAT]:         'Peat',
  [RockType.SED_LATERITE]:     'Laterite',
  [RockType.SED_CALICHE]:      'Caliche',
  [RockType.SED_REGOLITH]:     'Regolith',
  [RockType.MET_SLATE]:        'Slate',
  [RockType.MET_PHYLLITE]:     'Phyllite',
  [RockType.MET_SCHIST]:       'Schist',
  [RockType.MET_GNEISS]:       'Gneiss',
  [RockType.MET_QUARTZITE]:    'Quartzite',
  [RockType.MET_MARBLE]:       'Marble',
  [RockType.MET_AMPHIBOLITE]:  'Amphibolite',
  [RockType.MET_ECLOGITE]:     'Eclogite',
  [RockType.MET_BLUESCHIST]:   'Blueschist',
  [RockType.MET_HORNFELS]:     'Hornfels',
  [RockType.MET_SERPENTINITE]:  'Serpentinite',
  [RockType.MET_MYLONITE]:     'Mylonite',
  [RockType.DEEP_LITHMAN]:     'Lithospheric Mantle',
  [RockType.DEEP_ASTHEN]:      'Asthenosphere',
  [RockType.DEEP_TRANS]:       'Transition Zone',
  [RockType.DEEP_LOWMAN]:      'Lower Mantle',
  [RockType.DEEP_CMB]:         'Core-Mantle Boundary',
  [RockType.DEEP_OUTCORE]:     'Outer Core',
  [RockType.DEEP_INCORE]:      'Inner Core',
};

export function getRockName(rt: RockType): string {
  return ROCK_NAMES[rt] ?? `Rock #${rt}`;
}

// ─── Soil Order Names ───────────────────────────────────────────────────────

const SOIL_NAMES: Record<number, string> = {
  [SoilOrder.NONE]:       '',
  [SoilOrder.ENTISOL]:    'Entisol',
  [SoilOrder.INCEPTISOL]: 'Inceptisol',
  [SoilOrder.MOLLISOL]:   'Mollisol',
  [SoilOrder.ALFISOL]:    'Alfisol',
  [SoilOrder.ULTISOL]:    'Ultisol',
  [SoilOrder.OXISOL]:     'Oxisol',
  [SoilOrder.SPODOSOL]:   'Spodosol',
  [SoilOrder.HISTOSOL]:   'Histosol',
  [SoilOrder.ARIDISOL]:   'Aridisol',
  [SoilOrder.VERTISOL]:   'Vertisol',
  [SoilOrder.ANDISOL]:    'Andisol',
  [SoilOrder.GELISOL]:    'Gelisol',
};

export function getSoilName(so: SoilOrder): string {
  return SOIL_NAMES[so] ?? '';
}

// ─── Vertical Scale ─────────────────────────────────────────────────────────

/**
 * Convert a depth in km to a Y-pixel coordinate using the split scale.
 * Linear from 0–100 km, logarithmic from 100–6371 km.
 * @param depthKm Depth below surface in km.
 * @param canvasHeight Total canvas height in pixels.
 * @param topMargin Top margin in pixels (for surface label).
 * @returns Y pixel position.
 */
export function depthToY(
  depthKm: number,
  canvasHeight: number,
  topMargin: number = 30,
): number {
  const drawHeight = canvasHeight - topMargin;
  const linearPx = drawHeight * LINEAR_FRACTION;
  const logPx = drawHeight * (1 - LINEAR_FRACTION);

  if (depthKm <= 0) return topMargin;

  if (depthKm <= LINEAR_DEPTH_KM) {
    return topMargin + (depthKm / LINEAR_DEPTH_KM) * linearPx;
  }

  // Logarithmic section: map 100..6371 → 0..1 via log scale
  const logMin = Math.log(LINEAR_DEPTH_KM);
  const logMax = Math.log(TOTAL_DEPTH_KM);
  const frac = (Math.log(Math.min(depthKm, TOTAL_DEPTH_KM)) - logMin) / (logMax - logMin);
  return topMargin + linearPx + frac * logPx;
}

// ─── Label Struct ───────────────────────────────────────────────────────────

export interface LayerLabel {
  x: number;
  y: number;
  text: string;
  color: string;
  rockType: RockType;
}

// ─── Build Label Text ───────────────────────────────────────────────────────

export function buildLayerLabel(layer: StratigraphicLayer, soilType?: SoilOrder): string {
  const parts: string[] = [];
  parts.push(getRockName(layer.rockType));
  parts.push(`${Math.abs(layer.ageDeposited).toFixed(0)} Ma`);
  if (soilType != null && soilType !== SoilOrder.NONE) {
    parts.push(getSoilName(soilType));
  }
  return parts.join(' · ');
}

// ─── Unconformity Hatching ──────────────────────────────────────────────────

function drawUnconformityLine(
  ctx: CanvasRenderingContext2D,
  x1: number, y: number, x2: number,
): void {
  ctx.save();
  ctx.strokeStyle = '#ff4444';
  ctx.lineWidth = 2;
  ctx.setLineDash([6, 4]);
  ctx.beginPath();
  ctx.moveTo(x1, y);
  ctx.lineTo(x2, y);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.restore();
}

// ─── Fault Indicator ────────────────────────────────────────────────────────

function drawFaultLine(
  ctx: CanvasRenderingContext2D,
  x: number, y1: number, y2: number,
): void {
  ctx.save();
  ctx.strokeStyle = '#ff0000';
  ctx.lineWidth = 2.5;
  ctx.setLineDash([4, 3]);
  ctx.beginPath();
  ctx.moveTo(x, y1);
  ctx.lineTo(x, y2);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.restore();
}

// ─── Renderer Configuration ─────────────────────────────────────────────────

export interface CrossSectionRenderConfig {
  /** Canvas width in pixels. */
  width: number;
  /** Canvas height in pixels. */
  height: number;
  /** Whether to render labels. */
  showLabels: boolean;
  /** Whether to render the legend panel. */
  showLegend: boolean;
  /** Top margin in pixels. */
  topMargin?: number;
  /** Left margin (for Y-axis labels). */
  leftMargin?: number;
  /** Right margin. */
  rightMargin?: number;
}

// ─── Main Render Function ───────────────────────────────────────────────────

/**
 * Render a CrossSectionProfile to a 2D canvas.
 * Returns the canvas for compositing or export.
 */
export function renderCrossSection(
  profile: CrossSectionProfile,
  config: CrossSectionRenderConfig,
  canvas?: HTMLCanvasElement,
): HTMLCanvasElement {
  const {
    width,
    height,
    showLabels,
    showLegend,
    topMargin = 30,
    leftMargin = 70,
    rightMargin = showLegend ? LEGEND_WIDTH + 20 : 10,
  } = config;

  if (!canvas) {
    canvas = document.createElement('canvas');
  }
  canvas.width = width;
  canvas.height = height;

  const ctx = canvas.getContext('2d');
  if (!ctx) return canvas; // No 2D context available (e.g. test environment)

  ctx.clearRect(0, 0, width, height);

  // Background
  ctx.fillStyle = '#0a0a0e';
  ctx.fillRect(0, 0, width, height);

  const drawWidth = width - leftMargin - rightMargin;
  const samples = profile.samples;
  if (samples.length === 0 || drawWidth <= 0) return canvas;

  const totalDist = profile.totalDistanceKm;
  const colWidth = drawWidth / Math.max(1, samples.length - 1);

  // Collect all visible rock types for legend
  const visibleRockTypes = new Set<RockType>();

  // ── Render stratigraphic layers (upper section) ──────────────────────
  for (let i = 0; i < samples.length; i++) {
    const s = samples[i];
    const x = leftMargin + (totalDist > 0
      ? (s.distanceKm / totalDist) * drawWidth
      : (i / Math.max(1, samples.length - 1)) * drawWidth);
    const w = Math.max(1, colWidth);

    // Build layer depths from surface downward
    let depthM = 0;
    for (let li = s.layers.length - 1; li >= 0; li--) {
      const layer = s.layers[li];
      const topDepthKm = depthM / 1000;
      depthM += layer.thickness;
      const botDepthKm = depthM / 1000;

      const y1 = depthToY(topDepthKm, height, topMargin);
      const y2 = depthToY(botDepthKm, height, topMargin);

      if (y2 - y1 < 0.5) continue; // sub-pixel — skip

      ctx.fillStyle = getRockColor(layer.rockType);
      ctx.fillRect(x - w / 2, y1, w, y2 - y1);

      visibleRockTypes.add(layer.rockType);

      // Unconformity marker
      if (layer.unconformity && y2 - y1 >= 1) {
        const xStart = Math.max(leftMargin, x - w / 2);
        const xEnd = Math.min(leftMargin + drawWidth, x + w / 2);
        drawUnconformityLine(ctx, xStart, y1, xEnd);
      }
    }

    // Detect fault: layer count changes abruptly between adjacent columns
    if (i > 0) {
      const prev = samples[i - 1];
      if (Math.abs(s.layers.length - prev.layers.length) > 2) {
        const faultX = x - w / 2;
        drawFaultLine(ctx, faultX, topMargin, depthToY(Math.min(depthM / 1000, LINEAR_DEPTH_KM), height, topMargin));
      }
    }

    // Draw Moho discontinuity
    const mohoKm = s.crustThicknessKm;
    if (mohoKm > 0) {
      const mohoY = depthToY(mohoKm, height, topMargin);
      ctx.strokeStyle = '#44aaff';
      ctx.lineWidth = 1;
      const xStart = x - w / 2;
      const xEnd = x + w / 2;
      ctx.beginPath();
      ctx.moveTo(xStart, mohoY);
      ctx.lineTo(xEnd, mohoY);
      ctx.stroke();
    }
  }

  // ── Render deep earth zones ──────────────────────────────────────────
  for (const zone of profile.deepEarthZones) {
    const y1 = depthToY(zone.topKm, height, topMargin);
    const y2 = depthToY(zone.bottomKm, height, topMargin);

    if (y2 - y1 < 1) continue;

    ctx.fillStyle = getRockColor(zone.rockType);
    ctx.fillRect(leftMargin, y1, drawWidth, y2 - y1);

    visibleRockTypes.add(zone.rockType);

    // Zone label (always shown)
    ctx.fillStyle = '#ffffff';
    ctx.font = '11px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    const midY = (y1 + y2) / 2;
    if (y2 - y1 > 14) {
      ctx.fillText(zone.name, leftMargin + drawWidth / 2, midY);
    }
  }

  // ── Scale break indicator ────────────────────────────────────────────
  const breakY = depthToY(LINEAR_DEPTH_KM, height, topMargin);
  ctx.save();
  ctx.strokeStyle = '#666';
  ctx.lineWidth = 1;
  ctx.setLineDash([8, 4]);
  ctx.beginPath();
  ctx.moveTo(leftMargin - 5, breakY);
  ctx.lineTo(leftMargin + drawWidth + 5, breakY);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.fillStyle = '#888';
  ctx.font = '10px monospace';
  ctx.textAlign = 'right';
  ctx.fillText('— scale break —', leftMargin - 8, breakY + 4);
  ctx.restore();

  // ── Y-axis depth labels ──────────────────────────────────────────────
  ctx.fillStyle = '#aaa';
  ctx.font = '10px monospace';
  ctx.textAlign = 'right';
  ctx.textBaseline = 'middle';

  const linearTicks = [0, 10, 20, 30, 50, 100];
  for (const d of linearTicks) {
    const y = depthToY(d, height, topMargin);
    ctx.fillText(`${d} km`, leftMargin - 8, y);
    // Tick mark
    ctx.beginPath();
    ctx.strokeStyle = '#444';
    ctx.moveTo(leftMargin - 4, y);
    ctx.lineTo(leftMargin, y);
    ctx.stroke();
  }

  const logTicks = [410, 660, 2891, 5150, 6371];
  for (const d of logTicks) {
    const y = depthToY(d, height, topMargin);
    ctx.fillText(`${d} km`, leftMargin - 8, y);
    ctx.beginPath();
    ctx.strokeStyle = '#444';
    ctx.moveTo(leftMargin - 4, y);
    ctx.lineTo(leftMargin, y);
    ctx.stroke();
  }

  // ── X-axis distance labels ───────────────────────────────────────────
  ctx.fillStyle = '#aaa';
  ctx.font = '10px monospace';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';

  const numXLabels = Math.min(6, samples.length);
  for (let i = 0; i < numXLabels; i++) {
    const frac = numXLabels > 1 ? i / (numXLabels - 1) : 0;
    const x = leftMargin + frac * drawWidth;
    const dist = frac * totalDist;
    ctx.fillText(`${dist.toFixed(0)} km`, x, height - 18);
  }

  // ── Surface elevation profile line ───────────────────────────────────
  ctx.save();
  ctx.strokeStyle = '#44ff44';
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  for (let i = 0; i < samples.length; i++) {
    const s = samples[i];
    const x = leftMargin + (totalDist > 0
      ? (s.distanceKm / totalDist) * drawWidth
      : (i / Math.max(1, samples.length - 1)) * drawWidth);
    // Elevation above sea level → above surface line (negative depth)
    const elevKm = s.surfaceElevation / 1000;
    const y = depthToY(-elevKm * 0.02, height, topMargin); // slight exaggeration for visibility
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.stroke();
  ctx.restore();

  // ── Title ────────────────────────────────────────────────────────────
  ctx.fillStyle = '#ddd';
  ctx.font = 'bold 13px sans-serif';
  ctx.textAlign = 'left';
  ctx.fillText(`Cross-Section — ${totalDist.toFixed(0)} km`, leftMargin, 14);

  // ── Labels ───────────────────────────────────────────────────────────
  if (showLabels) {
    renderLabels(ctx, profile, leftMargin, drawWidth, height, topMargin);
  }

  // ── Legend ────────────────────────────────────────────────────────────
  if (showLegend) {
    renderLegend(ctx, visibleRockTypes, width - LEGEND_WIDTH - 5, topMargin + 5, height - topMargin - 10);
  }

  return canvas;
}

// ─── Label Rendering ────────────────────────────────────────────────────────

function renderLabels(
  ctx: CanvasRenderingContext2D,
  profile: CrossSectionProfile,
  leftMargin: number,
  drawWidth: number,
  canvasHeight: number,
  topMargin: number,
): void {
  const samples = profile.samples;
  const totalDist = profile.totalDistanceKm;
  if (samples.length === 0) return;

  // Sample a subset of columns for labels (every ~10th column)
  const labelStep = Math.max(1, Math.floor(samples.length / 30));
  const labels: LayerLabel[] = [];

  for (let i = 0; i < samples.length; i += labelStep) {
    const s = samples[i];
    const x = leftMargin + (totalDist > 0
      ? (s.distanceKm / totalDist) * drawWidth
      : (i / Math.max(1, samples.length - 1)) * drawWidth);

    let depthM = 0;
    for (let li = s.layers.length - 1; li >= 0; li--) {
      const layer = s.layers[li];
      const topDepthKm = depthM / 1000;
      depthM += layer.thickness;
      const botDepthKm = depthM / 1000;

      const y1 = depthToY(topDepthKm, canvasHeight, topMargin);
      const y2 = depthToY(botDepthKm, canvasHeight, topMargin);
      const pixH = y2 - y1;

      if (pixH < MIN_LABEL_PX) continue;

      const soilType = (li === s.layers.length - 1) ? s.soilType : SoilOrder.NONE;
      const text = buildLayerLabel(layer, soilType);
      const midY = (y1 + y2) / 2;

      labels.push({
        x,
        y: midY,
        text,
        color: '#eee',
        rockType: layer.rockType,
      });
    }
  }

  // Anti-collision: sort by Y and push overlapping labels apart
  labels.sort((a, b) => a.y - b.y);
  for (let i = 1; i < labels.length; i++) {
    const minGap = 12;
    if (labels[i].y - labels[i - 1].y < minGap) {
      labels[i].y = labels[i - 1].y + minGap;
    }
  }

  // Draw labels
  ctx.save();
  ctx.font = '9px sans-serif';
  ctx.textBaseline = 'middle';
  for (const label of labels) {
    ctx.fillStyle = label.color;
    ctx.textAlign = 'left';
    ctx.fillText(label.text, label.x + 4, label.y);
  }
  ctx.restore();
}

// ─── Legend Rendering ───────────────────────────────────────────────────────

function renderLegend(
  ctx: CanvasRenderingContext2D,
  rockTypes: Set<RockType>,
  x: number,
  y: number,
  maxHeight: number,
): void {
  ctx.save();

  // Background
  ctx.fillStyle = 'rgba(10, 10, 14, 0.9)';
  const types = Array.from(rockTypes).sort((a, b) => a - b);
  const legendH = Math.min(
    types.length * (LEGEND_SWATCH + LEGEND_GAP) + 30,
    maxHeight,
  );
  ctx.fillRect(x, y, LEGEND_WIDTH, legendH);
  ctx.strokeStyle = 'rgba(255,255,255,0.1)';
  ctx.strokeRect(x, y, LEGEND_WIDTH, legendH);

  // Title
  ctx.fillStyle = '#ccc';
  ctx.font = 'bold 11px sans-serif';
  ctx.textAlign = 'left';
  ctx.textBaseline = 'top';
  ctx.fillText('Legend', x + 8, y + 6);

  let yPos = y + 24;
  ctx.font = '10px sans-serif';
  ctx.textBaseline = 'middle';

  for (const rt of types) {
    if (yPos + LEGEND_SWATCH > y + legendH) break;

    ctx.fillStyle = getRockColor(rt);
    ctx.fillRect(x + 8, yPos, LEGEND_SWATCH, LEGEND_SWATCH);
    ctx.strokeStyle = '#555';
    ctx.strokeRect(x + 8, yPos, LEGEND_SWATCH, LEGEND_SWATCH);

    ctx.fillStyle = '#ccc';
    ctx.fillText(getRockName(rt), x + 8 + LEGEND_SWATCH + 6, yPos + LEGEND_SWATCH / 2);

    yPos += LEGEND_SWATCH + LEGEND_GAP;
  }

  ctx.restore();
}

// ─── Export Utility ─────────────────────────────────────────────────────────

/**
 * Export a cross-section canvas to a PNG data URL.
 */
export function exportCrossSectionPNG(canvas: HTMLCanvasElement): string {
  return canvas.toDataURL('image/png');
}
