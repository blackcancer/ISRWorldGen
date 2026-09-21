using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L04B;

[TestClass]
public sealed class DistancePrecipitationTests
{
    private static readonly DistancePrecipitationSettings Settings = new(1d, 8_000d, 200d, 15d);

    [TestMethod]
    public void RefiningAFlatTransectPreservesRainfallVolumesAndExport()
    {
        DistancePrecipitationSnapshot coarse = Solve(Transect(16, 1_000, 4), 1_000);
        DistancePrecipitationSnapshot fine = Solve(Transect(32, 500, 8), 500);
        for (int i = 0; i < 16; i++)
            Assert.AreEqual(coarse.Fields[i].PrecipitationModelLengthPerYear,
                (fine.Fields[i * 2].PrecipitationModelLengthPerYear + fine.Fields[i * 2 + 1].PrecipitationModelLengthPerYear) * .5d, 1e-12);
        Assert.AreEqual(coarse.Balance.ExportedVolumePerYear, fine.Balance.ExportedVolumePerYear, 1e-6);
        Assert.AreEqual(coarse.Balance.PrecipitationVolumePerYear, fine.Balance.PrecipitationVolumePerYear, 1e-6);
    }

    [TestMethod]
    public void DryWorldHasNoInventedMoistureOrRunoffSource()
    {
        DistancePrecipitationSnapshot result = Solve(Transect(12, 1_000, 0), 1_000);
        Assert.IsTrue(result.Fields.All(field => field.PrecipitationModelLengthPerYear == 0d && field.AtmosphericMoistureModelLengthPerYear == 0d));
        Assert.AreEqual(0d, result.Balance.OceanEvaporationVolumePerYear);
        Assert.AreEqual(0d, result.Balance.ExportedVolumePerYear);
    }

    [TestMethod]
    public void OceanTemperatureChangesEvaporationAndRainfall()
    {
        DistancePrecipitationSnapshot cold = Solve(Transect(12, 1_000, 4, 0), 1_000);
        DistancePrecipitationSnapshot warm = Solve(Transect(12, 1_000, 4, 25), 1_000);
        Assert.IsGreaterThan(cold.Balance.OceanEvaporationVolumePerYear, warm.Balance.OceanEvaporationVolumePerYear);
        Assert.IsGreaterThan(cold.Fields[5].PrecipitationModelLengthPerYear, warm.Fields[5].PrecipitationModelLengthPerYear);
    }

    [TestMethod]
    public void MountainsCondenseOnWindwardSlopeWithoutCreatingWater()
    {
        PrecipitationCell[] cells = Transect(12, 1_000, 4);
        cells[6] = Cell(6, 1_000, false, 20, 250);
        cells[7] = Cell(7, 1_000, false, 20, 0);
        DistancePrecipitationSnapshot result = Solve(cells, 1_000);
        Assert.IsGreaterThan(result.Fields[7].PrecipitationModelLengthPerYear, result.Fields[6].PrecipitationModelLengthPerYear);
        Assert.IsLessThanOrEqualTo(1e-6, Math.Abs(result.Balance.ResidualVolumePerYear));
    }

    [TestMethod]
    public void MirroringWindAndInputsMirrorsRainfall()
    {
        PrecipitationCell[] original = Transect(16, 1_000, 4);
        PrecipitationCell[] mirrored = original.Select((cell, i) => Cell(15 - i, 1_000, cell.IsOcean, 20, cell.ElevationModelLength)).ToArray();
        DistancePrecipitationSnapshot east = Solve(original, 1_000);
        DistancePrecipitationSnapshot west = DistancePrecipitationSolver.Solve(mirrored, 1_000, 1_000, [new(-1, 0, 1)], Settings);
        for (int i = 0; i < 16; i++) Assert.AreEqual(east.Fields[i].PrecipitationModelLengthPerYear, west.Fields[15 - i].PrecipitationModelLengthPerYear, 1e-12);
    }

    [TestMethod]
    public void AnnualWindWeightsAverageInsteadOfDuplicatingSources()
    {
        PrecipitationCell[] cells = Transect(16, 1_000, 4);
        var east = Solve(cells, 1_000);
        var west = DistancePrecipitationSolver.Solve(cells, 1_000, 1_000, [new(-1, 0, 1)], Settings);
        var mix = DistancePrecipitationSolver.Solve(cells.Reverse(), 1_000, 1_000, [new(1, 0, .75), new(-1, 0, .25)], Settings);
        for (int i = 0; i < cells.Length; i++) Assert.AreEqual(.75 * east.Fields[i].PrecipitationModelLengthPerYear + .25 * west.Fields[i].PrecipitationModelLengthPerYear,
            mix.Fields[i].PrecipitationModelLengthPerYear, 1e-12);
        Assert.AreEqual(east.Balance.OceanEvaporationVolumePerYear, mix.Balance.OceanEvaporationVolumePerYear, 1e-6);
        var repeated = DistancePrecipitationSolver.Solve(cells, 1_000, 1_000, [new(-1, 0, .25), new(1, 0, .75)], Settings);
        CollectionAssert.AreEqual(mix.Fields.ToArray(), repeated.Fields.ToArray());
    }

    [TestMethod]
    public void LongTransportScaleUsesStableSmallOpticalDepth()
    {
        var result = DistancePrecipitationSolver.Solve(Transect(8, 1, 4), 1, 1_000, [new(1, 0, 1)], new(1, 1e12, 200, 15));
        Assert.IsTrue(result.Fields.All(field => field.PrecipitationModelLengthPerYear >= 0d));
        Assert.IsLessThanOrEqualTo(1e-6, Math.Abs(result.Balance.ResidualVolumePerYear));
    }

    [TestMethod]
    public void RejectsHolesWrongMetricsAndInvalidWindWeights()
    {
        var cells = Transect(8, 1_000, 4);
        Assert.ThrowsExactly<ArgumentException>(() => Solve(cells.Where(cell => cell.Id != 4), 1_000));
        Assert.ThrowsExactly<ArgumentException>(() => Solve(cells, 500));
        Assert.ThrowsExactly<ArgumentException>(() => DistancePrecipitationSolver.Solve(cells, 1_000, 1_000, [new(1, 1, 1)], Settings));
        Assert.ThrowsExactly<ArgumentException>(() => DistancePrecipitationSolver.Solve(cells, 1_000, 1_000, [new(1, 0, .8)], Settings));
        Assert.ThrowsExactly<ArgumentException>(() => DistancePrecipitationSolver.Solve(cells, 1_000, 1_000, [new(1, 0, .5), new(1, 0, .5)], Settings));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DistancePrecipitationSettings(1, double.NaN, 1, 1));
    }

    private static DistancePrecipitationSnapshot Solve(IEnumerable<PrecipitationCell> cells, long step) =>
        DistancePrecipitationSolver.Solve(cells, step, 1_000, [new(1, 0, 1)], Settings);
    private static PrecipitationCell[] Transect(int count, long step, int oceanCells, double temperature = 20) =>
        Enumerable.Range(0, count).Select(i => Cell(i, step, i < oceanCells, temperature, 0)).ToArray();
    private static PrecipitationCell Cell(int index, long step, bool ocean, double temperature, double altitude)
    {
        var position = new WorldBlockPosition(index * step, 0);
        var evaluated = TemperatureField.Evaluate(new LatitudeAxis(new(0, 0), 0, 1, 1_000),
            new TemperatureSettings(temperature, 0, 0, 0), new TemperatureInput(position, altitude, 0));
        return new PrecipitationCell(index, index, 0, position, altitude, ocean, evaluated);
    }
}
