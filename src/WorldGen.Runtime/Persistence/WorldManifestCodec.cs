using System.Buffers.Binary;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Runtime.Persistence;

internal static class WorldManifestCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static ReadOnlySpan<byte> Magic => "ISRWMF01"u8;

    internal static PersistenceResult<byte[]> Serialize(
        WorldManifest manifest,
        PersistenceLimits limits,
        string storageKey)
    {
        try
        {
            byte[] configuration = manifest.GetFrozenConfigurationCopy();
            if (configuration.Length > limits.MaxFrozenConfigurationBytes)
            {
                return Failure(
                    PersistenceErrorCode.PayloadTooLarge,
                    storageKey,
                    $"Frozen configuration length {configuration.Length} exceeds the configured limit.");
            }

            if (manifest.Snapshots.Count > limits.MaxSnapshotCount)
            {
                return Failure(
                    PersistenceErrorCode.PayloadTooLarge,
                    storageKey,
                    $"Snapshot count {manifest.Snapshots.Count} exceeds the configured limit.");
            }

            byte[] profile = EncodeText(manifest.Identity.DeterminismProfileId, limits);
            byte[] horizontalUnits = EncodeText(manifest.Units.Horizontal, limits);
            byte[] verticalUnits = EncodeText(manifest.Units.Vertical, limits);
            var encodedKinds = new byte[manifest.Snapshots.Count][];

            long length = 88L;
            length = AddLengthPrefixed(length, profile.Length);
            length = AddLengthPrefixed(length, horizontalUnits.Length);
            length = AddLengthPrefixed(length, verticalUnits.Length);
            length = AddLengthPrefixed(length, configuration.Length);
            length = checked(length + sizeof(uint));

            for (int index = 0; index < manifest.Snapshots.Count; index++)
            {
                SnapshotReference reference = manifest.Snapshots[index];
                if (reference.Parents.Count > limits.MaxParentsPerSnapshot)
                {
                    return Failure(
                        PersistenceErrorCode.PayloadTooLarge,
                        storageKey,
                        $"Parent count {reference.Parents.Count} for snapshot {reference.StorageKey} exceeds the configured limit.");
                }

                byte[] kind = EncodeText(reference.Kind, limits);
                encodedKinds[index] = kind;
                length = checked(length + StableId.ByteWidth + sizeof(ulong) + Hash256.ByteWidth);
                length = AddLengthPrefixed(length, kind.Length);
                length = checked(length + sizeof(uint));
                length = checked(length + ((long)reference.Parents.Count * (StableId.ByteWidth + sizeof(ulong) + Hash256.ByteWidth)));
            }

            if (length > limits.MaxManifestDecodedBytes || length > int.MaxValue)
            {
                return Failure(
                    PersistenceErrorCode.PayloadTooLarge,
                    storageKey,
                    $"Manifest length {length} exceeds the configured limit {limits.MaxManifestDecodedBytes}.");
            }

            byte[] payload = GC.AllocateUninitializedArray<byte>((int)length);
            var writer = new SpanWriter(payload);
            writer.Write(Magic);
            writer.WriteUInt32(WorldManifest.FormatVersion);
            writer.WriteInt32LittleEndian(manifest.Identity.NativeSeed);
            writer.WriteUInt32(manifest.Identity.AlgorithmVersion);
            writer.WriteUInt32(manifest.Identity.SchemaVersion);
            writer.WriteHash(manifest.Identity.GeographyConfigHash);
            writer.WriteHash(manifest.Identity.GenerationAssetHash);
            writer.WriteLengthPrefixed(profile);
            writer.WriteLengthPrefixed(horizontalUnits);
            writer.WriteLengthPrefixed(verticalUnits);
            writer.WriteLengthPrefixed(configuration);
            writer.WriteUInt32(checked((uint)manifest.Snapshots.Count));

            for (int index = 0; index < manifest.Snapshots.Count; index++)
            {
                SnapshotReference reference = manifest.Snapshots[index];
                writer.WriteStableId(reference.Id);
                writer.WriteUInt64(reference.Revision);
                writer.WriteHash(reference.PayloadHash);
                writer.WriteLengthPrefixed(encodedKinds[index]);
                writer.WriteUInt32(checked((uint)reference.Parents.Count));
                foreach (SnapshotParentReference parent in reference.Parents)
                {
                    writer.WriteStableId(parent.Id);
                    writer.WriteUInt64(parent.Revision);
                    writer.WriteHash(parent.PayloadHash);
                }
            }

            return PersistenceResult<byte[]>.Success(payload);
        }
        catch (OverflowException)
        {
            return Failure(PersistenceErrorCode.PayloadTooLarge, storageKey, "Manifest size arithmetic overflowed.");
        }
        catch (EncoderFallbackException exception)
        {
            return Failure(PersistenceErrorCode.CorruptData, storageKey, $"Manifest text cannot be encoded: {exception.Message}");
        }
    }

    internal static PersistenceResult<WorldManifest> Deserialize(
        ReadOnlySpan<byte> payload,
        PersistenceLimits limits,
        string storageKey)
    {
        if (payload.Length > limits.MaxManifestDecodedBytes)
        {
            return Failure<WorldManifest>(
                PersistenceErrorCode.PayloadTooLarge,
                storageKey,
                $"Manifest length {payload.Length} exceeds the configured limit.");
        }

        try
        {
            var reader = new SpanReader(payload);
            if (!reader.Read(Magic.Length).SequenceEqual(Magic))
            {
                throw new PersistenceCodecException(PersistenceErrorCode.CorruptData, "The manifest magic is invalid.");
            }

            uint version = reader.ReadUInt32();
            if (version != WorldManifest.FormatVersion)
            {
                throw new PersistenceCodecException(
                    PersistenceErrorCode.UnsupportedVersion,
                    $"Manifest version {version} is not supported.");
            }

            int seed = reader.ReadInt32LittleEndian();
            uint algorithmVersion = reader.ReadUInt32();
            uint schemaVersion = reader.ReadUInt32();
            Hash256 configurationHash = reader.ReadHash();
            Hash256 assetHash = reader.ReadHash();
            string profile = reader.ReadText(limits.MaxStringUtf8Bytes, "determinism profile");
            string horizontalUnits = reader.ReadText(limits.MaxStringUtf8Bytes, "horizontal units");
            string verticalUnits = reader.ReadText(limits.MaxStringUtf8Bytes, "vertical units");
            byte[] configuration = reader.ReadBytes(limits.MaxFrozenConfigurationBytes, "frozen configuration");
            int snapshotCount = reader.ReadCount(limits.MaxSnapshotCount, "snapshot count");
            var snapshots = new SnapshotReference[snapshotCount];

            for (int index = 0; index < snapshots.Length; index++)
            {
                StableId id = reader.ReadStableId();
                ulong revision = reader.ReadUInt64();
                Hash256 payloadHash = reader.ReadHash();
                string kind = reader.ReadText(limits.MaxStringUtf8Bytes, "snapshot kind");
                int parentCount = reader.ReadCount(limits.MaxParentsPerSnapshot, "snapshot parent count");
                var parents = new SnapshotParentReference[parentCount];
                for (int parentIndex = 0; parentIndex < parents.Length; parentIndex++)
                {
                    parents[parentIndex] = new SnapshotParentReference(
                        reader.ReadStableId(),
                        reader.ReadUInt64(),
                        reader.ReadHash());
                    if (parentIndex > 0 &&
                        SnapshotParentReferenceComparer.Instance.Compare(parents[parentIndex - 1], parents[parentIndex]) >= 0)
                    {
                        throw new PersistenceCodecException(
                            PersistenceErrorCode.CorruptData,
                            "Snapshot parent references are not in strict canonical order.");
                    }
                }

                snapshots[index] = new SnapshotReference(id, revision, payloadHash, kind, parents);
                if (index > 0 && SnapshotReferenceComparer.Instance.Compare(snapshots[index - 1], snapshots[index]) >= 0)
                {
                    throw new PersistenceCodecException(
                        PersistenceErrorCode.CorruptData,
                        "Snapshot references are not in strict canonical order.");
                }
            }

            reader.EnsureComplete();
            var identity = new GenerationIdentity(
                seed,
                algorithmVersion,
                schemaVersion,
                configurationHash,
                assetHash,
                profile);
            var units = new SnapshotUnits(horizontalUnits, verticalUnits);
            return PersistenceResult<WorldManifest>.Success(new WorldManifest(identity, units, configuration, snapshots));
        }
        catch (PersistenceCodecException exception)
        {
            return Failure<WorldManifest>(exception.Code, storageKey, exception.Message);
        }
        catch (DecoderFallbackException exception)
        {
            return Failure<WorldManifest>(PersistenceErrorCode.CorruptData, storageKey, $"Manifest text is invalid UTF-8: {exception.Message}");
        }
        catch (UnsupportedAlgorithmVersionException exception)
        {
            return Failure<WorldManifest>(PersistenceErrorCode.UnsupportedVersion, storageKey, exception.Message);
        }
        catch (UnsupportedSnapshotVersionException exception)
        {
            return Failure<WorldManifest>(PersistenceErrorCode.UnsupportedVersion, storageKey, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Failure<WorldManifest>(PersistenceErrorCode.CorruptData, storageKey, exception.Message);
        }
    }

    private static byte[] EncodeText(string value, PersistenceLimits limits)
    {
        int byteCount = StrictUtf8.GetByteCount(value);
        if (byteCount > limits.MaxStringUtf8Bytes)
        {
            throw new OverflowException($"Canonical text exceeds {limits.MaxStringUtf8Bytes} UTF-8 bytes.");
        }

        return StrictUtf8.GetBytes(value);
    }

    private static long AddLengthPrefixed(long current, int contentLength) =>
        checked(current + sizeof(uint) + contentLength);

    private static PersistenceResult<byte[]> Failure(
        PersistenceErrorCode code,
        string storageKey,
        string details) => Failure<byte[]>(code, storageKey, details);

    private static PersistenceResult<T> Failure<T>(
        PersistenceErrorCode code,
        string storageKey,
        string details) => PersistenceResult<T>.Failure(new PersistenceError(code, storageKey, details));

    private sealed class PersistenceCodecException : Exception
    {
        internal PersistenceCodecException(PersistenceErrorCode code, string message)
            : base(message) => Code = code;

        internal PersistenceErrorCode Code { get; }
    }

    private ref struct SpanWriter
    {
        private readonly Span<byte> destination;
        private int offset;

        internal SpanWriter(Span<byte> destination)
        {
            this.destination = destination;
            offset = 0;
        }

        internal void Write(ReadOnlySpan<byte> value)
        {
            value.CopyTo(destination[offset..]);
            offset += value.Length;
        }

        internal void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(offset, sizeof(uint)), value);
            offset += sizeof(uint);
        }

        internal void WriteUInt64(ulong value)
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(offset, sizeof(ulong)), value);
            offset += sizeof(ulong);
        }

        internal void WriteInt32LittleEndian(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, sizeof(int)), value);
            offset += sizeof(int);
        }

        internal void WriteHash(Hash256 value)
        {
            value.WriteCanonicalBytes(destination.Slice(offset, Hash256.ByteWidth));
            offset += Hash256.ByteWidth;
        }

        internal void WriteStableId(StableId value)
        {
            value.WriteCanonicalBytes(destination.Slice(offset, StableId.ByteWidth));
            offset += StableId.ByteWidth;
        }

        internal void WriteLengthPrefixed(ReadOnlySpan<byte> value)
        {
            WriteUInt32(checked((uint)value.Length));
            Write(value);
        }
    }

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> source;
        private int offset;

        internal SpanReader(ReadOnlySpan<byte> source)
        {
            this.source = source;
            offset = 0;
        }

        internal ReadOnlySpan<byte> Read(int count)
        {
            if (count < 0 || count > source.Length - offset)
            {
                throw new PersistenceCodecException(PersistenceErrorCode.TruncatedData, "The manifest payload is truncated.");
            }

            ReadOnlySpan<byte> value = source.Slice(offset, count);
            offset += count;
            return value;
        }

        internal uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Read(sizeof(uint)));

        internal ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(Read(sizeof(ulong)));

        internal int ReadInt32LittleEndian() => BinaryPrimitives.ReadInt32LittleEndian(Read(sizeof(int)));

        internal Hash256 ReadHash() => Hash256.FromCanonicalBytes(Read(Hash256.ByteWidth));

        internal StableId ReadStableId()
        {
            ReadOnlySpan<byte> bytes = Read(StableId.ByteWidth);
            return new StableId(
                BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]),
                BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));
        }

        internal string ReadText(int maximumByteCount, string memberName)
        {
            byte[] bytes = ReadBytes(maximumByteCount, memberName);
            return StrictUtf8.GetString(bytes);
        }

        internal byte[] ReadBytes(int maximumByteCount, string memberName)
        {
            uint declaredLength = ReadUInt32();
            if (declaredLength > maximumByteCount)
            {
                throw new PersistenceCodecException(
                    PersistenceErrorCode.PayloadTooLarge,
                    $"Declared {memberName} length {declaredLength} exceeds the configured limit {maximumByteCount}.");
            }

            return Read(checked((int)declaredLength)).ToArray();
        }

        internal int ReadCount(int maximumCount, string memberName)
        {
            uint declaredCount = ReadUInt32();
            if (declaredCount > maximumCount || declaredCount > int.MaxValue)
            {
                throw new PersistenceCodecException(
                    PersistenceErrorCode.PayloadTooLarge,
                    $"Declared {memberName} {declaredCount} exceeds the configured limit {maximumCount}.");
            }

            return (int)declaredCount;
        }

        internal void EnsureComplete()
        {
            if (offset != source.Length)
            {
                throw new PersistenceCodecException(PersistenceErrorCode.CorruptData, "The manifest contains trailing bytes.");
            }
        }
    }
}
