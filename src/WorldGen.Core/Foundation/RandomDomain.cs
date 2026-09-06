namespace ISRWorldGen.Core.Foundation;

/// <summary>
/// Frozen domain keys. Numeric values are part of deterministic format version 1.
/// </summary>
public enum RandomDomain : uint
{
    Geology = 0x47454f4c,
    Hydrology = 0x48594452,
    Climate = 0x434c494d,
    Vegetation = 0x56454745,
    Caverns = 0x43415645,
    Sites = 0x53495445,
}

public static class RandomDomainCatalog
{
    private static readonly IReadOnlyList<RandomDomain> KnownDomains = Array.AsReadOnly(
    [
        RandomDomain.Geology,
        RandomDomain.Hydrology,
        RandomDomain.Climate,
        RandomDomain.Vegetation,
        RandomDomain.Caverns,
        RandomDomain.Sites,
    ]);

    public static IReadOnlyList<RandomDomain> All => KnownDomains;

    public static void EnsureKnown(RandomDomain domain)
    {
        if (domain is not (RandomDomain.Geology or
            RandomDomain.Hydrology or
            RandomDomain.Climate or
            RandomDomain.Vegetation or
            RandomDomain.Caverns or
            RandomDomain.Sites))
        {
            throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown deterministic random domain.");
        }
    }
}
