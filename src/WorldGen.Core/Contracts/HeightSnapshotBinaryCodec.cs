using System.Buffers.Binary;
using System.Text;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Contracts;

/// <summary>
/// Canonical binary codec v1. Integers are big-endian except the native seed, whose four bytes retain
/// NativeSeedEncoding's explicit little-endian contract. Text is strict UTF-8 NFC with uint32 byte length.
/// Parent hashes and samples are serialized in strict ascending canonical order.
/// </summary>
public static class HeightSnapshotBinaryCodec
{
    private static readonly byte[] Magic = "ISRWSNP1"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public const int MagicByteCount = 8;
    public const uint EnvelopeVersion = 1;

    public static byte[] Serialize(HeightSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using var stream = new MemoryStream();
        var writer = new CanonicalWriter(stream);
        writer.WriteBytes(Magic);
        writer.WriteUInt32(EnvelopeVersion);
        writer.WriteNativeSeed(snapshot.Header.Identity.NativeSeed);
        writer.WriteUInt32(snapshot.Header.Identity.AlgorithmVersion);
        writer.WriteUInt32(snapshot.Header.Identity.SchemaVersion);
        writer.WriteHash(snapshot.Header.Identity.GeographyConfigHash);
        writer.WriteHash(snapshot.Header.Identity.GenerationAssetHash);
        writer.WriteString(snapshot.Header.Identity.DeterminismProfileId);
        writer.WriteString(snapshot.Header.Stage);
        writer.WriteUInt64(snapshot.Header.Revision);
        writer.WriteUInt32(checked((uint)snapshot.Header.ParentHashes.Count));
        foreach (Hash256 parent in snapshot.Header.ParentHashes)
        {
            writer.WriteHash(parent);
        }

        writer.WriteInt64(snapshot.Header.Dimensions.Width);
        writer.WriteInt64(snapshot.Header.Dimensions.Length);
        writer.WriteInt32(snapshot.Header.Dimensions.Height);
        writer.WriteString(snapshot.Header.Units.Horizontal);
        writer.WriteString(snapshot.Header.Units.Vertical);
        writer.WriteHash(snapshot.Header.ContentChecksum);
        WriteContent(writer, snapshot.Samples);
        return stream.ToArray();
    }

    public static HeightSnapshot Deserialize(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new CanonicalReader(bytes);
            if (!reader.ReadBytes(MagicByteCount).SequenceEqual(Magic))
            {
                throw new CorruptSnapshotException("Snapshot magic is invalid.");
            }

            uint envelopeVersion = reader.ReadUInt32();
            if (envelopeVersion != EnvelopeVersion)
            {
                throw new UnsupportedSnapshotVersionException(envelopeVersion);
            }

            int nativeSeed = reader.ReadNativeSeed();
            uint algorithmVersion = reader.ReadUInt32();
            uint schemaVersion = reader.ReadUInt32();
            Hash256 geographyConfigHash = reader.ReadHash();
            Hash256 generationAssetHash = reader.ReadHash();
            string profile = reader.ReadString();
            var identity = new GenerationIdentity(
                nativeSeed,
                algorithmVersion,
                schemaVersion,
                geographyConfigHash,
                generationAssetHash,
                profile);

            string stage = reader.ReadString();
            ulong revision = reader.ReadUInt64();
            int parentCount = reader.ReadBoundedCount(Hash256.ByteWidth);
            var parents = new Hash256[parentCount];
            for (int index = 0; index < parentCount; index++)
            {
                parents[index] = reader.ReadHash();
                if (index > 0 && parents[index - 1].CompareTo(parents[index]) >= 0)
                {
                    throw parents[index - 1] == parents[index]
                        ? new DuplicateParentHashException(parents[index])
                        : new CorruptSnapshotException("Parent hashes are not in strict canonical order.");
                }
            }

            var dimensions = new SnapshotDimensions(
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt32());
            var units = new SnapshotUnits(reader.ReadString(), reader.ReadString());
            Hash256 serializedChecksum = reader.ReadHash();

            int sampleCount = reader.ReadBoundedCount(CanonicalHeightSampleByteCount);
            var samples = new HeightSample[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                var id = new StableId(reader.ReadUInt64(), reader.ReadUInt64());
                samples[index] = new HeightSample(id, reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
                if (index > 0 && HeightSnapshot.CompareStableIds(samples[index - 1].Id, id) >= 0)
                {
                    throw samples[index - 1].Id == id
                        ? new DuplicateStableIdException(id)
                        : new CorruptSnapshotException("Samples are not in strict StableId order.");
                }
            }

            reader.EnsureFullyConsumed();
            HeightSnapshot snapshot = HeightSnapshot.CreateQuantized(
                identity,
                stage,
                revision,
                parents,
                dimensions,
                units,
                samples);
            if (snapshot.Header.ContentChecksum != serializedChecksum)
            {
                throw new CorruptSnapshotException("Snapshot content checksum does not match its samples.");
            }

            return snapshot;
        }
        catch (Exception exception) when (exception is not (
            UnsupportedSnapshotVersionException or
            UnsupportedAlgorithmVersionException or
            DuplicateStableIdException or
            DuplicateParentHashException or
            CorruptSnapshotException))
        {
            throw new CorruptSnapshotException("Snapshot payload is invalid or truncated.", exception);
        }
    }

    internal static Hash256 ComputeContentChecksum(IReadOnlyList<HeightSample> samples)
    {
        using var stream = new MemoryStream();
        var writer = new CanonicalWriter(stream);
        WriteContent(writer, samples);
        return Hash256.Compute(stream.ToArray());
    }

    private const int CanonicalHeightSampleByteCount = 40;

    private static void WriteContent(CanonicalWriter writer, IReadOnlyList<HeightSample> samples)
    {
        writer.WriteUInt32(checked((uint)samples.Count));
        foreach (HeightSample sample in samples)
        {
            writer.WriteUInt64(sample.Id.High);
            writer.WriteUInt64(sample.Id.Low);
            writer.WriteInt64(sample.X);
            writer.WriteInt64(sample.Z);
            writer.WriteInt64(sample.QuantizedHeight);
        }
    }

    private sealed class CanonicalWriter
    {
        private readonly Stream stream;

        internal CanonicalWriter(Stream stream) => this.stream = stream;

        internal void WriteBytes(ReadOnlySpan<byte> bytes) => stream.Write(bytes);

        internal void WriteNativeSeed(int value)
        {
            Span<byte> bytes = stackalloc byte[NativeSeedEncoding.ByteWidth];
            NativeSeedEncoding.Write(value, bytes);
            WriteBytes(bytes);
        }

        internal void WriteInt32(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            WriteBytes(bytes);
        }

        internal void WriteUInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            WriteBytes(bytes);
        }

        internal void WriteInt64(long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            WriteBytes(bytes);
        }

        internal void WriteUInt64(ulong value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            WriteBytes(bytes);
        }

        internal void WriteHash(Hash256 hash)
        {
            Span<byte> bytes = stackalloc byte[Hash256.ByteWidth];
            hash.WriteCanonicalBytes(bytes);
            WriteBytes(bytes);
        }

        internal void WriteString(string value)
        {
            string canonical = CanonicalText.Require(value, nameof(value));
            int byteCount = StrictUtf8.GetByteCount(canonical);
            WriteUInt32(checked((uint)byteCount));
            byte[] bytes = StrictUtf8.GetBytes(canonical);
            WriteBytes(bytes);
        }
    }

    private ref struct CanonicalReader
    {
        private readonly ReadOnlySpan<byte> bytes;
        private int offset;

        internal CanonicalReader(ReadOnlySpan<byte> bytes)
        {
            this.bytes = bytes;
            offset = 0;
        }

        internal ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > bytes.Length - offset)
            {
                throw new EndOfStreamException("Snapshot ended before the requested value.");
            }

            ReadOnlySpan<byte> value = bytes.Slice(offset, count);
            offset += count;
            return value;
        }

        internal int ReadNativeSeed() => NativeSeedEncoding.Read(ReadBytes(NativeSeedEncoding.ByteWidth));

        internal int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));

        internal uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));

        internal long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));

        internal ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(8));

        internal Hash256 ReadHash() => Hash256.FromCanonicalBytes(ReadBytes(Hash256.ByteWidth));

        internal string ReadString()
        {
            uint byteCount = ReadUInt32();
            if (byteCount > int.MaxValue)
            {
                throw new InvalidDataException("Canonical text length exceeds the supported range.");
            }

            string value = StrictUtf8.GetString(ReadBytes((int)byteCount));
            return CanonicalText.Require(value, nameof(value));
        }

        internal int ReadBoundedCount(int itemSize)
        {
            uint count = ReadUInt32();
            if (count > int.MaxValue || count > (uint)((bytes.Length - offset) / itemSize))
            {
                throw new InvalidDataException("Collection count exceeds the remaining canonical payload.");
            }

            return (int)count;
        }

        internal void EnsureFullyConsumed()
        {
            if (offset != bytes.Length)
            {
                throw new CorruptSnapshotException("Trailing bytes are forbidden after a canonical snapshot.");
            }
        }
    }
}
