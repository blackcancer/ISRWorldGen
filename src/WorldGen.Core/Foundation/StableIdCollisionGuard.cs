namespace ISRWorldGen.Core.Foundation;

/// <summary>
/// Canonical source tuple used to derive a persistent identity.
/// </summary>
public readonly record struct StableIdSource
{
    public StableIdSource(RandomDomain domain, StableId parent, ulong localIndex)
    {
        RandomDomainCatalog.EnsureKnown(domain);
        Domain = domain;
        Parent = parent;
        LocalIndex = localIndex;
    }

    public RandomDomain Domain { get; }

    public StableId Parent { get; }

    public ulong LocalIndex { get; }

    public StableId Derive() => StableId.Derive(Domain, Parent, LocalIndex);
}

/// <summary>
/// Publication-time collision policy for StableId version 1.
/// Same-source repeats are idempotent; distinct canonical sources producing one ID abort publication.
/// This builder-local guard is intentionally not shared mutable generation state.
/// </summary>
public sealed class StableIdCollisionGuard
{
    private readonly Dictionary<StableId, StableIdSource> sourcesById = [];

    public int Count => sourcesById.Count;

    public StableId Register(StableIdSource source)
    {
        StableId id = source.Derive();
        if (sourcesById.TryGetValue(id, out StableIdSource existing))
        {
            if (existing != source)
            {
                throw new StableIdCollisionException(id, existing, source);
            }

            return id;
        }

        sourcesById.Add(id, source);
        return id;
    }
}

public sealed class StableIdCollisionException : InvalidOperationException
{
    public StableIdCollisionException(
        StableId id,
        StableIdSource existing,
        StableIdSource conflicting)
        : base($"StableId collision for {id}; publication must abort.")
    {
        Id = id;
        Existing = existing;
        Conflicting = conflicting;
    }

    public StableId Id { get; }

    public StableIdSource Existing { get; }

    public StableIdSource Conflicting { get; }
}
