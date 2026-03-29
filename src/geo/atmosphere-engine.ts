// ─── Atmosphere Engine ───────────────────────────────────────────────────────
// Phase 4 orchestrator: runs ClimateEngine → WeatherEngine pipeline,
// emits climate/weather events, handles sub-tick batching.

import type { StateBufferViews, AtmosphericComposition } from '../shared/types';
import { GRID_SIZE } from '../shared/types';
import type { EventBus } from '../kernel/event-bus';
import type { EventLog } from '../kernel/event-log';
import { ClimateEngine, type ClimateResult } from './climate-engine';
import { WeatherEngine, type WeatherResult } from './weather-engine';
import { Xoshiro256ss } from '../proc/prng';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface AtmosphereTickResult {
  climate: ClimateResult;
  weather: WeatherResult;
}

export interface AtmosphereEngineConfig {
  /** Minimum atmosphere tick interval in Ma (default 1.0). */
  minTickInterval?: number;
  /** Grid size (default GRID_SIZE). */
  gridSize?: number;
}

// ─── AtmosphereEngine ───────────────────────────────────────────────────────

export class AtmosphereEngine {
  private readonly climateEngine: ClimateEngine;
  private readonly weatherEngine: WeatherEngine;
  private readonly bus: EventBus;
  private readonly eventLog: EventLog;
  private readonly rng: Xoshiro256ss;

  private stateViews: StateBufferViews | null = null;
  private atmosphere: AtmosphericComposition | null = null;

  /** Sub-tick accumulator. */
  private accumulator = 0;
  private readonly minTickInterval: number;

  /** Previous ice-age state for onset/end detection. */
  private prevIceAge = false;

  constructor(
    bus: EventBus,
    eventLog: EventLog,
    seed: number,
    config?: AtmosphereEngineConfig,
  ) {
    const gridSize = config?.gridSize ?? GRID_SIZE;
    this.bus = bus;
    this.eventLog = eventLog;
    this.rng = new Xoshiro256ss(seed);
    this.minTickInterval = config?.minTickInterval ?? 1.0;

    this.climateEngine = new ClimateEngine(gridSize);
    this.weatherEngine = new WeatherEngine(gridSize);
  }

  /**
   * Initialize with shared state and atmospheric composition.
   */
  initialize(
    stateViews: StateBufferViews,
    atmosphere: AtmosphericComposition,
  ): void {
    this.stateViews = stateViews;
    this.atmosphere = atmosphere;
    this.accumulator = 0;
    this.prevIceAge = false;
  }

  /**
   * Process one simulation tick.
   * Batches sub-ticks if deltaMa exceeds the minimum tick interval.
   */
  tick(timeMa: number, deltaMa: number): AtmosphereTickResult | null {
    if (!this.stateViews || !this.atmosphere || deltaMa <= 0) return null;

    this.accumulator += deltaMa;

    let lastResult: AtmosphereTickResult | null = null;

    while (this.accumulator >= this.minTickInterval) {
      const subDelta = this.minTickInterval;
      this.accumulator -= subDelta;
      const subTime = timeMa - this.accumulator;

      lastResult = this.processAtmosphereTick(subTime, subDelta);
    }

    return lastResult;
  }

  // ── Core tick processing ──────────────────────────────────────────────

  private processAtmosphereTick(timeMa: number, deltaMa: number): AtmosphereTickResult {
    const stateViews = this.stateViews!;
    const atmosphere = this.atmosphere!;

    // 1 — Climate (insolation, 3-cell winds, Milankovitch, greenhouse)
    const climate = this.climateEngine.tick(
      timeMa, deltaMa, stateViews, atmosphere, this.rng,
    );

    // 2 — Weather (fronts, cyclones, orographic precipitation, clouds)
    const weather = this.weatherEngine.tick(
      timeMa, deltaMa, stateViews, this.rng,
    );

    // ── Emit events ───────────────────────────────────────────────────────

    // Climate update
    this.bus.emit('CLIMATE_UPDATE', {
      meanTemperature: climate.meanTemperature,
      co2Ppm: climate.co2Ppm,
      iceAlbedoFeedback: climate.iceAlbedoFeedback,
    });

    // Tropical cyclones
    for (const cyclone of weather.tropicalCyclones) {
      this.bus.emit('TROPICAL_CYCLONE_FORMED', {
        lat: cyclone.lat,
        lon: cyclone.lon,
        intensity: cyclone.intensity,
      });
      this.eventLog.record({
        timeMa,
        type: 'TROPICAL_CYCLONE_FORMED',
        description: `Tropical cyclone Cat ${cyclone.intensity} at ${cyclone.lat.toFixed(1)}°, ${cyclone.lon.toFixed(1)}°`,
      });
    }

    // Snowball Earth
    if (climate.snowballTriggered) {
      this.bus.emit('SNOWBALL_EARTH', {
        equatorialTemp: climate.equatorialTemperature,
      });
      this.eventLog.record({
        timeMa,
        type: 'SNOWBALL_EARTH',
        description: `Snowball Earth threshold reached: equatorial temp ${climate.equatorialTemperature.toFixed(1)}°C`,
      });
    }

    // Ice age onset / end based on mean temperature threshold
    const isIceAge = climate.meanTemperature < -5;
    if (isIceAge && !this.prevIceAge) {
      this.bus.emit('ICE_AGE_ONSET', { severity: climate.iceAlbedoFeedback });
      this.eventLog.record({
        timeMa,
        type: 'ICE_AGE_ONSET',
        description: `Ice age onset: mean temp ${climate.meanTemperature.toFixed(1)}°C, ice coverage ${(climate.iceAlbedoFeedback * 100).toFixed(1)}%`,
      });
    } else if (!isIceAge && this.prevIceAge) {
      this.bus.emit('ICE_AGE_END', {});
      this.eventLog.record({
        timeMa,
        type: 'ICE_AGE_END',
        description: `Ice age ended: mean temp recovered to ${climate.meanTemperature.toFixed(1)}°C`,
      });
    }
    this.prevIceAge = isIceAge;

    return { climate, weather };
  }

  // ── Getters ──────────────────────────────────────────────────────────────

  getClimateEngine(): ClimateEngine {
    return this.climateEngine;
  }

  getWeatherEngine(): WeatherEngine {
    return this.weatherEngine;
  }
}
