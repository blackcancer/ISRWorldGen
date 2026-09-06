using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L01A;

[TestClass]
public sealed class CoordinateFoundationTests
{
    private static readonly long[] ExtremeWorldCoordinates =
    [
        long.MinValue,
        int.MinValue - 1L,
        int.MinValue,
        -33,
        -32,
        -31,
        -1,
        0,
        1,
        31,
        32,
        33,
        int.MaxValue,
        int.MaxValue + 1L,
        long.MaxValue,
    ];

    [TestMethod]
    [DataRow(-33L, 32L, -2L, 31L)]
    [DataRow(-32L, 32L, -1L, 0L)]
    [DataRow(-31L, 32L, -1L, 1L)]
    [DataRow(-1L, 32L, -1L, 31L)]
    [DataRow(0L, 32L, 0L, 0L)]
    [DataRow(1L, 32L, 0L, 1L)]
    [DataRow(31L, 32L, 0L, 31L)]
    [DataRow(32L, 32L, 1L, 0L)]
    [DataRow(33L, 32L, 1L, 1L)]
    public void FloorDivision_UsesMathematicalFloor(
        long value,
        long divisor,
        long expectedQuotient,
        long expectedRemainder)
    {
        Assert.AreEqual(expectedQuotient, CoordinateMath.FloorDivide(value, divisor));
        Assert.AreEqual(expectedRemainder, CoordinateMath.FloorModulo(value, divisor));
    }

    [TestMethod]
    public void FloorDivision_RejectsNonPositiveDivisors()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CoordinateMath.FloorDivide(1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CoordinateMath.FloorDivide(1, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CoordinateMath.FloorModulo(1, 0));
    }

    [TestMethod]
    public void ChunkGrid_RejectsNonPositiveSizes()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChunkGrid(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChunkGrid(-1));
    }

    [TestMethod]
    public void WorldChunkLocal_RoundTripsExtremeCoordinates()
    {
        int[] chunkSizes = [1, 2, 31, 32, 257, int.MaxValue];

        foreach (int chunkSize in chunkSizes)
        {
            var grid = new ChunkGrid(chunkSize);

            foreach (long x in ExtremeWorldCoordinates)
            {
                foreach (long z in ExtremeWorldCoordinates)
                {
                    var world = new WorldBlockPosition(x, z);
                    ChunkedBlockPosition split = grid.Split(world);

                    Assert.IsTrue(split.Local.X >= 0 && split.Local.X < chunkSize);
                    Assert.IsTrue(split.Local.Z >= 0 && split.Local.Z < chunkSize);
                    Assert.AreEqual(world, grid.Combine(split));
                }
            }
        }
    }

    [TestMethod]
    public void ChunkLocal_CombineRejectsInvalidLocalsAndOverflow()
    {
        var grid = new ChunkGrid(32);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            grid.Combine(new ChunkedBlockPosition(new ChunkPosition(0, 0), new LocalBlockPosition(-1, 0))));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            grid.Combine(new ChunkedBlockPosition(new ChunkPosition(0, 0), new LocalBlockPosition(32, 0))));
        Assert.ThrowsExactly<OverflowException>(() =>
            grid.Combine(new ChunkedBlockPosition(new ChunkPosition(long.MaxValue, 0), new LocalBlockPosition(0, 0))));
        Assert.ThrowsExactly<OverflowException>(() =>
            grid.Combine(new ChunkedBlockPosition(new ChunkPosition(long.MinValue, 0), new LocalBlockPosition(0, 0))));
    }

    [TestMethod]
    public void SemiOpenIntervals_IncludeMinimumAndExcludeMaximum()
    {
        var domain = new WorldDomain(
            new Int64Interval(-64, 64),
            new Int64Interval(-32, 96),
            height: 512,
            samplingOrigin: new WorldBlockPosition(0, 0));

        Assert.IsTrue(domain.Contains(new WorldBlockPosition(-64, -32)));
        Assert.IsTrue(domain.Contains(new WorldBlockPosition(63, 95)));
        Assert.IsFalse(domain.Contains(new WorldBlockPosition(64, 95)));
        Assert.IsFalse(domain.Contains(new WorldBlockPosition(63, 96)));
        Assert.IsFalse(domain.Contains(new WorldBlockPosition(-65, -32)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            domain.EnsureContains(new WorldBlockPosition(64, 0)));
    }

    [TestMethod]
    public void Domains_RejectEmptyRangesInvalidHeightsAndOutsideOrigins()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Int64Interval(4, 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Int64Interval(5, 4));

        var interval = new Int64Interval(-8, 8);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new WorldDomain(interval, interval, 0, new WorldBlockPosition(0, 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new WorldDomain(interval, interval, 256, new WorldBlockPosition(8, 0)));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new WorldDomain(null!, interval, 256, new WorldBlockPosition(0, 0)));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new WorldDomain(interval, null!, 256, new WorldBlockPosition(0, 0)));
    }

    [TestMethod]
    public void CheckedConversionsAndProducts_RefuseOverflow()
    {
        Assert.AreEqual(int.MinValue, CoordinateMath.ToInt32Checked(int.MinValue));
        Assert.AreEqual(int.MaxValue, CoordinateMath.ToInt32Checked(int.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() => CoordinateMath.ToInt32Checked(int.MinValue - 1L));
        Assert.ThrowsExactly<OverflowException>(() => CoordinateMath.ToInt32Checked(int.MaxValue + 1L));

        Assert.AreEqual(4_294_967_294L, CoordinateMath.MultiplyChecked(int.MaxValue, 2));
        Assert.ThrowsExactly<OverflowException>(() => CoordinateMath.MultiplyChecked(long.MaxValue, 2));
        Assert.ThrowsExactly<OverflowException>(() => CoordinateMath.MultiplyChecked(long.MinValue, -1));
    }
}
