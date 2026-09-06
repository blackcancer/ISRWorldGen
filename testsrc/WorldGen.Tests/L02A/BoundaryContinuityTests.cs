using ISRWorldGen.Core.Atlas.Geometry;

namespace ISRWorldGen.Tests.L02A;

[TestClass]
public sealed class BoundaryContinuityTests
{
    [TestMethod]
    public void DefaultExactRational_IsCanonicalZero()
    {
        ExactRational value = default;

        Assert.AreEqual(ExactRational.Zero, value);
        Assert.AreEqual(ExactRational.One, value + ExactRational.One);
        Assert.AreEqual("0/1", value.ToString());
    }

    [TestMethod]
    public void SmoothAnalyticalField_IsBitIdenticalAcrossFourTileOwners()
    {
        var field = new SmoothField2D(
            constant: 11,
            xLinear: 5,
            zLinear: -7,
            xSquared: 1,
            xz: 3,
            zSquared: 2,
            divisor: 1024);
        WorldBounds southwest = new(0, 0, 8, 8);
        WorldBounds southeast = new(8, 0, 16, 8);
        WorldBounds northwest = new(0, 8, 8, 16);
        WorldBounds northeast = new(8, 8, 16, 16);

        CompareVertical(field, southwest, southeast);
        CompareVertical(field, northwest, northeast);
        CompareHorizontal(field, southwest, northwest);
        CompareHorizontal(field, southeast, northeast);

        BoundaryFieldSample swCorner = field.SampleBoundary(southwest, BoundarySide.Right, 8);
        BoundaryFieldSample neCorner = field.SampleBoundary(northeast, BoundarySide.Left, 8);
        Assert.AreEqual(swCorner.Position, neCorner.Position);
        Assert.AreEqual(swCorner.CanonicalValue, neCorner.CanonicalValue);
        Assert.AreEqual(swCorner.ValueBits, neCorner.ValueBits);
    }

    [TestMethod]
    public void SemiOpenStorage_HasNoPaddingRowAndDoesNotWrap()
    {
        WorldBounds tile = new(-4, -3, 4, 5);
        int ownedCount = 0;
        for (long z = tile.MinZ; z < tile.MaxZExclusive; z++)
        {
            for (long x = tile.MinX; x < tile.MaxXExclusive; x++)
            {
                Assert.IsTrue(tile.Contains(x, z));
                ownedCount++;
            }
        }

        Assert.AreEqual(64, ownedCount);
        Assert.IsFalse(tile.Contains(tile.MaxXExclusive, 0));
        Assert.IsFalse(tile.Contains(0, tile.MaxZExclusive));
        Assert.IsFalse(tile.Contains(tile.MinX - 1, 0));
        Assert.IsFalse(tile.Contains(0, tile.MinZ - 1));

        var field = new SmoothField2D(0, 1, 1, 0, 0, 0, 1);
        BoundaryFieldSample right = field.SampleBoundary(tile, BoundarySide.Right, 0);
        Assert.AreEqual(ExactRational.FromInt64(tile.MaxXExclusive), right.Position.X);
        Assert.IsFalse(tile.Contains(tile.MaxXExclusive, 0), "Boundary queries must not inject padding into ownership.");
    }

    private static void CompareVertical(SmoothField2D field, WorldBounds left, WorldBounds right)
    {
        Assert.AreEqual(left.MaxXExclusive, right.MinX);
        for (long z = left.MinZ; z <= left.MaxZExclusive; z++)
        {
            BoundaryFieldSample fromLeft = field.SampleBoundary(left, BoundarySide.Right, z);
            BoundaryFieldSample fromRight = field.SampleBoundary(right, BoundarySide.Left, z);
            Assert.AreEqual(fromLeft.Position, fromRight.Position);
            Assert.AreEqual(fromLeft.CanonicalValue, fromRight.CanonicalValue);
            Assert.AreEqual(fromLeft.ValueBits, fromRight.ValueBits);
        }
    }

    private static void CompareHorizontal(SmoothField2D field, WorldBounds bottom, WorldBounds top)
    {
        Assert.AreEqual(bottom.MaxZExclusive, top.MinZ);
        for (long x = bottom.MinX; x <= bottom.MaxXExclusive; x++)
        {
            BoundaryFieldSample fromBottom = field.SampleBoundary(bottom, BoundarySide.Top, x);
            BoundaryFieldSample fromTop = field.SampleBoundary(top, BoundarySide.Bottom, x);
            Assert.AreEqual(fromBottom.Position, fromTop.Position);
            Assert.AreEqual(fromBottom.CanonicalValue, fromTop.CanonicalValue);
            Assert.AreEqual(fromBottom.ValueBits, fromTop.ValueBits);
        }
    }
}
