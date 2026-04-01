using GeoTime.Core.Models;
using GeoTime.Core.Engines;

namespace GeoTime.Tests;

public class StratigraphyTests
{
    [Fact]
    public void PushLayer_AddsToStack()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.IGN_BASALT, Thickness = 100, AgeDeposited = -4000
        });
        Assert.Single(stack.GetLayers(0));
        Assert.Equal(100, stack.GetTotalThickness(0));
    }

    [Fact]
    public void PushLayer_MergesWhenExceedsMax()
    {
        var stack = new StratigraphyStack();
        for (var i = 0; i < StratigraphyStack.MAX_LAYERS_PER_CELL + 10; i++)
            stack.PushLayer(0, new StratigraphicLayer
            {
                RockType = RockType.SED_SANDSTONE, Thickness = 10, AgeDeposited = i,
            });
        Assert.Equal(StratigraphyStack.MAX_LAYERS_PER_CELL, stack.GetLayers(0).Count);
    }

    [Fact]
    public void ErodeTop_RemovesMaterial()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.IGN_GRANITE, Thickness = 100, AgeDeposited = -4000,
        });
        var eroded = stack.ErodeTop(0, 40);
        Assert.Equal(40, eroded);
        Assert.Equal(60, stack.GetTotalThickness(0));
    }

    [Fact]
    public void ErodeTop_RemovesEntireLayer()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.IGN_GRANITE, Thickness = 50, AgeDeposited = -4000,
        });
        var eroded = stack.ErodeTop(0, 100);
        Assert.Equal(50, eroded);
        Assert.Empty(stack.GetLayers(0));
    }

    [Fact]
    public void InitializeBasement_OceanicCrust()
    {
        var stack = new StratigraphyStack();
        stack.InitializeBasement(0, isOceanic: true, ageDeposited: -4000);
        var layers = stack.GetLayers(0);
        Assert.Equal(2, layers.Count);
        Assert.Equal(RockType.IGN_GABBRO, layers[0].RockType);
        Assert.Equal(RockType.IGN_PILLOW_BASALT, layers[1].RockType);
    }

    [Fact]
    public void InitializeBasement_ContinentalCrust()
    {
        var stack = new StratigraphyStack();
        stack.InitializeBasement(0, isOceanic: false, ageDeposited: -4000);
        var layers = stack.GetLayers(0);
        Assert.Equal(2, layers.Count);
        Assert.Equal(RockType.MET_GNEISS, layers[0].RockType);
        Assert.Equal(RockType.IGN_GRANITE, layers[1].RockType);
    }

    [Fact]
    public void ApplyDeformation_UpdatesLayers()
    {
        var stack = new StratigraphyStack();
        stack.PushLayer(0, new StratigraphicLayer
        {
            RockType = RockType.SED_LIMESTONE, Thickness = 100,
            DipAngle = 5, Deformation = DeformationType.UNDEFORMED,
        });
        stack.ApplyDeformation(0, 10, 45, DeformationType.FOLDED);
        var top = stack.GetTopLayer(0);
        Assert.NotNull(top);
        Assert.Equal(15, top.DipAngle);
        Assert.Equal(DeformationType.FOLDED, top.Deformation);
    }
}
