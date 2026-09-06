using System.Globalization;
using System.Numerics;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Geometry;

/// <summary>
/// Canonical exact rational used by geometric construction. The denominator is always positive and
/// numerator/denominator are reduced by their greatest common divisor; no epsilon participates in topology.
/// </summary>
public readonly struct ExactRational : IEquatable<ExactRational>, IComparable<ExactRational>
{
    private readonly BigInteger numerator;
    private readonly BigInteger denominator;

    public ExactRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException("An exact rational denominator cannot be zero.");
        }

        if (denominator.Sign < 0)
        {
            numerator = BigInteger.Negate(numerator);
            denominator = BigInteger.Negate(denominator);
        }

        if (numerator.IsZero)
        {
            this.numerator = BigInteger.Zero;
            this.denominator = BigInteger.One;
            return;
        }

        BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        this.numerator = numerator / divisor;
        this.denominator = denominator / divisor;
    }

    public BigInteger Numerator => numerator;

    /// <summary>The canonical positive denominator; the default struct value is the valid rational zero.</summary>
    public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;

    public static ExactRational Zero { get; } = new(BigInteger.Zero, BigInteger.One);

    public static ExactRational One { get; } = new(BigInteger.One, BigInteger.One);

    public int Sign => Numerator.Sign;

    public static ExactRational FromInt64(long value) => new(value, BigInteger.One);

    public static ExactRational operator +(ExactRational left, ExactRational right) => new(
        (left.Numerator * right.Denominator) + (right.Numerator * left.Denominator),
        left.Denominator * right.Denominator);

    public static ExactRational operator -(ExactRational left, ExactRational right) => new(
        (left.Numerator * right.Denominator) - (right.Numerator * left.Denominator),
        left.Denominator * right.Denominator);

    public static ExactRational operator -(ExactRational value) => new(
        BigInteger.Negate(value.Numerator),
        value.Denominator);

    public static ExactRational operator *(ExactRational left, ExactRational right) => new(
        left.Numerator * right.Numerator,
        left.Denominator * right.Denominator);

    public static ExactRational operator /(ExactRational left, ExactRational right)
    {
        if (right.Numerator.IsZero)
        {
            throw new DivideByZeroException("Cannot divide an exact rational by zero.");
        }

        return new ExactRational(
            left.Numerator * right.Denominator,
            left.Denominator * right.Numerator);
    }

    public static bool operator <(ExactRational left, ExactRational right) => left.CompareTo(right) < 0;

    public static bool operator >(ExactRational left, ExactRational right) => left.CompareTo(right) > 0;

    public static bool operator <=(ExactRational left, ExactRational right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ExactRational left, ExactRational right) => left.CompareTo(right) >= 0;

    public static bool operator ==(ExactRational left, ExactRational right) => left.Equals(right);

    public static bool operator !=(ExactRational left, ExactRational right) => !left.Equals(right);

    public int CompareTo(ExactRational other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public bool Equals(ExactRational other) =>
        Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is ExactRational other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public double ToDouble()
    {
        double value = (double)Numerator / (double)Denominator;
        return double.IsFinite(value)
            ? value
            : throw new OverflowException("Exact rational is outside finite double range.");
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Numerator}/{Denominator}");
}

public readonly record struct ExactPoint(ExactRational X, ExactRational Z) : IComparable<ExactPoint>
{
    public static ExactPoint FromInt64(long x, long z) => new(
        ExactRational.FromInt64(x),
        ExactRational.FromInt64(z));

    public int CompareTo(ExactPoint other)
    {
        int x = X.CompareTo(other.X);
        return x != 0 ? x : Z.CompareTo(other.Z);
    }
}

/// <summary>Finite planar bounds. Site ownership is semi-open; geometric clipping uses the closed boundary planes.</summary>
public sealed record WorldBounds
{
    public WorldBounds(long minX, long minZ, long maxXExclusive, long maxZExclusive)
    {
        if (minX >= maxXExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxXExclusive), "The X interval must be non-empty and semi-open.");
        }

        if (minZ >= maxZExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxZExclusive), "The Z interval must be non-empty and semi-open.");
        }

        MinX = minX;
        MinZ = minZ;
        MaxXExclusive = maxXExclusive;
        MaxZExclusive = maxZExclusive;
        Width = checked(maxXExclusive - minX);
        Length = checked(maxZExclusive - minZ);
        Area = new ExactRational((BigInteger)Width * Length, BigInteger.One);
    }

    public long MinX { get; }

    public long MinZ { get; }

    public long MaxXExclusive { get; }

    public long MaxZExclusive { get; }

    public long Width { get; }

    public long Length { get; }

    public ExactRational Area { get; }

    public bool Contains(long x, long z) =>
        x >= MinX && x < MaxXExclusive && z >= MinZ && z < MaxZExclusive;
}

public readonly record struct AtlasSite(StableId Id, long X, long Z);

public enum GeometryCacheMode
{
    Cold = 0,
    Precomputed = 1,
}

public sealed record AtlasGeometryBuildOptions
{
    public AtlasGeometryBuildOptions(int workers, GeometryCacheMode cacheMode)
    {
        if (workers is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(workers), workers, "Workers must be in [1, 256].");
        }

        if (!Enum.IsDefined(cacheMode))
        {
            throw new ArgumentOutOfRangeException(nameof(cacheMode), cacheMode, "Unknown geometry cache mode.");
        }

        Workers = workers;
        CacheMode = cacheMode;
    }

    public int Workers { get; }

    public GeometryCacheMode CacheMode { get; }

    public static AtlasGeometryBuildOptions Default { get; } = new(1, GeometryCacheMode.Cold);
}

[Flags]
public enum WorldBoundaryMask
{
    None = 0,
    MinX = 1,
    MaxX = 2,
    MinZ = 4,
    MaxZ = 8,
}

public enum BoundarySide
{
    Left = 0,
    Right = 1,
    Bottom = 2,
    Top = 3,
}

public readonly record struct BoundaryFieldSample(
    ExactPoint Position,
    ExactRational CanonicalValue,
    ulong ValueBits);

/// <summary>A smooth polynomial evaluated in global coordinates with an exact rational canonical value.</summary>
public sealed class SmoothField2D
{
    private readonly BigInteger constant;
    private readonly BigInteger xLinear;
    private readonly BigInteger zLinear;
    private readonly BigInteger xSquared;
    private readonly BigInteger xz;
    private readonly BigInteger zSquared;
    private readonly BigInteger divisor;

    public SmoothField2D(
        long constant,
        long xLinear,
        long zLinear,
        long xSquared,
        long xz,
        long zSquared,
        long divisor)
    {
        if (divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(divisor), divisor, "Field divisor must be positive.");
        }

        this.constant = constant;
        this.xLinear = xLinear;
        this.zLinear = zLinear;
        this.xSquared = xSquared;
        this.xz = xz;
        this.zSquared = zSquared;
        this.divisor = divisor;
    }

    public ExactRational Evaluate(ExactPoint point)
    {
        ExactRational x = point.X;
        ExactRational z = point.Z;
        ExactRational numerator =
            Scale(constant, ExactRational.One) +
            Scale(xLinear, x) +
            Scale(zLinear, z) +
            Scale(xSquared, x * x) +
            Scale(xz, x * z) +
            Scale(zSquared, z * z);
        return numerator / new ExactRational(divisor, BigInteger.One);
    }

    public BoundaryFieldSample SampleBoundary(WorldBounds tile, BoundarySide side, long alongCoordinate)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ExactPoint point = side switch
        {
            BoundarySide.Left when alongCoordinate >= tile.MinZ && alongCoordinate <= tile.MaxZExclusive =>
                ExactPoint.FromInt64(tile.MinX, alongCoordinate),
            BoundarySide.Right when alongCoordinate >= tile.MinZ && alongCoordinate <= tile.MaxZExclusive =>
                ExactPoint.FromInt64(tile.MaxXExclusive, alongCoordinate),
            BoundarySide.Bottom when alongCoordinate >= tile.MinX && alongCoordinate <= tile.MaxXExclusive =>
                ExactPoint.FromInt64(alongCoordinate, tile.MinZ),
            BoundarySide.Top when alongCoordinate >= tile.MinX && alongCoordinate <= tile.MaxXExclusive =>
                ExactPoint.FromInt64(alongCoordinate, tile.MaxZExclusive),
            _ => throw new ArgumentOutOfRangeException(
                nameof(alongCoordinate),
                alongCoordinate,
                "Boundary coordinate must lie on the selected closed geometric edge."),
        };
        ExactRational canonical = Evaluate(point);
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(canonical.ToDouble()));
        return new BoundaryFieldSample(point, canonical, bits);
    }

    private static ExactRational Scale(BigInteger coefficient, ExactRational value) => new(
        coefficient * value.Numerator,
        value.Denominator);
}

public static class GeometryPredicates
{
    public static int Orientation(AtlasSite a, AtlasSite b, AtlasSite c)
    {
        BigInteger abx = (BigInteger)b.X - a.X;
        BigInteger abz = (BigInteger)b.Z - a.Z;
        BigInteger acx = (BigInteger)c.X - a.X;
        BigInteger acz = (BigInteger)c.Z - a.Z;
        return ((abx * acz) - (abz * acx)).Sign;
    }

    /// <summary>Positive only when d lies strictly inside the oriented-CCW circumcircle of a,b,c.</summary>
    public static int InCircle(AtlasSite a, AtlasSite b, AtlasSite c, AtlasSite d)
    {
        int orientation = Orientation(a, b, c);
        if (orientation == 0)
        {
            return 0;
        }

        BigInteger adx = (BigInteger)a.X - d.X;
        BigInteger adz = (BigInteger)a.Z - d.Z;
        BigInteger bdx = (BigInteger)b.X - d.X;
        BigInteger bdz = (BigInteger)b.Z - d.Z;
        BigInteger cdx = (BigInteger)c.X - d.X;
        BigInteger cdz = (BigInteger)c.Z - d.Z;
        BigInteger determinant =
            ((adx * adx) + (adz * adz)) * ((bdx * cdz) - (bdz * cdx)) -
            ((bdx * bdx) + (bdz * bdz)) * ((adx * cdz) - (adz * cdx)) +
            ((cdx * cdx) + (cdz * cdz)) * ((adx * bdz) - (adz * bdx));
        return orientation > 0 ? determinant.Sign : -determinant.Sign;
    }
}

internal sealed class StableIdOrdering : IComparer<StableId>
{
    internal static StableIdOrdering Instance { get; } = new();

    public int Compare(StableId x, StableId y)
    {
        int high = x.High.CompareTo(y.High);
        return high != 0 ? high : x.Low.CompareTo(y.Low);
    }
}
