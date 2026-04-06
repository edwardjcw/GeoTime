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

// ─── Event-layer overlay shaders (Phase D6) ──────────────────────────────────

const eventLayerVertexShader = /* glsl */ `
uniform sampler2D uHeightMap;
uniform float uDisplacementScale;
varying vec2 vUv;
void main() {
  vUv = uv;
  float height = texture2D(uHeightMap, uv).r;
  vec3 pos = position + normal * (height * uDisplacementScale + 0.003);
  gl_Position = projectionMatrix * modelViewMatrix * vec4(pos, 1.0);
}
`;

const eventLayerFragmentShader = /* glsl */ `
uniform sampler2D uEventMap;
varying vec2 vUv;
void main() {
  vec4 col = texture2D(uEventMap, vUv);
  if (col.a < 0.01) discard;
  gl_FragColor = col;
}
`;

/** RGB colour (0-255) per event type for the heatmap overlay. */
const EVENT_LAYER_PALETTE: Record<string, [number, number, number]> = {
  ImpactEjecta:          [255, 140,  40],
  VolcanicAsh:           [200, 200, 200],
  VolcanicSoot:          [ 80,  80,  80],
  GammaRayBurst:         [ 60, 220, 255],
  OceanAnoxicEvent:      [ 20,  40,  20],
  SnowballGlacial:       [200, 230, 255],
  IronFormation:         [180,  80,  40],
  MeteoriticIron:        [120,  60, 160],
  MassExtinction:        [220,  40,  40],
  CarbonIsotopeExcursion:[40,  200, 100],
  default:               [200, 200,  80],
};

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

  // Event-layer overlay (Phase D6)
  private eventLayerMesh: THREE.Mesh | null = null;
  private eventLayerMaterial: THREE.ShaderMaterial | null = null;
  private eventLayerTexture: THREE.DataTexture | null = null;

  private biomeBaseTexture: THREE.DataTexture | null = null;

  private triangleCount: number;
  private _isFirstPerson = false;
  private _cameraChangeCb: ((isFirstPerson: boolean) => void) | null = null;

  // ── Water mesh ───────────────────────────────────────────────────────────
  private _waterMesh: THREE.Mesh | null = null;

  // ── First-person controls state ──────────────────────────────────────────
  private _fpKeys = new Set<string>();
  private _fpLat = 0;
  private _fpLon = 0;
  private _fpYaw = 0;
  private _fpPitch = 0;
  private _fpPointerLocked = false;
  private _fpClickHandler: (() => void) | null = null;
  private _fpPLChangeHandler: (() => void) | null = null;
  private _fpMouseHandler: ((e: MouseEvent) => void) | null = null;
  private _fpKeyDownHandler: ((e: KeyboardEvent) => void) | null = null;
  private _fpKeyUpHandler: ((e: KeyboardEvent) => void) | null = null;

  // ── Wind animation overlay ────────────────────────────────────────────────
  private _windCanvas: HTMLCanvasElement | null = null;
  private _windCtx: CanvasRenderingContext2D | null = null;
  private _windU: Float32Array | null = null;
  private _windV: Float32Array | null = null;
  private _windGridSize = 512;
  private _windParticles: Array<{ lat: number; lon: number; age: number; speed: number }> = [];
  private _windAnimActive = false;
  /** Scratch vector to avoid allocations in hot animation path. */
  private _windScratch = new THREE.Vector3();

  // ── First-person terrain height map (CPU copy for camera positioning) ────
  private _fpHeightMap: Float32Array | null = null;
  private _fpHeightGridSize = 512;

  constructor(container: HTMLElement) {
    // ── Renderer ──────────────────────────────────────────────────────────
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
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

    // ── Ocean water sphere (sits at sea level = radius 1.0) ───────────────
    // Semi-transparent sphere that visually covers all below-sea-level cells.
    const waterGeometry = new THREE.SphereGeometry(1.0, 32, 16);
    const waterMaterial = new THREE.MeshPhongMaterial({
      color: 0x006994,
      transparent: true,
      opacity: 0.72,
      side: THREE.FrontSide,
      depthWrite: false,
      shininess: 100,
      specular: new THREE.Color(0x334466),
    });
    const waterMesh = new THREE.Mesh(waterGeometry, waterMaterial);
    this.scene.add(waterMesh);
    this._waterMesh = waterMesh;
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

    // Save a CPU-side reference for first-person terrain height sampling.
    // The DataTexture keeps this array alive, so saving the same reference is safe.
    this._fpHeightMap = heightData;
    this._fpHeightGridSize = gridSize;
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
    if (this._windCanvas) {
      this._windCanvas.width = width;
      this._windCanvas.height = height;
    }
  }

  /** Render one frame (call inside a rAF loop). */
  render(): void {
    // Skip OrbitControls update when in first-person mode (we drive the camera directly)
    if (!this._isFirstPerson) {
      this.controls.update();
    } else {
      this._applyFirstPersonMovement();
    }

    // Detect first-person mode transition
    const dist = this.camera.position.length();
    const nowFP = dist < FIRST_PERSON_THRESHOLD;
    if (nowFP !== this._isFirstPerson) {
      this._isFirstPerson = nowFP;
      if (nowFP) {
        // Switch to wider FOV for ground-level perspective
        this.camera.fov = 75;
        this.enableFirstPersonControls();
      } else {
        // Restore orbital FOV
        this.camera.fov = 50;
        this.disableFirstPersonControls();
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

    // Wind particle animation overlay (runs after WebGL render so particles sit on top)
    if (this._windAnimActive) {
      this._animateWindParticles();
    }
  }

  /** Returns true when wind particle animation is running. */
  isWindAnimationActive(): boolean {
    return this._windAnimActive;
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

  /** Returns the Three.js perspective camera for external projection use (e.g. label renderer). */
  getCamera(): THREE.PerspectiveCamera {
    return this.camera;
  }

  /**
   * Return the WebGL renderer name reported by the browser for the front-end GPU.
   * Uses the WEBGL_debug_renderer_info extension when available; falls back to a generic label.
   */
  getWebGLRendererInfo(): string {
    const gl = this.renderer.getContext();
    const ext = gl.getExtension('WEBGL_debug_renderer_info');
    if (ext) {
      return gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) as string;
    }
    return 'WebGL (info unavailable)';
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
   * Upload a scalar float[] event-layer thickness map and render it as a
   * heatmap overlay on the globe.  Call `setEventLayerVisible(true)` to show it.
   * @param values     - One thickness value per grid cell (row-major).
   * @param gridSize   - Width/height of the square grid.
   * @param eventType  - EventType name used to pick the colour palette.
   */
  updateEventLayerMap(values: number[], gridSize: number, eventType: string): void {
    const cellCount = gridSize * gridSize;
    const rgba = new Uint8Array(cellCount * 4);

    // Find max thickness for normalisation
    let maxVal = 0;
    for (let i = 0; i < cellCount; i++) {
      if (values[i] > maxVal) maxVal = values[i];
    }
    if (maxVal === 0) maxVal = 1;

    // Pick colour palette by event type
    const palette = EVENT_LAYER_PALETTE[eventType] ?? EVENT_LAYER_PALETTE['default'];

    for (let i = 0; i < cellCount; i++) {
      const t = Math.min(1, values[i] / maxVal);
      if (t < 0.001) {
        rgba[i * 4 + 3] = 0; // transparent where no layer
        continue;
      }
      rgba[i * 4 + 0] = Math.round(palette[0] * t);
      rgba[i * 4 + 1] = Math.round(palette[1] * t);
      rgba[i * 4 + 2] = Math.round(palette[2] * t);
      rgba[i * 4 + 3] = Math.round(180 * t);
    }

    if (this.eventLayerTexture) this.eventLayerTexture.dispose();
    this.eventLayerTexture = new THREE.DataTexture(rgba, gridSize, gridSize, THREE.RGBAFormat, THREE.UnsignedByteType);
    this.eventLayerTexture.magFilter = THREE.LinearFilter;
    this.eventLayerTexture.minFilter = THREE.LinearFilter;
    this.eventLayerTexture.flipY = true;
    this.eventLayerTexture.needsUpdate = true;

    if (!this.eventLayerMesh) {
      this.eventLayerMaterial = new THREE.ShaderMaterial({
        uniforms: {
          uEventMap:          { value: this.eventLayerTexture },
          uHeightMap:         { value: this.heightTexture },
          uDisplacementScale: { value: DISPLACEMENT_SCALE },
        },
        vertexShader: eventLayerVertexShader,
        fragmentShader: eventLayerFragmentShader,
        transparent: true,
        depthWrite: false,
      });
      this.eventLayerMesh = new THREE.Mesh(this.globeMesh.geometry, this.eventLayerMaterial);
      this.eventLayerMesh.visible = false;
    } else {
      this.eventLayerMaterial!.uniforms.uEventMap.value = this.eventLayerTexture;
    }
  }

  /** Show or hide the event-layer overlay. */
  setEventLayerVisible(visible: boolean): void {
    if (this.eventLayerMesh) {
      this.eventLayerMesh.visible = visible;
      if (visible && !this.eventLayerMesh.parent) {
        this.scene.add(this.eventLayerMesh);
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
    this.disableFirstPersonControls();
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

    if (this._waterMesh) {
      (this._waterMesh.material as THREE.MeshPhongMaterial).dispose();
      this._waterMesh.geometry.dispose();
      this.scene.remove(this._waterMesh);
    }

    if (this._windCanvas) {
      this._windCanvas.remove();
      this._windCanvas = null;
      this._windCtx = null;
    }

    this.renderer.dispose();
    this.renderer.domElement.remove();
  }

  // ── First-person controls ─────────────────────────────────────────────────

  private enableFirstPersonControls(): void {
    const canvas = this.renderer.domElement;

    // Derive lat/lon from current camera position
    const pos = this.camera.position;
    const r = pos.length();
    this._fpLat = Math.asin(Math.max(-1, Math.min(1, pos.y / r))) * (180 / Math.PI);
    this._fpLon = Math.atan2(pos.z, pos.x) * (180 / Math.PI);
    this._fpYaw = 0;
    this._fpPitch = 0;

    this.controls.enabled = false;

    this._fpClickHandler = () => {
      if (!this._fpPointerLocked) canvas.requestPointerLock();
    };
    canvas.addEventListener('click', this._fpClickHandler);

    this._fpPLChangeHandler = () => {
      this._fpPointerLocked = document.pointerLockElement === canvas;
    };
    document.addEventListener('pointerlockchange', this._fpPLChangeHandler);

    this._fpMouseHandler = (e: MouseEvent) => {
      if (this._fpPointerLocked) {
        this._fpYaw += e.movementX * 0.15;
        this._fpPitch = Math.max(-80, Math.min(80, this._fpPitch - e.movementY * 0.15));
      }
    };
    document.addEventListener('mousemove', this._fpMouseHandler);

    this._fpKeyDownHandler = (e: KeyboardEvent) => { this._fpKeys.add(e.code); };
    this._fpKeyUpHandler = (e: KeyboardEvent) => { this._fpKeys.delete(e.code); };
    document.addEventListener('keydown', this._fpKeyDownHandler);
    document.addEventListener('keyup', this._fpKeyUpHandler);
  }

  private disableFirstPersonControls(): void {
    const canvas = this.renderer.domElement;

    this.controls.enabled = true;
    this._fpPointerLocked = false;
    this._fpKeys.clear();

    if (this._fpClickHandler) {
      canvas.removeEventListener('click', this._fpClickHandler);
      this._fpClickHandler = null;
    }
    if (this._fpPLChangeHandler) {
      document.removeEventListener('pointerlockchange', this._fpPLChangeHandler);
      this._fpPLChangeHandler = null;
    }
    if (this._fpMouseHandler) {
      document.removeEventListener('mousemove', this._fpMouseHandler);
      this._fpMouseHandler = null;
    }
    if (this._fpKeyDownHandler) {
      document.removeEventListener('keydown', this._fpKeyDownHandler);
      this._fpKeyDownHandler = null;
    }
    if (this._fpKeyUpHandler) {
      document.removeEventListener('keyup', this._fpKeyUpHandler);
      this._fpKeyUpHandler = null;
    }

    if (document.pointerLockElement === canvas) document.exitPointerLock();

    // Reset camera orientation to look at globe center
    this.camera.up.set(0, 1, 0);
    this.camera.lookAt(0, 0, 0);
    this.controls.target.set(0, 0, 0);
  }

  private _applyFirstPersonMovement(): void {
    // Press Escape to exit first-person mode by pulling camera back
    if (this._fpKeys.has('Escape')) {
      const dir = this.camera.position.clone().normalize();
      this.camera.position.copy(dir.multiplyScalar(1.5));
      this._fpKeys.clear();
      return;
    }

    const SPEED_DEG = 0.05; // movement speed in degrees per frame
    const yawRad = this._fpYaw * (Math.PI / 180);
    const latRad = this._fpLat * (Math.PI / 180);
    const cosLat = Math.max(Math.abs(Math.cos(latRad)), 0.01);

    let dlat = 0;
    let dlonNorm = 0;

    if (this._fpKeys.has('KeyW') || this._fpKeys.has('ArrowUp')) {
      dlat += Math.cos(yawRad); dlonNorm += Math.sin(yawRad);
    }
    if (this._fpKeys.has('KeyS') || this._fpKeys.has('ArrowDown')) {
      dlat -= Math.cos(yawRad); dlonNorm -= Math.sin(yawRad);
    }
    if (this._fpKeys.has('KeyA') || this._fpKeys.has('ArrowLeft')) {
      dlat += Math.sin(yawRad); dlonNorm -= Math.cos(yawRad);
    }
    if (this._fpKeys.has('KeyD') || this._fpKeys.has('ArrowRight')) {
      dlat -= Math.sin(yawRad); dlonNorm += Math.cos(yawRad);
    }

    if (dlat !== 0 || dlonNorm !== 0) {
      this._fpLat = Math.max(-89, Math.min(89, this._fpLat + dlat * SPEED_DEG));
      this._fpLon += (dlonNorm * SPEED_DEG) / cosLat;
      this._fpLon = ((this._fpLon + 180) % 360 + 360) % 360 - 180;
    }

    // Position camera 6 feet (1.8288 m) above globe surface at (lat, lon),
    // taking terrain elevation into account.  For ocean cells (h < 0), the
    // surface is treated as sea level (0 m) so the user "walks on water".
    const latR = this._fpLat * (Math.PI / 180);
    const lonR = this._fpLon * (Math.PI / 180);

    let terrainH = 0;
    if (this._fpHeightMap) {
      const gs = this._fpHeightGridSize;
      const row = Math.max(0, Math.min(gs - 1, Math.round((90 - this._fpLat) / 180 * gs)));
      const col = ((Math.round(((this._fpLon + 180) / 360) * gs)) % gs + gs) % gs;
      terrainH = Math.max(0, this._fpHeightMap[row * gs + col]); // ocean → 0 m
    }

    const PERSON_HEIGHT_M = 1.8288; // 6 feet in meters
    const camR = 1.0 + (terrainH + PERSON_HEIGHT_M) * DISPLACEMENT_SCALE;
    const cx = camR * Math.cos(latR) * Math.cos(lonR);
    const cy = camR * Math.sin(latR);
    const cz = camR * Math.cos(latR) * Math.sin(lonR);
    this.camera.position.set(cx, cy, cz);

    // Surface coordinate frame
    const up = new THREE.Vector3(cx, cy, cz).normalize();
    const north = new THREE.Vector3(
      -Math.sin(latR) * Math.cos(lonR),
      Math.cos(latR),
      -Math.sin(latR) * Math.sin(lonR),
    );
    const east = new THREE.Vector3(-Math.sin(lonR), 0, Math.cos(lonR));

    // Compute look direction from yaw (horizontal) and pitch (vertical)
    const pitchR = this._fpPitch * (Math.PI / 180);
    const lookDir = new THREE.Vector3()
      .addScaledVector(north, Math.cos(pitchR) * Math.cos(yawRad))
      .addScaledVector(east, Math.cos(pitchR) * Math.sin(yawRad))
      .addScaledVector(up, Math.sin(pitchR));

    this.camera.up.copy(up);
    this.camera.lookAt(cx + lookDir.x, cy + lookDir.y, cz + lookDir.z);
  }

  // ── Wind animation ────────────────────────────────────────────────────────

  /**
   * Start animated wind-particle overlay using pre-fetched wind vectors.
   * Particles trace wind streamlines across the globe surface.
   * @param windU  - Zonal wind component per cell (westward-positive, m/s-equivalent).
   * @param windV  - Meridional wind component per cell (northward-positive).
   * @param gridSize - Width/height of the square grid.
   */
  startWindAnimation(windU: number[], windV: number[], gridSize: number): void {
    this._windU = new Float32Array(windU);
    this._windV = new Float32Array(windV);
    this._windGridSize = gridSize;

    // Lazy-create the canvas overlay
    if (!this._windCanvas) {
      const domEl = this.renderer.domElement;
      const container = domEl.parentElement;
      if (!container) return;
      this._windCanvas = document.createElement('canvas');
      this._windCanvas.style.cssText = 'position:absolute;top:0;left:0;pointer-events:none;';
      this._windCanvas.width = domEl.clientWidth || domEl.width;
      this._windCanvas.height = domEl.clientHeight || domEl.height;
      // Insert after the WebGL canvas so it sits on top
      container.insertBefore(this._windCanvas, domEl.nextSibling);
      this._windCtx = this._windCanvas.getContext('2d');
    }

    // Spawn particles spread across the globe
    const PARTICLE_COUNT = 2500;
    this._windParticles = Array.from({ length: PARTICLE_COUNT }, () => ({
      lat: Math.random() * 178 - 89,
      lon: Math.random() * 360 - 180,
      age: Math.random(),
      speed: 0.7 + Math.random() * 0.6,
    }));

    this._windAnimActive = true;
  }

  /** Stop and hide the wind animation overlay. */
  stopWindAnimation(): void {
    this._windAnimActive = false;
    this._windParticles = [];
    if (this._windCanvas && this._windCtx) {
      this._windCtx.clearRect(0, 0, this._windCanvas.width, this._windCanvas.height);
    }
  }

  /** Sample the wind vector (U eastward, V northward) at a lat/lon position. */
  private _sampleWindAt(lat: number, lon: number): { u: number; v: number } {
    if (!this._windU || !this._windV) return { u: 0, v: 0 };
    const gs = this._windGridSize;
    const col = Math.round(((lon + 180) / 360) * gs) % gs;
    const row = Math.max(0, Math.min(gs - 1, Math.round(((90 - lat) / 180) * gs)));
    const idx = row * gs + col;
    // Backend windU = westward-positive; we want eastward-positive for standard convention
    return { u: -this._windU[idx], v: this._windV[idx] };
  }

  /**
   * Per-frame wind particle update.  Called at the end of render().
   * Each particle traces the wind field; speed is artificially amplified for
   * visual effect.  Particles that age out are respawned at random positions.
   */
  private _animateWindParticles(): void {
    if (!this._windCtx || !this._windCanvas) return;

    const ctx = this._windCtx;
    const W = this._windCanvas.width;
    const H = this._windCanvas.height;

    // Soft fade of previous frame trails: use destination-out so existing pixels
    // lose alpha rather than accumulating opaque black.  This keeps the WebGL
    // terrain visible through the wind canvas at all times.
    ctx.globalCompositeOperation = 'destination-out';
    ctx.fillStyle = 'rgba(0,0,0,0.06)';
    ctx.fillRect(0, 0, W, H);
    ctx.globalCompositeOperation = 'source-over';

    // Visual amplification: a wind of 10 units should cross ~30° in ~6s (at 60fps)
    // 30° / (6s × 60fps) = 0.083°/frame → SCALE = 0.083 / 10 ≈ 0.008
    const SCALE = 0.01;
    const DEG2RAD = Math.PI / 180;

    const camPos = this.camera.position;

    for (const p of this._windParticles) {
      const { u, v } = this._sampleWindAt(p.lat, p.lon);
      const windSpeed = Math.sqrt(u * u + v * v);

      const prevLat = p.lat;
      const prevLon = p.lon;

      // Move particle along wind vector (account for cosLat compression on longitude)
      const cosLat = Math.max(0.02, Math.cos(p.lat * DEG2RAD));
      p.lat = Math.max(-89, Math.min(89, p.lat + v * SCALE * p.speed));
      p.lon += (u / cosLat) * SCALE * p.speed;
      p.lon = ((p.lon + 180) % 360 + 360) % 360 - 180;
      p.age += 0.004 + 0.003 * Math.min(1, windSpeed / 15);

      if (p.age > 1) {
        p.lat = Math.random() * 178 - 89;
        p.lon = Math.random() * 360 - 180;
        p.age = 0;
        continue;
      }

      // Project prevLat/prevLon to screen
      const latR1 = prevLat * DEG2RAD;
      const lonR1 = prevLon * DEG2RAD;
      this._windScratch.set(
        Math.cos(latR1) * Math.cos(lonR1),
        Math.sin(latR1),
        Math.cos(latR1) * Math.sin(lonR1),
      );
      // Skip particles on back-face of globe (dot with camera direction < threshold)
      if (this._windScratch.dot(camPos) < 0.08) continue;

      this._windScratch.project(this.camera);
      if (this._windScratch.z > 1) continue; // behind clip plane
      const x1 = (this._windScratch.x + 1) * 0.5 * W;
      const y1 = (1 - this._windScratch.y) * 0.5 * H;

      // Project current position
      const latR2 = p.lat * DEG2RAD;
      const lonR2 = p.lon * DEG2RAD;
      this._windScratch.set(
        Math.cos(latR2) * Math.cos(lonR2),
        Math.sin(latR2),
        Math.cos(latR2) * Math.sin(lonR2),
      );
      this._windScratch.project(this.camera);
      if (this._windScratch.z > 1) continue;
      const x2 = (this._windScratch.x + 1) * 0.5 * W;
      const y2 = (1 - this._windScratch.y) * 0.5 * H;

      // Skip segments that are too long (pole distortion / globe-edge artifacts)
      const dx = x2 - x1;
      const dy = y2 - y1;
      if (dx * dx + dy * dy > 80 * 80) continue;

      // Color by wind speed: dark blue (calm) → cyan → white (strong)
      const speedFrac = Math.min(1, windSpeed / 20);
      const cr = Math.round(20 + speedFrac * 235);
      const cg = Math.round(150 + speedFrac * 105);
      const cb = Math.round(255);
      const opacity = (1 - p.age) * Math.min(0.95, 0.3 + speedFrac * 0.6);

      ctx.beginPath();
      ctx.moveTo(x1, y1);
      ctx.lineTo(x2, y2);
      ctx.strokeStyle = `rgba(${cr},${cg},${cb},${opacity.toFixed(2)})`;
      ctx.lineWidth = 1 + speedFrac * 0.8;
      ctx.stroke();
    }
  }
}

