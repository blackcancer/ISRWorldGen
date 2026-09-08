using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Tests.L04B;

[TestClass]
public sealed class PrecipitationSolverTests
{
    private static readonly TemperatureSettings TemperatureSettings = new(20d, 0.2d, 0.005d, -2d);
    private static readonly LatitudeAxis Axis = new(new WorldBlockPosition(0, 0), 0d, 1d, 100d);
    private static readonly PrecipitationSettings Settings = new(10d, 0.05d, 0.0005d, 0.8d, 10d);

    [TestMethod]
    public void T04_03_WindwardBarrierIsWetAndMirroringWindAndBarrierMirrorsTheResult()
    {
        PrecipitationSnapshot eastward = PrecipitationSolver.Solve(Barrier(oceanAtWest: true), new WindVector(1, 0),
            MoistureBoundaryCondition.Closed, 0d, Settings);
        PrecipitationSnapshot westward = PrecipitationSolver.Solve(Barrier(oceanAtWest: false), new WindVector(-1, 0),
            MoistureBoundaryCondition.Closed, 0d, Settings);

        PrecipitationField windward = eastward.Fields.Single(field => field.CellId == 2);
        PrecipitationField leeward = eastward.Fields.Single(field => field.CellId == 3);
        Assert.IsGreaterThan(leeward.PrecipitationModelLengthPerYear, windward.PrecipitationModelLengthPerYear);
        Assert.AreEqual(windward.PrecipitationModelLengthPerYear, westward.Fields.Single(field => field.CellId == 2).PrecipitationModelLengthPerYear, 1e-12);
        Assert.AreEqual(leeward.PrecipitationModelLengthPerYear, westward.Fields.Single(field => field.CellId == 1).PrecipitationModelLengthPerYear, 1e-12);
    }

    [TestMethod]
    public void T04_03_ClosedTileBoundariesDoNotRechargeAndOrderIsDeterministic()
    {
        PrecipitationCell[] dry = [Cell(1, 0, 0, 0d, false, 0.5d), Cell(2, 1, 0, 100d, false, 0.5d)];
        PrecipitationSnapshot first = PrecipitationSolver.Solve(dry, new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, Settings);
        PrecipitationSnapshot reverse = PrecipitationSolver.Solve(dry.Reverse(), new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, Settings);
        CollectionAssert.AreEqual(first.Fields.ToArray(), reverse.Fields.ToArray());
        Assert.IsTrue(first.Fields.All(field => field.AtmosphericMoistureModelLengthPerYear == 0d && field.PrecipitationModelLengthPerYear == 0d));
        Assert.ThrowsExactly<ArgumentException>(() => PrecipitationSolver.Solve(dry, new WindVector(1, 0), MoistureBoundaryCondition.Closed, 1d, Settings));
    }

    [TestMethod]
    public void T04_04_SeparateFieldsRespondToPermeabilityWithoutIdentityOrOutOfBoundsValues()
    {
        PrecipitationCell[] cells = [Cell(1, 0, 0, 0d, true, 0d), Cell(2, 1, 0, 0d, false, 0d), Cell(3, 2, 0, 0d, false, 1d)];
        PrecipitationSnapshot snapshot = PrecipitationSolver.Solve(cells, new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, Settings);
        PrecipitationField impervious = snapshot.Fields.Single(field => field.CellId == 2);
        PrecipitationField permeable = snapshot.Fields.Single(field => field.CellId == 3);
        Assert.IsGreaterThan(impervious.GroundwaterRechargeModelLengthPerYear, permeable.GroundwaterRechargeModelLengthPerYear);
        Assert.IsGreaterThan(permeable.SurfaceRunoffModelLengthPerYear, impervious.SurfaceRunoffModelLengthPerYear);
        Assert.AreNotEqual(impervious.PrecipitationModelLengthPerYear, impervious.SoilMoistureNormalized);
        foreach (PrecipitationField field in snapshot.Fields)
        {
            Assert.IsTrue(double.IsFinite(field.PrecipitationModelLengthPerYear) && field.PrecipitationModelLengthPerYear >= 0d);
            Assert.IsTrue(field.SoilMoistureNormalized is >= 0d and <= 1d);
            Assert.AreEqual(field.PrecipitationModelLengthPerYear, field.SurfaceRunoffModelLengthPerYear + field.GroundwaterRechargeModelLengthPerYear, 1e-12);
        }
    }

    [TestMethod]
    public void InvalidInputsAndNonFiniteLimitsAreRejectedBeforePublication()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WindVector(2, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrecipitationSolver.Solve(Barrier(true), default, MoistureBoundaryCondition.Closed, 0d, Settings));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PrecipitationSettings(1d, 1.1d, 0d, 0d, 1d));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrecipitationSolver.Solve(Barrier(true), new WindVector(1, 0), MoistureBoundaryCondition.OpenGlobalOcean, double.NaN, Settings));
        Assert.ThrowsExactly<ArgumentException>(() => PrecipitationSolver.Solve([Cell(1, 0, 0, 0d, false, 0d), Cell(1, 1, 0, 0d, false, 0d)], new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, Settings));
        Assert.ThrowsExactly<ArgumentException>(() => PrecipitationSolver.Solve([Cell(1, 0, 0, 0d, false, 0d), Cell(2, 0, 0, 0d, false, 0d)], new WindVector(1, 0), MoistureBoundaryCondition.Closed, 0d, Settings));
    }

    private static PrecipitationCell[] Barrier(bool oceanAtWest)
    {
        double[] heights = [0d, 100d, 900d, 100d, 0d];
        return heights.Select((height, index) => Cell(index, index, 0, height, oceanAtWest ? index == 0 : index == 4, 0.5d)).ToArray();
    }

    private static PrecipitationCell Cell(long id, int x, int z, double elevation, bool ocean, double permeability)
    {
        TemperatureEvaluation temperature = TemperatureField.Evaluate(Axis, TemperatureSettings,
            new TemperatureInput(new WorldBlockPosition(x, z), elevation, 0d));
        return new PrecipitationCell(id, x, z, new WorldBlockPosition(x, z), elevation, ocean, temperature,
            new MaterialSample(GeologicalMaterialCode.Sandstone, default, new MaterialProperties(0.5d, 0.5d, permeability), false));
    }
}
