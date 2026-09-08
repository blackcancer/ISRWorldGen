using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Stratigraphy;

namespace ISRWorldGen.Tests.L14B;

[TestClass]
public sealed class StratigraphySnapshotTests
{
    [TestMethod]
    public void T14_02_AnalyticCrossSectionsRespectTiltThicknessFaultAndIntrusionPriority()
    {
        GeologyVolumeSnapshot volume = Fixture();

        // Independent fixture equations: shale top=60+0.5x, thickness=30; basalt top=30+0.5x.
        Assert.AreEqual("shale", volume.SampleRock(0, 59, 0).RockKey);
        Assert.AreEqual("basalt", volume.SampleRock(0, 29, 0).RockKey);
        Assert.AreEqual("shale", volume.SampleRock(20, 69, 0).RockKey);
        Assert.AreEqual("basalt", volume.SampleRock(20, 39, 0).RockKey);
        Assert.AreEqual("granite", volume.SampleRock(0, 40, 0).RockKey); // intrusive body has explicit higher priority.
        Assert.AreEqual("basalt", volume.SampleRock(0, 31, 0).RockKey); // positive side fault samples y-10.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StratigraphicLayer(Id(99), "shale", 0, new(0, 0, 0), 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StratigraphicSurface(double.NaN, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StratigraphicFault(Id(98), 0, 0, 0, 0, 0, 1));
    }

    [TestMethod]
    public void T14_03_IncisionExposesTheSameImmutableRockAndSeparatesAlluvium()
    {
        GeologyVolumeSnapshot volume = Fixture();
        GeologySample before = volume.SampleRock(20, 39, 0);
        GeologySample exposedAfterValley = volume.SampleRock(20, 39, 0);
        var alluvium = new SurfaceMaterialWitness("silt", 4d, "river-42", exposedAfterValley.RockKey);

        Assert.AreEqual(before, exposedAfterValley);
        Assert.AreEqual("basalt", exposedAfterValley.RockKey);
        Assert.AreEqual("river-42", alluvium.DepositProvenance);
        Assert.AreEqual("basalt", alluvium.BedrockKey);
        // Mutant oracle: choosing from the new surface Y=69 would incorrectly return shale.
        Assert.AreNotEqual(volume.SampleRock(20, 69, 0).RockKey, exposedAfterValley.RockKey);
    }

    [TestMethod]
    public void T14_04_GlobalCoordinatesIgnoreRegionTilingAndSamplingOrder()
    {
        GeologyVolumeSnapshot forward = Fixture();
        GeologyVolumeSnapshot reordered = Fixture(reverse: true);
        (long X, int Y, long Z)[] points = [(-33, 31, -17), (-1, 31, 0), (0, 31, 0), (20, 39, 0), (48, 83, -7)];

        GeologySample[] inTiles = points.Select(point => forward.SampleRock(point.X, point.Y, point.Z)).ToArray();
        GeologySample[] workersReordered = points.Reverse().AsParallel().AsOrdered().Select(point => reordered.SampleRock(point.X, point.Y, point.Z)).Reverse().ToArray();
        CollectionAssert.AreEqual(inTiles, workersReordered);
        Assert.AreEqual(forward.Fingerprint, reordered.Fingerprint);
        Assert.AreEqual("shale", forward.SampleRock(-1, 31, 0).RockKey);
        Assert.AreEqual("basalt", forward.SampleRock(0, 31, 0).RockKey);
    }

    [TestMethod]
    public void T14_05_RevisionGateRefusesMixedSnapshotsAndCarriesProperties()
    {
        GeologyVolumeSnapshot revisionOne = Fixture();
        GeologyVolumeSnapshot revisionTwo = Fixture(revision: "geo-r2");
        var recorder = new ConsumerWitness(revisionOne);
        recorder.Record(revisionOne.SampleRock(20, 39, 0));

        Assert.AreEqual("geo-r1", recorder.Revision);
        Assert.AreEqual(0.9d, recorder.Properties.ErosionResistance, 1e-15);
        Assert.ThrowsExactly<InvalidOperationException>(() => GeologyRevisionGate.RequireSame(revisionOne, revisionTwo));
    }

    private static GeologyVolumeSnapshot Fixture(bool reverse = false, string revision = "geo-r1")
    {
        ContentCatalogSnapshot catalog = new("catalog-r1", "vintagestory-1.22.7", "asset-r1", Hash256.Compute("catalog-r1"u8),
        [
            new("basalt", "game:rock-basalt", RockFamily.Igneous, new(0.9, 0.02, 0.08)),
            new("granite", "game:rock-granite", RockFamily.Igneous, new(0.85, 0.01, 0.04)),
            new("shale", "game:rock-shale", RockFamily.Sedimentary, new(0.25, 0.1, 0.3)),
        ]);
        StratigraphicLayer[] layers =
        [
            new(Id(1), "shale", 1, new(60, 0.5, 0), 30),
            new(Id(2), "basalt", 0, new(30, 0.5, 0), 90),
        ];
        StratigraphicFault[] faults = [new(Id(3), 1, 0, 0, 1, 0, 10)];
        IntrusionVolume[] intrusions = [new(Id(4), "granite", 5, 0, 40, 0, 3, 5, 3)];
        return new(revision, catalog, "blocks-r1", -100, 120, reverse ? layers.Reverse() : layers, reverse ? faults.Reverse() : faults, reverse ? intrusions.Reverse() : intrusions);
    }

    private static StableId Id(ulong index) => StableId.Derive(RandomDomain.Geology, StableId.Zero, 14000 + index);

    private readonly record struct SurfaceMaterialWitness(string DepositKey, double DepositThickness, string DepositProvenance, string BedrockKey);
    private sealed class ConsumerWitness(IGeologyVolumeQuery expected)
    {
        public string Revision { get; private set; } = string.Empty;
        public RockModelProperties Properties { get; private set; }
        public void Record(GeologySample sample) { GeologyRevisionGate.RequireSame(expected, new FixedQuery(sample, expected.Identity)); Revision = sample.GeologyRevision; Properties = sample.Properties; }
        private sealed class FixedQuery(GeologySample sample, GeologyRevisionIdentity identity) : IGeologyVolumeQuery
        {
            public GeologyRevisionIdentity Identity { get; } = identity;
            public GeologySample SampleRock(long x, int y, long z) => sample;
        }
    }
}
