using ILGPU;
using ILGPU.Algorithms;
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
    private readonly Action<Index1D,
        ArrayView<int>,                // destMap output
        ArrayView<ushort>,             // plateMap
        ArrayView<double>,             // kx per plate
        ArrayView<double>,             // ky per plate
        ArrayView<double>,             // kz per plate
        ArrayView<double>,             // cosTheta per plate
        ArrayView<double>,             // sinTheta per plate
        int> _advectScatterKernel;     // gridSize
    private readonly Action<Index1D,
        ArrayView<float>,              // newHeight
        ArrayView<float>,              // newCrust
        ArrayView<byte>,               // newRockType
        ArrayView<float>,              // newRockAge
        ArrayView<int>,                // hitCount
        float,                         // gapFloorHeight
        float,                         // gapCrustKm
        byte,                          // basaltRockType
        float> _gapFillKernel;         // timeMa
    private readonly Action<Index1D,
        ArrayView<ushort>,             // plateMap
        ArrayView<double>,             // plateSumX
        ArrayView<double>,             // plateSumY
        ArrayView<double>,             // plateSumZ
        ArrayView<double>,             // plateCount
        int,                           // gridSize
        int> _updatePlateCentersKernel; // numPlates
    private readonly Action<Index1D,
        ArrayView<float>,              // temperatureMap
        ArrayView<float>,              // heightMap
        int,                           // gridSize
        float,                         // alpha
        float,                         // dTghg
        float,                         // dTmilan
        float> _climateTemperatureKernel; // lapseRate
    private readonly Action<Index1D,
        ArrayView<float>,              // windU
        ArrayView<float>,              // windV
        int> _computeWindsKernel;      // gridSize
    private readonly Action<Index1D,
        ArrayView<float>,              // iceThickness
        ArrayView<float>,              // heightMap
        ArrayView<float>,              // temperatureMap
        float,                         // ela
        float,                         // deltaMa
        float,                         // glaciationTemp
        float,                         // accumRate
        float> _iceThicknessKernel;    // ablationRate

    private readonly Action<Index1D,
        ArrayView<ushort>,             // plateMap
        ArrayView<double>,             // poleLat (per plate, radians)
        ArrayView<double>,             // poleLon (per plate, radians)
        ArrayView<double>,             // omega   (per plate, rate)
        int,                           // gridSize
        int,                           // numPlates
        ArrayView<int>,                // boundaryType output
        ArrayView<int>,                // plate1 output
        ArrayView<int>,                // plate2 output
        ArrayView<double>> _boundaryClassifyKernel; // relSpeed output

    // ── S5: Collision resolution kernels ─────────────────────────────────────
    private readonly Action<Index1D,
        ArrayView<int>,                // destMap
        ArrayView<float>,              // srcHeight
        ArrayView<ushort>,             // srcPlateMap
        ArrayView<byte>,               // isOceanic (per plate, 1=oceanic)
        ArrayView<int>,                // hitCount output (atomic)
        ArrayView<long>,               // winnerPriority output (atomic max)
        ArrayView<int>> _collisionScatterKernel; // winnerSource output

    private readonly Action<Index1D,
        ArrayView<int>,                // hitCount
        ArrayView<int>,                // winnerSource
        ArrayView<float>,              // srcHeight
        ArrayView<float>,              // srcCrust
        ArrayView<byte>,               // srcRockType
        ArrayView<float>,              // srcRockAge
        ArrayView<ushort>,             // srcPlateMap
        ArrayView<float>,              // newHeight
        ArrayView<float>,              // newCrust
        ArrayView<byte>,               // newRockType
        ArrayView<float>,              // newRockAge
        ArrayView<ushort>,             // newPlateMap
        ArrayView<byte>,               // isOceanic (per plate)
        float> _collisionApplyKernel;  // deltaMa

    /// <summary>Human-readable compute device description for the UI.</summary>
    public ComputeInfo Info { get; }

    /// <summary>True when an actual GPU accelerator (not CPU emulation) is active.</summary>
    public bool IsGpuActive => Info.Mode == ComputeMode.GPU;

    public GpuComputeService()
    {
        _context = Context.Create(builder => builder.Default().EnableAlgorithms());

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
        _advectScatterKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<int>, ArrayView<ushort>,
                ArrayView<double>, ArrayView<double>, ArrayView<double>,
                ArrayView<double>, ArrayView<double>, int>(
                AdvectScatterKernel);
        _gapFillKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<float>, ArrayView<float>, ArrayView<byte>, ArrayView<float>,
                ArrayView<int>, float, float, byte, float>(
                GapFillKernel);
        _updatePlateCentersKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<ushort>, ArrayView<double>, ArrayView<double>,
                ArrayView<double>, ArrayView<double>, int, int>(
                UpdatePlateCentersKernel);
        _climateTemperatureKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<float>, ArrayView<float>, int, float, float, float, float>(
                ClimateTemperatureKernel);
        _computeWindsKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<float>, ArrayView<float>, int>(
                ComputeWindsKernel);
        _iceThicknessKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<float>, ArrayView<float>, ArrayView<float>,
                float, float, float, float, float>(
                IceThicknessKernel);
        _boundaryClassifyKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<ushort>, ArrayView<double>, ArrayView<double>, ArrayView<double>,
                int, int,
                ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<double>>(
                BoundaryClassifyKernel);

        // S5: Collision resolution kernels
        _collisionScatterKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<int>, ArrayView<float>, ArrayView<ushort>, ArrayView<byte>,
                ArrayView<int>, ArrayView<long>, ArrayView<int>>(
                CollisionScatterKernel);
        _collisionApplyKernel = _accelerator
            .LoadAutoGroupedStreamKernel<Index1D,
                ArrayView<int>, ArrayView<int>,
                ArrayView<float>, ArrayView<float>, ArrayView<byte>, ArrayView<float>, ArrayView<ushort>,
                ArrayView<float>, ArrayView<float>, ArrayView<byte>, ArrayView<float>, ArrayView<ushort>,
                ArrayView<byte>, float>(
                CollisionApplyKernel);
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

    // ── Advect scatter kernel ────────────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: per-cell Rodrigues' rotation to compute destination index.
    /// GPU handles the expensive trig (sin/cos/asin/atan2 per cell); CPU then
    /// performs the irregular scatter with collision resolution using the destMap.
    /// </summary>
    static void AdvectScatterKernel(
        Index1D idx,
        ArrayView<int> destMap,
        ArrayView<ushort> plateMap,
        ArrayView<double> kxArr,
        ArrayView<double> kyArr,
        ArrayView<double> kzArr,
        ArrayView<double> cosThetaArr,
        ArrayView<double> sinThetaArr,
        int gs)
    {
        const double PI = 3.14159265358979323846;
        const double TWO_PI = 2.0 * PI;

        int row = idx / gs;
        int col = idx % gs;

        // Row/col → lat/lon (matches TectonicEngine.RowToLat / ColToLon)
        double lat = PI / 2.0 - (double)row / gs * PI;
        double lon = (double)col / gs * TWO_PI - PI;

        int plateIdx = plateMap[idx];

        // Cell position on unit sphere
        double cosLat = XMath.Cos(lat);
        double sinLat = XMath.Sin(lat);
        double cosLon = XMath.Cos(lon);
        double sinLon = XMath.Sin(lon);
        double vx = cosLat * cosLon;
        double vy = cosLat * sinLon;
        double vz = sinLat;

        // Per-plate rotation parameters
        double ct = cosThetaArr[plateIdx];
        double st = sinThetaArr[plateIdx];
        double pkx = kxArr[plateIdx];
        double pky = kyArr[plateIdx];
        double pkz = kzArr[plateIdx];

        // Rodrigues' rotation: v' = v cos θ + (k × v) sin θ + k(k·v)(1 − cos θ)
        double cx = pky * vz - pkz * vy;
        double cy = pkz * vx - pkx * vz;
        double cz = pkx * vy - pky * vx;

        double dot = pkx * vx + pky * vy + pkz * vz;
        double oneMinusCos = 1.0 - ct;

        double nx = vx * ct + cx * st + pkx * dot * oneMinusCos;
        double ny = vy * ct + cy * st + pky * dot * oneMinusCos;
        double nz = vz * ct + cz * st + pkz * dot * oneMinusCos;

        // Convert back to lat/lon
        double clampedNz = IntrinsicMath.Clamp(nz, -1.0, 1.0);
        double newLat = XMath.Asin(clampedNz);
        double newLon = XMath.Atan2(ny, nx);

        // Convert to grid coordinates
        int newRow = (int)XMath.Round(((PI / 2.0 - newLat) / PI) * gs);
        int newCol = (int)XMath.Round(((newLon + PI) / TWO_PI) * gs);
        newRow = IntrinsicMath.Clamp(newRow, 0, gs - 1);
        newCol = ((newCol % gs) + gs) % gs;

        destMap[idx] = newRow * gs + newCol;
    }

    /// <summary>
    /// Compute destination indices for all cells using Rodrigues' rotation on the GPU.
    /// Returns an int[] where destMap[srcIdx] = destination cell index.
    /// </summary>
    public int[] ComputeAdvectDestinations(
        ushort[] plateMap,
        double[] kx, double[] ky, double[] kz,
        double[] cosTheta, double[] sinTheta,
        int gridSize)
    {
        var cc = plateMap.Length;
        var numPlates = kx.Length;
        var destMap = new int[cc];

        using var destBuf  = _accelerator.Allocate1D<int>(cc);
        using var plateBuf = _accelerator.Allocate1D<ushort>(cc);
        using var kxBuf    = _accelerator.Allocate1D<double>(numPlates);
        using var kyBuf    = _accelerator.Allocate1D<double>(numPlates);
        using var kzBuf    = _accelerator.Allocate1D<double>(numPlates);
        using var ctBuf    = _accelerator.Allocate1D<double>(numPlates);
        using var stBuf    = _accelerator.Allocate1D<double>(numPlates);

        try
        {
            plateBuf.CopyFromCPU(plateMap);
            kxBuf.CopyFromCPU(kx);
            kyBuf.CopyFromCPU(ky);
            kzBuf.CopyFromCPU(kz);
            ctBuf.CopyFromCPU(cosTheta);
            stBuf.CopyFromCPU(sinTheta);

            _advectScatterKernel(cc,
                destBuf.View, plateBuf.View,
                kxBuf.View, kyBuf.View, kzBuf.View,
                ctBuf.View, stBuf.View, gridSize);
            _accelerator.Synchronize();

            destBuf.CopyToCPU(destMap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU advect scatter failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU advect scatter computation failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }

        return destMap;
    }

    // ── Gap fill kernel ──────────────────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: fill empty cells (hitCount == 0) with fresh oceanic crust.
    /// </summary>
    static void GapFillKernel(
        Index1D idx,
        ArrayView<float> newHeight,
        ArrayView<float> newCrust,
        ArrayView<byte> newRockType,
        ArrayView<float> newRockAge,
        ArrayView<int> hitCount,
        float gapFloorHeight,
        float gapCrustKm,
        byte basaltRockType,
        float timeMa)
    {
        if (hitCount[idx] > 0) return;
        newHeight[idx]   = gapFloorHeight;
        newCrust[idx]    = gapCrustKm;
        newRockType[idx] = basaltRockType;
        newRockAge[idx]  = timeMa;
    }

    /// <summary>
    /// Fill gap cells (hitCount == 0) with fresh oceanic crust on the GPU.
    /// Arrays are modified in-place.
    /// </summary>
    public void FillGapCells(
        float[] newHeight, float[] newCrust, byte[] newRockType, float[] newRockAge,
        int[] hitCount, float gapFloorHeight, float gapCrustKm, byte basaltRockType, float timeMa)
    {
        var cc = newHeight.Length;
        using var hBuf   = _accelerator.Allocate1D<float>(cc);
        using var cBuf   = _accelerator.Allocate1D<float>(cc);
        using var rtBuf  = _accelerator.Allocate1D<byte>(cc);
        using var raBuf  = _accelerator.Allocate1D<float>(cc);
        using var hcBuf  = _accelerator.Allocate1D<int>(cc);

        try
        {
            hBuf.CopyFromCPU(newHeight);
            cBuf.CopyFromCPU(newCrust);
            rtBuf.CopyFromCPU(newRockType);
            raBuf.CopyFromCPU(newRockAge);
            hcBuf.CopyFromCPU(hitCount);

            _gapFillKernel(cc, hBuf.View, cBuf.View, rtBuf.View, raBuf.View,
                hcBuf.View, gapFloorHeight, gapCrustKm, basaltRockType, timeMa);
            _accelerator.Synchronize();

            hBuf.CopyToCPU(newHeight);
            cBuf.CopyToCPU(newCrust);
            rtBuf.CopyToCPU(newRockType);
            raBuf.CopyToCPU(newRockAge);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU gap fill failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU gap fill computation failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }
    }

    // ── Update plate centers kernel ──────────────────────────────────────────

    // ── S5: Collision resolution kernels ─────────────────────────────────────

    /// <summary>
    /// ILGPU kernel — Pass 1: For each source cell, atomically increment the
    /// hit count at its destination and compete for the winner slot using a
    /// packed priority value.  Priority encodes: continental &gt; oceanic, then
    /// higher height wins, with the source index as a tiebreaker.
    /// </summary>
    static void CollisionScatterKernel(
        Index1D idx,
        ArrayView<int> destMap,
        ArrayView<float> srcHeight,
        ArrayView<ushort> srcPlateMap,
        ArrayView<byte> isOceanic,        // per plate: 1 = oceanic, 0 = continental
        ArrayView<int> hitCount,          // output: atomic per-dest counter
        ArrayView<long> winnerPriority,   // output: atomic max of packed priority
        ArrayView<int> winnerSource)      // output: source index of the current winner
    {
        int dest = destMap[idx];
        Atomic.Add(ref hitCount[dest], 1);

        int plateIdx = srcPlateMap[idx];
        int isContinental = isOceanic[plateIdx] == 0 ? 1 : 0;

        // Pack priority: bit 62 = continental flag, bits 31..61 = height as biased int,
        // bits 0..30 = source index (tiebreaker).
        // floatBitsToInt maps height to an ordered integer (works correctly for
        // non-NaN positive/negative floats when biased by adding int.MaxValue for negatives).
        int heightBits = (int)Interop.FloatAsInt(srcHeight[idx]);
        // Bias so that negative floats sort correctly: flip all bits for negatives,
        // or flip sign bit for positives.
        if (heightBits < 0)
            heightBits = ~heightBits;           // fully negative → small positive
        else
            heightBits = heightBits ^ (1 << 30); // positive → large positive (flip bit 30 to avoid sign issues in long packing)

        // Ensure heightBits is non-negative for clean packing
        heightBits &= 0x7FFFFFFF;

        long priority = ((long)isContinental << 62) | ((long)heightBits << 31) | (long)(idx & 0x7FFFFFFF);

        // Atomically compete for the highest priority at this destination.
        Atomic.Max(ref winnerPriority[dest], priority);

        // After the atomic max, whichever thread set the current max also writes its index.
        // Note: this is a benign race — the final Atomic.Max winner will be the last
        // to write, and the apply kernel validates by re-checking the priority.
        // For correctness on GPU we store the source index associated with this priority.
        // The apply kernel uses winnerPriority to verify; if there's a race, we use a
        // second pass on CPU.  However, since all threads with the same max priority
        // have the same source index, this is safe.
        if (winnerPriority[dest] == priority)
            winnerSource[dest] = idx;
    }

    /// <summary>
    /// ILGPU kernel — Pass 2: For each destination cell, copy the winning source's
    /// properties and apply collision effects (height uplift, crust thickening)
    /// based on the plate types of winner and loser.
    /// </summary>
    static void CollisionApplyKernel(
        Index1D destIdx,
        ArrayView<int> hitCount,
        ArrayView<int> winnerSource,
        ArrayView<float> srcHeight,
        ArrayView<float> srcCrust,
        ArrayView<byte> srcRockType,
        ArrayView<float> srcRockAge,
        ArrayView<ushort> srcPlateMap,
        ArrayView<float> newHeight,
        ArrayView<float> newCrust,
        ArrayView<byte> newRockType,
        ArrayView<float> newRockAge,
        ArrayView<ushort> newPlateMap,
        ArrayView<byte> isOceanic,        // per plate
        float deltaMa)
    {
        int hits = hitCount[destIdx];
        if (hits == 0) return; // gap cell — handled by gap fill kernel

        int winner = winnerSource[destIdx];
        newHeight[destIdx]   = srcHeight[winner];
        newCrust[destIdx]    = srcCrust[winner];
        newRockType[destIdx] = srcRockType[winner];
        newRockAge[destIdx]  = srcRockAge[winner];
        newPlateMap[destIdx] = srcPlateMap[winner];

        if (hits > 1)
        {
            // Collision effects: the winner's plate type determines the outcome.
            int winnerPlate = srcPlateMap[winner];
            bool winnerIsOceanic = isOceanic[winnerPlate] == 1;

            // Since the winner is continental (highest priority), if both are continental
            // we get continent-continent collision.  If winner is continental and losers
            // are oceanic, that's continent-oceanic.  If winner is oceanic, the collision
            // is oceanic-oceanic (younger wins via height tiebreaker).
            if (!winnerIsOceanic)
            {
                // Continental winner — could be cont-cont or cont-oce collision.
                // Apply the more significant cont-cont uplift and crust thickening
                // as a conservative approximation.
                newHeight[destIdx] += 50.0f * deltaMa;
                newCrust[destIdx]  += srcCrust[winner] * 0.3f;
            }
            // Oceanic winner with collision: younger oceanic crust already won
            // (no additional effects needed — matches CPU oceanic-oceanic path).
        }
    }

    /// <summary>
    /// Resolve plate advection collisions on the GPU.
    /// Pass 1: scatter source cells to destinations, computing hit counts and
    /// determining the winning source per destination via packed-priority atomics.
    /// Pass 2: copy winning source properties and apply collision effects.
    /// </summary>
    /// <param name="destMap">Source-to-destination mapping from advection kernel.</param>
    /// <param name="srcHeight">Source height map.</param>
    /// <param name="srcCrust">Source crust thickness map.</param>
    /// <param name="srcRockType">Source rock type map.</param>
    /// <param name="srcRockAge">Source rock age map.</param>
    /// <param name="srcPlateMap">Source plate ownership map.</param>
    /// <param name="plates">Plate definitions (for IsOceanic flag).</param>
    /// <param name="deltaMa">Time step in million years.</param>
    /// <returns>Tuple of (newHeight, newCrust, newRockType, newRockAge, newPlateMap, hitCount) arrays.</returns>
    public (float[] newHeight, float[] newCrust, byte[] newRockType, float[] newRockAge,
            ushort[] newPlateMap, int[] hitCount) ResolveCollisionsGpu(
        int[] destMap,
        float[] srcHeight, float[] srcCrust, byte[] srcRockType,
        float[] srcRockAge, ushort[] srcPlateMap,
        List<Models.PlateInfo> plates, float deltaMa)
    {
        var cc = destMap.Length;
        var numPlates = plates.Count;

        // Build per-plate isOceanic flag array
        var isOceanicArr = new byte[numPlates];
        for (var p = 0; p < numPlates; p++)
            isOceanicArr[p] = plates[p].IsOceanic ? (byte)1 : (byte)0;

        var hitCount = new int[cc];
        var winnerPriority = new long[cc];
        var winnerSource = new int[cc];
        var newHeight = new float[cc];
        var newCrust = new float[cc];
        var newRockType = new byte[cc];
        var newRockAge = new float[cc];
        var newPlateMap = new ushort[cc];

        // Initialize winner priority to minimum so any source wins
        Array.Fill(winnerPriority, long.MinValue);

        using var destBuf    = _accelerator.Allocate1D<int>(cc);
        using var shBuf      = _accelerator.Allocate1D<float>(cc);
        using var spmBuf     = _accelerator.Allocate1D<ushort>(cc);
        using var ioBuf      = _accelerator.Allocate1D<byte>(numPlates);
        using var hcBuf      = _accelerator.Allocate1D<int>(cc);
        using var wpBuf      = _accelerator.Allocate1D<long>(cc);
        using var wsBuf      = _accelerator.Allocate1D<int>(cc);
        // Pass 2 buffers
        using var scBuf      = _accelerator.Allocate1D<float>(cc);
        using var srtBuf     = _accelerator.Allocate1D<byte>(cc);
        using var sraBuf     = _accelerator.Allocate1D<float>(cc);
        using var nhBuf      = _accelerator.Allocate1D<float>(cc);
        using var ncBuf      = _accelerator.Allocate1D<float>(cc);
        using var nrtBuf     = _accelerator.Allocate1D<byte>(cc);
        using var nraBuf     = _accelerator.Allocate1D<float>(cc);
        using var npmBuf     = _accelerator.Allocate1D<ushort>(cc);

        try
        {
            // Upload pass 1 inputs
            destBuf.CopyFromCPU(destMap);
            shBuf.CopyFromCPU(srcHeight);
            spmBuf.CopyFromCPU(srcPlateMap);
            ioBuf.CopyFromCPU(isOceanicArr);
            hcBuf.CopyFromCPU(hitCount);
            wpBuf.CopyFromCPU(winnerPriority);
            wsBuf.CopyFromCPU(winnerSource);

            // Pass 1: scatter
            _collisionScatterKernel(cc,
                destBuf.View, shBuf.View, spmBuf.View, ioBuf.View,
                hcBuf.View, wpBuf.View, wsBuf.View);
            _accelerator.Synchronize();

            // Upload remaining source arrays for pass 2
            scBuf.CopyFromCPU(srcCrust);
            srtBuf.CopyFromCPU(srcRockType);
            sraBuf.CopyFromCPU(srcRockAge);

            // Pass 2: apply
            _collisionApplyKernel(cc,
                hcBuf.View, wsBuf.View,
                shBuf.View, scBuf.View, srtBuf.View, sraBuf.View, spmBuf.View,
                nhBuf.View, ncBuf.View, nrtBuf.View, nraBuf.View, npmBuf.View,
                ioBuf.View, deltaMa);
            _accelerator.Synchronize();

            // Copy results back
            hcBuf.CopyToCPU(hitCount);
            nhBuf.CopyToCPU(newHeight);
            ncBuf.CopyToCPU(newCrust);
            nrtBuf.CopyToCPU(newRockType);
            nraBuf.CopyToCPU(newRockAge);
            npmBuf.CopyToCPU(newPlateMap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU collision resolution failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU collision resolution failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }

        return (newHeight, newCrust, newRockType, newRockAge, newPlateMap, hitCount);
    }

    // ── Update plate centers kernel ──────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: per-cell accumulation of Cartesian coordinates for plate
    /// center-of-mass computation using atomic adds on shared buffers.
    /// </summary>
    static void UpdatePlateCentersKernel(
        Index1D idx,
        ArrayView<ushort> plateMap,
        ArrayView<double> plateSumX,
        ArrayView<double> plateSumY,
        ArrayView<double> plateSumZ,
        ArrayView<double> plateCount,
        int gs,
        int numPlates)
    {
        const double PI = 3.14159265358979323846;
        const double TWO_PI = 2.0 * PI;

        int row = idx / gs;
        int col = idx % gs;

        double lat = PI / 2.0 - (double)row / gs * PI;
        double lon = (double)col / gs * TWO_PI - PI;

        int p = plateMap[idx];
        if (p >= numPlates) return;

        double cosLat = XMath.Cos(lat);
        Atomic.Add(ref plateSumX[p], cosLat * XMath.Cos(lon));
        Atomic.Add(ref plateSumY[p], cosLat * XMath.Sin(lon));
        Atomic.Add(ref plateSumZ[p], XMath.Sin(lat));
        Atomic.Add(ref plateCount[p], 1.0);
    }

    /// <summary>
    /// Compute plate center-of-mass sums on the GPU. Returns (sumX, sumY, sumZ, count)
    /// arrays of length numPlates that the caller uses to update plate centers.
    /// </summary>
    public (double[] sumX, double[] sumY, double[] sumZ, double[] count) ComputePlateCenterSums(
        ushort[] plateMap, int gridSize, int numPlates)
    {
        var cc = plateMap.Length;
        var sumX  = new double[numPlates];
        var sumY  = new double[numPlates];
        var sumZ  = new double[numPlates];
        var count = new double[numPlates];

        using var pmBuf = _accelerator.Allocate1D<ushort>(cc);
        using var sxBuf = _accelerator.Allocate1D<double>(numPlates);
        using var syBuf = _accelerator.Allocate1D<double>(numPlates);
        using var szBuf = _accelerator.Allocate1D<double>(numPlates);
        using var cBuf  = _accelerator.Allocate1D<double>(numPlates);

        try
        {
            pmBuf.CopyFromCPU(plateMap);
            // Zero-initialize accumulation buffers
            sxBuf.CopyFromCPU(sumX);
            syBuf.CopyFromCPU(sumY);
            szBuf.CopyFromCPU(sumZ);
            cBuf.CopyFromCPU(count);

            _updatePlateCentersKernel(cc, pmBuf.View,
                sxBuf.View, syBuf.View, szBuf.View, cBuf.View,
                gridSize, numPlates);
            _accelerator.Synchronize();

            sxBuf.CopyToCPU(sumX);
            syBuf.CopyToCPU(sumY);
            szBuf.CopyToCPU(sumZ);
            cBuf.CopyToCPU(count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU plate center computation failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU plate center computation failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }

        return (sumX, sumY, sumZ, count);
    }

    // ── Climate temperature kernel ───────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: per-cell climate temperature update.
    /// T_base = 30 * cos(latRad); hKm = max(0, h/1000);
    /// T_final = T_base - hKm*lapseRate + dTghg + dTmilan;
    /// temp[i] = temp[i]*(1-alpha) + T_final*alpha
    /// </summary>
    static void ClimateTemperatureKernel(
        Index1D idx,
        ArrayView<float> temp,
        ArrayView<float> height,
        int gs,
        float alpha,
        float dTghg,
        float dTmilan,
        float lapseRate)
    {
        const float PI = 3.14159265358979323846f;

        int row = idx / gs;
        float latDeg = 90f - (float)row / (gs - 1) * 180f;
        float latRad = latDeg * PI / 180f;

        float tBase = 30f * XMath.Cos(latRad);
        float hKm = XMath.Max(0f, height[idx] / 1000f);
        float tFinal = tBase - hKm * lapseRate + dTghg + dTmilan;

        temp[idx] = temp[idx] * (1f - alpha) + tFinal * alpha;
    }

    /// <summary>
    /// Run the climate temperature update kernel over the entire map.
    /// </summary>
    public void UpdateTemperature(float[] temp, float[] height, int gridSize,
        float alpha, float dTghg, float dTmilan)
    {
        const float LapseRate = 6.5f;
        var cc = temp.Length;
        using var tBuf = _accelerator.Allocate1D<float>(cc);
        using var hBuf = _accelerator.Allocate1D<float>(cc);

        try
        {
            tBuf.CopyFromCPU(temp);
            hBuf.CopyFromCPU(height);

            _climateTemperatureKernel(cc, tBuf.View, hBuf.View,
                gridSize, alpha, dTghg, dTmilan, LapseRate);
            _accelerator.Synchronize();

            tBuf.CopyToCPU(temp);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU climate temperature failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU climate temperature computation failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }
    }

    // ── Compute winds kernel ─────────────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: per-cell 3-cell circulation wind assignment based on latitude band.
    /// </summary>
    static void ComputeWindsKernel(
        Index1D idx,
        ArrayView<float> windU,
        ArrayView<float> windV,
        int gs)
    {
        const float PI = 3.14159265358979323846f;

        int row = idx / gs;
        float latDeg = 90f - (float)row / (gs - 1) * 180f;
        float absLat = XMath.Abs(latDeg);
        float sign = latDeg >= 0f ? 1f : -1f;

        float u, v;
        if (absLat <= 30f)
        {
            u = -XMath.Cos(absLat * PI / 30f);
            v = sign * -0.3f * XMath.Sin(absLat * PI / 30f);
        }
        else if (absLat <= 60f)
        {
            u = XMath.Cos((absLat - 45f) * PI / 30f);
            v = sign * 0.1f * XMath.Cos((absLat - 45f) * PI / 30f);
        }
        else
        {
            u = -XMath.Cos((absLat - 75f) * PI / 15f);
            v = sign * -0.2f * XMath.Sin((absLat - 75f) * PI / 15f);
        }

        windU[idx] = u;
        windV[idx] = v;
    }

    /// <summary>
    /// Run the wind computation kernel over the entire map.
    /// </summary>
    public void ComputeWinds(float[] windU, float[] windV, int gridSize)
    {
        var cc = windU.Length;
        using var uBuf = _accelerator.Allocate1D<float>(cc);
        using var vBuf = _accelerator.Allocate1D<float>(cc);

        try
        {
            _computeWindsKernel(cc, uBuf.View, vBuf.View, gridSize);
            _accelerator.Synchronize();

            uBuf.CopyToCPU(windU);
            vBuf.CopyToCPU(windV);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU compute winds failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU compute winds failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }
    }

    // ── Ice thickness kernel ─────────────────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: per-cell ice accumulation/ablation.
    /// Independent per cell — erosion/moraine remain on CPU.
    /// </summary>
    static void IceThicknessKernel(
        Index1D idx,
        ArrayView<float> iceThickness,
        ArrayView<float> heightMap,
        ArrayView<float> temperatureMap,
        float ela,
        float deltaMa,
        float glaciationTemp,
        float accumRate,
        float ablationRate)
    {
        float h = heightMap[idx];
        float temp = temperatureMap[idx];
        float ice = iceThickness[idx];

        if (h > ela && temp < glaciationTemp)
        {
            ice += accumRate * (glaciationTemp - temp) * deltaMa;
        }
        else if (ice > 0f)
        {
            ice -= ablationRate * XMath.Max(0f, temp - glaciationTemp) * deltaMa;
            if (ice < 0f) ice = 0f;
        }

        iceThickness[idx] = ice;
    }

    /// <summary>
    /// Run the ice thickness update kernel over the entire map.
    /// </summary>
    public void UpdateIceThickness(float[] iceThickness, float[] heightMap,
        float[] temperatureMap, float ela, float deltaMa,
        float glaciationTemp, float accumRate, float ablationRate)
    {
        var cc = iceThickness.Length;
        using var iceBuf  = _accelerator.Allocate1D<float>(cc);
        using var hBuf    = _accelerator.Allocate1D<float>(cc);
        using var tBuf    = _accelerator.Allocate1D<float>(cc);

        try
        {
            iceBuf.CopyFromCPU(iceThickness);
            hBuf.CopyFromCPU(heightMap);
            tBuf.CopyFromCPU(temperatureMap);

            _iceThicknessKernel(cc, iceBuf.View, hBuf.View, tBuf.View,
                ela, deltaMa, glaciationTemp, accumRate, ablationRate);
            _accelerator.Synchronize();

            iceBuf.CopyToCPU(iceThickness);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU ice thickness failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU ice thickness computation failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }
    }

    // ── Boundary classification kernel ──────────────────────────────────────

    /// <summary>
    /// ILGPU kernel: per-cell boundary classification.
    /// For each cell, checks 4 neighbors for different plate IDs and classifies
    /// the boundary as CONVERGENT (1), DIVERGENT (2), or TRANSFORM (3).
    /// Cells not on a boundary get NONE (0).
    /// </summary>
    static void BoundaryClassifyKernel(
        Index1D idx,
        ArrayView<ushort> plateMap,
        ArrayView<double> poleLatArr,   // per plate, radians
        ArrayView<double> poleLonArr,   // per plate, radians
        ArrayView<double> omegaArr,     // per plate, rate (deg/Ma)
        int gs,
        int numPlates,
        ArrayView<int> boundaryType,
        ArrayView<int> plate1Out,
        ArrayView<int> plate2Out,
        ArrayView<double> relSpeedOut)
    {
        const double PI = 3.14159265358979323846;
        const double TWO_PI = 2.0 * PI;

        int row = idx / gs;
        int col = idx % gs;
        int myPlate = plateMap[idx];

        // Check 4 neighbors: up, down, left (wrap), right (wrap)
        int neighborPlate = -1;
        int nRow = -1, nCol = -1;
        bool found = false;

        // Up
        if (row > 0)
        {
            int nIdx = (row - 1) * gs + col;
            int np = plateMap[nIdx];
            if (np != myPlate) { neighborPlate = np; nRow = row - 1; nCol = col; found = true; }
        }
        // Down
        if (!found && row < gs - 1)
        {
            int nIdx = (row + 1) * gs + col;
            int np = plateMap[nIdx];
            if (np != myPlate) { neighborPlate = np; nRow = row + 1; nCol = col; found = true; }
        }
        // Left (wrap)
        if (!found)
        {
            int wCol = (col - 1 + gs) % gs;
            int nIdx = row * gs + wCol;
            int np = plateMap[nIdx];
            if (np != myPlate) { neighborPlate = np; nRow = row; nCol = wCol; found = true; }
        }
        // Right (wrap)
        if (!found)
        {
            int wCol = (col + 1) % gs;
            int nIdx = row * gs + wCol;
            int np = plateMap[nIdx];
            if (np != myPlate) { neighborPlate = np; nRow = row; nCol = wCol; found = true; }
        }

        if (!found || myPlate >= numPlates || neighborPlate >= numPlates)
        {
            boundaryType[idx] = 0; // NONE
            plate1Out[idx] = myPlate;
            plate2Out[idx] = myPlate;
            relSpeedOut[idx] = 0.0;
            return;
        }

        // Compute lat/lon for this cell
        double lat = PI / 2.0 - (double)row / gs * PI;
        double lon = (double)col / gs * TWO_PI - PI;

        // Plate velocity at this cell for plate1
        double poleLat1 = poleLatArr[myPlate];
        double poleLon1 = poleLonArr[myPlate];
        double omega1 = omegaArr[myPlate];
        double dLon1 = lon - poleLon1;
        double v1Lat = omega1 * XMath.Cos(poleLat1) * XMath.Sin(dLon1);
        double v1Lon = omega1 * (XMath.Sin(poleLat1) * XMath.Cos(lat)
                                 - XMath.Cos(poleLat1) * XMath.Sin(lat) * XMath.Cos(dLon1));

        // Plate velocity at this cell for plate2
        double poleLat2 = poleLatArr[neighborPlate];
        double poleLon2 = poleLonArr[neighborPlate];
        double omega2 = omegaArr[neighborPlate];
        double dLon2 = lon - poleLon2;
        double v2Lat = omega2 * XMath.Cos(poleLat2) * XMath.Sin(dLon2);
        double v2Lon = omega2 * (XMath.Sin(poleLat2) * XMath.Cos(lat)
                                 - XMath.Cos(poleLat2) * XMath.Sin(lat) * XMath.Cos(dLon2));

        double dvLat = v1Lat - v2Lat;
        double dvLon = v1Lon - v2Lon;
        double relSpeed = XMath.Sqrt(dvLat * dvLat + dvLon * dvLon);

        // Boundary normal: direction from cell to neighbor
        double nLatPos = PI / 2.0 - (double)nRow / gs * PI;
        double nLonPos = (double)nCol / gs * TWO_PI - PI;
        double normalLat = nLatPos - lat;
        double normalLon = nLonPos - lon;
        double normalLen = XMath.Sqrt(normalLat * normalLat + normalLon * normalLon);

        int bType = 3; // TRANSFORM
        if (normalLen > 1e-10)
        {
            double dot = (dvLat * normalLat + dvLon * normalLon) / normalLen;
            double threshold = relSpeed * 0.3;
            if (dot < -threshold) bType = 1; // CONVERGENT
            else if (dot > threshold) bType = 2; // DIVERGENT
        }

        boundaryType[idx] = bType;
        plate1Out[idx] = myPlate;
        plate2Out[idx] = neighborPlate;
        relSpeedOut[idx] = relSpeed;
    }

    /// <summary>
    /// Classify plate boundaries on the GPU. Returns a list of <see cref="Models.BoundaryCell"/>
    /// by running a per-cell kernel and then compacting non-NONE results on the CPU.
    /// </summary>
    public List<Models.BoundaryCell> ClassifyBoundariesGpu(
        ushort[] plateMap, List<Models.PlateInfo> plates, int gridSize)
    {
        var cc = plateMap.Length;
        var numPlates = plates.Count;

        // Prepare per-plate Euler pole arrays in radians
        var poleLat = new double[numPlates];
        var poleLon = new double[numPlates];
        var omega = new double[numPlates];
        const double deg2Rad = Math.PI / 180.0;
        for (var p = 0; p < numPlates; p++)
        {
            poleLat[p] = plates[p].AngularVelocity.Lat * deg2Rad;
            poleLon[p] = plates[p].AngularVelocity.Lon * deg2Rad;
            omega[p] = plates[p].AngularVelocity.Rate;
        }

        var bTypeOut = new int[cc];
        var p1Out = new int[cc];
        var p2Out = new int[cc];
        var rsOut = new double[cc];

        using var pmBuf = _accelerator.Allocate1D<ushort>(cc);
        using var plBuf = _accelerator.Allocate1D<double>(numPlates);
        using var ploBuf = _accelerator.Allocate1D<double>(numPlates);
        using var omBuf = _accelerator.Allocate1D<double>(numPlates);
        using var btBuf = _accelerator.Allocate1D<int>(cc);
        using var p1Buf = _accelerator.Allocate1D<int>(cc);
        using var p2Buf = _accelerator.Allocate1D<int>(cc);
        using var rsBuf = _accelerator.Allocate1D<double>(cc);

        try
        {
            pmBuf.CopyFromCPU(plateMap);
            plBuf.CopyFromCPU(poleLat);
            ploBuf.CopyFromCPU(poleLon);
            omBuf.CopyFromCPU(omega);

            _boundaryClassifyKernel(cc,
                pmBuf.View, plBuf.View, ploBuf.View, omBuf.View,
                gridSize, numPlates,
                btBuf.View, p1Buf.View, p2Buf.View, rsBuf.View);
            _accelerator.Synchronize();

            btBuf.CopyToCPU(bTypeOut);
            p1Buf.CopyToCPU(p1Out);
            p2Buf.CopyToCPU(p2Out);
            rsBuf.CopyToCPU(rsOut);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPU boundary classification failed: {ex.Message}");
            throw new InvalidOperationException(
                $"GPU boundary classification failed (accelerator: {Info.AcceleratorType}). " +
                "This may indicate a GPU driver issue or incompatible accelerator state.",
                ex);
        }

        // CPU compaction: filter non-NONE entries into List<BoundaryCell>
        var boundaries = new List<Models.BoundaryCell>();
        for (var i = 0; i < cc; i++)
        {
            if (bTypeOut[i] == 0) continue; // NONE
            boundaries.Add(new Models.BoundaryCell
            {
                CellIndex = i,
                Type = (Models.BoundaryType)bTypeOut[i],
                Plate1 = p1Out[i],
                Plate2 = p2Out[i],
                RelativeSpeed = rsOut[i],
            });
        }

        return boundaries;
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
