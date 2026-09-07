using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Geology.Plates;

public enum CrustKind
{
    Oceanic = 0,
    Transitional = 1,
    Continental = 2,
}

public sealed class PlateCrustPatch
{
    public PlateCrustPatch(StableId cellId, CrustKind crustKind, int relativeAgePpm)
        : this(cellId, StableId.Zero, crustKind, relativeAgePpm)
    {
    }

    private PlateCrustPatch(StableId cellId, StableId plateId, CrustKind crustKind, int relativeAgePpm)
    {
        if (!Enum.IsDefined(crustKind))
        {
            throw new ArgumentOutOfRangeException(nameof(crustKind), crustKind, "Unknown crust kind.");
        }

        if (relativeAgePpm is < 0 or > ContinentalFieldModel.PartsPerMillion)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeAgePpm), "Relative age must be normalized in [0,1] ppm.");
        }

        CellId = cellId;
        PlateId = plateId;
        CrustKind = crustKind;
        RelativeAgePpm = relativeAgePpm;
    }

    public StableId CellId { get; }

    public StableId PlateId { get; }

    public CrustKind CrustKind { get; }

    public int RelativeAgePpm { get; }

    internal PlateCrustPatch OwnedBy(StableId plateId) => new(CellId, plateId, CrustKind, RelativeAgePpm);
}

public sealed class PlateDomain
{
    public PlateDomain(StableId plateId, PlateVelocity velocity, IEnumerable<PlateCrustPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(patches);
        PlateCrustPatch[] copy = patches.Select(patch => patch.OwnedBy(plateId))
            .OrderBy(patch => patch.CellId, PlateStableIdComparer.Instance)
            .ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException("A plate domain must own at least one crust patch.", nameof(patches));
        }

        if (copy.Select(patch => patch.CellId).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("A plate domain cannot own the same cell twice.", nameof(patches));
        }

        PlateId = plateId;
        Velocity = velocity;
        Patches = Array.AsReadOnly(copy);
        CrustKinds = Array.AsReadOnly(copy.Select(patch => patch.CrustKind).Distinct().Order().ToArray());
    }

    public StableId PlateId { get; }

    public PlateVelocity Velocity { get; }

    public ReadOnlyCollection<PlateCrustPatch> Patches { get; }

    public ReadOnlyCollection<CrustKind> CrustKinds { get; }
}

public readonly record struct PlateCellState(
    StableId CellId,
    StableId PlateId,
    CrustKind CrustKind,
    int RelativeAgePpm,
    int ContinentalHeightPpm,
    double UpliftNormalized,
    double SubsidenceNormalized);

public readonly record struct PlateBoundaryRecord(
    StableId CellA,
    StableId CellB,
    StableId PlateA,
    StableId PlateB,
    PlateBoundaryKind Kind,
    double IntensityNormalized,
    double UpliftNormalized,
    double SubsidenceNormalized,
    double ShearNormalized);

public sealed record PlateGenerationSettings
{
    public PlateGenerationSettings(
        int plateCount,
        ContinentalFieldSettings continentalField,
        int maximumCells,
        int maximumBoundaryEdges,
        long maximumBoundaryInfluenceEvaluations)
    {
        ArgumentNullException.ThrowIfNull(continentalField);
        PlateCount = plateCount;
        ContinentalField = continentalField;
        MaximumCells = maximumCells;
        MaximumBoundaryEdges = maximumBoundaryEdges;
        MaximumBoundaryInfluenceEvaluations = maximumBoundaryInfluenceEvaluations;
    }

    public int PlateCount { get; }

    public ContinentalFieldSettings ContinentalField { get; }

    public int MaximumCells { get; }

    public int MaximumBoundaryEdges { get; }

    public long MaximumBoundaryInfluenceEvaluations { get; }
}

public sealed class PlateAtlasSnapshot
{
    internal PlateAtlasSnapshot(
        IEnumerable<PlateDomain> plates,
        IEnumerable<PlateCellState> cells,
        IEnumerable<PlateBoundaryRecord> boundaries,
        Hash256 continentalModelChecksum,
        GenerationIdentity identity,
        FrozenScaleProfile profile)
    {
        Plates = Array.AsReadOnly(plates.OrderBy(plate => plate.PlateId, PlateStableIdComparer.Instance).ToArray());
        Cells = Array.AsReadOnly(cells.OrderBy(cell => cell.CellId, PlateStableIdComparer.Instance).ToArray());
        Boundaries = Array.AsReadOnly(boundaries
            .OrderBy(boundary => boundary.CellA, PlateStableIdComparer.Instance)
            .ThenBy(boundary => boundary.CellB, PlateStableIdComparer.Instance)
            .ToArray());
        ContinentalModelChecksum = continentalModelChecksum;
        ScaleProfileId = profile.Id;
        ScaleProfileVersion = profile.ProfileVersion;
        AtlasResolutionBlocks = profile.AtlasResolutionBlocks;
        AtlasTileSizeBlocks = profile.AtlasTileSizeBlocks;
        ContentChecksum = ComputeChecksum(this, identity);
    }

    public ReadOnlyCollection<PlateDomain> Plates { get; }

    public ReadOnlyCollection<PlateCellState> Cells { get; }

    public ReadOnlyCollection<PlateBoundaryRecord> Boundaries { get; }

    public Hash256 ContinentalModelChecksum { get; }

    public string ScaleProfileId { get; }

    public uint ScaleProfileVersion { get; }

    public int AtlasResolutionBlocks { get; }

    public int AtlasTileSizeBlocks { get; }

    public Hash256 ContentChecksum { get; }

    private static Hash256 ComputeChecksum(PlateAtlasSnapshot snapshot, GenerationIdentity identity)
    {
        var builder = new StringBuilder();
        builder.Append("ISRW-PLATE-ATLAS-V1\n").Append(identity.NativeSeed).Append('|')
            .Append(identity.GeographyConfigHash).Append('|').Append(snapshot.ContinentalModelChecksum).Append('|')
            .Append(snapshot.ScaleProfileId).Append('|').Append(snapshot.ScaleProfileVersion).Append('|')
            .Append(snapshot.AtlasResolutionBlocks).Append('|').Append(snapshot.AtlasTileSizeBlocks).Append('\n');
        foreach (PlateDomain plate in snapshot.Plates)
        {
            builder.Append("P|").Append(plate.PlateId).Append('|')
                .Append(plate.Velocity.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(plate.Velocity.Z.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (PlateCellState cell in snapshot.Cells)
        {
            builder.Append("C|").Append(cell.CellId).Append('|').Append(cell.PlateId).Append('|')
                .Append((int)cell.CrustKind).Append('|').Append(cell.RelativeAgePpm).Append('|')
                .Append(cell.ContinentalHeightPpm).Append('|')
                .Append(cell.UpliftNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(cell.SubsidenceNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (PlateBoundaryRecord boundary in snapshot.Boundaries)
        {
            builder.Append("B|").Append(boundary.CellA).Append('|').Append(boundary.CellB).Append('|')
                .Append(boundary.PlateA).Append('|').Append(boundary.PlateB).Append('|').Append((int)boundary.Kind).Append('|')
                .Append(boundary.IntensityNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(boundary.UpliftNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(boundary.SubsidenceNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(boundary.ShearNormalized.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        return Hash256.Compute(Encoding.UTF8.GetBytes(builder.ToString()));
    }
}

public static class PlateAtlasBuilder
{
    public static GenerationResult<PlateAtlasSnapshot> Build(
        GenerationIdentity identity,
        AtlasMesh atlas,
        FrozenScaleProfile profile,
        PlateGenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        if (identity.GeographyConfigHash != profile.GeographyConfigHash)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.plates.profile-hash",
                "Generation identity and frozen scale profile have different geography configuration hashes.");
        }

        WorldDomain domain = profile.AtlasIndexProfile.Domain;
        if (atlas.Bounds.MinX != domain.X.MinInclusive || atlas.Bounds.MinZ != domain.Z.MinInclusive ||
            atlas.Bounds.MaxXExclusive != domain.X.MaxExclusive || atlas.Bounds.MaxZExclusive != domain.Z.MaxExclusive)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.plates.profile-domain",
                "Atlas bounds do not match the frozen scale profile domain.");
        }

        if (atlas.Sites.Count > profile.SiteQuota)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.plates.profile-site-quota",
                $"Atlas has {atlas.Sites.Count} sites for frozen profile quota {profile.SiteQuota}.");
        }
        if (settings.PlateCount <= 0 || settings.PlateCount > atlas.Sites.Count)
        {
            return Failure(identity, GenerationFailureCode.InvalidInput, "geology.plates.count",
                "Plate count must be positive and no greater than the atlas site count.");
        }

        if (settings.MaximumCells <= 0 || atlas.Cells.Count > settings.MaximumCells)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.plates.cell-budget",
                $"Atlas has {atlas.Cells.Count} cells for budget {settings.MaximumCells}.");
        }

        if (settings.MaximumBoundaryEdges <= 0 || atlas.Edges.Count > settings.MaximumBoundaryEdges)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.plates.edge-budget",
                $"Atlas has {atlas.Edges.Count} edges for budget {settings.MaximumBoundaryEdges}.");
        }

        BigInteger maximumInfluenceEvaluations = (BigInteger)atlas.Edges.Count * atlas.Cells.Count;
        if (settings.MaximumBoundaryInfluenceEvaluations <= 0 ||
            maximumInfluenceEvaluations > settings.MaximumBoundaryInfluenceEvaluations)
        {
            return Failure(identity, GenerationFailureCode.BudgetExceeded, "geology.plates.influence-budget",
                $"Worst-case boundary spread requires {maximumInfluenceEvaluations} evaluations for budget " +
                $"{settings.MaximumBoundaryInfluenceEvaluations}.");
        }

        GenerationResult<ContinentalFieldModel> continentalResult = ContinentalFieldModel.Create(
            identity,
            profile,
            settings.ContinentalField);
        if (continentalResult is GenerationFailure<ContinentalFieldModel> continentalFailure)
        {
            return GenerationResult<PlateAtlasSnapshot>.Failure(continentalFailure.Error);
        }

        ContinentalFieldModel continental = ((GenerationSuccess<ContinentalFieldModel>)continentalResult).Snapshot;
        AtlasSite[] sites = atlas.Sites.ToArray();
        AtlasSite[] anchors = Enumerable.Range(0, settings.PlateCount)
            .Select(index => sites[(index * sites.Length) / settings.PlateCount])
            .ToArray();
        PlateKinematics[] kinematics = Enumerable.Range(0, settings.PlateCount)
            .Select(index => CreateKinematics(identity.NativeSeed, checked((ulong)index)))
            .ToArray();
        var kinematicsById = kinematics.ToDictionary(plate => plate.PlateId);
        var plateByCell = new Dictionary<StableId, StableId>();
        var heightByCell = new Dictionary<StableId, int>();
        var ageByCell = new Dictionary<StableId, int>();
        foreach (AtlasSite site in sites)
        {
            int anchor = NearestAnchor(site, anchors);
            plateByCell.Add(site.Id, kinematics[anchor].PlateId);
            heightByCell.Add(site.Id, continental.SampleHeightPpm(site.X, site.Z));
            ageByCell.Add(site.Id, checked((int)(StatelessRandomV1.NextUInt64(
                identity.NativeSeed,
                RandomDomain.Geology,
                site.Id,
                31) % (ContinentalFieldModel.PartsPerMillion + 1UL))));
        }

        var siteById = sites.ToDictionary(site => site.Id);
        var uplift = sites.ToDictionary(site => site.Id, _ => 0d);
        var subsidence = sites.ToDictionary(site => site.Id, _ => 0d);
        var boundaries = new List<PlateBoundaryRecord>();
        foreach (AtlasEdge edge in atlas.Edges)
        {
            StableId plateA = plateByCell[edge.A];
            StableId plateB = plateByCell[edge.B];
            if (plateA == plateB)
            {
                continue;
            }

            AtlasSite siteA = siteById[edge.A];
            AtlasSite siteB = siteById[edge.B];
            UnitDirection2 normal = UnitDirection2.FromComponents(
                (double)siteB.X - siteA.X,
                (double)siteB.Z - siteA.Z);
            PlateBoundaryEffect effect = PlateBoundaryEvaluator.Evaluate(
                kinematicsById[plateA],
                kinematicsById[plateB],
                normal);
            uplift[edge.A] = Math.Max(uplift[edge.A], effect.UpliftNormalized);
            uplift[edge.B] = Math.Max(uplift[edge.B], effect.UpliftNormalized);
            subsidence[edge.A] = Math.Max(subsidence[edge.A], effect.SubsidenceNormalized);
            subsidence[edge.B] = Math.Max(subsidence[edge.B], effect.SubsidenceNormalized);
            SpreadBoundaryField(atlas.Bounds, sites, siteA, siteB, effect, uplift, subsidence);
            boundaries.Add(new PlateBoundaryRecord(
                edge.A,
                edge.B,
                plateA,
                plateB,
                effect.Kind,
                effect.IntensityNormalized,
                effect.UpliftNormalized,
                effect.SubsidenceNormalized,
                effect.ShearNormalized));
        }

        PlateCellState[] cells = sites.Select(site => new PlateCellState(
            site.Id,
            plateByCell[site.Id],
            Crust(heightByCell[site.Id]),
            ageByCell[site.Id],
            heightByCell[site.Id],
            uplift[site.Id],
            subsidence[site.Id])).ToArray();
        PlateDomain[] plates = kinematics.Select(plate => new PlateDomain(
            plate.PlateId,
            plate.Velocity,
            cells.Where(cell => cell.PlateId == plate.PlateId)
                .Select(cell => new PlateCrustPatch(cell.CellId, cell.CrustKind, cell.RelativeAgePpm)))).ToArray();
        return GenerationResult<PlateAtlasSnapshot>.Success(
            new PlateAtlasSnapshot(plates, cells, boundaries, continental.ContentChecksum, identity, profile));
    }

    private static PlateKinematics CreateKinematics(int seed, ulong index)
    {
        StableId id = StableId.Derive(RandomDomain.Geology, StableId.Zero, index + 10_000);
        double angle = Unit(seed, id, 0) * Math.Tau;
        double magnitude = 0.2 + (0.8 * Unit(seed, id, 1));
        return new PlateKinematics(id, new PlateVelocity(Math.Cos(angle) * magnitude, Math.Sin(angle) * magnitude));
    }

    private static int NearestAnchor(AtlasSite site, IReadOnlyList<AtlasSite> anchors)
    {
        int bestIndex = 0;
        BigInteger bestDistance = DistanceSquared(site, anchors[0]);
        for (int index = 1; index < anchors.Count; index++)
        {
            BigInteger candidate = DistanceSquared(site, anchors[index]);
            if (candidate < bestDistance)
            {
                bestDistance = candidate;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static BigInteger DistanceSquared(AtlasSite left, AtlasSite right)
    {
        BigInteger dx = (BigInteger)left.X - right.X;
        BigInteger dz = (BigInteger)left.Z - right.Z;
        return (dx * dx) + (dz * dz);
    }

    /// <summary>
    /// Combines a narrow boundary envelope and a lower-amplitude regional envelope. Both are measured in
    /// fractions of the finite world domain, and the hard regional cutoff leaves explicit calm interiors.
    /// </summary>
    private static void SpreadBoundaryField(
        WorldBounds bounds,
        IReadOnlyList<AtlasSite> sites,
        AtlasSite edgeA,
        AtlasSite edgeB,
        PlateBoundaryEffect effect,
        IDictionary<StableId, double> uplift,
        IDictionary<StableId, double> subsidence)
    {
        double midpointX = ((double)edgeA.X + edgeB.X) / 2;
        double midpointZ = ((double)edgeA.Z + edgeB.Z) / 2;
        foreach (AtlasSite site in sites)
        {
            double dx = (site.X - midpointX) / bounds.Width;
            double dz = (site.Z - midpointZ) / bounds.Length;
            double distance = Math.Sqrt((dx * dx) + (dz * dz));
            if (distance >= 0.14)
            {
                continue;
            }

            double local = Math.Max(0, 1 - (distance / 0.045));
            double regional = Math.Max(0, 1 - (distance / 0.14));
            double influence = (0.7 * local) + (0.3 * regional);
            uplift[site.Id] = Math.Max(uplift[site.Id], effect.UpliftNormalized * influence);
            subsidence[site.Id] = Math.Max(subsidence[site.Id], effect.SubsidenceNormalized * influence);
        }
    }

    private static CrustKind Crust(int heightPpm) => heightPpm switch
    {
        < -50_000 => CrustKind.Oceanic,
        > 50_000 => CrustKind.Continental,
        _ => CrustKind.Transitional,
    };

    private static double Unit(int seed, StableId id, ulong counter) =>
        (StatelessRandomV1.NextUInt64(seed, RandomDomain.Geology, id, counter) >> 11) *
        (1.0 / (1UL << 53));

    private static GenerationResult<PlateAtlasSnapshot> Failure(
        GenerationIdentity identity,
        GenerationFailureCode code,
        string stage,
        string details) =>
        GenerationResult<PlateAtlasSnapshot>.Failure(new GenerationError(
            code,
            identity.NativeSeed,
            stage,
            StableId.Zero,
            identity.GeographyConfigHash,
            details,
            false));

}

internal sealed class PlateStableIdComparer : IComparer<StableId>
{
    internal static PlateStableIdComparer Instance { get; } = new();

    public int Compare(StableId x, StableId y)
    {
        int high = x.High.CompareTo(y.High);
        return high != 0 ? high : x.Low.CompareTo(y.Low);
    }
}
