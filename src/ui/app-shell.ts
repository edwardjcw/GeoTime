// ─── Application Shell ──────────────────────────────────────────────────────
// Builds the full UI chrome: viewport for the 3D globe, collapsible sidebar,
// cross-section panel, and a semi-transparent HUD bar.  All DOM is created
// programmatically.

export interface InspectInfo {
  lat: number;
  lon: number;
  elevation: number;
  crustThickness: number;
  rockType: string;
  rockAge: number;
  plateId: number;
  soilOrder: string;
  soilDepth: number;
  temperature: number;
  precipitation: number;
  biomass: number;
  biomatterDensity: number;
  organicCarbon: number;
  reefPresent: boolean;
}

export class AppShell {
  // ── Root containers ─────────────────────────────────────────────────────
  private root: HTMLElement;
  private viewport: HTMLElement;
  private sidebar: HTMLElement;
  private sidebarToggle: HTMLElement;
  private hud: HTMLElement;

  // ── HUD elements ────────────────────────────────────────────────────────
  private fpsEl: HTMLSpanElement;
  private triEl: HTMLSpanElement;
  private timeEl: HTMLSpanElement;
  private pauseBtn: HTMLButtonElement;

  // ── Sidebar elements ────────────────────────────────────────────────────
  private seedEl: HTMLSpanElement;
  private copyBtn: HTMLButtonElement;
  private newPlanetBtn: HTMLButtonElement;
  private rateSlider: HTMLInputElement;
  private rateLabel: HTMLSpanElement;

  // ── Cross-Section elements ──────────────────────────────────────────────
  private drawBtn: HTMLButtonElement;
  private crossSectionPanel: HTMLElement;
  private crossSectionCanvas: HTMLCanvasElement;
  private crossSectionScrollEl: HTMLElement;
  private labelToggleBtn: HTMLButtonElement;
  private exportPngBtn: HTMLButtonElement;
  private closeCrossSectionBtn: HTMLButtonElement;
  private zoomInBtn: HTMLButtonElement;
  private zoomOutBtn: HTMLButtonElement;
  private zoomResetBtn: HTMLButtonElement;

  // ── Log panel elements ──────────────────────────────────────────────────
  private logPanel: HTMLElement = document.createElement('div');
  private logPanelOpen = false;
  private logTimingEl: HTMLElement = document.createElement('div');
  private logEventsEl: HTMLElement = document.createElement('div');
  private logBtn: HTMLButtonElement = document.createElement('button');

  // ── Advanced log view ──────────────────────────────────────────────────
  private _advancedViewOpen = false;
  private _advancedViewBtn: HTMLButtonElement = document.createElement('button');
  private _advancedViewContainer: HTMLElement = document.createElement('div');
  private _advancedTimingCanvas: HTMLCanvasElement = document.createElement('canvas');
  private _advancedLoadCanvas: HTMLCanvasElement = document.createElement('canvas');
  private _advancedProcessingEl: HTMLElement = document.createElement('div');
  private _advancedLogEl: HTMLElement = document.createElement('div');
  private _tickHistory: Array<{ tectonicMs: number; surfaceMs: number; atmosphereMs: number; vegetationMs: number; biomatterMs: number; totalMs: number; tectonicAdvectionMs?: number; tectonicCollisionMs?: number; tectonicBoundaryMs?: number; tectonicDynamicsMs?: number; tectonicVolcanismMs?: number; isGpuActive?: boolean; tick: number }> = [];
  private _lastTickStats: { tectonicMs: number; surfaceMs: number; atmosphereMs: number; vegetationMs: number; biomatterMs: number; totalMs: number; tectonicAdvectionMs?: number; tectonicCollisionMs?: number; tectonicBoundaryMs?: number; tectonicDynamicsMs?: number; tectonicVolcanismMs?: number; isGpuActive?: boolean } | null = null;
  private _isGpu = false;

  /**
   * Phases that use GPU kernels when GPU acceleration is active.
   * Volcanism, vegetation, and biomatter always run on CPU.
   */
  private static readonly GPU_PHASES = new Set([
    'tectonic:advection', 'tectonic:collision', 'tectonic:boundaries', 'tectonic:dynamics',
    'surface', 'atmosphere',
  ]);

  // ── Inspect panel elements ──────────────────────────────────────────────
  private inspectPanel: HTMLElement;
  private inspectContent: HTMLElement;
  private inspectValueEls: Map<string, HTMLSpanElement> = new Map();

  // ── Layer legend elements ───────────────────────────────────────────────
  private legendPanel: HTMLElement;

  // ── Progress indicator ──────────────────────────────────────────────────
  private progressEl: HTMLButtonElement;

  // ── Agent status panel ──────────────────────────────────────────────────
  private agentPanel: HTMLElement;
  private agentPanelRows: Map<string, HTMLElement> = new Map();
  private agentPanelOpen = false;

  // ── Timeline elements ───────────────────────────────────────────────────
  private timelineStrip: HTMLElement;
  private timelineBar: HTMLElement;
  private timelineCursorEl: HTMLElement;

  // ── Layer overlay elements ──────────────────────────────────────────────
  private layerToggles: Map<string, HTMLButtonElement> = new Map();

  // ── Label layer container (Phase L5) ────────────────────────────────────
  private _labelLayer: HTMLElement = document.createElement('div');

  // ── Save/Load elements ──────────────────────────────────────────────────
  private saveStateBtn: HTMLButtonElement = document.createElement('button');
  private loadStateBtn: HTMLButtonElement = document.createElement('button');

  // ── First-person indicator ──────────────────────────────────────────────
  private firstPersonEl: HTMLSpanElement = document.createElement('span');

  // ── Compute backend indicator (GPU / CPU) ───────────────────────────────
  private computeEl: HTMLSpanElement = document.createElement('span');

  // ── Weather month selector ───────────────────────────────────────────────
  private weatherMonthPanel: HTMLElement = document.createElement('div');
  private weatherMonthLabel: HTMLSpanElement = document.createElement('span');
  private _currentWeatherMonth = 0;
  private _windToggleBtn: HTMLButtonElement | null = null;
  private _windActive = false;

  // ── LLM Settings panel (Phase D3) ───────────────────────────────────────
  private _llmBtn: HTMLButtonElement = document.createElement('button');
  private _llmPanel: HTMLElement = document.createElement('div');
  private _llmPanelOpen = false;
  private _llmProviderListEl: HTMLElement = document.createElement('div');
  private _llmSetupPanelEl: HTMLElement = document.createElement('div');
  private _llmSettingsChangedCb: ((provider: string, settings?: { apiKey?: string; model?: string; baseUrl?: string }) => void) | null = null;
  private _llmSetupCb: ((provider: string) => void) | null = null;

  // ── Callbacks ───────────────────────────────────────────────────────────
  private newPlanetCb: (() => void) | null = null;
  private pauseToggleCb: (() => void) | null = null;
  private rateChangeCb: ((rate: number) => void) | null = null;
  private drawModeCb: (() => void) | null = null;
  private labelToggleCb: ((visible: boolean) => void) | null = null;
  private exportPngCb: (() => void) | null = null;
  private closeCrossSectionCb: (() => void) | null = null;
  private inspectClickCb: ((x: number, y: number) => void) | null = null;
  private layerToggleCb: ((layer: string, active: boolean) => void) | null = null;
  private zoomInCb: (() => void) | null = null;
  private zoomOutCb: (() => void) | null = null;
  private zoomResetCb: (() => void) | null = null;
  private saveStateCb: (() => void) | null = null;
  private loadStateCb: (() => void) | null = null;
  private weatherMonthChangeCb: ((month: number) => void) | null = null;
  private windToggleCb: ((active: boolean) => void) | null = null;
  private abortRequestCb: (() => void) | null = null;

  // ── Description modal (Phase D5) ────────────────────────────────────────
  private _descModal: HTMLElement = document.createElement('div');
  private _descModalVisible = false;
  private _descOpenCb: (() => void) | null = null;

  // ── Event layer controls (Phase D6) ─────────────────────────────────────
  private _eventLayerGroup: HTMLElement = document.createElement('div');
  private _eventLayerSelect: HTMLSelectElement = document.createElement('select');
  private _eventLayerToggleBtn: HTMLButtonElement = document.createElement('button');
  private _eventLayerActive = false;
  private _eventLayerChangeCb: ((eventType: string | null) => void) | null = null;

  private sidebarOpen = true;
  private labelsVisible = true;
  private crossSectionOpen = false;
  private activeLayers: Set<string> = new Set();

  constructor(root: HTMLElement) {
    this.root = root;
    this.root.style.cssText =
      'position:relative;width:100%;height:100%;overflow:hidden;';

    // ── Viewport (fills available space) ──────────────────────────────────
    this.viewport = el('div', {
      position: 'absolute',
      top: '0',
      left: '0',
      right: '0',
      bottom: '0',
      overflow: 'hidden',
    });
    this.root.appendChild(this.viewport);

    // ── Label layer — div overlay for geographic feature labels (Phase L5) ──
    const labelLayer = document.createElement('div');
    labelLayer.id = 'label-layer';
    Object.assign(labelLayer.style, {
      position: 'absolute',
      top: '0',
      left: '0',
      right: '0',
      bottom: '0',
      pointerEvents: 'none',
      zIndex: '10',
      overflow: 'hidden',
    });
    this.root.appendChild(labelLayer);
    this._labelLayer = labelLayer;

    // ── Inspect panel (floating, hidden by default) ───────────────────────
    this.inspectPanel = el('div', {
      position: 'absolute',
      top: '48px',
      left: '12px',
      width: '240px',
      background: 'rgba(10,10,14,0.92)',
      border: '1px solid rgba(255,255,255,0.12)',
      borderRadius: '6px',
      padding: '8px 12px',
      display: 'none',
      flexDirection: 'column',
      gap: '4px',
      zIndex: '22',
      color: '#ddd',
      fontSize: '12px',
      fontFamily: 'monospace',
      pointerEvents: 'auto',
    });

    // Inspect panel header with close button
    const inspectHeader = el('div', {
      display: 'flex',
      justifyContent: 'space-between',
      alignItems: 'center',
      marginBottom: '4px',
    });
    const inspectTitle = document.createElement('span');
    inspectTitle.textContent = '📍 Cell Info';
    inspectTitle.style.fontWeight = 'bold';
    const inspectDescBtn = document.createElement('button');
    inspectDescBtn.textContent = 'ℹ';
    inspectDescBtn.title = 'Generate geological description';
    Object.assign(inspectDescBtn.style, {
      background: 'none',
      border: 'none',
      color: '#8af',
      cursor: 'pointer',
      fontSize: '14px',
      padding: '0 4px',
      marginLeft: 'auto',
    });
    inspectDescBtn.addEventListener('click', () => this._descOpenCb?.());
    const inspectCloseBtn = document.createElement('button');
    inspectCloseBtn.textContent = '✕';
    Object.assign(inspectCloseBtn.style, {
      background: 'none',
      border: 'none',
      color: '#aaa',
      cursor: 'pointer',
      fontSize: '13px',
      padding: '0 2px',
    });
    inspectCloseBtn.addEventListener('click', () => this.hideInspectPanel());
    inspectHeader.append(inspectTitle, inspectDescBtn, inspectCloseBtn);
    this.inspectPanel.appendChild(inspectHeader);

    this.inspectContent = el('div', {
      display: 'flex',
      flexDirection: 'column',
      gap: '2px',
    });
    this.inspectPanel.appendChild(this.inspectContent);
    this.root.appendChild(this.inspectPanel);

    // ── Description modal (Phase D5) ─────────────────────────────────────
    Object.assign(this._descModal.style, {
      position: 'absolute',
      top: '48px',
      left: '260px',
      width: '380px',
      maxHeight: '70vh',
      overflowY: 'auto',
      background: 'rgba(8,10,18,0.96)',
      border: '1px solid rgba(100,160,255,0.25)',
      borderRadius: '8px',
      padding: '12px 16px',
      display: 'none',
      flexDirection: 'column',
      gap: '8px',
      zIndex: '30',
      color: '#ddd',
      fontSize: '12px',
      fontFamily: 'sans-serif',
      pointerEvents: 'auto',
      lineHeight: '1.55',
    });
    this.root.appendChild(this._descModal);

    // ── Layer legend panel (floating, lower-left, hidden by default) ──────
    this.legendPanel = el('div', {
      position: 'absolute',
      bottom: '48px',
      left: '12px',
      minWidth: '150px',
      background: 'rgba(10,10,14,0.88)',
      border: '1px solid rgba(255,255,255,0.12)',
      borderRadius: '6px',
      padding: '8px 12px',
      display: 'none',
      flexDirection: 'column',
      gap: '4px',
      zIndex: '21',
      color: '#ddd',
      fontSize: '11px',
      fontFamily: 'monospace',
      pointerEvents: 'none',
    });
    this.root.appendChild(this.legendPanel);

    // Forward viewport clicks for inspect
    this.viewport.addEventListener('click', (e: MouseEvent) => {
      if (this.inspectClickCb) {
        const rect = this.viewport.getBoundingClientRect();
        this.inspectClickCb(e.clientX - rect.left, e.clientY - rect.top);
      }
    });

    // ── HUD bar ───────────────────────────────────────────────────────────
    this.hud = el('div', {
      position: 'absolute',
      top: '0',
      left: '0',
      right: '0',
      height: '36px',
      display: 'flex',
      alignItems: 'center',
      gap: '16px',
      padding: '0 12px',
      background: 'rgba(0,0,0,0.65)',
      color: '#ccc',
      fontSize: '13px',
      fontFamily: 'monospace',
      zIndex: '20',
      pointerEvents: 'auto',
      userSelect: 'none',
    });

    this.fpsEl = document.createElement('span');
    this.fpsEl.textContent = 'FPS: --';

    this.triEl = document.createElement('span');
    this.triEl.textContent = 'Tris: --';

    this.timeEl = document.createElement('span');
    this.timeEl.textContent = 'Time: --';

    this.pauseBtn = document.createElement('button');
    this.pauseBtn.textContent = '⏸ Pause';
    styleBtn(this.pauseBtn);
    this.pauseBtn.addEventListener('click', () => this.pauseToggleCb?.());

    this.firstPersonEl = document.createElement('span');
    this.firstPersonEl.textContent = '';
    this.firstPersonEl.style.color = '#4af';
    this.firstPersonEl.style.fontWeight = 'bold';
    this.firstPersonEl.style.display = 'none';

    this.computeEl = document.createElement('span');
    this.computeEl.textContent = '';
    Object.assign(this.computeEl.style, {
      fontSize: '11px',
      fontFamily: 'monospace',
      opacity: '0.75',
      color: '#adf',
    });
    this.computeEl.title = 'Backend compute device';

    this.progressEl = document.createElement('button');
    Object.assign(this.progressEl.style, {
      opacity: '0.7',
      fontSize: '11px',
      background: 'none',
      border: 'none',
      color: '#ccc',
      cursor: 'pointer',
      fontFamily: 'monospace',
      padding: '2px 6px',
      borderRadius: '3px',
    });
    this.progressEl.title = 'Click to view agent status';
    this.progressEl.textContent = '';
    this.progressEl.addEventListener('click', () => {
      // When showing a freeze warning, clicking aborts the stuck request
      if (this.progressEl.dataset.frozen === 'true') {
        this.abortRequestCb?.();
      } else {
        this.toggleAgentPanel();
      }
    });
    this.progressEl.addEventListener('mouseenter', () => {
      this.progressEl.style.background = 'rgba(255,255,255,0.1)';
    });
    this.progressEl.addEventListener('mouseleave', () => {
      this.progressEl.style.background = 'none';
    });

    // ── Agent status panel ─────────────────────────────────────────────────
    this.agentPanel = el('div', {
      position: 'absolute',
      top: '40px',
      left: '50%',
      transform: 'translateX(-50%)',
      background: 'rgba(10,10,18,0.92)',
      border: '1px solid rgba(255,255,255,0.12)',
      borderRadius: '6px',
      padding: '10px 14px',
      display: 'none',
      flexDirection: 'column',
      gap: '5px',
      zIndex: '30',
      color: '#ddd',
      fontSize: '12px',
      fontFamily: 'monospace',
      minWidth: '200px',
      pointerEvents: 'none',
    });

    const agentPanelTitle = document.createElement('div');
    agentPanelTitle.textContent = 'Engine Agents';
    Object.assign(agentPanelTitle.style, {
      fontWeight: 'bold',
      marginBottom: '4px',
      opacity: '0.8',
      fontSize: '11px',
    });
    this.agentPanel.appendChild(agentPanelTitle);

    const AGENT_DEFS: Array<[string, string]> = [
      ['tectonic',   '⛰ Tectonic'],
      ['surface',    '🌊 Surface'],
      ['atmosphere', '☁️ Atmosphere'],
      ['vegetation', '🌿 Vegetation'],
      ['biomatter',  '🧬 Biomatter'],
    ];
    for (const [key, label] of AGENT_DEFS) {
      const row = el('div', { display: 'flex', justifyContent: 'space-between', gap: '12px' });
      const lbl = document.createElement('span');
      lbl.textContent = label;
      lbl.style.opacity = '0.7';
      const status = document.createElement('span');
      status.textContent = '○ idle';
      status.style.opacity = '0.5';
      row.append(lbl, status);
      this.agentPanel.appendChild(row);
      this.agentPanelRows.set(key, status);
    }
    this.root.appendChild(this.agentPanel);

    // ── Weather Month Selector (hidden by default) ─────────────────────────
    this.weatherMonthPanel = el('div', {
      position: 'absolute',
      top: '40px',
      left: '50%',
      transform: 'translateX(-50%)',
      background: 'rgba(10,10,14,0.88)',
      border: '1px solid rgba(255,255,255,0.12)',
      borderRadius: '6px',
      padding: '6px 12px',
      display: 'none',
      alignItems: 'center',
      gap: '10px',
      zIndex: '19',
      color: '#ddd',
      fontSize: '13px',
      fontFamily: 'monospace',
      pointerEvents: 'auto',
    });
    const prevMonthBtn = document.createElement('button');
    prevMonthBtn.textContent = '◀';
    styleBtn(prevMonthBtn);
    prevMonthBtn.style.padding = '2px 8px';
    prevMonthBtn.addEventListener('click', () => {
      this._currentWeatherMonth = (this._currentWeatherMonth + 11) % 12;
      this.weatherMonthLabel.textContent = `Weather Month: ${MONTH_NAMES[this._currentWeatherMonth]}`;
      this.weatherMonthChangeCb?.(this._currentWeatherMonth);
    });
    this.weatherMonthLabel = document.createElement('span');
    this.weatherMonthLabel.textContent = 'Weather Month: January';
    const nextMonthBtn = document.createElement('button');
    nextMonthBtn.textContent = '▶';
    styleBtn(nextMonthBtn);
    nextMonthBtn.style.padding = '2px 8px';
    nextMonthBtn.addEventListener('click', () => {
      this._currentWeatherMonth = (this._currentWeatherMonth + 1) % 12;
      this.weatherMonthLabel.textContent = `Weather Month: ${MONTH_NAMES[this._currentWeatherMonth]}`;
      this.weatherMonthChangeCb?.(this._currentWeatherMonth);
    });

    // Wind map toggle button
    const sep = document.createElement('span');
    sep.textContent = '|';
    sep.style.opacity = '0.3';
    this._windToggleBtn = document.createElement('button');
    this._windToggleBtn.textContent = '💨 Wind';
    styleBtn(this._windToggleBtn);
    this._windToggleBtn.style.padding = '2px 10px';
    this._windToggleBtn.title = 'Toggle animated wind particle overlay';
    this._windToggleBtn.addEventListener('click', () => {
      this._windActive = !this._windActive;
      if (this._windToggleBtn) {
        this._windToggleBtn.style.background = this._windActive ? '#2a6' : 'rgba(255,255,255,0.08)';
      }
      this.windToggleCb?.(this._windActive);
    });

    this.weatherMonthPanel.append(prevMonthBtn, this.weatherMonthLabel, nextMonthBtn, sep, this._windToggleBtn);
    this.root.appendChild(this.weatherMonthPanel);

    // ── Log button (📊) ────────────────────────────────────────────────────
    this.logBtn = document.createElement('button');
    this.logBtn.textContent = '📊';
    this.logBtn.title = 'Simulation log & timing';
    Object.assign(this.logBtn.style, {
      background: 'none',
      border: 'none',
      color: '#ccc',
      cursor: 'pointer',
      fontSize: '14px',
      padding: '2px 6px',
      borderRadius: '3px',
      fontFamily: 'monospace',
      // Dedicated slot — margin-left: auto pushes this and everything after to the right.
      marginLeft: 'auto',
      flexShrink: '0',
    });
    this.logBtn.addEventListener('click', () => this.toggleLogPanel());
    this.logBtn.addEventListener('mouseenter', () => { this.logBtn.style.background = 'rgba(255,255,255,0.1)'; });
    this.logBtn.addEventListener('mouseleave', () => { this.logBtn.style.background = this.logPanelOpen ? 'rgba(255,255,255,0.15)' : 'none'; });

    // ── Log panel ──────────────────────────────────────────────────────────
    this.logPanel = el('div', {
      position: 'absolute',
      top: '40px',
      right: '270px',
      background: 'rgba(10,10,18,0.94)',
      border: '1px solid rgba(255,255,255,0.12)',
      borderRadius: '6px',
      padding: '10px 14px',
      display: 'none',
      flexDirection: 'column',
      gap: '6px',
      zIndex: '30',
      color: '#ddd',
      fontSize: '12px',
      fontFamily: 'monospace',
      minWidth: '280px',
      maxHeight: '420px',
      overflowY: 'auto',
      pointerEvents: 'auto',
    });

    const logTitle = document.createElement('div');
    logTitle.textContent = '📊 Simulation Log';
    logTitle.dataset.logTitle = '1';
    Object.assign(logTitle.style, { fontWeight: 'bold', marginBottom: '4px', opacity: '0.85', fontSize: '12px' });
    this.logPanel.appendChild(logTitle);

    const logTimingTitle = document.createElement('div');
    logTimingTitle.textContent = 'Last Tick Timing';
    Object.assign(logTimingTitle.style, { opacity: '0.5', fontSize: '10px', marginTop: '4px' });
    this.logPanel.appendChild(logTimingTitle);

    this.logTimingEl = el('div', { display: 'flex', flexDirection: 'column', gap: '2px' });
    this.logTimingEl.textContent = '—';
    this.logPanel.appendChild(this.logTimingEl);

    const logEventsTitle = document.createElement('div');
    logEventsTitle.textContent = 'Recent Events';
    Object.assign(logEventsTitle.style, { opacity: '0.5', fontSize: '10px', marginTop: '8px' });
    this.logPanel.appendChild(logEventsTitle);

    this.logEventsEl = el('div', { display: 'flex', flexDirection: 'column', gap: '2px' });
    this.logEventsEl.textContent = '—';
    this.logPanel.appendChild(this.logEventsEl);

    // ── Advanced View toggle button ──────────────────────────────────────
    this._advancedViewBtn = document.createElement('button');
    this._advancedViewBtn.textContent = '▶ Advanced View';
    Object.assign(this._advancedViewBtn.style, {
      background: 'rgba(255,255,255,0.06)',
      border: '1px solid rgba(255,255,255,0.15)',
      color: '#aaf',
      cursor: 'pointer',
      fontSize: '10px',
      padding: '3px 8px',
      borderRadius: '3px',
      marginTop: '8px',
      width: '100%',
    });
    this._advancedViewBtn.addEventListener('click', () => this._toggleAdvancedView());
    this.logPanel.appendChild(this._advancedViewBtn);

    // ── Advanced View container ───────────────────────────────────────────
    this._advancedViewContainer = el('div', {
      display: 'none',
      flexDirection: 'column',
      gap: '8px',
      marginTop: '6px',
      borderTop: '1px solid rgba(255,255,255,0.1)',
      paddingTop: '8px',
    });

    // Processing status section
    const procTitle = document.createElement('div');
    procTitle.textContent = 'Current Processing';
    Object.assign(procTitle.style, { opacity: '0.5', fontSize: '10px' });
    this._advancedViewContainer.appendChild(procTitle);

    this._advancedProcessingEl = el('div', { fontSize: '11px', lineHeight: '1.7' });
    this._advancedProcessingEl.textContent = '— idle —';
    this._advancedViewContainer.appendChild(this._advancedProcessingEl);

    // Fine-grained log section
    const advLogTitle = document.createElement('div');
    advLogTitle.textContent = 'Recent Tick Logs';
    Object.assign(advLogTitle.style, { opacity: '0.5', fontSize: '10px', marginTop: '4px' });
    this._advancedViewContainer.appendChild(advLogTitle);

    this._advancedLogEl = el('div', {
      fontSize: '10px',
      fontFamily: 'monospace',
      maxHeight: '80px',
      overflowY: 'auto',
      background: 'rgba(0,0,0,0.3)',
      borderRadius: '3px',
      padding: '4px 6px',
      lineHeight: '1.6',
    });
    this._advancedLogEl.textContent = '— no ticks yet —';
    this._advancedViewContainer.appendChild(this._advancedLogEl);

    // Agent Timing History graph
    const timingTitle = document.createElement('div');
    timingTitle.textContent = 'Agent Timing History (ms)';
    Object.assign(timingTitle.style, { opacity: '0.5', fontSize: '10px', marginTop: '4px' });
    this._advancedViewContainer.appendChild(timingTitle);

    this._advancedTimingCanvas = document.createElement('canvas');
    Object.assign(this._advancedTimingCanvas.style, {
      background: 'rgba(0,0,0,0.3)',
      borderRadius: '3px',
      width: '100%',
      height: '120px',
    });
    this._advancedViewContainer.appendChild(this._advancedTimingCanvas);

    // Computational Load graph
    const loadTitle = document.createElement('div');
    loadTitle.textContent = 'Computational Load (ms per tick)';
    Object.assign(loadTitle.style, { opacity: '0.5', fontSize: '10px', marginTop: '4px' });
    this._advancedViewContainer.appendChild(loadTitle);

    this._advancedLoadCanvas = document.createElement('canvas');
    Object.assign(this._advancedLoadCanvas.style, {
      background: 'rgba(0,0,0,0.3)',
      borderRadius: '3px',
      width: '100%',
      height: '80px',
    });
    this._advancedViewContainer.appendChild(this._advancedLoadCanvas);

    this.logPanel.appendChild(this._advancedViewContainer);

    this.root.appendChild(this.logPanel);

    // ── LLM Settings button and panel (Phase D3) ──────────────────────────
    this._llmBtn = document.createElement('button');
    this._llmBtn.textContent = '⚙ LLM';
    this._llmBtn.title = 'LLM provider settings';
    Object.assign(this._llmBtn.style, {
      background: 'none',
      border: 'none',
      color: '#ccc',
      cursor: 'pointer',
      fontSize: '12px',
      padding: '2px 6px',
      borderRadius: '3px',
      fontFamily: 'monospace',
      flexShrink: '0',
    });
    this._llmBtn.addEventListener('click', () => this._toggleLlmPanel());
    this._llmBtn.addEventListener('mouseenter', () => { this._llmBtn.style.background = 'rgba(255,255,255,0.1)'; });
    this._llmBtn.addEventListener('mouseleave', () => { this._llmBtn.style.background = this._llmPanelOpen ? 'rgba(255,255,255,0.15)' : 'none'; });

    // LLM settings side-panel (non-blocking; globe stays visible)
    this._llmPanel = el('div', {
      position: 'absolute',
      top: '36px',
      right: '260px',
      width: '320px',
      maxHeight: 'calc(100% - 56px)',
      background: 'rgba(10,10,18,0.96)',
      border: '1px solid rgba(255,255,255,0.14)',
      borderRadius: '6px',
      padding: '12px 14px',
      display: 'none',
      flexDirection: 'column',
      gap: '8px',
      zIndex: '28',
      color: '#ddd',
      fontSize: '12px',
      fontFamily: 'monospace',
      overflowY: 'auto',
      pointerEvents: 'auto',
    });
    this._llmPanel.id = 'llm-settings-panel';

    const llmTitle = document.createElement('div');
    llmTitle.textContent = 'Active LLM Provider';
    Object.assign(llmTitle.style, { fontWeight: 'bold', fontSize: '13px', marginBottom: '4px' });
    this._llmPanel.appendChild(llmTitle);

    this._llmProviderListEl = el('div', { display: 'flex', flexDirection: 'column', gap: '6px' });
    this._llmProviderListEl.textContent = 'Loading…';
    this._llmPanel.appendChild(this._llmProviderListEl);

    // Setup sub-panel (shown when user clicks Setup ▶ next to a provider)
    this._llmSetupPanelEl = el('div', {
      display: 'none',
      flexDirection: 'column',
      gap: '4px',
      marginTop: '8px',
      padding: '8px',
      background: 'rgba(255,255,255,0.04)',
      borderRadius: '4px',
    });
    this._llmPanel.appendChild(this._llmSetupPanelEl);

    this.root.appendChild(this._llmPanel);

    this.hud.append(this.fpsEl, this.triEl, this.timeEl, this.pauseBtn, this.firstPersonEl, this.computeEl, this.progressEl, this.logBtn, this._llmBtn);
    this.root.appendChild(this.hud);

    // ── Sidebar ───────────────────────────────────────────────────────────
    this.sidebar = el('div', {
      position: 'absolute',
      top: '36px',
      right: '0',
      bottom: '0',
      width: '260px',
      background: 'rgba(10,10,14,0.88)',
      borderLeft: '1px solid rgba(255,255,255,0.08)',
      padding: '16px 14px',
      display: 'flex',
      flexDirection: 'column',
      gap: '14px',
      zIndex: '15',
      overflowY: 'auto',
      transition: 'transform 0.25s ease',
      color: '#ddd',
      fontSize: '13px',
      fontFamily: 'sans-serif',
    });

    // -- New Planet button
    this.newPlanetBtn = document.createElement('button');
    this.newPlanetBtn.textContent = '🌍 New Planet';
    styleBtn(this.newPlanetBtn, true);
    this.newPlanetBtn.addEventListener('click', () => this.newPlanetCb?.());
    this.sidebar.appendChild(this.newPlanetBtn);

    // -- Save / Load state buttons
    const saveLoadRow = el('div', { display: 'flex', gap: '6px' });
    this.saveStateBtn = document.createElement('button');
    this.saveStateBtn.textContent = '💾 Save State';
    styleBtn(this.saveStateBtn);
    this.saveStateBtn.style.flex = '1';
    this.saveStateBtn.addEventListener('click', () => this.saveStateCb?.());
    this.loadStateBtn = document.createElement('button');
    this.loadStateBtn.textContent = '📂 Load State';
    styleBtn(this.loadStateBtn);
    this.loadStateBtn.style.flex = '1';
    this.loadStateBtn.addEventListener('click', () => this.loadStateCb?.());
    saveLoadRow.append(this.saveStateBtn, this.loadStateBtn);
    this.sidebar.appendChild(saveLoadRow);

    // -- Seed display
    const seedRow = el('div', {
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
    });
    const seedLabel = document.createElement('span');
    seedLabel.textContent = 'Seed: ';
    seedLabel.style.opacity = '0.6';
    this.seedEl = document.createElement('span');
    this.seedEl.textContent = '--';
    this.seedEl.style.fontFamily = 'monospace';
    this.copyBtn = document.createElement('button');
    this.copyBtn.textContent = '📋';
    this.copyBtn.title = 'Copy seed';
    styleBtn(this.copyBtn);
    this.copyBtn.style.padding = '2px 6px';
    this.copyBtn.addEventListener('click', () => {
      navigator.clipboard
        .writeText(this.seedEl.textContent ?? '')
        .catch(() => {/* ignore clipboard errors */});
    });
    seedRow.append(seedLabel, this.seedEl, this.copyBtn);
    this.sidebar.appendChild(seedRow);

    // -- Rate slider
    const rateGroup = el('div', {
      display: 'flex',
      flexDirection: 'column',
      gap: '4px',
    });
    this.rateLabel = document.createElement('span');
    this.rateLabel.textContent = 'Rate: 0.010 Ma/s';
    this.rateLabel.style.opacity = '0.6';

    this.rateSlider = document.createElement('input');
    this.rateSlider.type = 'range';
    // Use a logarithmic mapping: slider 0..1000 → rate 0.001..100
    this.rateSlider.min = '0';
    this.rateSlider.max = '1000';
    this.rateSlider.value = '200'; // default ≈ 0.010 Ma/s
    this.rateSlider.style.width = '100%';
    this.rateSlider.style.accentColor = '#4af';
    this.rateSlider.addEventListener('input', () => {
      const rate = sliderToRate(Number(this.rateSlider.value));
      this.rateLabel.textContent = `Rate: ${rate.toFixed(3)} Ma/s`;
      this.rateChangeCb?.(rate);
    });

    rateGroup.append(this.rateLabel, this.rateSlider);
    this.sidebar.appendChild(rateGroup);

    // -- Draw Cross-Section button
    this.drawBtn = document.createElement('button');
    this.drawBtn.textContent = '✏️ Draw Cross-Section';
    styleBtn(this.drawBtn);
    this.drawBtn.addEventListener('click', () => this.drawModeCb?.());
    this.sidebar.appendChild(this.drawBtn);

    // -- Layer overlay toggles
    const layerGroup = el('div', {
      display: 'flex',
      flexDirection: 'column',
      gap: '4px',
    });
    const layerTitle = document.createElement('span');
    layerTitle.textContent = 'Layer Overlays';
    layerTitle.style.opacity = '0.6';
    layerTitle.style.marginBottom = '2px';
    layerGroup.appendChild(layerTitle);

    // Layer names must stay in sync with the switch cases in main.ts onLayerToggle handler.
    const layerNames = ['plates', 'temperature', 'precipitation', 'biome', 'soil', 'clouds', 'biomass', 'topo', 'weather', 'labels'];
    for (const name of layerNames) {
      const btn = document.createElement('button');
      btn.textContent = name;
      btn.dataset.layer = name;
      styleBtn(btn);
      btn.style.textAlign = 'left';
      btn.addEventListener('click', () => {
        const active = this.activeLayers.has(name);
        if (active) {
          this.activeLayers.delete(name);
          btn.style.background = 'rgba(255,255,255,0.08)';
        } else {
          this.activeLayers.add(name);
          btn.style.background = '#2a6';
        }
        this.layerToggleCb?.(name, !active);
      });
      this.layerToggles.set(name, btn);
      layerGroup.appendChild(btn);
    }

    // ── Event Layers toggle + type dropdown (Phase D6) ───────────────────
    styleBtn(this._eventLayerToggleBtn);
    this._eventLayerToggleBtn.textContent = 'event layers';
    this._eventLayerToggleBtn.style.textAlign = 'left';
    this._eventLayerToggleBtn.addEventListener('click', () => {
      this._eventLayerActive = !this._eventLayerActive;
      this._eventLayerToggleBtn.style.background = this._eventLayerActive ? '#a62' : 'rgba(255,255,255,0.08)';
      // Show the dropdown whenever event layers is active (even if no types yet).
      this._eventLayerSelect.style.display = this._eventLayerActive && this._eventLayerSelect.options.length > 0 ? 'block' : 'none';
      const type = this._eventLayerActive ? (this._eventLayerSelect.value || null) : null;
      this._eventLayerChangeCb?.(type);
    });
    layerGroup.appendChild(this._eventLayerToggleBtn);

    Object.assign(this._eventLayerSelect.style, {
      width: '100%',
      background: 'rgba(255,255,255,0.06)',
      color: '#ddd',
      border: '1px solid rgba(255,255,255,0.15)',
      borderRadius: '4px',
      padding: '4px 6px',
      fontSize: '11px',
      display: 'none',
    });
    // Add a default placeholder option so the select is never empty.
    const defaultPlaceholder = document.createElement('option');
    defaultPlaceholder.value = '';
    defaultPlaceholder.textContent = '— no event types yet —';
    this._eventLayerSelect.appendChild(defaultPlaceholder);
    this._eventLayerSelect.addEventListener('change', () => {
      if (this._eventLayerActive) {
        this._eventLayerChangeCb?.(this._eventLayerSelect.value || null);
      }
    });
    layerGroup.appendChild(this._eventLayerSelect);

    this.sidebar.appendChild(layerGroup);

    this.root.appendChild(this.sidebar);

    // ── Geological Timeline (bottom strip above cross-section) ────────────
    this.timelineStrip = el('div', {
      position: 'absolute',
      left: '0',
      right: '0',
      bottom: '0',
      height: '40px',
      background: 'rgba(10,10,14,0.85)',
      borderTop: '1px solid rgba(255,255,255,0.10)',
      display: 'flex',
      alignItems: 'center',
      zIndex: '18',
      pointerEvents: 'auto',
      userSelect: 'none',
    });
    this.timelineBar = el('div', {
      position: 'relative',
      flex: '1',
      height: '12px',
      margin: '0 12px',
      background: 'rgba(255,255,255,0.08)',
      borderRadius: '6px',
      overflow: 'visible',
    });
    this.timelineCursorEl = el('div', {
      position: 'absolute',
      top: '-4px',
      left: '0%',
      width: '3px',
      height: '20px',
      background: '#4af',
      borderRadius: '2px',
      transition: 'left 0.15s ease',
      pointerEvents: 'none',
    });
    this.timelineBar.appendChild(this.timelineCursorEl);
    this.timelineStrip.appendChild(this.timelineBar);
    this.root.appendChild(this.timelineStrip);

    // ── Cross-Section Panel (bottom, hidden by default) ───────────────────
    this.crossSectionPanel = el('div', {
      position: 'absolute',
      left: '0',
      right: '0',
      bottom: '0',
      height: '320px',
      background: 'rgba(10,10,14,0.95)',
      borderTop: '1px solid rgba(255,255,255,0.12)',
      display: 'none',
      flexDirection: 'column',
      zIndex: '25',
      color: '#ddd',
      fontSize: '13px',
      fontFamily: 'sans-serif',
    });

    // Cross-section panel header
    const csHeader = el('div', {
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      padding: '6px 12px',
      borderBottom: '1px solid rgba(255,255,255,0.08)',
    });

    const csTitle = document.createElement('span');
    csTitle.textContent = 'Cross-Section';
    csTitle.style.fontWeight = 'bold';
    csTitle.style.flex = '1';

    this.labelToggleBtn = document.createElement('button');
    this.labelToggleBtn.textContent = '🏷️ Labels';
    styleBtn(this.labelToggleBtn);
    this.labelToggleBtn.addEventListener('click', () => {
      this.labelsVisible = !this.labelsVisible;
      this.labelToggleBtn.style.opacity = this.labelsVisible ? '1' : '0.5';
      this.labelToggleCb?.(this.labelsVisible);
    });

    this.exportPngBtn = document.createElement('button');
    this.exportPngBtn.textContent = '📷 Export PNG';
    styleBtn(this.exportPngBtn);
    this.exportPngBtn.addEventListener('click', () => this.exportPngCb?.());

    this.zoomInBtn = document.createElement('button');
    this.zoomInBtn.textContent = '🔍+';
    this.zoomInBtn.title = 'Zoom In';
    styleBtn(this.zoomInBtn);
    this.zoomInBtn.style.padding = '4px 8px';
    this.zoomInBtn.addEventListener('click', () => this.zoomInCb?.());

    this.zoomOutBtn = document.createElement('button');
    this.zoomOutBtn.textContent = '🔍−';
    this.zoomOutBtn.title = 'Zoom Out';
    styleBtn(this.zoomOutBtn);
    this.zoomOutBtn.style.padding = '4px 8px';
    this.zoomOutBtn.addEventListener('click', () => this.zoomOutCb?.());

    this.zoomResetBtn = document.createElement('button');
    this.zoomResetBtn.textContent = '1×';
    this.zoomResetBtn.title = 'Reset Zoom';
    styleBtn(this.zoomResetBtn);
    this.zoomResetBtn.style.padding = '4px 8px';
    this.zoomResetBtn.addEventListener('click', () => this.zoomResetCb?.());

    this.closeCrossSectionBtn = document.createElement('button');
    this.closeCrossSectionBtn.textContent = '✕';
    styleBtn(this.closeCrossSectionBtn);
    this.closeCrossSectionBtn.style.padding = '4px 8px';
    this.closeCrossSectionBtn.addEventListener('click', () => {
      this.hideCrossSection();
      this.closeCrossSectionCb?.();
    });

    csHeader.append(
      csTitle,
      this.labelToggleBtn,
      this.exportPngBtn,
      this.zoomOutBtn,
      this.zoomResetBtn,
      this.zoomInBtn,
      this.closeCrossSectionBtn,
    );
    this.crossSectionPanel.appendChild(csHeader);

    // Scroll wrapper for the zoomable canvas
    this.crossSectionScrollEl = el('div', {
      flex: '1',
      overflow: 'auto',
      position: 'relative',
    });

    // Cross-section canvas (natural pixel size — zoom is achieved by changing canvas dimensions)
    this.crossSectionCanvas = document.createElement('canvas');
    this.crossSectionCanvas.style.cssText = 'display:block;';
    this.crossSectionScrollEl.appendChild(this.crossSectionCanvas);
    this.crossSectionPanel.appendChild(this.crossSectionScrollEl);

    this.root.appendChild(this.crossSectionPanel);

    // ── Sidebar toggle tab ────────────────────────────────────────────────
    this.sidebarToggle = el('div', {
      position: 'absolute',
      top: '50%',
      right: '260px',
      transform: 'translateY(-50%)',
      width: '22px',
      height: '48px',
      background: 'rgba(10,10,14,0.8)',
      borderRadius: '4px 0 0 4px',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      cursor: 'pointer',
      zIndex: '16',
      color: '#aaa',
      fontSize: '14px',
      transition: 'right 0.25s ease',
      userSelect: 'none',
    });
    this.sidebarToggle.textContent = '▶';
    this.sidebarToggle.addEventListener('click', () => this.toggleSidebar());
    this.root.appendChild(this.sidebarToggle);
  }

  // ── Public API ──────────────────────────────────────────────────────────

  getViewportElement(): HTMLElement {
    return this.viewport;
  }

  /** Returns the #label-layer container for the LabelRenderer (Phase L5). */
  getLabelLayer(): HTMLElement {
    return this._labelLayer;
  }

  setSeed(seed: number): void {
    this.seedEl.textContent = String(seed);
  }

  setFps(fps: number): void {
    this.fpsEl.textContent = `FPS: ${fps.toFixed(0)}`;
  }

  setTriangleCount(count: number): void {
    this.triEl.textContent = `Tris: ${formatCount(count)}`;
  }

  setSimTime(timeMa: number): void {
    const abs = Math.abs(timeMa);
    if (abs >= 1000) {
      this.timeEl.textContent = `Time: ${(timeMa / 1000).toFixed(3)} Ga`;
    } else {
      this.timeEl.textContent = `Time: ${timeMa.toFixed(3)} Ma`;
    }
  }

  setPaused(paused: boolean): void {
    this.pauseBtn.textContent = paused ? '▶ Resume' : '⏸ Pause';
  }

  /** Show or hide the first-person mode indicator in the HUD. */
  setFirstPersonMode(active: boolean): void {
    if (active) {
      this.firstPersonEl.textContent = '👁 First-Person';
      this.firstPersonEl.style.display = 'inline';
    } else {
      this.firstPersonEl.textContent = '';
      this.firstPersonEl.style.display = 'none';
    }
  }

  /**
   * Update the compute-backend indicator in the toolbar.
   * Shows both backend (simulation) and frontend (WebGL rendering) GPU information.
   * @param isGpu - true if an actual GPU accelerator is active for simulation
   * @param deviceName - human-readable device label from the backend
   * @param memoryMb - on-device memory in MB (0 = unknown)
   * @param frontendGpu - WebGL renderer string from the browser (optional)
   */
  setComputeMode(isGpu: boolean, deviceName: string, memoryMb = 0, frontendGpu?: string): void {
    this._isGpu = isGpu;
    const icon = isGpu ? '🖥 GPU' : '⚙️ CPU';
    this.computeEl.textContent = `${icon}`;
    const memLabel = isGpu && memoryMb > 0 ? ` · ${memoryMb >= 1024 ? (memoryMb / 1024).toFixed(1) + ' GB' : memoryMb + ' MB'}` : '';
    const backendLabel = `Backend: ${deviceName}${memLabel}`;
    const frontendLabel = frontendGpu ? `Frontend: ${frontendGpu}` : '';
    this.computeEl.title = frontendLabel ? `${backendLabel}\n${frontendLabel}` : backendLabel;
    this.computeEl.style.color = isGpu ? '#7ef' : '#adf';
  }

  onNewPlanet(cb: () => void): void {
    this.newPlanetCb = cb;
  }

  onPauseToggle(cb: () => void): void {
    this.pauseToggleCb = cb;
  }

  onRateChange(cb: (rate: number) => void): void {
    this.rateChangeCb = cb;
  }

  /** Return the current rate value from the slider. */
  getRate(): number {
    return sliderToRate(Number(this.rateSlider.value));
  }

  /** Register save-state callback. */
  onSaveState(cb: () => void): void {
    this.saveStateCb = cb;
  }

  /** Register load-state callback. */
  onLoadState(cb: () => void): void {
    this.loadStateCb = cb;
  }

  /** Temporarily disable the load button (e.g., when no snapshots exist). */
  setLoadStateEnabled(enabled: boolean): void {
    this.loadStateBtn.disabled = !enabled;
    this.loadStateBtn.style.opacity = enabled ? '1' : '0.4';
  }

  /** Register a callback invoked when the user clicks to abort a frozen sim request. */
  onAbortRequest(cb: () => void): void {
    this.abortRequestCb = cb;
  }

  /** Register a callback for weather month changes. */
  onWeatherMonthChange(cb: (month: number) => void): void {
    this.weatherMonthChangeCb = cb;
  }

  /** Register a callback for wind map toggle. */
  onWindToggle(cb: (active: boolean) => void): void {
    this.windToggleCb = cb;
  }

  /** Show the weather month selector panel. */
  showWeatherMonthSelector(month: number): void {
    this._currentWeatherMonth = month;
    this.weatherMonthLabel.textContent = `Weather Month: ${MONTH_NAMES[month]}`;
    this.weatherMonthPanel.style.display = 'flex';
  }

  /** Hide the weather month selector panel and reset wind state. */
  hideWeatherMonthSelector(): void {
    this.weatherMonthPanel.style.display = 'none';
    // Reset wind toggle to off
    if (this._windActive) {
      this._windActive = false;
      if (this._windToggleBtn) this._windToggleBtn.style.background = 'rgba(255,255,255,0.08)';
      this.windToggleCb?.(false);
    }
  }

  /**
   * Programmatically deactivate a layer (e.g., when play is clicked).
   * Updates the button visual state and fires the toggle callback.
   */
  deactivateLayer(name: string): void {
    if (!this.activeLayers.has(name)) return;
    this.activeLayers.delete(name);
    const btn = this.layerToggles.get(name);
    if (btn) btn.style.background = 'rgba(255,255,255,0.08)';
    this.layerToggleCb?.(name, false);
  }

  // ── Cross-Section API ──────────────────────────────────────────────────

  onDrawMode(cb: () => void): void {
    this.drawModeCb = cb;
  }

  onLabelToggle(cb: (visible: boolean) => void): void {
    this.labelToggleCb = cb;
  }

  onExportPng(cb: () => void): void {
    this.exportPngCb = cb;
  }

  onCloseCrossSection(cb: () => void): void {
    this.closeCrossSectionCb = cb;
  }

  onCrossSectionZoomIn(cb: () => void): void {
    this.zoomInCb = cb;
  }

  onCrossSectionZoomOut(cb: () => void): void {
    this.zoomOutCb = cb;
  }

  onCrossSectionZoomReset(cb: () => void): void {
    this.zoomResetCb = cb;
  }

  /** Get the cross-section scroll wrapper element (for measuring available width). */
  getCrossSectionScrollEl(): HTMLElement {
    return this.crossSectionScrollEl;
  }

  /** Show the cross-section panel. */
  showCrossSection(): void {
    this.crossSectionOpen = true;
    this.crossSectionPanel.style.display = 'flex';
  }

  /** Hide the cross-section panel. */
  hideCrossSection(): void {
    this.crossSectionOpen = false;
    this.crossSectionPanel.style.display = 'none';
  }

  /** Get the cross-section canvas for rendering. */
  getCrossSectionCanvas(): HTMLCanvasElement {
    return this.crossSectionCanvas;
  }

  /** Get whether the cross-section panel is currently open. */
  isCrossSectionOpen(): boolean {
    return this.crossSectionOpen;
  }

  /** Get whether labels are visible. */
  areLabelsVisible(): boolean {
    return this.labelsVisible;
  }

  /** Set the draw button state (active mode). */
  setDrawMode(active: boolean): void {
    this.drawBtn.style.background = active ? '#c84' : 'rgba(255,255,255,0.08)';
    this.drawBtn.textContent = active ? '✏️ Click Globe to Draw' : '✏️ Draw Cross-Section';
  }

  // ── Inspect Panel API ──────────────────────────────────────────────────

  /** Register a callback for viewport clicks (for inspect). */
  onInspectClick(cb: (x: number, y: number) => void): void {
    this.inspectClickCb = cb;
  }

  /** Show the inspect panel with location details. */
  showInspectPanel(info: InspectInfo): void {
    const lines: [string, string][] = [
      ['Lat/Lon', `${info.lat.toFixed(2)}°, ${info.lon.toFixed(2)}°`],
      ['Elevation', `${info.elevation.toFixed(0)} m`],
      ['Crust Thickness', `${info.crustThickness.toFixed(1)} km`],
      ['Rock Type', info.rockType],
      ['Rock Age', `${info.rockAge.toFixed(1)} Ma`],
      ['Plate ID', `${info.plateId}`],
      ['Soil Order', info.soilOrder],
      ['Soil Depth', `${info.soilDepth.toFixed(2)} m`],
      ['Temperature', `${info.temperature.toFixed(1)} °C`],
      ['Precipitation', `${info.precipitation.toFixed(0)} mm/yr`],
      ['Biomass', `${info.biomass.toFixed(1)} kg/m²`],
      ['Biomatter', `${info.biomatterDensity.toFixed(2)} kg C/m²`],
      ['Organic C', `${info.organicCarbon.toFixed(2)} kg C/m²`],
      ['Reef', info.reefPresent ? '✅ present' : '—'],
    ];

    if (this.inspectValueEls.size === 0) {
      // First call: build the rows and cache value elements.
      this.inspectContent.innerHTML = '';
      for (const [label, value] of lines) {
        const row = el('div', { display: 'flex', justifyContent: 'space-between', gap: '8px' });
        const lbl = document.createElement('span');
        lbl.textContent = label;
        lbl.style.opacity = '0.6';
        lbl.style.flexShrink = '0';
        const val = document.createElement('span');
        val.textContent = value;
        val.style.textAlign = 'right';
        row.append(lbl, val);
        this.inspectContent.appendChild(row);
        this.inspectValueEls.set(label, val);
      }
    } else {
      // Subsequent calls: update values in-place so the panel doesn't reflow.
      for (const [label, value] of lines) {
        const el = this.inspectValueEls.get(label);
        if (el) el.textContent = value;
      }
    }

    this.inspectPanel.style.display = 'flex';
  }

  /** Hide the inspect panel. */
  hideInspectPanel(): void {
    this.inspectPanel.style.display = 'none';
    // Reset cached value elements so the next showInspectPanel call rebuilds them fresh.
    this.inspectValueEls.clear();
  }

  // ── Description Modal API (Phase D5) ──────────────────────────────────

  /** Register callback for the ℹ description button in the inspect panel. */
  onDescribeOpen(cb: () => void): void {
    this._descOpenCb = cb;
  }

  /** Show the description modal with a loading spinner. */
  showDescriptionModal(): void {
    this._descModal.innerHTML = '';

    // Header
    const header = document.createElement('div');
    Object.assign(header.style, { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' });
    const titleEl = document.createElement('span');
    titleEl.textContent = '🔬 Geological Description';
    Object.assign(titleEl.style, { fontWeight: 'bold', fontSize: '13px', color: '#8af' });
    const closeBtn = document.createElement('button');
    closeBtn.textContent = '✕';
    Object.assign(closeBtn.style, { background: 'none', border: 'none', color: '#aaa', cursor: 'pointer', fontSize: '13px' });
    closeBtn.addEventListener('click', () => this.hideDescriptionModal());
    header.append(titleEl, closeBtn);
    this._descModal.appendChild(header);

    // Spinner
    const spinner = document.createElement('div');
    spinner.textContent = '⏳ Generating description…';
    spinner.style.opacity = '0.6';
    this._descModal.appendChild(spinner);

    this._descModal.style.display = 'flex';
    this._descModalVisible = true;

    // Close on outside click
    const onOutside = (e: MouseEvent) => {
      if (!this._descModal.contains(e.target as Node)) {
        this.hideDescriptionModal();
        document.removeEventListener('click', onOutside);
      }
    };
    setTimeout(() => document.addEventListener('click', onOutside), 100);
  }

  /** Populate the description modal with a full DescriptionResponse. */
  populateDescriptionModal(resp: {
    title: string;
    subtitle: string;
    paragraphs: string[];
    stats: Array<{ label: string; value: string }>;
    stratigraphicSummary: Array<{ age: string; thickness: string; rockType: string; eventNote: string }>;
    historyTimeline: Array<{ simTick: number; event: string; name: string }>;
    providerUsed: string;
  }): void {
    if (!this._descModalVisible) return;
    this._descModal.innerHTML = '';

    // Header
    const header = document.createElement('div');
    Object.assign(header.style, { display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4px' });
    const titleEl = document.createElement('span');
    titleEl.textContent = '🔬 Geological Description';
    Object.assign(titleEl.style, { fontWeight: 'bold', fontSize: '13px', color: '#8af' });
    const closeBtn = document.createElement('button');
    closeBtn.textContent = '✕';
    Object.assign(closeBtn.style, { background: 'none', border: 'none', color: '#aaa', cursor: 'pointer', fontSize: '13px' });
    closeBtn.addEventListener('click', () => this.hideDescriptionModal());
    header.append(titleEl, closeBtn);
    this._descModal.appendChild(header);

    // Feature title + subtitle
    const featureTitle = document.createElement('div');
    featureTitle.textContent = resp.title;
    Object.assign(featureTitle.style, { fontSize: '15px', fontWeight: 'bold', color: '#fff', marginBottom: '2px' });
    this._descModal.appendChild(featureTitle);
    if (resp.subtitle) {
      const sub = document.createElement('div');
      sub.textContent = resp.subtitle;
      Object.assign(sub.style, { fontSize: '11px', opacity: '0.55', marginBottom: '8px' });
      this._descModal.appendChild(sub);
    }

    // Paragraphs
    for (const para of resp.paragraphs) {
      const p = document.createElement('p');
      p.textContent = para;
      Object.assign(p.style, { margin: '0 0 6px 0', lineHeight: '1.6' });
      this._descModal.appendChild(p);
    }

    // Stats table
    if (resp.stats.length > 0) {
      const sep = document.createElement('hr');
      Object.assign(sep.style, { border: 'none', borderTop: '1px solid rgba(255,255,255,0.1)', margin: '8px 0' });
      this._descModal.appendChild(sep);
      const table = document.createElement('table');
      Object.assign(table.style, { width: '100%', borderCollapse: 'collapse', fontSize: '11px' });
      for (const stat of resp.stats) {
        const tr = document.createElement('tr');
        const tdLabel = document.createElement('td');
        tdLabel.textContent = stat.label;
        Object.assign(tdLabel.style, { opacity: '0.55', paddingRight: '8px', paddingBottom: '2px' });
        const tdVal = document.createElement('td');
        tdVal.textContent = stat.value;
        tdVal.style.fontFamily = 'monospace';
        tr.append(tdLabel, tdVal);
        table.appendChild(tr);
      }
      this._descModal.appendChild(table);
    }

    // Stratigraphic summary
    if (resp.stratigraphicSummary.length > 0) {
      const stratTitle = document.createElement('div');
      stratTitle.textContent = 'Stratigraphic Column';
      Object.assign(stratTitle.style, { fontWeight: 'bold', fontSize: '11px', marginTop: '8px', opacity: '0.7' });
      this._descModal.appendChild(stratTitle);
      const strip = document.createElement('div');
      Object.assign(strip.style, { display: 'flex', flexDirection: 'column', gap: '2px', margin: '4px 0' });
      for (const row of resp.stratigraphicSummary) {
        const rowEl = document.createElement('div');
        Object.assign(rowEl.style, { display: 'flex', gap: '6px', fontSize: '10px', fontFamily: 'monospace' });
        rowEl.textContent = `${row.age}  ${row.thickness}  ${row.rockType}${row.eventNote ? '  ⚡' + row.eventNote : ''}`;
        if (row.eventNote) rowEl.style.color = '#fa8';
        strip.appendChild(rowEl);
      }
      this._descModal.appendChild(strip);
    }

    // History timeline
    if (resp.historyTimeline.length > 0) {
      const histTitle = document.createElement('div');
      histTitle.textContent = 'Feature History';
      Object.assign(histTitle.style, { fontWeight: 'bold', fontSize: '11px', marginTop: '8px', opacity: '0.7' });
      this._descModal.appendChild(histTitle);
      const ol = document.createElement('ol');
      Object.assign(ol.style, { margin: '4px 0', paddingLeft: '16px', fontSize: '10px', fontFamily: 'monospace' });
      for (const entry of resp.historyTimeline) {
        const li = document.createElement('li');
        li.textContent = `Tick ${entry.simTick}: ${entry.event} → ${entry.name}`;
        ol.appendChild(li);
      }
      this._descModal.appendChild(ol);
    }

    // Provider badge
    const badge = document.createElement('div');
    badge.textContent = `Generated by: ${resp.providerUsed}`;
    Object.assign(badge.style, { fontSize: '10px', opacity: '0.35', marginTop: '8px', textAlign: 'right' });
    this._descModal.appendChild(badge);
  }

  /** Append a token to the last paragraph in the description modal (for streaming). */
  appendDescriptionToken(token: string): void {
    if (!this._descModalVisible) return;
    let last = this._descModal.querySelector('p:last-of-type') as HTMLElement | null;
    if (!last) {
      last = document.createElement('p');
      Object.assign(last.style, { margin: '0 0 6px 0', lineHeight: '1.6' });
      this._descModal.appendChild(last);
    }
    last.textContent = (last.textContent ?? '') + token;
  }

  /** Hide (but don't destroy) the description modal. */
  hideDescriptionModal(): void {
    this._descModal.style.display = 'none';
    this._descModalVisible = false;
  }

  // ── Event Layer API (Phase D6) ─────────────────────────────────────────

  /** Register callback for event layer type changes. `null` = layer deactivated. */
  onEventLayerChange(cb: (eventType: string | null) => void): void {
    this._eventLayerChangeCb = cb;
  }

  /** Populate the event layer type dropdown with available types. */
  setEventLayerTypes(types: string[]): void {
    this._eventLayerSelect.innerHTML = '';
    const placeholder = document.createElement('option');
    placeholder.value = '';
    placeholder.textContent = types.length === 0 ? '— no event types yet —' : '— select type —';
    this._eventLayerSelect.appendChild(placeholder);
    for (const t of types) {
      const opt = document.createElement('option');
      opt.value = t;
      opt.textContent = t;
      this._eventLayerSelect.appendChild(opt);
    }
    // Dropdown is visible whenever the event layers toggle is active.
    this._eventLayerSelect.style.display = this._eventLayerActive ? 'block' : 'none';
  }

  /** Set event markers on the geological timeline. */
  setTimelineEvents(events: Array<{ timeMa: number; type: string; description: string }>): void {
    // Remove existing markers
    const existing = this.timelineBar.querySelectorAll('[data-timeline-event]');
    existing.forEach((m) => m.remove());

    if (events.length === 0) return;
    const maxTime = Math.max(...events.map((e) => e.timeMa), 1);

    for (const evt of events) {
      const pct = Math.min(100, Math.max(0, (evt.timeMa / maxTime) * 100));
      const marker = el('div', {
        position: 'absolute',
        left: `${pct}%`,
        top: '0',
        width: '4px',
        height: '100%',
        borderRadius: '2px',
        background: evt.type === 'extinction' ? '#e55' : '#fa4',
        opacity: '0.8',
        pointerEvents: 'none',
      });
      marker.dataset.timelineEvent = evt.description;
      marker.title = `${evt.timeMa} Ma – ${evt.description}`;
      this.timelineBar.appendChild(marker);
    }
  }

  /** Set the cursor position on the geological timeline. */
  setTimelineCursor(timeMa: number): void {
    // Cursor position is relative to the max time of currently placed markers
    const markers = this.timelineBar.querySelectorAll('[data-timeline-event]');
    let maxTime = 4600; // default Earth age
    if (markers.length > 0) {
      const times: number[] = [];
      markers.forEach((m) => {
        const title = (m as HTMLElement).title;
        const match = title.match(/^([\d.]+)/);
        if (match) times.push(Number(match[1]));
      });
      if (times.length > 0) maxTime = Math.max(...times, 1);
    }
    const pct = Math.min(100, Math.max(0, (timeMa / maxTime) * 100));
    this.timelineCursorEl.style.left = `${pct}%`;
  }

  // ── Layer Overlay API ──────────────────────────────────────────────────

  /** Register a callback for layer overlay toggles. */
  onLayerToggle(cb: (layer: string, active: boolean) => void): void {
    this.layerToggleCb = cb;
  }

  /**
   * Show a colour legend for the given layer.
   * @param title  Short name for the layer.
   * @param items  Array of { color: CSS colour string, label: description }.
   */
  showLayerLegend(title: string, items: Array<{ color: string; label: string }>): void {
    this.legendPanel.innerHTML = '';

    const titleEl = document.createElement('div');
    titleEl.textContent = title;
    titleEl.style.fontWeight = 'bold';
    titleEl.style.marginBottom = '4px';
    titleEl.style.opacity = '0.8';
    this.legendPanel.appendChild(titleEl);

    for (const item of items) {
      const row = el('div', { display: 'flex', alignItems: 'center', gap: '6px' });
      const swatch = el('div', {
        width: '14px',
        height: '14px',
        borderRadius: '2px',
        background: item.color,
        flexShrink: '0',
      });
      const lbl = document.createElement('span');
      lbl.textContent = item.label;
      row.append(swatch, lbl);
      this.legendPanel.appendChild(row);
    }

    this.legendPanel.style.display = 'flex';
  }

  /** Hide the layer legend. */
  hideLayerLegend(): void {
    this.legendPanel.style.display = 'none';
  }

  /** Show a brief engine-phase progress string in the HUD. */
  setProgressText(text: string): void {
    this.progressEl.textContent = text;
    const frozen = text.includes('⚠️ Frozen?');
    this.progressEl.dataset.frozen = String(frozen);
    this.progressEl.title = frozen ? 'Click to abort stuck request' : 'Click to view agent status';
    // Show a subtle indicator on the button when something is in progress
    this.progressEl.style.opacity = text ? '1' : '0.7';
  }

  // ── Agent status panel API ──────────────────────────────────────────────

  /** Update the per-agent status display. */
  updateAgentStatuses(statuses: Record<string, 'idle' | 'running' | 'done'>): void {
    for (const [key, el] of this.agentPanelRows) {
      const s = statuses[key] ?? 'idle';
      if (s === 'running') {
        el.textContent = '● running';
        el.style.color = '#4af';
        el.style.opacity = '1';
      } else if (s === 'done') {
        el.textContent = '✓ done';
        el.style.color = '#4c8';
        el.style.opacity = '0.8';
      } else {
        el.textContent = '○ idle';
        el.style.color = '#ccc';
        el.style.opacity = '0.5';
      }
    }
  }

  /** Toggle the agent status panel visibility. */
  private toggleAgentPanel(): void {
    this.agentPanelOpen = !this.agentPanelOpen;
    this.agentPanel.style.display = this.agentPanelOpen ? 'flex' : 'none';
    this.progressEl.style.background = this.agentPanelOpen
      ? 'rgba(255,255,255,0.15)'
      : 'none';
  }

  // ── Log Panel API ──────────────────────────────────────────────────────

  /** Update the log panel with timing stats and recent events. */
  updateLogPanel(
    stats: { tectonicMs: number; surfaceMs: number; atmosphereMs: number; vegetationMs: number; biomatterMs: number; totalMs: number; tectonicAdvectionMs?: number; tectonicCollisionMs?: number; tectonicBoundaryMs?: number; tectonicDynamicsMs?: number; tectonicVolcanismMs?: number } | null,
    events: Array<{ timeMa: number; type: string; description: string }>,
    tickCount?: number,
  ): void {
    // Tick count section
    const logTitle = this.logPanel.querySelector('[data-log-title]') as HTMLElement | null;
    if (logTitle) {
      logTitle.textContent = tickCount !== undefined ? `📊 Simulation Log (${tickCount} tick${tickCount === 1 ? '' : 's'})` : '📊 Simulation Log';
    }
    // Timing section
    this.logTimingEl.innerHTML = '';
    if (stats && stats.totalMs > 0) {
      const phases: [string, number][] = [];
      // Show sub-phase breakdown if available, otherwise show total
      if (stats.tectonicAdvectionMs !== undefined) {
        phases.push(
          ['⛰ Advection',   stats.tectonicAdvectionMs],
          ['⛰ Collision',   stats.tectonicCollisionMs ?? 0],
          ['⛰ Boundaries',  stats.tectonicBoundaryMs ?? 0],
          ['⛰ Dynamics',    stats.tectonicDynamicsMs ?? 0],
          ['🌋 Volcanism',   stats.tectonicVolcanismMs ?? 0],
        );
      } else {
        phases.push(['⛰ Tectonic', stats.tectonicMs]);
      }
      phases.push(
        ['🌊 Surface',    stats.surfaceMs],
        ['☁️ Atmosphere', stats.atmosphereMs],
        ['🌿 Vegetation', stats.vegetationMs],
        ['🧬 Biomatter',  stats.biomatterMs],
      );
      for (const [label, ms] of phases) {
        const row = el('div', { display: 'flex', justifyContent: 'space-between', gap: '12px' });
        const lbl = document.createElement('span');
        lbl.textContent = label;
        lbl.style.opacity = '0.7';
        const val = document.createElement('span');
        val.textContent = `${ms} ms`;
        val.style.color = ms > 1000 ? '#f84' : '#4af';
        row.append(lbl, val);
        this.logTimingEl.appendChild(row);
      }
      const totalRow = el('div', { display: 'flex', justifyContent: 'space-between', gap: '12px', borderTop: '1px solid rgba(255,255,255,0.1)', paddingTop: '4px', marginTop: '2px' });
      const totalLbl = document.createElement('span');
      totalLbl.textContent = 'Total';
      totalLbl.style.opacity = '0.9';
      totalLbl.style.fontWeight = 'bold';
      const totalVal = document.createElement('span');
      totalVal.textContent = `${stats.totalMs} ms`;
      totalVal.style.fontWeight = 'bold';
      totalVal.style.color = '#fff';
      totalRow.append(totalLbl, totalVal);
      this.logTimingEl.appendChild(totalRow);
    } else {
      this.logTimingEl.textContent = '— no tick yet —';
    }

    // Events section
    this.logEventsEl.innerHTML = '';
    if (events.length === 0) {
      this.logEventsEl.textContent = '— none —';
    } else {
      for (const evt of events.slice(-20).reverse()) {
        const row = document.createElement('div');
        row.style.fontSize = '11px';
        row.style.opacity = '0.8';
        row.textContent = `[${evt.timeMa.toFixed(0)} Ma] ${evt.type}: ${evt.description}`;
        this.logEventsEl.appendChild(row);
      }
    }
  }

  private logOpenCb: (() => void) | null = null;

  /** Register a callback invoked when the log panel is opened. */
  onLogOpen(cb: () => void): void { this.logOpenCb = cb; }

  /** Toggle the log panel visibility. */
  private toggleLogPanel(): void {
    this.logPanelOpen = !this.logPanelOpen;
    this.logPanel.style.display = this.logPanelOpen ? 'flex' : 'none';
    this.logBtn.style.background = this.logPanelOpen ? 'rgba(255,255,255,0.15)' : 'none';
    if (this.logPanelOpen) this.logOpenCb?.();
  }

  /** Open the log panel (used programmatically). */
  openLogPanel(): void {
    if (!this.logPanelOpen) this.toggleLogPanel();
  }

  /** Whether the log panel is currently open. */
  get isLogPanelOpen(): boolean { return this.logPanelOpen; }

  // ── Advanced Log View ──────────────────────────────────────────────────

  /** Toggle the advanced view container. */
  private _toggleAdvancedView(): void {
    this._advancedViewOpen = !this._advancedViewOpen;
    this._advancedViewContainer.style.display = this._advancedViewOpen ? 'flex' : 'none';
    this._advancedViewBtn.textContent = this._advancedViewOpen ? '▼ Advanced View' : '▶ Advanced View';
    if (this._advancedViewOpen) {
      // Enlarge log panel for graphs
      this.logPanel.style.minWidth = '360px';
      this.logPanel.style.maxHeight = '700px';
      this._renderAdvancedGraphs();
    } else {
      this.logPanel.style.minWidth = '280px';
      this.logPanel.style.maxHeight = '420px';
    }
  }

  /** Push a tick stats snapshot into the history ring buffer (max 50). */
  pushTickHistory(stats: { tectonicMs: number; surfaceMs: number; atmosphereMs: number; vegetationMs: number; biomatterMs: number; totalMs: number; tectonicAdvectionMs?: number; tectonicCollisionMs?: number; tectonicBoundaryMs?: number; tectonicDynamicsMs?: number; tectonicVolcanismMs?: number; isGpuActive?: boolean }, tickCount: number): void {
    this._tickHistory.push({ ...stats, tick: tickCount });
    this._lastTickStats = { ...stats };
    if (this._tickHistory.length > 50) this._tickHistory.shift();
    if (this._advancedViewOpen) {
      this._renderAdvancedGraphs();
      this._updateAdvancedLog();
    }
  }

  /** Update the processing status indicator with per-phase percentage and GPU/CPU tags. */
  setAdvancedProcessingStatus(statuses: Record<string, 'idle' | 'running' | 'done'>): void {
    if (!this._advancedViewOpen) return;
    const icons: Record<string, string> = { idle: '○', running: '⟳', done: '✓' };
    const colors: Record<string, string> = { idle: '#666', running: '#4af', done: '#4c8' };
    const total = this._lastTickStats?.totalMs ?? 0;

    const phaseMs: Record<string, number> = {
      'tectonic:advection':  this._lastTickStats?.tectonicAdvectionMs  ?? 0,
      'tectonic:collision':  this._lastTickStats?.tectonicCollisionMs  ?? 0,
      'tectonic:boundaries': this._lastTickStats?.tectonicBoundaryMs   ?? 0,
      'tectonic:dynamics':   this._lastTickStats?.tectonicDynamicsMs   ?? 0,
      'tectonic:volcanism':  this._lastTickStats?.tectonicVolcanismMs  ?? 0,
      surface:               this._lastTickStats?.surfaceMs     ?? 0,
      atmosphere:            this._lastTickStats?.atmosphereMs  ?? 0,
      vegetation:            this._lastTickStats?.vegetationMs  ?? 0,
      biomatter:             this._lastTickStats?.biomatterMs   ?? 0,
    };

    const lines: string[] = [];
    for (const [agent, status] of Object.entries(statuses)) {
      const computeTag = this._isGpu && AppShell.GPU_PHASES.has(agent) ? '<span style="color:#7ef;font-size:9px">[GPU]</span>' : '<span style="color:#adf;font-size:9px">[CPU]</span>';
      let pctStr = '';
      if (status === 'done' && total > 0) {
        pctStr = ` <span style="color:#999;font-size:9px">${AppShell._pct(phaseMs[agent] ?? 0, total)}%</span>`;
      }
      lines.push(`<div><span style="color:${colors[status]}">${icons[status]}</span> ${agent} ${computeTag}<span style="color:${colors[status]}">${status}</span>${pctStr}</div>`);
    }
    this._advancedProcessingEl.innerHTML = lines.join('');
  }

  /** Compute percentage of a phase ms relative to total, rounded. */
  private static _pct(ms: number, total: number): number {
    return total > 0 ? Math.round((ms / total) * 100) : 0;
  }

  /** Refresh the fine-grained tick log in the advanced view. */
  private _updateAdvancedLog(): void {
    if (!this._lastTickStats) return;
    const s = this._lastTickStats;
    const compute = (s.isGpuActive ?? this._isGpu) ? 'GPU' : 'CPU';
    const total = s.totalMs;
    const lines: string[] = [];
    const pct = (ms: number) => total > 0 ? `${AppShell._pct(ms, total)}%` : '—';

    const hasSubPhases = s.tectonicAdvectionMs !== undefined;
    if (hasSubPhases) {
      lines.push(`<span style="color:#7ef">[${compute}]</span> <span style="color:#ef4">Tectonic sub-phases:</span>`);
      lines.push(`  ⛰ Advect  ${s.tectonicAdvectionMs}ms (${pct(s.tectonicAdvectionMs ?? 0)})`);
      lines.push(`  ⛰ Collide ${s.tectonicCollisionMs}ms (${pct(s.tectonicCollisionMs ?? 0)})`);
      lines.push(`  ⛰ Bounds  ${s.tectonicBoundaryMs}ms (${pct(s.tectonicBoundaryMs ?? 0)})`);
      lines.push(`  ⛰ Dynamics ${s.tectonicDynamicsMs}ms (${pct(s.tectonicDynamicsMs ?? 0)})`);
      lines.push(`  🌋 Volcanism ${s.tectonicVolcanismMs}ms (${pct(s.tectonicVolcanismMs ?? 0)})`);
    } else {
      lines.push(`<span style="color:#7ef">[${compute}]</span> ⛰ Tectonic ${s.tectonicMs}ms (${pct(s.tectonicMs)})`);
    }
    lines.push(`<span style="color:#7ef">[${compute}]</span> 🌊 Surface ${s.surfaceMs}ms (${pct(s.surfaceMs)})`);
    lines.push(`<span style="color:#7ef">[${compute}]</span> ☁️ Atmosphere ${s.atmosphereMs}ms (${pct(s.atmosphereMs)})`);
    lines.push(`<span style="color:#adf">[CPU]</span> 🌿 Vegetation ${s.vegetationMs}ms (${pct(s.vegetationMs)})`);
    lines.push(`<span style="color:#adf">[CPU]</span> 🧬 Biomatter ${s.biomatterMs}ms (${pct(s.biomatterMs)})`);
    lines.push(`<b>Total: ${total}ms</b>`);

    this._advancedLogEl.innerHTML = lines.map(l => `<div>${l}</div>`).join('');
    // Scroll to bottom
    this._advancedLogEl.scrollTop = this._advancedLogEl.scrollHeight;
  }

  /** Render the timing history and computational load charts on Canvas. */
  private _renderAdvancedGraphs(): void {
    this._renderTimingGraph();
    this._renderLoadGraph();
  }

  /** Stacked area chart: per-agent timing over tick history. */
  private _renderTimingGraph(): void {
    const canvas = this._advancedTimingCanvas;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    // Sync canvas buffer dimensions with its CSS-rendered size to avoid stretching.
    const cw = canvas.clientWidth || 260;
    const ch = canvas.clientHeight || 120;
    if (canvas.width !== cw) canvas.width = cw;
    if (canvas.height !== ch) canvas.height = ch;
    const w = canvas.width;
    const h = canvas.height;
    ctx.clearRect(0, 0, w, h);

    const data = this._tickHistory;
    if (data.length < 2) {
      ctx.fillStyle = '#666';
      ctx.font = '10px monospace';
      ctx.fillText('Waiting for tick data…', 10, h / 2);
      return;
    }

    // Find max total for Y-axis scaling
    const maxTotal = Math.max(1, ...data.map(d => d.totalMs));
    const barW = Math.max(2, (w - 30) / data.length);

    const agents: Array<{ key: keyof typeof data[0]; color: string; label: string }> = [
      { key: 'biomatterMs', color: '#8b5cf6', label: 'Biomatter' },
      { key: 'vegetationMs', color: '#22c55e', label: 'Vegetation' },
      { key: 'atmosphereMs', color: '#60a5fa', label: 'Atmosphere' },
      { key: 'surfaceMs', color: '#f59e0b', label: 'Surface' },
      { key: 'tectonicMs', color: '#ef4444', label: 'Tectonic' },
    ];

    // Draw stacked bars
    for (let i = 0; i < data.length; i++) {
      const d = data[i];
      const x = 25 + i * barW;
      let y = h - 2;
      for (const agent of agents) {
        const val = d[agent.key] as number;
        const barH = (val / maxTotal) * (h - 15);
        ctx.fillStyle = agent.color;
        ctx.fillRect(x, y - barH, barW - 1, barH);
        y -= barH;
      }
    }

    // Y-axis labels
    ctx.fillStyle = '#888';
    ctx.font = '9px monospace';
    ctx.fillText(`${maxTotal}ms`, 0, 10);
    ctx.fillText('0', 0, h - 2);

    // Legend (compact)
    let lx = 25;
    for (const agent of agents.slice().reverse()) {
      ctx.fillStyle = agent.color;
      ctx.fillRect(lx, 1, 6, 6);
      ctx.fillStyle = '#aaa';
      ctx.fillText(agent.label.substring(0, 4), lx + 8, 7);
      lx += 40;
    }
  }

  /** Line chart: total ms per tick (computational load). */
  private _renderLoadGraph(): void {
    const canvas = this._advancedLoadCanvas;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    // Sync canvas buffer dimensions with its CSS-rendered size to avoid stretching.
    const cw = canvas.clientWidth || 260;
    const ch = canvas.clientHeight || 80;
    if (canvas.width !== cw) canvas.width = cw;
    if (canvas.height !== ch) canvas.height = ch;
    const w = canvas.width;
    const h = canvas.height;
    ctx.clearRect(0, 0, w, h);

    const data = this._tickHistory;
    if (data.length < 2) {
      ctx.fillStyle = '#666';
      ctx.font = '10px monospace';
      ctx.fillText('Waiting for tick data…', 10, h / 2);
      return;
    }

    const maxMs = Math.max(1, ...data.map(d => d.totalMs));
    const stepX = (w - 30) / (data.length - 1);

    // Threshold zones
    const thresholds = [
      { ms: 100, color: 'rgba(239,68,68,0.08)', label: '>100ms (slow)' },
      { ms: 50, color: 'rgba(245,158,11,0.08)', label: '>50ms' },
    ];
    for (const t of thresholds) {
      if (maxMs > t.ms) {
        const y = h - 4 - ((t.ms / maxMs) * (h - 14));
        ctx.fillStyle = t.color;
        ctx.fillRect(25, y, w - 30, h - 4 - y);
        ctx.strokeStyle = 'rgba(255,255,255,0.1)';
        ctx.setLineDash([3, 3]);
        ctx.beginPath();
        ctx.moveTo(25, y);
        ctx.lineTo(w, y);
        ctx.stroke();
        ctx.setLineDash([]);
        ctx.fillStyle = '#666';
        ctx.font = '8px monospace';
        ctx.fillText(`${t.ms}`, 0, y + 3);
      }
    }

    // Line
    ctx.strokeStyle = '#4af';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    for (let i = 0; i < data.length; i++) {
      const x = 25 + i * stepX;
      const y = h - 4 - ((data[i].totalMs / maxMs) * (h - 14));
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();

    // Fill under line
    ctx.lineTo(25 + (data.length - 1) * stepX, h - 4);
    ctx.lineTo(25, h - 4);
    ctx.closePath();
    ctx.fillStyle = 'rgba(96,165,250,0.15)';
    ctx.fill();

    // Labels
    ctx.fillStyle = '#888';
    ctx.font = '9px monospace';
    ctx.fillText(`${maxMs}ms`, 0, 10);
    ctx.fillText('0', 0, h - 2);

    // Current value
    const last = data[data.length - 1];
    const color = last.totalMs > 100 ? '#f84' : last.totalMs > 50 ? '#fb0' : '#4c8';
    ctx.fillStyle = color;
    ctx.font = 'bold 10px monospace';
    ctx.fillText(`${last.totalMs}ms`, w - 50, 10);
  }

  // ── LLM Settings Panel API (Phase D3) ──────────────────────────────────

  /**
   * Populate the LLM provider list panel with the given provider info.
   * Called by main.ts after fetching GET /api/llm/providers.
   */
  setLlmProviders(providers: Array<{
    name: string;
    displayName: string;
    isAvailable: boolean;
    needsSetup: boolean;
    activeModel: string | null;
    statusMessage: string;
    isActive: boolean;
  }>): void {
    this._llmProviderListEl.innerHTML = '';
    for (const p of providers) {
      const row = el('div', { display: 'flex', alignItems: 'center', gap: '6px', padding: '4px 0' });

      // Radio button
      const radio = document.createElement('input');
      radio.type = 'radio';
      radio.name = 'llm-provider';
      radio.value = p.name;
      radio.checked = p.isActive;
      row.appendChild(radio);

      // Name + status
      const nameEl = document.createElement('span');
      nameEl.textContent = p.displayName;
      nameEl.style.flex = '1';
      nameEl.style.opacity = p.isAvailable ? '1' : '0.5';
      row.appendChild(nameEl);

      const statusEl = document.createElement('span');
      statusEl.textContent = p.isAvailable ? `✓ ${p.statusMessage}` : `✗ ${p.statusMessage}`;
      statusEl.style.fontSize = '10px';
      statusEl.style.color = p.isAvailable ? '#4c8' : '#f84';
      statusEl.style.flex = '1.5';
      row.appendChild(statusEl);

      // Setup button for local providers that need it
      if (p.needsSetup) {
        const setupBtn = document.createElement('button');
        setupBtn.textContent = 'Setup ▶';
        styleBtn(setupBtn);
        setupBtn.style.padding = '2px 6px';
        setupBtn.style.fontSize = '10px';
        setupBtn.addEventListener('click', () => {
          this._llmSetupCb?.(p.name);
          this._showSetupProgress(p.name);
        });
        row.appendChild(setupBtn);
      }

      this._llmProviderListEl.appendChild(row);

      // API key field for cloud providers
      if (p.name === 'Gemini' || p.name === 'OpenAi' || p.name === 'Anthropic') {
        const keyRow = el('div', { display: 'none', gap: '4px', paddingLeft: '20px', alignItems: 'center' });
        const keyInput = document.createElement('input');
        keyInput.type = 'password';
        keyInput.placeholder = 'API key';
        keyInput.style.flex = '1';
        Object.assign(keyInput.style, {
          background: 'rgba(255,255,255,0.07)',
          border: '1px solid rgba(255,255,255,0.15)',
          borderRadius: '3px',
          color: '#eee',
          fontSize: '11px',
          padding: '3px 6px',
        });
        const applyBtn = document.createElement('button');
        applyBtn.textContent = 'Apply';
        styleBtn(applyBtn);
        applyBtn.style.fontSize = '10px';
        applyBtn.style.padding = '2px 8px';
        applyBtn.addEventListener('click', () => {
          if (keyInput.value) {
            this._llmSettingsChangedCb?.(p.name, { apiKey: keyInput.value });
          } else {
            this._llmSettingsChangedCb?.(p.name);
          }
        });
        keyRow.append(keyInput, applyBtn);

        radio.addEventListener('change', () => {
          if (radio.checked) keyRow.style.display = 'flex';
        });
        if (p.isActive) keyRow.style.display = 'flex';

        this._llmProviderListEl.appendChild(keyRow);
      } else {
        // For non-cloud providers, selecting the radio triggers the save immediately
        radio.addEventListener('change', () => {
          if (radio.checked) this._llmSettingsChangedCb?.(p.name);
        });
      }
    }
  }

  /**
   * Show a setup progress event inside the setup sub-panel.
   * Called by main.ts from the EventSource handler.
   */
  showLlmSetupProgress(event: {
    step: string;
    percentTotal: number;
    detail: string;
    isComplete: boolean;
    isError: boolean;
    errorMessage: string | null;
  }): void {
    this._llmSetupPanelEl.style.display = 'flex';
    this._llmSetupPanelEl.innerHTML = '';

    const stepEl = document.createElement('div');
    stepEl.textContent = event.isError ? `❌ ${event.errorMessage ?? 'Error'}` : `⏳ ${event.step}`;
    stepEl.style.fontWeight = 'bold';
    stepEl.style.color = event.isError ? '#f84' : event.isComplete ? '#4c8' : '#ccc';
    this._llmSetupPanelEl.appendChild(stepEl);

    const progressEl = document.createElement('progress');
    progressEl.value = event.percentTotal;
    progressEl.max = 100;
    progressEl.style.width = '100%';
    progressEl.style.accentColor = '#4af';
    this._llmSetupPanelEl.appendChild(progressEl);

    if (event.detail) {
      const detailEl = document.createElement('div');
      detailEl.textContent = event.detail;
      Object.assign(detailEl.style, { fontSize: '10px', opacity: '0.65', fontFamily: 'monospace' });
      this._llmSetupPanelEl.appendChild(detailEl);
    }

    if (event.isComplete) {
      stepEl.textContent = '✓ Complete — provider ready';
      stepEl.style.color = '#4c8';
    }
  }

  /**
   * Register a callback invoked when the user changes the active LLM provider
   * or updates its settings (e.g., API key). The callback should call PUT /api/llm/active.
   */
  onLlmSettingsChanged(cb: (
    provider: string,
    settings?: { apiKey?: string; model?: string; baseUrl?: string }
  ) => void): void {
    this._llmSettingsChangedCb = cb;
  }

  /** Register a callback invoked when the user clicks Setup ▶ for a local provider. */
  onLlmSetup(cb: (provider: string) => void): void {
    this._llmSetupCb = cb;
  }

  /** Programmatically open the LLM settings panel. */
  openLlmPanel(): void {
    if (!this._llmPanelOpen) this._toggleLlmPanel();
  }

  private _showSetupProgress(provider: string): void {
    this._llmSetupPanelEl.style.display = 'flex';
    this._llmSetupPanelEl.innerHTML = '';
    const msg = document.createElement('div');
    msg.textContent = `⏳ Starting setup for ${provider}…`;
    this._llmSetupPanelEl.appendChild(msg);
  }

  private _toggleLlmPanel(): void {
    this._llmPanelOpen = !this._llmPanelOpen;
    this._llmPanel.style.display = this._llmPanelOpen ? 'flex' : 'none';
    this._llmBtn.style.background = this._llmPanelOpen ? 'rgba(255,255,255,0.15)' : 'none';
  }

  dispose(): void {
    this.root.innerHTML = '';
    this.newPlanetCb = null;
    this.logOpenCb = null;
    this.pauseToggleCb = null;
    this.rateChangeCb = null;
    this.drawModeCb = null;
    this.labelToggleCb = null;
    this.exportPngCb = null;
    this.closeCrossSectionCb = null;
    this.inspectClickCb = null;
    this.layerToggleCb = null;
    this.zoomInCb = null;
    this.zoomOutCb = null;
    this.zoomResetCb = null;
    this.saveStateCb = null;
    this.loadStateCb = null;
    this.weatherMonthChangeCb = null;
    this.windToggleCb = null;
    this.abortRequestCb = null;
    this._llmSettingsChangedCb = null;
    this._llmSetupCb = null;
    this._descOpenCb = null;
    this._eventLayerChangeCb = null;
  }

  // ── Sidebar toggle ─────────────────────────────────────────────────────

  private toggleSidebar(): void {
    this.sidebarOpen = !this.sidebarOpen;
    if (this.sidebarOpen) {
      this.sidebar.style.transform = 'translateX(0)';
      this.sidebarToggle.style.right = '260px';
      this.sidebarToggle.textContent = '▶';
    } else {
      this.sidebar.style.transform = 'translateX(100%)';
      this.sidebarToggle.style.right = '0';
      this.sidebarToggle.textContent = '◀';
    }
  }
}

// ── Helpers ──────────────────────────────────────────────────────────────────

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/** Create a styled div. */
function el(
  tag: string,
  styles: Record<string, string>,
): HTMLElement {
  const node = document.createElement(tag);
  Object.assign(node.style, styles);
  return node;
}

/** Apply a base button style. */
function styleBtn(btn: HTMLButtonElement, primary = false): void {
  Object.assign(btn.style, {
    background: primary ? '#2a6' : 'rgba(255,255,255,0.08)',
    color: '#eee',
    border: 'none',
    borderRadius: '4px',
    padding: '6px 12px',
    cursor: 'pointer',
    fontSize: '13px',
    fontFamily: 'inherit',
  } as CSSStyleDeclaration);
}

/** Map slider position (0-1000) to a log-scale rate (0.001-100). */
function sliderToRate(v: number): number {
  // 0 → 0.001, 500 → ~0.316, 1000 → 100
  const minLog = Math.log10(0.001); // -3
  const maxLog = Math.log10(100);   //  2
  return 10 ** (minLog + (v / 1000) * (maxLog - minLog));
}

/** Format a large number with K suffix. */
function formatCount(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`;
  return String(n);
}
