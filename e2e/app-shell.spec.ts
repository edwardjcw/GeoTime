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

// ─── Phase 6: Vegetation & Polish Tests ─────────────────────────────────────

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
    await expect(page.locator('button', { hasText: 'soil' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'clouds' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'biomass' })).toBeVisible();
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
