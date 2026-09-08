using ISRWorldGen.Core.Hydrology.Depressions;

namespace ISRWorldGen.Tests.L05A;

[TestClass]
public sealed class DepressionTopologyTests
{
    [TestMethod]
    public void T05_01_NestedDepressionsPreserveReliefAndExposeCapacityAndSpill()
    {
        DrainageTopology topology = DepressionTopologyBuilder.Build([
            Cell(0, 0, [1], DrainageTerminalKind.Ocean, true), Cell(1, 8, [0, 2]),
            Cell(2, 3, [1, 3]), Cell(3, 5, [2, 4]), Cell(4, 2, [3])]);

        RoutedCell inner = topology.Cells.Single(cell => cell.Id == 4);
        Assert.AreEqual(2d, inner.PhysicalElevation);
        Assert.AreEqual(8d, inner.RoutingElevation, "The routing field, not the physical relief, carries the fill.");
        Assert.HasCount(3, topology.Depressions);
        Depression depression = topology.Depressions.Single(item => item.ParentDepressionId is null);
        CollectionAssert.AreEqual(new long[] { 2, 3, 4 }, depression.CellIds.ToArray());
        Assert.AreEqual(8d, depression.SpillElevation);
        Assert.AreEqual(14d, depression.Capacity);
        Depression nestedCup = topology.Depressions.Single(item => item.CellIds.SequenceEqual(new long[] { 4 }));
        Assert.AreEqual(depression.Id, nestedCup.ParentDepressionId);
        Assert.AreEqual(5d, nestedCup.SpillElevation);
        Assert.AreEqual(3d, nestedCup.Capacity);
    }

    [TestMethod]
    public void T05_02_FlatDrainageIsAcyclicStableAndEveryCellEndsAtAnExplicitTerminal()
    {
        DrainageCell[] forward = [Cell(9, 0, [4]), Cell(4, 2, [3, 5, 9]), Cell(3, 2, [2, 4]), Cell(2, 2, [1, 3]), Cell(1, 2, [0, 2]), Cell(0, 0, [1], DrainageTerminalKind.Ocean, true), Cell(5, 2, [4, 6]), Cell(6, 2, [5])];
        DrainageTopology first = DepressionTopologyBuilder.Build(forward);
        DrainageTopology reversed = DepressionTopologyBuilder.Build(forward.Reverse());
        CollectionAssert.AreEqual(first.Cells.ToArray(), reversed.Cells.ToArray());
        foreach (RoutedCell cell in first.Cells)
        {
            var seen = new HashSet<long>(); RoutedCell cursor = cell;
            while (cursor.ReceiverId is long receiver)
            {
                Assert.IsTrue(seen.Add(cursor.Id), "A drainage receiver cycle was found.");
                cursor = first.Cells.Single(candidate => candidate.Id == receiver);
            }
            Assert.IsNotNull(cursor.Terminal, "Every receiver chain must reach an explicit terminal.");
        }
        Assert.AreEqual(0L, first.Cells.Single(cell => cell.Id == 1).ReceiverId, "Stable ID breaks an equal-height, equal-exit plateau tie.");
    }

    [TestMethod]
    public void T05_01_AdjacentFlatBottomCellsFormOneAnalyticalCupDespiteIdTieBreak()
    {
        DrainageTopology topology = DepressionTopologyBuilder.Build([
            Cell(0, 0, [1], DrainageTerminalKind.Ocean, true), Cell(1, 8, [0, 2]),
            Cell(2, 2, [1, 3]), Cell(3, 2, [2, 4]), Cell(4, 5, [3])]);

        Depression cup = topology.Depressions.Single(item => item.CellIds.SequenceEqual(new long[] { 2, 3 }));
        Assert.AreEqual(5d, cup.SpillElevation);
        Assert.AreEqual(6d, cup.Capacity);
        Assert.AreEqual(2L, topology.Cells.Single(cell => cell.Id == 3).ReceiverId,
            "ID may orient a flat receiver chain, but must not split its analytical water body.");
    }

    [TestMethod]
    public void InvalidGraphIsRejectedBeforeRouting()
    {
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build([Cell(1, 1, [2]), Cell(2, 1, [])]));
    }

    [TestMethod]
    public void T05_02_AllInputAndNeighbourPermutationsProduceTheSameDagAndHierarchy()
    {
        DrainageCell[] cells =
        [
            Cell(20, 0, [30], DrainageTerminalKind.Ocean, true),
            Cell(30, 5, [20, 40, 50]),
            Cell(40, 1, [30, 60]),
            Cell(50, 1, [30, 60]),
            Cell(60, 1, [50, 40, 70]),
            Cell(70, 5, [60]),
        ];
        DrainageTopology baseline = DepressionTopologyBuilder.Build(cells);
        DrainageTopology permuted = DepressionTopologyBuilder.Build(cells.Reverse()
            .Select(cell => Cell(cell.Id, cell.PhysicalElevation, cell.Neighbours.Reverse().ToArray(), cell.Terminal, cell.IsMarineBoundary)));

        CollectionAssert.AreEqual(baseline.Cells.ToArray(), permuted.Cells.ToArray());
        CollectionAssert.AreEqual(
            baseline.Depressions.Select(item => (item.Id, Cells: string.Join(',', item.CellIds), item.SpillElevation, item.Capacity, item.ParentDepressionId)).ToArray(),
            permuted.Depressions.Select(item => (item.Id, Cells: string.Join(',', item.CellIds), item.SpillElevation, item.Capacity, item.ParentDepressionId)).ToArray());
        CollectionAssert.AreEqual(baseline.Connectivity.ToArray(), permuted.Connectivity.ToArray());
        Assert.AreEqual(20L, baseline.Cells.Single(cell => cell.Id == 30).ReceiverId,
            "Equivalent saddle exits must select the lowest stable terminal ID.");
    }

    [TestMethod]
    public void T05_04_ConnectivityClassifiesOceanOnlyWhenItReachesAnOceanTerminal()
    {
        DrainageTopology topology = DepressionTopologyBuilder.Build(
        [
            Cell(1, -10, [2]), Cell(2, -8, [1], DrainageTerminalKind.DryBasin),
            Cell(10, -4, [11]), Cell(11, 0, [10], DrainageTerminalKind.Ocean, true),
            Cell(20, 100, [21]), Cell(21, 105, [20], DrainageTerminalKind.EndorheicLake),
        ], seaLevel: 0d);

        DrainageConnectivity submergedBasin = topology.Connectivity.Single(item => item.CellId == 1);
        Assert.AreEqual(DrainageTerminalKind.DryBasin, submergedBasin.TerminalKind,
            "A sub-sea-level interior basin is not ocean without topological connection.");
        Assert.AreEqual(DrainageTerminalKind.Ocean, topology.Connectivity.Single(item => item.CellId == 10).TerminalKind,
            "The open lagoon reaches its declared marine outlet.");
        Assert.AreEqual(DrainageTerminalKind.EndorheicLake, topology.Connectivity.Single(item => item.CellId == 20).TerminalKind,
            "A closed mountain lake remains a distinct terminal kind.");
        Assert.AreEqual(DrainageWaterKind.SubmarineDryBasin, topology.WaterStates.Single(item => item.CellId == 1).Kind);
        Assert.AreEqual(DrainageWaterKind.OceanConnected, topology.WaterStates.Single(item => item.CellId == 10).Kind);
        Assert.AreEqual(DrainageWaterKind.ClosedLake, topology.WaterStates.Single(item => item.CellId == 20).Kind);
        Assert.AreEqual(11L, topology.Connectivity.Single(item => item.CellId == 10).MarineBoundaryCellId);
        Assert.IsNull(topology.Connectivity.Single(item => item.CellId == 1).MarineBoundaryCellId);
    }

    [TestMethod]
    public void InvalidTopologiesAndUnsupportedNumericCapacityAreRejectedExplicitly()
    {
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build(
        [Cell(1, 0, [1])]));
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build(
        [Cell(1, 0, [2]), Cell(1, 0, [2])]));
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build(
        [Cell(1, 0, [2]), Cell(2, 0, [])]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DepressionTopologyBuilder.Build(
        [Cell(0, double.MaxValue, [1], DrainageTerminalKind.Ocean, true), Cell(1, -double.MaxValue, [0])]));
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build(
        [Cell(30, -5, [], DrainageTerminalKind.Ocean)]));
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build(
        [Cell(31, 0, [], DrainageTerminalKind.DryBasin, true)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DepressionTopologyBuilder.Build(
        [Cell(0, 0, [], DrainageTerminalKind.Ocean, true)], double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Cell(0, double.PositiveInfinity, []));
    }

    private static DrainageCell Cell(long id, double elevation, long[] neighbours, DrainageTerminalKind? terminal = null, bool isMarineBoundary = false)
        => new(id, elevation, neighbours, terminal, isMarineBoundary);
}
