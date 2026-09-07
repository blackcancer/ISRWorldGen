using System.Buffers.Binary;
using System.Text;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.ScaleProfiles;

internal enum NativeProfilePersistenceState : byte
{
    Pending = 1,
    Committed = 2,
    Rejected = 3
}

internal sealed class NativeFrozenProfileEnvelope
{
    private readonly byte[] profileBytes;

    internal NativeFrozenProfileEnvelope(
        string savegameIdentifier,
        int mapSizeX,
        int mapSizeY,
        int mapSizeZ,
        int chunkSize,
        string nativeRuleSetId,
        uint nativeRuleSetVersion,
        Hash256 geographyConfigHash,
        string profileCodecId,
        uint profileCodecVersion,
        NativeProfilePersistenceState persistenceState,
        ReadOnlySpan<byte> profileBytes)
    {
        SavegameIdentifier = savegameIdentifier;
        MapSizeX = mapSizeX;
        MapSizeY = mapSizeY;
        MapSizeZ = mapSizeZ;
        ChunkSize = chunkSize;
        NativeRuleSetId = nativeRuleSetId;
        NativeRuleSetVersion = nativeRuleSetVersion;
        GeographyConfigHash = geographyConfigHash;
        ProfileCodecId = profileCodecId;
        ProfileCodecVersion = profileCodecVersion;
        PersistenceState = persistenceState;
        this.profileBytes = profileBytes.ToArray();
    }

    internal string SavegameIdentifier { get; }

    internal int MapSizeX { get; }

    internal int MapSizeY { get; }

    internal int MapSizeZ { get; }

    internal int ChunkSize { get; }

    internal string NativeRuleSetId { get; }

    internal uint NativeRuleSetVersion { get; }

    internal Hash256 GeographyConfigHash { get; }

    internal string ProfileCodecId { get; }

    internal uint ProfileCodecVersion { get; }

    internal NativeProfilePersistenceState PersistenceState { get; }

    internal byte[] GetProfileBytesCopy() => profileBytes.ToArray();
}

internal static class NativeFrozenProfileEnvelopeCodec
{
    internal const string FrozenProfileCodecId = "isrworldgen.core.frozen-scale-profile";
    internal const uint FrozenProfileCodecVersion = 1;
    private const uint EnvelopeVersion = 1;
    internal const int MaximumEnvelopeBytes = 8_192;
    private const int MaximumIdentifierBytes = 128;
    private const int MaximumProfileBytes = 4_096;
    private static readonly byte[] Magic = "ISRNPF01"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] Encode(NativeWorldSnapshot world, FrozenScaleProfile profile) =>
        Encode(world, profile, NativeProfilePersistenceState.Committed);

    internal static byte[] Encode(
        NativeWorldSnapshot world,
        FrozenScaleProfile profile,
        NativeProfilePersistenceState persistenceState)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.WidthBlocks != world.MapSizeX ||
            profile.HeightBlocks != world.MapSizeY ||
            profile.LengthBlocks != world.MapSizeZ ||
            !string.Equals(profile.NativeRuleSetId, world.NativeRuleSetId, StringComparison.Ordinal) ||
            profile.NativeRuleSetVersion != world.NativeRuleSetVersion)
        {
            throw new ArgumentException("Frozen profile is not bound to the supplied native world.", nameof(profile));
        }

        byte[] profileBytes = FrozenScaleProfileCodec.Serialize(profile);
        var envelope = new NativeFrozenProfileEnvelope(
            ValidateIdentifier(world.SavegameIdentifier, nameof(world)),
            world.MapSizeX,
            world.MapSizeY,
            world.MapSizeZ,
            world.ChunkSize,
            ValidateIdentifier(world.NativeRuleSetId, nameof(world)),
            world.NativeRuleSetVersion,
            profile.GeographyConfigHash,
            FrozenProfileCodecId,
            FrozenProfileCodecVersion,
            persistenceState,
            profileBytes);
        return Encode(envelope);
    }

    internal static byte[] Encode(NativeFrozenProfileEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        byte[] saveIdentifier = EncodeIdentifier(envelope.SavegameIdentifier, nameof(envelope));
        byte[] ruleSetIdentifier = EncodeIdentifier(envelope.NativeRuleSetId, nameof(envelope));
        byte[] profileCodecIdentifier = EncodeIdentifier(envelope.ProfileCodecId, nameof(envelope));
        byte[] profileBytes = envelope.GetProfileBytesCopy();
        if (envelope.MapSizeX <= 0 || envelope.MapSizeY <= 0 || envelope.MapSizeZ <= 0 ||
            envelope.ChunkSize <= 0 || envelope.NativeRuleSetVersion == 0 ||
            envelope.GeographyConfigHash == Hash256.Zero ||
            !string.Equals(envelope.ProfileCodecId, FrozenProfileCodecId, StringComparison.Ordinal) ||
            envelope.ProfileCodecVersion != FrozenProfileCodecVersion ||
            !Enum.IsDefined(envelope.PersistenceState) ||
            profileBytes.Length is < 1 or > MaximumProfileBytes)
        {
            throw new ArgumentException("Native profile envelope fields are invalid or unsupported.", nameof(envelope));
        }

        int payloadLength = checked(
            4 + saveIdentifier.Length +
            (4 * 4) +
            4 + ruleSetIdentifier.Length +
            4 +
            Hash256.ByteWidth +
            4 + profileCodecIdentifier.Length +
            4 +
            1 +
            4 + profileBytes.Length);
        int checksumOffset = checked(Magic.Length + 4 + 4 + payloadLength);
        int totalLength = checked(checksumOffset + Hash256.ByteWidth);
        if (totalLength > MaximumEnvelopeBytes)
        {
            throw new ArgumentException("Native profile envelope exceeds its canonical bound.", nameof(envelope));
        }

        byte[] encoded = new byte[totalLength];
        int offset = 0;
        WriteBytes(encoded, ref offset, Magic);
        WriteUInt32(encoded, ref offset, EnvelopeVersion);
        WriteInt32(encoded, ref offset, payloadLength);
        WriteLengthPrefixedBytes(encoded, ref offset, saveIdentifier);
        WriteInt32(encoded, ref offset, envelope.MapSizeX);
        WriteInt32(encoded, ref offset, envelope.MapSizeY);
        WriteInt32(encoded, ref offset, envelope.MapSizeZ);
        WriteInt32(encoded, ref offset, envelope.ChunkSize);
        WriteLengthPrefixedBytes(encoded, ref offset, ruleSetIdentifier);
        WriteUInt32(encoded, ref offset, envelope.NativeRuleSetVersion);
        envelope.GeographyConfigHash.WriteCanonicalBytes(encoded.AsSpan(offset, Hash256.ByteWidth));
        offset += Hash256.ByteWidth;
        WriteLengthPrefixedBytes(encoded, ref offset, profileCodecIdentifier);
        WriteUInt32(encoded, ref offset, envelope.ProfileCodecVersion);
        encoded[offset++] = (byte)envelope.PersistenceState;
        WriteLengthPrefixedBytes(encoded, ref offset, profileBytes);
        Hash256.Compute(encoded.AsSpan(0, offset)).WriteCanonicalBytes(encoded.AsSpan(offset, Hash256.ByteWidth));
        return encoded;
    }

    internal static NativeProfileResult<NativeFrozenProfileEnvelope> Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < Magic.Length + 4)
        {
            return FormatFailure("Native profile envelope is truncated.");
        }

        if (!encoded[..Magic.Length].SequenceEqual(Magic))
        {
            return FormatFailure("Native profile envelope magic is invalid.");
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(Magic.Length, 4));
        if (version != EnvelopeVersion)
        {
            return NativeProfileResult<NativeFrozenProfileEnvelope>.Failure(
                GenerationFailureCode.UnsupportedVersion,
                "native-profile.envelope-version",
                $"Native profile envelope version {version} is unsupported.");
        }

        if (encoded.Length > MaximumEnvelopeBytes || encoded.Length < Magic.Length + 8 + Hash256.ByteWidth)
        {
            return FormatFailure("Native profile envelope length is outside its canonical bound.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(encoded.Slice(Magic.Length + 4, 4));
        int expectedLength;
        try
        {
            expectedLength = checked(Magic.Length + 8 + payloadLength + Hash256.ByteWidth);
        }
        catch (OverflowException)
        {
            return FormatFailure("Native profile payload length overflows its canonical envelope.");
        }

        if (payloadLength < 0 || encoded.Length != expectedLength)
        {
            return FormatFailure("Native profile envelope contains a forged length or trailing data.");
        }

        int checksumOffset = encoded.Length - Hash256.ByteWidth;
        if (Hash256.Compute(encoded[..checksumOffset]) != Hash256.FromCanonicalBytes(encoded[checksumOffset..]))
        {
            return NativeProfileResult<NativeFrozenProfileEnvelope>.Failure(
                GenerationFailureCode.CorruptData,
                "native-profile.envelope-checksum",
                "Native profile envelope checksum does not match its content.");
        }

        try
        {
            var reader = new NativeEnvelopeReader(encoded.Slice(Magic.Length + 8, payloadLength));
            string saveIdentifier = reader.ReadIdentifier();
            int mapSizeX = reader.ReadInt32();
            int mapSizeY = reader.ReadInt32();
            int mapSizeZ = reader.ReadInt32();
            int chunkSize = reader.ReadInt32();
            string ruleSetIdentifier = reader.ReadIdentifier();
            uint ruleSetVersion = reader.ReadUInt32();
            Hash256 geographyConfigHash = Hash256.FromCanonicalBytes(reader.ReadBytes(Hash256.ByteWidth));
            string profileCodecIdentifier = reader.ReadIdentifier();
            uint profileCodecVersion = reader.ReadUInt32();
            NativeProfilePersistenceState persistenceState = (NativeProfilePersistenceState)reader.ReadByte();
            byte[] profileBytes = reader.ReadBoundedBytes(MaximumProfileBytes);
            if (!reader.IsComplete ||
                mapSizeX <= 0 || mapSizeY <= 0 || mapSizeZ <= 0 || chunkSize <= 0 ||
                ruleSetVersion == 0 || geographyConfigHash == Hash256.Zero ||
                !Enum.IsDefined(persistenceState))
            {
                return FormatFailure("Native profile envelope payload is not canonical.");
            }

            if (!string.Equals(profileCodecIdentifier, FrozenProfileCodecId, StringComparison.Ordinal) ||
                profileCodecVersion != FrozenProfileCodecVersion)
            {
                return NativeProfileResult<NativeFrozenProfileEnvelope>.Failure(
                    GenerationFailureCode.UnsupportedVersion,
                    "native-profile.profile-codec",
                    "Native profile envelope requests an unsupported frozen-profile blob codec.");
            }

            return NativeProfileResult<NativeFrozenProfileEnvelope>.Success(new NativeFrozenProfileEnvelope(
                saveIdentifier,
                mapSizeX,
                mapSizeY,
                mapSizeZ,
                chunkSize,
                ruleSetIdentifier,
                ruleSetVersion,
                geographyConfigHash,
                profileCodecIdentifier,
                profileCodecVersion,
                persistenceState,
                profileBytes));
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException or OverflowException)
        {
            return FormatFailure($"Native profile envelope cannot be decoded ({exception.GetType().Name}).");
        }
    }

    private static NativeProfileResult<NativeFrozenProfileEnvelope> FormatFailure(string details) =>
        NativeProfileResult<NativeFrozenProfileEnvelope>.Failure(
            GenerationFailureCode.CorruptData,
            "native-profile.envelope-format",
            details);

    private static string ValidateIdentifier(string value, string parameterName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !value.IsNormalized(NormalizationForm.FormC) ||
                StrictUtf8.GetByteCount(value) > MaximumIdentifierBytes)
            {
                throw new ArgumentException("Identifier must be bounded canonical NFC text.", parameterName);
            }

            return value;
        }
        catch (Exception exception) when (exception is EncoderFallbackException or ArgumentException)
        {
            throw new ArgumentException("Identifier must be bounded canonical NFC text.", parameterName, exception);
        }
    }

    private static byte[] EncodeIdentifier(string value, string parameterName) =>
        StrictUtf8.GetBytes(ValidateIdentifier(value, parameterName));

    private static void WriteLengthPrefixedBytes(byte[] destination, ref int offset, ReadOnlySpan<byte> value)
    {
        WriteInt32(destination, ref offset, value.Length);
        WriteBytes(destination, ref offset, value);
    }

    private static void WriteBytes(byte[] destination, ref int offset, ReadOnlySpan<byte> value)
    {
        value.CopyTo(destination.AsSpan(offset, value.Length));
        offset += value.Length;
    }

    private static void WriteInt32(byte[] destination, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, 4), value);
        offset += 4;
    }

    private static void WriteUInt32(byte[] destination, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, 4), value);
        offset += 4;
    }

    private ref struct NativeEnvelopeReader
    {
        private readonly ReadOnlySpan<byte> source;
        private int offset;

        internal NativeEnvelopeReader(ReadOnlySpan<byte> source)
        {
            this.source = source;
            offset = 0;
        }

        internal bool IsComplete => offset == source.Length;

        internal string ReadIdentifier()
        {
            ReadOnlySpan<byte> bytes = ReadBoundedBytes(MaximumIdentifierBytes);
            if (bytes.IsEmpty)
            {
                throw new InvalidDataException("Identifier is empty.");
            }

            string value = StrictUtf8.GetString(bytes);
            if (!value.IsNormalized(NormalizationForm.FormC) ||
                string.IsNullOrWhiteSpace(value) ||
                !StrictUtf8.GetBytes(value).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException("Identifier is not canonical UTF-8 NFC text.");
            }

            return value;
        }

        internal byte[] ReadBoundedBytes(int maximumLength)
        {
            int length = ReadInt32();
            if (length is < 1 || length > maximumLength)
            {
                throw new InvalidDataException("Length-prefixed value is outside its canonical bound.");
            }

            return ReadBytes(length).ToArray();
        }

        internal ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (length < 0 || length > source.Length - offset)
            {
                throw new InvalidDataException("Native profile payload is truncated or has a forged length.");
            }

            ReadOnlySpan<byte> value = source.Slice(offset, length);
            offset += length;
            return value;
        }

        internal int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));

        internal uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));

        internal byte ReadByte() => ReadBytes(1)[0];
    }
}
