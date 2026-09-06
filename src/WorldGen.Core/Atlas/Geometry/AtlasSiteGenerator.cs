using System.Collections.ObjectModel;
using System.Numerics;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Atlas.Geometry;

public sealed record AtlasSiteGenerationSettings
{
    public AtlasSiteGenerationSettings(int siteCount) => SiteCount = siteCount;

    public int SiteCount { get; }
}

public sealed class GeneratedSiteSet
{
    internal GeneratedSiteSet(IEnumerable<AtlasSite> sites)
    {
        AtlasSite[] copy = sites.ToArray();
        Array.Sort(copy, static (left, right) => StableIdOrdering.Instance.Compare(left.Id, right.Id));
        Sites = Array.AsReadOnly(copy);
    }

    public ReadOnlyCollection<AtlasSite> Sites { get; }
}

public static class AtlasSiteGenerator
{
    public static GenerationResult<GeneratedSiteSet> Generate(
        GenerationIdentity identity,
        WorldBounds bounds,
        AtlasSiteGenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.SiteCount <= 0)
        {
            return Failure(
                identity,
                GenerationFailureCode.InvalidInput,
                "Site count must be positive.");
        }

        BigInteger capacity = (BigInteger)bounds.Width * bounds.Length;
        if (settings.SiteCount > capacity)
        {
            return Failure(
                identity,
                GenerationFailureCode.BudgetExceeded,
                $"Requested {settings.SiteCount} sites for only {capacity} integral positions.");
        }

        try
        {
            var occupied = new HashSet<(long X, long Z)>();
            var collisionGuard = new StableIdCollisionGuard();
            var sites = new AtlasSite[settings.SiteCount];
            for (int index = 0; index < settings.SiteCount; index++)
            {
                var source = new StableIdSource(RandomDomain.Sites, StableId.Zero, checked((ulong)index));
                StableId id = collisionGuard.Register(source);
                ulong randomX = StatelessRandomV1.NextUInt64(identity.NativeSeed, RandomDomain.Sites, id, 0);
                ulong randomZ = StatelessRandomV1.NextUInt64(identity.NativeSeed, RandomDomain.Sites, id, 1);
                long xOffset = checked((long)(randomX % checked((ulong)bounds.Width)));
                long zOffset = checked((long)(randomZ % checked((ulong)bounds.Length)));
                BigInteger start = ((BigInteger)zOffset * bounds.Width) + xOffset;

                bool placed = false;
                for (int probe = 0; probe <= index; probe++)
                {
                    BigInteger flat = (start + probe) % capacity;
                    BigInteger zCandidate = BigInteger.DivRem(flat, bounds.Width, out BigInteger xCandidate);
                    long x = checked(bounds.MinX + (long)xCandidate);
                    long z = checked(bounds.MinZ + (long)zCandidate);
                    if (!occupied.Add((x, z)))
                    {
                        continue;
                    }

                    sites[index] = new AtlasSite(id, x, z);
                    placed = true;
                    break;
                }

                if (!placed)
                {
                    return Failure(
                        identity,
                        GenerationFailureCode.GeometryFailure,
                        "Deterministic collision resolution exhausted unexpectedly.");
                }
            }

            return GenerationResult<GeneratedSiteSet>.Success(new GeneratedSiteSet(sites));
        }
        catch (Exception exception) when (exception is ArithmeticException or StableIdCollisionException)
        {
            return Failure(identity, GenerationFailureCode.GeometryFailure, exception.Message);
        }
    }

    private static GenerationResult<GeneratedSiteSet> Failure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string details) =>
        GenerationResult<GeneratedSiteSet>.Failure(
            new GenerationError(
                code,
                identity.NativeSeed,
                "atlas.site-generation",
                StableId.Zero,
                identity.GeographyConfigHash,
                details,
                false));
}
