using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Materials;

/// <summary>Geological codes, deliberately independent from Vintage Story block assets.</summary>
public enum GeologicalMaterialCode : byte
{
    Sandstone = 0,
    Shale = 1,
    Limestone = 2,
    Basalt = 3,
    Granite = 4,
}

/// <summary>
/// Dimensionless material coefficients in [0,1]. They express relative erosion
/// resistance, dissolution susceptibility, and connected pore space respectively.
/// </summary>
public readonly record struct MaterialProperties
{
    public const string Unit = "normalized-0-1";

    public MaterialProperties(
        double erosionResistanceNormalized,
        double solubilityNormalized,
        double permeabilityNormalized)
    {
        EnsureUnitInterval(erosionResistanceNormalized, nameof(erosionResistanceNormalized));
        EnsureUnitInterval(solubilityNormalized, nameof(solubilityNormalized));
        EnsureUnitInterval(permeabilityNormalized, nameof(permeabilityNormalized));
        ErosionResistanceNormalized = erosionResistanceNormalized;
        SolubilityNormalized = solubilityNormalized;
        PermeabilityNormalized = permeabilityNormalized;
    }

    public double ErosionResistanceNormalized { get; }

    public double SolubilityNormalized { get; }

    public double PermeabilityNormalized { get; }

    private static void EnsureUnitInterval(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Material coefficients must be finite and normalized in [0,1].");
        }
    }
}

public sealed class MaterialCatalog
{
    private readonly IReadOnlyDictionary<GeologicalMaterialCode, MaterialProperties> properties;

    public MaterialCatalog(IEnumerable<KeyValuePair<GeologicalMaterialCode, MaterialProperties>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var copy = new SortedDictionary<GeologicalMaterialCode, MaterialProperties>();
        foreach ((GeologicalMaterialCode code, MaterialProperties value) in properties)
        {
            if (!Enum.IsDefined(code))
            {
                throw new ArgumentOutOfRangeException(nameof(properties), code, "Unknown geological material code.");
            }

            if (!copy.TryAdd(code, value))
            {
                throw new ArgumentException("A material catalog cannot contain duplicate codes.", nameof(properties));
            }
        }

        if (copy.Count != Enum.GetValues<GeologicalMaterialCode>().Length)
        {
            throw new ArgumentException("A material catalog must define every geological material code.", nameof(properties));
        }

        this.properties = new ReadOnlyDictionary<GeologicalMaterialCode, MaterialProperties>(copy);
        ContentChecksum = Hash256.Compute(Encoding.UTF8.GetBytes(CanonicalText(copy)));
    }

    public IReadOnlyDictionary<GeologicalMaterialCode, MaterialProperties> Properties => properties;

    public Hash256 ContentChecksum { get; }

    public MaterialProperties Get(GeologicalMaterialCode code) =>
        properties.TryGetValue(code, out MaterialProperties value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown geological material code.");

    private static string CanonicalText(IEnumerable<KeyValuePair<GeologicalMaterialCode, MaterialProperties>> values)
    {
        var builder = new StringBuilder("ISRW-MATERIAL-CATALOG-V1\n");
        foreach ((GeologicalMaterialCode code, MaterialProperties value) in values)
        {
            builder.Append((int)code).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(value.ErosionResistanceNormalized).ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(value.SolubilityNormalized).ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(value.PermeabilityNormalized).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>One semi-open vertical layer [BottomInclusiveY, TopExclusiveY).</summary>
public readonly record struct StratigraphicLayer
{
    public StratigraphicLayer(
        StableId layerId,
        int bottomInclusiveY,
        int topExclusiveY,
        GeologicalMaterialCode materialCode)
    {
        if (topExclusiveY <= bottomInclusiveY)
        {
            throw new ArgumentOutOfRangeException(nameof(topExclusiveY), "A stratum must have positive vertical thickness.");
        }

        if (!Enum.IsDefined(materialCode))
        {
            throw new ArgumentOutOfRangeException(nameof(materialCode), materialCode, "Unknown geological material code.");
        }

        LayerId = layerId;
        BottomInclusiveY = bottomInclusiveY;
        TopExclusiveY = topExclusiveY;
        MaterialCode = materialCode;
    }

    public StableId LayerId { get; }

    public int BottomInclusiveY { get; }

    public int TopExclusiveY { get; }

    public GeologicalMaterialCode MaterialCode { get; }

    public bool Contains(int y) => y >= BottomInclusiveY && y < TopExclusiveY;
}

/// <summary>A world-space fracture stripe. Its permeability effect never changes the host rock code.</summary>
public readonly record struct FractureZone
{
    private const long MaximumDirectionComponent = 1_000_000;

    public FractureZone(
        StableId fractureId,
        long anchorX,
        long anchorZ,
        long directionX,
        long directionZ,
        double halfWidthBlocks,
        double permeabilityBoostNormalized)
    {
        if ((directionX == 0 && directionZ == 0) || directionX is < -MaximumDirectionComponent or > MaximumDirectionComponent || directionZ is < -MaximumDirectionComponent or > MaximumDirectionComponent ||
            !double.IsFinite(halfWidthBlocks) || halfWidthBlocks <= 0 ||
            !double.IsFinite(permeabilityBoostNormalized) || permeabilityBoostNormalized is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(directionX), "Fractures require a non-zero direction, positive finite width, and a normalized boost.");
        }

        FractureId = fractureId;
        AnchorX = anchorX;
        AnchorZ = anchorZ;
        DirectionX = directionX;
        DirectionZ = directionZ;
        HalfWidthBlocks = halfWidthBlocks;
        PermeabilityBoostNormalized = permeabilityBoostNormalized;
    }

    public StableId FractureId { get; }

    public long AnchorX { get; }

    public long AnchorZ { get; }

    public long DirectionX { get; }

    public long DirectionZ { get; }

    public double HalfWidthBlocks { get; }

    public double PermeabilityBoostNormalized { get; }

    public bool Contains(long x, long z)
    {
        BigInteger dx = (BigInteger)x - AnchorX;
        BigInteger dz = (BigInteger)z - AnchorZ;
        BigInteger cross = (dx * DirectionZ) - (dz * DirectionX);
        double directionLength = Math.Sqrt(((double)DirectionX * DirectionX) + ((double)DirectionZ * DirectionZ));
        return (double)BigInteger.Abs(cross) / directionLength <= HalfWidthBlocks;
    }
}

public readonly record struct MaterialSample(
    GeologicalMaterialCode MaterialCode,
    StableId LayerId,
    MaterialProperties Properties,
    bool IsFractured);

public interface IMaterialQuery
{
    MaterialSample Query(long x, int y, long z);
}

/// <summary>Minimal Core hand-off used by erosion, cavern, or resource code without selecting any game asset.</summary>
public interface IMaterialSampleConsumer
{
    void Consume(MaterialSample sample);
}

public static class MaterialQueryDelivery
{
    public static void Deliver(IMaterialQuery query, long x, int y, long z, IMaterialSampleConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(consumer);
        consumer.Consume(query.Query(x, y, z));
    }
}

public enum RockRemovalKind : byte
{
    Valley = 0,
    Cavern = 1,
}

/// <summary>Explicit semi-open Core removal volume; it never carries a replacement material palette.</summary>
public readonly record struct RockRemovalVolume
{
    public RockRemovalVolume(
        RockRemovalKind kind,
        long minX,
        long maxXExclusive,
        int bottomInclusiveY,
        int topExclusiveY,
        long minZ,
        long maxZExclusive)
    {
        if (!Enum.IsDefined(kind) || maxXExclusive <= minX || maxZExclusive <= minZ || topExclusiveY <= bottomInclusiveY)
        {
            throw new ArgumentOutOfRangeException(nameof(maxXExclusive), "Rock removal volumes must use known kinds and non-empty semi-open bounds.");
        }

        Kind = kind;
        MinX = minX;
        MaxXExclusive = maxXExclusive;
        BottomInclusiveY = bottomInclusiveY;
        TopExclusiveY = topExclusiveY;
        MinZ = minZ;
        MaxZExclusive = maxZExclusive;
    }

    public RockRemovalKind Kind { get; }

    public long MinX { get; }

    public long MaxXExclusive { get; }

    public int BottomInclusiveY { get; }

    public int TopExclusiveY { get; }

    public long MinZ { get; }

    public long MaxZExclusive { get; }

    public bool Contains(long x, int y, long z) =>
        x >= MinX && x < MaxXExclusive && y >= BottomInclusiveY && y < TopExclusiveY && z >= MinZ && z < MaxZExclusive;
}

/// <summary>
/// Core exposure view. Removal can hide a material cell, but every exposed solid is
/// re-read from the immutable material snapshot and therefore cannot be recolored.
/// </summary>
public sealed class MaterialExposure
{
    private readonly MaterialSnapshot snapshot;
    private readonly RockRemovalVolume[] removals;

    public MaterialExposure(MaterialSnapshot snapshot, IEnumerable<RockRemovalVolume> removals)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(removals);
        this.snapshot = snapshot;
        this.removals = removals.OrderBy(removal => removal.Kind).ThenBy(removal => removal.MinX).ThenBy(removal => removal.MinZ)
            .ThenBy(removal => removal.BottomInclusiveY).ThenBy(removal => removal.TopExclusiveY).ToArray();
    }

    public bool TryQuerySolid(long x, int y, long z, out MaterialSample sample)
    {
        if (removals.Any(removal => removal.Contains(x, y, z)))
        {
            sample = default;
            return false;
        }

        sample = snapshot.Query(x, y, z);
        return true;
    }

    public bool TryGetExposedSolidBelow(long x, long z, int removedBottomExclusiveY, out int exposedY, out MaterialSample sample)
    {
        int lowestY = snapshot.Layers[^1].BottomInclusiveY;
        for (int y = checked(removedBottomExclusiveY - 1); y >= lowestY; y--)
        {
            if (TryQuerySolid(x, y, z, out sample))
            {
                exposedY = y;
                return true;
            }
        }

        exposedY = default;
        sample = default;
        return false;
    }
}

/// <summary>
/// Immutable columnar material snapshot. Querying is a pure world-coordinate lookup;
/// erosion and excavation remove voxels but never repaint the remaining strata.
/// </summary>
public sealed class MaterialSnapshot : IMaterialQuery
{
    private readonly StratigraphicLayer[] layers;
    private readonly FractureZone[] fractures;

    public MaterialSnapshot(
        MaterialCatalog catalog,
        IEnumerable<StratigraphicLayer> layers,
        IEnumerable<FractureZone> fractures)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(fractures);
        this.layers = layers.OrderByDescending(layer => layer.TopExclusiveY).ToArray();
        this.fractures = fractures.OrderBy(fracture => fracture.FractureId.High).ThenBy(fracture => fracture.FractureId.Low).ToArray();
        if (this.layers.Length == 0)
        {
            throw new ArgumentException("A material snapshot requires at least one stratum.", nameof(layers));
        }

        for (int index = 0; index < this.layers.Length; index++)
        {
            if (index > 0 && this.layers[index - 1].BottomInclusiveY != this.layers[index].TopExclusiveY)
            {
                throw new ArgumentException("Strata must be contiguous with stable, non-overlapping interfaces.", nameof(layers));
            }
        }

        if (this.layers.Select(layer => layer.LayerId).Distinct().Count() != this.layers.Length ||
            this.fractures.Select(fracture => fracture.FractureId).Distinct().Count() != this.fractures.Length)
        {
            throw new ArgumentException("Stratigraphic and fracture identifiers must be unique.");
        }

        Catalog = catalog;
        Layers = Array.AsReadOnly(this.layers);
        Fractures = Array.AsReadOnly(this.fractures);
        ContentChecksum = Hash256.Compute(Encoding.UTF8.GetBytes(CanonicalText()));
    }

    public MaterialCatalog Catalog { get; }

    public ReadOnlyCollection<StratigraphicLayer> Layers { get; }

    public ReadOnlyCollection<FractureZone> Fractures { get; }

    public Hash256 ContentChecksum { get; }

    public MaterialSample Query(long x, int y, long z)
    {
        StratigraphicLayer layer = layers.FirstOrDefault(layer => layer.Contains(y));
        if (!layer.Contains(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "The requested elevation lies outside the published material column.");
        }

        bool fractured = fractures.Any(fracture => fracture.Contains(x, z));
        MaterialProperties baseProperties = Catalog.Get(layer.MaterialCode);
        MaterialProperties properties = fractured
            ? new MaterialProperties(
                baseProperties.ErosionResistanceNormalized,
                baseProperties.SolubilityNormalized,
                Math.Max(baseProperties.PermeabilityNormalized, fractures.Where(fracture => fracture.Contains(x, z)).Max(fracture => fracture.PermeabilityBoostNormalized)))
            : baseProperties;
        return new MaterialSample(layer.MaterialCode, layer.LayerId, properties, fractured);
    }

    private string CanonicalText()
    {
        var builder = new StringBuilder("ISRW-MATERIAL-SNAPSHOT-V1\n").Append(Catalog.ContentChecksum).Append('\n');
        foreach (StratigraphicLayer layer in layers)
        {
            builder.Append(layer.LayerId).Append('|').Append(layer.BottomInclusiveY).Append('|').Append(layer.TopExclusiveY).Append('|').Append((int)layer.MaterialCode).Append('\n');
        }

        foreach (FractureZone fracture in fractures)
        {
            builder.Append(fracture.FractureId).Append('|').Append(fracture.AnchorX).Append('|').Append(fracture.AnchorZ).Append('|')
                .Append(fracture.DirectionX).Append('|').Append(fracture.DirectionZ).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(fracture.HalfWidthBlocks).ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(BitConverter.DoubleToInt64Bits(fracture.PermeabilityBoostNormalized).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return builder.ToString();
    }
}
