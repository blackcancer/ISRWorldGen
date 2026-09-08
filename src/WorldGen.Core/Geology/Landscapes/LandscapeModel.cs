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
    Hash256 FamilyParameterChecksum)
{
    /// <summary>
    /// Absolute sampling radii for reviewing morphology. Every family shares this
    /// physical frame so the scale choice cannot disclose the blind answer key.
    /// The distances never shrink to fit a world or image edge.
    /// </summary>
    public LandscapePhysicalScaleRadii PhysicalScaleRadii =>
        LandscapePhysicalScaleRadii.Review;
}

/// <summary>
/// Three strictly separated, family-neutral physical review scales. Their diameters
/// respectively resolve the finest catalog detail, span the broadest detail, and
/// cover the broadest catalog macro wavelength. A caller must select a fixture that
/// contains the requested extent; clipping or edge clamping is forbidden.
/// </summary>
public readonly record struct LandscapePhysicalScaleRadii
{
    public static LandscapePhysicalScaleRadii Review { get; } = new(450, 4_500, 26_000);

    public LandscapePhysicalScaleRadii(
        long coreRadiusBlocks,
        long detailRadiusBlocks,
        long regionRadiusBlocks)
    {
        if (coreRadiusBlocks <= 0 || detailRadiusBlocks <= coreRadiusBlocks || regionRadiusBlocks <= detailRadiusBlocks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coreRadiusBlocks),
                "Physical landscape radii must satisfy 0 < core < detail < region.");
        }

        CoreRadiusBlocks = coreRadiusBlocks;
        DetailRadiusBlocks = detailRadiusBlocks;
        RegionRadiusBlocks = regionRadiusBlocks;
    }

    public long CoreRadiusBlocks { get; }

    public long DetailRadiusBlocks { get; }

    public long RegionRadiusBlocks { get; }
}

public readonly record struct LandscapeSample(
    StableId DominantCellId,
    LandscapeFamily DominantFamily,
    double ModelAltitudeNormalized,
    double GeologicalDatumNormalized,
    double PrimaryResidualContributionNormalized,
    double ForeignResidualContributionNormalized,
    double AltitudeBlocks,
    double BathymetryBlocks,
    double PrimaryResidualWeight,
    bool IsTransition,
    int ActiveResidualContributorCount,
    double ForeignResidualWeight,
    double TransitionDistanceRatio);

/// <summary>
/// Immutable regional frame.  The extents describe the owning Voronoi cell in a
/// deterministic oriented frame; they are inputs to morphology, not a post-sample clamp.
/// </summary>
public readonly record struct LandscapeRegionPlan(
    long CenterX,
    long CenterZ,
    double OrientationRadians,
    double CoreExtentUBlocks,
    double CoreExtentVBlocks,
    double TransitionExtentUBlocks,
    double TransitionExtentVBlocks,
    ulong VariantOrdinal);

public sealed class LandscapeModel
{
    private readonly int nativeSeed;
    private readonly WorldBounds bounds;
    private readonly SiteEntry[] sites;
    private readonly double coreDominanceRatio;

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
        // This turns the frozen overlap setting into an explicit regional core.  A
        // point is pure until its nearest/second-nearest distance ratio reaches it.
        coreDominanceRatio = 1d / supportOverlapFactor;
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
        }

        double primaryResidual = SampleResidual(dominant, x, z);
        double primaryDatum = LandscapeAltitudeBounds.GeologicalDatum(dominant.Cell);
        double neighbourWeight = 0d;
        double greatestTransitionDistanceRatio = 0d;
        double foreignResidual = 0d;
        int activeContributors = 1;
        double weightedResidual = primaryResidual;
        double weightedDatum = primaryDatum;
        foreach (SiteEntry candidate in sites)
        {
            if (candidate.Cell.CellId == dominant.Cell.CellId)
            {
                continue;
            }

            double dx = (double)x - candidate.X;
            double dz = (double)z - candidate.Z;
            double weight = NeighbourTransitionWeight(dominantDistance, (dx * dx) + (dz * dz));
            if (weight == 0d)
            {
                continue;
            }

            // Every eligible neighbour is evaluated.  Thus B/C rank exchanges are
            // C1 changes in weights, never a strict-second-neighbour switch.
            neighbourWeight += weight;
            activeContributors++;
            double candidateResidual = SampleResidual(candidate, x, z);
            weightedResidual += weight * candidateResidual;
            foreignResidual += weight * candidateResidual;
            weightedDatum += weight * LandscapeAltitudeBounds.GeologicalDatum(candidate.Cell);
            greatestTransitionDistanceRatio = Math.Max(
                greatestTransitionDistanceRatio,
                Math.Sqrt(dominantDistance / ((dx * dx) + (dz * dz))));
        }
        double totalWeight = 1d + neighbourWeight;
        // Datum and residual are intentionally different fields.  The datum is
        // continuous across a regional boundary; only residuals participate in the
        // explicit C1 transition band, and never receive distant-site contributions.
        double geologicalDatum = weightedDatum / totalWeight;
        double morphologyResidual = weightedResidual / totalWeight;
        double primaryResidualContribution = primaryResidual / totalWeight;
        double foreignResidualContribution = foreignResidual / totalWeight;
        double modelAltitude = geologicalDatum + morphologyResidual;

        if (!double.IsFinite(modelAltitude) || modelAltitude is < -1 or > 1)
        {
            throw new InvalidOperationException("Landscape composition violated its construction-time analytic envelope.");
        }

        double altitudeBlocks = VerticalPlan.Transform.MapModelAltitudeToBlocks(modelAltitude);
        double bathymetryBlocks = altitudeBlocks < VerticalPlan.Transform.SeaLevelBlocks
            ? VerticalPlan.Transform.SeaLevelBlocks - altitudeBlocks
            : 0;
        return new LandscapeSample(
            dominant.Cell.CellId,
            dominant.Cell.Family,
            modelAltitude,
            geologicalDatum,
            primaryResidualContribution,
            foreignResidualContribution,
            altitudeBlocks,
            bathymetryBlocks,
            1d / totalWeight,
            neighbourWeight > 0d,
            activeContributors,
            neighbourWeight / totalWeight,
            greatestTransitionDistanceRatio);
    }

    private double SampleResidual(SiteEntry entry, long x, long z)
    {
        LandscapeCellProfile cell = entry.Cell;
        LandscapeFamilyProfile family = LandscapeFamilyCatalog.Get(cell.Family);
        double signature = LandscapeSignatureSampler.SampleRegional(
            family,
            x,
            z,
            nativeSeed,
            cell.CellId.Low ^ cell.CellId.High,
            entry.Region);
        return family.ReliefAmplitudeNormalized * signature;
    }

    private static double ComposeCellAltitude(LandscapeCellProfile cell, LandscapeFamilyProfile family, double signature)
    {
        double geologicalBase = LandscapeAltitudeBounds.GeologicalDatum(cell);
        // The geological datum deliberately does not encode a family silhouette.
        // Keeping it separate from the zero-centred local residual means a basin
        // remains a basin and a plateau keeps its flat top after composition.
        double morphologyResidual = family.ReliefAmplitudeNormalized * signature;
        return geologicalBase + morphologyResidual;
    }

    private double NeighbourTransitionWeight(double primaryDistanceSquared, double neighbourDistanceSquared)
    {
        if (!double.IsFinite(primaryDistanceSquared) || !double.IsFinite(neighbourDistanceSquared) ||
            primaryDistanceSquared < 0 || neighbourDistanceSquared <= 0)
        {
            throw new InvalidOperationException("Regional nearest-site distances must be finite and ordered.");
        }

        double ratio = Math.Sqrt(primaryDistanceSquared / neighbourDistanceSquared);
        if (ratio <= coreDominanceRatio)
        {
            return 0d;
        }

        // ratio==1 is the Voronoi boundary. Smoothstep supplies zero derivative at
        // both band limits, so switching the direct neighbour remains C1.
        double normalized = Math.Min(1d, (ratio - coreDominanceRatio) / (1d - coreDominanceRatio));
        return normalized * normalized * (3d - (2d * normalized));
    }

    private StableId[] ContributorIds(long x, long z) => sites.Where(site =>
    {
        double dx = (double)x - site.X;
        double dz = (double)z - site.Z;
        return CompactSupportWeight(Math.Sqrt((dx * dx) + (dz * dz)) / site.SupportRadiusBlocks) > 0;
    }).Select(site => site.Cell.CellId).ToArray();

    private static Hash256 ComputeChecksum(LandscapeModel model, GenerationIdentity identity)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "ISRW-LANDSCAPE-MODEL-V7-BOUNDED-PLAIN-RELIEF");
        AppendInt32(hash, identity.NativeSeed); AppendUInt32(hash, identity.AlgorithmVersion); AppendUInt32(hash, identity.SchemaVersion);
        AppendHash(hash, identity.GeographyConfigHash); AppendHash(hash, identity.GenerationAssetHash); AppendString(hash, identity.DeterminismProfileId);
        AppendHash(hash, model.PlateSnapshotChecksum); AppendHash(hash, model.AtlasContentChecksum);
        AppendString(hash, model.ScaleProfileId); AppendUInt32(hash, model.ScaleProfileVersion); AppendDouble(hash, model.coreDominanceRatio);
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
        foreach (SiteEntry site in model.sites)
        {
            AppendStableId(hash, site.Cell.CellId); AppendInt64(hash, site.Region.CenterX); AppendInt64(hash, site.Region.CenterZ);
            AppendDouble(hash, site.Region.OrientationRadians); AppendDouble(hash, site.Region.CoreExtentUBlocks); AppendDouble(hash, site.Region.CoreExtentVBlocks);
            AppendDouble(hash, site.Region.TransitionExtentUBlocks); AppendDouble(hash, site.Region.TransitionExtentVBlocks); AppendUInt64(hash, site.Region.VariantOrdinal);
        }
        return Hash256.FromCanonicalBytes(hash.GetHashAndReset());
    }

    private static void AppendInt32(IncrementalHash hash, int value) { Span<byte> b = stackalloc byte[sizeof(int)]; BinaryPrimitives.WriteInt32BigEndian(b, value); hash.AppendData(b); }
    private static void AppendInt64(IncrementalHash hash, long value) { Span<byte> b = stackalloc byte[sizeof(long)]; BinaryPrimitives.WriteInt64BigEndian(b, value); hash.AppendData(b); }
    private static void AppendUInt32(IncrementalHash hash, uint value) { Span<byte> b = stackalloc byte[sizeof(uint)]; BinaryPrimitives.WriteUInt32BigEndian(b, value); hash.AppendData(b); }
    private static void AppendUInt64(IncrementalHash hash, ulong value) { Span<byte> b = stackalloc byte[sizeof(ulong)]; BinaryPrimitives.WriteUInt64BigEndian(b, value); hash.AppendData(b); }
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

    internal readonly record struct SiteEntry(long X, long Z, double SupportRadiusBlocks, LandscapeCellProfile Cell, LandscapeRegionPlan Region);
}

internal static class LandscapeChecksumEncoding { internal const int MaximumStringUtf8Bytes = 128; }

/// <summary>
/// Conservative proof bounds for every term of the composed normalized altitude.
/// Source-cell validation makes these bounds a construction invariant; compact weighted
/// composition then remains within the same interval without a final clamp.
/// </summary>
internal static class LandscapeAltitudeBounds
{
    internal const int MinimumContinentalHeightPpm = -1_000_000;
    internal const int MaximumContinentalHeightPpm = 1_000_000;
    internal const double MinimumBoundaryNormalized = 0;
    internal const double MaximumBoundaryNormalized = 1;
    internal const double MinimumSignatureNormalized = -1;
    internal const double MaximumSignatureNormalized = 1;
    internal const double MinimumGeologicalDatum = -.63;
    internal const double MaximumGeologicalDatum = .58;
    internal const double MinimumComposedAltitude = -.91;
    internal const double MaximumComposedAltitude = .86;

    internal static bool IsValidSourceCell(PlateCellState cell) =>
        cell.ContinentalHeightPpm is >= MinimumContinentalHeightPpm and <= MaximumContinentalHeightPpm &&
        double.IsFinite(cell.UpliftNormalized) && cell.UpliftNormalized is >= MinimumBoundaryNormalized and <= MaximumBoundaryNormalized &&
        double.IsFinite(cell.SubsidenceNormalized) && cell.SubsidenceNormalized is >= MinimumBoundaryNormalized and <= MaximumBoundaryNormalized;

    internal static double GeologicalDatum(LandscapeCellProfile cell)
    {
        if (cell.ContinentalHeightPpm is < MinimumContinentalHeightPpm or > MaximumContinentalHeightPpm ||
            !double.IsFinite(cell.UpliftNormalized) || cell.UpliftNormalized is < MinimumBoundaryNormalized or > MaximumBoundaryNormalized ||
            !double.IsFinite(cell.SubsidenceNormalized) || cell.SubsidenceNormalized is < MinimumBoundaryNormalized or > MaximumBoundaryNormalized)
        {
            throw new ArgumentOutOfRangeException(nameof(cell), "Landscape cell lies outside the analytic altitude inputs.");
        }

        double continental = cell.ContinentalHeightPpm / 1_000_000d;
        return continental >= 0
            ? .10 + (.26 * continental) + (.22 * cell.UpliftNormalized) - (.10 * cell.SubsidenceNormalized)
            : -.18 + (.35 * continental) + (.04 * cell.UpliftNormalized) - (.10 * cell.SubsidenceNormalized);
    }

    internal static (double Minimum, double Maximum) Composed(LandscapeFamilyProfile family) =>
        (MinimumGeologicalDatum + (family.ReliefAmplitudeNormalized * MinimumSignatureNormalized),
         MaximumGeologicalDatum + (family.ReliefAmplitudeNormalized * MaximumSignatureNormalized));
}

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
        IReadOnlyDictionary<StableId, LandscapeRegionPlan> regionPlans = BuildRegionPlans(atlas, identity.NativeSeed, settings.SupportOverlapFactor);
        for (int index = 0; index < sourceSites.Length; index++)
        {
            if (sourceSites[index].Id != sourceCells[index].CellId)
            {
                return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.cell-identity",
                    "Atlas and plate snapshot cell identifiers differ.");
            }

            if (!LandscapeAltitudeBounds.IsValidSourceCell(sourceCells[index]))
            {
                return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.altitude-input",
                    "Plate-cell altitude inputs fall outside the analytic landscape envelope.");
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
                cells[index],
                regionPlans[sourceSites[index].Id]);
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

        // Oceanic interiors are quiet bathymetric domains, not continental plains.
        // Preserve the volcanic exception before applying the calm-interior rule.
        if (cell.CrustKind == CrustKind.Oceanic)
        {
            return cell.UpliftNormalized > 0.45
                ? LandscapeFamily.VolcanicDomains
                : LandscapeFamily.SedimentaryBasins;
        }

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

    private static IReadOnlyDictionary<StableId, LandscapeRegionPlan> BuildRegionPlans(
        AtlasMesh atlas,
        int nativeSeed,
        double supportOverlapFactor)
    {
        var sites = atlas.Sites.ToDictionary(site => site.Id);
        var plans = new Dictionary<StableId, LandscapeRegionPlan>(sites.Count);
        foreach (VoronoiCell cell in atlas.Cells)
        {
            AtlasSite site = sites[cell.SiteId];
            ulong variant = StatelessRandomV1.NextUInt64(nativeSeed, RandomDomain.Geology, site.Id, 701);
            double orientation = Math.Tau * ((variant >> 11) * (1d / (1UL << 53)));
            double cos = Math.Cos(orientation);
            double sin = Math.Sin(orientation);
            double extentU = 0;
            double extentV = 0;
            foreach (ExactPoint vertex in cell.Vertices)
            {
                double dx = vertex.X.ToDouble() - site.X;
                double dz = vertex.Z.ToDouble() - site.Z;
                extentU = Math.Max(extentU, Math.Abs((cos * dx) + (sin * dz)));
                extentV = Math.Max(extentV, Math.Abs((-sin * dx) + (cos * dz)));
            }

            // Degenerate point/linear topology has no enclosing polygon.  The same
            // finite-world fallback as support calculation gives it a real extent.
            if (!double.IsFinite(extentU) || extentU <= 0 || !double.IsFinite(extentV) || extentV <= 0)
            {
                double fallback = FarthestWorldCornerDistance(atlas.Bounds, site);
                extentU = fallback;
                extentV = fallback;
            }

            double coreRatio = 1d / supportOverlapFactor;
            plans.Add(site.Id, new LandscapeRegionPlan(
                site.X,
                site.Z,
                orientation,
                Math.BitIncrement(extentU * coreRatio),
                Math.BitIncrement(extentV * coreRatio),
                Math.BitIncrement(extentU),
                Math.BitIncrement(extentV),
                variant));
        }

        if (plans.Count != sites.Count || plans.Values.Any(plan =>
            !double.IsFinite(plan.CoreExtentUBlocks) || !double.IsFinite(plan.CoreExtentVBlocks) ||
            !double.IsFinite(plan.TransitionExtentUBlocks) || !double.IsFinite(plan.TransitionExtentVBlocks) ||
            plan.CoreExtentUBlocks <= 0 || plan.CoreExtentVBlocks <= 0 ||
            plan.TransitionExtentUBlocks <= 0 || plan.TransitionExtentVBlocks <= 0))
        {
            throw new InvalidOperationException("Atlas geometry did not yield finite regional landscape extents.");
        }

        return plans;
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
