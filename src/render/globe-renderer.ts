// ─── Globe Renderer ─────────────────────────────────────────────────────────
// Three.js WebGL renderer for the GeoTime globe, using a custom ShaderMaterial
// to displace icosphere vertices by height and color them by elevation band.

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { createIcosphere } from './icosphere';

// ─── GLSL Shaders ───────────────────────────────────────────────────────────

const vertexShader = /* glsl */ `
uniform sampler2D uHeightMap;
uniform float uDisplacementScale;

varying float vHeight;
varying vec3 vNormal;
varying vec3 vWorldPos;
varying vec2 vUv;

void main() {
  // Sample height from equirectangular texture using UV
  float height = texture2D(uHeightMap, uv).r;
  vHeight = height;
  vUv = uv;

  // Displace vertex along normal
  vec3 displaced = position + normal * height * uDisplacementScale;

  vNormal = normalize(normalMatrix * normal);
  vec4 worldPos = modelMatrix * vec4(displaced, 1.0);
  vWorldPos = worldPos.xyz;

  gl_Position = projectionMatrix * viewMatrix * worldPos;
}
`;

const fragmentShader = /* glsl */ `
uniform vec3 uSunDirection;
uniform sampler2D uBiomeBase;
uniform float uBiomeBlend;

varying float vHeight;
varying vec3 vNormal;
varying vec3 vWorldPos;
varying vec2 vUv;

// Elevation thresholds (in meters, normalised by the data range)
// The heightMap stores raw meter values; we colour by those bands.
vec3 getTerrainColor(float h) {
  // Ocean: deep blue → shallow blue
  if (h < 0.0) {
    float t = clamp(h / -8000.0, 0.0, 1.0); // -8 000 m deepest
    return mix(vec3(0.05, 0.20, 0.55), vec3(0.01, 0.05, 0.20), t);
  }
  // Low land: green
  if (h < 2000.0) {
    float t = h / 2000.0;
    return mix(vec3(0.15, 0.55, 0.15), vec3(0.40, 0.55, 0.20), t);
  }
  // Highland: brown
  if (h < 5000.0) {
    float t = (h - 2000.0) / 3000.0;
    return mix(vec3(0.55, 0.40, 0.20), vec3(0.60, 0.55, 0.45), t);
  }
  // Mountain peaks: white
  float t = clamp((h - 5000.0) / 4000.0, 0.0, 1.0);
  return mix(vec3(0.60, 0.55, 0.45), vec3(1.0, 1.0, 1.0), t);
}

void main() {
  vec3 elevColor = getTerrainColor(vHeight);

  // Blend with biome base texture when available
  vec4 biomeCol = texture2D(uBiomeBase, vUv);
  // Only blend on land cells and when biome blend is active
  float blendFactor = uBiomeBlend * step(0.0, vHeight) * biomeCol.a;
  vec3 baseColor = mix(elevColor, biomeCol.rgb, blendFactor);

  // Simple directional (sun) + ambient lighting
  float NdotL = max(dot(vNormal, normalize(uSunDirection)), 0.0);
  vec3 ambient = 0.25 * baseColor;
  vec3 diffuse = 0.75 * baseColor * NdotL;

  gl_FragColor = vec4(ambient + diffuse, 1.0);
}
`;

// ─── Biome color shaders ────────────────────────────────────────────────────

// The biome overlay reads the same heightmap as the terrain so it displaces
// its vertices by the same amount, placing it exactly on the terrain surface
// (plus a tiny epsilon to prevent z-fighting).
const biomeVertexShader = /* glsl */ `
uniform sampler2D uHeightMap;
uniform float uDisplacementScale;

varying vec2 vUv;
void main() {
  vUv = uv;
  float height = texture2D(uHeightMap, uv).r;
  // Apply the same displacement as the terrain, plus a tiny offset so the
  // overlay always sits on top of the terrain surface.
  vec3 pos = position + normal * (height * uDisplacementScale + 0.002);
  gl_Position = projectionMatrix * modelViewMatrix * vec4(pos, 1.0);
}
`;

const biomeFragmentShader = /* glsl */ `
uniform sampler2D uBiomeMap;
varying vec2 vUv;

void main() {
  vec4 col = texture2D(uBiomeMap, vUv);
  if (col.a < 0.01) discard;
  gl_FragColor = col;
}
`;

const plateVertexShader = /* glsl */ `
varying vec2 vUv;
void main() {
  vUv = uv;
  // Slight offset outward to avoid z-fighting with the terrain mesh
  vec3 pos = position * 1.0005;
  gl_Position = projectionMatrix * modelViewMatrix * vec4(pos, 1.0);
}
`;

const plateFragmentShader = /* glsl */ `
uniform sampler2D uPlateMap;
varying vec2 vUv;

// Hash a float into a pseudo-random colour
vec3 plateColor(float id) {
  float r = fract(sin(id * 127.1) * 43758.5453);
  float g = fract(sin(id * 269.5) * 18347.6412);
  float b = fract(sin(id * 419.2) * 63725.1234);
  return vec3(r, g, b);
}

void main() {
  float id = texture2D(uPlateMap, vUv).r * 65535.0;
  if (id < 0.5) discard; // No plate
  vec3 col = plateColor(id);
  gl_FragColor = vec4(col, 0.35);
}
`;

// ─── Constants ──────────────────────────────────────────────────────────────

const SUBDIVISION_LEVEL = 5; // ~20 480 triangles
// Heights are stored in metres; 5e-6 gives ~4% visual exaggeration at 8 000 m peaks.
const DISPLACEMENT_SCALE = 5e-6;
const CAMERA_DISTANCE = 3.0;
// Camera distance at which we enter first-person mode (globe radius = 1.0)
const FIRST_PERSON_THRESHOLD = 1.05;

// ─── Biome Color Lookup (Whittaker diagram) ──────────────────────────────────

/**
 * Map temperature (°C) × precipitation (mm/yr equivalent) to RGB [0-255].
 * 12-biome Whittaker classification.
 */
function biomeColor(temp: number, precip: number): [number, number, number] {
  if (temp < -15) return [240, 248, 255]; // Ice / polar desert
  if (temp < -5 && precip < 200) return [200, 220, 240]; // Tundra
  if (temp < 5 && precip > 200) return [100, 130, 100];  // Taiga / boreal forest
  if (temp < 10 && precip < 300) return [180, 190, 150]; // Cold desert / steppe
  if (temp < 20 && precip > 600) return [ 60, 120,  50]; // Temperate rainforest
  if (temp < 20 && precip > 300) return [ 80, 150,  60]; // Temperate deciduous
  if (temp < 20 && precip < 300) return [200, 200, 120]; // Temperate grassland / shrubland
  if (temp >= 20 && precip < 200) return [240, 220, 130]; // Hot desert
  if (temp >= 20 && precip < 600) return [170, 200,  80]; // Savanna / dry woodland
  if (temp >= 20 && precip < 1500) return [ 50, 160,  60]; // Subtropical forest
  if (temp >= 20 && precip >= 1500) return [ 10,  80,  10]; // Tropical rainforest
  return [120, 160,  80]; // Default: subtropical
}

// ─── GlobeRenderer Class ────────────────────────────────────────────────────

export class GlobeRenderer {
  private renderer: THREE.WebGLRenderer;
  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private controls: OrbitControls;

  private globeMesh: THREE.Mesh;
  private globeMaterial: THREE.ShaderMaterial;
  private heightTexture: THREE.DataTexture;

  private plateMesh: THREE.Mesh | null = null;
  private plateMaterial: THREE.ShaderMaterial | null = null;
  private plateTexture: THREE.DataTexture | null = null;

  private biomeMesh: THREE.Mesh | null = null;
  private biomeMaterial: THREE.ShaderMaterial | null = null;
  private biomeTexture: THREE.DataTexture | null = null;

  private biomeBaseTexture: THREE.DataTexture | null = null;

  private triangleCount: number;
  private _isFirstPerson = false;
  private _cameraChangeCb: ((isFirstPerson: boolean) => void) | null = null;

  constructor(container: HTMLElement) {
    // ── Renderer ──────────────────────────────────────────────────────────
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(window.devicePixelRatio);
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    container.appendChild(this.renderer.domElement);

    // ── Scene ─────────────────────────────────────────────────────────────
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x000000);

    // ── Camera ────────────────────────────────────────────────────────────
    const aspect = container.clientWidth / container.clientHeight;
    // Near plane starts at 0.01 (orbital distance ~3) and is adjusted
    // dynamically in render() to 0.001 when in first-person surface mode.
    this.camera = new THREE.PerspectiveCamera(50, aspect, 0.01, 100);
    this.camera.position.set(0, 0, CAMERA_DISTANCE);

    // ── Controls (arcball with inertia) ───────────────────────────────────
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.minDistance = 1.001; // Allow zooming all the way to the surface
    this.controls.maxDistance = 10;

    // ── Lights ────────────────────────────────────────────────────────────
    const sun = new THREE.DirectionalLight(0xffffff, 1.0);
    sun.position.set(5, 3, 5);
    this.scene.add(sun);

    const ambient = new THREE.AmbientLight(0xffffff, 0.3);
    this.scene.add(ambient);

    // ── Height DataTexture (1×1 placeholder, replaced by updateHeightMap) ─
    this.heightTexture = new THREE.DataTexture(
      new Float32Array([0]),
      1,
      1,
      THREE.RedFormat,
      THREE.FloatType,
    );
    this.heightTexture.flipY = true;
    this.heightTexture.needsUpdate = true;

    // ── Biome base texture (1×1 transparent placeholder) ──────────────────
    this.biomeBaseTexture = new THREE.DataTexture(
      new Uint8Array([0, 0, 0, 0]),
      1,
      1,
      THREE.RGBAFormat,
      THREE.UnsignedByteType,
    );
    this.biomeBaseTexture.flipY = true;
    this.biomeBaseTexture.needsUpdate = true;

    // ── Globe ShaderMaterial ──────────────────────────────────────────────
    this.globeMaterial = new THREE.ShaderMaterial({
      uniforms: {
        uHeightMap: { value: this.heightTexture },
        uDisplacementScale: { value: DISPLACEMENT_SCALE },
        uSunDirection: { value: new THREE.Vector3(5, 3, 5).normalize() },
        uBiomeBase: { value: this.biomeBaseTexture },
        uBiomeBlend: { value: 0.0 },
      },
      vertexShader,
      fragmentShader,
    });

    // ── Icosphere geometry ────────────────────────────────────────────────
    const ico = createIcosphere(SUBDIVISION_LEVEL);
    this.triangleCount = ico.indices.length / 3;

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(ico.positions, 3));
    geometry.setAttribute('uv', new THREE.BufferAttribute(ico.uvs, 2));
    geometry.setIndex(new THREE.BufferAttribute(ico.indices, 1));
    geometry.computeVertexNormals();

    this.globeMesh = new THREE.Mesh(geometry, this.globeMaterial);
    this.scene.add(this.globeMesh);
  }

  // ── Public API ────────────────────────────────────────────────────────────

  /**
   * Upload a new height map to the GPU for vertex displacement and colouring.
   * @param heightData - Float32 array of elevation values in meters (row-major).
   * @param gridSize   - Width/height of the square grid.
   */
  updateHeightMap(heightData: Float32Array, gridSize: number): void {
    this.heightTexture.dispose();

    this.heightTexture = new THREE.DataTexture(
      heightData,
      gridSize,
      gridSize,
      THREE.RedFormat,
      THREE.FloatType,
    );
    this.heightTexture.magFilter = THREE.LinearFilter;
    this.heightTexture.minFilter = THREE.LinearFilter;
    this.heightTexture.flipY = true;
    this.heightTexture.needsUpdate = true;

    this.globeMaterial.uniforms.uHeightMap.value = this.heightTexture;

    // Keep the biome overlay in sync with the terrain displacement.
    if (this.biomeMaterial) {
      this.biomeMaterial.uniforms.uHeightMap.value = this.heightTexture;
    }
  }

  /**
   * Upload plate-ID data for an optional translucent overlay.
   * @param plateData - Uint16 plate IDs (row-major).
   * @param gridSize  - Width/height of the square grid.
   */
  updatePlateMap(plateData: Uint16Array, gridSize: number): void {
    // Convert Uint16 → Float32 for texture compatibility
    const floatData = new Float32Array(plateData.length);
    for (let i = 0; i < plateData.length; i++) {
      floatData[i] = plateData[i] / 65535;
    }

    if (this.plateTexture) this.plateTexture.dispose();

    this.plateTexture = new THREE.DataTexture(
      floatData,
      gridSize,
      gridSize,
      THREE.RedFormat,
      THREE.FloatType,
    );
    this.plateTexture.magFilter = THREE.NearestFilter;
    this.plateTexture.minFilter = THREE.NearestFilter;
    this.plateTexture.flipY = true;
    this.plateTexture.needsUpdate = true;

    if (!this.plateMesh) {
      this.plateMaterial = new THREE.ShaderMaterial({
        uniforms: {
          uPlateMap: { value: this.plateTexture },
        },
        vertexShader: plateVertexShader,
        fragmentShader: plateFragmentShader,
        transparent: true,
        depthWrite: false,
      });

      // Reuse globe geometry for the overlay (hidden by default)
      this.plateMesh = new THREE.Mesh(
        this.globeMesh.geometry,
        this.plateMaterial,
      );
      this.plateMesh.visible = false;
    } else {
      this.plateMaterial!.uniforms.uPlateMap.value = this.plateTexture;
    }
  }

  /** Handle window / container resize. */
  resize(width: number, height: number): void {
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height);
  }

  /** Render one frame (call inside a rAF loop). */
  render(): void {
    this.controls.update();

    // Detect first-person mode transition
    const dist = this.camera.position.length();
    const nowFP = dist < FIRST_PERSON_THRESHOLD;
    if (nowFP !== this._isFirstPerson) {
      this._isFirstPerson = nowFP;
      if (nowFP) {
        // Switch to wider FOV for ground-level perspective
        this.camera.fov = 75;
      } else {
        // Restore orbital FOV
        this.camera.fov = 50;
      }
      this._cameraChangeCb?.(nowFP);
    }

    // Dynamically adjust near clipping plane based on camera distance to avoid
    // depth-buffer precision issues at large distances while still allowing
    // close surface views.  At surface level (dist ≈ 1.0), use a very small
    // near plane; at orbit distance (dist ≥ 2), use a larger value.
    const near = nowFP ? 0.001 : Math.max(0.01, dist * 0.01);
    if (Math.abs(this.camera.near - near) > near * 0.1) {
      this.camera.near = near;
    }
    this.camera.updateProjectionMatrix();

    this.renderer.render(this.scene, this.camera);
  }

  /** Current triangle count of the globe mesh. */
  getTriangleCount(): number {
    return this.triangleCount;
  }

  /** Returns true when the camera is close enough for first-person perspective. */
  isFirstPersonMode(): boolean {
    return this._isFirstPerson;
  }

  /** Returns the current camera distance from the globe centre. */
  getCameraDistance(): number {
    return this.camera.position.length();
  }

  /** Register a callback for first-person mode transitions. */
  onCameraChange(cb: (isFirstPerson: boolean) => void): void {
    this._cameraChangeCb = cb;
  }

  /**
   * Upload pre-computed RGBA colour data as the colour overlay texture.
   * This is the low-level method; all per-layer overlays funnel through here.
   * @param rgba      - Uint8Array of RGBA bytes (row-major, same grid).
   * @param gridSize  - Width/height of the square grid.
   */
  updateColorMap(rgba: Uint8Array, gridSize: number): void {
    if (this.biomeTexture) this.biomeTexture.dispose();

    this.biomeTexture = new THREE.DataTexture(
      rgba,
      gridSize,
      gridSize,
      THREE.RGBAFormat,
      THREE.UnsignedByteType,
    );
    this.biomeTexture.magFilter = THREE.LinearFilter;
    this.biomeTexture.minFilter = THREE.LinearFilter;
    this.biomeTexture.flipY = true;
    this.biomeTexture.needsUpdate = true;

    if (!this.biomeMesh) {
      this.biomeMaterial = new THREE.ShaderMaterial({
        uniforms: {
          uBiomeMap: { value: this.biomeTexture },
          uHeightMap: { value: this.heightTexture },
          uDisplacementScale: { value: DISPLACEMENT_SCALE },
        },
        vertexShader: biomeVertexShader,
        fragmentShader: biomeFragmentShader,
        transparent: true,
        depthWrite: false,
      });

      this.biomeMesh = new THREE.Mesh(
        this.globeMesh.geometry,
        this.biomeMaterial,
      );
      this.biomeMesh.visible = false;
    } else {
      this.biomeMaterial!.uniforms.uBiomeMap.value = this.biomeTexture;
    }
  }

  /**
   * Build and upload a biome overlay texture from temperature and precipitation maps.
   * Uses the Whittaker biome classification (temperature × precipitation).
   * @param temperatureMap  - Float32 array of °C values (row-major, same grid).
   * @param precipitationMap - Float32 array of mm/yr equivalent values.
   * @param gridSize         - Width/height of the square grid.
   */
  updateClimateMap(
    temperatureMap: Float32Array,
    precipitationMap: Float32Array,
    gridSize: number,
  ): void {
    const cellCount = gridSize * gridSize;
    const rgba = new Uint8Array(cellCount * 4);

    for (let i = 0; i < cellCount; i++) {
      const [r, g, b] = biomeColor(temperatureMap[i], precipitationMap[i]);
      rgba[i * 4 + 0] = r;
      rgba[i * 4 + 1] = g;
      rgba[i * 4 + 2] = b;
      rgba[i * 4 + 3] = 180;
    }

    this.updateColorMap(rgba, gridSize);
  }

  /**
   * Set a biome-influenced base texture that blends into the default terrain view.
   * This makes the default (non-overlay) view show biome variation such as deserts,
   * forests, tundra etc. instead of plain elevation bands.
   * @param temperatureMap  - Float32 array of °C values.
   * @param precipitationMap - Float32 array of mm/yr equivalent values.
   * @param heightMap       - Float32 array of elevation values in meters.
   * @param gridSize        - Width/height of the square grid.
   */
  updateBiomeBaseMap(
    temperatureMap: Float32Array,
    precipitationMap: Float32Array,
    heightMap: Float32Array,
    gridSize: number,
  ): void {
    const cellCount = gridSize * gridSize;
    const rgba = new Uint8Array(cellCount * 4);

    for (let i = 0; i < cellCount; i++) {
      const h = heightMap[i];
      if (h < 0) {
        // Ocean: no biome color blending (keep elevation shader for oceans)
        rgba[i * 4 + 3] = 0;
        continue;
      }
      const [r, g, b] = biomeColor(temperatureMap[i], precipitationMap[i]);

      // Add snow on high peaks based on temperature (below -5°C)
      const temp = temperatureMap[i];
      const snowFactor = temp < -5 ? Math.min(1, (-5 - temp) / 10) : 0;
      rgba[i * 4 + 0] = Math.round(r + (255 - r) * snowFactor);
      rgba[i * 4 + 1] = Math.round(g + (255 - g) * snowFactor);
      rgba[i * 4 + 2] = Math.round(b + (255 - b) * snowFactor);
      rgba[i * 4 + 3] = 200; // Strong blend on land
    }

    if (this.biomeBaseTexture) this.biomeBaseTexture.dispose();

    this.biomeBaseTexture = new THREE.DataTexture(
      rgba,
      gridSize,
      gridSize,
      THREE.RGBAFormat,
      THREE.UnsignedByteType,
    );
    this.biomeBaseTexture.magFilter = THREE.LinearFilter;
    this.biomeBaseTexture.minFilter = THREE.LinearFilter;
    this.biomeBaseTexture.flipY = true;
    this.biomeBaseTexture.needsUpdate = true;

    this.globeMaterial.uniforms.uBiomeBase.value = this.biomeBaseTexture;
    this.globeMaterial.uniforms.uBiomeBlend.value = 0.75; // 75% biome blend on land
  }

  /** Show or hide the plate overlay. */
  setPlateOverlayVisible(visible: boolean): void {
    if (this.plateMesh) {
      this.plateMesh.visible = visible;
      if (visible && !this.plateMesh.parent) {
        this.scene.add(this.plateMesh);
      }
    }
  }

  /** Show or hide the biome/climate overlay. */
  setBiomeOverlayVisible(visible: boolean): void {
    if (this.biomeMesh) {
      this.biomeMesh.visible = visible;
      if (visible && !this.biomeMesh.parent) {
        this.scene.add(this.biomeMesh);
      }
    }
  }

  /**
   * Raycast from screen coordinates to the globe surface and return lat/lon.
   * @param x - Screen X coordinate relative to container.
   * @param y - Screen Y coordinate relative to container.
   * @param containerWidth  - Container pixel width.
   * @param containerHeight - Container pixel height.
   * @returns { lat, lon } in degrees or null if the ray misses the globe.
   */
  screenToLatLon(
    x: number,
    y: number,
    containerWidth: number,
    containerHeight: number,
  ): { lat: number; lon: number } | null {
    const ndc = new THREE.Vector2(
      (x / containerWidth) * 2 - 1,
      -(y / containerHeight) * 2 + 1,
    );
    const raycaster = new THREE.Raycaster();
    raycaster.setFromCamera(ndc, this.camera);
    const hits = raycaster.intersectObject(this.globeMesh);
    if (hits.length === 0) return null;

    const point = hits[0].point;
    const r = point.length();
    const lat = Math.asin(Math.max(-1, Math.min(1, point.y / r))) * (180 / Math.PI);
    const lon = Math.atan2(point.z, point.x) * (180 / Math.PI);
    return { lat, lon };
  }

  /** Release all GPU resources. */
  dispose(): void {
    this.controls.dispose();

    this.heightTexture.dispose();
    this.globeMaterial.dispose();
    this.globeMesh.geometry.dispose();
    this.scene.remove(this.globeMesh);

    if (this.plateMesh) {
      this.plateTexture?.dispose();
      this.plateMaterial?.dispose();
      this.plateMesh.geometry.dispose();
      this.scene.remove(this.plateMesh);
    }

    if (this.biomeMesh) {
      this.biomeTexture?.dispose();
      this.biomeMaterial?.dispose();
      this.scene.remove(this.biomeMesh);
    }

    if (this.biomeBaseTexture) {
      this.biomeBaseTexture.dispose();
    }

    this.renderer.dispose();
    this.renderer.domElement.remove();
  }
}

