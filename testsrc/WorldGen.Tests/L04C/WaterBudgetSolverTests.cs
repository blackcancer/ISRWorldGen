using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Tests.L04C;

[TestClass]
public sealed class WaterBudgetSolverTests
{
    private static readonly LatitudeAxis Axis = new(new WorldBlockPosition(0, 0), 0d, 1d, 100d);
    private static readonly TemperatureSettings TemperatureSettings = new(20d, 0.2d, 0.005d, -2d);
    private static readonly WaterBudgetSettings Settings = new(0.5d, 0.25d, 10d);

    [TestMethod]
    public void T04_05_AnalyticalBalanceCountsRechargeAndResurgenceExactlyOnce()
    {
        WaterBudgetSnapshot snapshot = Solve([Input(1, 1, 1d), Input(2, 2, 1d)], [new GroundwaterTransfer(Id(99), Id(1), Id(2), 2.5d, GroundwaterTransferKind.Resurgence)]);
        WaterBudgetCell source = snapshot.Cells.Single(cell => cell.CellId == 1);
        WaterBudgetCell destination = snapshot.Cells.Single(cell => cell.CellId == 2);
        Assert.AreEqual(10d, source.PrecipitationModelLengthPerYear, 1e-12);
        Assert.AreEqual(4d, source.ActualEvapotranspirationModelLengthPerYear, 1e-12);
        Assert.AreEqual(1.5d, source.StorageChangeModelLengthPerYear, 1e-12);
        Assert.AreEqual(4.5d, source.RechargeModelLengthPerYear, 1e-12);
        Assert.AreEqual(0d, source.RunoffModelLengthPerYear, 1e-12);
        Assert.IsLessThanOrEqualTo(1e-9d + (1e-6d * source.ReferenceFlowModelLengthPerYear), Math.Abs(source.ResidualModelLengthPerYear));
        Assert.AreEqual(10d, destination.PrecipitationModelLengthPerYear, 1e-12, "A resurgence arrives only as a transfer and never increases local rain.");
        Assert.HasCount(1, snapshot.Transfers);
        IWaterBudgetSnapshotSource handoff = snapshot;
        Assert.AreEqual(WaterBudgetSnapshot.AlgorithmVersion, handoff.AlgorithmVersion);
        CollectionAssert.AreEqual(snapshot.Cells.ToArray(), handoff.Cells.ToArray());
    }

    [TestMethod]
    public void T04_05_PermeabilityChangesRechargeNotPrecipitationAndResultsArePermutationStable()
    {
        WaterBudgetSnapshot forward = Solve([Input(1, 1, 0d), Input(2, 2, 1d)], []);
        WaterBudgetSnapshot reverse = Solve([Input(2, 2, 1d), Input(1, 1, 0d)], []);
        CollectionAssert.AreEqual(forward.Cells.ToArray(), reverse.Cells.ToArray());
        WaterBudgetCell impermeable = forward.Cells.Single(cell => cell.CellId == 1);
        WaterBudgetCell permeable = forward.Cells.Single(cell => cell.CellId == 2);
        Assert.AreEqual(impermeable.PrecipitationModelLengthPerYear, permeable.PrecipitationModelLengthPerYear, 1e-12);
        Assert.IsGreaterThan(impermeable.RechargeModelLengthPerYear, permeable.RechargeModelLengthPerYear);
        Assert.IsGreaterThan(permeable.RunoffModelLengthPerYear, impermeable.RunoffModelLengthPerYear);
        Assert.IsTrue(forward.Cells.All(cell => double.IsFinite(cell.SoilMoistureNormalized) && cell.SoilMoistureNormalized is >= 0d and <= 1d));
    }

    [TestMethod]
    public void T04_05_TransfersRejectDoubleCountingUnknownReservoirsAndInvalidFiniteDomain()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Solve([Input(1, 1, 1d), Input(2, 2, 1d)], [new GroundwaterTransfer(Id(1), Id(1), Id(2), 100d, GroundwaterTransferKind.Resurgence)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Solve([Input(1, 1, 1d)], [new GroundwaterTransfer(Id(2), Id(1), Id(99), 0d, GroundwaterTransferKind.Loss)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WaterBudgetSolver.Solve([Input(1, 1, 1d) with { PotentialEvapotranspirationModelLengthPerYear = double.NaN }], Precipitation(), Settings, [], 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WaterBudgetSettings(1d, 0d, double.PositiveInfinity));
    }

    [TestMethod]
    public void T04_05_ExtremeFiniteInputsRemainFiniteAndTransfersAreStableAcrossOrderAndBoundaries()
    {
        WaterBudgetInput[] inputs = [Input(long.MinValue, 1, 1d) with { AreaModelSquareLength = double.MaxValue / 16d }, Input(long.MaxValue, 2, 1d)];
        GroundwaterTransfer[] transfers = [new(Id(20), Id(1), Id(2), 1d, GroundwaterTransferKind.Loss), new(Id(10), Id(2), Id(1), 1d, GroundwaterTransferKind.Resurgence)];
        WaterBudgetSnapshot first = WaterBudgetSolver.Solve(inputs, ExtremePrecipitation(), Settings, transfers, 0);
        WaterBudgetSnapshot second = WaterBudgetSolver.Solve(inputs.Reverse(), ExtremePrecipitation(), Settings, transfers.Reverse(), 0);
        CollectionAssert.AreEqual(first.Cells.ToArray(), second.Cells.ToArray());
        CollectionAssert.AreEqual(first.Transfers.ToArray(), second.Transfers.ToArray());
        Assert.IsTrue(first.Cells.All(cell => double.IsFinite(cell.RechargeModelVolumePerYear)));
    }

    private static WaterBudgetSnapshot Solve(IEnumerable<WaterBudgetInput> inputs, IEnumerable<GroundwaterTransfer> transfers) => WaterBudgetSolver.Solve(inputs, Precipitation(), Settings, transfers, 0);
    private static IPrecipitationFieldSource Precipitation() => new TestPrecipitation([new(1, 0d, 10d), new(2, 0d, 10d)]);
    private static IPrecipitationFieldSource ExtremePrecipitation() => new TestPrecipitation([new(long.MinValue, 0d, 10d), new(long.MaxValue, 0d, 10d)]);
    private static WaterBudgetInput Input(long cellId, ulong reservoir, double permeability) => new(cellId, Id(reservoir), 4d, 0.25d, 8d, new MaterialProperties(0d, 0d, permeability), TemperatureField.Evaluate(Axis, TemperatureSettings, new TemperatureInput(new WorldBlockPosition(0, 0), 0d, 0d)));
    private static StableId Id(ulong low) => new(0, low);

    private sealed class TestPrecipitation(IEnumerable<PrecipitationField> fields) : IPrecipitationFieldSource
    {
        public int AlgorithmVersion => PrecipitationSnapshot.AlgorithmVersion;
        public IReadOnlyList<PrecipitationField> Fields { get; } = fields.OrderBy(field => field.CellId).ToArray();
        public bool TryGetField(long cellId, out PrecipitationField field) { field = Fields.SingleOrDefault(value => value.CellId == cellId); return Fields.Any(value => value.CellId == cellId); }
    }
}
