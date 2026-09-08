using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Hydrology.Depressions;
using ISRWorldGen.Core.Hydrology.Discharge;

namespace ISRWorldGen.Tests.L05B;

[TestClass]
public sealed class DischargeAccumulatorTests
{
    private static readonly DischargeClassificationSettings Classification = new(3d, 8d, 20d);

    [TestMethod]
    public void T05_03_AnalyticalYConservesEveryJunctionAndTheGlobalBasin()
    {
        DischargeSnapshot result = DischargeAccumulator.Accumulate(
            Budget(iteration: 27), Topology(), [new DischargeAdjustment(3, 2d, 1d)], Classification);

        DischargeReach left = result.Reaches.Single(reach => reach.CellId == 1);
        DischargeReach right = result.Reaches.Single(reach => reach.CellId == 2);
        DischargeReach junction = result.Reaches.Single(reach => reach.CellId == 3);
        Assert.AreEqual(5d, left.DischargeModelVolumePerYear, 1e-12, "Source recharge is reduced by its transfer exactly once.");
        Assert.AreEqual(6d, right.DischargeModelVolumePerYear, 1e-12, "The resurgence arrives once at its destination.");
        Assert.AreEqual(11d, junction.UpstreamDischargeModelVolumePerYear, 1e-12);
        Assert.AreEqual(9d, junction.DischargeModelVolumePerYear, 1e-12);
        Assert.AreEqual(40d, junction.DrainageAreaModelSquareLength, 1e-12);
        Assert.AreEqual(DischargeReachClass.River, junction.Classification, "Classification depends on conserved flow and area, not Strahler order.");
        Assert.AreEqual(27, result.Iteration, "The published discharge hand-off must retain the exact C02 preparatory cycle identity.");
        Assert.IsLessThanOrEqualTo(Tolerance(junction.DischargeModelVolumePerYear), Math.Abs(junction.ResidualModelVolumePerYear));
        Assert.AreEqual(12d, result.Balance.LocalWaterModelVolumePerYear, 1e-12);
        Assert.AreEqual(9d, result.Balance.TerminalDischargeModelVolumePerYear, 1e-12);
        Assert.AreEqual(2d, result.Balance.LossModelVolumePerYear, 1e-12);
        Assert.AreEqual(1d, result.Balance.StorageChangeModelVolumePerYear, 1e-12);
        Assert.IsLessThanOrEqualTo(Tolerance(result.Balance.ReferenceFlowModelVolumePerYear), Math.Abs(result.Balance.ResidualModelVolumePerYear));
        Assert.AreEqual(DrainageTerminalKind.Ocean, result.Terminals.Single(terminal => terminal.CellId == 3).Kind);
    }

    [TestMethod]
    public void T05_03_PermutationsAndDryReachDoNotChangeTheImmutableSnapshot()
    {
        TestBudget source = Budget(iteration: 41);
        var reversed = new TestBudget(source.Cells.Reverse(), source.Transfers.Reverse(), source.Iteration);
        DrainageTopology topology = Topology();
        DischargeSnapshot first = DischargeAccumulator.Accumulate(source, topology, [new DischargeAdjustment(3, 2d, 1d), new DischargeAdjustment(4, 0d, 0d)], Classification);
        DischargeSnapshot second = DischargeAccumulator.Accumulate(reversed, topology, [new DischargeAdjustment(4, 0d, 0d), new DischargeAdjustment(3, 2d, 1d)], Classification);

        CollectionAssert.AreEqual(first.Reaches.ToArray(), second.Reaches.ToArray());
        CollectionAssert.AreEqual(first.Terminals.ToArray(), second.Terminals.ToArray());
        Assert.AreEqual(first.Balance, second.Balance);
        Assert.AreEqual(41, first.Iteration);
        Assert.AreEqual(first.Iteration, second.Iteration);
        Assert.IsTrue(first.Reaches.All(reach => double.IsFinite(reach.DischargeModelVolumePerYear) && double.IsFinite(reach.DrainageAreaModelSquareLength)));
    }

    [TestMethod]
    public void T05_03_RejectsUnexplainedNegativeOutflowAndTransferDoubleCounting()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(Budget(), Topology(), [new DischargeAdjustment(1, 6d, 0d)], Classification));
        TestBudget duplicated = Budget(new GroundwaterTransfer(Id(99), Id(1), Id(2), 4.00001d, GroundwaterTransferKind.Resurgence));
        Assert.ThrowsExactly<ArgumentException>(() => DischargeAccumulator.Accumulate(duplicated, Topology(), [], Classification));
    }

    [TestMethod]
    public void T05_03_RejectsNonFiniteBudgetTransferLossAndStorageTermsExplicitly()
    {
        WaterBudgetCell[] cells = Budget().Cells.ToArray();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(new TestBudget([cells[0] with { PrecipitationModelLengthPerYear = double.NaN }, .. cells[1..]], []), Topology(), [], Classification));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(Budget(new GroundwaterTransfer(Id(99), Id(1), Id(2), double.PositiveInfinity, GroundwaterTransferKind.Resurgence)), Topology(), [], Classification));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(Budget(), Topology(), [new DischargeAdjustment(1, double.NaN, 0d)], Classification));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(Budget(), Topology(), [new DischargeAdjustment(1, 0d, double.NegativeInfinity)], Classification));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(Budget(iteration: -1), Topology(), [], Classification));
    }

    [TestMethod]
    public void T05_03_RejectsExtremeVolumeOverflowStablyAcrossPermutations()
    {
        WaterBudgetCell[] cells = Budget().Cells.ToArray();
        WaterBudgetCell extreme = cells[0] with { AreaModelSquareLength = double.MaxValue, RunoffModelLengthPerYear = 2d, RechargeModelLengthPerYear = 0d };
        var forward = new TestBudget([extreme, .. cells[1..]], []);
        var reverse = new TestBudget(cells[1..].Reverse().Append(extreme), []);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(forward, Topology(), [], Classification));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DischargeAccumulator.Accumulate(reverse, Topology(), [], Classification));
    }

    private static TestBudget Budget(GroundwaterTransfer? replacement = null, int iteration = 0) => new(
    [
        Cell(1, 2d, 4d, 10d, Id(1)), Cell(2, 3d, 2d, 10d, Id(2)), Cell(3, 1d, 0d, 20d, Id(3)), Cell(4, 0d, 0d, 5d, Id(4)),
    ],
    [replacement ?? new GroundwaterTransfer(Id(99), Id(1), Id(2), 1d, GroundwaterTransferKind.Resurgence)], iteration);

    private static DrainageTopology Topology() => DepressionTopologyBuilder.Build(
    [
        new DrainageCell(1, 4d, [3]), new DrainageCell(2, 3d, [3]), new DrainageCell(3, 0d, [1, 2], DrainageTerminalKind.Ocean, true), new DrainageCell(4, 0d, [], DrainageTerminalKind.DryBasin),
    ]);

    private static WaterBudgetCell Cell(long id, double runoff, double recharge, double area, StableId reservoir) =>
        new(id, reservoir, 0d, 0d, runoff / area, recharge / area, 0d, 0d, area);
    private static StableId Id(ulong low) => new(0, low);
    private static double Tolerance(double reference) => DischargeAccumulator.AbsoluteResidualToleranceModelVolumePerYear + (DischargeAccumulator.RelativeResidualTolerance * reference);

    private sealed class TestBudget(IEnumerable<WaterBudgetCell> cells, IEnumerable<GroundwaterTransfer> transfers, int iteration = 0) : IWaterBudgetSnapshotSource
    {
        public int AlgorithmVersion => WaterBudgetSnapshot.AlgorithmVersion;
        public int Iteration => iteration;
        public IReadOnlyList<WaterBudgetCell> Cells { get; } = cells.ToArray();
        public IReadOnlyList<GroundwaterTransfer> Transfers { get; } = transfers.ToArray();
    }
}
