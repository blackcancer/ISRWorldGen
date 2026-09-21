using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Landscapes;
using ISRWorldGen.Core.Hydrology.Depressions;
using ISRWorldGen.Core.Hydrology.Discharge;

namespace ISRWorldGen.Tests.L05A;

[TestClass]
public sealed class DrainageReliefCouplingTests
{
    [TestMethod]
    public void ImplicitChannelStepUsesActualDischargeAndCountsAllRemovedMaterial()
    {
        var fixture = Fixture(false, .1);
        var settings = new DrainageIncisionSettings(.1, 10, 100);
        var result = DrainageReliefCoupling.Step(fixture.Topology, fixture.Flow, fixture.Positions, 100, 0, settings);
        double alpha1 = .1 * Math.Sqrt(fixture.Flow.Reaches[1].DischargeModelVolumePerYear / 100);
        double alpha2 = .1 * Math.Sqrt(fixture.Flow.Reaches[2].DischargeModelVolumePerYear / 100);
        double h1 = 5 / (1 + alpha1), h2 = (10 + alpha2 * h1) / (1 + alpha2);
        Assert.AreEqual(0d, result.Samples[0].After);
        Assert.AreEqual(h1, result.Samples[1].After, 1e-12);
        Assert.AreEqual(h2, result.Samples[2].After, 1e-12);
        Assert.AreEqual((15 - h1 - h2) * 100, result.ExportedSedimentModelVolume, 1e-10);
        CollectionAssert.AreEqual(new[] { 0d, 5d, 10d }, fixture.Topology.Cells.Select(cell => cell.PhysicalElevation).ToArray());
    }

    [TestMethod]
    public void ZeroDischargeLeavesTerrainExactlyUnchanged()
    {
        var fixture = Fixture(false, 0);
        var result = DrainageReliefCoupling.Step(fixture.Topology, fixture.Flow, fixture.Positions, 100, 0, new(.5, 10, 100));
        Assert.IsTrue(result.Samples.All(sample => sample.Before == sample.After && sample.RemovedDepth == 0d));
        Assert.AreEqual(0d, result.ExportedSedimentModelVolume);
    }

    [TestMethod]
    public void VirtualLakeFloorIsNotMistakenForAnIncisableAscendingChannel()
    {
        var fixture = Fixture(true, .1);
        var result = DrainageReliefCoupling.Step(fixture.Topology, fixture.Flow, fixture.Positions, 100, 0, new(.5, 10, 100));
        Assert.AreEqual(2d, result.Samples[2].Before);
        Assert.AreEqual(2d, result.Samples[2].After);
        Assert.AreEqual(0d, result.Samples[2].RemovedDepth);
        Assert.IsGreaterThan(0d, result.ExportedSedimentModelVolume);
    }

    [TestMethod]
    public void RepeatedConcurrentReadsDoNotChangeAnyPublishedSnapshot()
    {
        var fixture = Fixture(false, .1); var settings = new DrainageIncisionSettings(.1, 10, 100);
        var expected = DrainageReliefCoupling.Step(fixture.Topology, fixture.Flow, fixture.Positions, 100, 0, settings);
        var results = new DrainageIncisionSnapshot[16];
        Parallel.For(0, results.Length, i => results[i] = DrainageReliefCoupling.Step(fixture.Topology, fixture.Flow,
            fixture.Positions, 100, 0, settings));
        foreach (var result in results)
        {
            CollectionAssert.AreEqual(expected.Samples.ToArray(), result.Samples.ToArray());
            Assert.AreEqual(expected.ExportedSedimentModelVolume, result.ExportedSedimentModelVolume);
        }
    }

    [TestMethod]
    public void MismatchedMetricAndNonFiniteSettingsAreRejected()
    {
        var fixture = Fixture(false, .1);
        Assert.ThrowsExactly<ArgumentException>(() => DrainageReliefCoupling.Step(fixture.Topology, fixture.Flow,
            new Dictionary<long, WorldBlockPosition>(), 100, 0, new(.1, 10, 100)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DrainageIncisionSettings(double.NaN, 10, 100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DrainageReliefCoupling.Step(fixture.Topology, fixture.Flow,
            fixture.Positions, double.NaN, 0, new(.1, 10, 100)));
    }

    private static (DrainageTopology Topology, DischargeSnapshot Flow, Dictionary<long, WorldBlockPosition> Positions) Fixture(bool lake, double runoff)
    {
        DrainageCell[] cells = [new(0, 0, [1], DrainageTerminalKind.Ocean, true),
            new(1, lake ? 10 : 5, [0, 2]), new(2, lake ? 2 : 10, [1])];
        var points = cells.ToDictionary(cell => cell.Id, cell => new WorldBlockPosition(cell.Id * 10, 0));
        DrainageTopology topology = MetricDrainageBuilder.Build(cells, points, 0);
        var flow = DischargeAccumulator.Accumulate(new Budget(cells.Select(cell => new WaterBudgetCell(cell.Id,
            new StableId(0, (ulong)cell.Id), runoff, 0, runoff, 0, 0, 0, 100))), topology, [], new(1, 2, 2));
        return (topology, flow, points);
    }
    private sealed class Budget(IEnumerable<WaterBudgetCell> cells) : IWaterBudgetSnapshotSource
    {
        public int AlgorithmVersion => WaterBudgetSnapshot.AlgorithmVersion;
        public int Iteration => 0;
        public IReadOnlyList<WaterBudgetCell> Cells { get; } = cells.ToArray();
        public IReadOnlyList<GroundwaterTransfer> Transfers { get; } = [];
    }
}
