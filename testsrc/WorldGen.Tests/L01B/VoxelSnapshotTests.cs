using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L01B;

[TestClass]
public sealed class VoxelSnapshotTests
{
    [TestMethod]
    public void VoxelSnapshot_IsImmutableSortedAndCanonicallyRoundTrips()
    {
        VoxelCell[] mutable = VoxelTestData.Cells.Reverse().ToArray();
        VoxelSnapshot snapshot = VoxelTestData.Create(mutable);
        byte[] first = VoxelSnapshotBinaryCodec.Serialize(snapshot);

        mutable[0] = default;
        byte[] second = VoxelSnapshotBinaryCodec.Serialize(snapshot);
        VoxelSnapshot restored = VoxelSnapshotBinaryCodec.Deserialize(first);

        CollectionAssert.AreEqual(first, second);
        CollectionAssert.AreEqual(first, VoxelSnapshotBinaryCodec.Serialize(restored));
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<VoxelCell>)snapshot.Voxels)[0] = default);
    }

    [TestMethod]
    public void VoxelSnapshot_RejectsDuplicatesCoordinatesAndUnknownMaterials()
    {
        VoxelCell cell = VoxelTestData.Cells[0];
        Assert.ThrowsExactly<DuplicateStableIdException>(() => VoxelTestData.Create([cell, cell]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => VoxelTestData.Create([cell with { X = -1 }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => VoxelTestData.Create([cell with { X = 2 }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => VoxelTestData.Create([cell with { Y = 4 }]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            VoxelTestData.Create([cell with { Material = (VoxelMaterial)255 }]));
    }

    [TestMethod]
    public void VoxelGolden_HasManualFrozenCanonicalHash()
    {
        // GOLDEN L01B-VOXEL-V1. Seed 73, 2x2x4, four explicit material cells.
        // Deliberately manual: no code path regenerates this constant.
        const string expectedCanonicalSha256 = "98a94ff1351a977d6c992484c2958417293fb5bc69ee0be3dc7ab3779af72957";

        byte[] bytes = VoxelSnapshotBinaryCodec.Serialize(VoxelTestData.Create(VoxelTestData.Cells));
        Console.WriteLine($"L01B_VOXEL_GOLDEN_SHA256={Hash256.Compute(bytes)}");
        Assert.AreEqual(expectedCanonicalSha256, Hash256.Compute(bytes).ToString());
    }
}

internal static class VoxelTestData
{
    internal static readonly IReadOnlyList<VoxelCell> Cells =
    [
        Cell(3, 1, 2, 1, VoxelMaterial.Soil),
        Cell(0, 0, 0, 0, VoxelMaterial.Rock),
        Cell(2, 0, 1, 1, VoxelMaterial.Water),
        Cell(1, 1, 3, 0, VoxelMaterial.Air),
    ];

    internal static VoxelSnapshot Create(IEnumerable<VoxelCell> cells) =>
        VoxelSnapshot.Create(
            SnapshotTestData.CreateIdentity(),
            "l01b.synthetic-voxel",
            revision: 4,
            SnapshotTestData.ParentHashes,
            new SnapshotDimensions(2, 2, 4),
            new SnapshotUnits("block", "block"),
            cells);

    private static VoxelCell Cell(ulong index, long x, int y, long z, VoxelMaterial material) =>
        new(StableId.Derive(RandomDomain.Caverns, StableId.Zero, index), x, y, z, material);
}
