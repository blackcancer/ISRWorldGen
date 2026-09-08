using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Tests.L04C;

[TestClass]
public sealed class WaterBudgetCouplingTests
{
    [TestMethod]
    public void T04_06_ConvergesWithinBoundAndPublishesOnlyMatchingIterationSnapshot()
    {
        int calls = 0;
        WaterBudgetCouplingResult result = WaterBudgetCouplingSolver.Run(new WaterBudgetCouplingSettings(5, 0.1d, false), iteration =>
        {
            calls++;
            return new WaterBudgetCouplingStep(Snapshot(iteration), iteration == 2 ? 0.1d : 1d);
        });
        Assert.AreEqual(WaterBudgetCouplingStatus.Converged, result.Status);
        Assert.AreEqual(3, result.IterationsExecuted);
        Assert.AreEqual(3, calls);
        Assert.IsTrue(result.IsPublished);
        Assert.AreEqual(2, result.PublishedSnapshot!.Iteration);
    }

    [TestMethod]
    public void T04_06_NonConvergenceTerminatesAtFixedBoundAndRefusesPublicationUnlessExplicitlyDegraded()
    {
        int calls = 0;
        WaterBudgetCouplingResult refused = WaterBudgetCouplingSolver.Run(new WaterBudgetCouplingSettings(3, 0d, false), iteration => { calls++; return new WaterBudgetCouplingStep(Snapshot(iteration), 1d); });
        Assert.AreEqual(WaterBudgetCouplingStatus.NonConverged, refused.Status);
        Assert.AreEqual(3, calls);
        Assert.IsFalse(refused.IsPublished);

        WaterBudgetCouplingResult degraded = WaterBudgetCouplingSolver.Run(new WaterBudgetCouplingSettings(2, 0d, true), iteration => new WaterBudgetCouplingStep(Snapshot(iteration), 1d));
        Assert.AreEqual(WaterBudgetCouplingStatus.DegradedPublished, degraded.Status);
        Assert.IsTrue(degraded.IsPublished);
        Assert.AreEqual(1, degraded.PublishedSnapshot!.Iteration);
    }

    [TestMethod]
    public void T04_06_RejectsNonFiniteDiagnosticsAndMismatchedSnapshotsBeforePublication()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WaterBudgetCouplingSolver.Run(new WaterBudgetCouplingSettings(1, 0d, false), iteration => new WaterBudgetCouplingStep(Snapshot(iteration + 1), 0d)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WaterBudgetCouplingSolver.Run(new WaterBudgetCouplingSettings(1, 0d, false), iteration => new WaterBudgetCouplingStep(Snapshot(iteration), double.NaN)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WaterBudgetCouplingSettings(0, 0d, false));
    }

    private static WaterBudgetSnapshot Snapshot(int iteration) => WaterBudgetSolver.Solve(
        [new WaterBudgetInput(1, new StableId(0, 1), 1d, 0d, 0d, new MaterialProperties(0d, 0d, 0d),
            TemperatureField.Evaluate(new LatitudeAxis(new WorldBlockPosition(0, 0), 0d, 1d, 100d), new TemperatureSettings(0d, 0d, 0d, 0d), new TemperatureInput(new WorldBlockPosition(0, 0), 0d, 0d)))],
        new EmptyPrecipitation(), new WaterBudgetSettings(0d, 0d, 1d), [], iteration);

    private sealed class EmptyPrecipitation : ISRWorldGen.Core.Climate.Precipitation.IPrecipitationFieldSource
    {
        public int AlgorithmVersion => ISRWorldGen.Core.Climate.Precipitation.PrecipitationSnapshot.AlgorithmVersion;
        public IReadOnlyList<PrecipitationField> Fields => [new PrecipitationField(1, 0d, 0d)];
        public bool TryGetField(long cellId, out PrecipitationField field) { field = new PrecipitationField(1, 0d, 0d); return cellId == 1; }
    }
}
