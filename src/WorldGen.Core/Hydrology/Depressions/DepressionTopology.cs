using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Hydrology.Depressions;

/// <summary>Explicit terminal kinds. A routing graph never uses an implicit null terminal.</summary>
public enum DrainageTerminalKind { Ocean, EndorheicLake, DryBasin, OutOfDomain }

/// <summary>
/// Topological destination of a routed cell.  This deliberately carries the declared
/// terminal rather than deriving salinity or a water body from elevation alone.
/// </summary>
public sealed record DrainageConnectivity(long CellId, long TerminalCellId, DrainageTerminalKind TerminalKind);

/// <summary>Water-relevant topological state; this is not a physical fill instruction.</summary>
public enum DrainageWaterKind { OceanConnected, ClosedLake, DryBasin, SubmarineDryBasin, OutOfDomain }

/// <summary>Immutable per-cell water classification for consumers of routing.</summary>
public sealed record DrainageWaterState(long CellId, long TerminalCellId, DrainageWaterKind Kind);

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
    internal DrainageTopology(
        IEnumerable<RoutedCell> cells,
        IEnumerable<Depression> depressions,
        IEnumerable<DrainageConnectivity> connectivity,
        IEnumerable<DrainageWaterState> waterStates,
        double? seaLevel)
    {
        Cells = Array.AsReadOnly(cells.OrderBy(cell => cell.Id).ToArray());
        Depressions = Array.AsReadOnly(depressions.OrderBy(item => item.Id).ToArray());
        Connectivity = Array.AsReadOnly(connectivity.OrderBy(item => item.CellId).ToArray());
        WaterStates = Array.AsReadOnly(waterStates.OrderBy(item => item.CellId).ToArray());
        SeaLevel = seaLevel;
    }

    public ReadOnlyCollection<RoutedCell> Cells { get; }
    public ReadOnlyCollection<Depression> Depressions { get; }
    public ReadOnlyCollection<DrainageConnectivity> Connectivity { get; }
    public ReadOnlyCollection<DrainageWaterState> WaterStates { get; }
    public double? SeaLevel { get; }
}

/// <summary>
/// Deterministic Priority-Flood routing. The result is a routing surface separate from
/// physical relief, so consumers may render unfilled terrain while using the DAG.
/// </summary>
public static class DepressionTopologyBuilder
{
    public static DrainageTopology Build(IEnumerable<DrainageCell> source)
        => BuildCore(source, null);

    /// <summary>
    /// Builds the immutable routing snapshot with an explicit sea level.  It permits
    /// consumers to distinguish a connected ocean from an enclosed low dry basin
    /// without physically filling either terrain cell.
    /// </summary>
    public static DrainageTopology Build(IEnumerable<DrainageCell> source, double seaLevel)
    {
        if (!double.IsFinite(seaLevel)) throw new ArgumentOutOfRangeException(nameof(seaLevel));
        return BuildCore(source, seaLevel);
    }

    private static DrainageTopology BuildCore(IEnumerable<DrainageCell> source, double? seaLevel)
    {
        ArgumentNullException.ThrowIfNull(source);
        DrainageCell[] supplied = source.ToArray();
        if (supplied.Any(cell => cell is null)) throw new ArgumentException("Drainage cells cannot contain null.", nameof(source));
        DrainageCell[] cells = supplied.OrderBy(cell => cell.Id).ToArray();
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
        DrainageConnectivity[] connectivity = BuildConnectivity(result).ToArray();
        return new DrainageTopology(
            result,
            BuildDepressions(cells, result),
            connectivity,
            BuildWaterStates(connectivity, result, seaLevel),
            seaLevel);
    }

    private static IEnumerable<DrainageConnectivity> BuildConnectivity(IReadOnlyList<RoutedCell> cells)
    {
        Dictionary<long, RoutedCell> byId = cells.ToDictionary(cell => cell.Id);
        var resolved = new Dictionary<long, DrainageConnectivity>();
        foreach (RoutedCell cell in cells.OrderBy(cell => cell.Id))
        {
            if (resolved.ContainsKey(cell.Id)) continue;

            var path = new List<long>();
            var seen = new HashSet<long>();
            RoutedCell cursor = cell;
            DrainageConnectivity? outcome = null;
            while (!resolved.TryGetValue(cursor.Id, out outcome))
            {
                if (!seen.Add(cursor.Id)) throw new InvalidOperationException("Priority-Flood produced a drainage cycle.");
                path.Add(cursor.Id);
                if (cursor.ReceiverId is null)
                {
                    if (cursor.Terminal is null) throw new InvalidOperationException("A drainage terminal must be explicit.");
                    outcome = new DrainageConnectivity(cursor.Id, cursor.Id, cursor.Terminal.Value);
                    resolved[cursor.Id] = outcome;
                    break;
                }

                cursor = byId[cursor.ReceiverId.Value];
            }

            foreach (long id in path) resolved[id] = outcome!;
        }

        return resolved.Select(pair => new DrainageConnectivity(
            pair.Key,
            pair.Value.TerminalCellId,
            pair.Value.TerminalKind));
    }

    private static IEnumerable<DrainageWaterState> BuildWaterStates(
        IReadOnlyList<DrainageConnectivity> connectivity,
        IReadOnlyList<RoutedCell> cells,
        double? seaLevel)
    {
        Dictionary<long, RoutedCell> byId = cells.ToDictionary(cell => cell.Id);
        foreach (DrainageConnectivity item in connectivity)
        {
            DrainageWaterKind kind = item.TerminalKind switch
            {
                DrainageTerminalKind.Ocean => DrainageWaterKind.OceanConnected,
                DrainageTerminalKind.EndorheicLake => DrainageWaterKind.ClosedLake,
                DrainageTerminalKind.DryBasin when seaLevel is double level && byId[item.TerminalCellId].PhysicalElevation < level
                    => DrainageWaterKind.SubmarineDryBasin,
                DrainageTerminalKind.DryBasin => DrainageWaterKind.DryBasin,
                DrainageTerminalKind.OutOfDomain => DrainageWaterKind.OutOfDomain,
                _ => throw new InvalidOperationException("Unknown drainage terminal kind."),
            };
            yield return new DrainageWaterState(item.CellId, item.TerminalCellId, kind);
        }
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
            if (!double.IsFinite(capacity))
                throw new ArgumentOutOfRangeException(nameof(physical), "Depression capacity exceeds the supported finite numeric range.");
            long rootId = nextId++;
            yield return new Depression(rootId, Array.AsReadOnly(component.ToArray()), spill, capacity, null);

            // Retain local cups under their common routed component. The parent is
            // analytical (and may overlap its children), as in Fill-Spill-Merge:
            // it does not rewrite physical cells or turn a lake into a cell exception.
            foreach (long[] minimumPlateau in FindLocalMinimumPlateaus(component, original))
            {
                double elevation = original[minimumPlateau[0]].PhysicalElevation;
                double localSpill = minimumPlateau
                    .SelectMany(id => original[id].Neighbours)
                    .Where(neighbour => original[neighbour].PhysicalElevation > elevation)
                    .Select(neighbour => original[neighbour].PhysicalElevation)
                    .DefaultIfEmpty(spill)
                    .Min();
                localSpill = Math.Min(localSpill, spill);
                double localCapacity = (localSpill - elevation) * minimumPlateau.Length;
                if (!double.IsFinite(localCapacity))
                    throw new ArgumentOutOfRangeException(nameof(physical), "Depression capacity exceeds the supported finite numeric range.");
                yield return new Depression(
                    nextId++,
                    Array.AsReadOnly(minimumPlateau),
                    localSpill,
                    localCapacity,
                    rootId);
            }
        }
    }

    private static IEnumerable<long[]> FindLocalMinimumPlateaus(
        IReadOnlyCollection<long> component,
        IReadOnlyDictionary<long, DrainageCell> original)
    {
        var unseen = new HashSet<long>(component);
        while (unseen.Count > 0)
        {
            long start = unseen.Min();
            double elevation = original[start].PhysicalElevation;
            var plateau = new List<long>();
            var pending = new Queue<long>();
            unseen.Remove(start); pending.Enqueue(start);
            while (pending.Count > 0)
            {
                long id = pending.Dequeue(); plateau.Add(id);
                foreach (long neighbour in original[id].Neighbours.Where(candidate =>
                    unseen.Contains(candidate) && original[candidate].PhysicalElevation == elevation).OrderBy(candidate => candidate))
                {
                    unseen.Remove(neighbour); pending.Enqueue(neighbour);
                }
            }

            plateau.Sort();
            if (plateau.All(id => original[id].Neighbours.Where(component.Contains)
                .All(neighbour => original[neighbour].PhysicalElevation >= elevation)))
            {
                yield return plateau.ToArray();
            }
        }
    }
}
