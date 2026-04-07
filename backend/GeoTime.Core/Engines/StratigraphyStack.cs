using GeoTime.Core.Models;

namespace GeoTime.Core.Engines;

/// <summary>
/// Per-cell stratigraphic layer stack management (64-layer budget per cell).
/// Thread-safe for parallel engine access.
/// </summary>
public sealed class StratigraphyStack
{
    public const int MAX_LAYERS_PER_CELL = 64;
    private readonly Dictionary<int, List<StratigraphicLayer>> _stacks = new();
    private readonly Lock _lockObject = new();

    public IReadOnlyList<StratigraphicLayer> GetLayers(int cellIndex)
    {
        lock (_lockObject)
        {
            return _stacks.TryGetValue(cellIndex, out var s) ? s : Array.Empty<StratigraphicLayer>();
        }
    }

    public StratigraphicLayer? GetTopLayer(int cellIndex)
    {
        lock (_lockObject)
        {
            if (_stacks.TryGetValue(cellIndex, out var s) && s.Count > 0)
                return s[^1];
            return null;
        }
    }

    public double GetTotalThickness(int cellIndex)
    {
        lock (_lockObject)
        {
            if (!_stacks.TryGetValue(cellIndex, out var s)) return 0;
            return s.Sum(l => l.Thickness);
        }
    }

    public void PushLayer(int cellIndex, StratigraphicLayer layer)
    {
        lock (_lockObject)
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
    }

    public void InitializeBasement(int cellIndex, bool isOceanic, double ageDeposited)
    {
        lock (_lockObject)
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
    }

    public void ApplyDeformation(int cellIndex, double dipDelta, double direction, DeformationType type)
    {
        lock (_lockObject)
        {
            if (!_stacks.TryGetValue(cellIndex, out var stack)) return;
            foreach (var layer in stack)
            {
                layer.DipAngle = Math.Clamp(layer.DipAngle + dipDelta, 0, 90);
                layer.DipDirection = direction % 360;
                if (type > layer.Deformation) layer.Deformation = type;
            }
        }
    }

    public double ErodeTop(int cellIndex, double thickness)
    {
        lock (_lockObject)
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
    }

    /// <summary>
    /// Remap stratigraphy columns after plate advection.
    /// <paramref name="mapping"/> maps source cell index → destination cell index.
    /// Columns that map to the same destination are merged (layers concatenated).
    /// Gap cells (hitCount == 0) get a fresh oceanic basement.
    /// </summary>
    public void RemapColumns(int[] mapping, int cellCount, int[] hitCount, double timeMa)
    {
        lock (_lockObject)
        {
            var newStacks = new Dictionary<int, List<StratigraphicLayer>>();

            // Move existing columns to their new destinations.
            for (var src = 0; src < cellCount; src++)
            {
                if (!_stacks.TryGetValue(src, out var srcStack) || srcStack.Count == 0)
                    continue;

                var dest = mapping[src];
                if (!newStacks.TryGetValue(dest, out var destStack))
                {
                    destStack = new List<StratigraphicLayer>(srcStack.Count);
                    newStacks[dest] = destStack;
                }

                foreach (var layer in srcStack)
                    destStack.Add(layer.Clone());

                // Enforce budget.
                while (destStack.Count > MAX_LAYERS_PER_CELL)
                {
                    var bottom = destStack[0];
                    destStack.RemoveAt(0);
                    if (destStack.Count > 0) destStack[0].Thickness += bottom.Thickness;
                }
            }

            // Fill gap cells with fresh oceanic basement.
            for (var i = 0; i < cellCount; i++)
            {
                if (hitCount[i] > 0) continue;
                newStacks[i] =
                [
                    new StratigraphicLayer
                    {
                        RockType = RockType.IGN_GABBRO, AgeDeposited = timeMa,
                        Thickness = 4000, Deformation = DeformationType.UNDEFORMED,
                    },
                    new StratigraphicLayer
                    {
                        RockType = RockType.IGN_PILLOW_BASALT, AgeDeposited = timeMa,
                        Thickness = 3000, Deformation = DeformationType.UNDEFORMED,
                    },
                ];
            }

            _stacks.Clear();
            foreach (var (key, value) in newStacks)
                _stacks[key] = value;
        }
    }

    public int Size
    {
        get
        {
            lock (_lockObject)
            {
                return _stacks.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_lockObject)
        {
            _stacks.Clear();
        }
    }
}
