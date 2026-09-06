using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace ISRWorldGen.Core.Contracts;

/// <summary>Immutable 256-bit value with canonical big-endian bytes and lowercase hexadecimal text.</summary>
public readonly record struct Hash256(ulong Word0, ulong Word1, ulong Word2, ulong Word3)
    : IComparable<Hash256>, ISpanFormattable
{
    public const int ByteWidth = 32;

    public static Hash256 Zero { get; } = new(0, 0, 0, 0);

    public static Hash256 Compute(ReadOnlySpan<byte> content)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(content, digest);
        return FromCanonicalBytes(digest);
    }

    public static Hash256 FromCanonicalBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteWidth)
        {
            throw new ArgumentException($"A Hash256 requires exactly {ByteWidth} bytes.", nameof(bytes));
        }

        return new Hash256(
            BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[8..16]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[16..24]),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[24..32]));
    }

    public static Hash256 Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != ByteWidth * 2 || value.Any(character => !IsLowercaseHex(character)))
        {
            throw new FormatException("Hash256 must contain exactly 64 canonical lowercase hexadecimal characters.");
        }

        return new Hash256(
            ParseWord(value.AsSpan(0, 16)),
            ParseWord(value.AsSpan(16, 16)),
            ParseWord(value.AsSpan(32, 16)),
            ParseWord(value.AsSpan(48, 16)));
    }

    public void WriteCanonicalBytes(Span<byte> destination)
    {
        if (destination.Length < ByteWidth)
        {
            throw new ArgumentException($"At least {ByteWidth} bytes are required.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], Word0);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..16], Word1);
        BinaryPrimitives.WriteUInt64BigEndian(destination[16..24], Word2);
        BinaryPrimitives.WriteUInt64BigEndian(destination[24..32], Word3);
    }

    public int CompareTo(Hash256 other)
    {
        int comparison = Word0.CompareTo(other.Word0);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Word1.CompareTo(other.Word1);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Word2.CompareTo(other.Word2);
        return comparison != 0 ? comparison : Word3.CompareTo(other.Word3);
    }

    public override string ToString() => string.Create(
        ByteWidth * 2,
        this,
        static (destination, hash) => hash.TryFormat(destination, out _, "x", CultureInfo.InvariantCulture));

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        Span<char> buffer = stackalloc char[ByteWidth * 2];
        return TryFormat(buffer, out int written, format, formatProvider)
            ? new string(buffer[..written])
            : throw new FormatException("Hash256 only supports canonical lowercase hexadecimal format.");
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        if (destination.Length < ByteWidth * 2 ||
            (!format.IsEmpty && !format.Equals("x", StringComparison.Ordinal)))
        {
            charsWritten = 0;
            return false;
        }

        bool word0Written = Word0.TryFormat(destination[..16], out int w0, "x16", CultureInfo.InvariantCulture);
        bool word1Written = Word1.TryFormat(destination[16..32], out int w1, "x16", CultureInfo.InvariantCulture);
        bool word2Written = Word2.TryFormat(destination[32..48], out int w2, "x16", CultureInfo.InvariantCulture);
        bool word3Written = Word3.TryFormat(destination[48..64], out int w3, "x16", CultureInfo.InvariantCulture);
        bool success = word0Written && word1Written && word2Written && word3Written;
        charsWritten = success ? w0 + w1 + w2 + w3 : 0;
        return charsWritten == ByteWidth * 2;
    }

    private static ulong ParseWord(ReadOnlySpan<char> word) =>
        ulong.Parse(word, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static bool IsLowercaseHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}
