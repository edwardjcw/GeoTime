// @vitest-environment jsdom
// Unit tests for cell info (InspectInfo) display in the app shell.

import { describe, it, expect, beforeEach } from 'vitest';
import { AppShell, InspectInfo } from '../src/ui/app-shell';

function makeFullInfo(): InspectInfo {
  return {
    lat: 45.0,
    lon: -90.0,
    elevation: 1200,
    crustThickness: 35.5,
    rockType: 'Granite',
    rockAge: 250.0,
    plateId: 3,
    soilOrder: 'Alfisol',
    soilDepth: 1.2,
    temperature: 12.5,
    precipitation: 750,
    biomass: 8.3,
    biomatterDensity: 0.45,
    organicCarbon: 2.1,
    reefPresent: false,
  };
}

describe('AppShell – showInspectPanel', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
  });

  it('should show all basic fields', () => {
    const info = makeFullInfo();
    shell.showInspectPanel(info);
    const text = root.textContent ?? '';
    expect(text).toContain('45.00°');
    expect(text).toContain('1200 m');
    expect(text).toContain('Granite');
    expect(text).toContain('Alfisol');
    expect(text).toContain('12.5 °C');
    expect(text).toContain('750 mm/yr');
    expect(text).toContain('8.3 kg/m²');
  });

  it('should show crust thickness', () => {
    shell.showInspectPanel(makeFullInfo());
    expect(root.textContent).toContain('35.5 km');
  });

  it('should show rock age', () => {
    shell.showInspectPanel(makeFullInfo());
    expect(root.textContent).toContain('250.0 Ma');
  });

  it('should show plate ID', () => {
    shell.showInspectPanel(makeFullInfo());
    expect(root.textContent).toContain('Plate ID');
    expect(root.textContent).toContain('3');
  });

  it('should show soil depth', () => {
    shell.showInspectPanel(makeFullInfo());
    expect(root.textContent).toContain('1.20 m');
  });

  it('should show biomatter density', () => {
    shell.showInspectPanel(makeFullInfo());
    expect(root.textContent).toContain('0.45 kg C/m²');
  });

  it('should show organic carbon', () => {
    shell.showInspectPanel(makeFullInfo());
    expect(root.textContent).toContain('2.10 kg C/m²');
  });

  it('should show reef as absent when false', () => {
    shell.showInspectPanel(makeFullInfo());
    expect(root.textContent).toContain('—');
  });

  it('should show reef as present when true', () => {
    const info = makeFullInfo();
    info.reefPresent = true;
    shell.showInspectPanel(info);
    expect(root.textContent).toContain('✅ present');
  });

  it('should make the panel visible', () => {
    const panelBefore = root.querySelector('[style*="display: none"]') as HTMLElement | null;
    // Panel starts hidden
    shell.showInspectPanel(makeFullInfo());
    const visiblePanels = Array.from(root.querySelectorAll('div')).filter(
      (el) => (el as HTMLElement).style.display === 'flex',
    );
    expect(visiblePanels.length).toBeGreaterThan(0);
  });

  it('should update values when called again', () => {
    shell.showInspectPanel(makeFullInfo());
    const updated = makeFullInfo();
    updated.temperature = 99.9;
    shell.showInspectPanel(updated);
    expect(root.textContent).toContain('99.9 °C');
    expect(root.textContent).not.toContain('12.5 °C');
  });
});

describe('AppShell – updateAgentStatuses', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
  });

  it('should show all five agents', () => {
    shell.updateAgentStatuses({
      tectonic: 'running',
      surface: 'running',
      atmosphere: 'running',
      vegetation: 'running',
      biomatter: 'running',
    });
    const text = root.textContent ?? '';
    expect(text).toContain('running');
  });

  it('should show done state', () => {
    shell.updateAgentStatuses({
      tectonic: 'done',
      surface: 'done',
      atmosphere: 'done',
      vegetation: 'done',
      biomatter: 'done',
    });
    const text = root.textContent ?? '';
    expect(text).toContain('done');
  });
});

describe('AppShell – updateLogPanel', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
  });

  it('should show timing when stats are provided', () => {
    shell.updateLogPanel(
      { tectonicMs: 200, surfaceMs: 100, atmosphereMs: 80, vegetationMs: 40, biomatterMs: 50, totalMs: 470 },
      [],
    );
    const text = root.textContent ?? '';
    expect(text).toContain('200 ms');
    expect(text).toContain('470 ms');
  });

  it('should show events in the log panel', () => {
    shell.updateLogPanel(null, [
      { timeMa: -4000, type: 'VOLCANIC_ERUPTION', description: 'Big eruption' },
    ]);
    const text = root.textContent ?? '';
    expect(text).toContain('VOLCANIC_ERUPTION');
    expect(text).toContain('Big eruption');
  });

  it('should show placeholder when no timing stats', () => {
    shell.updateLogPanel(null, []);
    const text = root.textContent ?? '';
    expect(text).toContain('no tick yet');
  });

  it('should show placeholder when no events', () => {
    shell.updateLogPanel(null, []);
    const text = root.textContent ?? '';
    expect(text).toContain('none');
  });
});

describe('AppShell – setSimTime (3 decimal places)', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
  });

  it('should show time in Ma with 3 decimal places', () => {
    shell.setSimTime(-500.123456);
    const text = root.textContent ?? '';
    expect(text).toMatch(/Time: -?[\d]+\.\d{3} Ma/);
  });

  it('should show time in Ga with 3 decimal places when ≥1000 Ma', () => {
    shell.setSimTime(1500);
    const text = root.textContent ?? '';
    expect(text).toMatch(/Time: [\d]+\.\d{3} Ga/);
  });
});

describe('AppShell – rate slider defaults', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
  });

  it('should display default rate label with 3 decimal places', () => {
    const text = root.textContent ?? '';
    expect(text).toMatch(/Rate: 0\.010 Ma\/s/);
  });

  it('should return initial rate of ~0.010 from getRate()', () => {
    const rate = shell.getRate();
    expect(rate).toBeCloseTo(0.010, 2);
  });
});

describe('AppShell – event layer dropdown', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
  });

  it('should show dropdown when event layers is toggled on (even with no types)', () => {
    // No types populated yet — find the toggle button and click it
    const btn = Array.from(root.querySelectorAll('button')).find(
      (b) => b.textContent === 'event layers',
    );
    expect(btn).toBeTruthy();
    btn!.click();
    // After toggle the select should be visible
    const select = root.querySelector('select');
    expect(select).toBeTruthy();
    expect(select!.style.display).not.toBe('none');
  });

  it('should show dropdown with types after setEventLayerTypes', () => {
    // First activate the toggle
    const btn = Array.from(root.querySelectorAll('button')).find(
      (b) => b.textContent === 'event layers',
    );
    btn!.click();
    shell.setEventLayerTypes(['ImpactEjecta', 'VolcanicAsh']);
    const select = root.querySelector('select')!;
    expect(select.style.display).not.toBe('none');
    expect(select.options.length).toBe(3); // placeholder + 2 types
  });

  it('should hide dropdown when event layers is toggled off', () => {
    const btn = Array.from(root.querySelectorAll('button')).find(
      (b) => b.textContent === 'event layers',
    );
    btn!.click(); // on
    btn!.click(); // off
    const select = root.querySelector('select')!;
    expect(select.style.display).toBe('none');
  });
});

describe('AppShell – advanced view processing status with GPU/CPU tags and percentage', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
    // Open the log panel first, then the advanced view
    const logBtn = root.querySelector<HTMLButtonElement>('[data-log-btn]');
    if (logBtn) logBtn.click();
  });

  it('pushTickHistory stores stats and advancedLogEl shows tick data when view is open', () => {
    // Open the advanced view button
    const advBtn = Array.from(root.querySelectorAll('button')).find(
      (b) => b.textContent?.includes('Advanced View'),
    );
    if (advBtn) advBtn.click(); // open advanced view

    shell.pushTickHistory(
      {
        tectonicMs: 100, surfaceMs: 50, atmosphereMs: 30, vegetationMs: 10, biomatterMs: 5,
        totalMs: 195, isGpuActive: false,
        tectonicAdvectionMs: 40, tectonicCollisionMs: 30, tectonicBoundaryMs: 15,
        tectonicDynamicsMs: 10, tectonicVolcanismMs: 5,
      },
      1,
    );
    // The advanced log element should contain tick data with total ms
    const logText = root.textContent ?? '';
    expect(logText).toContain('195ms');
    // Should contain CPU tag since isGpuActive = false
    expect(logText).toContain('[CPU]');
  });

  it('setComputeMode with GPU=true causes GPU tags to appear in processing status', () => {
    shell.setComputeMode(true, 'Test GPU', 4096);

    // Open the advanced view
    const advBtn = Array.from(root.querySelectorAll('button')).find(
      (b) => b.textContent?.includes('Advanced View'),
    );
    if (advBtn) advBtn.click();

    // Push tick stats so lastTickStats is populated
    shell.pushTickHistory(
      { tectonicMs: 100, surfaceMs: 50, atmosphereMs: 30, vegetationMs: 10, biomatterMs: 5, totalMs: 195 },
      1,
    );

    // Trigger processing status update
    shell.setAdvancedProcessingStatus({
      'tectonic:advection': 'done',
      surface: 'done',
      vegetation: 'done',
    });

    const html = root.innerHTML;
    // GPU phases should show [GPU] tag, CPU phases [CPU]
    expect(html).toContain('[GPU]'); // tectonic:advection and surface are GPU
    expect(html).toContain('[CPU]'); // vegetation is always CPU
  });
});

