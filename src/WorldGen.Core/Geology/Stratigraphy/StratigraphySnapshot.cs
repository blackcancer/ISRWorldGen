using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Stratigraphy;

/// <summary>Semantic rock properties used by Core; native block identifiers remain in the adapter.</summary>
public readonly record struct RockModelProperties
{
    public RockModelProperties(double erosionResistance, double solubility, double permeability)
    {
        Ensure(erosionResistance, nameof(erosionResistance));
        Ensure(solubility, nameof(solubility));
        Ensure(permeability, nameof(permeability));
        ErosionResistance = erosionResistance;
        Solubility = solubility;
        Permeability = permeability;
    }
    public double ErosionResistance { get; }
    public double Solubility { get; }
    public double Permeability { get; }

    private static void Ensure(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0d or > 1d) throw new ArgumentOutOfRangeException(name);
    }
}

public enum RockFamily : byte { Sedimentary, Igneous, Metamorphic, Impact }

public sealed record RockDefinition(string RockKey, string NativeCode, RockFamily Family, RockModelProperties Properties)
{
    public string RockKey { get; } = Token.Require(RockKey, nameof(RockKey));
    public string NativeCode { get; } = Token.Require(NativeCode, nameof(NativeCode));
    public RockFamily Family { get; } = Enum.IsDefined(Family) ? Family : throw new ArgumentOutOfRangeException(nameof(Family));
}

/// <summary>
/// Core projection of L14-A's canonical native inventory. An adapter supplies semantic
/// strings and a fingerprint; Core never references its Vintage Story implementation.
/// </summary>
public sealed class ContentCatalogSnapshot
{
    public ContentCatalogSnapshot(string catalogId, string targetVersion, string assetRevision, Hash256 fingerprint, IEnumerable<RockDefinition> rocks)
    {
        CatalogId = Token.Require(catalogId, nameof(catalogId));
        TargetVersion = Token.Require(targetVersion, nameof(targetVersion));
        AssetRevision = Token.Require(assetRevision, nameof(assetRevision));
        ArgumentNullException.ThrowIfNull(rocks);
        RockDefinition[] copy = rocks.ToArray();
        if (copy.Length == 0 || copy.Any(rock => rock is null) ||
            copy.GroupBy(rock => rock.RockKey, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            copy.GroupBy(rock => rock.NativeCode, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new ArgumentException("Rocks must be non-empty and have unique semantic identities.", nameof(rocks));
        Rocks = Array.AsReadOnly(copy.OrderBy(rock => rock.RockKey, StringComparer.Ordinal).ToArray());
        Fingerprint = fingerprint;
    }

    public string CatalogId { get; }
    public string TargetVersion { get; }
    public string AssetRevision { get; }
    public Hash256 Fingerprint { get; }
    public IReadOnlyList<RockDefinition> Rocks { get; }
    public RockDefinition Get(string rockKey) => Rocks.FirstOrDefault(rock => string.Equals(rock.RockKey, rockKey, StringComparison.Ordinal))
        ?? throw new ArgumentException("Rock key is not in this catalog.", nameof(rockKey));
}

/// <summary>A tilted surface y = BaseY + SlopeX*x + SlopeZ*z in Core world coordinates.</summary>
public readonly record struct StratigraphicSurface
{
    public StratigraphicSurface(double baseY, double slopeX, double slopeZ)
    {
        if (!double.IsFinite(baseY) || !double.IsFinite(slopeX) || !double.IsFinite(slopeZ)) throw new ArgumentOutOfRangeException(nameof(baseY));
        BaseY = baseY; SlopeX = slopeX; SlopeZ = slopeZ;
    }
    public double BaseY { get; }
    public double SlopeX { get; }
    public double SlopeZ { get; }
    public double At(long x, long z) => BaseY + (SlopeX * x) + (SlopeZ * z);
}

/// <summary>A layer below a tilted top surface, with a positive thickness everywhere.</summary>
public sealed record StratigraphicLayer(StableId FormationId, string RockKey, int Priority, StratigraphicSurface Top, double Thickness)
{
    public string RockKey { get; } = Token.Require(RockKey, nameof(RockKey));
    public double Thickness { get; } = double.IsFinite(Thickness) && Thickness > 0d ? Thickness : throw new ArgumentOutOfRangeException(nameof(Thickness));
    public bool Contains(long x, int y, long z)
    {
        double top = Top.At(x, z);
        return double.IsFinite(top) && y < top && y >= top - Thickness;
    }
}

/// <summary>Fault displacement is applied in stable priority/id order before sampling host layers.</summary>
public sealed record StratigraphicFault(StableId FaultId, int Priority, long AnchorX, long AnchorZ, double NormalX, double NormalZ, double UpthrowY)
{
    public double NormalX { get; } = double.IsFinite(NormalX) && (NormalX != 0d || NormalZ != 0d) ? NormalX : throw new ArgumentOutOfRangeException(nameof(NormalX));
    public double NormalZ { get; } = double.IsFinite(NormalZ) && (NormalX != 0d || NormalZ != 0d) ? NormalZ : throw new ArgumentOutOfRangeException(nameof(NormalZ));
    public double UpthrowY { get; } = double.IsFinite(UpthrowY) ? UpthrowY : throw new ArgumentOutOfRangeException(nameof(UpthrowY));
    public bool PositiveSide(long x, long z) => ((NormalX * ((double)x - AnchorX)) + (NormalZ * ((double)z - AnchorZ))) >= 0d;
    public int Apply(long x, int y, long z) => PositiveSide(x, z) ? checked(y - (int)Math.Round(UpthrowY, MidpointRounding.ToEven)) : y;
}

/// <summary>Ellipsoidal intrusive body. Higher priority wins; StableId is the deterministic tie-breaker.</summary>
public sealed record IntrusionVolume(StableId FormationId, string RockKey, int Priority, long CenterX, int CenterY, long CenterZ, double RadiusX, double RadiusY, double RadiusZ)
{
    public string RockKey { get; } = Token.Require(RockKey, nameof(RockKey));
    public double RadiusX { get; } = Radius(RadiusX, nameof(RadiusX));
    public double RadiusY { get; } = Radius(RadiusY, nameof(RadiusY));
    public double RadiusZ { get; } = Radius(RadiusZ, nameof(RadiusZ));
    public bool Contains(long x, int y, long z)
    {
        double dx = ((double)x - CenterX) / RadiusX, dy = ((double)y - CenterY) / RadiusY, dz = ((double)z - CenterZ) / RadiusZ;
        return (dx * dx) + (dy * dy) + (dz * dz) <= 1d;
    }
    private static double Radius(double value, string name) => double.IsFinite(value) && value > 0d ? value : throw new ArgumentOutOfRangeException(name);
}

public readonly record struct GeologySample(string RockKey, StableId FormationId, RockModelProperties Properties, string GeologyRevision, Hash256 Fingerprint);
public readonly record struct GeologyRevisionIdentity(string GeologyRevision, string CatalogId, Hash256 Fingerprint);

public interface IGeologyVolumeQuery { GeologyRevisionIdentity Identity { get; } GeologySample SampleRock(long x, int y, long z); }

/// <summary>Known alluvial cover. It is distinct from the immutable bedrock queried below it.</summary>
public sealed record AlluvialDeposit(string DepositKey, double Thickness, string Provenance)
{
    public string DepositKey { get; } = Token.Require(DepositKey, nameof(DepositKey));
    public double Thickness { get; } = double.IsFinite(Thickness) && Thickness > 0d ? Thickness : throw new ArgumentOutOfRangeException(nameof(Thickness));
    public string Provenance { get; } = Token.Require(Provenance, nameof(Provenance));
}

public readonly record struct SurfaceMaterialSample(GeologySample Bedrock, AlluvialDeposit? Deposit, long X, int ExposedY, long Z);

/// <summary>Maps an incision's exposed coordinate directly back to the immutable geology volume.</summary>
public sealed class StratigraphySurfaceSampler
{
    private readonly IGeologyVolumeQuery volume;
    public StratigraphySurfaceSampler(IGeologyVolumeQuery volume) => this.volume = volume ?? throw new ArgumentNullException(nameof(volume));
    public SurfaceMaterialSample SampleAfterIncision(long x, int exposedY, long z, AlluvialDeposit? deposit)
    {
        GeologySample bedrock = volume.SampleRock(x, exposedY, z);
        if (bedrock.GeologyRevision != volume.Identity.GeologyRevision || bedrock.Fingerprint != volume.Identity.Fingerprint)
            throw new InvalidOperationException("The sampled bedrock identity does not match its geology volume.");
        return new SurfaceMaterialSample(bedrock, deposit, x, exposedY, z);
    }
}

/// <summary>Core test/delivery port; future consumers must receive the published snapshot, not reconstructed samples.</summary>
public interface IGeologySnapshotConsumer { void Consume(GeologyVolumeSnapshot snapshot); }
public static class GeologySnapshotDelivery
{
    public static void Deliver(GeologyVolumeSnapshot snapshot, IGeologySnapshotConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(consumer);
        consumer.Consume(snapshot);
    }
}

public static class GeologyRevisionGate
{
    public static void RequireSame(IGeologyVolumeQuery expected, IGeologyVolumeQuery supplied)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(supplied);
        if (expected.Identity != supplied.Identity) throw new InvalidOperationException("Geology revision mismatch; publication is refused.");
    }
}

/// <summary>Immutable, pure world-coordinate geology. It has no chunk, worker, clock, or RNG input.</summary>
public sealed class GeologyVolumeSnapshot : IGeologyVolumeQuery
{
    private readonly StratigraphicLayer[] layers;
    private readonly StratigraphicFault[] faults;
    private readonly IntrusionVolume[] intrusions;

    public GeologyVolumeSnapshot(string geologyRevision, ContentCatalogSnapshot catalog, string modelScaleId, int minimumY, int maximumYExclusive,
        IEnumerable<StratigraphicLayer> layers, IEnumerable<StratigraphicFault> faults, IEnumerable<IntrusionVolume> intrusions)
    {
        GeologyRevision = Token.Require(geologyRevision, nameof(geologyRevision));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ModelScaleId = Token.Require(modelScaleId, nameof(modelScaleId));
        if (maximumYExclusive <= minimumY) throw new ArgumentOutOfRangeException(nameof(maximumYExclusive));
        MinimumY = minimumY; MaximumYExclusive = maximumYExclusive;
        this.layers = Copy(layers, nameof(layers), layer => layer.FormationId).OrderByDescending(layer => layer.Priority).ThenBy(layer => layer.FormationId.High).ThenBy(layer => layer.FormationId.Low).ToArray();
        this.faults = Copy(faults, nameof(faults), fault => fault.FaultId).OrderByDescending(fault => fault.Priority).ThenBy(fault => fault.FaultId.High).ThenBy(fault => fault.FaultId.Low).ToArray();
        this.intrusions = Copy(intrusions, nameof(intrusions), intrusion => intrusion.FormationId).OrderByDescending(intrusion => intrusion.Priority).ThenBy(intrusion => intrusion.FormationId.High).ThenBy(intrusion => intrusion.FormationId.Low).ToArray();
        if (this.layers.Length == 0) throw new ArgumentException("At least one host layer is required.", nameof(layers));
        if (this.layers.Concat<StratigraphicLayer>([]).Any(layer => !catalog.Rocks.Any(rock => rock.RockKey == layer.RockKey)) || this.intrusions.Any(body => !catalog.Rocks.Any(rock => rock.RockKey == body.RockKey)))
            throw new ArgumentException("Every formation must name a catalog rock.");
        if (this.layers.Select(layer => layer.FormationId).Concat(this.intrusions.Select(body => body.FormationId)).Distinct().Count() != this.layers.Length + this.intrusions.Length || this.faults.Select(fault => fault.FaultId).Distinct().Count() != this.faults.Length)
            throw new ArgumentException("Formation and fault identities must be unique.");
        Layers = Array.AsReadOnly(this.layers); Faults = Array.AsReadOnly(this.faults); Intrusions = Array.AsReadOnly(this.intrusions);
        Fingerprint = Hash256.Compute(Encoding.UTF8.GetBytes(CanonicalText()));
        Identity = new(GeologyRevision, catalog.CatalogId, Fingerprint);
    }

    public string GeologyRevision { get; } public ContentCatalogSnapshot Catalog { get; } public string ModelScaleId { get; }
    public int MinimumY { get; } public int MaximumYExclusive { get; } public Hash256 Fingerprint { get; } public GeologyRevisionIdentity Identity { get; }
    public IReadOnlyList<StratigraphicLayer> Layers { get; } public IReadOnlyList<StratigraphicFault> Faults { get; } public IReadOnlyList<IntrusionVolume> Intrusions { get; }
    public GeologySample SampleRock(long x, int y, long z)
    {
        if (y < MinimumY || y >= MaximumYExclusive) throw new ArgumentOutOfRangeException(nameof(y));
        IntrusionVolume? intrusion = intrusions.FirstOrDefault(body => body.Contains(x, y, z));
        if (intrusion is not null) return Make(intrusion.RockKey, intrusion.FormationId);
        int displacedY = faults.Aggregate(y, (current, fault) => fault.Apply(x, current, z));
        StratigraphicLayer? layer = layers.FirstOrDefault(candidate => candidate.Contains(x, displacedY, z));
        if (layer is null) throw new InvalidOperationException("No formation covers the requested coordinate.");
        return Make(layer.RockKey, layer.FormationId);
    }
    private GeologySample Make(string rockKey, StableId formationId) => new(rockKey, formationId, Catalog.Get(rockKey).Properties, GeologyRevision, Fingerprint);
    private static T[] Copy<T>(IEnumerable<T> input, string name, Func<T, StableId> _) { ArgumentNullException.ThrowIfNull(input); T[] copy = input.ToArray(); if (copy.Any(value => value is null)) throw new ArgumentException("Collections cannot contain null.", name); return copy; }
    private string CanonicalText()
    {
        var text = new StringBuilder("ISRW-GEOLOGY-V1\n").Append(GeologyRevision).Append('|').Append(Catalog.Fingerprint).Append('|').Append(ModelScaleId).Append('|').Append(MinimumY).Append('|').Append(MaximumYExclusive).Append('\n');
        foreach (var layer in layers) text.Append("L|").Append(layer.FormationId).Append('|').Append(layer.RockKey).Append('|').Append(layer.Priority).Append('|').Append(Bits(layer.Top.BaseY)).Append('|').Append(Bits(layer.Top.SlopeX)).Append('|').Append(Bits(layer.Top.SlopeZ)).Append('|').Append(Bits(layer.Thickness)).Append('\n');
        foreach (var fault in faults) text.Append("F|").Append(fault.FaultId).Append('|').Append(fault.Priority).Append('|').Append(fault.AnchorX).Append('|').Append(fault.AnchorZ).Append('|').Append(Bits(fault.NormalX)).Append('|').Append(Bits(fault.NormalZ)).Append('|').Append(Bits(fault.UpthrowY)).Append('\n');
        foreach (var body in intrusions) text.Append("I|").Append(body.FormationId).Append('|').Append(body.RockKey).Append('|').Append(body.Priority).Append('|').Append(body.CenterX).Append('|').Append(body.CenterY).Append('|').Append(body.CenterZ).Append('|').Append(Bits(body.RadiusX)).Append('|').Append(Bits(body.RadiusY)).Append('|').Append(Bits(body.RadiusZ)).Append('\n');
        return text.ToString();
    }
    private static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture);
}

internal static class Token
{
    internal static string Require(string? value, string name) => !string.IsNullOrWhiteSpace(value) && value == value.ToLowerInvariant() && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '/' or '.' or '_' or '-') ? value : throw new ArgumentException("Semantic tokens must be lowercase and non-empty.", name);
}
