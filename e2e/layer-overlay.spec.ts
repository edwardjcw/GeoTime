// Automated layer overlay regression tests.
// Verifies each scalar overlay shows spatial color variation (not a flat wash)
// and that event-layer types populate the dropdown after planet generation.

import { test, expect, type Page } from '@playwright/test';
import {
  assertLayerHasSpatialVariation,
  measureGlobeScreenshotVariance,
} from './helpers/canvas-variance';

const globeCanvas = (page: Page) => page.locator('canvas').first();

async function waitForPlanetReady(page: Page): Promise<void> {
  await page.goto('/');
  await page.waitForSelector('canvas', { timeout: 20_000 });
  const seedEl = page.locator('text=Seed:').locator('..').locator('span').nth(1);
  await expect(seedEl).not.toHaveText('--', { timeout: 30_000 });
}

async function activateLayer(page: Page, layer: string): Promise<void> {
  const btn = page.locator(`button[data-layer="${layer}"]`);
  const responseWait = page.waitForResponse(
    (res) => res.url().includes('/api/state/') && res.status() === 200,
    { timeout: 30_000 },
  );
  await btn.click();
  await responseWait.catch(() => {/* cached bundle — no fetch */});
  await page.waitForTimeout(400);
  await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))));
}

async function deactivateLayer(page: Page, layer: string): Promise<void> {
  await page.locator(`button[data-layer="${layer}"]`).click();
  await page.waitForTimeout(200);
}

test.describe('Layer overlay spatial variation', () => {
  test.beforeEach(async ({ page }) => {
    await waitForPlanetReady(page);
  });

  const scalarLayers = [
    'topo',
    'temperature',
    'precipitation',
    'plates',
    'soil',
    'biome',
    'clouds',
  ] as const;

  for (const layer of scalarLayers) {
    test(`layer "${layer}" shows spatial color variation on globe`, async ({ page }) => {
      test.setTimeout(90_000);

      await activateLayer(page, layer);

      const stats = await measureGlobeScreenshotVariance(page);
      await page.screenshot({
        path: `e2e/screenshots/layer-variance-${layer}.png`,
        fullPage: false,
      });

      try {
        assertLayerHasSpatialVariation(layer, stats);
      } catch (err) {
        throw new Error(`${(err as Error).message}\nScreenshot: e2e/screenshots/layer-variance-${layer}.png`);
      }

      await deactivateLayer(page, layer);
    });
  }

  // Biomass may stay near-zero on a fresh planet; verify API data varies after one advance.
  test('biomass layer shows variation after simulation tick', async ({ page, request }) => {
    test.setTimeout(120_000);

    await request.post('http://127.0.0.1:5000/api/simulation/advance', {
      data: { deltaMa: 5 },
    });

    await activateLayer(page, 'biomass');
    const stats = await measureGlobeScreenshotVariance(page);
    await page.screenshot({ path: 'e2e/screenshots/layer-variance-biomass.png', fullPage: false });
    assertLayerHasSpatialVariation('biomass', stats);
  });

  test('switching topo → plates keeps plates visible with variation', async ({ page }) => {
    test.setTimeout(90_000);

    await activateLayer(page, 'topo');
    await activateLayer(page, 'plates');

    const stats = await measureGlobeScreenshotVariance(page);
    await page.screenshot({ path: 'e2e/screenshots/layer-topo-then-plates.png', fullPage: false });
    assertLayerHasSpatialVariation('plates', stats);
  });
});

test.describe('Event layer dropdown', () => {
  test('populates with all event types after planet generation', async ({ page, request }) => {
    await waitForPlanetReady(page);

    const typesRes = await request.get('http://127.0.0.1:5000/api/state/eventlayermap/types');
    expect(typesRes.ok()).toBe(true);
    const apiTypes = (await typesRes.json()) as string[];
    expect(apiTypes.length).toBeGreaterThan(5);
    expect(apiTypes).toContain('ImpactEjecta');
    expect(apiTypes).toContain('VolcanicAsh');

    const eventBtn = page.locator('button', { hasText: 'event layers' });
    await eventBtn.click();

    const select = page.locator('select').filter({ has: page.locator('option', { hasText: 'ImpactEjecta' }) });
    await expect(select).toBeVisible();

    const optionTexts = await select.locator('option').allTextContents();
    expect(optionTexts.some((t) => t.includes('ImpactEjecta'))).toBe(true);
    expect(optionTexts.some((t) => t.includes('VolcanicAsh'))).toBe(true);
  });
});
