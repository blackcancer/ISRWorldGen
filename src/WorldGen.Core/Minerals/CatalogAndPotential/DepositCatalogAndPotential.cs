using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Stratigraphy;

namespace ISRWorldGen.Core.Minerals.CatalogAndPotential;

/// <summary>Reference system used by a native deposit rule; it is never guessed from a Y value.</summary>
public enum DepositAltitudeReference : byte { AbsoluteY, SeaLevelRelativeY, WorldHeightFraction, DepthBelowSurface }
public enum DepositSurfaceIndicator : byte { None, NativeProbabilistic, NativeRequired }

public readonly record struct DepositAltitudeRange
{
    public DepositAltitudeRange(DepositAltitudeReference reference, double minimumInclusive, double maximumInclusive)
    {
        if (!Enum.IsDefined(reference) || !double.IsFinite(minimumInclusive) || !double.IsFinite(maximumInclusive) || maximumInclusive < minimumInclusive ||
            (reference == DepositAltitudeReference.WorldHeightFraction && (minimumInclusive < 0d || maximumInclusive > 1d)))
            throw new ArgumentOutOfRangeException(nameof(maximumInclusive), "Deposit altitude ranges must be finite, ordered, and use their declared reference.");
        Reference = reference; MinimumInclusive = minimumInclusive; MaximumInclusive = maximumInclusive;
    }
    public DepositAltitudeReference Reference { get; }
    public double MinimumInclusive { get; }
    public double MaximumInclusive { get; }
}

/// <summary>Explicit references needed to convert a native altitude rule; no world setting is read opportunistically.</summary>
public readonly record struct DepositAltitudeContext
{
    public DepositAltitudeContext(int worldMinimumY, int worldMaximumYExclusive, int seaLevelY, int surfaceY)
    {
        if (worldMaximumYExclusive <= worldMinimumY || seaLevelY < worldMinimumY || seaLevelY >= worldMaximumYExclusive || surfaceY < worldMinimumY || surfaceY >= worldMaximumYExclusive)
            throw new ArgumentOutOfRangeException(nameof(worldMaximumYExclusive), "Altitude context must name a non-empty world and in-range reference elevations.");
        WorldMinimumY = worldMinimumY; WorldMaximumYExclusive = worldMaximumYExclusive; SeaLevelY = seaLevelY; SurfaceY = surfaceY;
    }
    public int WorldMinimumY { get; } public int WorldMaximumYExclusive { get; } public int SeaLevelY { get; } public int SurfaceY { get; }
}

public readonly record struct DepositAbsoluteAltitudeRange
{
    public DepositAbsoluteAltitudeRange(double minimumInclusiveY, double maximumInclusiveY)
    {
        if (!double.IsFinite(minimumInclusiveY) || !double.IsFinite(maximumInclusiveY) || maximumInclusiveY < minimumInclusiveY) throw new ArgumentOutOfRangeException(nameof(maximumInclusiveY));
        MinimumInclusiveY = minimumInclusiveY; MaximumInclusiveY = maximumInclusiveY;
    }
    public double MinimumInclusiveY { get; }
    public double MaximumInclusiveY { get; }
    public bool Contains(int y) => y >= MinimumInclusiveY && y <= MaximumInclusiveY;
}

public static class DepositAltitudeMapper
{
    public static DepositAbsoluteAltitudeRange Resolve(DepositAltitudeRange range, DepositAltitudeContext context)
    {
        double span = checked((double)context.WorldMaximumYExclusive - context.WorldMinimumY - 1d);
        (double minimum, double maximum) = range.Reference switch
        {
            DepositAltitudeReference.AbsoluteY => (range.MinimumInclusive, range.MaximumInclusive),
            DepositAltitudeReference.SeaLevelRelativeY => (context.SeaLevelY + range.MinimumInclusive, context.SeaLevelY + range.MaximumInclusive),
            DepositAltitudeReference.WorldHeightFraction => (context.WorldMinimumY + (range.MinimumInclusive * span), context.WorldMinimumY + (range.MaximumInclusive * span)),
            DepositAltitudeReference.DepthBelowSurface => (context.SurfaceY - range.MaximumInclusive, context.SurfaceY - range.MinimumInclusive),
            _ => throw new ArgumentOutOfRangeException(nameof(range)),
        };
        return new DepositAbsoluteAltitudeRange(minimum, maximum);
    }
}

/// <summary>Unmodified values observed in the native definition; profile coefficients are kept separately.</summary>
public readonly record struct NativeDepositParameters
{
    public NativeDepositParameters(double frequency, double minimumSize, double maximumSize, double richness)
    {
        if (!double.IsFinite(frequency) || frequency < 0d || !double.IsFinite(minimumSize) || !double.IsFinite(maximumSize) ||
            minimumSize <= 0d || maximumSize < minimumSize || !double.IsFinite(richness) || richness < 0d)
            throw new ArgumentOutOfRangeException(nameof(frequency), "Native deposit parameters must be finite, non-negative, and ordered.");
        Frequency = frequency; MinimumSize = minimumSize; MaximumSize = maximumSize; Richness = richness;
    }
    public double Frequency { get; }
    public double MinimumSize { get; }
    public double MaximumSize { get; }
    public double Richness { get; }
}

public sealed class DepositDefinition
{
    public DepositDefinition(string resourceKey, string nativeProvenance, IEnumerable<string> allowedHostRockKeys,
        DepositAltitudeRange altitudeRange, NativeDepositParameters nativeParameters, string shapeCode,
        string? parentResourceKey, DepositSurfaceIndicator surfaceIndicator, string potentialCategory, string? guideCode)
    {
        ResourceKey = SemanticToken.Require(resourceKey, nameof(resourceKey));
        NativeProvenance = SemanticToken.Require(nativeProvenance, nameof(nativeProvenance));
        ArgumentNullException.ThrowIfNull(allowedHostRockKeys);
        string[] hosts = allowedHostRockKeys.Select(value => SemanticToken.Require(value, nameof(allowedHostRockKeys))).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (hosts.Length == 0 || hosts.Distinct(StringComparer.Ordinal).Count() != hosts.Length) throw new ArgumentException("A deposit requires unique semantic host rocks.", nameof(allowedHostRockKeys));
        AllowedHostRockKeys = Array.AsReadOnly(hosts); AltitudeRange = altitudeRange; NativeParameters = nativeParameters;
        ShapeCode = SemanticToken.Require(shapeCode, nameof(shapeCode));
        ParentResourceKey = parentResourceKey is null ? null : SemanticToken.Require(parentResourceKey, nameof(parentResourceKey));
        if (!Enum.IsDefined(surfaceIndicator)) throw new ArgumentOutOfRangeException(nameof(surfaceIndicator));
        SurfaceIndicator = surfaceIndicator; PotentialCategory = SemanticToken.Require(potentialCategory, nameof(potentialCategory));
        GuideCode = guideCode is null ? null : SemanticToken.Require(guideCode, nameof(guideCode));
    }
    public string ResourceKey { get; }
    public string NativeProvenance { get; }
    public IReadOnlyList<string> AllowedHostRockKeys { get; }
    public DepositAltitudeRange AltitudeRange { get; }
    public NativeDepositParameters NativeParameters { get; }
    public string ShapeCode { get; }
    public string? ParentResourceKey { get; }
    public DepositSurfaceIndicator SurfaceIndicator { get; }
    public string PotentialCategory { get; }
    public string? GuideCode { get; }
}

/// <summary>Immutable semantic deposit rules tied to the exact L14 content catalog; no numeric game IDs are retained.</summary>
public sealed class DepositCatalogSnapshot
{
    public const int SchemaVersion = 1;
    private readonly DepositDefinition[] definitions;

    public DepositCatalogSnapshot(string catalogRevision, ContentCatalogSnapshot contentCatalog, IEnumerable<DepositDefinition> definitions)
    {
        CatalogRevision = SemanticToken.Require(catalogRevision, nameof(catalogRevision));
        ContentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
        ArgumentNullException.ThrowIfNull(definitions);
        this.definitions = definitions.ToArray();
        if (this.definitions.Length == 0 || this.definitions.Any(definition => definition is null) ||
            this.definitions.GroupBy(definition => definition.ResourceKey, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new ArgumentException("Deposit definitions must be non-empty and have unique resource keys.", nameof(definitions));
        var rockKeys = contentCatalog.Rocks.Select(rock => rock.RockKey).ToHashSet(StringComparer.Ordinal);
        if (this.definitions.Any(definition => definition.AllowedHostRockKeys.Any(host => !rockKeys.Contains(host))) ||
            this.definitions.Any(definition => definition.ParentResourceKey is not null && !this.definitions.Any(parent => parent.ResourceKey == definition.ParentResourceKey)) ||
            HasParentCycle(this.definitions))
            throw new ArgumentException("Every host and parent must be present in the published semantic catalog.", nameof(definitions));
        Definitions = Array.AsReadOnly(this.definitions.OrderBy(definition => definition.ResourceKey, StringComparer.Ordinal).ToArray());
        Fingerprint = Hash256.Compute(Encoding.UTF8.GetBytes(CanonicalText()));
    }

    public string CatalogRevision { get; }
    public ContentCatalogSnapshot ContentCatalog { get; }
    public string CatalogId => ContentCatalog.CatalogId;
    public Hash256 Fingerprint { get; }
    public IReadOnlyList<DepositDefinition> Definitions { get; }
    public DepositDefinition Get(string resourceKey) => Definitions.FirstOrDefault(definition => definition.ResourceKey == resourceKey) ?? throw new ArgumentException("Unknown deposit resource.", nameof(resourceKey));

    private string CanonicalText()
    {
        var text = new StringBuilder("ISRW-DEPOSIT-CATALOG-V").Append(SchemaVersion).Append('\n')
            .Append(CatalogRevision).Append('|').Append(ContentCatalog.CatalogId).Append('|').Append(ContentCatalog.Fingerprint).Append('\n');
        foreach (DepositDefinition definition in Definitions)
        {
            text.Append(definition.ResourceKey).Append('|').Append(definition.NativeProvenance).Append('|').Append((int)definition.AltitudeRange.Reference).Append('|')
                .Append(Bits(definition.AltitudeRange.MinimumInclusive)).Append('|').Append(Bits(definition.AltitudeRange.MaximumInclusive)).Append('|')
                .Append(Bits(definition.NativeParameters.Frequency)).Append('|').Append(Bits(definition.NativeParameters.MinimumSize)).Append('|').Append(Bits(definition.NativeParameters.MaximumSize)).Append('|').Append(Bits(definition.NativeParameters.Richness)).Append('|')
                .Append(definition.ShapeCode).Append('|').Append(definition.ParentResourceKey ?? "").Append('|').Append((int)definition.SurfaceIndicator).Append('|').Append(definition.PotentialCategory).Append('|').Append(definition.GuideCode ?? "").Append('|')
                .Append(string.Join(',', definition.AllowedHostRockKeys)).Append('\n');
        }
        return text.ToString();
    }
    private static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture);
    private static bool HasParentCycle(IEnumerable<DepositDefinition> definitions)
    {
        var byResource = definitions.ToDictionary(definition => definition.ResourceKey, StringComparer.Ordinal);
        foreach (DepositDefinition definition in byResource.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (string? current = definition.ResourceKey; current is not null; current = byResource[current].ParentResourceKey)
                if (!visited.Add(current)) return true;
        }
        return false;
    }
}

/// <summary>Explicit profile coefficients, separate from the native rule values and bounded to prevent overflow.</summary>
public readonly record struct GeologicalPotentialCoefficients
{
    public GeologicalPotentialCoefficients(double erosionResistanceWeight, double solubilityWeight, double permeabilityWeight, double multiplier)
    {
        if (!double.IsFinite(erosionResistanceWeight) || !double.IsFinite(solubilityWeight) || !double.IsFinite(permeabilityWeight) ||
            erosionResistanceWeight < 0d || solubilityWeight < 0d || permeabilityWeight < 0d ||
            !double.IsFinite(multiplier) || multiplier < 0d || multiplier > 1d)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "Potential coefficients must be finite, non-negative, and use a normalized multiplier.");
        double sum = erosionResistanceWeight + solubilityWeight + permeabilityWeight;
        if (!double.IsFinite(sum) || sum <= 0d) throw new ArgumentOutOfRangeException(nameof(erosionResistanceWeight), "At least one geological coefficient is required.");
        ErosionResistanceWeight = erosionResistanceWeight; SolubilityWeight = solubilityWeight; PermeabilityWeight = permeabilityWeight; Multiplier = multiplier;
    }
    public double ErosionResistanceWeight { get; }
    public double SolubilityWeight { get; }
    public double PermeabilityWeight { get; }
    public double Multiplier { get; }
}

public sealed class MineralPotentialProfile
{
    private readonly IReadOnlyDictionary<string, GeologicalPotentialCoefficients> coefficients;
    public MineralPotentialProfile(string profileRevision, IEnumerable<KeyValuePair<string, GeologicalPotentialCoefficients>> coefficients)
    {
        ProfileRevision = SemanticToken.Require(profileRevision, nameof(profileRevision)); ArgumentNullException.ThrowIfNull(coefficients);
        var copy = new SortedDictionary<string, GeologicalPotentialCoefficients>(StringComparer.Ordinal);
        foreach ((string key, GeologicalPotentialCoefficients value) in coefficients)
            if (!copy.TryAdd(SemanticToken.Require(key, nameof(coefficients)), value)) throw new ArgumentException("Potential profile keys must be unique.", nameof(coefficients));
        if (copy.Count == 0) throw new ArgumentException("A potential profile must define at least one resource.", nameof(coefficients));
        this.coefficients = new ReadOnlyDictionary<string, GeologicalPotentialCoefficients>(copy);
        Fingerprint = Hash256.Compute(Encoding.UTF8.GetBytes(string.Join('\n', copy.Select(pair => $"{ProfileRevision}|{pair.Key}|{Bits(pair.Value.ErosionResistanceWeight)}|{Bits(pair.Value.SolubilityWeight)}|{Bits(pair.Value.PermeabilityWeight)}|{Bits(pair.Value.Multiplier)}"))));
    }
    public string ProfileRevision { get; }
    public Hash256 Fingerprint { get; }
    public GeologicalPotentialCoefficients Get(string resourceKey) => coefficients.TryGetValue(resourceKey, out GeologicalPotentialCoefficients value) ? value : throw new ArgumentException("Potential profile does not define the resource.", nameof(resourceKey));
    private static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture);
}

public readonly record struct MineralPotentialReading
{
    public MineralPotentialReading(string resourceKey, double intensity, bool hostIsAdmissible)
    {
        ResourceKey = SemanticToken.Require(resourceKey, nameof(resourceKey));
        if (!double.IsFinite(intensity) || intensity < 0d || intensity > 1d) throw new ArgumentOutOfRangeException(nameof(intensity));
        Intensity = intensity; HostIsAdmissible = hostIsAdmissible;
    }
    public string ResourceKey { get; }
    public double Intensity { get; }
    public bool HostIsAdmissible { get; }
}

/// <summary>Immutable regional intensity output, explicitly not a block or occurrence guarantee.</summary>
public sealed class MineralPotentialSnapshot
{
    public MineralPotentialSnapshot(string geologyRevision, string catalogId, Hash256 geologyFingerprint, Hash256 depositCatalogFingerprint, Hash256 profileFingerprint, IEnumerable<MineralPotentialReading> readings)
    {
        GeologyRevision = SemanticToken.Require(geologyRevision, nameof(geologyRevision)); CatalogId = SemanticToken.Require(catalogId, nameof(catalogId));
        ArgumentNullException.ThrowIfNull(readings); MineralPotentialReading[] copy = readings.ToArray();
        if (copy.Length == 0 || copy.GroupBy(reading => reading.ResourceKey, StringComparer.Ordinal).Any(group => group.Count() != 1)) throw new ArgumentException("Potential readings must be non-empty and unique.", nameof(readings));
        GeologyFingerprint = geologyFingerprint; DepositCatalogFingerprint = depositCatalogFingerprint; ProfileFingerprint = profileFingerprint;
        Readings = Array.AsReadOnly(copy.OrderBy(reading => reading.ResourceKey, StringComparer.Ordinal).ToArray());
    }
    public string GeologyRevision { get; } public string CatalogId { get; } public Hash256 GeologyFingerprint { get; } public Hash256 DepositCatalogFingerprint { get; } public Hash256 ProfileFingerprint { get; }
    public IReadOnlyList<MineralPotentialReading> Readings { get; }
}

/// <summary>Pure deterministic potential model. It has no chunk, worker, clock, or shared RNG input.</summary>
public sealed class MineralPotentialModel
{
    private readonly DepositCatalogSnapshot catalog; private readonly MineralPotentialProfile profile; private readonly GeologyRevisionIdentity expectedGeology;
    public MineralPotentialModel(DepositCatalogSnapshot catalog, MineralPotentialProfile profile, GeologyRevisionIdentity expectedGeology)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        this.expectedGeology = expectedGeology;
        if (expectedGeology.CatalogId != catalog.CatalogId) throw new InvalidOperationException("The potential model must be bound to the deposit catalog's published geology identity.");
        foreach (DepositDefinition definition in catalog.Definitions) _ = profile.Get(definition.ResourceKey);
    }
    public MineralPotentialSnapshot Evaluate(IGeologyVolumeQuery geology, long x, int y, long z)
    {
        ArgumentNullException.ThrowIfNull(geology);
        if (geology.Identity != expectedGeology || geology.Identity.CatalogId != catalog.CatalogId) throw new InvalidOperationException("Deposit catalog and geology identity differ; potential publication is refused.");
        GeologySample sample = geology.SampleRock(x, y, z);
        if (sample.GeologyRevision != geology.Identity.GeologyRevision || sample.Fingerprint != geology.Identity.Fingerprint) throw new InvalidOperationException("The geology sample does not match its published fingerprint.");
        MineralPotentialReading[] readings = catalog.Definitions.Select(definition => Evaluate(definition, sample)).ToArray();
        return new MineralPotentialSnapshot(geology.Identity.GeologyRevision, catalog.CatalogId, geology.Identity.Fingerprint, catalog.Fingerprint, profile.Fingerprint, readings);
    }
    private MineralPotentialReading Evaluate(DepositDefinition definition, GeologySample sample)
    {
        bool admissible = definition.AllowedHostRockKeys.Contains(sample.RockKey, StringComparer.Ordinal);
        if (!admissible) return new(definition.ResourceKey, 0d, false);
        GeologicalPotentialCoefficients coefficient = profile.Get(definition.ResourceKey);
        double sum = coefficient.ErosionResistanceWeight + coefficient.SolubilityWeight + coefficient.PermeabilityWeight;
        double weighted = ((sample.Properties.ErosionResistance * coefficient.ErosionResistanceWeight) + (sample.Properties.Solubility * coefficient.SolubilityWeight) + (sample.Properties.Permeability * coefficient.PermeabilityWeight)) / sum;
        double intensity = Math.Clamp(weighted * coefficient.Multiplier, 0d, 1d);
        return new(definition.ResourceKey, intensity, true);
    }
}

internal static class SemanticToken
{
    internal static string Require(string? value, string parameterName) => !string.IsNullOrWhiteSpace(value) && value == value.ToLowerInvariant() && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '/' or '.' or '_' or '-')
        ? value : throw new ArgumentException("Semantic tokens must be lowercase and non-empty.", parameterName);
}
