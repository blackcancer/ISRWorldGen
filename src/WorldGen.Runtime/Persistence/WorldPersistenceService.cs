using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Runtime.Persistence;

/// <summary>
/// Reads and writes canonical world-generation state through a save-owned store.
/// Each key is independently framed; multi-key crash publication belongs to L10-B.
/// </summary>
public sealed class WorldPersistenceService
{
    private readonly IWorldSnapshotStore store;
    private readonly PersistenceLimits limits;

    public WorldPersistenceService(IWorldSnapshotStore store, PersistenceLimits? limits = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.limits = limits ?? PersistenceLimits.Default;
    }

    public PersistenceResult<SnapshotReference> WriteSnapshot(
        SnapshotReference reference,
        ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (payload.Length > limits.MaxSnapshotDecodedBytes)
        {
            return Failure<SnapshotReference>(
                PersistenceErrorCode.PayloadTooLarge,
                reference.StorageKey,
                $"Snapshot length {payload.Length} exceeds the configured limit {limits.MaxSnapshotDecodedBytes}.");
        }

        byte[] ownedPayload = payload.ToArray();
        Hash256 payloadHash = Hash256.Compute(ownedPayload);
        if (payloadHash != reference.PayloadHash)
        {
            return Failure<SnapshotReference>(
                PersistenceErrorCode.ChecksumMismatch,
                reference.StorageKey,
                "Snapshot content does not match its canonical reference hash.");
        }

        PersistenceResult<byte[]> encoded = PersistenceEnvelope.Encode(
            ownedPayload,
            payloadHash,
            limits.MaxSnapshotDecodedBytes,
            limits.MaxEnvelopeBytes,
            reference.StorageKey);
        if (encoded is PersistenceFailure<byte[]> failure)
        {
            return PersistenceResult<SnapshotReference>.Failure(failure.Error);
        }

        return WriteValue(reference.StorageKey, ((PersistenceSuccess<byte[]>)encoded).Value, reference);
    }

    public PersistenceResult<WorldManifest> WriteManifest(WorldManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        PersistenceResult<byte[]> serialized = WorldManifestCodec.Serialize(manifest, limits, PersistenceKeys.Manifest);
        if (serialized is PersistenceFailure<byte[]> serializationFailure)
        {
            return PersistenceResult<WorldManifest>.Failure(serializationFailure.Error);
        }

        PersistenceError? graphError = ValidateGraph(manifest);
        if (graphError is not null)
        {
            return PersistenceResult<WorldManifest>.Failure(graphError);
        }

        byte[] manifestPayload = ((PersistenceSuccess<byte[]>)serialized).Value;
        PersistenceResult<byte[]> encoded = PersistenceEnvelope.Encode(
            manifestPayload,
            Hash256.Compute(manifestPayload),
            limits.MaxManifestDecodedBytes,
            limits.MaxEnvelopeBytes,
            PersistenceKeys.Manifest);
        if (encoded is PersistenceFailure<byte[]> envelopeFailure)
        {
            return PersistenceResult<WorldManifest>.Failure(envelopeFailure.Error);
        }

        return WriteValue(PersistenceKeys.Manifest, ((PersistenceSuccess<byte[]>)encoded).Value, manifest);
    }

    public PersistenceResult<RestoredWorld> Restore(WorldCompatibilityProfile expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        PersistenceResult<bool> manifestPresence = Contains(PersistenceKeys.Manifest);
        if (manifestPresence is PersistenceFailure<bool> presenceFailure)
        {
            return PersistenceResult<RestoredWorld>.Failure(presenceFailure.Error);
        }

        if (!((PersistenceSuccess<bool>)manifestPresence).Value)
        {
            return Failure<RestoredWorld>(
                PersistenceErrorCode.WorldNotActivated,
                PersistenceKeys.Manifest,
                "ISRWorldGen persistence is not activated for this world; no canonical manifest exists.");
        }

        PersistenceResult<byte[]> manifestEnvelope = PersistenceEnvelope.Read(
            store,
            PersistenceKeys.Manifest,
            limits.MaxManifestDecodedBytes,
            limits.MaxEnvelopeBytes);
        if (manifestEnvelope is PersistenceFailure<byte[]> manifestEnvelopeFailure)
        {
            return PersistenceResult<RestoredWorld>.Failure(manifestEnvelopeFailure.Error);
        }

        PersistenceResult<WorldManifest> decodedManifest = WorldManifestCodec.Deserialize(
            ((PersistenceSuccess<byte[]>)manifestEnvelope).Value,
            limits,
            PersistenceKeys.Manifest);
        if (decodedManifest is PersistenceFailure<WorldManifest> manifestFailure)
        {
            return PersistenceResult<RestoredWorld>.Failure(manifestFailure.Error);
        }

        WorldManifest manifest = ((PersistenceSuccess<WorldManifest>)decodedManifest).Value;
        PersistenceError? compatibilityError = ValidateCompatibility(manifest, expected);
        if (compatibilityError is not null)
        {
            return PersistenceResult<RestoredWorld>.Failure(compatibilityError);
        }

        PersistenceError? graphError = ValidateGraph(manifest);
        if (graphError is not null)
        {
            return PersistenceResult<RestoredWorld>.Failure(graphError);
        }

        HashSet<SnapshotLocator> parents = manifest.Snapshots
            .SelectMany(reference => reference.Parents)
            .Select(parent => new SnapshotLocator(parent.Id, parent.Revision))
            .ToHashSet();
        var restoredSnapshots = new List<StoredSnapshot>(manifest.Snapshots.Count);

        foreach (SnapshotReference reference in manifest.Snapshots)
        {
            PersistenceResult<bool> presence = Contains(reference.StorageKey);
            if (presence is PersistenceFailure<bool> snapshotPresenceFailure)
            {
                return PersistenceResult<RestoredWorld>.Failure(snapshotPresenceFailure.Error);
            }

            if (!((PersistenceSuccess<bool>)presence).Value)
            {
                SnapshotLocator locator = new(reference.Id, reference.Revision);
                PersistenceErrorCode code = parents.Contains(locator)
                    ? PersistenceErrorCode.MissingParent
                    : PersistenceErrorCode.MissingSnapshot;
                return Failure<RestoredWorld>(code, reference.StorageKey, "A canonical snapshot referenced by the manifest is absent.");
            }

            PersistenceResult<byte[]> snapshotEnvelope = PersistenceEnvelope.Read(
                store,
                reference.StorageKey,
                limits.MaxSnapshotDecodedBytes,
                limits.MaxEnvelopeBytes);
            if (snapshotEnvelope is PersistenceFailure<byte[]> snapshotFailure)
            {
                return PersistenceResult<RestoredWorld>.Failure(snapshotFailure.Error);
            }

            byte[] payload = ((PersistenceSuccess<byte[]>)snapshotEnvelope).Value;
            if (Hash256.Compute(payload) != reference.PayloadHash)
            {
                return Failure<RestoredWorld>(
                    PersistenceErrorCode.ChecksumMismatch,
                    reference.StorageKey,
                    "Snapshot payload does not match the hash recorded in the manifest.");
            }

            restoredSnapshots.Add(new StoredSnapshot(reference, payload));
        }

        return PersistenceResult<RestoredWorld>.Success(new RestoredWorld(manifest, restoredSnapshots));
    }

    private static PersistenceError? ValidateCompatibility(
        WorldManifest manifest,
        WorldCompatibilityProfile expected)
    {
        GenerationIdentity actual = manifest.Identity;
        if (actual.NativeSeed != expected.NativeSeed)
        {
            return CompatibilityFailure(PersistenceErrorCode.IncompatibleSeed, "native seed");
        }

        if (actual.AlgorithmVersion != expected.AlgorithmVersion)
        {
            return CompatibilityFailure(PersistenceErrorCode.IncompatibleAlgorithm, "algorithm version");
        }

        if (actual.SchemaVersion != expected.SnapshotSchemaVersion)
        {
            return CompatibilityFailure(PersistenceErrorCode.IncompatibleSchema, "snapshot schema version");
        }

        if (actual.GeographyConfigHash != expected.GeographyConfigHash)
        {
            return CompatibilityFailure(PersistenceErrorCode.IncompatibleConfiguration, "geography configuration hash");
        }

        if (actual.GenerationAssetHash != expected.GenerationAssetHash)
        {
            return CompatibilityFailure(PersistenceErrorCode.IncompatibleAssets, "generation asset hash");
        }

        if (!string.Equals(actual.DeterminismProfileId, expected.DeterminismProfileId, StringComparison.Ordinal))
        {
            return CompatibilityFailure(PersistenceErrorCode.IncompatibleDeterminismProfile, "determinism profile");
        }

        if (expected.Units is null ||
            !string.Equals(manifest.Units.Horizontal, expected.Units.Horizontal, StringComparison.Ordinal) ||
            !string.Equals(manifest.Units.Vertical, expected.Units.Vertical, StringComparison.Ordinal))
        {
            return CompatibilityFailure(PersistenceErrorCode.IncompatibleUnits, "snapshot units");
        }

        return null;
    }

    private static PersistenceError? ValidateGraph(WorldManifest manifest)
    {
        var references = manifest.Snapshots.ToDictionary(
            reference => new SnapshotLocator(reference.Id, reference.Revision));
        var remainingParents = new Dictionary<SnapshotLocator, int>(references.Count);
        var dependents = new Dictionary<SnapshotLocator, List<SnapshotLocator>>();

        foreach (SnapshotReference reference in manifest.Snapshots)
        {
            SnapshotLocator referenceLocator = new(reference.Id, reference.Revision);
            remainingParents.Add(referenceLocator, reference.Parents.Count);
            foreach (SnapshotParentReference parent in reference.Parents)
            {
                SnapshotLocator parentLocator = new(parent.Id, parent.Revision);
                if (!references.TryGetValue(parentLocator, out SnapshotReference? parentReference))
                {
                    return new PersistenceError(
                        PersistenceErrorCode.MissingParent,
                        PersistenceKeys.Snapshot(parent.Id, parent.Revision, parent.PayloadHash),
                        $"Parent of snapshot {reference.StorageKey} is absent from the manifest.");
                }

                if (parentReference.PayloadHash != parent.PayloadHash)
                {
                    return new PersistenceError(
                        PersistenceErrorCode.CorruptData,
                        reference.StorageKey,
                        "A parent reference hash disagrees with the referenced manifest entry.");
                }

                if (!dependents.TryGetValue(parentLocator, out List<SnapshotLocator>? children))
                {
                    children = [];
                    dependents.Add(parentLocator, children);
                }

                children.Add(referenceLocator);
            }
        }

        var ready = new Queue<SnapshotLocator>(remainingParents
            .Where(entry => entry.Value == 0)
            .Select(entry => entry.Key));
        int visitedCount = 0;
        while (ready.TryDequeue(out SnapshotLocator locator))
        {
            visitedCount++;
            if (!dependents.TryGetValue(locator, out List<SnapshotLocator>? children))
            {
                continue;
            }

            foreach (SnapshotLocator child in children)
            {
                int count = remainingParents[child] - 1;
                remainingParents[child] = count;
                if (count == 0)
                {
                    ready.Enqueue(child);
                }
            }
        }

        if (visitedCount != references.Count)
        {
            SnapshotReference cycleMember = manifest.Snapshots.First(reference =>
                remainingParents[new SnapshotLocator(reference.Id, reference.Revision)] > 0);
            return new PersistenceError(
                PersistenceErrorCode.CorruptData,
                cycleMember.StorageKey,
                "The snapshot dependency graph contains a cycle.");
        }

        return null;
    }

    private PersistenceResult<bool> Contains(string storageKey)
    {
        try
        {
            return PersistenceResult<bool>.Success(store.TryGetLength(storageKey, out _));
        }
        catch (IOException exception)
        {
            return StorageFailure<bool>(storageKey, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return StorageFailure<bool>(storageKey, exception);
        }
    }

    private PersistenceResult<T> WriteValue<T>(string storageKey, byte[] content, T value)
    {
        try
        {
            store.Write(storageKey, content);
            return PersistenceResult<T>.Success(value);
        }
        catch (IOException exception)
        {
            return StorageFailure<T>(storageKey, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return StorageFailure<T>(storageKey, exception);
        }
    }

    private static PersistenceError CompatibilityFailure(PersistenceErrorCode code, string member) =>
        new(code, PersistenceKeys.Manifest, $"The saved and expected {member} values are incompatible.");

    private static PersistenceResult<T> StorageFailure<T>(string storageKey, Exception exception) =>
        Failure<T>(PersistenceErrorCode.StorageFailure, storageKey, $"Storage operation failed: {exception.Message}");

    private static PersistenceResult<T> Failure<T>(
        PersistenceErrorCode code,
        string storageKey,
        string details) => PersistenceResult<T>.Failure(new PersistenceError(code, storageKey, details));

}
