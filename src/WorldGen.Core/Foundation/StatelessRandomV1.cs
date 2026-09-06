namespace ISRWorldGen.Core.Foundation;

/// <summary>
/// Versioned counter-based 64-bit generator. Every sample is a pure function of the native int32 seed,
/// frozen domain key, stable stream ID, and explicit counter. There is no shared sequence to advance.
/// </summary>
public static class StatelessRandomV1
{
    private const ulong NativeSeedMagic = 0x49535257474e0100UL;
    private const ulong StreamMagic = 0x4b45595631000000UL;
    private const ulong CounterIncrement = 0x9e3779b97f4a7c15UL;

    public const int AlgorithmVersion = 1;

    public static ulong NextUInt64(
        int nativeSeed,
        RandomDomain domain,
        StableId streamId,
        ulong counter)
    {
        RandomDomainCatalog.EnsureKnown(domain);

        unchecked
        {
            Span<byte> encodedNativeSeed = stackalloc byte[NativeSeedEncoding.ByteWidth];
            NativeSeedEncoding.Write(nativeSeed, encodedNativeSeed);
            ulong seedBits = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(encodedNativeSeed);
            ulong domainBits = (ulong)(uint)domain << 32;
            ulong key0 = Mix64(NativeSeedMagic ^ seedBits ^ domainBits);
            ulong key1 = Mix64(StreamMagic ^ streamId.High);
            ulong key2 = Mix64(streamId.Low ^ key0);
            ulong state = (counter * CounterIncrement) + key0;
            return Mix64(state ^ RotateLeft(key1, 17) ^ key2);
        }
    }

    private static ulong Mix64(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }
    }

    private static ulong RotateLeft(ulong value, int offset) =>
        (value << offset) | (value >> (64 - offset));
}
