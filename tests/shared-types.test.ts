import {
  SharedStateLayout,
  TOTAL_BUFFER_SIZE,
  GRID_SIZE,
  createStateBufferLayout,
} from '../src/shared/types';

describe('SharedStateLayout', () => {
  it('should have correct TOTAL_BUFFER_SIZE', () => {
    expect(SharedStateLayout.TOTAL_BUFFER_SIZE).toBe(TOTAL_BUFFER_SIZE);
    expect(TOTAL_BUFFER_SIZE).toBe(
      SharedStateLayout.BIOMASS_MAP_OFFSET +
        SharedStateLayout.BIOMASS_MAP_BYTES,
    );
  });
});

describe('createStateBufferLayout', () => {
  const cellCount = GRID_SIZE * GRID_SIZE;

  function makeViews() {
    const buf = new ArrayBuffer(TOTAL_BUFFER_SIZE) as unknown as SharedArrayBuffer;
    return createStateBufferLayout(buf);
  }

  it('should create valid typed array views', () => {
    const views = makeViews();
    expect(views.heightMap).toBeInstanceOf(Float32Array);
    expect(views.crustThicknessMap).toBeInstanceOf(Float32Array);
    expect(views.rockTypeMap).toBeInstanceOf(Uint8Array);
    expect(views.rockAgeMap).toBeInstanceOf(Float32Array);
    expect(views.plateMap).toBeInstanceOf(Uint16Array);
    expect(views.soilTypeMap).toBeInstanceOf(Uint8Array);
    expect(views.soilDepthMap).toBeInstanceOf(Float32Array);
    expect(views.temperatureMap).toBeInstanceOf(Float32Array);
    expect(views.precipitationMap).toBeInstanceOf(Float32Array);
    expect(views.windUMap).toBeInstanceOf(Float32Array);
    expect(views.windVMap).toBeInstanceOf(Float32Array);
    expect(views.cloudTypeMap).toBeInstanceOf(Uint8Array);
    expect(views.cloudCoverMap).toBeInstanceOf(Float32Array);
    expect(views.biomassMap).toBeInstanceOf(Float32Array);
  });

  it('all views should have correct length (GRID_SIZE * GRID_SIZE)', () => {
    const views = makeViews();
    expect(views.heightMap.length).toBe(cellCount);
    expect(views.crustThicknessMap.length).toBe(cellCount);
    expect(views.rockTypeMap.length).toBe(cellCount);
    expect(views.rockAgeMap.length).toBe(cellCount);
    expect(views.plateMap.length).toBe(cellCount);
    expect(views.soilTypeMap.length).toBe(cellCount);
    expect(views.soilDepthMap.length).toBe(cellCount);
    expect(views.temperatureMap.length).toBe(cellCount);
    expect(views.precipitationMap.length).toBe(cellCount);
    expect(views.windUMap.length).toBe(cellCount);
    expect(views.windVMap.length).toBe(cellCount);
    expect(views.cloudTypeMap.length).toBe(cellCount);
    expect(views.cloudCoverMap.length).toBe(cellCount);
    expect(views.biomassMap.length).toBe(cellCount);
  });

  it('views should be writable and readable', () => {
    const views = makeViews();
    views.heightMap[0] = 1.5;
    expect(views.heightMap[0]).toBeCloseTo(1.5);
    views.plateMap[0] = 7;
    expect(views.plateMap[0]).toBe(7);
    views.rockTypeMap[0] = 3;
    expect(views.rockTypeMap[0]).toBe(3);
  });
});
