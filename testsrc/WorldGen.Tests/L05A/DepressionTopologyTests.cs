using ISRWorldGen.Core.Hydrology.Depressions;

namespace ISRWorldGen.Tests.L05A;

[TestClass]
public sealed class DepressionTopologyTests
{
    [TestMethod]
    public void T05_01_NestedDepressionsPreserveReliefAndExposeCapacityAndSpill()
    {
        DrainageTopology topology = DepressionTopologyBuilder.Build([
            Cell(0, 0, [1], DrainageTerminalKind.Ocean), Cell(1, 8, [0, 2]),
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
        DrainageCell[] forward = [Cell(9, 0, [4]), Cell(4, 2, [3, 5, 9]), Cell(3, 2, [2, 4]), Cell(2, 2, [1, 3]), Cell(1, 2, [0, 2]), Cell(0, 0, [1], DrainageTerminalKind.Ocean), Cell(5, 2, [4, 6]), Cell(6, 2, [5])];
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
    public void InvalidGraphIsRejectedBeforeRouting()
    {
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build([Cell(1, 1, [2]), Cell(2, 1, [])]));
    }

    private static DrainageCell Cell(long id, double elevation, long[] neighbours, DrainageTerminalKind? terminal = null) => new(id, elevation, neighbours, terminal);
}
