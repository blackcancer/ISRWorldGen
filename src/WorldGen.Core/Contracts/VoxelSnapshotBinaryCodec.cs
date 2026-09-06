using System.Buffers.Binary;
using System.Text;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Contracts;

/// <summary>
/// Canonical binary codec v1 for synthetic Core voxels. It deliberately contains no Vintage Story block ID.
/// Header rules match the height codec; each voxel is ID, X, Y, Z, material in strict StableId order.
/// </summary>
public static class VoxelSnapshotBinaryCodec
{
    private static readonly byte[] Magic = "ISRWVOX1"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private const int CanonicalVoxelByteCount = 37;

    public const int MagicByteCount = 8;
    public const uint EnvelopeVersion = 1;

    public static byte[] Serialize(VoxelSnapshot snapshot)
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
        WriteContent(writer, snapshot.Voxels);
        return stream.ToArray();
    }

    public static VoxelSnapshot Deserialize(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var reader = new CanonicalReader(bytes);
            if (!reader.ReadBytes(MagicByteCount).SequenceEqual(Magic))
            {
                throw new CorruptSnapshotException("Voxel snapshot magic is invalid.");
            }

            uint envelopeVersion = reader.ReadUInt32();
            if (envelopeVersion != EnvelopeVersion)
            {
                throw new UnsupportedSnapshotVersionException(envelopeVersion);
            }

            var identity = new GenerationIdentity(
                reader.ReadNativeSeed(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadHash(),
                reader.ReadHash(),
                reader.ReadString());
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
                        : new CorruptSnapshotException("Voxel parent hashes are not in strict canonical order.");
                }
            }

            var dimensions = new SnapshotDimensions(
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt32());
            var units = new SnapshotUnits(reader.ReadString(), reader.ReadString());
            Hash256 serializedChecksum = reader.ReadHash();

            int voxelCount = reader.ReadBoundedCount(CanonicalVoxelByteCount);
            var voxels = new VoxelCell[voxelCount];
            for (int index = 0; index < voxelCount; index++)
            {
                var id = new StableId(reader.ReadUInt64(), reader.ReadUInt64());
                voxels[index] = new VoxelCell(
                    id,
                    reader.ReadInt64(),
                    reader.ReadInt32(),
                    reader.ReadInt64(),
                    (VoxelMaterial)reader.ReadByte());
                if (index > 0 && HeightSnapshot.CompareStableIds(voxels[index - 1].Id, id) >= 0)
                {
                    throw voxels[index - 1].Id == id
                        ? new DuplicateStableIdException(id)
                        : new CorruptSnapshotException("Voxels are not in strict StableId order.");
                }
            }

            reader.EnsureFullyConsumed();
            VoxelSnapshot snapshot = VoxelSnapshot.Create(
                identity,
                stage,
                revision,
                parents,
                dimensions,
                units,
                voxels);
            if (snapshot.Header.ContentChecksum != serializedChecksum)
            {
                throw new CorruptSnapshotException("Voxel content checksum does not match its payload.");
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
            throw new CorruptSnapshotException("Voxel snapshot payload is invalid or truncated.", exception);
        }
    }

    internal static Hash256 ComputeContentChecksum(IReadOnlyList<VoxelCell> voxels)
    {
        using var stream = new MemoryStream();
        var writer = new CanonicalWriter(stream);
        WriteContent(writer, voxels);
        return Hash256.Compute(stream.ToArray());
    }

    private static void WriteContent(CanonicalWriter writer, IReadOnlyList<VoxelCell> voxels)
    {
        writer.WriteUInt32(checked((uint)voxels.Count));
        foreach (VoxelCell voxel in voxels)
        {
            writer.WriteUInt64(voxel.Id.High);
            writer.WriteUInt64(voxel.Id.Low);
            writer.WriteInt64(voxel.X);
            writer.WriteInt32(voxel.Y);
            writer.WriteInt64(voxel.Z);
            writer.WriteByte((byte)voxel.Material);
        }
    }

    private sealed class CanonicalWriter
    {
        private readonly Stream stream;

        internal CanonicalWriter(Stream stream) => this.stream = stream;

        internal void WriteByte(byte value) => stream.WriteByte(value);

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
            WriteBytes(StrictUtf8.GetBytes(canonical));
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

        internal byte ReadByte() => ReadBytes(1)[0];

        internal ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > bytes.Length - offset)
            {
                throw new EndOfStreamException("Voxel snapshot ended before the requested value.");
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
                throw new InvalidDataException("Canonical voxel text length exceeds the supported range.");
            }

            string value = StrictUtf8.GetString(ReadBytes((int)byteCount));
            return CanonicalText.Require(value, nameof(value));
        }

        internal int ReadBoundedCount(int itemSize)
        {
            uint count = ReadUInt32();
            if (count > int.MaxValue || count > (uint)((bytes.Length - offset) / itemSize))
            {
                throw new InvalidDataException("Voxel collection count exceeds the remaining payload.");
            }

            return (int)count;
        }

        internal void EnsureFullyConsumed()
        {
            if (offset != bytes.Length)
            {
                throw new CorruptSnapshotException("Trailing bytes are forbidden after a canonical voxel snapshot.");
            }
        }
    }
}
