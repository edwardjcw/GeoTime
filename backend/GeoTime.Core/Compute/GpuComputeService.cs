using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

namespace GeoTime.Core.Compute;

/// <summary>
/// Manages GPU/CPU compute backend selection and kernel dispatch.
/// On startup, tries to acquire a CUDA or OpenCL GPU accelerator via ILGPU,
/// always preferring the <em>dedicated</em> GPU (NVIDIA/AMD) over any integrated
/// graphics (Intel HD/Iris/Arc) so that simulation kernels run on the most capable
/// accelerator available.
///
/// Selection order:
/// <list type="number">
///   <item>CUDA — dedicated NVIDIA GPU. When multiple CUDA devices are present the
///     one with the most on-device memory is chosen (highest VRAM = dedicated card).
///     Accelerator creation is wrapped in a try/catch so a bad CUDA runtime falls
///     through to the next tier instead of crashing the service.</item>
///   <item>OpenCL — non-integrated GPU preferred. Devices are scored:
///     NVIDIA = 100, AMD = 80, other named GPU = 40, Intel = 10.
///     Within the same score tier the device with more memory wins.</item>
///   <item>CPU (ILGPU multi-threaded) — final fallback.</item>
/// </list>
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

        Accelerator? accelerator = null;
        Device?      selectedDevice = null;

        // ── Tier 1: CUDA (dedicated NVIDIA GPU) ──────────────────────────────
        // CUDA devices are exclusively NVIDIA hardware; Intel integrated GPUs are
        // never CUDA-capable.  If multiple CUDA devices are present (rare dual-GPU
        // workstations) pick the one with the most on-device memory.
        var cudaDevices = _context.Devices
            .Where(d => d.AcceleratorType == AcceleratorType.Cuda)
            .OrderByDescending(d => d.MemorySize)
            .ToList();

        foreach (var candidate in cudaDevices)
        {
            try
            {
                var accel = candidate.CreateAccelerator(_context);
                accelerator   = accel;
                selectedDevice = candidate;
                break;
            }
            catch (Exception ex)
            {
                // CUDA runtime not installed or driver mismatch — try next device.
                System.Diagnostics.Debug.WriteLine(
                    $"[GpuComputeService] CUDA accelerator creation failed for '{candidate.Name}': {ex.Message}. " +
                    "Falling through to OpenCL.");
            }
        }

        // ── Tier 2: OpenCL — prefer discrete GPU over integrated ──────────────
        // Intel integrated graphics (HD Graphics / Iris / Arc) score lowest so
        // that a discrete NVIDIA or AMD card is always preferred when both are
        // available via OpenCL.
        if (accelerator == null)
        {
            var openClDevices = _context.Devices
                .Where(d => d.AcceleratorType == AcceleratorType.OpenCL)
                .Select(d => (Device: d, Score: ScoreOpenClDevice(d)))
                .Where(t => t.Score > 0)
                .OrderByDescending(t => t.Score)
                .ThenByDescending(t => t.Device.MemorySize)
                .ToList();

            foreach (var (candidate, _) in openClDevices)
            {
                try
                {
                    var accel = candidate.CreateAccelerator(_context);
                    accelerator    = accel;
                    selectedDevice = candidate;
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[GpuComputeService] OpenCL accelerator creation failed for '{candidate.Name}': {ex.Message}. " +
                        "Trying next device.");
                }
            }
        }

        // ── Tier 3: CPU (ILGPU multi-threaded) ───────────────────────────────
        if (accelerator == null)
        {
            selectedDevice = _context.GetPreferredDevice(preferCPU: true);
            accelerator    = selectedDevice.CreateAccelerator(_context);
        }

        _accelerator   = accelerator;
        selectedDevice ??= _context.GetPreferredDevice(preferCPU: true);

        var isGpu = selectedDevice.AcceleratorType != AcceleratorType.CPU;
        var mode  = isGpu ? ComputeMode.GPU : ComputeMode.CPU;
        var modeLabel = selectedDevice.AcceleratorType switch
        {
            AcceleratorType.Cuda   => "GPU (CUDA)",
            AcceleratorType.OpenCL => "GPU (OpenCL)",
            _                      => $"CPU (ILGPU · {Environment.ProcessorCount} threads)",
        };
        var memMb = selectedDevice.MemorySize / (1024 * 1024);

        Info = new ComputeInfo(
            mode,
            $"{modeLabel} – {selectedDevice.Name}",
            selectedDevice.AcceleratorType.ToString(),
            memMb);

        // Pre-compile kernels once at startup
        _isostasyKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float, float>(
                IsostasyKernel);
        _diffuseKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int, float>(
                DiffuseKernel);
    }

    /// <summary>
    /// Assigns a preference score to an OpenCL device so the selection algorithm
    /// picks dedicated GPUs (NVIDIA/AMD) over integrated graphics (Intel).
    /// Returns 0 for CPU-type OpenCL devices so they are excluded from the GPU tier.
    /// </summary>
    /// <remarks>
    /// Score bands:
    /// <list type="table">
    ///   <item><term>100</term><description>NVIDIA GPU</description></item>
    ///   <item><term>80</term><description>AMD GPU</description></item>
    ///   <item><term>40</term><description>Any other named GPU (non-Intel, non-CPU)</description></item>
    ///   <item><term>10</term><description>Intel GPU (integrated graphics)</description></item>
    ///   <item><term>0</term><description>CPU — excluded from GPU tier</description></item>
    /// </list>
    /// </remarks>
    private static int ScoreOpenClDevice(Device device)
    {
        var name = device.Name ?? string.Empty;

        // Skip software/CPU OpenCL devices.
        if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
         || name.Contains("pocl", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return 100;

        if (name.Contains("AMD",    StringComparison.OrdinalIgnoreCase)
         || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return 80;

        // Intel integrated graphics (HD / Iris / Arc) — deprioritised.
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return 10;

        // Unknown GPU-type device — better than Intel integrated, worse than NVIDIA/AMD.
        return 40;
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

        try
        {
            hBuf.CopyFromCPU(height);
            cBuf.CopyFromCPU(crust);

            _isostasyKernel(cc, hBuf.View, cBuf.View, relax, factor, offset);
            _accelerator.Synchronize();

            hBuf.CopyToCPU(height);
        }
        catch (Exception ex)
        {
            // Log the error and provide fallback behavior
            System.Diagnostics.Debug.WriteLine($"GPU operation failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU isostasy computation failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state. " +
                "Consider restarting or checking GPU drivers.",
                ex);
        }
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

        try
        {
            buf.CopyFromCPU(temp);
            _diffuseKernel(cc, buf.View, gridSize, alpha);
            _accelerator.Synchronize();
            buf.CopyToCPU(temp);
        }
        catch (Exception ex)
        {
            // Log the error and provide fallback behavior
            System.Diagnostics.Debug.WriteLine($"GPU temperature diffusion failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU temperature diffusion failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }
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

    /// <summary>
    /// On-device memory in megabytes. Useful for confirming a dedicated GPU is in
    /// use (a discrete NVIDIA/AMD card will have far more VRAM than integrated graphics).
    /// 0 when the value is unavailable (CPU fallback).
    /// </summary>
    public long MemoryMb { get; }

    public ComputeInfo(ComputeMode mode, string deviceName, string acceleratorType, long memoryMb = 0)
    {
        Mode          = mode;
        DeviceName    = deviceName;
        AcceleratorType = acceleratorType;
        MemoryMb      = memoryMb;
    }
}
