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
