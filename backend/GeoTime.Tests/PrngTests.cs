using GeoTime.Core.Proc;

namespace GeoTime.Tests;

public class Xoshiro256ssTests
{
    [Fact]
    public void Next_ReturnsBetweenZeroAndOne()
    {
        var rng = new Xoshiro256ss(42);
        for (var i = 0; i < 1000; i++)
        {
            var v = rng.Next();
            Assert.InRange(v, 0.0, 1.0);
        }
    }

    [Fact]
    public void NextInt_ReturnsWithinRange()
    {
        var rng = new Xoshiro256ss(123);
        for (var i = 0; i < 1000; i++)
        {
            var v = rng.NextInt(5, 15);
            Assert.InRange(v, 5, 15);
        }
    }

    [Fact]
    public void NextFloat_ReturnsWithinRange()
    {
        var rng = new Xoshiro256ss(99);
        for (var i = 0; i < 1000; i++)
        {
            var v = rng.NextFloat(-10.0, 10.0);
            Assert.InRange(v, -10.0, 10.0);
        }
    }

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var rng1 = new Xoshiro256ss(42);
        var rng2 = new Xoshiro256ss(42);
        for (var i = 0; i < 100; i++)
            Assert.Equal(rng1.Next(), rng2.Next());
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var rng1 = new Xoshiro256ss(42);
        var rng2 = new Xoshiro256ss(43);
        var anyDifferent = false;
        for (var i = 0; i < 10; i++)
            if (rng1.Next() != rng2.Next()) anyDifferent = true;
        Assert.True(anyDifferent);
    }
}
