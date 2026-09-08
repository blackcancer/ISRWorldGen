using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Stratigraphy;

namespace ISRWorldGen.Tests.L14B;

[TestClass]
public sealed class StratigraphySnapshotTests
{
    [TestMethod]
    public void T14_02_AnalyticCrossSectionsRejectAnInvertedFaultNormal()
    {
        GeologyVolumeSnapshot volume = Fixture();
        GeologyVolumeSnapshot normalInvertedMutant = Fixture(faultNormalX: -1d);
        GeologyVolumeSnapshot priorityOracle = Fixture(competingIntrusions: true);
        GeologyVolumeSnapshot priorityInvertedMutant = Fixture(competingIntrusions: true, granitePriority: 4, basaltPriority: 5);

        Assert.AreEqual("shale", volume.SampleRock(20, 69, 0).RockKey);
        Assert.AreEqual("basalt", volume.SampleRock(20, 39, 0).RockKey);
        Assert.AreEqual("granite", volume.SampleRock(0, 40, 0).RockKey);
        Assert.AreEqual("basalt", volume.SampleRock(1, 31, 0).RockKey);
        Assert.AreEqual("shale", normalInvertedMutant.SampleRock(1, 31, 0).RockKey);
        Assert.AreEqual("granite", priorityOracle.SampleRock(0, 40, 0).RockKey);
        Assert.AreEqual("basalt", priorityInvertedMutant.SampleRock(0, 40, 0).RockKey);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StratigraphicLayer(Id(99), "shale", 0, new(0, 0, 0), 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StratigraphicSurface(double.NaN, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new StratigraphicFault(Id(98), 0, 0, 0, 0, 0, 1));
    }

    [TestMethod]
    public void T14_03_IncisionSamplesTheExposedCoordinateAndKeepsAlluvialProvenanceSeparate()
    {
        GeologyVolumeSnapshot volume = Fixture();
        var recordingVolume = new RecordingQuery(volume);
        var surface = new StratigraphySurfaceSampler(recordingVolume);
        AlluvialDeposit deposit = new("silt", 4d, "river-42");

        SurfaceMaterialSample exposed = surface.SampleAfterIncision(20, 39, 0, deposit);

        Assert.AreEqual((20L, 39, 0L), recordingVolume.LastCoordinate);
        Assert.AreEqual("basalt", exposed.Bedrock.RockKey);
        Assert.AreEqual("river-42", exposed.Deposit!.Provenance);
        Assert.AreEqual("basalt", volume.SampleRock(exposed.X, exposed.ExposedY, exposed.Z).RockKey);
        Assert.AreNotEqual(volume.SampleRock(20, 69, 0).RockKey, exposed.Bedrock.RockKey);
    }

    [TestMethod]
    public void T14_04_RegionalFixtureHasStableSeamsAndRejectsARegionLocalOffset()
    {
        GeologyVolumeSnapshot volume = Fixture();
        RegionFixture fixture = new(16,
        [
            new(-1, 0, 15, 31, 0, "shale"),
            new(0, 0, 0, 31, 0, "basalt"),
            new(-1, -1, 15, 50, 15, "shale"),
            new(0, -1, 0, 50, 15, "shale"),
        ]);

        RegionObservation[] canonical = fixture.Sample(volume);
        RegionObservation[] reorderedWorkers = fixture.Sample(volume, reverseOrder: true, parallel: true);
        RegionObservation[] shiftedRegionMutant = fixture.Sample(volume, shiftedRegion: (0, 0), xOffset: -1, assertExpected: false);

        CollectionAssert.AreEqual(new[] { "shale", "shale", "shale", "basalt" }, canonical.Select(value => value.RockKey).ToArray());
        CollectionAssert.AreEqual(canonical, reorderedWorkers);
        Assert.AreEqual(RegionFixture.Digest(canonical), RegionFixture.Digest(reorderedWorkers));
        Assert.AreNotEqual(RegionFixture.Digest(canonical), RegionFixture.Digest(shiftedRegionMutant));
        Assert.AreEqual("shale", shiftedRegionMutant[3].RockKey);
    }

    [TestMethod]
    public void T14_05_SnapshotConsumerReceivesPublishedIdentityAndRejectsStaleSnapshot()
    {
        GeologyVolumeSnapshot current = Fixture();
        GeologyVolumeSnapshot stale = Fixture(revision: "geo-r2");
        var consumer = new ConsumerWitness();

        GeologySnapshotDelivery.Deliver(current, consumer);

        Assert.AreEqual(current.Identity, consumer.Received!.Identity);
        Assert.AreEqual(0.9d, consumer.Received.SampleRock(20, 39, 0).Properties.ErosionResistance, 1e-15);
        Assert.ThrowsExactly<InvalidOperationException>(() => consumer.RequireCompatible(stale));
    }

    private static GeologyVolumeSnapshot Fixture(bool reverse = false, string revision = "geo-r1", double faultNormalX = 1d, bool competingIntrusions = false, int granitePriority = 5, int basaltPriority = 4)
    {
        ContentCatalogSnapshot catalog = new("catalog-r1", "vintagestory-1.22.7", "asset-r1", Hash256.Compute("catalog-r1"u8),
        [
            new("basalt", "game:rock-basalt", RockFamily.Igneous, new(0.9, 0.02, 0.08)),
            new("granite", "game:rock-granite", RockFamily.Igneous, new(0.85, 0.01, 0.04)),
            new("shale", "game:rock-shale", RockFamily.Sedimentary, new(0.25, 0.1, 0.3)),
        ]);
        StratigraphicLayer[] layers = [new(Id(1), "shale", 1, new(60, 0.5, 0), 30), new(Id(2), "basalt", 0, new(30, 0.5, 0), 90)];
        StratigraphicFault[] faults = [new(Id(3), 1, 0, 0, faultNormalX, 0, 10)];
        IntrusionVolume[] intrusions = competingIntrusions
            ? [new(Id(4), "granite", granitePriority, 0, 40, 0, 3, 5, 3), new(Id(5), "basalt", basaltPriority, 0, 40, 0, 3, 5, 3)]
            : [new(Id(4), "granite", granitePriority, 0, 40, 0, 3, 5, 3)];
        return new(revision, catalog, "blocks-r1", -100, 120, reverse ? layers.Reverse() : layers, reverse ? faults.Reverse() : faults, reverse ? intrusions.Reverse() : intrusions);
    }

    private static StableId Id(ulong index) => StableId.Derive(RandomDomain.Geology, StableId.Zero, 14000 + index);

    private sealed class RecordingQuery(IGeologyVolumeQuery inner) : IGeologyVolumeQuery
    {
        public GeologyRevisionIdentity Identity => inner.Identity;
        public (long X, int Y, long Z) LastCoordinate { get; private set; }
        public GeologySample SampleRock(long x, int y, long z) { LastCoordinate = (x, y, z); return inner.SampleRock(x, y, z); }
    }

    private sealed class ConsumerWitness : IGeologySnapshotConsumer
    {
        public GeologyVolumeSnapshot? Received { get; private set; }
        public void Consume(GeologyVolumeSnapshot snapshot) => Received = snapshot;
        public void RequireCompatible(GeologyVolumeSnapshot candidate) => GeologyRevisionGate.RequireSame(Received ?? throw new InvalidOperationException("No snapshot was delivered."), candidate);
    }

    private readonly record struct RegionPoint(int RegionX, int RegionZ, int LocalX, int Y, int LocalZ, string ExpectedRock);
    private readonly record struct RegionObservation(int RegionX, int RegionZ, long WorldX, int Y, long WorldZ, string RockKey);
    private sealed class RegionFixture(int regionSize, IReadOnlyList<RegionPoint> points)
    {
        public RegionObservation[] Sample(IGeologyVolumeQuery volume, bool reverseOrder = false, bool parallel = false, (int X, int Z)? shiftedRegion = null, int xOffset = 0, bool assertExpected = true)
        {
            RegionPoint[] ordered = (reverseOrder ? points.Reverse() : points).ToArray();
            Func<RegionPoint, RegionObservation> samplePoint = point =>
            {
                long x = ((long)point.RegionX * regionSize) + point.LocalX;
                if (shiftedRegion == (point.RegionX, point.RegionZ)) x += xOffset;
                long z = ((long)point.RegionZ * regionSize) + point.LocalZ;
                GeologySample sample = volume.SampleRock(x, point.Y, z);
                if (assertExpected) Assert.AreEqual(point.ExpectedRock, sample.RockKey, "Analytic regional fixture mismatch.");
                return new RegionObservation(point.RegionX, point.RegionZ, x, point.Y, z, sample.RockKey);
            };
            IEnumerable<RegionObservation> observations = parallel
                ? ordered.AsParallel().WithDegreeOfParallelism(2).Select(samplePoint).ToArray()
                : ordered.Select(samplePoint);
            return observations.OrderBy(value => value.RegionZ).ThenBy(value => value.RegionX).ThenBy(value => value.WorldZ).ThenBy(value => value.WorldX).ToArray();
        }
        public static Hash256 Digest(IEnumerable<RegionObservation> observations) => Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('\n', observations.Select(value => $"{value.RegionX}|{value.RegionZ}|{value.WorldX}|{value.Y}|{value.WorldZ}|{value.RockKey}"))));
    }
}
