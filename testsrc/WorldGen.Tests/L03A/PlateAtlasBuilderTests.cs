using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Plates;
using System.Globalization;

namespace ISRWorldGen.Tests.L03A;

[TestClass]
public sealed class PlateAtlasBuilderTests
{
    [TestMethod]
    public void AtlasCellsProduceCanonicalPlateSnapshotWithBoundaryFields()
    {
        FrozenScaleProfile profile = L03ATestSupport.FrozenProfile("balanced");
        WorldBounds bounds = L03ATestSupport.Bounds(profile);
        var identity = L03ATestSupport.Identity(20260906, profile);
        GeneratedSiteSet sites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            identity,
            bounds,
            new AtlasSiteGenerationSettings(64)));
        AtlasMesh mesh = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            identity,
            bounds,
            sites.Sites,
            new AtlasGeometryBuildOptions(4, GeometryCacheMode.Precomputed)));
        PlateGenerationSettings settings = new(
            plateCount: 7,
            L03ATestSupport.ContinentalSettings(),
            maximumCells: 1_000,
            maximumBoundaryEdges: 4_000,
            maximumBoundaryInfluenceEvaluations: 4_000_000);

        PlateAtlasSnapshot first = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(identity, mesh, profile, settings));
        PlateAtlasSnapshot reversed = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(identity, mesh, profile, settings));

        Assert.HasCount(7, first.Plates);
        Assert.HasCount(mesh.Cells.Count, first.Cells);
        Assert.IsGreaterThan(0, first.Boundaries.Count);
        Assert.AreEqual(first.ContentChecksum, reversed.ContentChecksum);
        Assert.AreEqual(profile.Id, first.ScaleProfileId);
        Assert.AreEqual(profile.AtlasTileSizeBlocks, first.AtlasTileSizeBlocks);
        CollectionAssert.AreEqual(first.Cells.ToArray(), reversed.Cells.ToArray());
        Assert.IsTrue(first.Cells.All(cell => double.IsFinite(cell.UpliftNormalized)));
        Assert.IsTrue(first.Cells.All(cell => double.IsFinite(cell.SubsidenceNormalized)));
        Assert.IsTrue(first.Cells.All(cell => cell.RelativeAgePpm is >= 0 and <= 1_000_000));
        Assert.IsTrue(first.Cells.All(cell => cell.ContinentalHeightPpm is >= -1_000_000 and <= 1_000_000));
        var boundaryCells = first.Boundaries.SelectMany(boundary => new[] { boundary.CellA, boundary.CellB }).ToHashSet();
        Assert.IsTrue(first.Cells.Any(cell =>
            !boundaryCells.Contains(cell.CellId) && (cell.UpliftNormalized > 0 || cell.SubsidenceNormalized > 0)),
            "Boundary fields did not reach their regional envelope.");
        Assert.IsTrue(first.Cells.Any(cell => cell.UpliftNormalized == 0 && cell.SubsidenceNormalized == 0),
            "No calm plate interior remains outside the bounded regional envelope.");
        Assert.IsGreaterThanOrEqualTo(3, first.Cells
            .Select(cell => Math.Max(cell.UpliftNormalized, cell.SubsidenceNormalized))
            .Where(value => value > 0)
            .Distinct()
            .Count(),
            "Local and regional boundary envelopes did not produce multiple field amplitudes.");
        Assert.IsTrue(first.Plates.Any(plate => plate.CrustKinds.Count > 1),
            "At least one generated plate must demonstrate mixed crust domains on this frozen fixture.");
    }

    [TestMethod]
    [DoNotParallelize]
    public void PlateBuilderRefusesImpossibleCountsAndBudgets()
    {
        FrozenScaleProfile profile = L03ATestSupport.FrozenProfile("laboratory");
        WorldBounds bounds = L03ATestSupport.Bounds(profile);
        GenerationIdentity identity = L03ATestSupport.Identity(73, profile);
        GeneratedSiteSet sites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            identity, bounds, new AtlasSiteGenerationSettings(16)));
        AtlasMesh mesh = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            identity, bounds, sites.Sites));

        Assert.IsFalse(PlateAtlasBuilder.Build(
            identity, mesh, profile,
            new PlateGenerationSettings(17, L03ATestSupport.ContinentalSettings(), 100, 100, 10_000)).IsSuccess);
        Assert.IsFalse(PlateAtlasBuilder.Build(
            identity, mesh, profile,
            new PlateGenerationSettings(2, L03ATestSupport.ContinentalSettings(), 15, 100, 10_000)).IsSuccess);

        long allocatedBeforeInfluencePreflight = GC.GetAllocatedBytesForCurrentThread();
        GenerationResult<PlateAtlasSnapshot> influenceBudget = PlateAtlasBuilder.Build(
            identity,
            mesh,
            profile,
            new PlateGenerationSettings(2, L03ATestSupport.ContinentalSettings(), 100, 100, 1));
        long influencePreflightAllocation =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeInfluencePreflight;
        Assert.IsInstanceOfType<GenerationFailure<PlateAtlasSnapshot>>(influenceBudget);
        Assert.AreEqual(
            GenerationFailureCode.BudgetExceeded,
            ((GenerationFailure<PlateAtlasSnapshot>)influenceBudget).Error.Code);
        Assert.AreEqual(
            "geology.plates.influence-budget",
            ((GenerationFailure<PlateAtlasSnapshot>)influenceBudget).Error.Stage);
        Assert.IsLessThan(64 * 1024L, influencePreflightAllocation,
            "The coupled work-budget refusal must occur before continental construction or spread allocation.");
    }

    [TestMethod]
    public void PlateSnapshotSealsCompleteIdentityAndCanonicalAtlasProvenance()
    {
        FrozenScaleProfile profile = L03ATestSupport.FrozenProfile("balanced");
        WorldBounds bounds = L03ATestSupport.Bounds(profile);
        GenerationIdentity identityA = L03ATestSupport.Identity(20260906, profile);
        GenerationIdentity identityB = L03ATestSupport.Identity(20260907, profile);
        AtlasMesh atlasA = BuildAtlas(identityA, bounds);
        AtlasMesh atlasB = BuildAtlas(identityB, bounds);
        PlateGenerationSettings settings = new(
            plateCount: 7,
            L03ATestSupport.ContinentalSettings(),
            maximumCells: 1_000,
            maximumBoundaryEdges: 4_000,
            maximumBoundaryInfluenceEvaluations: 4_000_000);

        PlateAtlasSnapshot snapshotA = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(identityA, atlasA, profile, settings));
        PlateAtlasSnapshot snapshotB = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(identityB, atlasB, profile, settings));

        Assert.AreEqual(identityA, snapshotA.Identity);
        Assert.AreEqual(PlateAtlasProvenance.ComputeAtlasContentChecksum(atlasA), snapshotA.AtlasContentChecksum);
        Assert.IsTrue(PlateAtlasProvenance.Matches(snapshotA, identityA, profile, atlasA));
        Assert.IsFalse(PlateAtlasProvenance.Matches(snapshotA, identityA, profile, atlasB),
            "A snapshot must reject a geometrically different Atlas even when the profile remains valid.");
        Assert.IsFalse(PlateAtlasProvenance.Matches(snapshotA, identityB, profile, atlasA),
            "A snapshot must reject an Atlas paired with a different complete generation identity.");
        Assert.AreNotEqual(snapshotA.ContentChecksum, snapshotB.ContentChecksum);

        GenerationIdentity changedAssetAndProfile = new(
            identityA.NativeSeed,
            identityA.AlgorithmVersion,
            identityA.SchemaVersion,
            identityA.GeographyConfigHash,
            Hash256.Zero,
            "l03a-provenance-regression-v2");
        PlateAtlasSnapshot changedIdentity = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(changedAssetAndProfile, atlasA, profile, settings));
        Assert.AreNotEqual(snapshotA.ContentChecksum, changedIdentity.ContentChecksum,
            "Asset hash and determinism profile are persistent parents of the plate snapshot.");

        AtlasMesh rebuiltWithReversedInput = BuildAtlas(identityA, bounds, reverseInput: true);
        Assert.AreEqual(
            PlateAtlasProvenance.ComputeAtlasContentChecksum(atlasA),
            PlateAtlasProvenance.ComputeAtlasContentChecksum(rebuiltWithReversedInput),
            "Canonical atlas provenance must not depend on source site enumeration order.");
    }

    [TestMethod]
    [DoNotParallelize]
    public void PlateSnapshotChecksumIsInvariantToCurrentCultureAndNumericSigns()
    {
        FrozenScaleProfile profile = L03ATestSupport.FrozenProfile("balanced");
        GenerationIdentity identity = L03ATestSupport.Identity(-20260906, profile);
        AtlasMesh atlas = BuildAtlas(identity, L03ATestSupport.Bounds(profile));
        PlateGenerationSettings settings = new(
            plateCount: 7,
            L03ATestSupport.ContinentalSettings(),
            maximumCells: 1_000,
            maximumBoundaryEdges: 4_000,
            maximumBoundaryInfluenceEvaluations: 4_000_000);
        PlateAtlasSnapshot snapshot = L03ATestSupport.Success(
            PlateAtlasBuilder.Build(identity, atlas, profile, settings));
        Hash256 baseline = snapshot.ContentChecksum;
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        var altered = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        altered.NumberFormat.NegativeSign = "~";
        altered.NumberFormat.NumberDecimalSeparator = ",";

        try
        {
            CultureInfo.CurrentCulture = altered;
            CultureInfo.CurrentUICulture = altered;
            Hash256 underAlteredCulture = snapshot.RecomputeContentChecksum();
            Assert.AreEqual(baseline, underAlteredCulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestMethod]
    public void PlateBuilderBoundsDeterminismProfileUtf8BeforeSnapshotAllocation()
    {
        FrozenScaleProfile profile = L03ATestSupport.FrozenProfile("balanced");
        GenerationIdentity baseIdentity = L03ATestSupport.Identity(73, profile);
        AtlasMesh atlas = BuildAtlas(baseIdentity, L03ATestSupport.Bounds(profile));
        PlateGenerationSettings settings = new(
            plateCount: 7,
            L03ATestSupport.ContinentalSettings(),
            maximumCells: 1_000,
            maximumBoundaryEdges: 4_000,
            maximumBoundaryInfluenceEvaluations: 4_000_000);

        GenerationIdentity maximumLength = WithProfileId(baseIdentity, new string('a', 128));
        Assert.IsTrue(PlateAtlasBuilder.Build(maximumLength, atlas, profile, settings).IsSuccess);

        GenerationIdentity maximumUtf8Length = WithProfileId(baseIdentity, new string('é', 64));
        Assert.IsTrue(PlateAtlasBuilder.Build(maximumUtf8Length, atlas, profile, settings).IsSuccess,
            "64 occurrences of é occupy exactly 128 UTF-8 bytes.");

        GenerationIdentity tooLong = WithProfileId(baseIdentity, new string('a', 129));
        AssertProfileTooLong(tooLong, atlas, profile, settings);
        GenerationIdentity tooLongUtf8 = WithProfileId(baseIdentity, new string('é', 65));
        AssertProfileTooLong(tooLongUtf8, atlas, profile, settings);
    }

    private static AtlasMesh BuildAtlas(GenerationIdentity identity, WorldBounds bounds, bool reverseInput = false)
    {
        GeneratedSiteSet generated = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            identity,
            bounds,
            new AtlasSiteGenerationSettings(64)));
        IEnumerable<AtlasSite> sites = reverseInput ? generated.Sites.Reverse() : generated.Sites;
        return L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            identity,
            bounds,
            sites,
            new AtlasGeometryBuildOptions(4, GeometryCacheMode.Precomputed)));
    }

    private static GenerationIdentity WithProfileId(GenerationIdentity identity, string profileId) => new(
        identity.NativeSeed,
        identity.AlgorithmVersion,
        identity.SchemaVersion,
        identity.GeographyConfigHash,
        identity.GenerationAssetHash,
        profileId);

    private static void AssertProfileTooLong(
        GenerationIdentity identity,
        AtlasMesh atlas,
        FrozenScaleProfile profile,
        PlateGenerationSettings settings)
    {
        GenerationResult<PlateAtlasSnapshot> result = PlateAtlasBuilder.Build(identity, atlas, profile, settings);
        Assert.IsInstanceOfType<GenerationFailure<PlateAtlasSnapshot>>(result);
        GenerationError error = ((GenerationFailure<PlateAtlasSnapshot>)result).Error;
        Assert.AreEqual(GenerationFailureCode.InvalidInput, error.Code);
        Assert.AreEqual("geology.plates.determinism-profile", error.Stage);
    }
}
