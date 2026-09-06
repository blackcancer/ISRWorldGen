using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Runtime.Persistence;

/// <summary>Transport-agnostic storage owned by the save provider.</summary>
public interface IWorldSnapshotStore
{
    bool TryGetLength(string key, out long length);

    Stream OpenRead(string key);

    void Write(string key, ReadOnlySpan<byte> content);
}

/// <summary>Separate, disposable storage for values derived from canonical save data.</summary>
public interface IDerivedSnapshotCache
{
    void Clear();
}

public static class DerivedCacheMaintenance
{
    public static void Purge(IDerivedSnapshotCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.Clear();
    }
}

public static class PersistenceKeys
{
    public const string RootNamespace = "isrworldgen/persistence/v1/";
    public const string CanonicalPrefix = RootNamespace + "canonical/";
    public const string CanonicalSnapshotPrefix = CanonicalPrefix + "snapshots/";
    public const string DerivedCachePrefix = RootNamespace + "derived-cache/";
    public const string Manifest = CanonicalPrefix + "world-manifest";

    public static string Snapshot(StableId id, ulong revision, Hash256 payloadHash) =>
        string.Concat(
            CanonicalSnapshotPrefix,
            id.ToString(),
            "/",
            revision.ToString("x16", CultureInfo.InvariantCulture),
            "/",
            payloadHash.ToString());
}

public sealed record PersistenceLimits
{
    public static PersistenceLimits Default { get; } = new(
        maxEnvelopeBytes: 512L * 1024 * 1024,
        maxManifestDecodedBytes: 4L * 1024 * 1024,
        maxSnapshotDecodedBytes: 256L * 1024 * 1024,
        maxFrozenConfigurationBytes: 1024 * 1024,
        maxSnapshotCount: 100_000,
        maxParentsPerSnapshot: 1_024,
        maxStringUtf8Bytes: 16 * 1024);

    public PersistenceLimits(
        long maxEnvelopeBytes,
        long maxManifestDecodedBytes,
        long maxSnapshotDecodedBytes,
        int maxFrozenConfigurationBytes,
        int maxSnapshotCount,
        int maxParentsPerSnapshot,
        int maxStringUtf8Bytes)
    {
        MaxEnvelopeBytes = RequireByteLimit(maxEnvelopeBytes, nameof(maxEnvelopeBytes));
        MaxManifestDecodedBytes = RequireByteLimit(maxManifestDecodedBytes, nameof(maxManifestDecodedBytes));
        MaxSnapshotDecodedBytes = RequireByteLimit(maxSnapshotDecodedBytes, nameof(maxSnapshotDecodedBytes));
        MaxFrozenConfigurationBytes = RequirePositive(maxFrozenConfigurationBytes, nameof(maxFrozenConfigurationBytes));
        MaxSnapshotCount = RequirePositive(maxSnapshotCount, nameof(maxSnapshotCount));
        MaxParentsPerSnapshot = RequirePositive(maxParentsPerSnapshot, nameof(maxParentsPerSnapshot));
        MaxStringUtf8Bytes = RequirePositive(maxStringUtf8Bytes, nameof(maxStringUtf8Bytes));

        if (MaxEnvelopeBytes <= PersistenceEnvelope.HeaderByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEnvelopeBytes),
                maxEnvelopeBytes,
                "The envelope limit must leave room for a framed payload.");
        }

        if (MaxManifestDecodedBytes > MaxEnvelopeBytes - PersistenceEnvelope.HeaderByteCount ||
            MaxSnapshotDecodedBytes > MaxEnvelopeBytes - PersistenceEnvelope.HeaderByteCount)
        {
            throw new ArgumentException("Decoded limits must fit inside the raw v1 envelope limit.");
        }

        if (MaxFrozenConfigurationBytes > MaxManifestDecodedBytes || MaxStringUtf8Bytes > MaxManifestDecodedBytes)
        {
            throw new ArgumentException("Manifest member limits must not exceed the manifest limit.");
        }
    }

    public long MaxEnvelopeBytes { get; }

    public long MaxManifestDecodedBytes { get; }

    public long MaxSnapshotDecodedBytes { get; }

    public int MaxFrozenConfigurationBytes { get; }

    public int MaxSnapshotCount { get; }

    public int MaxParentsPerSnapshot { get; }

    public int MaxStringUtf8Bytes { get; }

    private static long RequireByteLimit(long value, string parameterName)
    {
        if (value <= 0 || value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Byte limits must be in [1, Int32.MaxValue].");
        }

        return value;
    }

    private static int RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Limits must be positive.");
        }

        return value;
    }
}

public readonly record struct SnapshotParentReference(
    StableId Id,
    ulong Revision,
    Hash256 PayloadHash);

public sealed class SnapshotReference
{
    public SnapshotReference(
        StableId id,
        ulong revision,
        Hash256 payloadHash,
        string kind,
        IEnumerable<SnapshotParentReference> parents)
    {
        ArgumentNullException.ThrowIfNull(parents);
        SnapshotParentReference[] canonicalParents = parents.ToArray();
        Array.Sort(canonicalParents, SnapshotParentReferenceComparer.Instance);
        for (int index = 1; index < canonicalParents.Length; index++)
        {
            if (SnapshotParentReferenceComparer.Instance.Compare(canonicalParents[index - 1], canonicalParents[index]) == 0)
            {
                throw new ArgumentException("Duplicate snapshot parent references are forbidden.", nameof(parents));
            }
        }

        Id = id;
        Revision = revision;
        PayloadHash = payloadHash;
        Kind = PersistenceText.Require(kind, nameof(kind));
        Parents = Array.AsReadOnly(canonicalParents);
    }

    public StableId Id { get; }

    public ulong Revision { get; }

    public Hash256 PayloadHash { get; }

    public string Kind { get; }

    public ReadOnlyCollection<SnapshotParentReference> Parents { get; }

    public string StorageKey => PersistenceKeys.Snapshot(Id, Revision, PayloadHash);
}

public sealed class WorldManifest
{
    public const uint FormatVersion = 1;

    private readonly byte[] frozenConfiguration;

    public WorldManifest(
        GenerationIdentity identity,
        SnapshotUnits units,
        ReadOnlySpan<byte> frozenGeographyConfiguration,
        IEnumerable<SnapshotReference> snapshots)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(snapshots);

        byte[] configurationCopy = frozenGeographyConfiguration.ToArray();
        if (Hash256.Compute(configurationCopy) != identity.GeographyConfigHash)
        {
            throw new ArgumentException(
                "Frozen geography configuration does not match the identity configuration hash.",
                nameof(frozenGeographyConfiguration));
        }

        SnapshotReference[] canonicalSnapshots = snapshots.ToArray();
        if (canonicalSnapshots.Any(reference => reference is null))
        {
            throw new ArgumentException("Snapshot references cannot contain null.", nameof(snapshots));
        }

        Array.Sort(canonicalSnapshots, SnapshotReferenceComparer.Instance);
        for (int index = 1; index < canonicalSnapshots.Length; index++)
        {
            if (SnapshotReferenceComparer.Instance.Compare(canonicalSnapshots[index - 1], canonicalSnapshots[index]) == 0)
            {
                throw new ArgumentException("Duplicate snapshot ID/revision pairs are forbidden.", nameof(snapshots));
            }
        }

        Identity = identity;
        Units = units;
        frozenConfiguration = configurationCopy;
        Snapshots = Array.AsReadOnly(canonicalSnapshots);
    }

    public GenerationIdentity Identity { get; }

    public SnapshotUnits Units { get; }

    public ReadOnlyCollection<SnapshotReference> Snapshots { get; }

    public byte[] GetFrozenConfigurationCopy() => frozenConfiguration.ToArray();
}

public sealed record WorldCompatibilityProfile(
    int NativeSeed,
    uint AlgorithmVersion,
    uint SnapshotSchemaVersion,
    Hash256 GeographyConfigHash,
    Hash256 GenerationAssetHash,
    string DeterminismProfileId,
    SnapshotUnits Units)
{
    public static WorldCompatibilityProfile FromManifest(WorldManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new WorldCompatibilityProfile(
            manifest.Identity.NativeSeed,
            manifest.Identity.AlgorithmVersion,
            manifest.Identity.SchemaVersion,
            manifest.Identity.GeographyConfigHash,
            manifest.Identity.GenerationAssetHash,
            manifest.Identity.DeterminismProfileId,
            manifest.Units);
    }
}

public enum PersistenceErrorCode
{
    WorldNotActivated = 1,
    UnsupportedVersion = 2,
    UnsupportedEncoding = 3,
    TruncatedData = 4,
    PayloadTooLarge = 5,
    ChecksumMismatch = 6,
    CorruptData = 7,
    MissingSnapshot = 8,
    MissingParent = 9,
    IncompatibleSeed = 10,
    IncompatibleAlgorithm = 11,
    IncompatibleSchema = 12,
    IncompatibleConfiguration = 13,
    IncompatibleAssets = 14,
    IncompatibleDeterminismProfile = 15,
    IncompatibleUnits = 16,
    StorageFailure = 17,
}

public sealed record PersistenceError
{
    public PersistenceError(PersistenceErrorCode code, string storageKey, string details)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown persistence error code.");
        }

        Code = code;
        StorageKey = PersistenceText.Require(storageKey, nameof(storageKey));
        Details = PersistenceText.Require(details, nameof(details));
    }

    public PersistenceErrorCode Code { get; }

    public string StorageKey { get; }

    public string Details { get; }
}

public abstract class PersistenceResult<T>
{
    private protected PersistenceResult(bool isSuccess) => IsSuccess = isSuccess;

    public bool IsSuccess { get; }

    public static PersistenceResult<T> Success(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new PersistenceSuccess<T>(value);
    }

    public static PersistenceResult<T> Failure(PersistenceError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new PersistenceFailure<T>(error);
    }
}

public sealed class PersistenceSuccess<T> : PersistenceResult<T>
{
    internal PersistenceSuccess(T value)
        : base(true) => Value = value;

    public T Value { get; }
}

public sealed class PersistenceFailure<T> : PersistenceResult<T>
{
    internal PersistenceFailure(PersistenceError error)
        : base(false) => Error = error;

    public PersistenceError Error { get; }
}

public sealed class StoredSnapshot
{
    private readonly byte[] payload;

    internal StoredSnapshot(SnapshotReference reference, ReadOnlySpan<byte> payload)
    {
        Reference = reference;
        this.payload = payload.ToArray();
    }

    public SnapshotReference Reference { get; }

    public byte[] GetPayloadCopy() => payload.ToArray();
}

public sealed class RestoredWorld
{
    private readonly Dictionary<SnapshotLocator, StoredSnapshot> byLocator;

    internal RestoredWorld(WorldManifest manifest, IEnumerable<StoredSnapshot> snapshots)
    {
        Manifest = manifest;
        StoredSnapshot[] values = snapshots.ToArray();
        Snapshots = Array.AsReadOnly(values);
        byLocator = values.ToDictionary(
            snapshot => new SnapshotLocator(snapshot.Reference.Id, snapshot.Reference.Revision));
    }

    public WorldManifest Manifest { get; }

    public ReadOnlyCollection<StoredSnapshot> Snapshots { get; }

    public StoredSnapshot GetSnapshot(StableId id, ulong revision) => byLocator[new SnapshotLocator(id, revision)];
}

internal readonly record struct SnapshotLocator(StableId Id, ulong Revision);

internal sealed class SnapshotReferenceComparer : IComparer<SnapshotReference>
{
    internal static SnapshotReferenceComparer Instance { get; } = new();

    public int Compare(SnapshotReference? left, SnapshotReference? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        int idComparison = CompareStableIds(left.Id, right.Id);
        return idComparison != 0 ? idComparison : left.Revision.CompareTo(right.Revision);
    }

    internal static int CompareStableIds(StableId left, StableId right)
    {
        int high = left.High.CompareTo(right.High);
        return high != 0 ? high : left.Low.CompareTo(right.Low);
    }
}

internal sealed class SnapshotParentReferenceComparer : IComparer<SnapshotParentReference>
{
    internal static SnapshotParentReferenceComparer Instance { get; } = new();

    public int Compare(SnapshotParentReference left, SnapshotParentReference right)
    {
        int idComparison = SnapshotReferenceComparer.CompareStableIds(left.Id, right.Id);
        if (idComparison != 0)
        {
            return idComparison;
        }

        int revisionComparison = left.Revision.CompareTo(right.Revision);
        return revisionComparison != 0 ? revisionComparison : left.PayloadHash.CompareTo(right.PayloadHash);
    }
}

internal static class PersistenceText
{
    internal static string Require(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Canonical persistence text cannot be empty or whitespace.", parameterName);
        }

        if (!value.IsNormalized(NormalizationForm.FormC))
        {
            throw new ArgumentException("Canonical persistence text must use Unicode normalization form C.", parameterName);
        }

        return value;
    }
}
