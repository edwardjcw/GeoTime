using GeoTime.Core.Models;

namespace GeoTime.Core.Engines;

/// <summary>
/// Per-cell stratigraphic layer stack management (64-layer budget per cell).
/// </summary>
public sealed class StratigraphyStack
{
    public const int MAX_LAYERS_PER_CELL = 64;
    private readonly Dictionary<int, List<StratigraphicLayer>> _stacks = new();

    public IReadOnlyList<StratigraphicLayer> GetLayers(int cellIndex)
        => _stacks.TryGetValue(cellIndex, out var s) ? s : Array.Empty<StratigraphicLayer>();

    public StratigraphicLayer? GetTopLayer(int cellIndex)
    {
        if (_stacks.TryGetValue(cellIndex, out var s) && s.Count > 0)
            return s[^1];
        return null;
    }

    public double GetTotalThickness(int cellIndex)
    {
        if (!_stacks.TryGetValue(cellIndex, out var s)) return 0;
        return s.Sum(l => l.Thickness);
    }

    public void PushLayer(int cellIndex, StratigraphicLayer layer)
    {
        if (!_stacks.TryGetValue(cellIndex, out var stack))
        {
            stack = [];
            _stacks[cellIndex] = stack;
        }
        stack.Add(layer.Clone());
        while (stack.Count > MAX_LAYERS_PER_CELL)
        {
            var bottom = stack[0];
            stack.RemoveAt(0);
            if (stack.Count > 0) stack[0].Thickness += bottom.Thickness;
        }
    }

    public void InitializeBasement(int cellIndex, bool isOceanic, double ageDeposited)
    {
        var stack = new List<StratigraphicLayer>();
        if (isOceanic)
        {
            stack.Add(new StratigraphicLayer
            {
                RockType = RockType.IGN_GABBRO, AgeDeposited = ageDeposited,
                Thickness = 4000, Deformation = DeformationType.UNDEFORMED,
            });
            stack.Add(new StratigraphicLayer
            {
                RockType = RockType.IGN_PILLOW_BASALT, AgeDeposited = ageDeposited,
                Thickness = 3000, Deformation = DeformationType.UNDEFORMED,
            });
        }
        else
        {
            stack.Add(new StratigraphicLayer
            {
                RockType = RockType.MET_GNEISS, AgeDeposited = ageDeposited,
                Thickness = 15000, Deformation = DeformationType.METAMORPHOSED,
            });
            stack.Add(new StratigraphicLayer
            {
                RockType = RockType.IGN_GRANITE, AgeDeposited = ageDeposited,
                Thickness = 20000, Deformation = DeformationType.UNDEFORMED,
            });
        }
        _stacks[cellIndex] = stack;
    }

    public void ApplyDeformation(int cellIndex, double dipDelta, double direction, DeformationType type)
    {
        if (!_stacks.TryGetValue(cellIndex, out var stack)) return;
        foreach (var layer in stack)
        {
            layer.DipAngle = Math.Clamp(layer.DipAngle + dipDelta, 0, 90);
            layer.DipDirection = direction % 360;
            if (type > layer.Deformation) layer.Deformation = type;
        }
    }

    public double ErodeTop(int cellIndex, double thickness)
    {
        if (!_stacks.TryGetValue(cellIndex, out var stack) || stack.Count == 0) return 0;
        double remaining = thickness, eroded = 0;
        while (remaining > 0 && stack.Count > 0)
        {
            var top = stack[^1];
            if (top.Thickness <= remaining)
            {
                remaining -= top.Thickness;
                eroded += top.Thickness;
                stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                top.Thickness -= remaining;
                eroded += remaining;
                remaining = 0;
            }
        }
        return eroded;
    }

    public int Size => _stacks.Count;
    public void Clear() => _stacks.Clear();
}
