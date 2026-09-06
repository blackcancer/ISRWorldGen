using ISRWorldGen.ScaleProfiles;

namespace ISRWorldGen.Tests.L02CNative;

internal static class NativeProfileTestSupport
{
    internal static NativeWorldSnapshot NewLaboratoryWorld(
        int mapSizeX = 4_096,
        int mapSizeY = 256,
        int mapSizeZ = 4_096,
        string savegameIdentifier = "l02c-native-laboratory") => new(
            savegameIdentifier,
            isNew: true,
            mapSizeX,
            mapSizeY,
            mapSizeZ,
            chunkSize: 32,
            maximumWorldSizeXz: 67_108_864,
            maximumWorldSizeY: 16_384,
            nativeRuleSetId: "vintagestory-1.22.7-effective-world-v1",
            nativeRuleSetVersion: 1);

    internal static NativeWorldSnapshot Existing(NativeWorldSnapshot snapshot) => snapshot with { IsNew = false };

    internal static NativeProfileSelection LaboratorySelection() => new(isSpecified: true, "laboratory");

    internal static NativeProfileSelection NoSelection() => new(isSpecified: false, profileId: null);
}

internal sealed class MemoryFrozenProfileStore : IFrozenProfileStore
{
    private byte[]? bytes;

    internal int ReadCount { get; private set; }

    internal int WriteCount { get; private set; }

    internal bool ReturnDifferentBytesAfterWrite { get; set; }

    public byte[]? Read()
    {
        ReadCount++;
        byte[]? copy = bytes?.ToArray();
        if (ReturnDifferentBytesAfterWrite && WriteCount > 0 && copy is not null)
        {
            copy[^1] ^= 0xff;
        }

        return copy;
    }

    public void Write(ReadOnlySpan<byte> content)
    {
        WriteCount++;
        bytes = content.ToArray();
    }

    internal byte[] GetStoredCopy() => bytes?.ToArray() ?? throw new InvalidOperationException("Nothing was stored.");

    internal void Replace(ReadOnlySpan<byte> content) => bytes = content.ToArray();
}
