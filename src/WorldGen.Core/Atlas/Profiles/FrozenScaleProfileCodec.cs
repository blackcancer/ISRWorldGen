using System.Buffers.Binary;
using System.Text;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Profiles;

public static class FrozenScaleProfileCodec
{
    private const uint EnvelopeVersion = 1;
    private const int MaximumManifestBytes = 4_096;
    private static readonly byte[] Magic = "ISRPFM01"u8.ToArray();

    public static byte[] Serialize(FrozenScaleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        byte[] configuration = ProfileCanonicalEncoding.EncodeConfiguration(profile.Definition);
        byte[] nativeRuleId = ProfileCanonicalEncoding.EncodeIdentifier(profile.NativeRuleSetId);
        int payloadLength = checked(configuration.Length + 4 + nativeRuleId.Length + 4);
        int checksumOffset = checked(Magic.Length + 4 + 4 + payloadLength);
        byte[] encoded = new byte[checked(checksumOffset + Hash256.ByteWidth)];
        int offset = 0;
        Magic.CopyTo(encoded, offset);
        offset += Magic.Length;
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(offset, 4), EnvelopeVersion);
        offset += 4;
        BinaryPrimitives.WriteInt32BigEndian(encoded.AsSpan(offset, 4), payloadLength);
        offset += 4;
        configuration.CopyTo(encoded, offset);
        offset += configuration.Length;
        BinaryPrimitives.WriteInt32BigEndian(encoded.AsSpan(offset, 4), nativeRuleId.Length);
        offset += 4;
        nativeRuleId.CopyTo(encoded, offset);
        offset += nativeRuleId.Length;
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(offset, 4), profile.NativeRuleSetVersion);
        offset += 4;
        Hash256.Compute(encoded.AsSpan(0, offset)).WriteCanonicalBytes(encoded.AsSpan(offset, Hash256.ByteWidth));
        return encoded;
    }

    public static GenerationResult<FrozenScaleProfile> Reload(
        ReadOnlySpan<byte> encoded,
        Hash256 expectedGeographyConfigHash,
        NativeWorldConstraints currentNativeConstraints)
    {
        ArgumentNullException.ThrowIfNull(currentNativeConstraints);
        if (encoded.Length < Magic.Length + 4)
        {
            return Corrupt("Manifest is truncated.", "atlas.profile.manifest-format");
        }

        if (!encoded[..Magic.Length].SequenceEqual(Magic))
        {
            return Corrupt("Manifest magic is invalid.", "atlas.profile.manifest-format");
        }

        uint envelopeVersion = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(Magic.Length, 4));
        if (envelopeVersion != EnvelopeVersion)
        {
            return Fail(
                GenerationFailureCode.UnsupportedVersion,
                "atlas.profile.manifest-version",
                Hash256.Zero,
                $"Manifest envelope version {envelopeVersion} is unsupported.");
        }

        if (encoded.Length > MaximumManifestBytes || encoded.Length < Magic.Length + 8 + Hash256.ByteWidth)
        {
            return Corrupt("Manifest length is outside the bounded canonical envelope.", "atlas.profile.manifest-format");
        }

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(encoded.Slice(Magic.Length + 4, 4));
        int expectedLength;
        try
        {
            expectedLength = checked(Magic.Length + 4 + 4 + payloadLength + Hash256.ByteWidth);
        }
        catch (OverflowException)
        {
            return Corrupt("Manifest payload length overflows the canonical envelope.", "atlas.profile.manifest-format");
        }

        if (payloadLength < 0 || encoded.Length != expectedLength)
        {
            return Corrupt("Manifest contains a forged length or trailing data.", "atlas.profile.manifest-format");
        }

        int checksumOffset = encoded.Length - Hash256.ByteWidth;
        Hash256 actualChecksum = Hash256.Compute(encoded[..checksumOffset]);
        Hash256 encodedChecksum = Hash256.FromCanonicalBytes(encoded[checksumOffset..]);
        if (actualChecksum != encodedChecksum)
        {
            return Corrupt("Manifest checksum does not match its content.", "atlas.profile.manifest-checksum");
        }

        try
        {
            var reader = new CanonicalProfileReader(encoded.Slice(Magic.Length + 8, payloadLength));
            ScaleProfileDefinition definition = reader.ReadDefinition();
            string persistedNativeRuleSetId = reader.ReadIdentifier();
            uint persistedNativeRuleSetVersion = reader.ReadUInt32();
            if (!reader.IsComplete ||
                !ProfileCanonicalEncoding.IsCanonicalIdentifier(persistedNativeRuleSetId) ||
                persistedNativeRuleSetVersion == 0)
            {
                return Corrupt("Manifest payload is not canonical.", "atlas.profile.manifest-format");
            }

            Hash256 actualGeographyHash = ProfileCanonicalEncoding.ComputeConfigurationHash(definition);
            if (actualGeographyHash != expectedGeographyConfigHash)
            {
                return Fail(
                    GenerationFailureCode.CorruptData,
                    "atlas.profile.frozen-mutation",
                    actualGeographyHash,
                    "Persisted profile differs from the geography configuration frozen for this world.");
            }

            return ScaleProfileValidator.ValidateAndFreeze(
                definition,
                currentNativeConstraints,
                persistedNativeRuleSetId,
                persistedNativeRuleSetVersion);
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException or OverflowException)
        {
            return Corrupt($"Manifest payload cannot be decoded: {exception.Message}", "atlas.profile.manifest-format");
        }
    }

    private static GenerationResult<FrozenScaleProfile> Corrupt(string details, string stage) =>
        Fail(GenerationFailureCode.CorruptData, stage, Hash256.Zero, details);

    private static GenerationResult<FrozenScaleProfile> Fail(
        GenerationFailureCode code,
        string stage,
        Hash256 inputHash,
        string details) => GenerationResult<FrozenScaleProfile>.Failure(new GenerationError(
            code,
            nativeSeed: 0,
            stage,
            StableId.Zero,
            inputHash,
            details,
            canRetry: false));
}

internal static class ProfileCanonicalEncoding
{
    private const int MaximumIdentifierBytes = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool IsCanonicalIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return value.IsNormalized(NormalizationForm.FormC) &&
                StrictUtf8.GetByteCount(value) <= MaximumIdentifierBytes;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static byte[] EncodeIdentifier(string value)
    {
        if (!IsCanonicalIdentifier(value))
        {
            throw new InvalidDataException("Identifier is not bounded canonical NFC text.");
        }

        return StrictUtf8.GetBytes(value);
    }

    internal static Hash256 TryComputeConfigurationHash(ScaleProfileDefinition definition)
    {
        try
        {
            return ComputeConfigurationHash(definition);
        }
        catch (Exception exception) when (exception is InvalidDataException or EncoderFallbackException or OverflowException)
        {
            return Hash256.Zero;
        }
    }

    internal static Hash256 ComputeConfigurationHash(ScaleProfileDefinition definition) =>
        Hash256.Compute(EncodeConfiguration(definition));

    internal static byte[] EncodeConfiguration(ScaleProfileDefinition definition)
    {
        byte[] id = EncodeIdentifier(definition.Id);
        const int fixedByteCount = 4 + (8 * 2) + (4 * 5) + (8 * 5);
        byte[] bytes = new byte[checked(fixedByteCount + 4 + id.Length)];
        int offset = 0;
        WriteUInt32(bytes, ref offset, definition.ProfileVersion);
        WriteInt32(bytes, ref offset, id.Length);
        id.CopyTo(bytes, offset);
        offset += id.Length;
        WriteInt64(bytes, ref offset, definition.WidthBlocks);
        WriteInt64(bytes, ref offset, definition.LengthBlocks);
        WriteInt32(bytes, ref offset, definition.HeightBlocks);
        WriteInt32(bytes, ref offset, definition.AtlasResolutionBlocks);
        WriteInt32(bytes, ref offset, definition.AtlasTileSizeBlocks);
        WriteInt32(bytes, ref offset, definition.RequestedSiteCount);
        WriteInt32(bytes, ref offset, definition.SiteQuota);
        WriteInt64(bytes, ref offset, definition.AtlasMemoryBudgetBytes);
        WriteInt64(bytes, ref offset, definition.SeaLevelBlocks);
        WriteInt64(bytes, ref offset, definition.MinimumFloorThicknessBlocks);
        WriteInt64(bytes, ref offset, definition.MinimumCavernCoverBlocks);
        WriteInt64(bytes, ref offset, definition.HeightMarginBlocks);
        return bytes;
    }

    internal static string DecodeIdentifier(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumIdentifierBytes)
        {
            throw new InvalidDataException("Identifier length is outside the canonical bound.");
        }

        string value = StrictUtf8.GetString(bytes);
        if (!IsCanonicalIdentifier(value) || !EncodeIdentifier(value).AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidDataException("Identifier is not canonical UTF-8 NFC text.");
        }

        return value;
    }

    private static void WriteUInt32(byte[] bytes, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
        offset += 4;
    }

    private static void WriteInt32(byte[] bytes, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), value);
        offset += 4;
    }

    private static void WriteInt64(byte[] bytes, ref int offset, long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset, 8), value);
        offset += 8;
    }
}

internal ref struct CanonicalProfileReader
{
    private readonly ReadOnlySpan<byte> bytes;
    private int offset;

    internal CanonicalProfileReader(ReadOnlySpan<byte> bytes)
    {
        this.bytes = bytes;
        offset = 0;
    }

    internal bool IsComplete => offset == bytes.Length;

    internal ScaleProfileDefinition ReadDefinition()
    {
        uint profileVersion = ReadUInt32();
        string id = ReadIdentifier();
        long widthBlocks = ReadInt64();
        long lengthBlocks = ReadInt64();
        int heightBlocks = ReadInt32();
        int atlasResolutionBlocks = ReadInt32();
        int atlasTileSizeBlocks = ReadInt32();
        int requestedSiteCount = ReadInt32();
        int siteQuota = ReadInt32();
        long atlasMemoryBudgetBytes = ReadInt64();
        long seaLevelBlocks = ReadInt64();
        long minimumFloorThicknessBlocks = ReadInt64();
        long minimumCavernCoverBlocks = ReadInt64();
        long heightMarginBlocks = ReadInt64();
        return new ScaleProfileDefinition(
            id,
            profileVersion,
            widthBlocks,
            lengthBlocks,
            heightBlocks,
            atlasResolutionBlocks,
            atlasTileSizeBlocks,
            requestedSiteCount,
            siteQuota,
            atlasMemoryBudgetBytes,
            seaLevelBlocks,
            minimumFloorThicknessBlocks,
            minimumCavernCoverBlocks,
            heightMarginBlocks);
    }

    internal string ReadIdentifier()
    {
        int length = ReadInt32();
        return ProfileCanonicalEncoding.DecodeIdentifier(ReadBytes(length));
    }

    internal uint ReadUInt32()
    {
        ReadOnlySpan<byte> value = ReadBytes(4);
        return BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    private int ReadInt32()
    {
        ReadOnlySpan<byte> value = ReadBytes(4);
        return BinaryPrimitives.ReadInt32BigEndian(value);
    }

    private long ReadInt64()
    {
        ReadOnlySpan<byte> value = ReadBytes(8);
        return BinaryPrimitives.ReadInt64BigEndian(value);
    }

    private ReadOnlySpan<byte> ReadBytes(int length)
    {
        if (length < 0 || length > bytes.Length - offset)
        {
            throw new InvalidDataException("Manifest payload is truncated or contains a forged length.");
        }

        ReadOnlySpan<byte> value = bytes.Slice(offset, length);
        offset += length;
        return value;
    }
}
