using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Hydrology.Depressions;

/// <summary>Explicit terminal kinds. A routing graph never uses an implicit null terminal.</summary>
public enum DrainageTerminalKind { Ocean, EndorheicLake, DryBasin, OutOfDomain }

/// <summary>
/// Immutable input vertex for the analytical routing graph. Elevation is the physical
/// relief; it is deliberately never changed by the routing calculation.
/// </summary>
public sealed record DrainageCell
{
    public DrainageCell(long id, double physicalElevation, IEnumerable<long> neighbours, DrainageTerminalKind? terminal = null)
    {
        if (!double.IsFinite(physicalElevation)) throw new ArgumentOutOfRangeException(nameof(physicalElevation));
        ArgumentNullException.ThrowIfNull(neighbours);
        Id = id;
        PhysicalElevation = physicalElevation;
        Neighbours = Array.AsReadOnly(neighbours.Distinct().OrderBy(value => value).ToArray());
        Terminal = terminal;
    }

    public long Id { get; }
    public double PhysicalElevation { get; }
    public ReadOnlyCollection<long> Neighbours { get; }
    public DrainageTerminalKind? Terminal { get; }
}

public sealed record RoutedCell(long Id, double PhysicalElevation, double RoutingElevation, long? ReceiverId, DrainageTerminalKind? Terminal);

public sealed record Depression(long Id, IReadOnlyList<long> CellIds, double SpillElevation, double Capacity, long? ParentDepressionId);

public sealed class DrainageTopology
{
    internal DrainageTopology(IEnumerable<RoutedCell> cells, IEnumerable<Depression> depressions)
    {
        Cells = Array.AsReadOnly(cells.OrderBy(cell => cell.Id).ToArray());
        Depressions = Array.AsReadOnly(depressions.OrderBy(item => item.Id).ToArray());
    }

    public ReadOnlyCollection<RoutedCell> Cells { get; }
    public ReadOnlyCollection<Depression> Depressions { get; }
}

/// <summary>
/// Deterministic Priority-Flood routing. The result is a routing surface separate from
/// physical relief, so consumers may render unfilled terrain while using the DAG.
/// </summary>
public static class DepressionTopologyBuilder
{
    public static DrainageTopology Build(IEnumerable<DrainageCell> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        DrainageCell[] cells = source.OrderBy(cell => cell.Id).ToArray();
        if (cells.Length == 0) throw new ArgumentException("A drainage graph requires at least one cell.", nameof(source));
        if (cells.Select(cell => cell.Id).Distinct().Count() != cells.Length) throw new ArgumentException("Drainage cell IDs must be unique.", nameof(source));

        Dictionary<long, DrainageCell> byId = cells.ToDictionary(cell => cell.Id);
        foreach (DrainageCell cell in cells)
        {
            foreach (long neighbour in cell.Neighbours)
            {
                if (neighbour == cell.Id || !byId.ContainsKey(neighbour) || !byId[neighbour].Neighbours.Contains(cell.Id))
                    throw new ArgumentException("Drainage adjacency must be symmetric and refer only to distinct known cells.", nameof(source));
            }
        }

        // A graph without a declared outlet is an explicitly dry terminal, selected by
        // (elevation, ID); this makes closed basins deterministic rather than null-routed.
        DrainageCell[] declared = cells.Where(cell => cell.Terminal is not null).ToArray();
        if (declared.Length == 0)
        {
            DrainageCell dry = cells.OrderBy(cell => cell.PhysicalElevation).ThenBy(cell => cell.Id).First();
            declared = [new DrainageCell(dry.Id, dry.PhysicalElevation, dry.Neighbours, DrainageTerminalKind.DryBasin)];
            byId[dry.Id] = declared[0];
        }

        var visited = new HashSet<long>();
        var routing = new Dictionary<long, double>();
        var receivers = new Dictionary<long, long?>();
        var queue = new PriorityQueue<long, (double Elevation, long Id)>();
        foreach (DrainageCell terminal in declared.OrderBy(cell => cell.Id))
        {
            visited.Add(terminal.Id); routing[terminal.Id] = terminal.PhysicalElevation; receivers[terminal.Id] = null;
            queue.Enqueue(terminal.Id, (terminal.PhysicalElevation, terminal.Id));
        }

        while (queue.TryDequeue(out long currentId, out (double Elevation, long Id) priority))
        {
            DrainageCell current = byId[currentId];
            foreach (long neighbourId in current.Neighbours)
            {
                if (!visited.Add(neighbourId)) continue;
                DrainageCell neighbour = byId[neighbourId];
                double route = Math.Max(priority.Elevation, neighbour.PhysicalElevation);
                routing[neighbourId] = route;
                receivers[neighbourId] = currentId;
                queue.Enqueue(neighbourId, (route, neighbourId));
            }
        }

        // Each disconnected component is a specified dry terminal, never a silent null.
        while (visited.Count != cells.Length)
        {
            DrainageCell dry = cells.Where(cell => !visited.Contains(cell.Id)).OrderBy(cell => cell.PhysicalElevation).ThenBy(cell => cell.Id).First();
            visited.Add(dry.Id); routing[dry.Id] = dry.PhysicalElevation; receivers[dry.Id] = null;
            byId[dry.Id] = new DrainageCell(dry.Id, dry.PhysicalElevation, dry.Neighbours, DrainageTerminalKind.DryBasin);
            queue.Enqueue(dry.Id, (dry.PhysicalElevation, dry.Id));
            while (queue.TryDequeue(out long currentId, out (double Elevation, long Id) priority))
            {
                foreach (long neighbourId in byId[currentId].Neighbours)
                {
                    if (!visited.Add(neighbourId)) continue;
                    DrainageCell neighbour = byId[neighbourId]; double route = Math.Max(priority.Elevation, neighbour.PhysicalElevation);
                    routing[neighbourId] = route; receivers[neighbourId] = currentId; queue.Enqueue(neighbourId, (route, neighbourId));
                }
            }
        }

        RoutedCell[] result = cells.Select(cell => new RoutedCell(cell.Id, cell.PhysicalElevation, routing[cell.Id], receivers[cell.Id], byId[cell.Id].Terminal)).ToArray();
        return new DrainageTopology(result, BuildDepressions(cells, result));
    }

    private static IEnumerable<Depression> BuildDepressions(IReadOnlyList<DrainageCell> physical, IReadOnlyList<RoutedCell> routed)
    {
        Dictionary<long, DrainageCell> original = physical.ToDictionary(cell => cell.Id);
        Dictionary<long, RoutedCell> routing = routed.ToDictionary(cell => cell.Id);
        var candidates = new HashSet<long>(routed.Where(cell => cell.RoutingElevation > cell.PhysicalElevation).Select(cell => cell.Id));
        long nextId = 0;
        while (candidates.Count > 0)
        {
            long start = candidates.Min(); candidates.Remove(start);
            var component = new List<long>(); var pending = new Queue<long>(); pending.Enqueue(start);
            while (pending.Count > 0)
            {
                long id = pending.Dequeue(); component.Add(id);
                foreach (long neighbour in original[id].Neighbours.Where(candidates.Contains).OrderBy(value => value))
                {
                    candidates.Remove(neighbour); pending.Enqueue(neighbour);
                }
            }
            component.Sort();
            double spill = component.Select(id => routing[id].RoutingElevation).Max();
            double capacity = component.Sum(id => routing[id].RoutingElevation - routing[id].PhysicalElevation);
            long rootId = nextId++;
            yield return new Depression(rootId, Array.AsReadOnly(component.ToArray()), spill, capacity, null);

            // Retain local cups under their common routed component. The parent is
            // analytical (and may overlap its children), as in Fill-Spill-Merge:
            // it does not rewrite physical cells or turn a lake into a cell exception.
            foreach (long minimum in component.Where(id => IsStableLocalMinimum(id, component, original)).OrderBy(id => id))
            {
                double localSpill = original[minimum].Neighbours
                    .Where(neighbour => original[neighbour].PhysicalElevation > original[minimum].PhysicalElevation)
                    .Select(neighbour => original[neighbour].PhysicalElevation)
                    .DefaultIfEmpty(spill)
                    .Min();
                localSpill = Math.Min(localSpill, spill);
                yield return new Depression(
                    nextId++,
                    Array.AsReadOnly(new[] { minimum }),
                    localSpill,
                    localSpill - original[minimum].PhysicalElevation,
                    rootId);
            }
        }
    }

    private static bool IsStableLocalMinimum(long id, IReadOnlyCollection<long> component, IReadOnlyDictionary<long, DrainageCell> original) =>
        original[id].Neighbours.Where(component.Contains).All(neighbour =>
            original[id].PhysicalElevation < original[neighbour].PhysicalElevation ||
            (original[id].PhysicalElevation == original[neighbour].PhysicalElevation && id < neighbour));
}
