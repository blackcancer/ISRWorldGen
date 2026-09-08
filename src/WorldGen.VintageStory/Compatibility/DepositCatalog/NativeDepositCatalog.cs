using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Stratigraphy;
using ISRWorldGen.Core.Minerals.CatalogAndPotential;

namespace ISRWorldGen.Compatibility.DepositCatalog;

/// <summary>
/// Adapter-side extraction envelope. Native asset parsing belongs to a later audited
/// integration step; this type deliberately stores semantic asset locations, never BlockId.
/// </summary>
internal sealed class NativeDepositCatalogManifest
{
    internal NativeDepositCatalogManifest(string catalogId, string targetVersion, string catalogRevision, Hash256 expectedContentFingerprint,
        IEnumerable<string> admittedProvenances, IEnumerable<string> requiredResourceKeys, IEnumerable<NativeDepositHostFamilyRequirement> requiredHostFamilies)
    {
        CatalogId = DepositToken.Require(catalogId, nameof(catalogId)); TargetVersion = DepositToken.Require(targetVersion, nameof(targetVersion));
        CatalogRevision = DepositToken.Require(catalogRevision, nameof(catalogRevision)); ExpectedContentFingerprint = expectedContentFingerprint;
        ArgumentNullException.ThrowIfNull(admittedProvenances); ArgumentNullException.ThrowIfNull(requiredResourceKeys); ArgumentNullException.ThrowIfNull(requiredHostFamilies);
        string[] provenance = admittedProvenances.Select(value => DepositToken.Require(value, nameof(admittedProvenances))).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] resources = requiredResourceKeys.Select(value => DepositToken.Require(value, nameof(requiredResourceKeys))).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        NativeDepositHostFamilyRequirement[] families = requiredHostFamilies.ToArray();
        if (provenance.Length == 0 || provenance.Distinct(StringComparer.Ordinal).Count() != provenance.Length || resources.Distinct(StringComparer.Ordinal).Count() != resources.Length ||
            families.Any(value => value is null) || families.GroupBy(value => (value.ResourceKey, value.Family)).Any(group => group.Count() != 1))
            throw new ArgumentException("Deposit manifest requirements must be non-empty and unique.");
        AdmittedProvenances = Array.AsReadOnly(provenance); RequiredResourceKeys = Array.AsReadOnly(resources);
        RequiredHostFamilies = Array.AsReadOnly(families.OrderBy(value => value.ResourceKey, StringComparer.Ordinal).ThenBy(value => value.Family).ToArray());
    }
    internal string CatalogId { get; } internal string TargetVersion { get; } internal string CatalogRevision { get; } internal Hash256 ExpectedContentFingerprint { get; }
    internal IReadOnlyList<string> AdmittedProvenances { get; } internal IReadOnlyList<string> RequiredResourceKeys { get; } internal IReadOnlyList<NativeDepositHostFamilyRequirement> RequiredHostFamilies { get; }
}

internal sealed record NativeDepositRule(
    string ResourceKey, string Provenance, string SourceAssetLocation, IReadOnlyList<string> HostRockKeys,
    DepositAltitudeRange AltitudeRange, NativeDepositParameters NativeParameters, string ShapeCode,
    string? ParentResourceKey, DepositSurfaceIndicator SurfaceIndicator, string PotentialCategory, string? GuideCode)
{
    internal string ResourceKey { get; } = DepositToken.Require(ResourceKey, nameof(ResourceKey));
    internal string Provenance { get; } = DepositToken.Require(Provenance, nameof(Provenance));
    internal string SourceAssetLocation { get; } = DepositToken.Require(SourceAssetLocation, nameof(SourceAssetLocation));
    internal IReadOnlyList<string> HostRockKeys { get; } = CopyHosts(HostRockKeys);
    internal string ShapeCode { get; } = DepositToken.Require(ShapeCode, nameof(ShapeCode));
    internal string? ParentResourceKey { get; } = ParentResourceKey is null ? null : DepositToken.Require(ParentResourceKey, nameof(ParentResourceKey));
    internal string PotentialCategory { get; } = DepositToken.Require(PotentialCategory, nameof(PotentialCategory));
    internal string? GuideCode { get; } = GuideCode is null ? null : DepositToken.Require(GuideCode, nameof(GuideCode));
    private static IReadOnlyList<string> CopyHosts(IEnumerable<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts); string[] copy = hosts.Select(value => DepositToken.Require(value, nameof(hosts))).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return copy.Length > 0 && copy.Distinct(StringComparer.Ordinal).Count() == copy.Length ? Array.AsReadOnly(copy) : throw new ArgumentException("Native rules require unique hosts.", nameof(hosts));
    }
}

internal sealed record NativeDepositHostFamilyRequirement(string ResourceKey, RockFamily Family)
{
    internal string ResourceKey { get; } = DepositToken.Require(ResourceKey, nameof(ResourceKey));
    internal RockFamily Family { get; } = Enum.IsDefined(Family) ? Family : throw new ArgumentOutOfRangeException(nameof(Family));
}

/// <summary>Validates an effective native extraction before projecting it to the Core snapshot.</summary>
internal static class NativeDepositCatalogExtractor
{
    internal static DepositCatalogSnapshot Extract(NativeDepositCatalogManifest manifest, ContentCatalogSnapshot contentCatalog, IEnumerable<NativeDepositRule> nativeRules)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(contentCatalog); ArgumentNullException.ThrowIfNull(nativeRules);
        if (manifest.CatalogId != contentCatalog.CatalogId || manifest.TargetVersion != contentCatalog.TargetVersion || manifest.ExpectedContentFingerprint != contentCatalog.Fingerprint)
            throw new InvalidOperationException("Native deposit extraction does not match the published content catalog identity.");
        NativeDepositRule[] rules = nativeRules.ToArray();
        if (rules.Length == 0 || rules.Any(rule => rule is null) || rules.GroupBy(rule => rule.ResourceKey, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            rules.GroupBy(rule => rule.SourceAssetLocation, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            rules.Any(rule => !manifest.AdmittedProvenances.Contains(rule.Provenance, StringComparer.Ordinal)))
            throw new ArgumentException("Effective native deposit rules must be unique and use admitted provenance.", nameof(nativeRules));
        if (manifest.RequiredResourceKeys.Any(resource => !rules.Any(rule => rule.ResourceKey == resource)))
            throw new ArgumentException("A required native deposit resource is absent.", nameof(nativeRules));
        foreach (NativeDepositHostFamilyRequirement requirement in manifest.RequiredHostFamilies)
            if (!rules.Any(rule => rule.ResourceKey == requirement.ResourceKey && rule.HostRockKeys.Any(host => contentCatalog.Get(host).Family == requirement.Family)))
                throw new ArgumentException("A required deposit host family is absent.", nameof(nativeRules));
        return new DepositCatalogSnapshot(manifest.CatalogRevision, contentCatalog, rules.Select(rule => new DepositDefinition(rule.ResourceKey, rule.Provenance, rule.HostRockKeys,
            rule.AltitudeRange, rule.NativeParameters, rule.ShapeCode, rule.ParentResourceKey, rule.SurfaceIndicator, rule.PotentialCategory, rule.GuideCode)));
    }
}

internal static class DepositToken
{
    internal static string Require(string? value, string parameterName) => !string.IsNullOrWhiteSpace(value) && value == value.ToLowerInvariant() && value.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '/' or '.' or '_' or '-')
        ? value : throw new ArgumentException("Deposit identifiers must be lowercase semantic tokens.", parameterName);
}
