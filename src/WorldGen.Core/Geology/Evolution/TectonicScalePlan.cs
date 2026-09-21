namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// FullAtlasScaled: resizing a game world never crops a million-block atlas.
/// The longest reference axis is one million units. A single isotropic scale
/// preserves the aspect ratio of rectangular worlds. Sampling resolution is a
/// separate concern. No native world setting is written by this value object.
/// </summary>
public sealed record TectonicScalePlan
{
    public const double ReferenceLongEdge = 1_000_000;
    public long WidthBlocks { get; }
    public long LengthBlocks { get; }
    public double ReferenceWidth { get; }
    public double ReferenceLength { get; }
    public double BlocksPerReferenceUnit { get; }
    public string Mode => "FULL_ATLAS_SCALED_NOT_CROPPED";

    public TectonicScalePlan(long widthBlocks, long lengthBlocks)
    {
        if (widthBlocks is < 8192 or > 1_024_000 || lengthBlocks is < 8192 or > 1_024_000 ||
            Math.Max(widthBlocks, lengthBlocks) > 4 * Math.Min(widthBlocks, lengthBlocks))
            throw new ArgumentOutOfRangeException(nameof(widthBlocks), "Qualified atlas: axes 8192..1024000 blocks, aspect ratio <=4.");
        WidthBlocks = widthBlocks; LengthBlocks = lengthBlocks;
        BlocksPerReferenceUnit = Math.Max(widthBlocks, lengthBlocks) / ReferenceLongEdge;
        ReferenceWidth = widthBlocks / BlocksPerReferenceUnit;
        ReferenceLength = lengthBlocks / BlocksPerReferenceUnit;
    }

    public (double X, double Z) ToReference(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || x < 0 || z < 0 || x >= WidthBlocks || z >= LengthBlocks)
            throw new ArgumentOutOfRangeException(nameof(x), "Position must be inside this entire world.");
        return (x / BlocksPerReferenceUnit, z / BlocksPerReferenceUnit);
    }
}

/// <summary>Explicit experimental units: reference horizontal units, model Myr, km of equivalent crust thickness.</summary>
public sealed record TectonicEvolutionSettings
{
    public int Side { get; }
    public int PlateCount { get; }
    public int CratonCount { get; }
    public double Duration { get; }
    public double SpeedReferenceUnitsPerTime { get; }
    public double DeformationWidth { get; }
    public double LowerCrustMobility { get; }
    public double InitialOceanAge { get; }
    public int MotionSign { get; }
    public bool AdvectPlateDomains { get; }
    public string DomainMotionPolicy => AdvectPlateDomains ? AdvectedPlateDomains.AlgorithmId : "moving-voronoi-reference-v1";
    public string MaterialContactPolicy => CrustResponse.PolarityPolicy;

    public TectonicEvolutionSettings(int side = 256, int plateCount = 12, int cratonCount = 4,
        double duration = 36, double speedReferenceUnitsPerTime = 1800,
        double deformationWidth = 22000, double lowerCrustMobility = 2_000_000, double initialOceanAge = 50, int motionSign = 1, bool advectPlateDomains = false)
    {
        if (motionSign is not (-1 or 1) || side is < 32 or > 512 || (side & (side - 1)) != 0 || plateCount is < 2 or > 32 || cratonCount is < 2 or > 6 ||
            !double.IsFinite(duration) || duration is < 0 or > 100 ||
            !double.IsFinite(speedReferenceUnitsPerTime) || speedReferenceUnitsPerTime is < 0 or > 4000 ||
            !double.IsFinite(deformationWidth) || deformationWidth is < 5000 or > 80000 ||
            !double.IsFinite(lowerCrustMobility) || lowerCrustMobility is < 0 or > 5_000_000 ||
            !double.IsFinite(initialOceanAge) || initialOceanAge is < 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(side), "Unsupported bounded tectonic campaign settings.");
        Side = side; PlateCount = plateCount; CratonCount = cratonCount;
        Duration = duration; SpeedReferenceUnitsPerTime = speedReferenceUnitsPerTime;
        DeformationWidth = deformationWidth; LowerCrustMobility = lowerCrustMobility; InitialOceanAge = initialOceanAge; MotionSign = motionSign; AdvectPlateDomains = advectPlateDomains;
    }
}
