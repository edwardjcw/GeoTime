// ─── Application Shell ──────────────────────────────────────────────────────
// Builds the full UI chrome: viewport for the 3D globe, collapsible sidebar,
// cross-section panel, and a semi-transparent HUD bar.  All DOM is created
// programmatically.

export interface InspectInfo {
  lat: number;
  lon: number;
  elevation: number;
  rockType: string;
  soilOrder: string;
  temperature: number;
  precipitation: number;
  biomass: number;
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

  // ── Inspect panel elements ──────────────────────────────────────────────
  private inspectPanel: HTMLElement;
  private inspectContent: HTMLElement;

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

  // ── Save/Load elements ──────────────────────────────────────────────────
  private saveStateBtn: HTMLButtonElement = document.createElement('button');
  private loadStateBtn: HTMLButtonElement = document.createElement('button');

  // ── First-person indicator ──────────────────────────────────────────────
  private firstPersonEl: HTMLSpanElement = document.createElement('span');

  // ── Weather month selector ───────────────────────────────────────────────
  private weatherMonthPanel: HTMLElement = document.createElement('div');
  private weatherMonthLabel: HTMLSpanElement = document.createElement('span');
  private _currentWeatherMonth = 0;

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
  private abortRequestCb: (() => void) | null = null;

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
    inspectHeader.append(inspectTitle, inspectCloseBtn);
    this.inspectPanel.appendChild(inspectHeader);

    this.inspectContent = el('div', {
      display: 'flex',
      flexDirection: 'column',
      gap: '2px',
    });
    this.inspectPanel.appendChild(this.inspectContent);
    this.root.appendChild(this.inspectPanel);

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
    this.weatherMonthPanel.append(prevMonthBtn, this.weatherMonthLabel, nextMonthBtn);
    this.root.appendChild(this.weatherMonthPanel);

    this.hud.append(this.fpsEl, this.triEl, this.timeEl, this.pauseBtn, this.firstPersonEl, this.progressEl);
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
    this.rateLabel.textContent = 'Rate: 1.00 Ma/s';
    this.rateLabel.style.opacity = '0.6';

    this.rateSlider = document.createElement('input');
    this.rateSlider.type = 'range';
    // Use a logarithmic mapping: slider 0..1000 → rate 0.001..100
    this.rateSlider.min = '0';
    this.rateSlider.max = '1000';
    this.rateSlider.value = '600'; // default ≈ 1
    this.rateSlider.style.width = '100%';
    this.rateSlider.style.accentColor = '#4af';
    this.rateSlider.addEventListener('input', () => {
      const rate = sliderToRate(Number(this.rateSlider.value));
      this.rateLabel.textContent = `Rate: ${rate.toFixed(2)} Ma/s`;
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
    const layerNames = ['plates', 'temperature', 'precipitation', 'biome', 'soil', 'clouds', 'biomass', 'topo', 'weather'];
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
      this.timeEl.textContent = `Time: ${(timeMa / 1000).toFixed(2)} Ga`;
    } else {
      this.timeEl.textContent = `Time: ${timeMa.toFixed(2)} Ma`;
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

  onNewPlanet(cb: () => void): void {
    this.newPlanetCb = cb;
  }

  onPauseToggle(cb: () => void): void {
    this.pauseToggleCb = cb;
  }

  onRateChange(cb: (rate: number) => void): void {
    this.rateChangeCb = cb;
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

  /** Show the weather month selector panel. */
  showWeatherMonthSelector(month: number): void {
    this._currentWeatherMonth = month;
    this.weatherMonthLabel.textContent = `Weather Month: ${MONTH_NAMES[month]}`;
    this.weatherMonthPanel.style.display = 'flex';
  }

  /** Hide the weather month selector panel. */
  hideWeatherMonthSelector(): void {
    this.weatherMonthPanel.style.display = 'none';
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
    this.inspectContent.innerHTML = '';
    const lines: [string, string][] = [
      ['Lat/Lon', `${info.lat.toFixed(2)}°, ${info.lon.toFixed(2)}°`],
      ['Elevation', `${info.elevation.toFixed(0)} m`],
      ['Rock Type', info.rockType],
      ['Soil Order', info.soilOrder],
      ['Temperature', `${info.temperature.toFixed(1)} °C`],
      ['Precipitation', `${info.precipitation.toFixed(0)} mm/yr`],
      ['Biomass', `${info.biomass.toFixed(1)} kg/m²`],
    ];
    for (const [label, value] of lines) {
      const row = el('div', { display: 'flex', justifyContent: 'space-between' });
      const lbl = document.createElement('span');
      lbl.textContent = label;
      lbl.style.opacity = '0.6';
      const val = document.createElement('span');
      val.textContent = value;
      row.append(lbl, val);
      this.inspectContent.appendChild(row);
    }
    this.inspectPanel.style.display = 'flex';
  }

  /** Hide the inspect panel. */
  hideInspectPanel(): void {
    this.inspectPanel.style.display = 'none';
  }

  // ── Timeline API ───────────────────────────────────────────────────────

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

  dispose(): void {
    this.root.innerHTML = '';
    this.newPlanetCb = null;
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
    this.abortRequestCb = null;
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
