using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L04B;

[TestClass]
public sealed class PrecipitationSolverTests
{
    private static readonly TemperatureSettings TemperatureSettings = new(20d, 0.2d, 0.005d, -2d);
    private static readonly LatitudeAxis Axis = new(new WorldBlockPosition(0, 0), 0d, 1d, 100d);
    private static readonly PrecipitationSettings Settings = new(10d, 0.05d, 0.0005d);

    [TestMethod]
    public void T04_03_WindwardBarrierIsWetAndMirroringWindAndBarrierMirrorsTheResult()
    {
        PrecipitationSnapshot eastward = Solve(Barrier(true), new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, []);
        PrecipitationSnapshot westward = Solve(Barrier(false), new WindVector(-1, 0), MoistureBoundaryCondition.Closed, 0d, []);
        PrecipitationField windward = eastward.Fields.Single(field => field.CellId == 2);
        PrecipitationField leeward = eastward.Fields.Single(field => field.CellId == 3);
        Assert.IsGreaterThan(leeward.PrecipitationModelLengthPerYear, windward.PrecipitationModelLengthPerYear);
        Assert.AreEqual(windward.PrecipitationModelLengthPerYear, westward.Fields.Single(field => field.CellId == 2).PrecipitationModelLengthPerYear, 1e-12);
        Assert.AreEqual(leeward.PrecipitationModelLengthPerYear, westward.Fields.Single(field => field.CellId == 1).PrecipitationModelLengthPerYear, 1e-12);
    }

    [TestMethod]
    public void T04_03_OpenGlobalOceanInjectsOnlyExplicitStableWindwardPorts()
    {
        PrecipitationCell[] cells = [Cell(10, 0, 0, 0d, true), Cell(20, 1, 0, 0d, false), Cell(30, 3, 0, 0d, false)];
        PrecipitationSnapshot first = Solve(cells, new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, 4d, [10]);
        PrecipitationSnapshot reversed = Solve(cells.Reverse(), new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, 4d, [10]);

        CollectionAssert.AreEqual(first.Fields.ToArray(), reversed.Fields.ToArray());
        Assert.IsGreaterThan(0d, first.Fields.Single(field => field.CellId == 10).AtmosphericMoistureModelLengthPerYear);
        Assert.AreEqual(0d, first.Fields.Single(field => field.CellId == 30).AtmosphericMoistureModelLengthPerYear, 1e-12,
            "A missing upwind neighbour is a technical hole, never an implicit global port.");
        Assert.ThrowsExactly<ArgumentException>(() => Solve(cells, new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, 4d, [30]));
    }

    [TestMethod]
    public void T04_03_ClosedBoundariesAndNonPortsDoNotInjectHumidity()
    {
        PrecipitationCell[] dry = [Cell(1, 0, 0, 0d, false), Cell(2, 1, 0, 100d, false)];
        PrecipitationSnapshot first = Solve(dry, new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, []);
        PrecipitationSnapshot reverse = Solve(dry.Reverse(), new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, []);
        CollectionAssert.AreEqual(first.Fields.ToArray(), reverse.Fields.ToArray());
        Assert.IsTrue(first.Fields.All(field => field.AtmosphericMoistureModelLengthPerYear == 0d && field.PrecipitationModelLengthPerYear == 0d));
        Assert.ThrowsExactly<ArgumentException>(() => Solve(dry, new WindVector(1, 0), MoistureBoundaryCondition.Closed, 1d, []));
        Assert.ThrowsExactly<ArgumentException>(() => Solve(dry, new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, [1]));
    }

    [TestMethod]
    public void T04_04_HandoffIsImmutableAndContainsOnlyAtmosphericAndPrecipitationFields()
    {
        PrecipitationSnapshot snapshot = Solve(Barrier(true), new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, []);
        IPrecipitationFieldSource source = snapshot;
        Assert.AreEqual(PrecipitationSnapshot.AlgorithmVersion, source.AlgorithmVersion);
        Assert.IsTrue(source.TryGetField(2, out PrecipitationField field));
        Assert.IsFalse(source.TryGetField(99, out _));
        Assert.AreEqual(snapshot.Fields.Single(value => value.CellId == 2), field);
        Assert.IsTrue(snapshot.Fields.All(value => double.IsFinite(value.AtmosphericMoistureModelLengthPerYear) &&
            double.IsFinite(value.PrecipitationModelLengthPerYear) && value.AtmosphericMoistureModelLengthPerYear >= 0d && value.PrecipitationModelLengthPerYear >= 0d));
        Assert.HasCount(3, typeof(PrecipitationField).GetProperties(),
            "L04-C, not L04-B, owns ET, soil moisture, runoff and recharge fractions.");
    }

    [TestMethod]
    public void InvalidInputsAndNonFiniteLimitsAreRejectedBeforePublication()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WindVector(2, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Solve(Barrier(true), default, MoistureBoundaryCondition.Closed, 0d, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PrecipitationSettings(1d, 1.1d, 0d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Solve(Barrier(true), new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, double.NaN, [0]));
        Assert.ThrowsExactly<ArgumentException>(() => Solve(Barrier(true), new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, 1d, []));
        Assert.ThrowsExactly<ArgumentException>(() => Solve(Barrier(true), new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, 1d, [0, 0]));
        Assert.ThrowsExactly<ArgumentException>(() => Solve([Cell(1, 0, 0, 0d, false), Cell(1, 1, 0, 0d, false)], new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, []));
        PrecipitationSnapshot extreme = Solve([Cell(10, int.MinValue, 0, 0d, true), Cell(11, int.MinValue + 1, 0, 0d, false)],
            new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, 1d, [10]);
        Assert.IsTrue(extreme.Fields.All(field => double.IsFinite(field.AtmosphericMoistureModelLengthPerYear)));
    }

    private static PrecipitationSnapshot Solve(IEnumerable<PrecipitationCell> cells, WindVector wind, MoistureBoundaryCondition boundary, double humidity, IEnumerable<long> ports)
        => PrecipitationSolver.Solve(cells, wind, boundary, humidity, ports, Settings);

    private static PrecipitationCell[] Barrier(bool oceanAtWest)
    {
        double[] heights = [0d, 100d, 900d, 100d, 0d];
        return heights.Select((height, index) => Cell(index, index, 0, height, oceanAtWest ? index == 0 : index == 4)).ToArray();
    }

    private static PrecipitationCell Cell(long id, int x, int z, double elevation, bool ocean)
    {
        TemperatureEvaluation temperature = TemperatureField.Evaluate(Axis, TemperatureSettings,
            new TemperatureInput(new WorldBlockPosition(x, z), elevation, 0d));
        return new PrecipitationCell(id, x, z, new WorldBlockPosition(x, z), elevation, ocean, temperature);
    }
}
