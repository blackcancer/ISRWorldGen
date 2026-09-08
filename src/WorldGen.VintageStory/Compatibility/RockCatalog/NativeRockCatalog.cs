using System.Collections.ObjectModel;
using System.Text;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Compatibility.RockCatalog;

/// <summary>
/// Immutable semantic inventory of the effective native rock assets.  It deliberately
/// contains no native numeric block identifiers and does not register a worldgen pass.
/// </summary>
internal sealed class NativeRockCatalogSnapshot
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal NativeRockCatalogSnapshot(
        string catalogId,
        string targetVersion,
        string assetRevision,
        IEnumerable<NativeAssetRecord> effectiveAssets,
        IEnumerable<NativeRockDefinition> rocks,
        IEnumerable<NativeResourceHost> resourceHosts,
        IEnumerable<NativeResourceHost> forbiddenHosts)
    {
        CatalogId = RequireToken(catalogId, nameof(catalogId));
        TargetVersion = RequireToken(targetVersion, nameof(targetVersion));
        AssetRevision = RequireToken(assetRevision, nameof(assetRevision));
        ArgumentNullException.ThrowIfNull(effectiveAssets);
        ArgumentNullException.ThrowIfNull(rocks);
        ArgumentNullException.ThrowIfNull(resourceHosts);
        ArgumentNullException.ThrowIfNull(forbiddenHosts);

        NativeAssetRecord[] assets = effectiveAssets.ToArray();
        NativeRockDefinition[] suppliedRocks = rocks.ToArray();
        NativeResourceHost[] suppliedHosts = resourceHosts.ToArray();
        NativeResourceHost[] suppliedForbiddenHosts = forbiddenHosts.ToArray();
        ValidateAssets(assets, AssetRevision);
        ValidateRocks(suppliedRocks, assets, AssetRevision);
        ValidateHosts(suppliedHosts, suppliedForbiddenHosts, suppliedRocks);

        Rocks = Array.AsReadOnly(suppliedRocks.OrderBy(rock => rock.RockKey, StringComparer.Ordinal).ToArray());
        ResourceHosts = Array.AsReadOnly(suppliedHosts
            .OrderBy(host => host.ResourceKey, StringComparer.Ordinal)
            .ThenBy(host => host.RockKey, StringComparer.Ordinal)
            .ThenBy(host => host.Requirement)
            .ToArray());
        ForbiddenHosts = Array.AsReadOnly(suppliedForbiddenHosts
            .OrderBy(host => host.ResourceKey, StringComparer.Ordinal)
            .ThenBy(host => host.RockKey, StringComparer.Ordinal)
            .ToArray());
        Fingerprint = Hash256.Compute(StrictUtf8.GetBytes(CreateCanonicalExport()));
    }

    internal string CatalogId { get; }
    internal string TargetVersion { get; }
    internal string AssetRevision { get; }
    internal IReadOnlyList<NativeRockDefinition> Rocks { get; }
    internal IReadOnlyList<NativeResourceHost> ResourceHosts { get; }
    internal IReadOnlyList<NativeResourceHost> ForbiddenHosts { get; }
    internal Hash256 Fingerprint { get; }

    internal string CreateCanonicalExport()
    {
        var builder = new StringBuilder();
        builder.Append("catalog=").Append(CatalogId)
            .Append(";target=").Append(TargetVersion)
            .Append(";revision=").Append(AssetRevision).Append('\n');
        foreach (NativeRockDefinition rock in Rocks)
        {
            builder.Append("rock=").Append(rock.RockKey)
                .Append(";code=").Append(rock.NativeCode)
                .Append(";family=").Append(rock.Family)
                .Append(";asset=").Append(rock.AssetLocation)
                .Append(";provenance=").Append(rock.Provenance)
                .Append(";weight=").Append(rock.RelativeWeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }

        foreach (NativeResourceHost host in ResourceHosts)
        {
            builder.Append("host=").Append(host.ResourceKey)
                .Append(";rock=").Append(host.RockKey)
                .Append(";requirement=").Append(host.Requirement).Append('\n');
        }

        foreach (NativeResourceHost host in ForbiddenHosts)
        {
            builder.Append("forbidden=").Append(host.ResourceKey)
                .Append(";rock=").Append(host.RockKey).Append('\n');
        }

        return builder.ToString();
    }

    private static void ValidateAssets(NativeAssetRecord[] assets, string revision)
    {
        if (assets.Length == 0 || assets.Any(asset => asset is null) ||
            assets.Any(asset => !string.Equals(asset.AssetRevision, revision, StringComparison.Ordinal)) ||
            assets.GroupBy(asset => asset.AssetLocation, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Effective assets must be non-empty, uniquely located, and from one declared revision.");
        }
    }

    private static void ValidateRocks(NativeRockDefinition[] rocks, NativeAssetRecord[] assets, string revision)
    {
        if (rocks.Length == 0 || rocks.Any(rock => rock is null) ||
            rocks.GroupBy(rock => rock.RockKey, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            rocks.GroupBy(rock => rock.NativeCode, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Rock keys and native codes must be unique and non-empty.");
        }

        var assetsByLocation = assets.ToDictionary(asset => asset.AssetLocation, StringComparer.Ordinal);
        foreach (NativeRockDefinition rock in rocks)
        {
            if (!assetsByLocation.TryGetValue(rock.AssetLocation, out NativeAssetRecord? asset) ||
                !string.Equals(asset.Provenance, rock.Provenance, StringComparison.Ordinal) ||
                !string.Equals(asset.AssetRevision, revision, StringComparison.Ordinal))
            {
                throw new ArgumentException("Every rock must point to an effective asset with matching provenance and revision.");
            }
        }
    }

    private static void ValidateHosts(
        NativeResourceHost[] hosts,
        NativeResourceHost[] forbiddenHosts,
        NativeRockDefinition[] rocks)
    {
        if (hosts.Any(host => host is null) || forbiddenHosts.Any(host => host is null) ||
            hosts.GroupBy(host => (host.ResourceKey, host.RockKey), StringTupleComparer.Ordinal).Any(group => group.Count() != 1) ||
            forbiddenHosts.GroupBy(host => (host.ResourceKey, host.RockKey), StringTupleComparer.Ordinal).Any(group => group.Count() != 1))
        {
            throw new ArgumentException("Resource/host associations must be unique.");
        }

        var rockKeys = rocks.Select(rock => rock.RockKey).ToHashSet(StringComparer.Ordinal);
        if (hosts.Concat(forbiddenHosts).Any(host => !rockKeys.Contains(host.RockKey)) ||
            forbiddenHosts.Any(forbidden => hosts.Any(host =>
                string.Equals(host.ResourceKey, forbidden.ResourceKey, StringComparison.Ordinal) &&
                string.Equals(host.RockKey, forbidden.RockKey, StringComparison.Ordinal))))
        {
            throw new ArgumentException("Resource hosts must name catalog rocks and cannot also be forbidden.");
        }

        foreach (string resource in hosts.Where(host => host.Requirement == NativeHostRequirement.Required)
                     .Select(host => host.ResourceKey).Distinct(StringComparer.Ordinal))
        {
            if (!hosts.Any(host => host.Requirement == NativeHostRequirement.Required &&
                                   string.Equals(host.ResourceKey, resource, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Every required resource must retain a compatible host.");
            }
        }
    }

    private static string RequireToken(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.ToLowerInvariant() ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is ':' or '/' or '.' or '_' or '-')))
        {
            throw new ArgumentException("Catalog identifiers must be lowercase semantic tokens.", parameterName);
        }

        return value;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string ResourceKey, string RockKey)>
    {
        internal static StringTupleComparer Ordinal { get; } = new();
        public bool Equals((string ResourceKey, string RockKey) x, (string ResourceKey, string RockKey) y) =>
            string.Equals(x.ResourceKey, y.ResourceKey, StringComparison.Ordinal) &&
            string.Equals(x.RockKey, y.RockKey, StringComparison.Ordinal);
        public int GetHashCode((string ResourceKey, string RockKey) value) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.ResourceKey), StringComparer.Ordinal.GetHashCode(value.RockKey));
    }
}

internal sealed record NativeAssetRecord(string AssetLocation, string Provenance, string AssetRevision)
{
    internal string AssetLocation { get; } = NativeRockCatalogSnapshotToken.Require(AssetLocation, nameof(AssetLocation));
    internal string Provenance { get; } = NativeRockCatalogSnapshotToken.Require(Provenance, nameof(Provenance));
    internal string AssetRevision { get; } = NativeRockCatalogSnapshotToken.Require(AssetRevision, nameof(AssetRevision));
}

internal sealed record NativeRockDefinition(string RockKey, string NativeCode, NativeRockFamily Family, string AssetLocation, string Provenance, double RelativeWeight)
{
    internal string RockKey { get; } = NativeRockCatalogSnapshotToken.Require(RockKey, nameof(RockKey));
    internal string NativeCode { get; } = NativeRockCatalogSnapshotToken.Require(NativeCode, nameof(NativeCode));
    internal string AssetLocation { get; } = NativeRockCatalogSnapshotToken.Require(AssetLocation, nameof(AssetLocation));
    internal string Provenance { get; } = NativeRockCatalogSnapshotToken.Require(Provenance, nameof(Provenance));
    internal NativeRockFamily Family { get; } = Enum.IsDefined(Family) ? Family : throw new ArgumentOutOfRangeException(nameof(Family));
    internal double RelativeWeight { get; } = double.IsFinite(RelativeWeight) && RelativeWeight > 0d ? RelativeWeight : throw new ArgumentOutOfRangeException(nameof(RelativeWeight));
}

internal sealed record NativeResourceHost(string ResourceKey, string RockKey, NativeHostRequirement Requirement)
{
    internal string ResourceKey { get; } = NativeRockCatalogSnapshotToken.Require(ResourceKey, nameof(ResourceKey));
    internal string RockKey { get; } = NativeRockCatalogSnapshotToken.Require(RockKey, nameof(RockKey));
    internal NativeHostRequirement Requirement { get; } = Enum.IsDefined(Requirement) ? Requirement : throw new ArgumentOutOfRangeException(nameof(Requirement));
}

internal enum NativeRockFamily { Sedimentary, Igneous, Metamorphic, Impact }
internal enum NativeHostRequirement { Optional, Required }

internal static class NativeRockCatalogSnapshotToken
{
    internal static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.ToLowerInvariant() ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is ':' or '/' or '.' or '_' or '-')))
        {
            throw new ArgumentException("Catalog identifiers must be lowercase semantic tokens.", parameterName);
        }

        return value;
    }
}
