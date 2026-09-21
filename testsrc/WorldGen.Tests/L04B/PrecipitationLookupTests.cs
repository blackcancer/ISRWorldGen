using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L04B;

[TestClass]
public sealed class PrecipitationLookupTests
{
    [TestMethod]
    public void LookupPreservesFullPayloadAtInt64ExtremesAndOnMiss()
    {
        long[] ids = [long.MaxValue, 0, long.MinValue, -7, 19];
        PrecipitationSnapshot snapshot = Build(ids);
        foreach (PrecipitationField expected in snapshot.Fields)
        {
            Assert.IsTrue(snapshot.TryGetField(expected.CellId, out PrecipitationField actual));
            Assert.AreEqual(expected, actual);
        }
        foreach (long absent in new[] { long.MinValue + 1, -8, -1, 1, 18, 20, long.MaxValue - 1 })
        {
            Assert.IsFalse(snapshot.TryGetField(absent, out PrecipitationField value));
            Assert.AreEqual(default(PrecipitationField), value);
        }
        CollectionAssert.AreEqual(ids.Order().ToArray(), snapshot.Fields.Select(field => field.CellId).ToArray());
    }

    [TestMethod]
    public void SingleZeroIdFieldIsFoundButMissingIdsReturnDefault()
    {
        PrecipitationSnapshot snapshot = Build([0]);
        Assert.IsTrue(snapshot.TryGetField(0, out PrecipitationField field));
        Assert.AreEqual(snapshot.Fields[0], field);
        Assert.IsFalse(snapshot.TryGetField(-1, out PrecipitationField missing));
        Assert.AreEqual(default(PrecipitationField), missing);
        Assert.IsFalse(snapshot.TryGetField(1, out _));
    }

    [TestMethod]
    public void LargeSnapshotSupportsParallelReadsWithoutChangingPublishedFields()
    {
        long[] ids = Enumerable.Range(0, 8_192).Select(index => (long)index * 2 - 8_192).ToArray();
        PrecipitationSnapshot snapshot = Build(ids);
        PrecipitationField[] before = snapshot.Fields.ToArray();
        var observed = new PrecipitationField[ids.Length];
        var missing = new bool[ids.Length];
        Parallel.For(0, ids.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, index =>
        {
            if (!snapshot.TryGetField(ids[index], out observed[index]))
                throw new InvalidOperationException("Published field disappeared during a concurrent read.");
            missing[index] = !snapshot.TryGetField(ids[index] + 1, out _);
        });
        CollectionAssert.AreEqual(before, observed);
        Assert.IsTrue(missing.All(value => value));
        CollectionAssert.AreEqual(before, snapshot.Fields.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<PrecipitationField>)snapshot.Fields)[0] = default);
        Assert.IsTrue(snapshot.TryGetField(ids[0], out PrecipitationField first));
        Assert.AreEqual(before[0], first);
    }

    [TestMethod]
    public void ManyWindwardPortsRetainExactSupplyAndPermutationInvariantResults()
    {
        const int count = 1_024;
        PrecipitationCell[] cells = Enumerable.Range(0, count)
            .Select(index => Cell(index * 3L, 0, index, true)).ToArray();
        long[] ports = cells.Select(cell => cell.Id).ToArray();
        var settings = new PrecipitationSettings(4d, .5d, 0d);
        PrecipitationSnapshot first = PrecipitationSolver.Solve(cells, new WindVector(1, 0),
            MoistureBoundaryCondition.OpenGlobalOcean, 12d, ports, settings);
        PrecipitationSnapshot second = PrecipitationSolver.Solve(cells.Reverse(), new WindVector(1, 0),
            MoistureBoundaryCondition.OpenGlobalOcean, 12d, ports.Reverse(), settings);
        CollectionAssert.AreEqual(first.Fields.ToArray(), second.Fields.ToArray());
        foreach (PrecipitationField field in first.Fields)
        {
            Assert.AreEqual(8d, field.PrecipitationModelLengthPerYear);
            Assert.AreEqual(8d, field.AtmosphericMoistureModelLengthPerYear);
        }
    }

    [TestMethod]
    public void IndexedPortsStillRejectUnknownDuplicateLandAndDownwindEntries()
    {
        PrecipitationCell[] cells = [Cell(10, 0, 0, true), Cell(20, 1, 0, true), Cell(30, 0, 1, false)];
        foreach (long[] invalid in new long[][] { [99], [10, 10], [30], [20] })
        {
            Assert.ThrowsExactly<ArgumentException>(() => PrecipitationSolver.Solve(cells, new WindVector(1, 0),
                MoistureBoundaryCondition.OpenGlobalOcean, 1d, invalid, new PrecipitationSettings(1d, .5d, 0d)));
        }
    }

    private static PrecipitationSnapshot Build(long[] ids)
    {
        PrecipitationCell[] cells = ids.Select((id, index) => Cell(id, index % 127, index / 127, index % 3 == 0)).ToArray();
        return PrecipitationSolver.Solve(cells.Reverse(), new WindVector(1, 0),
            MoistureBoundaryCondition.Closed, 0d, [], new PrecipitationSettings(4d, .25d, 0d));
    }

    private static PrecipitationCell Cell(long id, int x, int z, bool ocean)
    {
        var position = new WorldBlockPosition(x, z);
        TemperatureEvaluation temperature = TemperatureField.Evaluate(
            new LatitudeAxis(new WorldBlockPosition(0, 0), 0d, 1d, 1_000d),
            new TemperatureSettings(20d, .25d, .005d, -2d),
            new TemperatureInput(position, 0d, 0d));
        return new PrecipitationCell(id, x, z, position, 0d, ocean, temperature);
    }
}
