using GeoTime.Core.Kernel;

namespace GeoTime.Tests;

public class SnapshotDeltaCompressorTests
{
    [Fact]
    public void ComputeDelta_IdenticalArrays_ReturnsEmptyDelta()
    {
        var a = new byte[512];
        var b = new byte[512];
        for (int i = 0; i < 512; i++) a[i] = b[i] = (byte)(i % 256);

        var delta = SnapshotDeltaCompressor.ComputeDelta(a, b);
        Assert.Empty(delta);
    }

    [Fact]
    public void ComputeDelta_SingleBlockDiffers_ReturnsOneBlock()
    {
        var a = new byte[512];
        var b = new byte[512];
        b[10] = 0xFF; // differs in first block

        var delta = SnapshotDeltaCompressor.ComputeDelta(a, b);
        Assert.Single(delta);
        Assert.Equal(0, delta[0].Offset);
    }

    [Fact]
    public void ComputeDelta_MultipleBlocksDiffer_ReturnsCorrectCount()
    {
        var a = new byte[768]; // 3 blocks of 256
        var b = new byte[768];
        b[0] = 1;   // block 0 differs
        b[512] = 1; // block 2 differs

        var delta = SnapshotDeltaCompressor.ComputeDelta(a, b);
        Assert.Equal(2, delta.Count);
        Assert.Equal(0, delta[0].Offset);
        Assert.Equal(512, delta[1].Offset);
    }

    [Fact]
    public void ApplyDelta_RestoresOriginal()
    {
        var original = new byte[512];
        var modified = new byte[512];
        for (int i = 0; i < 512; i++) original[i] = (byte)(i % 256);
        modified[100] = 0xFF;
        modified[300] = 0xAB;

        var delta = SnapshotDeltaCompressor.ComputeDelta(original, modified);

        var target = new byte[512];
        Buffer.BlockCopy(original, 0, target, 0, 512);
        SnapshotDeltaCompressor.ApplyDelta(target, delta);

        Assert.Equal(modified, target);
    }

    [Fact]
    public void RoundTrip_DeltaCompression()
    {
        var rng = new Random(42);
        var from = new byte[1024];
        var to = new byte[1024];
        rng.NextBytes(from);
        Buffer.BlockCopy(from, 0, to, 0, 1024);
        // Modify a few bytes
        to[50] = (byte)(from[50] ^ 0xFF);
        to[600] = (byte)(from[600] ^ 0xFF);

        var delta = SnapshotDeltaCompressor.ComputeDelta(from, to);
        Assert.True(delta.Count > 0);

        var reconstructed = new byte[1024];
        Buffer.BlockCopy(from, 0, reconstructed, 0, 1024);
        SnapshotDeltaCompressor.ApplyDelta(reconstructed, delta);

        Assert.Equal(to, reconstructed);
    }
}
