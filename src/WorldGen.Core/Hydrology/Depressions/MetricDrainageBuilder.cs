using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Hydrology.Depressions;

/// <summary>
/// Geometric routing revision: Priority-Flood determines only the spill surface.
/// Strict descents use drop / physical edge length. Equal-height regions use a
/// separate shortest-path potential to their real exits; no epsilon is added to
/// the terrain. The old topology-only builder remains available for replay.
/// </summary>
public static class MetricDrainageBuilder
{
    public const string AlgorithmId = "metric-drainage-v1-steepest-and-flat-distance";

    public static DrainageTopology Build(
        IEnumerable<DrainageCell> source,
        IReadOnlyDictionary<long, WorldBlockPosition> positions,
        double? seaLevel = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(positions);
        DrainageCell[] cells = source.ToArray();
        // The existing builder validates IDs, symmetric adjacency and terminals,
        // and preserves the physical relief while resolving depression levels.
        DrainageTopology flood = seaLevel is double level
            ? DepressionTopologyBuilder.Build(cells, level)
            : DepressionTopologyBuilder.Build(cells);
        if (positions.Count != cells.Length || cells.Any(cell => !positions.ContainsKey(cell.Id)))
            throw new ArgumentException("Exactly one metric position is required for every drainage cell.", nameof(positions));
        if (positions.Values.Distinct().Count() != positions.Count)
            throw new ArgumentException("Distinct drainage cells must have distinct metric positions.", nameof(positions));

        var adjacency = cells.ToDictionary(cell => cell.Id);
        var routed = flood.Cells.ToDictionary(cell => cell.Id);
        var receivers = new Dictionary<long, long?>();
        var distances = new Dictionary<long, double>();
        var seeds = new HashSet<long>();
        var queue = new PriorityQueue<long, (double Distance, long Id)>();
        foreach (RoutedCell cell in flood.Cells)
        {
            if (cell.Terminal is not null)
            {
                receivers.Add(cell.Id, null);
                Seed(cell.Id);
                continue;
            }
            double bestSlope = 0d;
            long? bestReceiver = null;
            foreach (long neighbour in adjacency[cell.Id].Neighbours)
            {
                double drop = cell.RoutingElevation - routed[neighbour].RoutingElevation;
                if (drop <= 0d) continue;
                double slope = drop / Length(cell.Id, neighbour);
                if (!double.IsFinite(slope)) throw new ArgumentOutOfRangeException(nameof(source), "Unrepresentable metric slope.");
                // Neighbours have a canonical ascending order; equal physical
                // slopes use the stable ID only after geometry has been compared.
                if (slope > bestSlope)
                {
                    bestSlope = slope;
                    bestReceiver = neighbour;
                }
            }
            if (bestReceiver is long receiver)
            {
                receivers.Add(cell.Id, receiver);
                Seed(cell.Id);
            }
        }

        // Multi-source Dijkstra restricted to equal spill elevations. An exit has
        // potential zero. Every flat receiver has a strictly smaller potential;
        // strict-slope receivers instead have a strictly smaller spill elevation.
        // This lexicographic invariant rules out cycles without changing height.
        while (queue.TryDequeue(out long current, out (double Distance, long Id) priority))
        {
            if (distances[current] != priority.Distance) continue;
            foreach (long neighbour in adjacency[current].Neighbours)
            {
                if (seeds.Contains(neighbour) || routed[neighbour].RoutingElevation != routed[current].RoutingElevation) continue;
                double candidate = priority.Distance + Length(current, neighbour);
                if (!double.IsFinite(candidate) || candidate <= priority.Distance)
                    throw new ArgumentOutOfRangeException(nameof(positions), "Flat distance cannot be represented with a strictly decreasing potential.");
                if (!distances.TryGetValue(neighbour, out double old) || candidate < old)
                {
                    distances[neighbour] = candidate;
                    receivers[neighbour] = current;
                    queue.Enqueue(neighbour, (candidate, neighbour));
                }
                else if (candidate == old && current < receivers[neighbour]!.Value)
                {
                    receivers[neighbour] = current;
                }
            }
        }
        if (receivers.Count != cells.Length)
            throw new InvalidOperationException("A filled flat has no geometric route to an explicit exit.");
        RoutedCell[] result = flood.Cells.Select(cell => cell with { ReceiverId = receivers[cell.Id] }).ToArray();
        DrainageConnectivity[] connectivity = Connect(result).OrderBy(item => item.CellId).ToArray();
        return new DrainageTopology(result, flood.Depressions, connectivity,
            WaterStates(connectivity, result, seaLevel), seaLevel);

        void Seed(long id)
        {
            seeds.Add(id);
            distances.Add(id, 0d);
            queue.Enqueue(id, (0d, id));
        }
        double Length(long a, long b)
        {
            WorldBlockPosition first = positions[a], second = positions[b];
            // Subtract as decimal first: adjacent Int64 coordinates must not
            // collapse to the same double when the world origin is very large.
            double dx = (double)((decimal)first.X - second.X);
            double dz = (double)((decimal)first.Z - second.Z);
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (!double.IsFinite(length) || length <= 0d)
                throw new ArgumentException("Drainage edge length must be finite and positive.", nameof(positions));
            return length;
        }
    }

    private static IEnumerable<DrainageConnectivity> Connect(RoutedCell[] cells)
    {
        var byId = cells.ToDictionary(cell => cell.Id);
        var resolved = new Dictionary<long, DrainageConnectivity>();
        foreach (RoutedCell start in cells)
        {
            var path = new List<long>();
            var seen = new HashSet<long>();
            RoutedCell cursor = start;
            while (!resolved.ContainsKey(cursor.Id))
            {
                if (!seen.Add(cursor.Id)) throw new InvalidOperationException("Metric routing contains a cycle.");
                path.Add(cursor.Id);
                if (cursor.ReceiverId is long receiver) { cursor = byId[receiver]; continue; }
                DrainageTerminalKind kind = cursor.Terminal ?? throw new InvalidOperationException("Missing explicit metric terminal.");
                resolved.Add(cursor.Id, new(cursor.Id, cursor.Id, kind, kind == DrainageTerminalKind.Ocean ? cursor.Id : null));
                break;
            }
            DrainageConnectivity destination = resolved[cursor.Id];
            foreach (long id in path) resolved[id] = destination with { CellId = id };
        }
        return resolved.Values.OrderBy(item => item.CellId);
    }

    private static IEnumerable<DrainageWaterState> WaterStates(DrainageConnectivity[] connectivity, RoutedCell[] cells, double? sea)
    {
        var byId = cells.ToDictionary(cell => cell.Id);
        foreach (DrainageConnectivity item in connectivity)
        {
            DrainageWaterKind kind = item.TerminalKind switch
            {
                DrainageTerminalKind.Ocean => DrainageWaterKind.OceanConnected,
                DrainageTerminalKind.EndorheicLake => DrainageWaterKind.ClosedLake,
                DrainageTerminalKind.DryBasin when sea is double level && byId[item.TerminalCellId].PhysicalElevation < level => DrainageWaterKind.SubmarineDryBasin,
                DrainageTerminalKind.DryBasin => DrainageWaterKind.DryBasin,
                DrainageTerminalKind.OutOfDomain => DrainageWaterKind.OutOfDomain,
                _ => throw new InvalidOperationException("Unsupported drainage terminal."),
            };
            yield return new(item.CellId, item.TerminalCellId, item.MarineBoundaryCellId, kind);
        }
    }
}
