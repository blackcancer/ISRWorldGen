using System.Collections.ObjectModel;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Hydrology.Boundaries;

/// <summary>Axis of a shared region face. Region identifiers are always stored in ascending order.</summary>
public enum BoundaryAxis : byte { EastWest = 0, NorthSouth = 1 }

/// <summary>Canonical shared face between exactly two technical regions.</summary>
public readonly record struct BoundaryPlane(long FirstRegionId, long SecondRegionId, BoundaryAxis Axis)
{
    public BoundaryPlane Canonicalize()
    {
        if (!Enum.IsDefined(Axis) || FirstRegionId == SecondRegionId)
            throw new ArgumentOutOfRangeException(nameof(Axis), "A boundary plane needs two distinct regions and a known axis.");
        return FirstRegionId < SecondRegionId ? this : new BoundaryPlane(SecondRegionId, FirstRegionId, Axis);
    }
}

/// <summary>Quantized global face coordinate, shared verbatim by both consumers.</summary>
public readonly record struct BoundaryCrossing(long GlobalXQuantized, long GlobalZQuantized);

/// <summary>Quantized, non-pressurised channel geometry at a shared crossing.</summary>
public readonly record struct BoundaryChannelProfile(long BedLevelQuantized, int WidthQuantized, int DepthQuantized)
{
    public void Validate(long waterLevelQuantized)
    {
        if (WidthQuantized <= 0 || DepthQuantized <= 0 || BedLevelQuantized > waterLevelQuantized)
            throw new ArgumentOutOfRangeException(nameof(BedLevelQuantized), "A wet boundary profile requires positive width/depth and a bed at or below water.");
    }
}

/// <summary>Inclusive global corridor that protects a coarse published crossing during refinement.</summary>
public readonly record struct BoundaryCorridor(long MinimumXQuantized, long MaximumXQuantized, long MinimumZQuantized, long MaximumZQuantized)
{
    public void Validate()
    {
        if (MinimumXQuantized > MaximumXQuantized || MinimumZQuantized > MaximumZQuantized)
            throw new ArgumentOutOfRangeException(nameof(MinimumXQuantized), "A boundary corridor must have ordered inclusive bounds.");
    }

    public bool Contains(BoundaryCrossing crossing) => crossing.GlobalXQuantized >= MinimumXQuantized && crossing.GlobalXQuantized <= MaximumXQuantized &&
        crossing.GlobalZQuantized >= MinimumZQuantized && crossing.GlobalZQuantized <= MaximumZQuantized;
}

/// <summary>Input owned by L05-C until a shared BasinPlan contract is introduced.</summary>
public sealed record BoundaryPortRequest(
    StableId OwnerId,
    ulong OwnerLocalIndex,
    BoundaryPlane Plane,
    BoundaryCrossing Crossing,
    long WaterLevelQuantized,
    BoundaryChannelProfile Profile,
    double ReferenceFlowModelVolumePerYear,
    BoundaryCorridor ProtectedCorridor,
    int Iteration,
    StableId ReliefSignature);

/// <summary>Immutable published hand-off. PortId is derived only from stable owner identity and local index.</summary>
public sealed record BoundaryPort(
    StableId PortId,
    StableId OwnerId,
    ulong OwnerLocalIndex,
    BoundaryPlane Plane,
    BoundaryCrossing Crossing,
    long WaterLevelQuantized,
    BoundaryChannelProfile Profile,
    double ReferenceFlowModelVolumePerYear,
    BoundaryCorridor ProtectedCorridor,
    int Iteration,
    StableId ReliefSignature);

/// <summary>Canonical, immutable boundary publication for consumers that can be generated in any region order.</summary>
public sealed class BoundaryPortSnapshot
{
    internal BoundaryPortSnapshot(int iteration, StableId reliefSignature, IEnumerable<BoundaryPort> ports)
    {
        Iteration = iteration;
        ReliefSignature = reliefSignature;
        Ports = Array.AsReadOnly(ports.OrderBy(port => port.PortId.High).ThenBy(port => port.PortId.Low).ToArray());
    }

    public const int AlgorithmVersion = 1;
    public int Iteration { get; }
    public StableId ReliefSignature { get; }
    public ReadOnlyCollection<BoundaryPort> Ports { get; }
}

public static class BoundaryPortPublisher
{
    public static BoundaryPortSnapshot Publish(int iteration, StableId reliefSignature, IEnumerable<BoundaryPortRequest> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (iteration < 0) throw new ArgumentOutOfRangeException(nameof(iteration), "The preparatory iteration must be non-negative.");
        BoundaryPortRequest[] values = source.ToArray();
        if (values.Length == 0 || values.Any(value => value is null)) throw new ArgumentException("At least one non-null boundary port is required.", nameof(source));

        var ports = new List<BoundaryPort>(values.Length);
        foreach (BoundaryPortRequest request in values)
        {
            if (request.Iteration != iteration || request.ReliefSignature != reliefSignature || !double.IsFinite(request.ReferenceFlowModelVolumePerYear) || request.ReferenceFlowModelVolumePerYear < 0d)
                throw new ArgumentOutOfRangeException(nameof(source), "Every port must use this finite non-negative flow and exact iteration/relief provenance.");
            BoundaryPlane plane = request.Plane.Canonicalize();
            request.Profile.Validate(request.WaterLevelQuantized);
            request.ProtectedCorridor.Validate();
            if (!request.ProtectedCorridor.Contains(request.Crossing))
                throw new ArgumentException("A published crossing must be inside its protected corridor.", nameof(source));
            ports.Add(new BoundaryPort(StableId.Derive(RandomDomain.Hydrology, request.OwnerId, request.OwnerLocalIndex), request.OwnerId, request.OwnerLocalIndex,
                plane, request.Crossing, request.WaterLevelQuantized, request.Profile, request.ReferenceFlowModelVolumePerYear, request.ProtectedCorridor, iteration, reliefSignature));
        }
        if (ports.Select(port => port.PortId).Distinct().Count() != ports.Count)
            throw new ArgumentException("Boundary port identities must be unique.", nameof(source));
        return new BoundaryPortSnapshot(iteration, reliefSignature, ports);
    }
}
