using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Core.Geology.Landscapes;

public sealed record LandscapeGenerationSettings
{
    public LandscapeGenerationSettings(
        ReliefBudgetRequest verticalBudget,
        int maximumCells,
        double supportOverlapFactor)
    {
        ArgumentNullException.ThrowIfNull(verticalBudget);
        if (maximumCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCells), "Cell budget must be positive.");
        }

        if (!double.IsFinite(supportOverlapFactor) || supportOverlapFactor is < 1.05 or > 1.5)
        {
            throw new ArgumentOutOfRangeException(nameof(supportOverlapFactor), "Support overlap factor must be finite and in [1.05,1.5].");
        }

        VerticalBudget = verticalBudget;
        MaximumCells = maximumCells;
        SupportOverlapFactor = supportOverlapFactor;
    }

    public ReliefBudgetRequest VerticalBudget { get; }

    public int MaximumCells { get; }

    /// <summary>
    /// Factor applied to each Voronoi-derived support radius. It produces intentional
    /// overlap while the compact kernel remains exactly zero outside its support.
    /// </summary>
    public double SupportOverlapFactor { get; }
}

public readonly record struct LandscapeCellProfile(
    StableId CellId,
    StableId PlateId,
    LandscapeFamily Family,
    CrustKind CrustKind,
    int RelativeAgePpm,
    int ContinentalHeightPpm,
    double UpliftNormalized,
    double SubsidenceNormalized,
    Hash256 FamilyParameterChecksum);

public readonly record struct LandscapeSample(
    StableId DominantCellId,
    LandscapeFamily DominantFamily,
    double ModelAltitudeNormalized,
    double AltitudeBlocks,
    double BathymetryBlocks);

public sealed class LandscapeModel
{
    private readonly int nativeSeed;
    private readonly WorldBounds bounds;
    private readonly SiteEntry[] sites;
    private readonly double supportOverlapFactor;

    internal LandscapeModel(
        int nativeSeed,
        WorldBounds bounds,
        IEnumerable<SiteEntry> sites,
        IEnumerable<LandscapeCellProfile> cells,
        double supportOverlapFactor,
        ReliefVerticalPlan verticalPlan,
        Hash256 plateSnapshotChecksum,
        Hash256 atlasContentChecksum,
        GenerationIdentity identity,
        FrozenScaleProfile profile)
    {
        this.nativeSeed = nativeSeed;
        this.bounds = bounds;
        this.sites = sites.OrderBy(item => item.Cell.CellId, LandscapeStableIdComparer.Instance).ToArray();
        this.supportOverlapFactor = supportOverlapFactor;
        Cells = Array.AsReadOnly(cells.OrderBy(item => item.CellId, LandscapeStableIdComparer.Instance).ToArray());
        VerticalPlan = verticalPlan;
        PlateSnapshotChecksum = plateSnapshotChecksum;
        AtlasContentChecksum = atlasContentChecksum;
        ScaleProfileId = profile.Id;
        ScaleProfileVersion = profile.ProfileVersion;
        ContentChecksum = ComputeChecksum(this, identity);
    }

    public ReadOnlyCollection<LandscapeCellProfile> Cells { get; }

    public ReliefVerticalPlan VerticalPlan { get; }

    public Hash256 PlateSnapshotChecksum { get; }

    /// <summary>Canonical checksum of the exact atlas sealed into the parent plate snapshot.</summary>
    public Hash256 AtlasContentChecksum { get; }

    public string ScaleProfileId { get; }

    public uint ScaleProfileVersion { get; }

    public Hash256 ContentChecksum { get; }

    public LandscapeSample Sample(long x, long z)
    {
        if (!bounds.Contains(x, z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Landscape coordinates must lie inside the finite world.");
        }

        SiteEntry dominant = sites[0];
        double dominantDistance = double.PositiveInfinity;
        double weighted = 0;
        double totalWeight = 0;
        for (int index = 0; index < sites.Length; index++)
        {
            double dx = (double)x - sites[index].X;
            double dz = (double)z - sites[index].Z;
            double distanceSquared = (dx * dx) + (dz * dz);
            if (distanceSquared < dominantDistance)
            {
                dominantDistance = distanceSquared;
                dominant = sites[index];
            }
            double normalizedDistance = Math.Sqrt(distanceSquared) / sites[index].SupportRadiusBlocks;
            double weight = CompactSupportWeight(normalizedDistance);
            if (weight == 0)
            {
                continue;
            }

            weighted += SampleCell(sites[index], x, z) * weight;
            totalWeight += weight;
        }

        if (totalWeight <= 0 || !double.IsFinite(totalWeight))
        {
            throw new InvalidOperationException("Voronoi-derived landscape supports failed to cover an in-bounds coordinate.");
        }

        double modelAltitude = weighted / totalWeight;

        if (!double.IsFinite(modelAltitude) || modelAltitude is < -1 or > 1)
        {
            throw new InvalidOperationException("Landscape composition exceeded its proven normalized envelope.");
        }

        double altitudeBlocks = VerticalPlan.Transform.MapModelAltitudeToBlocks(modelAltitude);
        double bathymetryBlocks = altitudeBlocks < VerticalPlan.Transform.SeaLevelBlocks
            ? VerticalPlan.Transform.SeaLevelBlocks - altitudeBlocks
            : 0;
        return new LandscapeSample(
            dominant.Cell.CellId,
            dominant.Cell.Family,
            modelAltitude,
            altitudeBlocks,
            bathymetryBlocks);
    }

    private double SampleCell(SiteEntry entry, long x, long z)
    {
        LandscapeCellProfile cell = entry.Cell;
        LandscapeFamilyProfile family = LandscapeFamilyCatalog.Get(cell.Family);
        double signature = LandscapeSignatureSampler.Sample(
            family,
            x,
            z,
            nativeSeed,
            cell.CellId.Low ^ cell.CellId.High,
            entry.X,
            entry.Z);
        return ComposeCellAltitude(cell, family, signature);
    }

    private static double ComposeCellAltitude(LandscapeCellProfile cell, LandscapeFamilyProfile family, double signature)
    {
        double continental = cell.ContinentalHeightPpm / 1_000_000d;
        if (continental >= 0)
        {
            return 0.08 +
                (0.30 * continental) +
                (0.25 * cell.UpliftNormalized) -
                (0.08 * cell.SubsidenceNormalized) +
                (family.ReliefAmplitudeNormalized * signature);
        }

        return -0.08 +
            (0.45 * continental) -
            (0.12 * cell.SubsidenceNormalized) +
            (0.06 * cell.UpliftNormalized) +
            (family.ReliefAmplitudeNormalized * signature);
    }

    private static Hash256 ComputeChecksum(LandscapeModel model, GenerationIdentity identity)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "ISRW-LANDSCAPE-MODEL-V5-COMPACT-VORONOI-SUPPORT");
        AppendInt32(hash, identity.NativeSeed); AppendUInt32(hash, identity.AlgorithmVersion); AppendUInt32(hash, identity.SchemaVersion);
        AppendHash(hash, identity.GeographyConfigHash); AppendHash(hash, identity.GenerationAssetHash); AppendString(hash, identity.DeterminismProfileId);
        AppendHash(hash, model.PlateSnapshotChecksum); AppendHash(hash, model.AtlasContentChecksum);
        AppendString(hash, model.ScaleProfileId); AppendUInt32(hash, model.ScaleProfileVersion); AppendDouble(hash, model.supportOverlapFactor);
        AppendInt64(hash, model.VerticalPlan.RockFloorTopBlocks); AppendInt64(hash, model.VerticalPlan.DeepestOceanFloorBlocks);
        AppendInt64(hash, model.VerticalPlan.MaximumCavernCeilingBlocks); AppendInt64(hash, model.VerticalPlan.HighestReliefBlocks);
        AppendInt64(hash, model.VerticalPlan.MaximumOceanDepthBlocks); AppendInt64(hash, model.VerticalPlan.MinimumCavernInteriorHeightBlocks);
        AppendInt64(hash, model.VerticalPlan.MaximumReliefAboveSeaBlocks);
        foreach (LandscapeCellProfile cell in model.Cells)
        {
            AppendStableId(hash, cell.CellId); AppendStableId(hash, cell.PlateId); AppendInt32(hash, (int)cell.Family);
            AppendInt32(hash, (int)cell.CrustKind); AppendInt32(hash, cell.RelativeAgePpm); AppendInt32(hash, cell.ContinentalHeightPpm);
            AppendDouble(hash, cell.UpliftNormalized); AppendDouble(hash, cell.SubsidenceNormalized); AppendHash(hash, cell.FamilyParameterChecksum);
        }
        return Hash256.FromCanonicalBytes(hash.GetHashAndReset());
    }

    private static void AppendInt32(IncrementalHash hash, int value) { Span<byte> b = stackalloc byte[sizeof(int)]; BinaryPrimitives.WriteInt32BigEndian(b, value); hash.AppendData(b); }
    private static void AppendInt64(IncrementalHash hash, long value) { Span<byte> b = stackalloc byte[sizeof(long)]; BinaryPrimitives.WriteInt64BigEndian(b, value); hash.AppendData(b); }
    private static void AppendUInt32(IncrementalHash hash, uint value) { Span<byte> b = stackalloc byte[sizeof(uint)]; BinaryPrimitives.WriteUInt32BigEndian(b, value); hash.AppendData(b); }
    private static void AppendDouble(IncrementalHash hash, double value) => AppendInt64(hash, BitConverter.DoubleToInt64Bits(value));
    private static void AppendHash(IncrementalHash hash, Hash256 value) { Span<byte> b = stackalloc byte[Hash256.ByteWidth]; value.WriteCanonicalBytes(b); hash.AppendData(b); }
    private static void AppendStableId(IncrementalHash hash, StableId value) { Span<byte> b = stackalloc byte[sizeof(ulong) * 2]; BinaryPrimitives.WriteUInt64BigEndian(b[..sizeof(ulong)], value.High); BinaryPrimitives.WriteUInt64BigEndian(b[sizeof(ulong)..], value.Low); hash.AppendData(b); }
    private static void AppendString(IncrementalHash hash, string value)
    {
        int bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > LandscapeChecksumEncoding.MaximumStringUtf8Bytes) throw new InvalidOperationException("Landscape canonical text exceeds its bounded encoding.");
        AppendInt32(hash, bytes); hash.AppendData(Encoding.UTF8.GetBytes(value));
    }

    private static double CompactSupportWeight(double normalizedDistance)
    {
        if (!double.IsFinite(normalizedDistance) || normalizedDistance < 0)
        {
            throw new InvalidOperationException("Landscape site distance must be finite and nonnegative.");
        }

        if (normalizedDistance >= 1)
        {
            return 0;
        }

        // C1 compact kernel: it is positive inside the support and joins zero with
        // zero slope at the Voronoi-derived boundary.
        double remaining = 1d - (normalizedDistance * normalizedDistance);
        return remaining * remaining;
    }

    private int CountContributors(long x, long z)
    {
        int contributors = 0;
        foreach (SiteEntry site in sites)
        {
            double dx = (double)x - site.X;
            double dz = (double)z - site.Z;
            if (CompactSupportWeight(Math.Sqrt((dx * dx) + (dz * dz)) / site.SupportRadiusBlocks) > 0)
            {
                contributors++;
            }
        }

        return contributors;
    }

    internal readonly record struct SiteEntry(long X, long Z, double SupportRadiusBlocks, LandscapeCellProfile Cell);
}

internal static class LandscapeChecksumEncoding { internal const int MaximumStringUtf8Bytes = 128; }

public static class LandscapeModelBuilder
{
    public static GenerationResult<LandscapeModel> Build(
        GenerationIdentity identity,
        AtlasMesh atlas,
        PlateAtlasSnapshot plates,
        FrozenScaleProfile profile,
        LandscapeGenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        if (identity.GeographyConfigHash != profile.GeographyConfigHash)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.profile-hash",
                "Generation identity and frozen scale profile have different geography configuration hashes.");
        }

        WorldDomain domain = profile.AtlasIndexProfile.Domain;
        if (atlas.Bounds.MinX != domain.X.MinInclusive || atlas.Bounds.MinZ != domain.Z.MinInclusive ||
            atlas.Bounds.MaxXExclusive != domain.X.MaxExclusive || atlas.Bounds.MaxZExclusive != domain.Z.MaxExclusive)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.profile-domain",
                "Atlas bounds do not match the frozen scale profile domain.");
        }

        if (!string.Equals(plates.ScaleProfileId, profile.Id, StringComparison.Ordinal) ||
            plates.ScaleProfileVersion != profile.ProfileVersion ||
            plates.AtlasResolutionBlocks != profile.AtlasResolutionBlocks ||
            plates.AtlasTileSizeBlocks != profile.AtlasTileSizeBlocks)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.plate-profile",
                "Plate snapshot metadata does not match the frozen scale profile.");
        }
        if (Encoding.UTF8.GetByteCount(identity.DeterminismProfileId) > LandscapeChecksumEncoding.MaximumStringUtf8Bytes ||
            Encoding.UTF8.GetByteCount(profile.Id) > LandscapeChecksumEncoding.MaximumStringUtf8Bytes)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.canonical-text",
                "Generation identity or profile identifier exceeds the bounded landscape checksum encoding.");
        }

        if (atlas.Sites.Count != plates.Cells.Count)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.cell-identity",
                "Atlas and plate snapshot do not contain the same cell count.");
        }

        if (atlas.Sites.Count > settings.MaximumCells)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.landscapes.cell-budget",
                $"Atlas has {atlas.Sites.Count} cells for explicit landscape budget {settings.MaximumCells}.");
        }

        if (atlas.Sites.Count > profile.SiteQuota)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.landscapes.profile-site-quota",
                $"Atlas has {atlas.Sites.Count} sites for frozen profile quota {profile.SiteQuota}.");
        }

        // All count/quota checks above are intentionally non-allocating; provenance canonicalization may sort.
        if (!PlateAtlasProvenance.Matches(plates, identity, profile, atlas))
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.plate-provenance",
                "Plate snapshot provenance does not seal this generation identity, frozen profile, and atlas.");
        }

        GenerationResult<ReliefVerticalPlan> planResult = ReliefVerticalBudgetPlanner.Create(
            identity,
            profile,
            settings.VerticalBudget);
        if (planResult is GenerationFailure<ReliefVerticalPlan> planFailure)
        {
            return GenerationResult<LandscapeModel>.Failure(planFailure.Error);
        }

        ReliefVerticalPlan plan = ((GenerationSuccess<ReliefVerticalPlan>)planResult).Snapshot;
        PlateCellState[] sourceCells = plates.Cells.ToArray();
        Array.Sort(sourceCells, (left, right) => LandscapeStableIdComparer.Instance.Compare(left.CellId, right.CellId));
        AtlasSite[] sourceSites = atlas.Sites.ToArray();
        Array.Sort(sourceSites, (left, right) => LandscapeStableIdComparer.Instance.Compare(left.Id, right.Id));
        IReadOnlyDictionary<StableId, double> supportRadii = BuildSupportRadii(atlas, settings.SupportOverlapFactor);
        for (int index = 0; index < sourceSites.Length; index++)
        {
            if (sourceSites[index].Id != sourceCells[index].CellId)
            {
                return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.cell-identity",
                    "Atlas and plate snapshot cell identifiers differ.");
            }
        }

        var cells = new LandscapeCellProfile[sourceCells.Length];
        var entries = new LandscapeModel.SiteEntry[sourceCells.Length];
        for (int index = 0; index < sourceCells.Length; index++)
        {
            PlateCellState source = sourceCells[index];
            LandscapeFamily family = Classify(identity.NativeSeed, source);
            LandscapeFamilyProfile familyProfile = LandscapeFamilyCatalog.Get(family);
            cells[index] = new LandscapeCellProfile(
                source.CellId,
                source.PlateId,
                family,
                source.CrustKind,
                source.RelativeAgePpm,
                source.ContinentalHeightPpm,
                source.UpliftNormalized,
                source.SubsidenceNormalized,
                familyProfile.ParameterChecksum);
            entries[index] = new LandscapeModel.SiteEntry(
                sourceSites[index].X,
                sourceSites[index].Z,
                supportRadii[sourceSites[index].Id],
                cells[index]);
        }

        return GenerationResult<LandscapeModel>.Success(new LandscapeModel(
            identity.NativeSeed,
            atlas.Bounds,
            entries,
            cells,
            settings.SupportOverlapFactor,
            plan,
            plates.ContentChecksum,
            plates.AtlasContentChecksum,
            identity,
            profile));
    }

    private static LandscapeFamily Classify(int seed, PlateCellState cell)
    {
        double boundaryIntensity = Math.Max(cell.UpliftNormalized, cell.SubsidenceNormalized);
        if (boundaryIntensity < 0.03)
        {
            return LandscapeFamily.Plains;
        }

        if (cell.UpliftNormalized > 0.45)
        {
            return LandscapeFamily.RuggedRanges;
        }

        if (cell.SubsidenceNormalized > 0.35)
        {
            return LandscapeFamily.SedimentaryBasins;
        }

        ulong selector = StatelessRandomV1.NextUInt64(
            seed,
            RandomDomain.Geology,
            cell.CellId,
            601) % 6;
        if (cell.CrustKind == CrustKind.Oceanic)
        {
            return selector % 3 == 0
                ? LandscapeFamily.VolcanicDomains
                : LandscapeFamily.SedimentaryBasins;
        }

        if (cell.RelativeAgePpm >= 700_000)
        {
            return LandscapeFamily.OldMassifs;
        }

        if (cell.CrustKind == CrustKind.Transitional)
        {
            return selector % 2 == 0
                ? LandscapeFamily.VolcanicDomains
                : LandscapeFamily.Plateaus;
        }

        return selector switch
        {
            0 or 1 => LandscapeFamily.Plateaus,
            2 => LandscapeFamily.OldMassifs,
            3 => LandscapeFamily.VolcanicDomains,
            4 => LandscapeFamily.RuggedRanges,
            _ => LandscapeFamily.Plains,
        };
    }

    private static IReadOnlyDictionary<StableId, double> BuildSupportRadii(AtlasMesh atlas, double overlapFactor)
    {
        var sites = atlas.Sites.ToDictionary(site => site.Id);
        var radii = new Dictionary<StableId, double>(sites.Count);
        foreach (VoronoiCell cell in atlas.Cells)
        {
            AtlasSite site = sites[cell.SiteId];
            double radius = 0;
            foreach (ExactPoint vertex in cell.Vertices)
            {
                double dx = vertex.X.ToDouble() - site.X;
                double dz = vertex.Z.ToDouble() - site.Z;
                radius = Math.Max(radius, Math.Sqrt((dx * dx) + (dz * dz)));
            }

            // A point/linear topology may expose fewer polygon vertices. The finite
            // world corners are a deterministic conservative fallback, so the owner
            // still covers every in-bounds coordinate without fabricating topology.
            if (!double.IsFinite(radius) || radius <= 0)
            {
                radius = FarthestWorldCornerDistance(atlas.Bounds, site);
            }

            radii.Add(site.Id, Math.BitIncrement(radius) * overlapFactor);
        }

        if (radii.Count != sites.Count || radii.Values.Any(radius => !double.IsFinite(radius) || radius <= 0))
        {
            throw new InvalidOperationException("Atlas geometry did not yield a finite compact landscape support for every site.");
        }

        return radii;
    }

    private static double FarthestWorldCornerDistance(WorldBounds bounds, AtlasSite site)
    {
        long maxX = bounds.MaxXExclusive - 1;
        long maxZ = bounds.MaxZExclusive - 1;
        return new[]
        {
            Math.Sqrt(Math.Pow((double)site.X - bounds.MinX, 2) + Math.Pow((double)site.Z - bounds.MinZ, 2)),
            Math.Sqrt(Math.Pow((double)site.X - maxX, 2) + Math.Pow((double)site.Z - bounds.MinZ, 2)),
            Math.Sqrt(Math.Pow((double)site.X - bounds.MinX, 2) + Math.Pow((double)site.Z - maxZ, 2)),
            Math.Sqrt(Math.Pow((double)site.X - maxX, 2) + Math.Pow((double)site.Z - maxZ, 2)),
        }.Max();
    }

    private static GenerationResult<LandscapeModel> Failure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details) => GenerationResult<LandscapeModel>.Failure(new GenerationError(
            code,
            identity.NativeSeed,
            stage,
            StableId.Zero,
            identity.GeographyConfigHash,
            details,
            false));
}

internal sealed class LandscapeStableIdComparer : IComparer<StableId>
{
    internal static LandscapeStableIdComparer Instance { get; } = new();

    public int Compare(StableId x, StableId y)
    {
        int high = x.High.CompareTo(y.High);
        return high != 0 ? high : x.Low.CompareTo(y.Low);
    }
}
