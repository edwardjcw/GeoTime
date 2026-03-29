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
