using GeoTime.Core.Models;
using GeoTime.Core.Kernel;
using GeoTime.Core.Proc;

namespace GeoTime.Core.Engines;

/// <summary>
/// Phase 7: Biomatter system — microbes, plankton, reef organisms, fungi.
/// Drives ocean chemistry, biogenic sedimentation, atmosphere O₂/CH₄ feedback,
/// and petroleum source-rock formation. Feature-flagged.
/// </summary>
public sealed class BiomatterEngine(
    EventBus bus,
    EventLog log,
    uint seed,
    int gridSize,
    double minTick = 1.0,
    bool enabled = true)
{
    // ── Constants ──────────────────────────────────────────────────────────────

    /// <summary>Maximum marine biomatter density (kg C/m²).</summary>
    public const double MAX_MARINE_BIOMATTER = 5.0;

    /// <summary>Maximum terrestrial non-plant biomatter (kg C/m²).</summary>
    public const double MAX_TERRESTRIAL_BIOMATTER = 2.0;

    /// <summary>Optimal temperature for tropical marine plankton (°C).</summary>
    public const double PLANKTON_OPTIMAL_TEMP = 20.0;

    /// <summary>Standard deviation for plankton temperature bell curve.</summary>
    public const double PLANKTON_TEMP_SIGMA = 10.0;

    /// <summary>Minimum temperature for reef organisms (°C).</summary>
    public const double REEF_MIN_TEMP = 18.0;

    /// <summary>Maximum temperature for reef organisms (°C).</summary>
    public const double REEF_MAX_TEMP = 30.0;

    /// <summary>Maximum depth for reef organisms (m, negative = below sea level).</summary>
    public const double REEF_MAX_DEPTH = -50.0;

    /// <summary>Minimum temperature for cyanobacteria/microbial mats (°C).</summary>
    public const double CYANO_MIN_TEMP = 10.0;

    /// <summary>Minimum temperature for fungi/decomposers (°C).</summary>
    public const double FUNGI_MIN_TEMP = 0.0;

    /// <summary>Minimum soil depth for fungi (m).</summary>
    public const double FUNGI_MIN_SOIL = 0.1;

    /// <summary>Fungi productivity = this fraction × vegetation biomass.</summary>
    public const double FUNGI_BIOMASS_FRACTION = 0.2;

    /// <summary>Pedogenesis rate boost from fungi (fractional, 20–40% → use 0.3).</summary>
    public const double FUNGI_PEDOGENESIS_BOOST = 0.3;

    /// <summary>Base marine productivity rate (kg C/m²/Myr).</summary>
    public const double BASE_MARINE_RATE = 0.5;

    /// <summary>O₂ production rate per unit biomatter (fraction/Myr).</summary>
    public const double O2_PRODUCTION_RATE = 1e-6;

    /// <summary>CH₄ production rate from anaerobic microbes (fraction/Myr).</summary>
    public const double CH4_PRODUCTION_RATE = 2e-7;

    /// <summary>CO₂ drawdown rate from biological pump (fraction/Myr per unit biomatter).</summary>
    public const double CO2_DRAWDOWN_RATE = 5e-8;

    /// <summary>Organic carbon accumulation rate in anoxic basins (kg C/m²/Myr).</summary>
    public const double ORGANIC_CARBON_RATE = 0.02;

    /// <summary>Oil window minimum temperature (°C).</summary>
    public const double OIL_WINDOW_MIN_TEMP = 60.0;

    /// <summary>Oil window maximum temperature (°C).</summary>
    public const double OIL_WINDOW_MAX_TEMP = 120.0;

    /// <summary>Minimum burial depth for kerogen formation (m).</summary>
    public const double OIL_WINDOW_MIN_DEPTH = 2000.0;

    /// <summary>O₂ threshold for oxygenation event (fraction).</summary>
    public const double OXYGENATION_THRESHOLD = 0.02;

    /// <summary>O₂ threshold for aerobic marine life (fraction).</summary>
    public const double AEROBIC_MARINE_O2 = 0.001;

    /// <summary>O₂ threshold for terrestrial decomposers (fraction).</summary>
    public const double TERRESTRIAL_O2 = 0.02;

    /// <summary>Shallow marine depth threshold (m, heights below 0 and above this).</summary>
    public const double SHALLOW_MARINE_DEPTH = -200.0;

    /// <summary>Reef height increase per Myr (m).</summary>
    public const double REEF_GROWTH_RATE = 3.0;

    // ── Biogenic sedimentation rates (m/Myr) ─────────────────────────────────

    public const double CHALK_DEPOSITION_RATE = 0.03;
    public const double CHERT_DEPOSITION_RATE = 0.01;
    public const double DIATOMITE_DEPOSITION_RATE = 0.02;
    public const double REEF_LIMESTONE_RATE = 0.5;
    public const double STROMATOLITE_RATE = 0.05;
    public const double PHOSPHORITE_RATE = 0.005;
    public const double BIF_RATE = 0.03;

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly Xoshiro256ss _rng = new(seed);

    private SimulationState? _state;
    private AtmosphericComposition? _atmo;
    private StratigraphyStack? _strat;
    private double _accumulator;
    private bool _oxygenationFired;

    public bool Enabled { get; } = enabled;

    // ── Constructor ───────────────────────────────────────────────────────────

    public void Initialize(SimulationState state, AtmosphericComposition atmo, StratigraphyStack strat)
    {
        _state = state;
        _atmo = atmo;
        _strat = strat;
        _accumulator = 0;
        _oxygenationFired = false;
    }

    // ── Main tick ─────────────────────────────────────────────────────────────

    public BiomatterTickResult? Tick(double timeMa, double deltaMa)
    {
        if (!Enabled || _state == null || _atmo == null || deltaMa <= 0) return null;
        _accumulator += deltaMa;
        BiomatterTickResult? last = null;
        while (_accumulator >= minTick)
        {
            _accumulator -= minTick;
            last = Process(timeMa - _accumulator, minTick);
        }
        return last;
    }

    // ── Static productivity helpers (public for unit testing) ─────────────────

    /// <summary>Temperature factor: Gaussian bell curve centered on optimal temp.</summary>
    public static double TemperatureFactor(double tempC, double optimal, double sigma)
    {
        var d = tempC - optimal;
        return Math.Exp(-(d * d) / (2 * sigma * sigma));
    }

    /// <summary>Light factor: 1.0 in photic zone (depth less than 200m), decreases deeper.</summary>
    public static double LightFactor(double heightM)
    {
        if (heightM >= 0) return 0; // land cell
        var depth = -heightM;
        if (depth <= 200) return 1.0;
        return Math.Max(0, 1.0 - (depth - 200) / 4800); // tapers to 0 at 5000m
    }

    /// <summary>Reef suitability: 1.0 for warm shallow marine, 0 otherwise.</summary>
    public static double ReefFactor(double tempC, double heightM)
    {
        if (heightM >= 0) return 0; // land
        if (heightM < REEF_MAX_DEPTH) return 0; // too deep
        if (tempC < REEF_MIN_TEMP || tempC > REEF_MAX_TEMP) return 0;
        // Peak at 24°C
        var tOpt = (REEF_MIN_TEMP + REEF_MAX_TEMP) / 2;
        var tRange = (REEF_MAX_TEMP - REEF_MIN_TEMP) / 2;
        var tFactor = 1.0 - Math.Abs(tempC - tOpt) / tRange;
        return Math.Max(0, tFactor);
    }

    /// <summary>Marine biomatter productivity (kg C/m²/Myr).</summary>
    public static double MarineProductivity(double tempC, double heightM, double o2Level)
    {
        if (heightM >= 0) return 0; // land
        if (o2Level < AEROBIC_MARINE_O2) return 0; // below aerobic threshold
        var tFactor = TemperatureFactor(tempC, PLANKTON_OPTIMAL_TEMP, PLANKTON_TEMP_SIGMA);
        var lFactor = LightFactor(heightM);
        return BASE_MARINE_RATE * tFactor * lFactor;
    }

    /// <summary>Cyanobacteria productivity: shallow marine, > min temp. Active even at very low O₂.</summary>
    public static double CyanobacteriaProductivity(double tempC, double heightM)
    {
        if (heightM >= 0) return 0; // land
        if (-heightM > 200) return 0; // not shallow
        if (tempC < CYANO_MIN_TEMP) return 0;
        return BASE_MARINE_RATE * 0.3 * Math.Min(1.0, (tempC - CYANO_MIN_TEMP) / 20.0);
    }

    /// <summary>Fungi/decomposer productivity (kg C/m²/Myr).</summary>
    public static double FungiProductivity(double tempC, double soilDepth, double vegetationBiomass, double o2Level)
    {
        if (o2Level < TERRESTRIAL_O2) return 0;
        if (tempC < FUNGI_MIN_TEMP) return 0;
        if (soilDepth < FUNGI_MIN_SOIL) return 0;
        return FUNGI_BIOMASS_FRACTION * vegetationBiomass;
    }

    // ── Process one sub-tick ──────────────────────────────────────────────────

    private BiomatterTickResult Process(double timeMa, double deltaMa)
    {
        var sv = _state!;
        var atmo = _atmo!;
        var cc = gridSize * gridSize;

        double totalBiomatter = 0;
        double totalOrgCarbon = 0;
        var marineCells = 0;
        var reefCells = 0;
        var cyanoCells = 0;
        var fungiCells = 0;
        var oilShaleLayers = 0;
        var biogenicLayers = 0;

        double o2Production = 0;
        double ch4Production = 0;
        double co2Drawdown = 0;

        for (var i = 0; i < cc; i++)
        {
            double h = sv.HeightMap[i];
            double temp = sv.TemperatureMap[i];
            double biomatter = sv.BiomatterMap[i];
            double orgCarbon = sv.OrganicCarbonMap[i];

            var isOcean = h < 0;
            var isShallow = isOcean && -h <= 200;

            if (isOcean)
            {
                // ── Marine biomatter ──────────────────────────────────────
                var marineP = MarineProductivity(temp, h, atmo.O2);
                var cyanoP = CyanobacteriaProductivity(temp, h);
                var reef = ReefFactor(temp, h);

                var totalP = marineP + cyanoP;
                biomatter = Math.Min(MAX_MARINE_BIOMATTER,
                    biomatter + totalP * deltaMa);

                if (marineP > 0) marineCells++;
                if (cyanoP > 0) cyanoCells++;

                // O₂ from cyanobacteria + phytoplankton
                o2Production += (cyanoP + marineP * 0.5) * deltaMa;

                // CH₄ from anaerobic microbes in deep anoxic ocean
                if (!isShallow && biomatter > 0.1)
                    ch4Production += biomatter * 0.1 * deltaMa;

                // CO₂ drawdown (biological pump)
                co2Drawdown += marineP * deltaMa;

                // ── Organic carbon burial (anoxic basins) ─────────────────
                if (!isShallow && biomatter > 0.5)
                {
                    var burial = ORGANIC_CARBON_RATE * biomatter * deltaMa;
                    orgCarbon = Math.Min(50.0, orgCarbon + burial);
                }

                // ── Reef growth ───────────────────────────────────────────
                if (reef > 0.1)
                {
                    reefCells++;
                    var heightBump = reef * REEF_GROWTH_RATE * deltaMa;
                    sv.HeightMap[i] = (float)Math.Min(-1, h + heightBump);
                }

                // ── Biogenic sedimentation ────────────────────────────────
                if (_strat != null)
                    biogenicLayers += DepositBiogenicSediment(i, timeMa, deltaMa, temp, h,
                        marineP, cyanoP, reef, isShallow, atmo.O2);
            }
            else
            {
                // ── Terrestrial biomatter (fungi & decomposers) ───────────
                double soilD = sv.SoilDepthMap[i];
                double vegBio = sv.BiomassMap[i];
                var fungiP = FungiProductivity(temp, soilD, vegBio, atmo.O2);

                if (fungiP > 0)
                {
                    biomatter = Math.Min(MAX_TERRESTRIAL_BIOMATTER,
                        biomatter + fungiP * deltaMa);
                    fungiCells++;
                }
                else
                {
                    biomatter = Math.Max(0, biomatter - 0.01 * deltaMa);
                }
            }

            sv.BiomatterMap[i] = (float)Math.Max(0, biomatter);
            sv.OrganicCarbonMap[i] = (float)Math.Max(0, orgCarbon);
            totalBiomatter += sv.BiomatterMap[i];
            totalOrgCarbon += sv.OrganicCarbonMap[i];
        }

        // ── Petroleum source-rock conversion ──────────────────────────────────
        if (_strat != null)
            oilShaleLayers = ProcessPetroleumConversion(timeMa, cc);

        // ── Atmosphere feedback ───────────────────────────────────────────────
        UpdateAtmosphere(atmo, o2Production, ch4Production, co2Drawdown, deltaMa);

        // ── Events ────────────────────────────────────────────────────────────
        var meanProductivity = cc > 0 ? totalBiomatter / cc : 0;
        bus.Emit("BIOMATTER_UPDATE", new { totalBiomatter, meanProductivity });

        if (_oxygenationFired || !(atmo.O2 >= OXYGENATION_THRESHOLD))
            return new BiomatterTickResult
            {
                TotalBiomatter = totalBiomatter,
                TotalOrganicCarbon = totalOrgCarbon,
                MarineCells = marineCells,
                ReefCells = reefCells,
                CyanobacteriaCells = cyanoCells,
                FungiCells = fungiCells,
                OilShaleLayers = oilShaleLayers,
                BiogenicLayers = biogenicLayers,
                AtmosphericO2 = atmo.O2,
                AtmosphericCH4 = atmo.CH4,
            };
        
        _oxygenationFired = true;
        bus.Emit("OXYGENATION_EVENT", new { o2Level = atmo.O2 });
        log.Record(new GeoLogEntry
        {
            TimeMa = timeMa, Type = "OXYGENATION_EVENT",
            Description = $"Atmospheric O₂ reached {atmo.O2 * 100:F2}%"
        });

        return new BiomatterTickResult
        {
            TotalBiomatter = totalBiomatter,
            TotalOrganicCarbon = totalOrgCarbon,
            MarineCells = marineCells,
            ReefCells = reefCells,
            CyanobacteriaCells = cyanoCells,
            FungiCells = fungiCells,
            OilShaleLayers = oilShaleLayers,
            BiogenicLayers = biogenicLayers,
            AtmosphericO2 = atmo.O2,
            AtmosphericCH4 = atmo.CH4,
        };
    }

    // ── Biogenic sedimentation ────────────────────────────────────────────────

    private int DepositBiogenicSediment(int cellIndex, double timeMa, double deltaMa,
        double temp, double height, double marineP, double cyanoP, double reef,
        bool isShallow, double o2Level)
    {
        var deposited = 0;

        // Coccolith ooze → SED_CHALK (deep marine, high plankton productivity)
        if (!isShallow && marineP > 0.2)
        {
            var thickness = CHALK_DEPOSITION_RATE * marineP * deltaMa;
            if (thickness > 0.001)
            {
                _strat!.PushLayer(cellIndex, new StratigraphicLayer
                {
                    RockType = RockType.SED_CHALK,
                    AgeDeposited = timeMa,
                    Thickness = thickness,
                });
                deposited++;
            }
        }

        // Radiolarian/diatom ooze → SED_CHERT or SED_DIATOMITE (cold nutrient-rich)
        if (temp < 10 && marineP > 0.1)
        {
            var rate = temp < 5 ? DIATOMITE_DEPOSITION_RATE : CHERT_DEPOSITION_RATE;
            var thickness = rate * marineP * deltaMa;
            if (thickness > 0.001)
            {
                _strat!.PushLayer(cellIndex, new StratigraphicLayer
                {
                    RockType = temp < 5 ? RockType.SED_DIATOMITE : RockType.SED_CHERT,
                    AgeDeposited = timeMa,
                    Thickness = thickness,
                });
                deposited++;
            }
        }

        // Reef limestone → SED_LIMESTONE (warm shallow marine with reef)
        if (reef > 0.1)
        {
            var thickness = REEF_LIMESTONE_RATE * reef * deltaMa;
            if (thickness > 0.001)
            {
                _strat!.PushLayer(cellIndex, new StratigraphicLayer
                {
                    RockType = RockType.SED_LIMESTONE,
                    AgeDeposited = timeMa,
                    Thickness = thickness,
                });
                deposited++;
            }
        }

        switch (isShallow)
        {
            // Stromatolite carbonate → SED_LIMESTONE (shallow tidal, microbial mats)
            case true when cyanoP > 0.05:
            {
                var thickness = STROMATOLITE_RATE * cyanoP * deltaMa;
                if (thickness > 0.001)
                {
                    _strat!.PushLayer(cellIndex, new StratigraphicLayer
                    {
                        RockType = RockType.SED_LIMESTONE,
                        AgeDeposited = timeMa,
                        Thickness = thickness,
                    });
                    deposited++;
                }

                break;
            }
            // Phosphorite → SED_PHOSPHORITE (high biomatter, moderate depth)
            case false when marineP > 0.3:
            {
                var thickness = PHOSPHORITE_RATE * marineP * deltaMa;
                if (thickness > 0.001)
                {
                    _strat!.PushLayer(cellIndex, new StratigraphicLayer
                    {
                        RockType = RockType.SED_PHOSPHORITE,
                        AgeDeposited = timeMa,
                        Thickness = thickness,
                    });
                    deposited++;
                }

                break;
            }
        }

        // Banded iron formation → SED_IRONSTONE (low O₂, cyanobacteria active)
        if (!(o2Level < OXYGENATION_THRESHOLD) || !(cyanoP > 0.02)) return deposited;
        var thickness1 = BIF_RATE * cyanoP * deltaMa;
        if (!(thickness1 > 0.001)) return deposited;
        _strat!.PushLayer(cellIndex, new StratigraphicLayer
        {
            RockType = RockType.SED_IRONSTONE,
            AgeDeposited = timeMa,
            Thickness = thickness1,
        });
        deposited++;

        return deposited;
    }

    // ── Petroleum source-rock pipeline ────────────────────────────────────────

    private int ProcessPetroleumConversion(double timeMa, int cellCount)
    {
        var conversions = 0;

        for (var i = 0; i < cellCount; i++)
        {
            double orgCarbon = _state!.OrganicCarbonMap[i];
            if (orgCarbon < 1.0) continue;

            // Estimate burial depth from stratigraphy total thickness
            var burialDepth = _strat!.GetTotalThickness(i);
            if (burialDepth < OIL_WINDOW_MIN_DEPTH) continue;

            // Estimate temperature from burial depth (geothermal gradient ~25°C/km)
            var burialTemp = 15 + burialDepth / 1000 * 25;
            if (burialTemp < OIL_WINDOW_MIN_TEMP || burialTemp > OIL_WINDOW_MAX_TEMP) continue;

            // Convert organic carbon to oil shale
            _strat.PushLayer(i, new StratigraphicLayer
            {
                RockType = RockType.SED_OIL_SHALE,
                AgeDeposited = timeMa,
                Thickness = orgCarbon * 0.1, // thickness proportional to organic carbon
            });

            // Consume some organic carbon
            _state.OrganicCarbonMap[i] = (float)(orgCarbon * 0.8);
            conversions++;

            if (conversions != 1) continue; // Log first conversion per tick
            bus.Emit("PETROLEUM_DEPOSIT", new { cellIndex = i, burialDepth, burialTemp });
            log.Record(new GeoLogEntry
            {
                TimeMa = timeMa, Type = "PETROLEUM_DEPOSIT",
                Description = $"Oil shale at depth {burialDepth:F0}m, {burialTemp:F0}°C"
            });
        }

        return conversions;
    }

    // ── Atmosphere feedback ───────────────────────────────────────────────────

    private static void UpdateAtmosphere(AtmosphericComposition atmo,
        double o2Production, double ch4Production, double co2Drawdown, double deltaMa)
    {
        // O₂ increase from cyanobacteria and phytoplankton
        atmo.O2 = Math.Min(0.35, atmo.O2 + o2Production * O2_PRODUCTION_RATE);

        // CH₄ from anaerobic microbes
        atmo.CH4 = Math.Max(0, atmo.CH4 + ch4Production * CH4_PRODUCTION_RATE);

        // CO₂ drawdown from biological pump
        atmo.CO2 = Math.Max(0.0001, atmo.CO2 - co2Drawdown * CO2_DRAWDOWN_RATE);

        // CH₄ oxidation (naturally decays when O₂ is present)
        if (atmo is { O2: > 0.01, CH4: > 0 })
        {
            var oxidation = atmo.CH4 * 0.01 * deltaMa;
            atmo.CH4 = Math.Max(0, atmo.CH4 - oxidation);
        }
    }
}

/// <summary>Result of a biomatter engine tick.</summary>
public sealed class BiomatterTickResult
{
    public double TotalBiomatter { get; set; }
    public double TotalOrganicCarbon { get; set; }
    public int MarineCells { get; set; }
    public int ReefCells { get; set; }
    public int CyanobacteriaCells { get; set; }
    public int FungiCells { get; set; }
    public int OilShaleLayers { get; set; }
    public int BiogenicLayers { get; set; }
    public double AtmosphericO2 { get; set; }
    public double AtmosphericCH4 { get; set; }
}
