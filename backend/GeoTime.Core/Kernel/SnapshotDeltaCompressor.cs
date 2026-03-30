using MessagePack;

namespace GeoTime.Core.Kernel;

/// <summary>
/// Computes and applies sparse delta patches between snapshot byte arrays.
/// Only stores 256-byte blocks that differ, providing efficient delta compression.
/// </summary>
public static class SnapshotDeltaCompressor
{
    private const int BLOCK_SIZE = 256;

    /// <summary>
    /// Compare two byte arrays and produce a sparse delta.
    /// Only includes 256-byte blocks that differ between the two arrays.
    /// </summary>
    public static List<DeltaBlock> ComputeDelta(byte[] from, byte[] to)
    {
        var changes = new List<DeltaBlock>();
        int len = Math.Min(from.Length, to.Length);

        for (int off = 0; off < len; off += BLOCK_SIZE)
        {
            int end = Math.Min(off + BLOCK_SIZE, len);
            bool differs = false;
            for (int j = off; j < end; j++)
            {
                if (from[j] != to[j])
                {
                    differs = true;
                    break;
                }
            }
            if (differs)
            {
                var data = new byte[end - off];
                Buffer.BlockCopy(to, off, data, 0, data.Length);
                changes.Add(new DeltaBlock { Offset = off, Data = data });
            }
        }

        return changes;
    }

    /// <summary>
    /// Apply a delta patch to a byte array.
    /// </summary>
    public static void ApplyDelta(byte[] buffer, List<DeltaBlock> changes)
    {
        foreach (var block in changes)
        {
            Buffer.BlockCopy(block.Data, 0, buffer, block.Offset, block.Data.Length);
        }
    }
}

/// <summary>A block of changed data at a specific offset.</summary>
[MessagePackObject]
public sealed class DeltaBlock
{
    [Key(0)]
    public int Offset { get; set; }

    [Key(1)]
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
