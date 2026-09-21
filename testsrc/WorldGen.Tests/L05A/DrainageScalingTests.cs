using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Hydrology.Depressions;
using ISRWorldGen.Core.Hydrology.Discharge;

namespace ISRWorldGen.Tests.L05A;

/// <summary>
/// Graph-only analytical fixtures: IDs are not X/Z coordinates and no world,
/// raster, native climate, storage backend or game process is represented.
/// </summary>
[TestClass]
public sealed class DrainageScalingTests
{
    private static readonly DischargeClassificationSettings Classification = new(2d, 8d, 8d);
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DisconnectedComponentsKeepTheLowestElevationThenIdTerminal()
    {
        const int count = 2048;
        var cells = new List<DrainageCell>(count * 3);
        for (int i = 0; i < count; i++)
        {
            long first = i * 4L;
            bool tied = i % 2 == 0;
            cells.Add(new(first, tied ? 1d : 3d, [first + 1]));
            cells.Add(new(first + 1, tied ? 1d : -4d, [first, first + 2]));
            cells.Add(new(first + 2, 5d, [first + 1]));
        }
        DrainageTopology result = DepressionTopologyBuilder.Build(cells.AsEnumerable().Reverse(), 0d);
        Assert.HasCount(count * 3, result.Cells);
        Assert.HasCount(count, result.Cells.Where(cell => cell.ReceiverId is null).ToArray());
        foreach (DrainageConnectivity connection in result.Connectivity)
        {
            long group = connection.CellId / 4;
            Assert.AreEqual(group * 4 + (group % 2 == 0 ? 0 : 1), connection.TerminalCellId);
            Assert.AreEqual(DrainageTerminalKind.DryBasin, connection.TerminalKind);
            Assert.IsNull(connection.MarineBoundaryCellId);
        }
        Assert.IsTrue(result.WaterStates.Where(item => item.CellId / 4 % 2 == 1)
            .All(item => item.Kind == DrainageWaterKind.SubmarineDryBasin));
    }

    [TestMethod]
    public void HighDegreeAdjacencyUsesAllInt64IdsWithoutWeakeningSymmetry()
    {
        const int count = 4096;
        long[] leaves = Enumerable.Range(0, count).Select(i => long.MaxValue - i).ToArray();
        var cells = new List<DrainageCell> { new(long.MinValue, 0d, leaves, DrainageTerminalKind.Ocean, true) };
        cells.AddRange(leaves.Select(id => new DrainageCell(id, 2d, [long.MinValue])));
        DrainageTopology result = DepressionTopologyBuilder.Build(cells);
        Assert.HasCount(count + 1, result.Cells);
        Assert.IsTrue(result.Connectivity.All(item => item.TerminalCellId == long.MinValue));
        Assert.HasCount(0, result.Depressions);
        cells[^1] = new DrainageCell(leaves[^1], 2d, []);
        Assert.ThrowsExactly<ArgumentException>(() => DepressionTopologyBuilder.Build(cells));
    }

    [TestMethod]
    public void NeighbourIndexRetainsTheOwnedReadOnlySortedSnapshot()
    {
        long[] input = [long.MaxValue, long.MinValue, 0, long.MaxValue];
        var cell = new DrainageCell(42, 3d, input);
        input[0] = 99;
        CollectionAssert.AreEqual(new long[] { long.MinValue, 0, long.MaxValue }, cell.Neighbours.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<long>)cell.Neighbours)[0] = 99);
        DrainageTopology result = DepressionTopologyBuilder.Build([
            cell,
            new(long.MinValue, 0, [42], DrainageTerminalKind.Ocean, true),
            new(0, 4, [42]), new(long.MaxValue, 4, [42])]);
        Assert.IsTrue(result.Connectivity.All(item => item.TerminalCellId == long.MinValue));
    }

    [TestMethod]
    public void ManySeparateCupsKeepCanonicalHierarchyAndExactCapacity()
    {
        const int count = 2048;
        DrainageTopology result = DepressionTopologyBuilder.Build(SeparateCups(count));
        Assert.HasCount(count * 2, result.Depressions);
        for (int i = 0; i < count; i++)
        {
            Depression parent = result.Depressions[i * 2];
            Depression child = result.Depressions[i * 2 + 1];
            Assert.AreEqual(i * 2L, parent.Id);
            Assert.AreEqual(parent.Id, child.ParentDepressionId);
            Assert.AreEqual(10d - i % 4, parent.Capacity);
            Assert.AreEqual(parent.Capacity, child.Capacity);
            Assert.AreEqual(10d, parent.SpillElevation);
            CollectionAssert.AreEqual(new[] { i * 2L + 2 }, parent.CellIds.ToArray());
        }
    }

    [TestMethod]
    public void WideFlatBottomRemainsOneCupRatherThanThousandsOfIdFragments()
    {
        const int count = 8192;
        DrainageTopology result = DepressionTopologyBuilder.Build(CupChain(count, false));
        Assert.HasCount(2, result.Depressions);
        Depression parent = result.Depressions[0], child = result.Depressions[1];
        Assert.HasCount(count, parent.CellIds);
        CollectionAssert.AreEqual(parent.CellIds.ToArray(), child.CellIds.ToArray());
        Assert.AreEqual(10d * count, parent.Capacity);
        Assert.AreEqual(parent.Capacity, child.Capacity);
        Assert.AreEqual(parent.Id, child.ParentDepressionId);
        Assert.IsTrue(result.Cells.Skip(2).All(cell => cell.PhysicalElevation == 2d && cell.RoutingElevation == 12d));
    }

    [TestMethod]
    public void AlternatingLocalMinimaRetainEachChildAndTheirCommonParent()
    {
        const int count = 4096;
        DrainageTopology result = DepressionTopologyBuilder.Build(CupChain(count, true));
        Assert.HasCount(1 + count / 2, result.Depressions);
        Assert.AreEqual(17d * count / 2, result.Depressions[0].Capacity);
        for (int i = 1; i < result.Depressions.Count; i++)
        {
            Depression child = result.Depressions[i];
            Assert.AreEqual(0L, child.ParentDepressionId);
            Assert.AreEqual(3d, child.Capacity);
            Assert.AreEqual(5d, child.SpillElevation);
            CollectionAssert.AreEqual(new[] { i * 2L }, child.CellIds.ToArray());
        }
    }

    [TestMethod]
    public void LongRiverAccumulatesEveryUnitWithoutRecursiveTraversal()
    {
        const int count = 16384;
        DrainageCell[] cells = RiverChain(count);
        DrainageTopology topology = DepressionTopologyBuilder.Build(cells);
        DischargeSnapshot flow = Accumulate(topology, cells.Reverse().Select(cell => cell.Id));
        Assert.HasCount(count, flow.Reaches);
        for (int i = 0; i < count; i++)
        {
            Assert.AreEqual((double)(count - i), flow.Reaches[i].DischargeModelVolumePerYear);
            Assert.AreEqual((double)(count - i), flow.Reaches[i].DrainageAreaModelSquareLength);
            Assert.AreEqual(0d, flow.Reaches[i].ResidualModelVolumePerYear);
        }
        Assert.HasCount(1, flow.Terminals);
        Assert.AreEqual((double)count, flow.Terminals[0].DischargeModelVolumePerYear);
        Assert.AreEqual(0d, flow.Balance.ResidualModelVolumePerYear);
        Assert.AreEqual(37, flow.Iteration);
    }

    [TestMethod]
    public void IndexedAlignmentStillRejectsDuplicateMissingAndForeignBudgetIds()
    {
        DrainageTopology topology = DepressionTopologyBuilder.Build(RiverChain(3));
        Assert.ThrowsExactly<ArgumentException>(() => Accumulate(topology, [0, 1, 99]));
        Assert.ThrowsExactly<ArgumentException>(() => Accumulate(topology, [0, 1]));
        Assert.ThrowsExactly<ArgumentException>(() => Accumulate(topology, [0, 1, 1]));
        DischargeSnapshot valid = Accumulate(topology, [2, 0, 1]);
        Assert.AreEqual(3d, valid.Terminals[0].DischargeModelVolumePerYear);
    }

    [TestMethod]
    public void ParallelBuildsAndPermutationsPreserveCompletePublishedPayloads()
    {
        DrainageCell[] cells = SeparateCups(128);
        byte[] before = JsonSerializer.SerializeToUtf8Bytes(cells);
        byte[] expected = Payload(DepressionTopologyBuilder.Build(cells));
        Parallel.For(0, 16, iteration =>
        {
            IEnumerable<DrainageCell> input = iteration % 2 == 0 ? cells : cells.Reverse();
            CollectionAssert.AreEqual(expected, Payload(DepressionTopologyBuilder.Build(input)));
        });
        CollectionAssert.AreEqual(before, JsonSerializer.SerializeToUtf8Bytes(cells));
    }

    [TestMethod]
    public void TinyGraphsAgreeWithIndependentMinimaxRelaxation()
    {
        (int A, int B)[] edges = [(0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3)];
        for (int mask = 0; mask < 64; mask++)
        for (int mode = 0; mode < 3; mode++)
        {
            var neighbours = Enumerable.Range(0, 4).Select(_ => new List<long>()).ToArray();
            for (int edge = 0; edge < edges.Length; edge++)
                if ((mask & (1 << edge)) != 0)
                {
                    (int a, int b) = edges[edge];
                    neighbours[a].Add(b); neighbours[b].Add(a);
                }
            DrainageCell[] cells = Enumerable.Range(0, 4).Select(i => new DrainageCell(i,
                mode == 2 ? 2d : new double[] { 0, 5, -2, 3 }[i], neighbours[i],
                mode == 0 && i == 0 ? DrainageTerminalKind.Ocean :
                mode == 1 && i is 1 or 3 ? DrainageTerminalKind.EndorheicLake : null,
                mode == 0 && i == 0)).ToArray();
            double[] expected = ReferenceMinimax(cells);
            DrainageTopology result = DepressionTopologyBuilder.Build(cells.Reverse(), 0);
            foreach (RoutedCell routed in result.Cells)
            {
                Assert.AreEqual(expected[(int)routed.Id], routed.RoutingElevation, $"Mask={mask}, mode={mode}, cell={routed.Id}");
                Assert.AreEqual(cells[(int)routed.Id].PhysicalElevation, routed.PhysicalElevation);
            }
        }
    }

    [TestMethod]
    public void RoutingAndDischargeWitnessRetainsCanonicalBytesAndRecordsMeasurements()
    {
        // This test also runs unchanged against the pinned old production source
        // in CI. Timing is observed, never used as a flaky acceptance threshold.
        var records = new List<object>();
        foreach (int size in new[] { 256, 1024, 4096 })
        foreach (string shape in new[] { "isolated", "star", "separate-cups", "flat-cup", "alternating-cup", "river" })
        {
            DrainageCell[] cells = Fixture(shape, size);
            DrainageTopology? topology = null;
            DischargeSnapshot? flow = null;
            var routingTimes = new List<double>();
            var dischargeTimes = new List<double>();
            var allocated = new List<long>();
            for (int repetition = 0; repetition < 3; repetition++)
            {
                long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                topology = DepressionTopologyBuilder.Build(cells, 0d);
                routingTimes.Add(timer.Elapsed.TotalMilliseconds);
                timer.Restart();
                flow = Accumulate(topology, cells.Select(cell => cell.Id));
                dischargeTimes.Add(timer.Elapsed.TotalMilliseconds);
                allocated.Add(GC.GetAllocatedBytesForCurrentThread() - allocationBefore);
            }
            Assert.IsNotNull(topology);
            Assert.IsNotNull(flow);
            Assert.AreEqual((double)cells.Length, flow.Balance.TerminalDischargeModelVolumePerYear);
            Assert.AreEqual(0d, flow.Balance.ResidualModelVolumePerYear);
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(new { topology, flow });
            records.Add(new
            {
                shape, size, cells = cells.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                terminalFlow = flow.Balance.TerminalDischargeModelVolumePerYear,
                routingMs = routingTimes.Order().ElementAt(1),
                dischargeMs = dischargeTimes.Order().ElementAt(1),
                allocatedBytes = allocated.Order().ElementAt(1)
            });
        }
        TestContext.WriteLine("DRAINAGE_SCALING_WITNESS=" + JsonSerializer.Serialize(records));
    }

    private static byte[] Payload(DrainageTopology topology) => JsonSerializer.SerializeToUtf8Bytes(topology);

    private static DrainageCell[] Fixture(string shape, int size) => shape switch
    {
        "isolated" => Enumerable.Range(0, size).Select(i => new DrainageCell(i, i % 7, [])).ToArray(),
        "star" => new[] { new DrainageCell(0, 0, Enumerable.Range(1, size).Select(i => (long)i), DrainageTerminalKind.Ocean, true) }
            .Concat(Enumerable.Range(1, size).Select(i => new DrainageCell(i, i % 5, [0]))).ToArray(),
        "separate-cups" => SeparateCups(size),
        "flat-cup" => CupChain(size, false),
        "alternating-cup" => CupChain(size, true),
        "river" => RiverChain(size),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    private static DrainageCell[] SeparateCups(int count)
    {
        var cells = new List<DrainageCell>(count * 2 + 1)
        {
            new(0, 0, Enumerable.Range(0, count).Select(i => i * 2L + 1), DrainageTerminalKind.Ocean, true)
        };
        for (int i = 0; i < count; i++)
        {
            long saddle = i * 2L + 1;
            cells.Add(new(saddle, 10, [0, saddle + 1]));
            cells.Add(new(saddle + 1, i % 4, [saddle]));
        }
        return cells.ToArray();
    }

    private static DrainageCell[] CupChain(int count, bool alternating)
    {
        var cells = new List<DrainageCell> { new(0, 0, [1], DrainageTerminalKind.Ocean, true), new(1, 12, [0, 2]) };
        for (int i = 0; i < count; i++)
        {
            long id = i + 2L;
            long[] neighbours = i == count - 1 ? [id - 1] : [id - 1, id + 1];
            cells.Add(new(id, alternating && i % 2 == 1 ? 5 : 2, neighbours));
        }
        return cells.ToArray();
    }

    private static DrainageCell[] RiverChain(int count) => Enumerable.Range(0, count).Select(i =>
        new DrainageCell(i, i, i == 0 ? [1] : i == count - 1 ? [i - 1L] : [i - 1L, i + 1L],
            i == 0 ? DrainageTerminalKind.Ocean : null, i == 0)).ToArray();

    private static DischargeSnapshot Accumulate(DrainageTopology topology, IEnumerable<long> ids)
        => DischargeAccumulator.Accumulate(new UnitBudget(ids), topology, [], Classification);

    private sealed class UnitBudget(IEnumerable<long> ids) : IWaterBudgetSnapshotSource
    {
        public int AlgorithmVersion => WaterBudgetSnapshot.AlgorithmVersion;
        public int Iteration => 37;
        public IReadOnlyList<WaterBudgetCell> Cells { get; } = ids.Select(id =>
            new WaterBudgetCell(id, new StableId(0, unchecked((ulong)id)), 1, 0, 1, 0, 0, 0, 1)).ToArray();
        public IReadOnlyList<GroundwaterTransfer> Transfers { get; } = Array.Empty<GroundwaterTransfer>();
    }

    private static double[] ReferenceMinimax(DrainageCell[] cells)
    {
        // Independently identify connected components, select implicit roots, then
        // use repeated all-edge relaxation. No PriorityQueue or production helper.
        var roots = new HashSet<long>();
        var remaining = cells.Select(cell => cell.Id).ToHashSet();
        while (remaining.Count > 0)
        {
            long start = remaining.Min();
            var group = new HashSet<long> { start };
            var pending = new Queue<long>(); pending.Enqueue(start); remaining.Remove(start);
            while (pending.TryDequeue(out long id))
                foreach (long neighbour in cells[(int)id].Neighbours)
                    if (group.Add(neighbour)) { pending.Enqueue(neighbour); remaining.Remove(neighbour); }
            long[] declared = group.Where(id => cells[(int)id].Terminal.HasValue).ToArray();
            if (declared.Length > 0) roots.UnionWith(declared);
            else roots.Add(group.OrderBy(id => cells[(int)id].PhysicalElevation).ThenBy(id => id).First());
        }
        double[] levels = cells.Select(cell => roots.Contains(cell.Id) ? cell.PhysicalElevation : double.PositiveInfinity).ToArray();
        for (int pass = 0; pass < cells.Length; pass++)
            foreach (DrainageCell cell in cells)
                if (!roots.Contains(cell.Id))
                    foreach (long neighbour in cell.Neighbours)
                        levels[(int)cell.Id] = Math.Min(levels[(int)cell.Id], Math.Max(cell.PhysicalElevation, levels[(int)neighbour]));
        return levels;
    }
}
