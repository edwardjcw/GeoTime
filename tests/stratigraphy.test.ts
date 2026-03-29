import { StratigraphyStack, MAX_LAYERS_PER_CELL } from '../src/geo/stratigraphy';
import { RockType, DeformationType, SoilOrder } from '../src/shared/types';
import type { StratigraphicLayer } from '../src/shared/types';

function makeLayer(overrides?: Partial<StratigraphicLayer>): StratigraphicLayer {
  return {
    rockType: RockType.IGN_BASALT,
    ageDeposited: -4000,
    thickness: 1000,
    dipAngle: 0,
    dipDirection: 0,
    deformation: DeformationType.UNDEFORMED,
    unconformity: false,
    soilHorizon: SoilOrder.NONE,
    formationName: 0,
    ...overrides,
  };
}

describe('StratigraphyStack', () => {
  let stack: StratigraphyStack;

  beforeEach(() => {
    stack = new StratigraphyStack();
  });

  it('should start empty with size 0', () => {
    expect(stack.size).toBe(0);
    expect(stack.getLayers(0)).toEqual([]);
    expect(stack.getTopLayer(0)).toBeUndefined();
    expect(stack.getTotalThickness(0)).toBe(0);
  });

  it('should push and retrieve a layer', () => {
    const layer = makeLayer();
    stack.pushLayer(0, layer);
    expect(stack.getLayers(0)).toHaveLength(1);
    expect(stack.getTopLayer(0)?.rockType).toBe(RockType.IGN_BASALT);
    expect(stack.getTotalThickness(0)).toBe(1000);
    expect(stack.size).toBe(1);
  });

  it('should push multiple layers in order', () => {
    stack.pushLayer(0, makeLayer({ rockType: RockType.IGN_GABBRO, thickness: 500 }));
    stack.pushLayer(0, makeLayer({ rockType: RockType.IGN_GRANITE, thickness: 300 }));
    stack.pushLayer(0, makeLayer({ rockType: RockType.SED_SANDSTONE, thickness: 200 }));

    const layers = stack.getLayers(0);
    expect(layers).toHaveLength(3);
    expect(layers[0].rockType).toBe(RockType.IGN_GABBRO);
    expect(layers[2].rockType).toBe(RockType.SED_SANDSTONE);
    expect(stack.getTopLayer(0)?.rockType).toBe(RockType.SED_SANDSTONE);
    expect(stack.getTotalThickness(0)).toBe(1000);
  });

  it('should keep separate stacks for different cells', () => {
    stack.pushLayer(0, makeLayer({ rockType: RockType.IGN_BASALT }));
    stack.pushLayer(100, makeLayer({ rockType: RockType.IGN_GRANITE }));
    expect(stack.getLayers(0)).toHaveLength(1);
    expect(stack.getLayers(100)).toHaveLength(1);
    expect(stack.getTopLayer(0)?.rockType).toBe(RockType.IGN_BASALT);
    expect(stack.getTopLayer(100)?.rockType).toBe(RockType.IGN_GRANITE);
    expect(stack.size).toBe(2);
  });

  it('should merge oldest layers when exceeding MAX_LAYERS_PER_CELL', () => {
    for (let i = 0; i <= MAX_LAYERS_PER_CELL; i++) {
      stack.pushLayer(0, makeLayer({ thickness: 10 }));
    }
    const layers = stack.getLayers(0);
    expect(layers.length).toBeLessThanOrEqual(MAX_LAYERS_PER_CELL);
    // The bottom layer should have merged thickness
    expect(layers[0].thickness).toBe(20); // original 10 + merged 10
  });

  it('should initialize oceanic basement correctly', () => {
    stack.initializeBasement(0, true, -4000);
    const layers = stack.getLayers(0);
    expect(layers).toHaveLength(2);
    expect(layers[0].rockType).toBe(RockType.IGN_GABBRO);
    expect(layers[1].rockType).toBe(RockType.IGN_PILLOW_BASALT);
    expect(layers[0].thickness).toBe(4000);
    expect(layers[1].thickness).toBe(3000);
    expect(stack.getTotalThickness(0)).toBe(7000);
  });

  it('should initialize continental basement correctly', () => {
    stack.initializeBasement(0, false, -4000);
    const layers = stack.getLayers(0);
    expect(layers).toHaveLength(2);
    expect(layers[0].rockType).toBe(RockType.MET_GNEISS);
    expect(layers[1].rockType).toBe(RockType.IGN_GRANITE);
    expect(layers[0].thickness).toBe(15000);
    expect(layers[1].thickness).toBe(20000);
    expect(layers[0].deformation).toBe(DeformationType.METAMORPHOSED);
    expect(stack.getTotalThickness(0)).toBe(35000);
  });

  it('should apply deformation to all layers in a cell', () => {
    stack.pushLayer(0, makeLayer());
    stack.pushLayer(0, makeLayer());
    stack.applyDeformation(0, 15, 90, DeformationType.FOLDED);

    const layers = stack.getLayers(0);
    for (const l of layers) {
      expect(l.dipAngle).toBe(15);
      expect(l.dipDirection).toBe(90);
      expect(l.deformation).toBe(DeformationType.FOLDED);
    }
  });

  it('should clamp dip angle between 0 and 90', () => {
    stack.pushLayer(0, makeLayer({ dipAngle: 85 }));
    stack.applyDeformation(0, 10, 0, DeformationType.FOLDED);
    expect(stack.getTopLayer(0)?.dipAngle).toBe(90);

    stack.clear();
    stack.pushLayer(0, makeLayer({ dipAngle: 3 }));
    stack.applyDeformation(0, -10, 0, DeformationType.FOLDED);
    expect(stack.getTopLayer(0)?.dipAngle).toBe(0);
  });

  it('should erode from the top of the stack', () => {
    stack.pushLayer(0, makeLayer({ thickness: 500, rockType: RockType.SED_SANDSTONE }));
    stack.pushLayer(0, makeLayer({ thickness: 300, rockType: RockType.SED_SHALE }));

    const eroded = stack.erodeTop(0, 400);
    expect(eroded).toBe(400);
    const layers = stack.getLayers(0);
    expect(layers).toHaveLength(1);
    expect(layers[0].rockType).toBe(RockType.SED_SANDSTONE);
    expect(layers[0].thickness).toBe(400);
    expect(stack.getTotalThickness(0)).toBe(400);
  });

  it('should handle erosion exceeding stack thickness', () => {
    stack.pushLayer(0, makeLayer({ thickness: 200 }));
    const eroded = stack.erodeTop(0, 500);
    expect(eroded).toBe(200);
    expect(stack.getLayers(0)).toHaveLength(0);
    expect(stack.getTotalThickness(0)).toBe(0);
  });

  it('should handle erosion on empty stack', () => {
    const eroded = stack.erodeTop(99, 100);
    expect(eroded).toBe(0);
  });

  it('should clear all stacks', () => {
    stack.pushLayer(0, makeLayer());
    stack.pushLayer(1, makeLayer());
    stack.clear();
    expect(stack.size).toBe(0);
    expect(stack.getLayers(0)).toEqual([]);
  });

  it('should not upgrade deformation type to a lower one', () => {
    stack.pushLayer(0, makeLayer({ deformation: DeformationType.FOLDED }));
    stack.applyDeformation(0, 5, 0, DeformationType.UNDEFORMED);
    expect(stack.getTopLayer(0)?.deformation).toBe(DeformationType.FOLDED);
  });

  it('should make copies of layers on push (no aliasing)', () => {
    const layer = makeLayer();
    stack.pushLayer(0, layer);
    layer.thickness = 9999;
    expect(stack.getTopLayer(0)?.thickness).toBe(1000); // original, not mutated
  });
});
