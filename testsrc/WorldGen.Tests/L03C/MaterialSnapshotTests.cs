using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Tests.L03C;

[TestClass]
public sealed class MaterialSnapshotTests
{
    [TestMethod]
    public void T03_03_SedimentaryVolcanicPropertiesAreBoundedCompleteAndFracturesReachConsumers()
    {
        MaterialSnapshot snapshot = SyntheticSnapshot();

        MaterialSample sandstone = snapshot.Query(100, 70, 100);
        MaterialSample limestoneFracture = snapshot.Query(0, 45, 1);
        MaterialSample basalt = snapshot.Query(100, 10, 100);
        var consumer = new RecordingConsumer();
        MaterialQueryDelivery.Deliver(snapshot, 0, 45, 1, consumer);

        Assert.AreEqual(GeologicalMaterialCode.Sandstone, sandstone.MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Limestone, limestoneFracture.MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Basalt, basalt.MaterialCode);
        Assert.IsTrue(limestoneFracture.IsFractured);
        Assert.AreEqual(0.9, limestoneFracture.Properties.PermeabilityNormalized, 1e-15);
        Assert.AreEqual(limestoneFracture, consumer.LastSample);
        Assert.AreEqual(limestoneFracture.Properties, consumer.LastSample.Properties);
        foreach (MaterialProperties properties in snapshot.Catalog.Properties.Values)
        {
            Assert.IsTrue(double.IsFinite(properties.ErosionResistanceNormalized));
            Assert.IsTrue(double.IsFinite(properties.SolubilityNormalized));
            Assert.IsTrue(double.IsFinite(properties.PermeabilityNormalized));
            Assert.IsGreaterThanOrEqualTo(0d, properties.ErosionResistanceNormalized);
            Assert.IsLessThanOrEqualTo(1d, properties.ErosionResistanceNormalized);
            Assert.IsGreaterThanOrEqualTo(0d, properties.SolubilityNormalized);
            Assert.IsLessThanOrEqualTo(1d, properties.SolubilityNormalized);
            Assert.IsGreaterThanOrEqualTo(0d, properties.PermeabilityNormalized);
            Assert.IsLessThanOrEqualTo(1d, properties.PermeabilityNormalized);
        }
    }

    [TestMethod]
    public void T03_04_ExcavationRevealsExistingStrataAndChunkBoundariesDoNotReassignThem()
    {
        MaterialSnapshot snapshot = SyntheticSnapshot();
        RockRemovalVolume valley = new(RockRemovalKind.Valley, 0, 32, 60, 90, 0, 32);
        RockRemovalVolume cavern = new(RockRemovalKind.Cavern, 4, 28, 20, 60, 4, 28);
        var exposure = new MaterialExposure(snapshot, [valley, cavern]);
        ChunkGrid chunks = new(16);

        Assert.IsFalse(exposure.TryQuerySolid(15, 70, 15, out _));
        Assert.IsFalse(exposure.TryQuerySolid(15, 45, 15, out _));
        Assert.IsTrue(exposure.TryGetExposedSolidBelow(15, 15, valley.BottomInclusiveY, out int valleyFloorY, out MaterialSample valleyFloor));
        Assert.IsTrue(exposure.TryGetExposedSolidBelow(15, 15, cavern.BottomInclusiveY, out int cavernFloorY, out MaterialSample cavernFloor));
        Assert.AreEqual(19, cavernFloorY);
        Assert.AreEqual(GeologicalMaterialCode.Basalt, cavernFloor.MaterialCode);
        Assert.AreEqual(snapshot.Query(15, cavernFloorY, 15), cavernFloor);

        Assert.AreEqual(new ChunkPosition(0, 0), chunks.Split(new WorldBlockPosition(15, 15)).Chunk);
        Assert.AreEqual(new ChunkPosition(1, 0), chunks.Split(new WorldBlockPosition(16, 15)).Chunk);
        Assert.IsTrue(exposure.TryGetExposedSolidBelow(15, 15, valley.BottomInclusiveY, out _, out MaterialSample leftBoundary));
        Assert.IsTrue(exposure.TryGetExposedSolidBelow(16, 15, valley.BottomInclusiveY, out _, out MaterialSample rightBoundary));
        Assert.AreEqual(19, valleyFloorY); // The stacked cavern is also removed below the valley.
        Assert.AreEqual(leftBoundary.MaterialCode, rightBoundary.MaterialCode);
        Assert.AreEqual(leftBoundary.LayerId, rightBoundary.LayerId);
    }

    [TestMethod]
    public void InvalidGapsAndOutOfRangeQueriesAreRejectedBeforeMaterialPublication()
    {
        MaterialCatalog catalog = Catalog();
        Assert.ThrowsExactly<ArgumentException>(() => new MaterialSnapshot(catalog,
        [
            Layer(0, 0, 20, GeologicalMaterialCode.Basalt),
            Layer(1, 21, 40, GeologicalMaterialCode.Limestone),
        ], []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SyntheticSnapshot().Query(0, 100, 0));
    }

    [TestMethod]
    public void InputOrderAndOverlappingFracturesProduceTheSameSnapshotAndBoundaryMaterial()
    {
        MaterialSnapshot forward = SyntheticSnapshot();
        MaterialSnapshot reversed = new(Catalog(), forward.Layers.Reverse(), forward.Fractures.Reverse());
        var overlap = new MaterialSnapshot(Catalog(), forward.Layers,
        [
            new FractureZone(Id(11), 0, 0, 1, 0, 2, 0.7),
            new FractureZone(Id(12), 0, 0, 1, 0, 2, 0.95),
        ]);

        Assert.AreEqual(forward.ContentChecksum, reversed.ContentChecksum);
        Assert.AreEqual(forward.Query(100, 45, 100), reversed.Query(100, 45, 100));
        MaterialSample edge = overlap.Query(0, 45, 2);
        Assert.IsTrue(edge.IsFractured);
        Assert.AreEqual(0.95, edge.Properties.PermeabilityNormalized, 1e-15);
    }

    [TestMethod]
    public void FractureCoordinatesCannotOverflowAndDirectionsAreBounded()
    {
        var fracture = new FractureZone(Id(13), long.MinValue, long.MaxValue, 1_000_000, -1_000_000, 0.5, 0.6);
        Assert.IsFalse(fracture.Contains(long.MaxValue, long.MaxValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FractureZone(Id(14), 0, 0, 1_000_001, 0, 1, 0.5));
    }

    private static MaterialSnapshot SyntheticSnapshot() => new(
        Catalog(),
        [
            Layer(0, 0, 20, GeologicalMaterialCode.Basalt),
            Layer(1, 20, 60, GeologicalMaterialCode.Limestone),
            Layer(2, 60, 90, GeologicalMaterialCode.Sandstone),
        ],
        [new FractureZone(Id(10), 0, 0, 1, 0, 2, 0.9)]);

    private static MaterialCatalog Catalog() => new(
    [
        Pair(GeologicalMaterialCode.Sandstone, 0.45, 0.1, 0.55),
        Pair(GeologicalMaterialCode.Shale, 0.25, 0.05, 0.2),
        Pair(GeologicalMaterialCode.Limestone, 0.6, 0.95, 0.35),
        Pair(GeologicalMaterialCode.Basalt, 0.9, 0.02, 0.08),
        Pair(GeologicalMaterialCode.Granite, 0.85, 0.01, 0.04),
    ]);

    private static KeyValuePair<GeologicalMaterialCode, MaterialProperties> Pair(GeologicalMaterialCode code, double erosion, double solubility, double permeability) => new(code, new MaterialProperties(erosion, solubility, permeability));

    private static StratigraphicLayer Layer(ulong id, int bottom, int top, GeologicalMaterialCode code) => new(Id(id), bottom, top, code);

    private static StableId Id(ulong index) => StableId.Derive(RandomDomain.Geology, StableId.Zero, index + 50_000);

    private sealed class RecordingConsumer : IMaterialSampleConsumer
    {
        public MaterialSample LastSample { get; private set; }

        public void Consume(MaterialSample sample) => LastSample = sample;
    }
}
