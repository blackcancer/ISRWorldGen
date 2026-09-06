using System.Buffers.Binary;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Runtime.Persistence;

/// <summary>
/// Canonical framing for one persistence value. Version 1 deliberately uses an
/// uncompressed payload so the canonical save never depends on a decompressor.
/// </summary>
public static class PersistenceEnvelope
{
    private static ReadOnlySpan<byte> Magic => "ISRWPB01"u8;

    public const int HeaderByteCount = 61;
    public const int FormatVersionOffset = 8;
    public const int EncodingOffset = 12;
    public const int DecodedLengthOffset = 13;
    public const int StoredLengthOffset = 21;

    private const uint FormatVersion = 1;
    private const byte RawEncoding = 0;
    private const int ChecksumOffset = 29;

    internal static PersistenceResult<byte[]> Encode(
        byte[] payload,
        Hash256 payloadChecksum,
        long maximumDecodedBytes,
        long maximumEnvelopeBytes,
        string storageKey)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length > maximumDecodedBytes)
        {
            return Failure(
                PersistenceErrorCode.PayloadTooLarge,
                storageKey,
                $"Decoded payload length {payload.Length} exceeds the configured limit {maximumDecodedBytes}.");
        }

        long envelopeLength = checked(HeaderByteCount + (long)payload.Length);
        if (envelopeLength > maximumEnvelopeBytes)
        {
            return Failure(
                PersistenceErrorCode.PayloadTooLarge,
                storageKey,
                $"Envelope length {envelopeLength} exceeds the configured limit {maximumEnvelopeBytes}.");
        }

        byte[] envelope = GC.AllocateUninitializedArray<byte>(checked((int)envelopeLength));
        Magic.CopyTo(envelope);
        BinaryPrimitives.WriteUInt32BigEndian(envelope.AsSpan(FormatVersionOffset, 4), FormatVersion);
        envelope[EncodingOffset] = RawEncoding;
        BinaryPrimitives.WriteUInt64BigEndian(envelope.AsSpan(DecodedLengthOffset, 8), (ulong)payload.Length);
        BinaryPrimitives.WriteUInt64BigEndian(envelope.AsSpan(StoredLengthOffset, 8), (ulong)payload.Length);
        payloadChecksum.WriteCanonicalBytes(envelope.AsSpan(ChecksumOffset, Hash256.ByteWidth));
        payload.CopyTo(envelope.AsSpan(HeaderByteCount));
        return PersistenceResult<byte[]>.Success(envelope);
    }

    internal static PersistenceResult<byte[]> Read(
        IWorldSnapshotStore store,
        string storageKey,
        long maximumDecodedBytes,
        long maximumEnvelopeBytes)
    {
        try
        {
            if (!store.TryGetLength(storageKey, out long reportedLength))
            {
                return Failure(PersistenceErrorCode.MissingSnapshot, storageKey, "The persistence value is absent.");
            }

            if (reportedLength < 0)
            {
                return Failure(PersistenceErrorCode.CorruptData, storageKey, "The store reported a negative length.");
            }

            if (reportedLength > maximumEnvelopeBytes)
            {
                return Failure(
                    PersistenceErrorCode.PayloadTooLarge,
                    storageKey,
                    $"Reported envelope length {reportedLength} exceeds the configured limit {maximumEnvelopeBytes}.");
            }

            if (reportedLength < HeaderByteCount)
            {
                return Failure(PersistenceErrorCode.TruncatedData, storageKey, "The envelope header is truncated.");
            }

            using Stream input = store.OpenRead(storageKey);
            Span<byte> header = stackalloc byte[HeaderByteCount];
            if (!ReadExactly(input, header))
            {
                return Failure(PersistenceErrorCode.TruncatedData, storageKey, "The envelope header is truncated.");
            }

            if (!header[..Magic.Length].SequenceEqual(Magic))
            {
                return Failure(PersistenceErrorCode.CorruptData, storageKey, "The envelope magic is invalid.");
            }

            uint formatVersion = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(FormatVersionOffset, 4));
            if (formatVersion != FormatVersion)
            {
                return Failure(
                    PersistenceErrorCode.UnsupportedVersion,
                    storageKey,
                    $"Envelope version {formatVersion} is not supported.");
            }

            if (header[EncodingOffset] != RawEncoding)
            {
                return Failure(
                    PersistenceErrorCode.UnsupportedEncoding,
                    storageKey,
                    $"Envelope encoding {header[EncodingOffset]} is not supported.");
            }

            ulong decodedLength = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(DecodedLengthOffset, 8));
            ulong storedLength = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(StoredLengthOffset, 8));
            if (decodedLength > (ulong)maximumDecodedBytes || decodedLength > int.MaxValue)
            {
                return Failure(
                    PersistenceErrorCode.PayloadTooLarge,
                    storageKey,
                    $"Declared decoded length {decodedLength} exceeds the configured limit {maximumDecodedBytes}.");
            }

            ulong maximumStoredBytes = checked((ulong)(maximumEnvelopeBytes - HeaderByteCount));
            if (storedLength > maximumStoredBytes || storedLength > int.MaxValue)
            {
                return Failure(
                    PersistenceErrorCode.PayloadTooLarge,
                    storageKey,
                    $"Declared stored length {storedLength} exceeds the configured envelope limit.");
            }

            if (storedLength != decodedLength)
            {
                return Failure(
                    PersistenceErrorCode.CorruptData,
                    storageKey,
                    "Raw envelope stored and decoded lengths differ.");
            }

            ulong declaredEnvelopeLength = checked((ulong)HeaderByteCount + storedLength);
            if (declaredEnvelopeLength > (ulong)reportedLength)
            {
                return Failure(PersistenceErrorCode.TruncatedData, storageKey, "The envelope payload is truncated.");
            }

            if (declaredEnvelopeLength < (ulong)reportedLength)
            {
                return Failure(PersistenceErrorCode.CorruptData, storageKey, "The envelope contains trailing bytes.");
            }

            byte[] payload = GC.AllocateUninitializedArray<byte>((int)decodedLength);
            if (!ReadExactly(input, payload))
            {
                return Failure(PersistenceErrorCode.TruncatedData, storageKey, "The envelope payload is truncated.");
            }

            if (input.ReadByte() != -1)
            {
                return Failure(PersistenceErrorCode.CorruptData, storageKey, "The envelope contains trailing bytes.");
            }

            Hash256 expectedChecksum = Hash256.FromCanonicalBytes(header.Slice(ChecksumOffset, Hash256.ByteWidth));
            if (Hash256.Compute(payload) != expectedChecksum)
            {
                return Failure(PersistenceErrorCode.ChecksumMismatch, storageKey, "The envelope payload checksum is invalid.");
            }

            return PersistenceResult<byte[]>.Success(payload);
        }
        catch (IOException exception)
        {
            return StorageFailure(storageKey, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return StorageFailure(storageKey, exception);
        }
        catch (KeyNotFoundException exception)
        {
            return StorageFailure(storageKey, exception);
        }
    }

    private static bool ReadExactly(Stream stream, Span<byte> destination)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int count = stream.Read(destination[read..]);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }

    private static PersistenceResult<byte[]> StorageFailure(string storageKey, Exception exception) =>
        Failure(PersistenceErrorCode.StorageFailure, storageKey, $"Storage read failed: {exception.Message}");

    private static PersistenceResult<byte[]> Failure(
        PersistenceErrorCode code,
        string storageKey,
        string details) => PersistenceResult<byte[]>.Failure(new PersistenceError(code, storageKey, details));
}
