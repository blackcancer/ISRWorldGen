using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Plates;

/// <summary>Normalized procedural boundary classes; they are not claims of geophysical units.</summary>
public enum PlateBoundaryKind
{
    Quiescent = 0,
    Collision = 1,
    Divergence = 2,
    Shear = 3,
}

/// <summary>A finite velocity in normalized model-distance per model step, with magnitude at most one.</summary>
public readonly record struct PlateVelocity
{
    public PlateVelocity(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Plate velocity components must be finite.");
        }

        double magnitudeSquared = (x * x) + (z * z);
        if (!double.IsFinite(magnitudeSquared) || magnitudeSquared > 1 + 1e-12)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Plate velocity magnitude must be in [0, 1].");
        }

        X = x;
        Z = z;
    }

    public double X { get; }

    public double Z { get; }
}

/// <summary>A unit normal in the atlas X/Z plane.</summary>
public readonly record struct UnitDirection2
{
    private UnitDirection2(double x, double z)
    {
        X = x;
        Z = z;
    }

    public double X { get; }

    public double Z { get; }

    public static UnitDirection2 FromComponents(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Normal components must be finite.");
        }

        double length = Math.Sqrt((x * x) + (z * z));
        if (!double.IsFinite(length) || length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "A boundary normal must have a finite non-zero length.");
        }

        return new UnitDirection2(x / length, z / length);
    }

    public UnitDirection2 Negated() => new(-X, -Z);
}

public readonly record struct PlateKinematics(StableId PlateId, PlateVelocity Velocity);

/// <summary>
/// Boundary response under the convention delta-v = velocity(B) - velocity(A), normal directed A to B.
/// A negative normal component closes the boundary, a positive component opens it.
/// </summary>
public sealed record PlateBoundaryEffect
{
    internal PlateBoundaryEffect(
        PlateBoundaryKind kind,
        double normalRelativeVelocity,
        double tangentialRelativeSpeed,
        double intensityNormalized,
        double upliftNormalized,
        double subsidenceNormalized,
        double shearNormalized)
    {
        Kind = kind;
        NormalRelativeVelocity = CanonicalZero(normalRelativeVelocity);
        TangentialRelativeSpeed = CanonicalZero(tangentialRelativeSpeed);
        IntensityNormalized = CanonicalZero(intensityNormalized);
        UpliftNormalized = CanonicalZero(upliftNormalized);
        SubsidenceNormalized = CanonicalZero(subsidenceNormalized);
        ShearNormalized = CanonicalZero(shearNormalized);
        ContentChecksum = ComputeChecksum(this);
    }

    public PlateBoundaryKind Kind { get; }

    public double NormalRelativeVelocity { get; }

    public double TangentialRelativeSpeed { get; }

    public double IntensityNormalized { get; }

    public double UpliftNormalized { get; }

    public double SubsidenceNormalized { get; }

    public double ShearNormalized { get; }

    public Hash256 ContentChecksum { get; }

    private static double CanonicalZero(double value) => value == 0 ? 0 : value;

    private static Hash256 ComputeChecksum(PlateBoundaryEffect effect)
    {
        string canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"ISRW-PLATE-BOUNDARY-V1\n{(int)effect.Kind}\n" +
            $"{effect.NormalRelativeVelocity:R}\n{effect.TangentialRelativeSpeed:R}\n" +
            $"{effect.IntensityNormalized:R}\n{effect.UpliftNormalized:R}\n" +
            $"{effect.SubsidenceNormalized:R}\n{effect.ShearNormalized:R}\n");
        return Hash256.Compute(Encoding.UTF8.GetBytes(canonical));
    }
}

public static class PlateBoundaryEvaluator
{
    public const double ClassificationEpsilon = 1e-12;

    public static PlateBoundaryEffect Evaluate(
        PlateKinematics plateA,
        PlateKinematics plateB,
        UnitDirection2 normalAtoB)
    {
        if (plateA.PlateId == plateB.PlateId)
        {
            throw new ArgumentException("A plate boundary requires two distinct plate IDs.", nameof(plateB));
        }

        double normalLengthSquared = (normalAtoB.X * normalAtoB.X) + (normalAtoB.Z * normalAtoB.Z);
        if (!double.IsFinite(normalLengthSquared) || Math.Abs(normalLengthSquared - 1) > 1e-12)
        {
            throw new ArgumentOutOfRangeException(nameof(normalAtoB), "Boundary normal must be a finite unit direction.");
        }

        double deltaX = plateB.Velocity.X - plateA.Velocity.X;
        double deltaZ = plateB.Velocity.Z - plateA.Velocity.Z;
        double normal = (deltaX * normalAtoB.X) + (deltaZ * normalAtoB.Z);
        double tangent = (-deltaX * normalAtoB.Z) + (deltaZ * normalAtoB.X);
        double tangentMagnitude = Math.Abs(tangent);
        PlateBoundaryKind kind = normal switch
        {
            < -ClassificationEpsilon => PlateBoundaryKind.Collision,
            > ClassificationEpsilon => PlateBoundaryKind.Divergence,
            _ when tangentMagnitude > ClassificationEpsilon => PlateBoundaryKind.Shear,
            _ => PlateBoundaryKind.Quiescent,
        };

        double compression = Math.Clamp(-normal / 2, 0, 1);
        double opening = Math.Clamp(normal / 2, 0, 1);
        double shear = Math.Clamp(tangentMagnitude / 2, 0, 1);
        double intensity = kind switch
        {
            PlateBoundaryKind.Collision => compression,
            PlateBoundaryKind.Divergence => opening,
            PlateBoundaryKind.Shear => shear,
            _ => 0,
        };
        return new PlateBoundaryEffect(kind, normal, tangentMagnitude, intensity, compression, opening, shear);
    }
}
