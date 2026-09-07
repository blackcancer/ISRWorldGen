using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Plates;

public enum ContinentalFeatureScale
{
    Macro = 0,
    Regional = 1,
}

public sealed record ContinentalFieldSettings
{
    public ContinentalFieldSettings(
        int macroFeatureCount,
        int regionalFeatureCount,
        int maximumFeatureCount,
        long maximumRasterSamples)
    {
        MacroFeatureCount = macroFeatureCount;
        RegionalFeatureCount = regionalFeatureCount;
        MaximumFeatureCount = maximumFeatureCount;
        MaximumRasterSamples = maximumRasterSamples;
    }

    public int MacroFeatureCount { get; }

    public int RegionalFeatureCount { get; }

    public int MaximumFeatureCount { get; }

    public long MaximumRasterSamples { get; }
}

public readonly record struct ContinentalFeatureDescriptor(
    StableId FeatureId,
    ContinentalFeatureScale Scale,
    int CenterXPpm,
    int CenterZPpm,
    int RadiusXPpm,
    int RadiusZPpm);

public readonly record struct ContinentalSamplingGrid
{
    public ContinentalSamplingGrid(long originX, long originZ, long stepX, long stepZ, int width, int height)
    {
        if (stepX <= 0 || stepZ <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepX), "Sampling steps must be positive.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Sampling dimensions must be positive.");
        }

        OriginX = originX;
        OriginZ = originZ;
        StepX = stepX;
        StepZ = stepZ;
        Width = width;
        Height = height;
    }

    public long OriginX { get; }

    public long OriginZ { get; }

    public long StepX { get; }

    public long StepZ { get; }

    public int Width { get; }

    public int Height { get; }
}

public sealed class ContinentalRaster
{
    internal ContinentalRaster(
        ContinentalSamplingGrid grid,
        IEnumerable<int> heightPpm,
        Hash256 modelChecksum)
    {
        int[] values = heightPpm.ToArray();
        Grid = grid;
        HeightPpm = Array.AsReadOnly(values);
        ModelChecksum = modelChecksum;
        ContentChecksum = ComputeChecksum(grid, values, modelChecksum);
    }

    public ContinentalSamplingGrid Grid { get; }

    public int Width => Grid.Width;

    public int Height => Grid.Height;

    public ReadOnlyCollection<int> HeightPpm { get; }

    public Hash256 ModelChecksum { get; }

    public Hash256 ContentChecksum { get; }

    public bool IsLand(int x, int z) => HeightPpm[(z * Width) + x] >= 0;

    private static Hash256 ComputeChecksum(
        ContinentalSamplingGrid grid,
        IReadOnlyList<int> values,
        Hash256 modelChecksum)
    {
        var builder = new StringBuilder();
        builder.Append("ISRW-CONTINENT-RASTER-V1\n").Append(modelChecksum).Append('\n');
        builder.Append(grid.OriginX).Append('|').Append(grid.OriginZ).Append('|')
            .Append(grid.StepX).Append('|').Append(grid.StepZ).Append('|')
            .Append(grid.Width).Append('|').Append(grid.Height).Append('\n');
        foreach (int value in values)
        {
            builder.Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return Hash256.Compute(Encoding.UTF8.GetBytes(builder.ToString()));
    }
}

/// <summary>
/// Immutable whole-world continent model. Features use normalized world coordinates, never storage-tile coordinates.
/// Elliptic envelopes are perturbed at two angular scales to avoid exposing atlas/Voronoi polygons as coastlines.
/// </summary>
public sealed class ContinentalFieldModel
{
    public const int PartsPerMillion = 1_000_000;

    private readonly GenerationIdentity identity;
    private readonly ContinentalFieldSettings settings;
    private readonly Feature[] features;

    private ContinentalFieldModel(
        GenerationIdentity identity,
        WorldBounds bounds,
        ContinentalFieldSettings settings,
        Feature[] features)
    {
        this.identity = identity;
        this.settings = settings;
        Bounds = bounds;
        this.features = features;
        Features = Array.AsReadOnly(features.Select(feature => feature.Descriptor).ToArray());
        ContentChecksum = ComputeChecksum(identity, bounds, settings, features);
    }

    public WorldBounds Bounds { get; }

    public ReadOnlyCollection<ContinentalFeatureDescriptor> Features { get; }

    public Hash256 ContentChecksum { get; }

    public static GenerationResult<ContinentalFieldModel> Create(
        GenerationIdentity identity,
        WorldBounds bounds,
        ContinentalFieldSettings settings)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.MacroFeatureCount is < 2 or > 64 || settings.RegionalFeatureCount is < 2 or > 512)
        {
            return ModelFailure(identity, GenerationFailureCode.InvalidInput, "geology.continents.feature-count",
                "Macro feature count must be in [2,64] and regional feature count in [2,512].");
        }

        int total;
        try
        {
            total = checked(settings.MacroFeatureCount + settings.RegionalFeatureCount);
        }
        catch (OverflowException)
        {
            return ModelFailure(identity, GenerationFailureCode.InvalidInput, "geology.continents.feature-capacity",
                "Feature count overflowed its representable capacity.");
        }

        if (settings.MaximumFeatureCount <= 0 || total > settings.MaximumFeatureCount)
        {
            return ModelFailure(identity, GenerationFailureCode.BudgetExceeded, "geology.continents.feature-budget",
                $"Requested {total} features for budget {settings.MaximumFeatureCount}.");
        }

        if (settings.MaximumRasterSamples <= 0)
        {
            return ModelFailure(identity, GenerationFailureCode.InvalidInput, "geology.continents.raster-budget",
                "Raster sample budget must be positive.");
        }

        var generated = new Feature[total];
        for (int index = 0; index < settings.MacroFeatureCount; index++)
        {
            generated[index] = CreateMacroFeature(identity.NativeSeed, checked((ulong)index));
        }

        for (int index = 0; index < settings.RegionalFeatureCount; index++)
        {
            generated[settings.MacroFeatureCount + index] = CreateRegionalFeature(
                identity.NativeSeed,
                checked((ulong)index),
                generated[index % settings.MacroFeatureCount]);
        }

        return GenerationResult<ContinentalFieldModel>.Success(
            new ContinentalFieldModel(identity, bounds, settings, generated));
    }

    public static GenerationResult<ContinentalFieldModel> Create(
        GenerationIdentity identity,
        FrozenScaleProfile profile,
        ContinentalFieldSettings settings)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        if (identity.GeographyConfigHash != profile.GeographyConfigHash)
        {
            return ModelFailure(
                identity,
                GenerationFailureCode.InvalidInput,
                "geology.continents.profile-hash",
                "Generation identity and frozen scale profile have different geography configuration hashes.");
        }

        WorldDomain domain = profile.AtlasIndexProfile.Domain;
        return Create(
            identity,
            new WorldBounds(
                domain.X.MinInclusive,
                domain.Z.MinInclusive,
                domain.X.MaxExclusive,
                domain.Z.MaxExclusive),
            settings);
    }

    public int SampleHeightPpm(long x, long z)
    {
        if (!Bounds.Contains(x, z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Continental samples must lie inside world bounds.");
        }

        double nx = ((double)x - Bounds.MinX) / Bounds.Width;
        double nz = ((double)z - Bounds.MinZ) / Bounds.Length;
        double best = -1;
        foreach (Feature feature in features)
        {
            double dx = nx - feature.CenterX;
            double dz = nz - feature.CenterZ;
            double localX = (dx * feature.CosRotation) + (dz * feature.SinRotation);
            double localZ = (-dx * feature.SinRotation) + (dz * feature.CosRotation);
            double scaledX = localX / feature.RadiusX;
            double scaledZ = localZ / feature.RadiusZ;
            double radius = Math.Sqrt((scaledX * scaledX) + (scaledZ * scaledZ));
            double theta = Math.Atan2(scaledZ, scaledX);
            double shore = 1 +
                (feature.PrimaryRipple * Math.Sin((3 * theta) + feature.PrimaryPhase)) +
                (feature.SecondaryRipple * Math.Sin((7 * theta) + feature.SecondaryPhase));
            double potential = shore - radius;
            if (potential > best)
            {
                best = potential;
            }
        }

        return checked((int)Math.Round(
            Math.Clamp(best, -1, 1) * PartsPerMillion,
            MidpointRounding.ToEven));
    }

    public GenerationResult<ContinentalRaster> Rasterize(ContinentalSamplingGrid grid)
    {
        long sampleCount;
        long lastX;
        long lastZ;
        try
        {
            sampleCount = checked((long)grid.Width * grid.Height);
            lastX = checked(grid.OriginX + (checked((long)grid.Width - 1) * grid.StepX));
            lastZ = checked(grid.OriginZ + (checked((long)grid.Height - 1) * grid.StepZ));
        }
        catch (OverflowException)
        {
            return RasterFailure(identity, GenerationFailureCode.InvalidInput, "geology.continents.raster-capacity",
                "Raster dimensions or coordinates overflowed their representable capacity.");
        }

        if (!Bounds.Contains(grid.OriginX, grid.OriginZ) || !Bounds.Contains(lastX, lastZ))
        {
            return RasterFailure(identity, GenerationFailureCode.InvalidInput, "geology.continents.raster-bounds",
                "The requested global sampling grid lies outside world bounds.");
        }

        if (sampleCount > settings.MaximumRasterSamples || sampleCount > int.MaxValue)
        {
            return RasterFailure(identity, GenerationFailureCode.BudgetExceeded, "geology.continents.raster-budget",
                $"Requested {sampleCount} samples for budget {settings.MaximumRasterSamples}.");
        }

        var values = new int[checked((int)sampleCount)];
        for (int row = 0; row < grid.Height; row++)
        {
            long z = checked(grid.OriginZ + ((long)row * grid.StepZ));
            for (int column = 0; column < grid.Width; column++)
            {
                long x = checked(grid.OriginX + ((long)column * grid.StepX));
                values[(row * grid.Width) + column] = SampleHeightPpm(x, z);
            }
        }

        return GenerationResult<ContinentalRaster>.Success(new ContinentalRaster(grid, values, ContentChecksum));
    }

    private static Feature CreateMacroFeature(int seed, ulong index)
    {
        StableId id = StableId.Derive(RandomDomain.Geology, StableId.Zero, index);
        double centerX = 0.08 + (0.84 * Unit(seed, id, 0));
        double centerZ = 0.08 + (0.84 * Unit(seed, id, 1));
        double radiusX = 0.14 + (0.14 * Unit(seed, id, 2));
        double radiusZ = 0.10 + (0.13 * Unit(seed, id, 3));
        return CreateFeature(seed, id, ContinentalFeatureScale.Macro, centerX, centerZ, radiusX, radiusZ, 4);
    }

    private static Feature CreateRegionalFeature(int seed, ulong index, Feature parent)
    {
        StableId id = StableId.Derive(RandomDomain.Geology, parent.Descriptor.FeatureId, index);
        double centerX;
        double centerZ;
        if ((index & 1) == 0)
        {
            double angle = Unit(seed, id, 0) * Math.Tau;
            double distance = 0.75 + (0.65 * Unit(seed, id, 1));
            centerX = parent.CenterX + (Math.Cos(angle) * parent.RadiusX * distance);
            centerZ = parent.CenterZ + (Math.Sin(angle) * parent.RadiusZ * distance);
        }
        else
        {
            centerX = 0.03 + (0.94 * Unit(seed, id, 0));
            centerZ = 0.03 + (0.94 * Unit(seed, id, 1));
        }

        centerX = Math.Clamp(centerX, 0.02, 0.98);
        centerZ = Math.Clamp(centerZ, 0.02, 0.98);
        double radiusX = 0.025 + (0.065 * Unit(seed, id, 2));
        double radiusZ = 0.020 + (0.055 * Unit(seed, id, 3));
        return CreateFeature(seed, id, ContinentalFeatureScale.Regional, centerX, centerZ, radiusX, radiusZ, 4);
    }

    private static Feature CreateFeature(
        int seed,
        StableId id,
        ContinentalFeatureScale scale,
        double centerX,
        double centerZ,
        double radiusX,
        double radiusZ,
        ulong counterOffset)
    {
        double rotation = Unit(seed, id, counterOffset) * Math.Tau;
        double primaryPhase = Unit(seed, id, counterOffset + 1) * Math.Tau;
        double secondaryPhase = Unit(seed, id, counterOffset + 2) * Math.Tau;
        double primaryRipple = 0.07 + (0.06 * Unit(seed, id, counterOffset + 3));
        double secondaryRipple = 0.025 + (0.04 * Unit(seed, id, counterOffset + 4));
        var descriptor = new ContinentalFeatureDescriptor(
            id,
            scale,
            ToPpm(centerX),
            ToPpm(centerZ),
            ToPpm(radiusX),
            ToPpm(radiusZ));
        return new Feature(
            descriptor,
            centerX,
            centerZ,
            radiusX,
            radiusZ,
            Math.Cos(rotation),
            Math.Sin(rotation),
            primaryPhase,
            secondaryPhase,
            primaryRipple,
            secondaryRipple);
    }

    private static double Unit(int seed, StableId id, ulong counter) =>
        (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, id, counter) >> 11) *
        (1.0 / (1UL << 53));

    private static int ToPpm(double value) => checked((int)Math.Round(value * PartsPerMillion));

    private static Hash256 ComputeChecksum(
        GenerationIdentity identity,
        WorldBounds bounds,
        ContinentalFieldSettings settings,
        IEnumerable<Feature> features)
    {
        var builder = new StringBuilder();
        builder.Append("ISRW-CONTINENT-MODEL-V1\n")
            .Append(identity.NativeSeed).Append('|').Append(identity.AlgorithmVersion).Append('|')
            .Append(identity.SchemaVersion).Append('|').Append(identity.GeographyConfigHash).Append('|')
            .Append(identity.GenerationAssetHash).Append('|').Append(identity.DeterminismProfileId).Append('\n')
            .Append(bounds.MinX).Append('|').Append(bounds.MinZ).Append('|')
            .Append(bounds.MaxXExclusive).Append('|').Append(bounds.MaxZExclusive).Append('\n')
            .Append(settings.MacroFeatureCount).Append('|').Append(settings.RegionalFeatureCount).Append('|')
            .Append(settings.MaximumFeatureCount).Append('|').Append(settings.MaximumRasterSamples).Append('\n');
        foreach (Feature feature in features)
        {
            builder.Append(feature.Descriptor.FeatureId).Append('|').Append((int)feature.Descriptor.Scale).Append('|')
                .Append(feature.Descriptor.CenterXPpm).Append('|').Append(feature.Descriptor.CenterZPpm).Append('|')
                .Append(feature.Descriptor.RadiusXPpm).Append('|').Append(feature.Descriptor.RadiusZPpm).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(feature.CosRotation)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(feature.SinRotation)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(feature.PrimaryPhase)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(feature.SecondaryPhase)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(feature.PrimaryRipple)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(feature.SecondaryRipple)).Append('\n');
        }

        return Hash256.Compute(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static GenerationResult<ContinentalFieldModel> ModelFailure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details) =>
        GenerationResult<ContinentalFieldModel>.Failure(new GenerationError(
            code,
            identity.NativeSeed,
            stage,
            StableId.Zero,
            identity.GeographyConfigHash,
            details,
            false));

    private static GenerationResult<ContinentalRaster> RasterFailure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details) =>
        GenerationResult<ContinentalRaster>.Failure(new GenerationError(
            code,
            identity.NativeSeed,
            stage,
            StableId.Zero,
            identity.GeographyConfigHash,
            details,
            false));

    private sealed record Feature(
        ContinentalFeatureDescriptor Descriptor,
        double CenterX,
        double CenterZ,
        double RadiusX,
        double RadiusZ,
        double CosRotation,
        double SinRotation,
        double PrimaryPhase,
        double SecondaryPhase,
        double PrimaryRipple,
        double SecondaryRipple);
}
