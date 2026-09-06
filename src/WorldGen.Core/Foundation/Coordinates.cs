namespace ISRWorldGen.Core.Foundation;

/// <summary>
/// Centralized integer coordinate operations. Divisors represent positive grid sizes.
/// </summary>
public static class CoordinateMath
{
    public static long FloorDivide(long value, long positiveDivisor)
    {
        EnsurePositiveDivisor(positiveDivisor);

        long quotient = value / positiveDivisor;
        long remainder = value % positiveDivisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    public static long FloorModulo(long value, long positiveDivisor)
    {
        EnsurePositiveDivisor(positiveDivisor);

        long remainder = value % positiveDivisor;
        return remainder < 0 ? remainder + positiveDivisor : remainder;
    }

    public static int ToInt32Checked(long value) => checked((int)value);

    public static long MultiplyChecked(long left, long right) => checked(left * right);

    private static void EnsurePositiveDivisor(long divisor)
    {
        if (divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(divisor), divisor, "Grid divisors must be positive.");
        }
    }
}

public readonly record struct WorldBlockPosition(long X, long Z);

public readonly record struct ChunkPosition(long X, long Z);

public readonly record struct LocalBlockPosition(int X, int Z);

public readonly record struct ChunkedBlockPosition(ChunkPosition Chunk, LocalBlockPosition Local);

/// <summary>
/// Converts horizontal block coordinates through a runtime-provided chunk size.
/// </summary>
public sealed class ChunkGrid
{
    public ChunkGrid(int chunkSize)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be positive.");
        }

        ChunkSize = chunkSize;
    }

    public int ChunkSize { get; }

    public ChunkedBlockPosition Split(WorldBlockPosition world)
    {
        long chunkX = CoordinateMath.FloorDivide(world.X, ChunkSize);
        long chunkZ = CoordinateMath.FloorDivide(world.Z, ChunkSize);
        int localX = CoordinateMath.ToInt32Checked(CoordinateMath.FloorModulo(world.X, ChunkSize));
        int localZ = CoordinateMath.ToInt32Checked(CoordinateMath.FloorModulo(world.Z, ChunkSize));

        return new ChunkedBlockPosition(
            new ChunkPosition(chunkX, chunkZ),
            new LocalBlockPosition(localX, localZ));
    }

    public WorldBlockPosition Combine(ChunkedBlockPosition position)
    {
        EnsureLocalCoordinate(position.Local.X, nameof(position.Local.X));
        EnsureLocalCoordinate(position.Local.Z, nameof(position.Local.Z));

        return new WorldBlockPosition(
            CombineAxis(position.Chunk.X, position.Local.X),
            CombineAxis(position.Chunk.Z, position.Local.Z));
    }

    private long CombineAxis(long chunk, int local)
    {
        Int128 combined = ((Int128)chunk * ChunkSize) + local;
        return checked((long)combined);
    }

    private void EnsureLocalCoordinate(int value, string parameterName)
    {
        if (value < 0 || value >= ChunkSize)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Local coordinates use the semi-open interval [0, {ChunkSize}).");
        }
    }
}

/// <summary>
/// Non-empty semi-open interval [MinInclusive, MaxExclusive) in 64-bit block coordinates.
/// </summary>
public sealed record Int64Interval
{
    public Int64Interval(long minInclusive, long maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                maxExclusive,
                "The exclusive maximum must be greater than the inclusive minimum.");
        }

        MinInclusive = minInclusive;
        MaxExclusive = maxExclusive;
    }

    public long MinInclusive { get; }

    public long MaxExclusive { get; }

    public bool Contains(long value) => value >= MinInclusive && value < MaxExclusive;
}

/// <summary>
/// Qualified horizontal world limits, real vertical height, and sampling origin.
/// Horizontal ranges are semi-open and all coordinates remain 64-bit in Core.
/// </summary>
public sealed record WorldDomain
{
    public WorldDomain(
        Int64Interval x,
        Int64Interval z,
        int height,
        WorldBlockPosition samplingOrigin)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(z);

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "World height must be positive.");
        }

        if (!x.Contains(samplingOrigin.X) || !z.Contains(samplingOrigin.Z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplingOrigin),
                samplingOrigin,
                "The sampling origin must belong to the semi-open world domain.");
        }

        X = x;
        Z = z;
        Height = height;
        SamplingOrigin = samplingOrigin;
    }

    public Int64Interval X { get; }

    public Int64Interval Z { get; }

    public int Height { get; }

    public WorldBlockPosition SamplingOrigin { get; }

    public bool Contains(WorldBlockPosition position) =>
        X.Contains(position.X) && Z.Contains(position.Z);

    public void EnsureContains(WorldBlockPosition position)
    {
        if (!Contains(position))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "The block position lies outside the semi-open world domain.");
        }
    }
}
