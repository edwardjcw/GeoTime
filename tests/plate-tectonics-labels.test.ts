// @vitest-environment jsdom

import { describe, it, expect, beforeEach } from 'vitest';
import { AppShell } from '../src/ui/app-shell';
import {
  PLATE_LANDMASS_RESEARCH_DOC,
  formatPlateTectonicsOnsetGa,
  isPrePlateTectonicsOnset,
  plateLayerButtonLabel,
  plateLayerButtonTooltip,
  plateLayerLegendTitle,
  plateTectonicsOnsetHelpText,
} from '../src/plate-tectonics-labels';

describe('plate tectonics UI labels', () => {
  it('formats onset Ga from negative Ma', () => {
    expect(formatPlateTectonicsOnsetGa(-3800)).toBe('3.8');
  });

  it('uses Lithospheric plates label before onset', () => {
    expect(plateLayerButtonLabel(-3800, -4500)).toBe(
      'Lithospheric plates (active after 3.8 Ga)',
    );
    expect(plateLayerLegendTitle(-3800)).toBe(
      'Lithospheric plates (active after 3.8 Ga)',
    );
  });

  it('uses short plates label after onset', () => {
    expect(plateLayerButtonLabel(-3800, -3000)).toBe('plates');
  });

  it('pre-onset tooltip explains Hadean crust and links research doc', () => {
    const tooltip = plateLayerButtonTooltip(-3800, -4500);
    expect(tooltip).toContain('Hadean crust was not organized into modern lithospheric plates');
    expect(tooltip).toContain('3.8 Ga');
    expect(tooltip).toContain(PLATE_LANDMASS_RESEARCH_DOC);
  });

  it('onset help text references research doc', () => {
    expect(plateTectonicsOnsetHelpText()).toContain(PLATE_LANDMASS_RESEARCH_DOC);
  });

  it('isPrePlateTectonicsOnset matches simulation time vs onset', () => {
    expect(isPrePlateTectonicsOnset(-4500, -3800)).toBe(true);
    expect(isPrePlateTectonicsOnset(-3800, -3800)).toBe(false);
    expect(isPrePlateTectonicsOnset(-2000, -3800)).toBe(false);
  });
});

describe('AppShell – plate tectonics labeling', () => {
  let root: HTMLElement;
  let shell: AppShell;

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
    shell = new AppShell(root);
  });

  it('updates plates button label and tooltip before onset', () => {
    shell.setPlateTectonicsRegime(-3800, -4500);
    const platesBtn = Array.from(root.querySelectorAll('button')).find(
      (b) => b.dataset.layer === 'plates',
    );
    expect(platesBtn).toBeTruthy();
    expect(platesBtn!.textContent).toBe('Lithospheric plates (active after 3.8 Ga)');
    expect(platesBtn!.title).toContain('Hadean crust was not organized');
    expect(platesBtn!.style.opacity).toBe('0.45');
  });

  it('restores plates label after advancing past onset', () => {
    shell.setPlateTectonicsRegime(-3800, -4500);
    shell.setSimTime(-3000);
    const platesBtn = Array.from(root.querySelectorAll('button')).find(
      (b) => b.dataset.layer === 'plates',
    )!;
    expect(platesBtn.textContent).toBe('plates');
    expect(platesBtn.title).toContain('Lithospheric plates (active)');
    expect(platesBtn.style.opacity).toBe('1');
  });

  it('shows onset help hint in sidebar', () => {
    expect(root.textContent).toContain('research-landmass-plate-boundaries.md');
  });
});
