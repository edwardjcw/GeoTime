/** Path to the plate–landmass realism research note (repo-relative). */
export const PLATE_LANDMASS_RESEARCH_DOC = 'docs/research-landmass-plate-boundaries.md';

export function formatPlateTectonicsOnsetGa(onsetMa: number): string {
  return (Math.abs(onsetMa) / 1000).toFixed(1);
}

export function isPrePlateTectonicsOnset(currentTimeMa: number, onsetMa: number): boolean {
  return currentTimeMa < onsetMa;
}

/** Sidebar toggle label for the plates layer overlay. */
export function plateLayerButtonLabel(onsetMa: number, currentTimeMa: number): string {
  const onsetGa = formatPlateTectonicsOnsetGa(onsetMa);
  if (isPrePlateTectonicsOnset(currentTimeMa, onsetMa)) {
    return `Lithospheric plates (active after ${onsetGa} Ga)`;
  }
  return 'plates';
}

/** Legend panel title when the plate overlay is active before tectonics onset. */
export function plateLayerLegendTitle(onsetMa: number): string {
  const onsetGa = formatPlateTectonicsOnsetGa(onsetMa);
  return `Lithospheric plates (active after ${onsetGa} Ga)`;
}

/** Tooltip for the plates layer toggle. */
export function plateLayerButtonTooltip(onsetMa: number, currentTimeMa: number): string {
  const onsetGa = formatPlateTectonicsOnsetGa(onsetMa);
  if (isPrePlateTectonicsOnset(currentTimeMa, onsetMa)) {
    return (
      'Hadean crust was not organized into modern lithospheric plates — it formed heterogeneously ' +
      'via plumes, sagduction, and localized deformation. This overlay shows proto-lithosphere ' +
      `domains for reference; sustained lateral plate motion begins at ${onsetGa} Ga. ` +
      `See ${PLATE_LANDMASS_RESEARCH_DOC}`
    );
  }
  return 'Lithospheric plates (active). Boundaries show convergent, divergent, and transform motion.';
}

/** Help text for the plate-tectonics onset control in the sidebar. */
export function plateTectonicsOnsetHelpText(): string {
  return (
    'Before onset, crust evolves vertically (stagnant-lid / proto-lithosphere). ' +
    `Lateral plate motion ramps up at the configured Ga. See ${PLATE_LANDMASS_RESEARCH_DOC}.`
  );
}
