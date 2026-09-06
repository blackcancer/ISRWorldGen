using System.Buffers.Binary;

namespace ISRWorldGen.Core.Foundation;

/// <summary>
/// Canonical encoding of the game's native signed 32-bit seed.
/// It is exactly four little-endian two's-complement bytes; it is not sign-extended to a native 64-bit seed.
/// </summary>
public static class NativeSeedEncoding
{
    public const int ByteWidth = sizeof(int);

    public static void Write(int nativeSeed, Span<byte> destination)
    {
        if (destination.Length < ByteWidth)
        {
            throw new ArgumentException($"At least {ByteWidth} bytes are required.", nameof(destination));
        }

        BinaryPrimitives.WriteInt32LittleEndian(destination, nativeSeed);
    }

    public static int Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < ByteWidth)
        {
            throw new ArgumentException($"At least {ByteWidth} bytes are required.", nameof(source));
        }

        return BinaryPrimitives.ReadInt32LittleEndian(source);
    }

    public static int ToNativeSeedChecked(long candidate) => checked((int)candidate);
}
