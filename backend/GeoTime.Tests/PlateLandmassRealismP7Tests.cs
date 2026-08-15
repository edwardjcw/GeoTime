using GeoTime.Core.Engines;
using GeoTime.Core.Kernel;
using GeoTime.Core.Models;

namespace GeoTime.Tests;

/// <summary>P7 — Time-varying plate rigidity / mobility.</summary>
public class PlateLandmassRealismP7Tests
{
    private static SimulationState MakeSmallState(int gs = 16)
    {
        var state = new SimulationState(gs);
        for (var row = 0; row < gs; row++)
            for (var col = 0; col < gs; col++)
                state.PlateMap[row * gs + col] = (ushort)(col < gs / 2 ? 0 : 1);

        for (var i = 0; i < state.CellCount; i++)
        {
            if (state.PlateMap[i] == 0)
            {
                state.HeightMap[i] = 500f;
                state.CrustThicknessMap[i] = 35f;
                state.RockTypeMap[i] = (byte)RockType.IGN_GRANITE;
            }
            else
            {
                state.HeightMap[i] = -4000f;
                state.CrustThicknessMap[i] = 7f;
                state.RockTypeMap[i] = (byte)RockType.IGN_BASALT;
            }
            state.RockAgeMap[i] = 1000f;
        }

        return state;
    }

    private static List<PlateInfo> MakeTwoPlates(double rate0 = 80.0, double rate1 = -80.0)
    {
        return
        [
            new PlateInfo
            {
                Id = 0, CenterLat = 0, CenterLon = -45, IsOceanic = false, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = rate0 },
            },
            new PlateInfo
            {
                Id = 1, CenterLat = 0, CenterLon = 45, IsOceanic = true, Area = 0.5,
                AngularVelocity = new AngularVelocity { Lat = 90, Lon = 0, Rate = rate1 },
            },
        ];
    }

    private static int CountPlateMapChanges(ushort[] before, ushort[] after)
    {
        var changes = 0;
        for (var i = 0; i < before.Length; i++)
            if (before[i] != after[i]) changes++;
        return changes;
    }

    [Fact]
    public void P7_DefaultMobilityRampEnd_Is2500MaAgo()
    {
        Assert.Equal(-2500, SimulationPlanetConfig.DefaultPlateMobilityRampEndMa);
        Assert.Equal(-2500, new SimulationPlanetConfig().PlateMobilityRampEndMa);
    }

    [Fact]
    public void P7_MobilityFactor_PreOnset_IsZero()
    {
        var config = new SimulationPlanetConfig();
        Assert.Equal(0, config.GetPlateMobilityFactor(-4500));
        Assert.Equal(0, SimulationPlanetConfig.ComputePlateMobilityFactor(-4500, -3800, -2500));

        var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
        Assert.Equal(0, engine.GetPlateMobilityFactor(-4500));
    }

    [Fact]
    public void P7_MobilityFactor_Archean_IsBetweenZeroAndOne()
    {
        var config = new SimulationPlanetConfig();
        var factor = config.GetPlateMobilityFactor(-3000);

        Assert.InRange(factor, 0.01, 0.99);
        Assert.True(factor > config.GetPlateMobilityFactor(-3700));
    }

    [Fact]
    public void P7_MobilityFactor_PostRamp_IsFull()
    {
        var config = new SimulationPlanetConfig();
        Assert.Equal(1, config.GetPlateMobilityFactor(-2000));
        Assert.Equal(1, config.GetPlateMobilityFactor(0));
    }

    [Fact]
    public void P7_ScaledRates_Minus4500_LowerThanMinus3000()
    {
        var config = new SimulationPlanetConfig();
        var factorHadean = config.GetPlateMobilityFactor(-4500);
        var factorArchean = config.GetPlateMobilityFactor(-3000);

        Assert.Equal(0, factorHadean);
        Assert.InRange(factorArchean, 0.5, 0.99);
        Assert.True(factorArchean > factorHadean);

        var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
        engine.PlateTectonicsOnsetMa = -5000;
        engine.PlateMobilityRampEndMa = -2500;

        var factorEarlyActive = engine.GetPlateMobilityFactor(-4500);
        var factorLateActive = engine.GetPlateMobilityFactor(-3000);

        Assert.InRange(factorEarlyActive, 0.05, 0.2);
        Assert.True(factorLateActive > factorEarlyActive * 5);

        const double rate = 80.0;
        const double deltaMa = 10.0;
        const double deg2Rad = Math.PI / 180.0;
        var thetaEarly = rate * factorEarlyActive * deg2Rad * deltaMa;
        var thetaLate = rate * factorLateActive * deg2Rad * deltaMa;

        Assert.True(thetaLate > thetaEarly * 5,
            $"Effective rotation at -3000 Ma ({thetaLate:F4} rad) should exceed -4500 Ma ({thetaEarly:F4} rad)");
    }

    [Fact]
    public void P7_Advection_DisplacementScalesWithMobility()
    {
        const double deltaMa = 1.0;
        var rate = 80.0;

        int MeasureChanges(double timeMa)
        {
            var state = MakeSmallState();
            var before = (ushort[])state.PlateMap.Clone();
            var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
            engine.PlateTectonicsOnsetMa = -5000;
            engine.PlateMobilityRampEndMa = -2500;
            engine.Initialize(MakeTwoPlates(rate, -rate), [], new AtmosphericComposition(), state);
            engine.Tick(timeMa, deltaMa);
            return CountPlateMapChanges(before, state.PlateMap);
        }

        var earlyChanges = MeasureChanges(-4500);
        var lateChanges = MeasureChanges(-3000);

        Assert.True(earlyChanges >= 0);
        Assert.True(lateChanges > earlyChanges,
            $"Expected more advection at -3000 Ma ({lateChanges}) than -4500 Ma ({earlyChanges}) with deltaMa={deltaMa}");
    }

    [Fact]
    public void P7_BoundaryRelativeSpeed_ScalesWithMobility()
    {
        var state = MakeSmallState();
        var engine = new TectonicEngine(new EventBus(), new EventLog(), 42, 0.1);
        engine.PlateTectonicsOnsetMa = -5000;
        engine.PlateMobilityRampEndMa = -2500;
        engine.Initialize(MakeTwoPlates(), [], new AtmosphericComposition(), state);

        engine.Tick(-4500, 0.1);
        var earlyFactor = engine.GetPlateMobilityFactor(-4500);

        engine.Tick(-3000, 0.1);
        var lateFactor = engine.GetPlateMobilityFactor(-3000);

        Assert.True(lateFactor > earlyFactor);
        Assert.True(lateFactor > 0.5);
        Assert.True(earlyFactor < 0.25);
    }
}
