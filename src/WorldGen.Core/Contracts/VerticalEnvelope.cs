namespace ISRWorldGen.Core.Contracts;

/// <summary>Immutable vertical limits in block levels, all interpreted as semi-open bounds.</summary>
public sealed record VerticalEnvelope
{
    public VerticalEnvelope(
        long worldHeight,
        long seaLevel,
        long minimumFloorThickness,
        long minimumCavernCover,
        long heightMargin)
    {
        if (worldHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldHeight), worldHeight, "World height must be positive.");
        }

        if (minimumFloorThickness <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumFloorThickness),
                minimumFloorThickness,
                "Minimum floor thickness must be positive.");
        }

        if (minimumCavernCover <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCavernCover),
                minimumCavernCover,
                "Minimum cavern cover must be positive.");
        }

        if (heightMargin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightMargin), heightMargin, "Height margin cannot be negative.");
        }

        _ = checked(minimumFloorThickness + heightMargin);
        long maximumExclusive = checked(worldHeight - heightMargin);
        if (minimumFloorThickness >= maximumExclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumFloorThickness),
                minimumFloorThickness,
                "Floor and height margin leave no usable vertical interval.");
        }

        if (minimumCavernCover >= checked(maximumExclusive - minimumFloorThickness))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumCavernCover),
                minimumCavernCover,
                "Cavern cover cannot fit in the usable vertical interval.");
        }

        if (seaLevel < minimumFloorThickness || seaLevel >= maximumExclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seaLevel),
                seaLevel,
                "Sea level must belong to the semi-open usable vertical interval.");
        }

        WorldHeight = worldHeight;
        SeaLevel = seaLevel;
        MinimumFloorThickness = minimumFloorThickness;
        MinimumCavernCover = minimumCavernCover;
        HeightMargin = heightMargin;
        MinimumUsableLevel = minimumFloorThickness;
        MaximumExclusiveUsableLevel = maximumExclusive;
    }

    public long WorldHeight { get; }

    public long SeaLevel { get; }

    public long MinimumFloorThickness { get; }

    public long MinimumCavernCover { get; }

    public long HeightMargin { get; }

    public long MinimumUsableLevel { get; }

    public long MaximumExclusiveUsableLevel { get; }

    public void EnsureCavernCovered(long surfaceLevel, long cavernCeilingLevel)
    {
        if (surfaceLevel < MinimumUsableLevel || surfaceLevel >= MaximumExclusiveUsableLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceLevel), surfaceLevel, "Surface is outside the usable interval.");
        }

        if (cavernCeilingLevel < MinimumUsableLevel || cavernCeilingLevel >= surfaceLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cavernCeilingLevel),
                cavernCeilingLevel,
                "Cavern ceiling must be below the surface and inside the usable interval.");
        }

        if (checked(surfaceLevel - cavernCeilingLevel) < MinimumCavernCover)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cavernCeilingLevel),
                cavernCeilingLevel,
                "Cavern ceiling does not satisfy the minimum cover.");
        }
    }
}
