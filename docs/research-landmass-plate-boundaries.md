# Research: Landmasses Crossing Plate Boundaries

**Date:** 2026-07-08  
**Context:** User observation that at simulation start, a contiguous landmass can visually cross plate boundaries when the topography and plate overlays are compared.  
**Scope:** Geological literature review and implications for GeoTime. No code changes in this document.

---

## Executive Summary

**Short answer:** It depends on which phase of Earth history the simulation is meant to represent.

| Era | Would land cross plate boundaries? |
|-----|-------------------------------------|
| **Hadean / earliest Archean** (pre–~3.8 Ga), before sustained plate tectonics | The concept barely applies. Crust formed heterogeneously via plumes, sagduction, dripping, and localized subduction. There were no stable, global plate boundaries in the modern sense. |
| **Archean onward**, once lithospheric plates exist | **No** — not in the way GeoTime currently shows it. Continental crust is mechanically coupled to mantle lithosphere and moves **with** its plate as a coherent block. Boundaries form **between** plates, not through the interior of a single, intact craton. |
| **Mature continents today** | Large landmasses *can* span multiple plates (e.g. Eurasia, Indonesia), but these are **welded assemblies** with ancient suture zones — not a single crustal body independently drifting across an active boundary. |

**For GeoTime specifically:** At t = −4500 Ma, the simulator already assigns Voronoi plates and generates topography from independent Simplex noise. A connected land blob routinely spans multiple plates at t = 0. This is **not** consistent with a plate-tectonic world, but is **partially defensible** for the Hadean if interpreted as pre-tectonic heterogeneous proto-crust. It becomes increasingly unrealistic as the simulation advances into the Archean and Proterozoic unless corrected.

---

## What the User Is Seeing in GeoTime

### Current initialization (`PlanetGenerator`)

1. **Plate map** — Lloyd-relaxed Voronoi tessellation: every grid cell belongs to exactly one plate.
2. **Height / land** — Simplex noise FBM, normalized to ~70% ocean coverage. Land vs ocean is determined **per cell** from the noise sign, not from plate identity.
3. **Crust type** — Per cell: positive noise → 35 km granite (continental); negative → 7 km basalt (oceanic). Plate `IsOceanic` is derived from the plate's mean height.
4. **No coupling** between connected landmasses and plate domains at generation time.

Result: flood-fill continent detection (`FeatureDetectorService`) can find a single continent whose cells belong to many different plates. Toggling the plate overlay on top of topography makes boundaries cut through the middle of that continent.

### Current runtime (`TectonicEngine.AdvectPlates`)

After initialization, advection **does** move height, crust, rock type, and plate ID together per cell (Rodrigues rotation + collision resolution). So cells do not independently "slide off" their plate during motion. The cross-boundary appearance is primarily an **initialization artifact**, not a failure of advection coherence.

However, two runtime behaviors can **preserve or worsen** the visual mismatch:

- **Continental collision** thickens crust at boundary cells but does not merge plate IDs into a single plate or create suture metadata.
- **`ConsolidatePlateFragments`** removes small isolated plate islands but does not reassign continental crust to a dominant plate within a connected landmass.

---

## Geological Background

### 1. Early Earth tectonic regimes were not modern plate tectonics

The timing of plate-tectonics onset remains debated (estimates range from >4.0 Ga to ~0.8 Ga), but there is broad agreement that the Hadean and much of the Archean were **not** dominated by modern-style sustained subduction and rigid plate motion.

Key regimes discussed in recent literature:

| Regime | Characteristics | Relevance to "plates" |
|--------|-----------------|----------------------|
| **Stagnant lid** | Single or weakly mobile lithospheric shell; heat loss mainly conductive; mantle overturns | No discrete plate boundaries; crust patches form and rework locally |
| **Plutonic-squishy lid** | High intrusive magmatism weakens mid-lithosphere; vertical dripping/delamination | Patchy deformation; low lateral mobility; "high plateness, low mobility" |
| **Episodic-squishy lid** | Alternating squishy-lid and mobile-lid episodes (~0.5 Gyr switches) | Transient subduction; boundaries appear and disappear |
| **Sagduction / dripping** | Dense greenstone sinks through buoyant felsic crust; dome-and-keel structures | Vertical tectonics within crustal domains |
| **Modern plate tectonics** | Rigid lithospheric plates, localized boundaries, sustained subduction | Crust coupled to lithosphere; lateral transport |

**Sources:** Lourenço et al. (2020, plutonic-squishy lid); Crameri et al. (2025, episodic-squishy lid); Bédard (2017, stagnant lid + mantle overturns); Flament et al. / Coltice et al. (Archean lid transitions); Stern & Gerya (2018).

### 2. Hadean crust formation was geographically heterogeneous

Hadean detrital zircons (Jack Hills, Barberton) record **contemporaneous different tectonic styles** in different terranes — some subduction-like (continental arc signatures), some stagnant-lid-like reworking (Valley et al. / Nature 2025, "Contemporaneous mobile- and stagnant-lid tectonics on the Hadean Earth").

Implications:

- Crust did **not** form uniformly within pre-existing plate polygons.
- Distinct terranes had separate magmatic histories before later assembly.
- Subduction-related magmatism, where present, was **episodic and localized**, not a global plate network.

### 3. Once plates exist, crust rides with lithosphere

Continental lithosphere = crust + sub-continental mantle root (keel). Archean cratons are a **coupled crust–mantle system**:

- Cratonic crust is underlain by depleted mantle lithosphere of comparable age (Pearson et al. 2021; GSA Today 2023, "Creating Continents").
- Cratons survive because they are buoyant, thick, and deformation localizes in weaker surrounding "buffer zones" (mobile belts) rather than craton interiors (Paul et al. 2019; Lenardic et al.).
- Strain at the base of thick lithosphere promotes **lateral translation** rather than internal deformation — i.e. the block moves as a unit.

**Conclusion:** In a plate-tectonic regime, continental material does not independently cross a plate boundary. Either:

1. The **whole plate** (with its attached crust) approaches a boundary, or  
2. A **terrane on a different plate** collides and accretes at a **margin**, becoming part of the host continent along a **suture**.

### 4. How continents grow — and why they can span plates today

Post-Archean continental growth is mainly **accretionary**:

- Island arcs, oceanic plateaus, and microcontinents arrive at convergent margins.
- Buoyant crust welds to the continental edge (terrane accretion; Ellis et al. 2011, JGR).
- Suture zones mark boundaries between formerly separate terranes.

Modern large continents spanning multiple plates (e.g. India on its own plate, Arabia, fragments of Eurasia) are the product of **billions of years of assembly**, not initial crust formation. Internal "boundaries" within such continents are mostly **inactive sutures** or **rifting margins**, not places where a single Archean craton formed across an active plate edge.

### 5. Archean craton construction — three stages (Wyoming example)

GSA Today (2023) describes craton development as:

1. **Mafic crust formation** — komatiite/basalt volcanism, sagduction.
2. **TTG (felsic) crust formation** — partial melting, stabilization of thick rigid lithosphere.
3. **Neoarchean stabilization** — granite-greenstone belts, final cratonization, then participation in modern-style collision/orogeny at **margins**.

Cratons are then surrounded by Proterozoic orogens (e.g. Wyoming craton). The **stable interior** is where ancient crust lives; **boundaries are at edges**, not through the craton core.

---

## Direct Answer to the Research Question

### "During early Earth plate formation, would landmasses cross plate boundaries?"

**Phase A — Before discrete plates (Hadean, much of early Archean):**  
Crust formed in localized domains (plume zones, arcs, sagduction keels). There were no persistent global plate boundaries to "cross." The GeoTime observation is **geologically meaningless** in this phase if "plates" are interpreted as proto-lithosphere patches rather than modern plates.

**Phase B — During plate nucleation (~3.8–2.5 Ga transition):**  
As the lid became mobile, existing crustal patches were **incorporated into** embryonic lithospheric blocks. Boundaries formed **between** blocks. Pre-existing crust could end up near or at a boundary due to later motion, but a single coherent continental block did not form **across** an active plate boundary — it formed **within** a lithospheric domain, then interacted at **margins**.

**Phase C — Mature plate tectonics (Neoarchean onward):**  
All early crust formations that survive as cratons are **within** their respective lithospheric plates (now often in plate interiors). Active plate boundaries cut through continents only where:

- A continent is large enough to span multiple modern plates (post-assembly), or  
- A collision is in progress and crust is being deformed at the **margin** (fold-and-thrust belt), not freely crossing.

**Bottom line for the app:** If GeoTime models a world with rigid plates and plate advection from t = 0, then **continental crust should be generated within plate domains**, not from noise independent of plate geometry. If GeoTime models the Hadean before plate tectonics, **plates should not be shown** (or should be labeled proto-lithosphere / squishy-lid domains) until a configurable onset time.

---

## Assessment of GeoTime Realism

| Aspect | Current behavior | Geological realism |
|--------|------------------|-------------------|
| Plate assignment at t = 0 | Voronoi tessellation | Reasonable for a mature plate world; **anachronistic** for 4.5 Ga Hadean |
| Topography at t = 0 | Independent Simplex noise | Realistic for heterogeneous proto-crust; **inconsistent** with coupled lithosphere |
| Land spanning plates at t = 0 | Common | **Artifact** if plates represent modern lithosphere |
| Advection | Cell properties move together | **Correct** — crust coupled to plate during motion |
| Continental collision | Thickens boundary cells only | **Incomplete** — missing suture, terrane merge, plate ID unification |
| Simulation start time | −4500 Ma (Hadean) | Predates consensus plate-tectonics onset |

---

## Potential Application Changes

Listed in rough priority order. These are recommendations only — no implementation in this research task.

### P1 — Epoch-aware tectonic regime

Introduce a configurable **plate-tectonics onset** (e.g. 3.8 Ga, 3.2 Ga, or user-tunable). Before onset:

- Disable or dim plate overlay; use "proto-lithosphere" or thermal-domain visualization instead.
- Run stagnant-lid / vertical-tectonics engines (plume uplift, sagduction-style crustal thickening) without lateral advection.
- After onset, enable `AdvectPlates` and boundary classification.

**Rationale:** Matches the geological consensus that the Hadean was not a modern plate world, and directly addresses the user's observation at simulation start.

### P2 — Couple continental crust generation to plate domains

For simulations starting in or after the plate-tectonic era, change `PlanetGenerator` (or add a post-pass) so that:

- Continental crust seeds are placed **within** plate interiors (e.g. random plate centers, grow by flood-fill constrained to plate ID).
- Plate boundaries start as oceanic ridges/trenches, not cutting through elevated continental noise.
- Optionally: one continental "seed" per plate, sized by plate area.

**Rationale:** Eliminates the t = 0 cross-boundary landmass artifact for plate-tectonic scenarios.

### P3 — Crust provenance / terrane tracking

Separate **plate ID** (current kinematic unit) from **terrane ID** (crustal provenance block):

- At generation, each continental seed gets a terrane ID.
- On accretionary collision, terrane IDs persist; plate IDs may merge or remain distinct.
- Feature detector reports "suture" where terrane IDs meet within a continent.

**Rationale:** Models how real continents are welded from distinct Archean blocks (e.g. Baltic Shield terranes, North American Cordillera accreted terranes).

### P4 — Suture formation on continental collision

Extend `applyContinentalCollision` to:

- Record suture cells in a new map or event log.
- Optionally merge plate IDs after prolonged collision (two continents on converging plates → one plate).
- Render sutures as distinct from active boundaries (dashed / muted overlay).

**Rationale:** After collision, an internal "boundary" within the landmass is a **suture** (geologically valid), not an active plate edge.

### P5 — Post-generation coherence pass

After `ClassifyPlatesAndCrust`, for each connected land component above sea level:

- If >X% of cells belong to one plate, reassign outlier cells to that plate (or convert outliers to coastal/lowland transition).
- Mirror existing `ConsolidatePlateFragments` logic but keyed on **elevation-connected components** rather than plate fragments.

**Rationale:** Low-cost fix for the visual artifact without full generator rewrite. Less physically motivated than P2 but improves first impression.

### P6 — Craton placement heuristic

When detecting continents (`FeatureDetectorService`):

- Flag cells in plate interior (distance from boundary > threshold) with old rock age as **craton candidate**.
- Flag cells where plate boundaries intersect elevated crust as **orogen / margin** rather than craton core.
- Feed this into description templates and labels.

**Rationale:** Aligns feature naming and LLM context with geology (cratons in interiors, orogens at margins).

### P7 — Time-varying plate rigidity / mobility

Scale `AngularVelocity.Rate` by a function of simulation age (low in Hadean, ramping through Archean). Optionally increase boundary localization over time.

**Rationale:** Captures episodic-squishy-lid behavior without a hard on/off switch.

### P8 — Documentation and UI labeling

- Label plate overlay as **"Lithospheric plates (active after X Ga)"** when before onset.
- Add tooltip explaining that Hadean crust was not organized into modern plates.
- Link to this document from README or in-app help.

**Rationale:** Manages user expectations even before engine changes ship.

---

## Key References

1. **Valley, J. W. et al. (2025)** — "Contemporaneous mobile- and stagnant-lid tectonics on the Hadean Earth." *Nature*. [doi:10.1038/s41586-025-10066-2](https://doi.org/10.1038/s41586-025-10066-2)

2. **Crameri, F. et al. (2025)** — "Dissecting the puzzle of tectonic lid regimes in terrestrial planets." *Nature Communications*. [doi:10.1038/s41467-025-65943-1](https://doi.org/10.1038/s41467-025-65943-1)

3. **Lourenço, D. L. et al. (2020)** — "Plutonic-squishy lid: a new global tectonic regime generated by intrusive magmatism." *G³*. [doi:10.1029/2019GC008756](https://doi.org/10.1029/2019GC008756)

4. **Bédard, J. H. (2017)** — "Stagnant lids and mantle overturns: Implications for Archaean tectonics." *Geoscience Frontiers*. [doi:10.1016/j.gsf.2017.01.005](https://doi.org/10.1016/j.gsf.2017.01.005)

5. **Flament, N. et al. (2008)** — Archean geodynamics and transition to plate tectonics (cited in Elements Magazine review).

6. **GSA Today (2023)** — "Creating Continents: Archean Cratons Tell the Story." [geosociety.org](https://www.geosociety.org/gsa-today/january-2023/creating-continents-archean-cratons-tell-the-story)

7. **Paul, J., Ghosh, A., Conrad, C. P. (2019)** — "Traction and strain-rate at the base of the lithosphere." *Geophysical Journal International*. Craton coherence and strain localization.

8. **Ellis, S. et al. (2011)** — "Geodynamic models of terrane accretion." *JGR*. [doi:10.1029/2012JB009316](https://doi.org/10.1029/2012JB009316)

9. **Lenardic, A. et al. / Cooper, C. M. (2021)** — "Nature Versus Nurture: Preservation and Destruction of Archean Cratons." *Tectonics*. [doi:10.1029/2021TC006714](https://doi.org/10.1029/2021TC006714)

10. **Wang, C. et al. (2022)** — "Archean crust-mantle geodynamic regimes: A review." *Geography and Geology*. Stagnant lid, sagduction, dripping, hot subduction coexistence.

11. **NPS Geology** — "Accreted Terranes." Continental growth by terrane accretion at convergent margins. [nps.gov](https://www.nps.gov/subjects/geology/plate-tectonics-accreted-terranes.htm)

---

## GeoTime Code References (for implementers)

| File | Relevant behavior |
|------|-------------------|
| `backend/GeoTime.Core/Proc/PlanetGenerator.cs` | Voronoi plates + independent noise height; per-cell continental/oceanic crust |
| `backend/GeoTime.Core/Engines/TectonicEngine.cs` | `AdvectPlates`, collision resolution, `ConsolidatePlateFragments` |
| `backend/GeoTime.Core/Services/FeatureDetectorService.cs` | Flood-fill continents independent of plate membership |
| `src/render/globe-renderer.ts` | Separate topography and plate overlay layers |

---

## Conclusion

The user's observation is **real in the simulator** and **mostly an initialization artifact**: topography and plate geometry are generated independently. Geologically:

- **Hadean:** Cross-boundary land is not the right framing — there were no modern plates, and crust was heterogeneous.
- **Archean+:** Continental crust should live **within** lithospheric plates and move with them; boundaries belong at **margins** or as **sutures** after collision, not through the middle of a newly formed craton.

The highest-value improvements are an **epoch-aware tectonic regime** (defer or reframe plates before ~3.8 Ga) and **coupling continental crust seeds to plate interiors** when plate tectonics is active.
