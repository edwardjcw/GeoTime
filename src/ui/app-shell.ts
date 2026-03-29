// ─── Application Shell ──────────────────────────────────────────────────────
// Builds the full UI chrome: viewport for the 3D globe, collapsible sidebar,
// and a semi-transparent HUD bar.  All DOM is created programmatically.

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

  // ── Callbacks ───────────────────────────────────────────────────────────
  private newPlanetCb: (() => void) | null = null;
  private pauseToggleCb: (() => void) | null = null;
  private rateChangeCb: ((rate: number) => void) | null = null;

  private sidebarOpen = true;

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

    this.hud.append(this.fpsEl, this.triEl, this.timeEl, this.pauseBtn);
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

    this.root.appendChild(this.sidebar);

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

  onNewPlanet(cb: () => void): void {
    this.newPlanetCb = cb;
  }

  onPauseToggle(cb: () => void): void {
    this.pauseToggleCb = cb;
  }

  onRateChange(cb: (rate: number) => void): void {
    this.rateChangeCb = cb;
  }

  dispose(): void {
    this.root.innerHTML = '';
    this.newPlanetCb = null;
    this.pauseToggleCb = null;
    this.rateChangeCb = null;
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
