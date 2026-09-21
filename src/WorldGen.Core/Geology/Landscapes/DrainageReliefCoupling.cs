using System.Collections.ObjectModel;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Hydrology.Depressions;
using ISRWorldGen.Core.Hydrology.Discharge;

namespace ISRWorldGen.Core.Geology.Landscapes;

/// <summary>Explicit reduced-model step; reference length/flow do not depend on raster resolution.</summary>
public sealed record DrainageIncisionSettings
{
    public DrainageIncisionSettings(double stepStrength, double referenceLength, double referenceDischarge)
    {
        if (!double.IsFinite(stepStrength) || stepStrength <= 0d || stepStrength > 1d ||
            !double.IsFinite(referenceLength) || referenceLength <= 0d ||
            !double.IsFinite(referenceDischarge) || referenceDischarge <= 0d)
            throw new ArgumentOutOfRangeException(nameof(stepStrength));
        StepStrength = stepStrength; ReferenceLength = referenceLength; ReferenceDischarge = referenceDischarge;
    }
    public double StepStrength { get; }
    public double ReferenceLength { get; }
    public double ReferenceDischarge { get; }
}
public readonly record struct IncisionSample(long CellId, double Before, double After, double RemovedDepth);
public sealed class DrainageIncisionSnapshot
{
    internal DrainageIncisionSnapshot(IncisionSample[] samples, double removedVolume)
    { Samples = Array.AsReadOnly(samples); ExportedSedimentModelVolume = removedVolume; }
    public ReadOnlyCollection<IncisionSample> Samples { get; }
    public double ExportedSedimentModelVolume { get; }
    public string AlgorithmId => DrainageReliefCoupling.AlgorithmId;
}

/// <summary>
/// One implicit detachment-limited incision step, using an already acyclic,
/// geometrically routed graph and its actual discharge. Solve downstream first:
/// h' = (h + alpha*h'_receiver)/(1+alpha). Zero flow cannot erode. Marine cells,
/// explicit terminals and virtually filled depressions are left untouched.
/// Removed material is recorded as exported sediment, not deposited elsewhere.
/// This bounded preparatory model is NOT the full L06 sediment/lake solver and
/// must never regenerate a player's terrain or interpret virtual lakes as rivers.
/// </summary>
public static class DrainageReliefCoupling
{
    public const string AlgorithmId = "drainage-incision-v1-implicit-detachment";
    public static DrainageIncisionSnapshot Step(DrainageTopology topology, DischargeSnapshot discharge,
        IReadOnlyDictionary<long, WorldBlockPosition> positions, double cellArea, double seaLevel,
        DrainageIncisionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(topology); ArgumentNullException.ThrowIfNull(discharge);
        ArgumentNullException.ThrowIfNull(positions); ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(cellArea) || cellArea <= 0d || !double.IsFinite(seaLevel))
            throw new ArgumentOutOfRangeException(nameof(cellArea));
        var cells = topology.Cells.ToDictionary(cell => cell.Id);
        var flows = discharge.Reaches.ToDictionary(reach => reach.CellId);
        if (cells.Count != positions.Count || cells.Count != flows.Count ||
            cells.Keys.Any(id => !positions.ContainsKey(id) || !flows.ContainsKey(id)))
            throw new ArgumentException("Incision requires the exact topology, metric and discharge cell identities.");
        var heights = new Dictionary<long, double>();
        foreach (RoutedCell start in topology.Cells)
        {
            var path = new List<long>(); var seen = new HashSet<long>();
            RoutedCell current = start;
            while (!heights.ContainsKey(current.Id))
            {
                if (!seen.Add(current.Id)) throw new ArgumentException("Incision cannot consume cyclic routing.", nameof(topology));
                if (current.ReceiverId is not long next)
                { heights.Add(current.Id, current.PhysicalElevation); break; }
                path.Add(current.Id); current = cells[next];
            }
            for (int index = path.Count - 1; index >= 0; index--)
            {
                long id = path[index]; RoutedCell cell = cells[id];
                long receiver = cell.ReceiverId!.Value;
                double h = cell.PhysicalElevation, q = flows[id].DischargeModelVolumePerYear;
                if (!double.IsFinite(q) || q < 0d) throw new ArgumentException("Invalid incision discharge.", nameof(discharge));
                if (flows[id].ReceiverId != cell.ReceiverId)
                    throw new ArgumentException("Discharge belongs to a different routing snapshot.", nameof(discharge));
                double downstream = Math.Max(seaLevel, heights[receiver]);
                if (q == 0d || cell.RoutingElevation > h || h <= downstream)
                { heights.Add(id, h); continue; }
                double dx = (double)((decimal)positions[id].X - positions[receiver].X);
                double dz = (double)((decimal)positions[id].Z - positions[receiver].Z);
                double length = Math.Sqrt(dx * dx + dz * dz);
                if (!double.IsFinite(length) || length <= 0d) throw new ArgumentException("Invalid incision edge length.", nameof(positions));
                double alpha = settings.StepStrength * Math.Sqrt(q / settings.ReferenceDischarge) * settings.ReferenceLength / length;
                if (!double.IsFinite(alpha)) throw new ArgumentOutOfRangeException(nameof(settings), "Incision step exceeds finite domain.");
                double candidate = h - (h - downstream) * (alpha / (1d + alpha));
                if (!double.IsFinite(candidate) || candidate > h || candidate < downstream)
                    throw new InvalidOperationException("Implicit incision violated its monotone envelope.");
                heights.Add(id, candidate);
            }
        }
        IncisionSample[] samples = topology.Cells.Select(cell => new IncisionSample(cell.Id, cell.PhysicalElevation,
            heights[cell.Id], cell.PhysicalElevation - heights[cell.Id])).ToArray();
        double exported = samples.Sum(sample => sample.RemovedDepth * cellArea);
        if (!double.IsFinite(exported)) throw new OverflowException("Sediment accounting exceeded finite range.");
        return new DrainageIncisionSnapshot(samples, exported);
    }
}
