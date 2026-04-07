// @vitest-environment jsdom
// Unit tests for LabelRenderer — Phase L5 geographic feature label overlay.
// Tests focus on the two culling rules:
//   1. Back-hemisphere culling: labels with projected.z > 1 are hidden.
//   2. Zoom culling: labels where cameraDistance > label.zoomLevel are hidden.

import { describe, it, expect, beforeEach } from 'vitest';
import * as THREE from 'three';
import { LabelRenderer } from '../src/render/label-renderer';
import type { FeatureLabel } from '../src/api/backend-client';

// ── Helpers ──────────────────────────────────────────────────────────────────

function makeLabel(overrides: Partial<FeatureLabel> = {}): FeatureLabel {
  return {
    id: 'test-id',
    name: 'Test Feature',
    type: 'Continent',
    centerLat: 0,
    centerLon: 0,
    zoomLevel: 4.0,
    status: 'Active',
    formerNames: [],
    ...overrides,
  };
}

/** Build a simple perspective camera looking down −Z. */
function makeCamera(): THREE.PerspectiveCamera {
  const cam = new THREE.PerspectiveCamera(50, 1, 0.01, 100);
  cam.position.set(0, 0, 3);
  cam.lookAt(0, 0, 0);
  cam.updateProjectionMatrix();
  cam.updateMatrixWorld(true);
  return cam;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('LabelRenderer', () => {
  let container: HTMLElement;
  let renderer: LabelRenderer;
  let camera: THREE.PerspectiveCamera;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    renderer = new LabelRenderer(container);
    camera = makeCamera();
  });

  it('should create a div for each label', () => {
    renderer.setLabels([makeLabel(), makeLabel({ id: 'b', name: 'B' })]);
    const divs = container.querySelectorAll('div');
    expect(divs.length).toBe(2);
  });

  it('should set label text content from the name field', () => {
    renderer.setLabels([makeLabel({ name: 'Velundra Ocean' })]);
    renderer.update(camera, 800, 600, 3.0);
    const divs = Array.from(container.querySelectorAll('div'));
    expect(divs.some((d) => d.textContent === 'Velundra Ocean')).toBe(true);
  });

  it('should apply a CSS class based on feature type', () => {
    renderer.setLabels([makeLabel({ type: 'MountainRange' })]);
    renderer.update(camera, 800, 600, 1.5);
    const div = container.querySelector('div');
    expect(div?.className).toContain('label-mountainrange');
  });

  it('should hide all labels when setVisible(false) is called', () => {
    renderer.setLabels([makeLabel()]);
    renderer.setVisible(false);
    expect(container.style.display).toBe('none');
  });

  it('should show labels when setVisible(true) is called', () => {
    renderer.setLabels([makeLabel()]);
    renderer.setVisible(false);
    renderer.setVisible(true);
    expect(container.style.display).toBe('block');
  });

  it('should not update positions when not visible', () => {
    renderer.setLabels([makeLabel()]);
    renderer.setVisible(false);
    renderer.update(camera, 800, 600, 1.0);
    // Container hidden — no display styles set on inner divs
    const div = container.querySelector('div');
    // The inner div should still be display:none (set by _syncPool) since we skipped update
    expect(div?.style.display).toBe('none');
  });

  // ── Zoom culling ────────────────────────────────────────────────────────────

  it('should hide a label when cameraDistance exceeds zoomLevel', () => {
    // Place at front hemisphere so it's not back-culled, but zoom-culled
    const label = makeLabel({ centerLat: 0, centerLon: 90, zoomLevel: 2.0 });
    renderer.setLabels([label]);
    renderer.setVisible(true);
    // Camera at distance 3.0 > zoomLevel 2.0 → hidden by zoom culling
    renderer.update(camera, 800, 600, 3.0);
    const div = container.querySelector('div') as HTMLElement;
    expect(div.style.display).toBe('none');
  });

  it('should show a label when cameraDistance is within zoomLevel', () => {
    // Place label at the front of the globe facing the camera (lon=90 in this convention → (0,0,1) on globe)
    const label = makeLabel({ centerLat: 0, centerLon: 90, zoomLevel: 4.0 });
    renderer.setLabels([label]);
    renderer.setVisible(true);
    // Camera at distance 3.0 ≤ zoomLevel 4.0 → should not be culled by zoom
    renderer.update(camera, 800, 600, 3.0);
    const div = container.querySelector('div') as HTMLElement;
    expect(div.style.display).toBe('block');
  });

  // ── Back-hemisphere culling ─────────────────────────────────────────────────

  it('should hide a label on the back hemisphere', () => {
    // lon=270 (i.e. -90°) → 3D point (0, 0, -1), facing away from camera at (0,0,3).
    // dot((0,0,-1), (0,0,3)) = -3 < 0.05 → hidden.
    const label = makeLabel({ centerLat: 0, centerLon: 270, zoomLevel: 10.0 });
    renderer.setLabels([label]);
    renderer.setVisible(true);
    renderer.update(camera, 800, 600, 3.0);
    const div = container.querySelector('div') as HTMLElement;
    expect(div.style.display).toBe('none');
  });

  it('should show a label on the front hemisphere', () => {
    // lon=90 → 3D point (0, 0, 1), facing directly toward camera at (0,0,3).
    // dot((0,0,1), (0,0,3)) = 3 > 0.05 → visible.
    const label = makeLabel({ centerLat: 0, centerLon: 90, zoomLevel: 10.0 });
    renderer.setLabels([label]);
    renderer.setVisible(true);
    renderer.update(camera, 800, 600, 3.0);
    const div = container.querySelector('div') as HTMLElement;
    expect(div.style.display).toBe('block');
  });

  it('should update pool size when labels change', () => {
    renderer.setLabels([
      makeLabel({ id: 'a', name: 'Alpha' }),
      makeLabel({ id: 'b', name: 'Beta' }),
      makeLabel({ id: 'c', name: 'Gamma' }),
    ]);
    expect(container.querySelectorAll('div').length).toBe(3);

    // Shrink to 1 label — pool keeps 3 DOM nodes for reuse
    renderer.setLabels([makeLabel({ id: 'x', name: 'Xray' })]);
    const divs = Array.from(container.querySelectorAll('div'));
    // DOM pool is not shrunk
    expect(divs.length).toBe(3);
    // First div text is updated to the new single label
    expect(divs[0].textContent).toBe('Xray');
  });

  // ── Former names display ──────────────────────────────────────────────────

  it('should display former name as subtitle when formerNames is non-empty', () => {
    const label = makeLabel({
      name: 'North Pangea',
      formerNames: ['Pangea'],
    });
    renderer.setLabels([label]);
    const div = container.querySelector('div') as HTMLElement;
    const formerSpan = div.querySelector('.label-former-name');
    expect(formerSpan).not.toBeNull();
    expect(formerSpan?.textContent).toBe('(formerly Pangea)');
  });

  it('should show most recent former name as subtitle', () => {
    const label = makeLabel({
      name: 'Greater Gondwana',
      formerNames: ['Pangea', 'Gondwana'],
    });
    renderer.setLabels([label]);
    const div = container.querySelector('div') as HTMLElement;
    const formerSpan = div.querySelector('.label-former-name');
    expect(formerSpan?.textContent).toBe('(formerly Gondwana)');
  });

  it('should set title attribute with full former-name lineage', () => {
    const label = makeLabel({
      name: 'North Gondwana',
      formerNames: ['Pangea', 'Gondwana'],
    });
    renderer.setLabels([label]);
    const div = container.querySelector('div') as HTMLElement;
    expect(div.title).toBe('Former names: Pangea → Gondwana');
  });

  it('should not show former names when formerNames is empty', () => {
    const label = makeLabel({ name: 'Solara', formerNames: [] });
    renderer.setLabels([label]);
    const div = container.querySelector('div') as HTMLElement;
    expect(div.querySelector('.label-former-name')).toBeNull();
    expect(div.textContent).toBe('Solara');
    expect(div.title).toBe('');
  });
});
