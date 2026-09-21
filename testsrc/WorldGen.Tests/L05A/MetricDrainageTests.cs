using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Hydrology.Depressions;

namespace ISRWorldGen.Tests.L05A;

[TestClass]
public sealed class MetricDrainageTests
{
    [TestMethod]
    public void StrictDescentUsesSlopeNotTheFirstFloodParent()
    {
        DrainageCell[] cells = [new(10, 10, [20, 30]),
            new(20, 0, [10], DrainageTerminalKind.OutOfDomain),
            new(30, 8, [10], DrainageTerminalKind.OutOfDomain)];
        var points = new Dictionary<long, WorldBlockPosition> { [10] = new(0, 0), [20] = new(100, 0), [30] = new(1, 0) };
        Assert.AreEqual(20L, DepressionTopologyBuilder.Build(cells).Cells[0].ReceiverId,
            "Pinned counterexample: flood discovery ignores edge length.");
        Assert.AreEqual(30L, MetricDrainageBuilder.Build(cells, points).Cells[0].ReceiverId);
    }

    [TestMethod]
    public void DiagonalDistanceDoesNotStealTheCardinalSteepestDescent()
    {
        DrainageCell[] cells = [new(0, 10, [1, 2]), new(1, 9.2, [0], DrainageTerminalKind.OutOfDomain),
            new(2, 9.1, [0], DrainageTerminalKind.OutOfDomain)];
        var points = new Dictionary<long, WorldBlockPosition> { [0] = new(0, 0), [1] = new(1, 0), [2] = new(1, 1) };
        Assert.AreEqual(1L, MetricDrainageBuilder.Build(cells, points).Cells[0].ReceiverId);
    }

    [TestMethod]
    public void FlatUsesShortestExitDistanceWithoutAnyHeightEpsilon()
    {
        DrainageCell[] cells = [new(0, 10, [1, 2]), new(1, 10, [0], DrainageTerminalKind.OutOfDomain),
            new(2, 10, [0], DrainageTerminalKind.OutOfDomain)];
        var points = new Dictionary<long, WorldBlockPosition> { [0] = new(0, 0), [1] = new(-10, 0), [2] = new(2, 0) };
        DrainageTopology result = MetricDrainageBuilder.Build(cells, points);
        Assert.AreEqual(2L, result.Cells[0].ReceiverId);
        Assert.IsTrue(result.Cells.All(cell => cell.RoutingElevation == 10d && cell.PhysicalElevation == 10d));
    }

    [TestMethod]
    public void LargeCoordinateTranslationPreservesOneBlockDistances()
    {
        DrainageCell[] cells = [new(0, 2, [1, 2]), new(1, 0, [0], DrainageTerminalKind.OutOfDomain),
            new(2, 1, [0], DrainageTerminalKind.OutOfDomain)];
        var near = new Dictionary<long, WorldBlockPosition> { [0] = new(0, 0), [1] = new(4, 0), [2] = new(0, 1) };
        var far = near.ToDictionary(item => item.Key, item => new WorldBlockPosition(long.MaxValue - 20 + item.Value.X, long.MinValue + 20 + item.Value.Z));
        CollectionAssert.AreEqual(MetricDrainageBuilder.Build(cells, near).Cells.ToArray(),
            MetricDrainageBuilder.Build(cells.Reverse(), far).Cells.ToArray());
    }

    [TestMethod]
    public void ValleyRoutesToItsFloorAndThenToItsOutlet()
    {
        const int size = 11;
        var cells = new List<DrainageCell>(); var points = new Dictionary<long, WorldBlockPosition>();
        for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
        {
            int id = z * size + x;
            var neighbours = new List<long>();
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dz != 0) && x + dx >= 0 && x + dx < size && z + dz >= 0 && z + dz < size)
                    neighbours.Add((z + dz) * size + x + dx);
            cells.Add(new(id, 3 * Math.Abs(x - 5) + 10 - z, neighbours,
                x == 5 && z == 10 ? DrainageTerminalKind.OutOfDomain : null));
            points.Add(id, new(x * 10, z * 10));
        }
        DrainageTopology result = MetricDrainageBuilder.Build(cells, points);
        Assert.IsTrue(result.Connectivity.All(item => item.TerminalCellId == 115));
        foreach (RoutedCell cell in result.Cells.Where(cell => cell.ReceiverId is not null))
        {
            int x = (int)cell.Id % size, rx = (int)cell.ReceiverId!.Value % size;
            Assert.IsLessThanOrEqualTo(Math.Abs(x - 5), Math.Abs(rx - 5));
            Assert.AreEqual(cell.PhysicalElevation, cell.RoutingElevation);
        }
        CollectionAssert.AreEqual(result.Cells.ToArray(), MetricDrainageBuilder.Build(cells.AsEnumerable().Reverse(), points).Cells.ToArray());
    }

    [TestMethod]
    public void DepressionKeepsItsPhysicalFloorAndDryBasinsStayNonMarine()
    {
        DrainageCell[] cells = [new(0, 0, [1], DrainageTerminalKind.Ocean, true), new(1, 10, [0, 2]),
            new(2, -5, [1, 3]), new(3, -5, [2]), new(10, -30, [], DrainageTerminalKind.DryBasin)];
        var points = cells.ToDictionary(cell => cell.Id, cell => new WorldBlockPosition(cell.Id * 10, 0));
        DrainageTopology old = DepressionTopologyBuilder.Build(cells, 0), result = MetricDrainageBuilder.Build(cells, points, 0);
        CollectionAssert.AreEqual(old.Cells.Select(cell => cell.PhysicalElevation).ToArray(), result.Cells.Select(cell => cell.PhysicalElevation).ToArray());
        CollectionAssert.AreEqual(old.Cells.Select(cell => cell.RoutingElevation).ToArray(), result.Cells.Select(cell => cell.RoutingElevation).ToArray());
        Assert.AreEqual(DrainageWaterKind.SubmarineDryBasin, result.WaterStates.Single(item => item.CellId == 10).Kind);
    }

    [TestMethod]
    public void MissingAndCoincidentMetricCoordinatesAreRejected()
    {
        DrainageCell[] cells = [new(0, 1, [1]), new(1, 0, [0], DrainageTerminalKind.OutOfDomain)];
        Assert.ThrowsExactly<ArgumentException>(() => MetricDrainageBuilder.Build(cells, new Dictionary<long, WorldBlockPosition>()));
        Assert.ThrowsExactly<ArgumentException>(() => MetricDrainageBuilder.Build(cells,
            new Dictionary<long, WorldBlockPosition> { [0] = new(0, 0), [1] = new(0, 0) }));
    }
}
