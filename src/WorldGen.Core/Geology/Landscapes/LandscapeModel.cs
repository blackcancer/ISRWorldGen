using System.Collections.ObjectModel;
using System.Globalization;
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
        int blendSiteCount)
    {
        ArgumentNullException.ThrowIfNull(verticalBudget);
        if (maximumCells <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCells), "Cell budget must be positive.");
        }

        if (blendSiteCount is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(blendSiteCount), "Blend site count must be in [1,8].");
        }

        VerticalBudget = verticalBudget;
        MaximumCells = maximumCells;
        BlendSiteCount = blendSiteCount;
    }

    public ReliefBudgetRequest VerticalBudget { get; }

    public int MaximumCells { get; }

    public int BlendSiteCount { get; }
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
    private readonly int blendSiteCount;

    internal LandscapeModel(
        int nativeSeed,
        WorldBounds bounds,
        IEnumerable<SiteEntry> sites,
        IEnumerable<LandscapeCellProfile> cells,
        int blendSiteCount,
        ReliefVerticalPlan verticalPlan,
        Hash256 plateSnapshotChecksum,
        GenerationIdentity identity,
        FrozenScaleProfile profile)
    {
        this.nativeSeed = nativeSeed;
        this.bounds = bounds;
        this.sites = sites.OrderBy(item => item.Cell.CellId, LandscapeStableIdComparer.Instance).ToArray();
        this.blendSiteCount = blendSiteCount;
        Cells = Array.AsReadOnly(cells.OrderBy(item => item.CellId, LandscapeStableIdComparer.Instance).ToArray());
        VerticalPlan = verticalPlan;
        PlateSnapshotChecksum = plateSnapshotChecksum;
        ScaleProfileId = profile.Id;
        ScaleProfileVersion = profile.ProfileVersion;
        ContentChecksum = ComputeChecksum(this, identity);
    }

    public ReadOnlyCollection<LandscapeCellProfile> Cells { get; }

    public ReliefVerticalPlan VerticalPlan { get; }

    public Hash256 PlateSnapshotChecksum { get; }

    public string ScaleProfileId { get; }

    public uint ScaleProfileVersion { get; }

    public Hash256 ContentChecksum { get; }

    public LandscapeSample Sample(long x, long z)
    {
        if (!bounds.Contains(x, z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Landscape coordinates must lie inside the finite world.");
        }

        Span<int> selectedIndices = stackalloc int[8];
        Span<double> selectedDistances = stackalloc double[8];
        int selectedCount = 0;
        for (int index = 0; index < sites.Length; index++)
        {
            double dx = (double)x - sites[index].X;
            double dz = (double)z - sites[index].Z;
            double distanceSquared = (dx * dx) + (dz * dz);
            int insertion = selectedCount;
            while (insertion > 0 && distanceSquared < selectedDistances[insertion - 1])
            {
                insertion--;
            }

            if (insertion >= blendSiteCount)
            {
                continue;
            }

            int upper = Math.Min(selectedCount, blendSiteCount - 1);
            for (int move = upper; move > insertion; move--)
            {
                selectedIndices[move] = selectedIndices[move - 1];
                selectedDistances[move] = selectedDistances[move - 1];
            }

            selectedIndices[insertion] = index;
            selectedDistances[insertion] = distanceSquared;
            selectedCount = Math.Min(selectedCount + 1, blendSiteCount);
        }

        SiteEntry dominant = sites[selectedIndices[0]];
        double modelAltitude;
        if (selectedDistances[0] == 0)
        {
            modelAltitude = SampleCell(dominant, x, z);
        }
        else
        {
            double weighted = 0;
            double totalWeight = 0;
            for (int selected = 0; selected < selectedCount; selected++)
            {
                double distance = Math.Sqrt(selectedDistances[selected]);
                double normalizedDistance = distance / sites[selectedIndices[selected]].BlendScaleBlocks;
                double weight = 1 / ((1 + normalizedDistance) * (1 + normalizedDistance));
                weighted += SampleCell(sites[selectedIndices[selected]], x, z) * weight;
                totalWeight += weight;
            }

            modelAltitude = weighted / totalWeight;
        }

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
            cell.CellId.Low ^ cell.CellId.High);
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
        var builder = new StringBuilder();
        builder.Append("ISRW-LANDSCAPE-MODEL-V1\n")
            .Append(identity.NativeSeed).Append('|').Append(identity.GeographyConfigHash).Append('|')
            .Append(model.PlateSnapshotChecksum).Append('|').Append(model.ScaleProfileId).Append('|')
            .Append(model.ScaleProfileVersion).Append('|').Append(model.blendSiteCount).Append('\n')
            .Append(model.VerticalPlan.RockFloorTopBlocks).Append('|')
            .Append(model.VerticalPlan.DeepestOceanFloorBlocks).Append('|')
            .Append(model.VerticalPlan.MaximumCavernCeilingBlocks).Append('|')
            .Append(model.VerticalPlan.HighestReliefBlocks).Append('|')
            .Append(model.VerticalPlan.MaximumOceanDepthBlocks).Append('|')
            .Append(model.VerticalPlan.MinimumCavernInteriorHeightBlocks).Append('|')
            .Append(model.VerticalPlan.MaximumReliefAboveSeaBlocks).Append('\n');
        foreach (LandscapeCellProfile cell in model.Cells)
        {
            builder.Append(cell.CellId).Append('|').Append(cell.PlateId).Append('|').Append((int)cell.Family).Append('|')
                .Append((int)cell.CrustKind).Append('|').Append(cell.RelativeAgePpm).Append('|')
                .Append(cell.ContinentalHeightPpm).Append('|')
                .Append(cell.UpliftNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(cell.SubsidenceNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(cell.FamilyParameterChecksum).Append('\n');
        }

        return Hash256.Compute(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    internal readonly record struct SiteEntry(long X, long Z, double BlendScaleBlocks, LandscapeCellProfile Cell);
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

        if (settings.BlendSiteCount > atlas.Sites.Count)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.landscapes.blend-count",
                "Blend site count cannot exceed the atlas site count.");
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
                familyProfile.MacroWavelengthBlocks,
                cells[index]);
        }

        return GenerationResult<LandscapeModel>.Success(new LandscapeModel(
            identity.NativeSeed,
            atlas.Bounds,
            entries,
            cells,
            settings.BlendSiteCount,
            plan,
            plates.ContentChecksum,
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
