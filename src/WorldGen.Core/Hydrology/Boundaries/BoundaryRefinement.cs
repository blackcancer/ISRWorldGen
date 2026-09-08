using System.Collections.ObjectModel;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Hydrology.Boundaries;

/// <summary>A fine crossing allocated from one immutable coarse port budget.</summary>
public sealed record BoundaryRefinementChild(StableId ParentPortId, BoundaryPort ChildPort);

/// <summary>
/// Immutable conservation result. The retained flow is deliberately explicit: fine tributaries
/// may consume only part of a coarse budget without silently inventing or losing water.
/// </summary>
public sealed record BoundaryRefinementAllocation(StableId ParentPortId, double ChildFlowModelVolumePerYear, double RetainedParentFlowModelVolumePerYear);

public sealed class BoundaryRefinementSnapshot
{
    internal BoundaryRefinementSnapshot(BoundaryPortSnapshot parentSnapshot, IEnumerable<BoundaryRefinementChild> children, IEnumerable<BoundaryRefinementAllocation> allocations)
    {
        ParentSnapshot = parentSnapshot;
        Children = Array.AsReadOnly(children.OrderBy(child => child.ParentPortId.High).ThenBy(child => child.ParentPortId.Low)
            .ThenBy(child => child.ChildPort.PortId.High).ThenBy(child => child.ChildPort.PortId.Low).ToArray());
        Allocations = Array.AsReadOnly(allocations.OrderBy(allocation => allocation.ParentPortId.High).ThenBy(allocation => allocation.ParentPortId.Low).ToArray());
    }

    public const int AlgorithmVersion = 1;
    public BoundaryPortSnapshot ParentSnapshot { get; }
    public ReadOnlyCollection<BoundaryRefinementChild> Children { get; }
    public ReadOnlyCollection<BoundaryRefinementAllocation> Allocations { get; }
}

public static class BoundaryRefinementPlanner
{
    public static BoundaryRefinementSnapshot Refine(BoundaryPortSnapshot parentSnapshot, IEnumerable<BoundaryRefinementChild> source)
    {
        ArgumentNullException.ThrowIfNull(parentSnapshot);
        ArgumentNullException.ThrowIfNull(source);
        BoundaryRefinementChild[] children = source.ToArray();
        if (children.Any(child => child is null || child.ChildPort is null)) throw new ArgumentException("Refinement children cannot be null.", nameof(source));
        Dictionary<StableId, BoundaryPort> parents = parentSnapshot.Ports.ToDictionary(port => port.PortId);
        if (children.Select(child => child.ChildPort.PortId).Distinct().Count() != children.Length)
            throw new ArgumentException("Fine boundary port identities must be unique.", nameof(source));

        foreach (BoundaryRefinementChild child in children)
        {
            if (!parents.TryGetValue(child.ParentPortId, out BoundaryPort? parent)) throw new ArgumentException("Each fine port requires a known published parent.", nameof(source));
            BoundaryPort fine = child.ChildPort;
            if (fine.PortId == parent.PortId || fine.Iteration != parentSnapshot.Iteration || fine.ReliefSignature != parentSnapshot.ReliefSignature ||
                fine.PortId != StableId.Derive(RandomDomain.Hydrology, fine.OwnerId, fine.OwnerLocalIndex) || fine.Plane != fine.Plane.Canonicalize() ||
                fine.Plane != parent.Plane || !double.IsFinite(fine.ReferenceFlowModelVolumePerYear) || fine.ReferenceFlowModelVolumePerYear < 0d)
                throw new ArgumentException("Fine ports must retain canonical identity, provenance, plane, and finite non-negative flow.", nameof(source));
            fine.Profile.Validate(fine.WaterLevelQuantized);
            fine.ProtectedCorridor.Validate();
            if (!fine.ProtectedCorridor.Contains(fine.Crossing) || !parent.ProtectedCorridor.Contains(fine.Crossing))
                throw new ArgumentException("A fine crossing must lie in both its own and its published parent corridor.", nameof(source));
        }
        // The input is an untrusted enumeration.  This canonical total order is established
        // after validation and before any floating-point reduction or allocation decision.
        children = children.OrderBy(child => child.ParentPortId.High).ThenBy(child => child.ParentPortId.Low)
            .ThenBy(child => child.ChildPort.PortId.High).ThenBy(child => child.ChildPort.PortId.Low).ToArray();

        var allocations = new List<BoundaryRefinementAllocation>(parents.Count);
        foreach (BoundaryPort parent in parentSnapshot.Ports)
        {
            double childFlow = SumFinite(children.Where(child => child.ParentPortId == parent.PortId).Select(child => child.ChildPort.ReferenceFlowModelVolumePerYear), nameof(source));
            double retained = parent.ReferenceFlowModelVolumePerYear - childFlow;
            if (!double.IsFinite(retained) || retained < 0d)
                throw new ArgumentException("Fine crossings cannot exceed their published parent flow budget.", nameof(source));
            allocations.Add(new BoundaryRefinementAllocation(parent.PortId, childFlow, retained));
        }
        return new BoundaryRefinementSnapshot(parentSnapshot, children, allocations);
    }

    private static double SumFinite(IEnumerable<double> values, string parameter)
    {
        double sum = 0d;
        foreach (double value in values)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "Flow terms must be finite.");
            sum += value;
            if (!double.IsFinite(sum)) throw new ArgumentOutOfRangeException(parameter, "Flow accumulation exceeds the finite numeric domain.");
        }
        return sum;
    }
}
