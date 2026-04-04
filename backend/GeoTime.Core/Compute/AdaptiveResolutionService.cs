using GeoTime.Core.Models;

namespace GeoTime.Core.Compute;

/// <summary>
/// Strategy D – Adaptive Resolution.
/// When the camera is at orbital distance the atmosphere and vegetation engines
/// can operate on a coarser 128×128 grid and upsample results back to the full
/// 512×512 grid for display.  This gives a 4–16× speedup for climate/vegetation
/// at the cost of slightly blurred spatial detail.
///
/// The tectonic engine always runs at full 512×512 because geological accuracy
/// requires fine resolution.
/// </summary>
public static class AdaptiveResolutionService
{
    /// <summary>
    /// Downsample a full-resolution array to the coarse grid using 2×2-block average pooling.
    /// </summary>
    /// <param name="full">Source array at gridSize × gridSize.</param>
    /// <param name="fullSize">Full grid dimension (e.g., 512).</param>
    /// <param name="coarseSize">Coarse grid dimension (e.g., 128).</param>
    /// <returns>New array of length coarseSize × coarseSize.</returns>
    public static float[] Downsample(float[] full, int fullSize, int coarseSize)
    {
        var ratio = fullSize / coarseSize;        // e.g., 4 for 512→128
        var coarse = new float[coarseSize * coarseSize];

        Parallel.For(0, coarseSize, cr =>
        {
            for (var cc = 0; cc < coarseSize; cc++)
            {
                var sum = 0f;
                var count = 0;
                for (var dr = 0; dr < ratio; dr++)
                {
                    for (var dc = 0; dc < ratio; dc++)
                    {
                        var fr = cr * ratio + dr;
                        var fc = cc * ratio + dc;
                        if (fr < fullSize && fc < fullSize)
                        {
                            sum += full[fr * fullSize + fc];
                            count++;
                        }
                    }
                }
                coarse[cr * coarseSize + cc] = count > 0 ? sum / count : 0f;
            }
        });

        return coarse;
    }

    /// <summary>
    /// Upsample a coarse-resolution array back to the full grid using bilinear interpolation.
    /// </summary>
    /// <param name="coarse">Source array at coarseSize × coarseSize.</param>
    /// <param name="coarseSize">Coarse grid dimension (e.g., 128).</param>
    /// <param name="fullSize">Full grid dimension (e.g., 512).</param>
    /// <returns>New array of length fullSize × fullSize.</returns>
    public static float[] Upsample(float[] coarse, int coarseSize, int fullSize)
    {
        var full = new float[fullSize * fullSize];
        var scale = (float)(coarseSize - 1) / (fullSize - 1);

        Parallel.For(0, fullSize, fr =>
        {
            for (var fc = 0; fc < fullSize; fc++)
            {
                var cx = fc * scale;
                var cy = fr * scale;
                var cx0 = (int)cx;
                var cy0 = (int)cy;
                var cx1 = Math.Min(cx0 + 1, coarseSize - 1);
                var cy1 = Math.Min(cy0 + 1, coarseSize - 1);
                var tx = cx - cx0;
                var ty = cy - cy0;

                var v00 = coarse[cy0 * coarseSize + cx0];
                var v10 = coarse[cy0 * coarseSize + cx1];
                var v01 = coarse[cy1 * coarseSize + cx0];
                var v11 = coarse[cy1 * coarseSize + cx1];

                full[fr * fullSize + fc] =
                    v00 * (1 - tx) * (1 - ty) +
                    v10 * tx       * (1 - ty) +
                    v01 * (1 - tx) * ty       +
                    v11 * tx       * ty;
            }
        });

        return full;
    }

    /// <summary>
    /// Copy the upsampled coarse result back into the full-resolution target array in-place.
    /// </summary>
    public static void UpsampleInto(float[] coarse, int coarseSize, float[] target, int fullSize)
    {
        var upsampled = Upsample(coarse, coarseSize, fullSize);
        Array.Copy(upsampled, target, target.Length);
    }
}
