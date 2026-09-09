using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Runtime.Persistence;

namespace ISRWorldGen.Tests.L10A;

internal sealed class InMemoryWorldSnapshotStore : IWorldSnapshotStore
{
    private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> reportedLengths = new(StringComparer.Ordinal);
    private readonly Action<string>? beforeWrite;
    private readonly Func<string, byte[], byte[]>? transformWrittenContent;

    internal InMemoryWorldSnapshotStore(
        Action<string>? beforeWrite = null,
        Func<string, byte[], byte[]>? transformWrittenContent = null)
    {
        this.beforeWrite = beforeWrite;
        this.transformWrittenContent = transformWrittenContent;
    }

    internal int WriteCount { get; private set; }

    internal int OpenReadCount { get; private set; }

    public bool TryGetLength(string key, out long length)
    {
        if (reportedLengths.TryGetValue(key, out length))
        {
            return true;
        }

        if (values.TryGetValue(key, out byte[]? value))
        {
            length = value.Length;
            return true;
        }

        length = 0;
        return false;
    }

    public Stream OpenRead(string key)
    {
        OpenReadCount++;
        return new MemoryStream(values[key], writable: false);
    }

    public void Write(string key, ReadOnlySpan<byte> content)
    {
        beforeWrite?.Invoke(key);
        byte[] ownedContent = content.ToArray();
        values[key] = transformWrittenContent?.Invoke(key, ownedContent) ?? ownedContent;
        reportedLengths.Remove(key);
        WriteCount++;
    }

    internal byte[] GetRaw(string key) => values[key].ToArray();

    internal void PutRaw(string key, ReadOnlySpan<byte> content) => values[key] = content.ToArray();

    internal void Remove(string key) => values.Remove(key);

    internal void ReportLength(string key, long length) => reportedLengths[key] = length;

    internal void CopyCanonicalDataTo(InMemoryWorldSnapshotStore destination)
    {
        foreach ((string key, byte[] value) in values)
        {
            if (key.StartsWith(PersistenceKeys.CanonicalPrefix, StringComparison.Ordinal))
            {
                destination.PutRaw(key, value);
            }
        }
    }
}

internal sealed class RecordingDerivedSnapshotCache : IDerivedSnapshotCache
{
    internal int EntryCount { get; set; }

    internal int ClearCount { get; private set; }

    public void Clear()
    {
        EntryCount = 0;
        ClearCount++;
    }
}

internal sealed record PersistenceFixture(
    InMemoryWorldSnapshotStore Store,
    WorldPersistenceService Service,
    WorldManifest Manifest,
    SnapshotReference Parent,
    SnapshotReference Child,
    byte[] ParentPayload,
    byte[] ChildPayload,
    byte[] FrozenConfiguration,
    WorldCompatibilityProfile Compatibility);

internal static class PersistenceTestData
{
    internal static readonly PersistenceLimits Limits = new(
        maxEnvelopeBytes: 256 * 1024,
        maxManifestDecodedBytes: 64 * 1024,
        maxSnapshotDecodedBytes: 128 * 1024,
        maxFrozenConfigurationBytes: 4 * 1024,
        maxSnapshotCount: 32,
        maxParentsPerSnapshot: 8,
        maxStringUtf8Bytes: 256);

    internal static PersistenceFixture Create(bool writeParentPayload = true, bool writeChildPayload = true)
    {
        byte[] frozenConfiguration = Encoding.UTF8.GetBytes("{\"profile\":\"test-v1\",\"seaLevel\":110}");
        var identity = new GenerationIdentity(
            73,
            GenerationIdentity.SupportedAlgorithmVersion,
            GenerationIdentity.SupportedSchemaVersion,
            Hash256.Compute(frozenConfiguration),
            Hash256.Compute("l10a-assets-v1"u8),
            "net10-x64-l10a-v1");
        var units = new SnapshotUnits("block", "block/256");

        byte[] parentPayload = CreateHeightPayload(identity);
        byte[] childPayload = CreateVoxelPayload(identity, Hash256.Compute(parentPayload));
        StableId parentId = StableId.Derive(RandomDomain.Geology, StableId.Zero, 10);
        StableId childId = StableId.Derive(RandomDomain.Sites, parentId, 20);
        var parent = new SnapshotReference(
            parentId,
            revision: 1,
            payloadHash: Hash256.Compute(parentPayload),
            kind: "height",
            parents: []);
        var child = new SnapshotReference(
            childId,
            revision: 2,
            payloadHash: Hash256.Compute(childPayload),
            kind: "voxel",
            parents: [new SnapshotParentReference(parent.Id, parent.Revision, parent.PayloadHash)]);
        var manifest = new WorldManifest(identity, units, frozenConfiguration, [child, parent]);
        var compatibility = WorldCompatibilityProfile.FromManifest(manifest);
        var store = new InMemoryWorldSnapshotStore();
        var service = new WorldPersistenceService(store, Limits);

        if (writeParentPayload)
        {
            AssertSuccess(service.WriteSnapshot(parent, parentPayload));
        }

        if (writeChildPayload)
        {
            AssertSuccess(service.WriteSnapshot(child, childPayload));
        }

        AssertSuccess(service.WriteManifest(manifest));
        return new PersistenceFixture(
            store,
            service,
            manifest,
            parent,
            child,
            parentPayload,
            childPayload,
            frozenConfiguration,
            compatibility);
    }

    internal static T AssertSuccess<T>(PersistenceResult<T> result)
    {
        PersistenceSuccess<T> success = Assert.IsInstanceOfType<PersistenceSuccess<T>>(result);
        return success.Value;
    }

    internal static PersistenceError AssertFailure<T>(PersistenceResult<T> result, PersistenceErrorCode expectedCode)
    {
        PersistenceFailure<T> failure = Assert.IsInstanceOfType<PersistenceFailure<T>>(result);
        Assert.AreEqual(expectedCode, failure.Error.Code);
        return failure.Error;
    }

    private static byte[] CreateHeightPayload(GenerationIdentity identity)
    {
        HeightSnapshot snapshot = HeightSnapshot.Create(
            identity,
            "l10a.height",
            1,
            [],
            new SnapshotDimensions(2, 2, 384),
            new SnapshotUnits("block", "block/256"),
            [
                new HeightSampleInput(StableId.Derive(RandomDomain.Geology, StableId.Zero, 0), 0, 0, 64.25),
                new HeightSampleInput(StableId.Derive(RandomDomain.Geology, StableId.Zero, 1), 1, 1, 65.5),
            ]);
        return HeightSnapshotBinaryCodec.Serialize(snapshot);
    }

    private static byte[] CreateVoxelPayload(GenerationIdentity identity, Hash256 parentHash)
    {
        VoxelSnapshot snapshot = VoxelSnapshot.Create(
            identity,
            "l10a.voxel",
            2,
            [parentHash],
            new SnapshotDimensions(2, 2, 384),
            new SnapshotUnits("block", "block"),
            [
                new VoxelCell(StableId.Derive(RandomDomain.Sites, StableId.Zero, 0), 0, 64, 0, VoxelMaterial.Rock),
                new VoxelCell(StableId.Derive(RandomDomain.Sites, StableId.Zero, 1), 1, 65, 1, VoxelMaterial.Soil),
            ]);
        return VoxelSnapshotBinaryCodec.Serialize(snapshot);
    }
}
