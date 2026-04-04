using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

namespace GeoTime.Core.Compute;

/// <summary>
/// Manages GPU/CPU compute backend selection and kernel dispatch.
/// On startup, tries to acquire a CUDA or OpenCL GPU accelerator via ILGPU.
/// Falls back to the ILGPU CPU accelerator (multi-threaded) if no GPU is available.
/// Exposes <see cref="ComputeInfo"/> for the UI toolbar indicator.
/// </summary>
public sealed class GpuComputeService : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly Action<Index1D, ArrayView<float>, ArrayView<float>, float, float, float> _isostasyKernel;
    private readonly Action<Index1D, ArrayView<float>, int, float> _diffuseKernel;

    /// <summary>Human-readable compute device description for the UI.</summary>
    public ComputeInfo Info { get; }

    /// <summary>True when an actual GPU accelerator (not CPU emulation) is active.</summary>
    public bool IsGpuActive => Info.Mode == ComputeMode.GPU;

    public GpuComputeService()
    {
        _context = Context.CreateDefault();

        // GetPreferredDevice(preferCPU: false) returns the best GPU device if available,
        // or the CPU device if no GPU is present.
        var device = _context.GetPreferredDevice(preferCPU: false);
        _accelerator = device.CreateAccelerator(_context);

        var isGpu = device.AcceleratorType != AcceleratorType.CPU;
        var mode = isGpu ? ComputeMode.GPU : ComputeMode.CPU;
        var modeLabel = device.AcceleratorType switch
        {
            AcceleratorType.Cuda   => "GPU (CUDA)",
            AcceleratorType.OpenCL => "GPU (OpenCL)",
            _                      => $"CPU (ILGPU · {Environment.ProcessorCount} threads)",
        };

        Info = new ComputeInfo(mode, $"{modeLabel} – {device.Name}", device.AcceleratorType.ToString());

        // Pre-compile kernels once at startup
        _isostasyKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float, float>(
                IsostasyKernel);
        _diffuseKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int, float>(
                DiffuseKernel);
    }

    // ── Isostasy kernel ──────────────────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: per-cell isostatic equilibrium update.
    /// height[i] = lerp(height[i], equilibrium[i], relax)
    /// equilibrium[i] = crust[i] * factor + offset
    /// </summary>
    static void IsostasyKernel(
        Index1D idx,
        ArrayView<float> height,
        ArrayView<float> crust,
        float relax,
        float factor,
        float offset)
    {
        float eq = crust[idx] * factor + offset;
        height[idx] = height[idx] * (1f - relax) + eq * relax;
    }

    /// <summary>
    /// Run the isostasy kernel on height/crust arrays using the selected accelerator.
    /// </summary>
    public void ApplyIsostasy(float[] height, float[] crust, float relax, float factor, float offset)
    {
        var cc = height.Length;
        using var hBuf = _accelerator.Allocate1D<float>(cc);
        using var cBuf = _accelerator.Allocate1D<float>(cc);

        hBuf.CopyFromCPU(height);
        cBuf.CopyFromCPU(crust);

        _isostasyKernel(cc, hBuf.View, cBuf.View, relax, factor, offset);
        _accelerator.Synchronize();

        hBuf.CopyToCPU(height);
    }

    // ── Temperature diffusion kernel ─────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: 5-point Laplacian diffusion step (flat torus topology).
    /// temp[i] += alpha * (sum_neighbours - 4*temp[i])
    /// Written into the same array in-place (acceptable for small alpha).
    /// </summary>
    static void DiffuseKernel(
        Index1D idx,
        ArrayView<float> temp,
        int gs,
        float alpha)
    {
        int row = idx / gs;
        int col = idx % gs;

        int up    = ((row - 1 + gs) % gs) * gs + col;
        int down  = ((row + 1)      % gs) * gs + col;
        int left  = row * gs + (col - 1 + gs) % gs;
        int right = row * gs + (col + 1)      % gs;

        float lap = temp[up] + temp[down] + temp[left] + temp[right] - 4f * temp[idx];
        temp[idx] += alpha * lap;
    }

    /// <summary>
    /// Run one temperature diffusion step over the entire temperature map.
    /// </summary>
    public void DiffuseTemperature(float[] temp, int gridSize, float alpha)
    {
        var cc = temp.Length;
        using var buf = _accelerator.Allocate1D<float>(cc);
        buf.CopyFromCPU(temp);
        _diffuseKernel(cc, buf.View, gridSize, alpha);
        _accelerator.Synchronize();
        buf.CopyToCPU(temp);
    }

    public void Dispose()
    {
        _accelerator.Dispose();
        _context.Dispose();
    }
}

/// <summary>Compute backend mode.</summary>
public enum ComputeMode { CPU, GPU }

/// <summary>Snapshot of the active compute backend for display in the UI.</summary>
public sealed class ComputeInfo
{
    public ComputeMode Mode { get; }
    public string DeviceName { get; }
    public string AcceleratorType { get; }

    public ComputeInfo(ComputeMode mode, string deviceName, string acceleratorType)
    {
        Mode = mode;
        DeviceName = deviceName;
        AcceleratorType = acceleratorType;
    }
}
