using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Consecutive MATERIAL ribbons, not mechanical plates. Resistance is a
/// relative initial extensional membrane resistance (viscosity times thickness).
/// FailureStretch is an explicitly supplied constitutive threshold, NOT an Earth constant.</summary>
public sealed record RiftRibbon(int OriginId, double WidthReference, double CrustKm,
    double Resistance, double FailureStretch);

/// <summary>Boundary-normal velocities prescribed by an upstream mechanical history.
/// Increasing normal coordinate runs from the left boundary to the right boundary.
/// Closing phases are unsupported: a subduction/collision solver must handle them.</summary>
public sealed record RiftLoadingPhase(double StartMyr, double EndMyr,
    double LeftVelocity, double RightVelocity);

public sealed record RiftParcel(int OriginId, int Flank, double LeftReference,
    double RightReference, double CrustKm);

/// <summary>A single dated source on each conjugate flank. Start/End use forward
/// model time; the adapter shifts them relative to observation time, never to an age inferred from distance.</summary>
public sealed record RiftSpreadingPhase(int EventId, double StartMyr, double EndMyr,
    double RidgeAtStartReference, double RidgeVelocity,
    double LeftMaterialVelocity, double RightMaterialVelocity, bool CreatesOcean);

public sealed record RiftSection(double TimeMyr, double LoadCoordinate,
    double? BreakupTimeMyr, int? RupturedOriginId, ReadOnlyCollection<RiftParcel> Parcels,
    double GapLeftReference, double GapRightReference, double? RidgeReference,
    double ContinentalVolumeKm3, double NewOceanVolumeKm3, double NewOceanAreaKm2,
    string Status);

/// <summary>
/// Controlled plane-strain necking of a material transect under a spatially uniform
/// extensional membrane traction, with imposed boundary velocities. For ribbon i,
/// w_i = w_i0/(1-lambda/R_i), h_i = h_i0*(1-lambda/R_i).
/// This is the integral of Newtonian incompressible stretching with fixed viscosity:
/// d(log w_i)/dt is proportional to common traction/(viscosity_i*h_i).
/// Thus weakness concentrates thinning while EACH ribbon conserves continental volume.
/// The first constitutive failure opens a zero-area seam; no residual continent is deleted.
/// Subsequent separation creates ocean in the gap and dates its two conjugate flanks.
///
/// Deliberately not a global plate solver, not a calibrated breakup criterion, not
/// a world initializer, not an erosion or mantle-melting simulation. Post-breakup
/// continental fragments translate rigidly in this experiment. No crust is created
/// before separation, and no inherited seafloor receives an invented birth date.
/// </summary>
public sealed class RiftNecking
{
    public const string AlgorithmId = "conservative-ribbon-necking-to-conjugate-spreading-v1";
    private const double FailureTieResolution = 1e-12;
    private readonly RiftRibbon[] ribbons;
    private readonly RiftLoadingPhase[] phases;
    private readonly double[] breakupWidths, breakupHeights;
    private readonly int failureIndex;
    private readonly double initialLeft, alongLength, kmPerReference, newOceanKm;
    private readonly double ridgeFraction;
    private readonly int firstEventId;
    public ReadOnlyCollection<RiftRibbon> Ribbons { get; }
    public ReadOnlyCollection<RiftLoadingPhase> Loading { get; }
    public double AlongRiftLengthReference => alongLength;
    public double ReferenceKmPerUnit => kmPerReference;
    public double NewOceanicThicknessKm => newOceanKm;
    public double InitialWidthReference { get; }
    public double CriticalOpeningReference { get; }
    public double CriticalLoadCoordinate { get; }
    public double? BreakupTimeMyr { get; }
    public double ObservationTimeMyr => phases[^1].EndMyr;
    public string Checksum { get; }

    public RiftNecking(IReadOnlyList<RiftRibbon> material, IReadOnlyList<RiftLoadingPhase> loading,
        double initialLeftReference, double alongRiftLengthReference, double referenceKmPerUnit,
        double oceanicThicknessKm, double ridgeMotionFraction = .5, int firstSourceEventId = 0,
        int? explicitlySelectedFailureOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(material); ArgumentNullException.ThrowIfNull(loading);
        if (material.Count is < 1 or > 4096 || loading.Count is < 1 or > 128
            || !Finite(initialLeftReference) || !Positive(alongRiftLengthReference)
            || !Positive(referenceKmPerUnit) || !Positive(oceanicThicknessKm)
            || !Finite(ridgeMotionFraction) || ridgeMotionFraction <= 0 || ridgeMotionFraction >= 1
            || firstSourceEventId < 0 || firstSourceEventId > int.MaxValue - loading.Count)
            throw new ArgumentException("Invalid bounded rift geometry, units or event identity.");
        ribbons = material.ToArray(); phases = loading.ToArray();
        if (ribbons.Any(r => r is null || r.OriginId < 0 || !Positive(r.WidthReference)
                || !Positive(r.CrustKm) || !Positive(r.Resistance) || !Finite(r.FailureStretch)
                || r.FailureStretch <= 1 || r.FailureStretch > 100)
            || ribbons.Select(r => r.OriginId).Distinct().Count() != ribbons.Length)
            throw new ArgumentException("Ribbons need unique material origins and valid constitutive data.");
        for (int i = 0; i < phases.Length; i++)
        {
            var p = phases[i];
            if (p is null || !Finite(p.StartMyr) || !Finite(p.EndMyr) || p.StartMyr < 0
                || p.EndMyr <= p.StartMyr || !Finite(p.LeftVelocity) || !Finite(p.RightVelocity)
                || p.RightVelocity < p.LeftVelocity || (i > 0 && phases[i - 1].EndMyr != p.StartMyr))
                throw new ArgumentException("Unordered, closing, overlapping or incomplete motion history.");
        }
        if (phases[0].StartMyr != 0) throw new ArgumentException("The material state is defined at t=0.");
        // Subdividing an unchanged loading interval must not invent extra sources.
        var canonicalPhases = new List<RiftLoadingPhase>();
        foreach (var p in phases)
        {
            if (canonicalPhases.Count > 0 && canonicalPhases[^1].LeftVelocity == p.LeftVelocity
                && canonicalPhases[^1].RightVelocity == p.RightVelocity)
                canonicalPhases[^1] = canonicalPhases[^1] with { EndMyr = p.EndMyr };
            else canonicalPhases.Add(p);
        }
        phases = canonicalPhases.ToArray();
        initialLeft = initialLeftReference; alongLength = alongRiftLengthReference;
        kmPerReference = referenceKmPerUnit; newOceanKm = oceanicThicknessKm;
        ridgeFraction = ridgeMotionFraction; firstEventId = firstSourceEventId;
        InitialWidthReference = Sum(ribbons.Select(r => r.WidthReference));
        var failureLoads = ribbons.Select(r => r.Resistance * (1 - 1 / r.FailureStretch)).ToArray();
        CriticalLoadCoordinate = failureLoads.Min();
        if (!Positive(CriticalLoadCoordinate) || CriticalLoadCoordinate >= ribbons.Min(r => r.Resistance))
            throw new ArithmeticException("Unrepresentable failure before the constitutive singularity.");
        var candidates = Enumerable.Range(0, ribbons.Length).Where(i =>
            (failureLoads[i] - CriticalLoadCoordinate) / CriticalLoadCoordinate <= FailureTieResolution).ToArray();
        failureIndex = candidates[0];
        (breakupWidths, breakupHeights) = Deformed(CriticalLoadCoordinate);
        CriticalOpeningReference = Sum(breakupWidths) - InitialWidthReference;
        if (!Positive(CriticalOpeningReference)) throw new ArithmeticException("Unrepresentable critical opening.");
        double opened = 0;
        foreach (var p in phases)
        {
            double speed = p.RightVelocity - p.LeftVelocity;
            double addition = speed * (p.EndMyr - p.StartMyr);
            if (!Finite(addition) || !Finite(opened + addition)) throw new ArithmeticException("Motion overflow.");
            if (speed > 0 && opened + addition >= CriticalOpeningReference)
            {
                BreakupTimeMyr = p.StartMyr + (CriticalOpeningReference - opened) / speed;
                break;
            }
            opened += addition;
        }
        if (explicitlySelectedFailureOrigin is { } selected)
        {
            var matches = candidates.Where(i => ribbons[i].OriginId == selected).ToArray();
            if (matches.Length != 1) throw new ArgumentException("Selected rupture is not a co-critical material.");
            failureIndex = matches[0];
        }
        else if (BreakupTimeMyr is not null && candidates.Length > 1)
            throw new ArgumentException("Simultaneous failures need explicit topology; material ID is not a rupture oracle.");
        Ribbons = Array.AsReadOnly(ribbons); Loading = Array.AsReadOnly(phases);
        Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            AlgorithmId, ribbons, phases, initialLeft, alongLength, kmPerReference, newOceanKm,
            ridgeFraction, firstEventId, explicitlySelectedFailureOrigin })))).ToLowerInvariant();
        // Validate final displacement and volume now, including long post-breakup phases.
        _ = Sample(ObservationTimeMyr);
    }

    public RiftSection Sample(double timeMyr)
    {
        if (!Finite(timeMyr) || timeMyr < 0 || timeMyr > ObservationTimeMyr)
            throw new ArgumentOutOfRangeException(nameof(timeMyr));
        bool broken = BreakupTimeMyr is { } t && timeMyr >= t;
        double deformationTime = broken ? BreakupTimeMyr!.Value : timeMyr;
        var d = Displacement(0, deformationTime);
        double target = InitialWidthReference + d.Right - d.Left;
        if (!Positive(target)) throw new ArithmeticException("Invalid deformed extent.");
        double lambda = broken ? CriticalLoadCoordinate : SolveLoad(target);
        var (widths, heights) = broken ? (breakupWidths, breakupHeights) : Deformed(lambda);
        var shift = broken ? Displacement(BreakupTimeMyr!.Value, timeMyr) : (Left: 0d, Right: 0d);
        double x = initialLeft + d.Left;
        var output = new List<RiftParcel>(ribbons.Length + 1);
        double seam = x;
        for (int i = 0; i < ribbons.Length; i++)
        {
            double next = x + widths[i];
            if (i == failureIndex) seam = x + widths[i] / 2;
            if (!broken) output.Add(new(ribbons[i].OriginId, 0, x, next, heights[i]));
            else if (i == failureIndex)
            {
                output.Add(new(ribbons[i].OriginId, -1, x + shift.Left, seam + shift.Left, heights[i]));
                output.Add(new(ribbons[i].OriginId, 1, seam + shift.Right, next + shift.Right, heights[i]));
            }
            else
            {
                double offset = i < failureIndex ? shift.Left : shift.Right;
                output.Add(new(ribbons[i].OriginId, i < failureIndex ? -1 : 1, x + offset, next + offset, heights[i]));
            }
            x = next;
        }
        double left = seam + shift.Left, right = seam + shift.Right;
        double ridge = seam + (1 - ridgeFraction) * shift.Left + ridgeFraction * shift.Right;
        double areaFactor = alongLength * kmPerReference * kmPerReference;
        if (!Positive(areaFactor)) throw new ArithmeticException("Unrepresentable section area metric.");
        double volume = Sum(output.Select(p => (p.RightReference - p.LeftReference) * p.CrustKm)) * areaFactor;
        double original = Sum(ribbons.Select(r => r.WidthReference * r.CrustKm)) * areaFactor;
        RequireEqual(original, volume, "continental volume");
        foreach (var r in ribbons)
        {
            double now = Sum(output.Where(p => p.OriginId == r.OriginId).Select(p =>
                (p.RightReference - p.LeftReference) * p.CrustKm));
            RequireEqual(r.WidthReference * r.CrustKm, now, "material-origin volume");
        }
        double oceanArea = broken ? (right - left) * areaFactor : 0;
        double oceanVolume = oceanArea * newOceanKm;
        if (output.Any(p => !Finite(p.LeftReference) || !Finite(p.RightReference) || p.RightReference <= p.LeftReference)
            || !Finite(ridge) || !Finite(oceanArea) || !Finite(oceanVolume) || oceanArea < 0)
            throw new ArithmeticException("Section geometry or volume overflow; no clipping.");
        return new(timeMyr, lambda, BreakupTimeMyr, broken ? ribbons[failureIndex].OriginId : null,
            output.AsReadOnly(), left, right, broken ? ridge : null, volume, oceanVolume, oceanArea,
            broken ? "CONTROLLED_BREAKUP_NOT_GLOBAL_TECTONICS" : "CONNECTED_CONTINENTAL_RIFT_NO_OCEAN_SOURCE");
    }

    /// <summary>Time-ordered sources including dormant post-breakup motion. No event exists before breakup.</summary>
    public ReadOnlyCollection<RiftSpreadingPhase> SpreadingPhases()
    {
        var result = new List<RiftSpreadingPhase>();
        if (BreakupTimeMyr is not { } breakup || breakup >= ObservationTimeMyr) return result.AsReadOnly();
        for (int i = 0; i < phases.Length; i++)
        {
            var p = phases[i]; double start = Math.Max(breakup, p.StartMyr);
            if (start >= p.EndMyr) continue;
            var sample = Sample(start);
            bool active = p.RightVelocity > p.LeftVelocity;
            double velocity = (1 - ridgeFraction) * p.LeftVelocity + ridgeFraction * p.RightVelocity;
            result.Add(new(active ? firstEventId + i : -1, start, p.EndMyr,
                sample.RidgeReference!.Value, velocity, p.LeftVelocity, p.RightVelocity, active));
        }
        double formedArea = Sum(result.Where(p => p.CreatesOcean).Select(p =>
            (p.RightMaterialVelocity - p.LeftMaterialVelocity) * (p.EndMyr - p.StartMyr)))
            * alongLength * kmPerReference * kmPerReference;
        RequireEqual(Sample(ObservationTimeMyr).NewOceanAreaKm2, formedArea, "conjugate spreading area");
        return result.AsReadOnly();
    }

    private (double Left, double Right) Displacement(double from, double to)
    {
        double left = 0, right = 0;
        foreach (var p in phases)
        {
            double dt = Math.Max(0, Math.Min(to, p.EndMyr) - Math.Max(from, p.StartMyr));
            left += p.LeftVelocity * dt; right += p.RightVelocity * dt;
        }
        if (!Finite(left) || !Finite(right)) throw new ArithmeticException("Integrated displacement overflow.");
        return (left, right);
    }

    private double SolveLoad(double target)
    {
        if (target == InitialWidthReference) return 0;
        double lo = 0, hi = CriticalLoadCoordinate;
        for (int iteration = 0; iteration < 96; iteration++)
        {
            double mid = lo + (hi - lo) / 2;
            if (mid == lo || mid == hi) break;
            double width = Sum(ribbons.Select(r => r.WidthReference / (1 - mid / r.Resistance)));
            if (width < target) lo = mid; else hi = mid;
        }
        double result = lo + (hi - lo) / 2;
        RequireEqual(target, Sum(Deformed(result).Widths), "imposed boundary separation");
        return result;
    }

    private (double[] Widths, double[] Heights) Deformed(double lambda)
    {
        double[] widths = new double[ribbons.Length], heights = new double[ribbons.Length];
        for (int i = 0; i < ribbons.Length; i++)
        {
            double f = 1 - lambda / ribbons[i].Resistance;
            if (!Positive(f)) throw new ArithmeticException("Constitutive singularity.");
            widths[i] = ribbons[i].WidthReference / f; heights[i] = ribbons[i].CrustKm * f;
            if (!Positive(widths[i]) || !Positive(heights[i])) throw new ArithmeticException("Unrepresentable material deformation.");
        }
        return (widths, heights);
    }
    private static bool Finite(double v) => double.IsFinite(v);
    private static bool Positive(double v) => Finite(v) && v > 0;
    private static void RequireEqual(double expected, double actual, string field)
    {
        if (!Positive(expected) && expected != 0 || !Finite(actual)
            || Math.Abs(expected - actual) > 2e-11 * Math.Max(1, Math.Abs(expected)))
            throw new ArithmeticException("Rift invariant failed: " + field);
    }
    private static double Sum(IEnumerable<double> values)
    {
        double sum = 0, correction = 0;
        foreach (double v in values) { double y = v - correction, t = sum + y; correction = (t - sum) - y; sum = t; }
        if (!Finite(sum)) throw new ArithmeticException("Rift reduction overflow.");
        return sum;
    }
}
