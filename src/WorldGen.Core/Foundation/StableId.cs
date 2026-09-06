using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace ISRWorldGen.Core.Foundation;

/// <summary>
/// Canonical 128-bit persistent identity represented as 32 lowercase hexadecimal characters.
/// Version 1 derives the first 128 SHA-256 bits from:
/// ASCII "ISRW-SID", byte version 1, domain uint32 BE, parent 128-bit BE, local index uint64 BE.
/// </summary>
public readonly record struct StableId(ulong High, ulong Low) : ISpanFormattable
{
    private const int DerivationPayloadSize = 37;
    private const byte DerivationVersion = 1;

    public const int BitWidth = 128;
    public const int ByteWidth = BitWidth / 8;

    public static StableId Zero { get; } = new(0, 0);

    public static StableId Derive(RandomDomain domain, StableId parent, ulong localIndex)
    {
        RandomDomainCatalog.EnsureKnown(domain);

        Span<byte> payload = stackalloc byte[DerivationPayloadSize];
        "ISRW-SID"u8.CopyTo(payload);
        payload[8] = DerivationVersion;
        BinaryPrimitives.WriteUInt32BigEndian(payload[9..13], (uint)domain);
        parent.WriteCanonicalBytes(payload[13..29]);
        BinaryPrimitives.WriteUInt64BigEndian(payload[29..37], localIndex);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(payload, hash);
        return new StableId(
            BinaryPrimitives.ReadUInt64BigEndian(hash[..8]),
            BinaryPrimitives.ReadUInt64BigEndian(hash[8..16]));
    }

    public static StableId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != BitWidth / 4 || value.Any(character => !IsLowercaseHex(character)))
        {
            throw new FormatException("StableId must contain exactly 32 canonical lowercase hexadecimal characters.");
        }

        return new StableId(
            ulong.Parse(value.AsSpan(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
            ulong.Parse(value.AsSpan(16, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    public void WriteCanonicalBytes(Span<byte> destination)
    {
        if (destination.Length < ByteWidth)
        {
            throw new ArgumentException($"At least {ByteWidth} bytes are required.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], High);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..16], Low);
    }

    public override string ToString() => string.Create(
        BitWidth / 4,
        this,
        static (destination, id) => id.TryFormat(destination, out _, "x", CultureInfo.InvariantCulture));

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        Span<char> buffer = stackalloc char[BitWidth / 4];
        return TryFormat(buffer, out int charsWritten, format, formatProvider)
            ? new string(buffer[..charsWritten])
            : throw new FormatException("StableId only supports canonical lowercase hexadecimal format.");
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        if (destination.Length < BitWidth / 4 ||
            (!format.IsEmpty && !format.Equals("x", StringComparison.Ordinal)))
        {
            charsWritten = 0;
            return false;
        }

        bool highWritten = High.TryFormat(destination[..16], out int highLength, "x16", CultureInfo.InvariantCulture);
        bool lowWritten = Low.TryFormat(destination[16..32], out int lowLength, "x16", CultureInfo.InvariantCulture);
        charsWritten = highWritten && lowWritten ? highLength + lowLength : 0;
        return charsWritten == BitWidth / 4;
    }

    private static bool IsLowercaseHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}
