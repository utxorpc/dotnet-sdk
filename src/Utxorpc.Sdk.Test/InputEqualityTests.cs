using Xunit.Abstractions;

namespace Utxorpc.Sdk.Test;

/// <summary>Checks byte-value equality and allocation-free collection keys.</summary>
[Trait("Category", "Unit")]
public sealed class InputEqualityTests(ITestOutputHelper output)
{
    /// <summary>Compares copied and sliced hashes by their contents, including collection lookups.</summary>
    [Fact]
    public void PointsAndReferencesCompareByValue()
    {
        byte[] x = [1, 2, 3], y = [1, 2, 3], z = [1, 2, 4];
        ReadOnlyMemory<byte> slice = new byte[] { 9, 1, 2, 3 }.AsMemory(1);
        BlockPoint a = new(5, x), b = new(5, y), c = new(5, z), d = new(6, x);
        Assert.True(a == b && a == new BlockPoint(5, slice));
        Assert.False(a != b);
        Assert.True(a.Equals(b) && a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.GetHashCode(), new BlockPoint(5, slice).GetHashCode());
        Assert.True(a != c && a != d);
        Assert.False(a.Equals((object)"other"));
        Assert.True(new BlockPoint(0, default) == default(BlockPoint));
        HashSet<BlockPoint> points = [a, b, c, d];
        Assert.Equal(3, points.Count);
        Assert.Contains(new BlockPoint(5, (byte[])x.Clone()), points);
        OutputReference r = new(x, 1), s = new(y, 1), t = new(z, 1), u = new(x, 2);
        Assert.True(r == s && r == new OutputReference(slice, 1));
        Assert.False(r != s);
        Assert.True(r.Equals(s) && r.Equals((object)s));
        Assert.Equal(r.GetHashCode(), s.GetHashCode());
        Assert.Equal(r.GetHashCode(), new OutputReference(slice, 1).GetHashCode());
        Assert.True(r != t && r != u);
        Assert.True(new OutputReference(default, 0) == default(OutputReference));
        HashSet<OutputReference> references = [r, s, t, u];
        Assert.Equal(3, references.Count);
        Dictionary<OutputReference, int> map = new() { [r] = 1 };
        Assert.Equal(1, map[new OutputReference((byte[])x.Clone(), 1)]);
        int sink = 0;
        for (int i = 0; i < 1000; i++) { sink += a.GetHashCode() + r.GetHashCode(); if (a == b && r == s && a.Equals(c) == r.Equals(t)) sink++; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { sink += a.GetHashCode() + r.GetHashCode(); if (a == b && r == s && a.Equals(c) == r.Equals(t)) sink++; }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Equals/GetHashCode/==: {bytes} bytes per 1000 rounds (sink {sink != 0}).");
        Assert.Equal(0, bytes);
    }
}
