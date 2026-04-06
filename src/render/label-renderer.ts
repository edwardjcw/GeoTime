// ─── Label Renderer ──────────────────────────────────────────────────────────
// Renders geographic feature name labels as <div> overlays on the 3D globe.
// All label data comes from the backend (/api/state/features/labels); this
// class only handles DOM positioning and culling.
//
// Culling rules (per frame, per label):
//   1. Back-hemisphere: projected.z > 1  → hidden (label is behind the globe)
//   2. Zoom: cameraDistance > label.zoomLevel → hidden (too far away to matter)

import * as THREE from 'three';
import type { FeatureLabel } from '../api/backend-client';

export class LabelRenderer {
  private readonly container: HTMLElement;
  private labels: FeatureLabel[] = [];
  private divPool: HTMLElement[] = [];
  private _visible = false;

  /** Scratch vector reused per frame to avoid allocations. */
  private readonly _pos = new THREE.Vector3();

  constructor(container: HTMLElement) {
    this.container = container;
    // Labels are off by default; the 'labels' layer toggle activates them.
    this.container.style.display = 'none';
  }

  /** Replace the current label set. Creates/reuses DOM elements as needed. */
  setLabels(labels: FeatureLabel[]): void {
    this.labels = labels;
    this._syncPool();
  }

  /** Show or hide all labels. */
  setVisible(visible: boolean): void {
    this._visible = visible;
    this.container.style.display = visible ? 'block' : 'none';
  }

  /** Returns true if labels are currently visible. */
  isVisible(): boolean {
    return this._visible;
  }

  /**
   * Update label DOM positions. Call once per render frame.
   *
   * @param camera          - Three.js perspective camera (from GlobeRenderer.getCamera())
   * @param width           - Viewport width in CSS pixels
   * @param height          - Viewport height in CSS pixels
   * @param cameraDistance  - Current camera distance from globe centre
   */
  update(
    camera: THREE.Camera,
    width: number,
    height: number,
    cameraDistance: number,
  ): void {
    if (!this._visible || width === 0 || height === 0) return;

    for (let i = 0; i < this.labels.length; i++) {
      const label = this.labels[i];
      const div = this.divPool[i];
      if (!div) continue;

      // Zoom culling — hide labels for features too small to matter at this distance.
      if (cameraDistance > label.zoomLevel) {
        div.style.display = 'none';
        continue;
      }

      // Convert spherical lat/lon to a unit-sphere Cartesian point.
      // Matches the globe mesh convention used in GlobeRenderer wind particles:
      //   x = cos(lat)·cos(lon), y = sin(lat), z = cos(lat)·sin(lon)
      const latRad = (label.centerLat * Math.PI) / 180;
      const lonRad = (label.centerLon * Math.PI) / 180;
      const cosLat = Math.cos(latRad);
      this._pos.set(
        cosLat * Math.cos(lonRad),
        Math.sin(latRad),
        cosLat * Math.sin(lonRad),
      );

      // Back-hemisphere culling: dot product of point (= outward normal on unit sphere)
      // with camera position. When dot < small threshold, the point faces away from camera.
      if (this._pos.dot(camera.position) < 0.05) {
        div.style.display = 'none';
        continue;
      }

      // Project to NDC [-1, 1] space.
      this._pos.project(camera);

      // Safety check: hide if the projected point is behind the clip plane.
      if (this._pos.z > 1) {
        div.style.display = 'none';
        continue;
      }

      // Convert NDC to CSS pixel position (top-left origin).
      const x = ((this._pos.x + 1) / 2) * width;
      const y = ((-this._pos.y + 1) / 2) * height;

      div.style.display = 'block';
      div.style.left = `${x}px`;
      div.style.top = `${y}px`;
    }
  }

  // ── Private helpers ─────────────────────────────────────────────────────────

  private _syncPool(): void {
    // Grow pool as needed.
    while (this.divPool.length < this.labels.length) {
      const div = document.createElement('div');
      Object.assign(div.style, {
        position: 'absolute',
        transform: 'translate(-50%, -50%)',
        pointerEvents: 'none',
        whiteSpace: 'nowrap',
        userSelect: 'none',
        fontSize: '11px',
        fontFamily: 'sans-serif',
        color: '#fff',
        textShadow: '0 0 3px #000, 0 0 3px #000',
        opacity: '0.85',
        display: 'none',
      });
      this.container.appendChild(div);
      this.divPool.push(div);
    }

    // Hide any excess pool entries beyond the current label count.
    for (let i = this.labels.length; i < this.divPool.length; i++) {
      this.divPool[i].style.display = 'none';
    }

    // Update text content and CSS class for each active label.
    for (let i = 0; i < this.labels.length; i++) {
      const label = this.labels[i];
      const div = this.divPool[i];
      div.textContent = label.name;
      div.className = `label-${label.type.toLowerCase().replace(/_/g, '-')}`;
    }
  }
}
