using ISRWorldGen.Core.Contracts;
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

    [TestMethod]
    public void T03_03_CatalogIsExhaustiveImmutableAndChecksumIgnoresInputPermutation()
    {
        KeyValuePair<GeologicalMaterialCode, MaterialProperties>[] source = CatalogPairs();
        MaterialCatalog forward = new(source);
        MaterialCatalog reversed = new(source.Reverse());

        source[0] = Pair(GeologicalMaterialCode.Sandstone, 0, 0, 0);
        Assert.AreEqual(forward.ContentChecksum, reversed.ContentChecksum);
        Assert.HasCount(Enum.GetValues<GeologicalMaterialCode>().Length, forward.Properties);
        foreach (GeologicalMaterialCode code in Enum.GetValues<GeologicalMaterialCode>())
        {
            Assert.IsTrue(forward.Properties.ContainsKey(code));
            MaterialProperties properties = forward.Get(code);
            Assert.AreNotEqual(default, properties);
        }

        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<GeologicalMaterialCode, MaterialProperties>)forward.Properties).Add(GeologicalMaterialCode.Sandstone, default));
        Assert.ThrowsExactly<ArgumentException>(() => new MaterialCatalog(CatalogPairs().Take(4)));
        Assert.ThrowsExactly<ArgumentException>(() => new MaterialCatalog([.. CatalogPairs(), CatalogPairs()[0]]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MaterialCatalog([.. CatalogPairs(), Pair((GeologicalMaterialCode)255, 0.1, 0.1, 0.1)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MaterialProperties(double.NaN, 0.1, 0.1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MaterialProperties(0.1, double.PositiveInfinity, 0.1));
    }

    [TestMethod]
    public void T03_03_StrataAndFracturesHaveStableSemiOpenBoundariesAndComposedPermeability()
    {
        MaterialSnapshot snapshot = SyntheticSnapshot();
        MaterialSnapshot overlapping = new(Catalog(), snapshot.Layers,
        [
            new FractureZone(Id(20), 0, 0, 1, 0, 2, 0.4),
            new FractureZone(Id(21), 0, 0, 0, 1, 2, 0.9),
        ]);

        Assert.AreEqual(GeologicalMaterialCode.Basalt, snapshot.Query(7, 19, 7).MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Limestone, snapshot.Query(7, 20, 7).MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Limestone, snapshot.Query(7, 59, 7).MaterialCode);
        Assert.AreEqual(GeologicalMaterialCode.Sandstone, snapshot.Query(7, 60, 7).MaterialCode);
        Assert.IsTrue(overlapping.Query(2, 45, 2).IsFractured);
        Assert.AreEqual(0.9, overlapping.Query(2, 45, 2).Properties.PermeabilityNormalized, 1e-15);
        Assert.IsFalse(overlapping.Query(3, 45, 3).IsFractured);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.Query(0, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.Query(0, 90, 0));
        Assert.ThrowsExactly<ArgumentException>(() => new MaterialSnapshot(Catalog(), [Layer(1, 0, 20, GeologicalMaterialCode.Basalt), Layer(1, 20, 40, GeologicalMaterialCode.Limestone)], []));
        Assert.ThrowsExactly<ArgumentException>(() => new MaterialSnapshot(Catalog(), snapshot.Layers, [new FractureZone(Id(1), 0, 0, 1, 0, 1, 0.1), new FractureZone(Id(1), 0, 0, 0, 1, 1, 0.1)]));
    }

    [TestMethod]
    public void T03_04_RemovalVolumesRemainSemiOpenAcrossOverlapsAndVerticalExtremes()
    {
        MaterialSnapshot snapshot = SyntheticSnapshot();
        RockRemovalVolume valley = new(RockRemovalKind.Valley, long.MinValue, 1, 20, 60, long.MinValue, 1);
        RockRemovalVolume cavern = new(RockRemovalKind.Cavern, -1, 1, 0, 20, -1, 1);
        MaterialExposure exposure = new(snapshot, [cavern, valley]);

        Assert.IsFalse(exposure.TryQuerySolid(0, 20, 0, out _));
        Assert.IsTrue(exposure.TryQuerySolid(1, 20, 0, out MaterialSample atExclusiveX));
        Assert.AreEqual(GeologicalMaterialCode.Limestone, atExclusiveX.MaterialCode);
        Assert.IsTrue(exposure.TryQuerySolid(0, 60, 0, out MaterialSample atExclusiveY));
        Assert.AreEqual(GeologicalMaterialCode.Sandstone, atExclusiveY.MaterialCode);
        Assert.IsTrue(exposure.TryGetExposedSolidBelow(0, 0, int.MaxValue, out int topExposedY, out MaterialSample topExposed));
        Assert.AreEqual(89, topExposedY);
        Assert.AreEqual(snapshot.Query(0, topExposedY, 0), topExposed);
        Assert.IsFalse(exposure.TryGetExposedSolidBelow(0, 0, int.MinValue, out _, out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RockRemovalVolume((RockRemovalKind)255, 0, 1, 0, 1, 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RockRemovalVolume(RockRemovalKind.Valley, 0, 0, 0, 1, 0, 1));
    }

    [TestMethod]
    public void SnapshotCopiesInputCollectionsAndDeliveryRejectsMissingEndpoints()
    {
        StratigraphicLayer[] layers = SyntheticSnapshot().Layers.ToArray();
        FractureZone[] fractures = SyntheticSnapshot().Fractures.ToArray();
        MaterialSnapshot snapshot = new(Catalog(), layers, fractures);
        Hash256 checksum = snapshot.ContentChecksum;
        layers[0] = Layer(99, 0, 90, GeologicalMaterialCode.Granite);
        fractures[0] = new FractureZone(Id(99), 100, 100, 1, 0, 1, 0.1);

        Assert.AreEqual(checksum, snapshot.ContentChecksum);
        Assert.AreEqual(GeologicalMaterialCode.Limestone, snapshot.Query(0, 45, 1).MaterialCode);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<StratigraphicLayer>)snapshot.Layers).Add(Layer(100, 90, 100, GeologicalMaterialCode.Granite)));
        Assert.ThrowsExactly<ArgumentNullException>(() => MaterialQueryDelivery.Deliver(null!, 0, 0, 0, new RecordingConsumer()));
        Assert.ThrowsExactly<ArgumentNullException>(() => MaterialQueryDelivery.Deliver(snapshot, 0, 0, 0, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new MaterialExposure(null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new MaterialExposure(snapshot, null!));
    }

    [TestMethod]
    public void DownstreamReadersReceiveVersionedIdentityStableBoundsAndReadOnlyStructures()
    {
        MaterialSnapshot forward = SyntheticSnapshot();
        MaterialSnapshot reordered = new(Catalog(), forward.Layers.Reverse(), forward.Fractures.Reverse());
        IMaterialQuery waterOrErosionReader = forward;
        IMaterialStructureQuery caveReader = forward;

        Assert.AreEqual(MaterialSnapshotFormat.SchemaVersion, waterOrErosionReader.Descriptor.SchemaVersion);
        Assert.AreEqual(forward.ContentChecksum, waterOrErosionReader.Descriptor.ContentChecksum);
        Assert.AreEqual(forward.Descriptor, reordered.Descriptor);
        Assert.AreEqual(0, waterOrErosionReader.Descriptor.VerticalBounds.BottomInclusiveY);
        Assert.AreEqual(90, waterOrErosionReader.Descriptor.VerticalBounds.TopExclusiveY);
        Assert.IsTrue(waterOrErosionReader.TryQuery(long.MinValue, 20, long.MaxValue, out MaterialSample lowerInterface));
        Assert.AreEqual(GeologicalMaterialCode.Limestone, lowerInterface.MaterialCode);
        Assert.IsFalse(waterOrErosionReader.TryQuery(0, 90, 0, out _));
        Assert.HasCount(3, caveReader.Layers);
        Assert.HasCount(1, caveReader.Fractures);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<FractureZone>)caveReader.Fractures).Add(new FractureZone(Id(99), 0, 0, 1, 0, 1, 0.1)));
    }

    private static MaterialSnapshot SyntheticSnapshot() => new(
        Catalog(),
        [
            Layer(0, 0, 20, GeologicalMaterialCode.Basalt),
            Layer(1, 20, 60, GeologicalMaterialCode.Limestone),
            Layer(2, 60, 90, GeologicalMaterialCode.Sandstone),
        ],
        [new FractureZone(Id(10), 0, 0, 1, 0, 2, 0.9)]);

    private static MaterialCatalog Catalog() => new(CatalogPairs());

    private static KeyValuePair<GeologicalMaterialCode, MaterialProperties>[] CatalogPairs() =>
    [
        Pair(GeologicalMaterialCode.Sandstone, 0.45, 0.1, 0.55),
        Pair(GeologicalMaterialCode.Shale, 0.25, 0.05, 0.2),
        Pair(GeologicalMaterialCode.Limestone, 0.6, 0.95, 0.35),
        Pair(GeologicalMaterialCode.Basalt, 0.9, 0.02, 0.08),
        Pair(GeologicalMaterialCode.Granite, 0.85, 0.01, 0.04),
    ];

    private static KeyValuePair<GeologicalMaterialCode, MaterialProperties> Pair(GeologicalMaterialCode code, double erosion, double solubility, double permeability) => new(code, new MaterialProperties(erosion, solubility, permeability));

    private static StratigraphicLayer Layer(ulong id, int bottom, int top, GeologicalMaterialCode code) => new(Id(id), bottom, top, code);

    private static StableId Id(ulong index) => StableId.Derive(RandomDomain.Geology, StableId.Zero, index + 50_000);

    private sealed class RecordingConsumer : IMaterialSampleConsumer
    {
        public MaterialSample LastSample { get; private set; }

        public void Consume(MaterialSample sample) => LastSample = sample;
    }
}
