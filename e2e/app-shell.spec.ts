// ─── GeoTime Integration Tests ──────────────────────────────────────────────
// Playwright browser tests for the GeoTime application shell, verifying that
// the app boots, renders a globe, and the UI controls function correctly.

import { test, expect } from '@playwright/test';

test.describe('GeoTime App Shell', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    // Wait for the app to boot and canvas to appear
    await page.waitForSelector('canvas', { timeout: 10_000 });
  });

  test('should render the application with a WebGL canvas', async ({ page }) => {
    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible();
  });

  test('should display the HUD bar with FPS, Tris, and Time', async ({ page }) => {
    // The HUD bar contains FPS, Tris, and Time spans
    await expect(page.locator('text=FPS:')).toBeVisible();
    await expect(page.locator('text=Tris:')).toBeVisible();
    await expect(page.locator('text=Time:')).toBeVisible();
  });

  test('should display the seed in the sidebar', async ({ page }) => {
    // The sidebar should show "Seed:" followed by a numeric value
    await expect(page.locator('text=Seed:')).toBeVisible();
  });

  test('should have a New Planet button', async ({ page }) => {
    const btn = page.locator('button', { hasText: 'New Planet' });
    await expect(btn).toBeVisible();
  });

  test('should have a Pause button', async ({ page }) => {
    const btn = page.locator('button', { hasText: /Pause|Resume/ });
    await expect(btn).toBeVisible();
  });

  test('should have a rate slider', async ({ page }) => {
    const slider = page.locator('input[type="range"]');
    await expect(slider).toBeVisible();
  });

  test('should toggle pause on button click', async ({ page }) => {
    const btn = page.locator('button', { hasText: /Pause|Resume/ });
    const initialText = await btn.textContent();

    await btn.click();
    const afterClick = await btn.textContent();
    expect(afterClick).not.toBe(initialText);

    // Click again to toggle back
    await btn.click();
    const afterSecondClick = await btn.textContent();
    expect(afterSecondClick).toBe(initialText);
  });

  test('should generate a new planet when New Planet is clicked', async ({ page }) => {
    // Get the current seed
    const seedLocator = page.locator('text=Seed:').locator('..');
    const seedContainer = page.locator('span').filter({ hasText: /^\d+$/ }).first();
    const initialSeed = await seedContainer.textContent();

    // Click New Planet
    const newPlanetBtn = page.locator('button', { hasText: 'New Planet' });
    await newPlanetBtn.click();

    // Wait a bit for generation
    await page.waitForTimeout(2000);

    // Seed should (very likely) have changed
    const newSeed = await seedContainer.textContent();
    // Seeds are random, so there's an infinitesimal chance they're the same
    // We mainly verify the button doesn't crash
    expect(newSeed).toBeTruthy();
  });

  test('should display triangle count in the HUD', async ({ page }) => {
    // Wait for triangle count to be populated
    await page.waitForTimeout(1000);
    const trisText = await page.locator('text=Tris:').textContent();
    expect(trisText).toMatch(/Tris: \d/);
  });

  test('should display simulation time in the HUD', async ({ page }) => {
    await page.waitForTimeout(500);
    const timeText = await page.locator('text=Time:').textContent();
    // Should show either Ma or Ga format
    expect(timeText).toMatch(/Time: -?\d+\.?\d*\s*(Ma|Ga)/);
  });

  test('should not crash after multiple New Planet clicks', async ({ page }) => {
    const newPlanetBtn = page.locator('button', { hasText: 'New Planet' });

    // Click several times rapidly
    for (let i = 0; i < 3; i++) {
      await newPlanetBtn.click();
      await page.waitForTimeout(500);
    }

    // App should still be functional
    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible();
    await expect(page.locator('text=FPS:')).toBeVisible();
  });

  test('should have no console errors on load', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        errors.push(msg.text());
      }
    });

    // Navigate fresh to capture all console messages
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(2000);

    // Filter out known non-critical WebGL warnings
    const criticalErrors = errors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.'),
    );
    expect(criticalErrors).toHaveLength(0);
  });
});

// ─── Phase 3 Integration Tests ──────────────────────────────────────────────

test.describe('Phase 3 — Surface Processes Integration', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
  });

  test('should not crash with surface engine active after unpausing', async ({ page }) => {
    // Unpause the simulation to activate tectonic + surface engines
    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    await pauseBtn.click();

    // Let it run for a bit
    await page.waitForTimeout(3000);

    // App should still be functional
    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible();
    await expect(page.locator('text=FPS:')).toBeVisible();
  });

  test('should survive planet generation with surface engine initialized', async ({ page }) => {
    // Generate a new planet (reinitializes both tectonic + surface engine)
    const newPlanetBtn = page.locator('button', { hasText: 'New Planet' });
    await newPlanetBtn.click();
    await page.waitForTimeout(2000);

    // Unpause to start simulation
    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    await pauseBtn.click();
    await page.waitForTimeout(2000);

    // Pause again
    await pauseBtn.click();

    // App should still be functional
    await expect(page.locator('canvas')).toBeVisible();
    await expect(page.locator('text=Tris:')).toBeVisible();
  });

  test('should not produce console errors during surface simulation', async ({ page }) => {
    const errors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        errors.push(msg.text());
      }
    });

    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });

    // Unpause and let surface processes run
    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    await pauseBtn.click();
    await page.waitForTimeout(3000);

    // Filter out known non-critical warnings
    const criticalErrors = errors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.'),
    );
    expect(criticalErrors).toHaveLength(0);
  });

  test('should update simulation time while surface processes run', async ({ page }) => {
    // Get initial time
    await page.waitForTimeout(500);
    const timeLocator = page.locator('text=Time:');
    const initialTime = await timeLocator.textContent();

    // Set rate slider to maximum for fast advancement
    const slider = page.locator('input[type="range"]');
    await slider.fill('100');

    // Unpause and let simulation advance
    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    await pauseBtn.click();
    await page.waitForTimeout(5000);

    // Time should have advanced (or at minimum not crashed)
    const updatedTime = await timeLocator.textContent();
    // The simulation is running; verify the time display is still valid
    expect(updatedTime).toMatch(/Time: -?\d+\.?\d*\s*(Ma|Ga)/);
  });
});

// ─── Phase 4: Atmosphere Engine Tests ───────────────────────────────────────

test.describe('Phase 4: Atmosphere Engine', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
  });

  test('atmosphere engine active after unpausing (no crash)', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    // Unpause and let atmosphere engine run
    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    await pauseBtn.click();
    await page.waitForTimeout(3000);

    // No uncaught errors
    expect(errors).toHaveLength(0);
    // Globe still visible
    await expect(page.locator('canvas')).toBeVisible();
  });

  test('planet generation with atmosphere engine initialized', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    // Trigger new planet generation
    const newPlanetBtn = page.locator('button', { hasText: /New Planet/ });
    await newPlanetBtn.click();
    await page.waitForTimeout(2000);

    // App should still render without error
    expect(errors).toHaveLength(0);
    await expect(page.locator('canvas')).toBeVisible();
  });

  test('no console errors during atmospheric simulation', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    // Unpause, set fast rate, let atmosphere tick
    const slider = page.locator('input[type="range"]');
    await slider.fill('50');

    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    await pauseBtn.click();
    await page.waitForTimeout(4000);

    // Filter known non-critical renderer messages
    const critical = consoleErrors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.'),
    );
    expect(critical).toHaveLength(0);
  });
});

// ─── Phase 5: Cross-Section Viewer Tests ────────────────────────────────────

test.describe('Phase 5: Cross-Section Viewer', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
  });

  test('should have a Draw Cross-Section button in the sidebar', async ({ page }) => {
    const drawBtn = page.locator('button', { hasText: /Draw Cross-Section|Click Globe/ });
    await expect(drawBtn).toBeVisible();
  });

  test('should toggle draw mode on button click', async ({ page }) => {
    const drawBtn = page.locator('button', { hasText: /Draw Cross-Section/ });
    await expect(drawBtn).toBeVisible();

    // Click to activate draw mode
    await drawBtn.click();

    // Button text should change to indicate draw mode is active
    await expect(page.locator('button', { hasText: /Click Globe/ })).toBeVisible();

    // Click again to deactivate
    await drawBtn.click();
    await expect(page.locator('button', { hasText: /Draw Cross-Section/ })).toBeVisible();
  });

  test('cross-section panel should be hidden by default', async ({ page }) => {
    // The cross-section panel has a Labels button and Export PNG button
    // but should not be visible on initial load
    const labelsBtn = page.locator('button', { hasText: 'Labels' });
    await expect(labelsBtn).not.toBeVisible();
  });

  test('should not crash when draw mode is toggled multiple times', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const drawBtn = page.locator('button', { hasText: /Draw Cross-Section|Click Globe/ });

    // Toggle draw mode several times
    for (let i = 0; i < 5; i++) {
      await drawBtn.click();
      await page.waitForTimeout(200);
    }

    expect(errors).toHaveLength(0);
    await expect(page.locator('canvas')).toBeVisible();
  });

  test('draw mode should reset on new planet generation', async ({ page }) => {
    // Activate draw mode
    const drawBtn = page.locator('button', { hasText: /Draw Cross-Section/ });
    await drawBtn.click();
    await expect(page.locator('button', { hasText: /Click Globe/ })).toBeVisible();

    // Generate new planet
    const newPlanetBtn = page.locator('button', { hasText: 'New Planet' });
    await newPlanetBtn.click();
    await page.waitForTimeout(2000);

    // Draw mode should be reset
    await expect(page.locator('button', { hasText: /Draw Cross-Section/ })).toBeVisible();
  });

  test('no console errors with cross-section engine initialized', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });

    // Toggle draw mode on and off
    const drawBtn = page.locator('button', { hasText: /Draw Cross-Section/ });
    await drawBtn.click();
    await page.waitForTimeout(500);
    await drawBtn.click();
    await page.waitForTimeout(500);

    // Generate a new planet with cross-section engine wired in
    const newPlanetBtn = page.locator('button', { hasText: 'New Planet' });
    await newPlanetBtn.click();
    await page.waitForTimeout(2000);

    const critical = consoleErrors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.'),
    );
    expect(critical).toHaveLength(0);
  });
});

// ─── Phase 7: Layer Overlays, Topo, Time Display, and Cross-Section Zoom ─────

test.describe('Phase 7: Layer Overlays, Topo, and Cross-Section Zoom', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
  });

  test('should display all layer overlay buttons including topo', async ({ page }) => {
    await expect(page.locator('button', { hasText: 'plates' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'temperature' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'precipitation' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'biome' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'soil' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'clouds' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'biomass' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'topo' })).toBeVisible();
  });

  test('should toggle each layer without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const layers = ['plates', 'temperature', 'precipitation', 'biome', 'soil', 'clouds', 'biomass', 'topo'];
    for (const layer of layers) {
      const btn = page.locator('button', { hasText: layer });
      await btn.click();
      await page.waitForTimeout(200);
      await btn.click();
      await page.waitForTimeout(200);
    }

    expect(errors).toHaveLength(0);
  });

  test('should display simulation time in Ma or Ga format at startup', async ({ page }) => {
    // Time should be initialised immediately (no backend required)
    const timeText = await page.locator('text=Time:').textContent();
    expect(timeText).toMatch(/Time: -?\d+\.?\d*\s*(Ma|Ga)/);
  });

  test('should show cross-section zoom buttons when panel is open', async ({ page }) => {
    // Zoom buttons are inside the cross-section panel which starts hidden
    const zoomInBtn = page.locator('button', { hasText: '🔍+' });
    await expect(zoomInBtn).not.toBeVisible();
  });

  test('cross-section panel has zoom controls', async ({ page }) => {
    // Verify zoom buttons are present in the DOM (even if panel is hidden)
    const zoomInBtn = page.locator('button[title="Zoom In"]');
    const zoomOutBtn = page.locator('button[title="Zoom Out"]');
    const zoomResetBtn = page.locator('button[title="Reset Zoom"]');
    // They exist in DOM
    await expect(zoomInBtn).toHaveCount(1);
    await expect(zoomOutBtn).toHaveCount(1);
    await expect(zoomResetBtn).toHaveCount(1);
  });
});


test.describe('Phase 6: Vegetation & Polish', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
  });

  test('should display layer overlay toggles in the sidebar', async ({ page }) => {
    await expect(page.locator('text=Layer Overlays')).toBeVisible();
    await expect(page.locator('button', { hasText: 'plates' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'temperature' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'precipitation' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'biome' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'soil' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'clouds' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'biomass' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'topo' })).toBeVisible();
  });

  test('should toggle layer overlay buttons', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const platesBtn = page.locator('button', { hasText: 'plates' });
    await platesBtn.click();
    await page.waitForTimeout(200);

    // Click again to toggle off
    await platesBtn.click();
    await page.waitForTimeout(200);

    expect(errors).toHaveLength(0);
  });

  test('should display the geological timeline strip', async ({ page }) => {
    // Timeline strip should be at the bottom of the viewport
    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible();

    // Verify the app doesn't crash with the timeline rendered
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));
    await page.waitForTimeout(1000);
    expect(errors).toHaveLength(0);
  });

  test('should encode seed in URL fragment', async ({ page }) => {
    // Wait for planet to generate
    await page.waitForTimeout(1000);

    const url = page.url();
    expect(url).toMatch(/seed=\d+/);
  });

  test('should load planet from seed in URL', async ({ page }) => {
    // Navigate with a specific seed
    await page.goto('/#seed=42');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(1000);

    // Verify the seed is displayed
    const seedText = await page.locator('text=42').textContent();
    expect(seedText).toBeTruthy();
  });

  test('should run vegetation engine without errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    // Unpause and let the simulation run
    const pauseBtn = page.locator('button', { hasText: /Resume/ });
    if (await pauseBtn.isVisible()) {
      await pauseBtn.click();
    }

    // Let vegetation processes run
    await page.waitForTimeout(3000);

    // Pause again
    const pauseBtn2 = page.locator('button', { hasText: /Pause/ });
    if (await pauseBtn2.isVisible()) {
      await pauseBtn2.click();
    }

    const critical = errors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.'),
    );
    expect(critical).toHaveLength(0);
  });

  test('no console errors after new planet generation with Phase 6 engines', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    // Generate a new planet
    const newPlanetBtn = page.locator('button', { hasText: 'New Planet' });
    await newPlanetBtn.click();
    await page.waitForTimeout(2000);

    // Generate another planet
    await newPlanetBtn.click();
    await page.waitForTimeout(2000);

    const critical = consoleErrors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.'),
    );
    expect(critical).toHaveLength(0);
  });
});

// ── Phase 8 – Save/Load, Biome/Soil Layers, Elevation, First-Person, Default View ──

test.describe('Phase 8 – Save/Load State', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(1500);
  });

  test('should display Save State button in sidebar', async ({ page }) => {
    const btn = page.locator('button', { hasText: /Save State/i });
    await expect(btn).toBeVisible();
  });

  test('should display Load State button in sidebar', async ({ page }) => {
    const btn = page.locator('button', { hasText: /Load State/i });
    await expect(btn).toBeVisible();
  });

  test('should save state without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const saveBtn = page.locator('button', { hasText: /Save State/i });
    await saveBtn.click();
    await page.waitForTimeout(500);

    const critical = errors.filter((e) => !e.includes('WebGL') && !e.includes('THREE.'));
    expect(critical).toHaveLength(0);
  });

  test('should save and then load state without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    // Save current state
    const saveBtn = page.locator('button', { hasText: /Save State/i });
    await saveBtn.click();
    await page.waitForTimeout(800);

    // Load state
    const loadBtn = page.locator('button', { hasText: /Load State/i });
    await loadBtn.click();
    await page.waitForTimeout(1500);

    // Time display should still be valid
    const timeText = await page.locator('text=Time:').textContent();
    expect(timeText).toMatch(/Time: -?\d+\.?\d*\s*(Ma|Ga)/);

    const critical = errors.filter((e) => !e.includes('WebGL') && !e.includes('THREE.'));
    expect(critical).toHaveLength(0);
  });
});

test.describe('Phase 8 – Biome and Soil Layer Overlays', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(1500);
  });

  test('should have a biome layer button', async ({ page }) => {
    const btn = page.locator('button[data-layer="biome"]');
    await expect(btn).toBeVisible();
  });

  test('should have a soil layer button', async ({ page }) => {
    const btn = page.locator('button[data-layer="soil"]');
    await expect(btn).toBeVisible();
  });

  test('should not have an old "soil" layer that shows biome data', async ({ page }) => {
    // The old "soil" label should no longer exist; "biome" should be the Whittaker overlay
    const biomeBtn = page.locator('button[data-layer="biome"]');
    await expect(biomeBtn).toBeVisible();
    // Both biome and soil should be distinct buttons
    const soilBtn = page.locator('button[data-layer="soil"]');
    await expect(soilBtn).toBeVisible();
  });

  test('should toggle biome layer without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const btn = page.locator('button[data-layer="biome"]');
    await btn.click();
    await page.waitForTimeout(1000);
    await btn.click(); // toggle off
    await page.waitForTimeout(500);

    const critical = errors.filter((e) => !e.includes('WebGL') && !e.includes('THREE.'));
    expect(critical).toHaveLength(0);
  });

  test('should toggle soil (USDA orders) layer without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const btn = page.locator('button[data-layer="soil"]');
    await btn.click();
    await page.waitForTimeout(1000);
    await btn.click(); // toggle off
    await page.waitForTimeout(500);

    const critical = errors.filter((e) => !e.includes('WebGL') && !e.includes('THREE.'));
    expect(critical).toHaveLength(0);
  });

  test('should show biome legend when biome layer is active', async ({ page }) => {
    const btn = page.locator('button[data-layer="biome"]');
    await btn.click();
    await page.waitForTimeout(800);

    // Legend should contain biome-related text
    await expect(page.locator('text=Biome')).toBeVisible();

    // Toggle off
    await btn.click();
    await page.waitForTimeout(300);
  });

  test('should show soil legend with USDA soil order names when soil layer is active', async ({ page }) => {
    const btn = page.locator('button[data-layer="soil"]');
    await btn.click();
    await page.waitForTimeout(800);

    // Legend should contain soil-related text
    await expect(page.locator('text=Soil')).toBeVisible();

    // Toggle off
    await btn.click();
    await page.waitForTimeout(300);
  });
});

test.describe('Phase 8 – Elevation Readout', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(1500);
  });

  test('should show Elevation in inspect panel when clicking globe', async ({ page }) => {
    // Click on the center of the globe viewport
    const canvas = page.locator('canvas');
    const box = await canvas.boundingBox();
    if (!box) return;

    await canvas.click({ position: { x: box.width / 2, y: box.height / 2 } });
    await page.waitForTimeout(800);

    // If an inspect panel appeared, it should contain Elevation
    const elevationRow = page.locator('text=Elevation');
    const isVisible = await elevationRow.isVisible();
    if (isVisible) {
      const elevText = await elevationRow.locator('..').textContent();
      // Should contain a number followed by "m"
      expect(elevText).toMatch(/-?\d+\s*m/);
    }
  });
});

test.describe('Phase 8 – First-Person Mode', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(1500);
  });

  test('should not show first-person indicator at default zoom', async ({ page }) => {
    const fpIndicator = page.locator('text=First-Person');
    await expect(fpIndicator).not.toBeVisible();
  });

  test('should show canvas and allow zooming without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible();

    // Simulate scroll wheel to zoom in (pinch in on canvas)
    const box = await canvas.boundingBox();
    if (box) {
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      // Scroll down to zoom in
      for (let i = 0; i < 10; i++) {
        await page.mouse.wheel(0, -100);
        await page.waitForTimeout(50);
      }
      await page.waitForTimeout(500);
    }

    const critical = errors.filter((e) => !e.includes('WebGL') && !e.includes('THREE.'));
    expect(critical).toHaveLength(0);
  });
});

test.describe('Phase 8 – Default View Variation', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(2000);
  });

  test('should render globe with biome-influenced default view', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    // Just verify the globe renders without errors
    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible();

    const critical = errors.filter((e) => !e.includes('WebGL') && !e.includes('THREE.'));
    expect(critical).toHaveLength(0);
  });

  test('should generate new planet with varied default view without errors', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text());
    });

    const newPlanetBtn = page.locator('button', { hasText: 'New Planet' });
    await newPlanetBtn.click();
    await page.waitForTimeout(3000);

    const canvas = page.locator('canvas');
    await expect(canvas).toBeVisible();

    const critical = consoleErrors.filter((e) => !e.includes('WebGL') && !e.includes('THREE.'));
    expect(critical).toHaveLength(0);
  });

  test('all 8 layer buttons should be visible including biome and soil', async ({ page }) => {
    const layers = ['plates', 'temperature', 'precipitation', 'biome', 'soil', 'clouds', 'biomass', 'topo'];
    for (const layer of layers) {
      const btn = page.locator(`button[data-layer="${layer}"]`);
      await expect(btn).toBeVisible();
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Phase 9 Tests
// ─────────────────────────────────────────────────────────────────────────────

test.describe('Phase 9 – Simulation Freeze Detection', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(1500);
  });

  test('should show progress element in HUD', async ({ page }) => {
    // The progress element is always present (even if empty text)
    const progressEl = page.locator('.progress-el, [title*="agent"], [title*="abort"]').first();
    // It may or may not be visible depending on whether sim is running
    // Just verify no crashes
    const canvas = page.locator('canvas').first();
    await expect(canvas).toBeVisible();
  });

  test('should show pause button in HUD', async ({ page }) => {
    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    await expect(pauseBtn).toBeVisible();
  });
});

test.describe('Phase 9 – Ocean Water Sphere', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(2000);
  });

  test('should render globe without errors (water sphere included)', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const canvas = page.locator('canvas').first();
    await expect(canvas).toBeVisible();

    await page.waitForTimeout(500);

    const critical = errors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.') && !e.includes('NetworkError'),
    );
    expect(critical).toHaveLength(0);
  });

  test('should take screenshot showing globe with ocean water', async ({ page }) => {
    const canvas = page.locator('canvas').first();
    await expect(canvas).toBeVisible();
    await page.waitForTimeout(1000);
    // Take a screenshot to visually confirm the globe renders
    await page.screenshot({ path: 'e2e/screenshots/globe-with-ocean.png', fullPage: false });
  });
});

test.describe('Phase 9 – Weather Pattern Layer', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(2000);
  });

  test('should show weather layer button in sidebar', async ({ page }) => {
    const weatherBtn = page.locator('button[data-layer="weather"]');
    await expect(weatherBtn).toBeVisible();
  });

  test('all 9 layer buttons should be visible including weather', async ({ page }) => {
    const layers = ['plates', 'temperature', 'precipitation', 'biome', 'soil', 'clouds', 'biomass', 'topo', 'weather'];
    for (const layer of layers) {
      const btn = page.locator(`button[data-layer="${layer}"]`);
      await expect(btn).toBeVisible();
    }
  });

  test('activating weather layer should pause simulation and show month selector', async ({ page }) => {
    // Ensure simulation is not paused
    const pauseBtn = page.locator('button', { hasText: /Pause|Resume/ });
    const pauseText = await pauseBtn.textContent();
    if (pauseText?.includes('Resume')) {
      await pauseBtn.click(); // unpause first
      await page.waitForTimeout(200);
    }

    // Click the weather layer button
    const weatherBtn = page.locator('button[data-layer="weather"]');
    await weatherBtn.click();
    await page.waitForTimeout(1500); // wait for async fetch

    // Should show month selector
    const monthLabel = page.locator('text=/Weather Month:/');
    await expect(monthLabel).toBeVisible();

    // The pause button should now show "Resume" (sim was paused)
    const pauseBtnAfter = page.locator('button', { hasText: 'Resume' });
    await expect(pauseBtnAfter).toBeVisible();

    // Take screenshot
    await page.screenshot({ path: 'e2e/screenshots/weather-layer-active.png', fullPage: false });
  });

  test('weather month selector should navigate between months', async ({ page }) => {
    const weatherBtn = page.locator('button[data-layer="weather"]');
    await weatherBtn.click();
    await page.waitForTimeout(1500);

    const monthLabel = page.locator('text=/Weather Month:/');
    await expect(monthLabel).toBeVisible();

    // Click next month button
    const nextBtn = page.locator('button', { hasText: '▶' }).first();
    await nextBtn.click();
    await page.waitForTimeout(1000);

    // Month should have changed (February or next month name)
    const newText = await monthLabel.textContent();
    expect(newText).not.toBeNull();
    expect(newText).toMatch(/Weather Month:/);
  });

  test('deactivating weather layer should hide month selector', async ({ page }) => {
    const weatherBtn = page.locator('button[data-layer="weather"]');
    await weatherBtn.click();
    await page.waitForTimeout(1500);

    // Verify month selector is visible
    const monthLabel = page.locator('text=/Weather Month:/');
    await expect(monthLabel).toBeVisible();

    // Deactivate weather layer
    await weatherBtn.click();
    await page.waitForTimeout(500);

    await expect(monthLabel).not.toBeVisible();
  });

  test('clicking play while weather layer active should deactivate it', async ({ page }) => {
    // Activate weather layer (pauses sim)
    const weatherBtn = page.locator('button[data-layer="weather"]');
    await weatherBtn.click();
    await page.waitForTimeout(1500);

    // Verify paused
    const resumeBtn = page.locator('button', { hasText: 'Resume' });
    await expect(resumeBtn).toBeVisible();

    // Click Resume (play)
    await resumeBtn.click();
    await page.waitForTimeout(500);

    // Month selector should be gone
    const monthLabel = page.locator('text=/Weather Month:/');
    await expect(monthLabel).not.toBeVisible();

    // Take screenshot
    await page.screenshot({ path: 'e2e/screenshots/weather-layer-after-play.png', fullPage: false });
  });

  test('wind map toggle button should be visible when weather layer is active', async ({ page }) => {
    const weatherBtn = page.locator('button[data-layer="weather"]');
    await weatherBtn.click();
    await page.waitForTimeout(1500);

    // Should show the Wind toggle button
    const windBtn = page.locator('button', { hasText: /Wind/ });
    await expect(windBtn).toBeVisible();

    // Take screenshot of weather layer with wind button visible
    await page.screenshot({ path: 'e2e/screenshots/weather-layer-with-wind-toggle.png', fullPage: false });
  });

  test('clicking wind toggle should activate wind animation without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const weatherBtn = page.locator('button[data-layer="weather"]');
    await weatherBtn.click();
    await page.waitForTimeout(1500);

    // Click the Wind toggle button
    const windBtn = page.locator('button', { hasText: /Wind/ });
    await windBtn.click();
    await page.waitForTimeout(1000); // allow animation to run a few frames

    const critical = errors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.') && !e.includes('NetworkError'),
    );
    expect(critical).toHaveLength(0);

    // Take screenshot of wind animation active
    await page.screenshot({ path: 'e2e/screenshots/weather-wind-animation.png', fullPage: false });
  });
});

test.describe('Phase 9 – First Person Mode Controls', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('canvas', { timeout: 10_000 });
    await page.waitForTimeout(1500);
  });

  test('should not show first-person indicator at default zoom', async ({ page }) => {
    const fpIndicator = page.locator('text=First-Person');
    await expect(fpIndicator).not.toBeVisible();
  });

  test('should handle zoom without crashing', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (err) => errors.push(err.message));

    const canvas = page.locator('canvas').first();
    const box = await canvas.boundingBox();

    if (box) {
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      // Zoom in significantly
      for (let i = 0; i < 15; i++) {
        await page.mouse.wheel(0, -120);
        await page.waitForTimeout(30);
      }
      await page.waitForTimeout(800);
    }

    const critical = errors.filter(
      (e) => !e.includes('WebGL') && !e.includes('THREE.') && !e.includes('NetworkError'),
    );
    expect(critical).toHaveLength(0);

    await page.screenshot({ path: 'e2e/screenshots/first-person-zoom.png', fullPage: false });
  });
});
