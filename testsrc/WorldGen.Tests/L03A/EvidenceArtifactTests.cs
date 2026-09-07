using System.Text;
using System.Text.Json;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03A;

[TestClass]
public sealed class EvidenceArtifactTests
{
    private const int MinimumCurvedCoastCases = 192;
    private const int ComponentAreaSmallMaximumSamples = 299;
    private const int ComponentAreaLargeMinimumSamples = 751;
    private const int TileModuloDiversityMinimum = 8;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [TestMethod]
    [DoNotParallelize]
    public void T03EvidenceRecordsParametersMetricsAndTwoHashedReviewZooms()
    {
        string output = Path.Combine(L03ATestSupport.FindRepositoryRoot(), ".local", "L03A", "evidence");
        Directory.CreateDirectory(output);
        string reportPath = Path.Combine(output, "T03-01-02.json");
        string commit = Environment.GetEnvironmentVariable("ISR_L03A_EVIDENCE_COMMIT") ?? "WORKING_TREE";
        string configuration = Environment.GetEnvironmentVariable("ISR_L03A_EVIDENCE_CONFIGURATION") ?? "UNKNOWN";
        WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            requirementIds = new[] { "R03-01", "R03-02" },
            automatedStatus = "RUNNING",
            qualitativeReviewStatus = "REVIEW_REQUIRED",
            overallStatus = "RUNNING",
            commit,
            configuration,
        }, JsonOptions));

        try
        {
            RunCampaignAndWritePassingEvidence(output, reportPath, commit, configuration);
        }
        catch (Exception exception)
        {
            WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                requirementIds = new[] { "R03-01", "R03-02" },
                automatedStatus = "FAIL",
                qualitativeReviewStatus = "NOT_RUN",
                overallStatus = "FAIL",
                commit,
                configuration,
                failure = new
                {
                    type = exception.GetType().FullName,
                    exception.Message,
                },
            }, JsonOptions));
            throw;
        }
    }

    private static void RunCampaignAndWritePassingEvidence(
        string output,
        string reportPath,
        string commit,
        string configuration)
    {
        const int seed = 73;
        FrozenScaleProfile balanced = L03ATestSupport.FrozenProfile("balanced");
        FrozenScaleProfile vast = L03ATestSupport.FrozenProfile("vast-expeditions");
        ContinentalFieldSettings settings = L03ATestSupport.ContinentalSettings();
        IReadOnlyList<int> calibrationSeeds = L03ATestSupport.SeedCorpus("calibration_seeds");
        IReadOnlyList<int> holdoutSeeds = L03ATestSupport.SeedCorpus("holdout_seeds");
        Assert.HasCount(192, calibrationSeeds);
        Assert.HasCount(64, holdoutSeeds);

        CorpusMetric[] calibrationCases = calibrationSeeds
            .Select(item => CorpusMetrics("calibration", item, balanced, settings))
            .ToArray();
        CorpusMetric[] holdoutCases = holdoutSeeds
            .Select(item => CorpusMetrics("holdout", item, vast, settings))
            .ToArray();
        CorpusMetric[] allCases = calibrationCases.Concat(holdoutCases).ToArray();
        Assert.HasCount(256, allCases);
        Assert.IsTrue(calibrationCases.All(item => item.ProfileId == "balanced"));
        Assert.IsTrue(holdoutCases.All(item => item.ProfileId == "vast-expeditions"));
        Assert.IsTrue(allCases.All(item => item.LandSampleCount > 0));
        Assert.IsTrue(allCases.All(item => item.OpenOceanSampleCount > 0));
        Assert.IsTrue(allCases.All(item => item.OpenOceanComponentCount > 0));
        Assert.IsTrue(allCases.All(item => item.CoastlineSegmentCount > 0));
        Assert.IsTrue(allCases.All(item =>
            item.LandSampleCount + item.OpenOceanSampleCount + item.InlandBasinSampleCount ==
            item.Grid.Width * item.Grid.Height));
        Assert.IsTrue(allCases.All(item => item.LongestStraightCoastRun * 5 <= item.CoastlineSegmentCount),
            "At least one corpus case has a single straight run above the declared 20% ceiling.");
        Assert.IsTrue(allCases.All(item => item.TileAlignedCoastlineSegments * 5 <= item.CoastlineSegmentCount),
            "At least one corpus case aligns more than 20% of its coast with its real profile tile size.");
        Assert.IsTrue(allCases.All(item => item.StorageTileBoundaryLinesTested > 0),
            "Every profile campaign grid must cross at least one real storage-tile boundary.");
        int mapsWithCurvedCoasts = allCases.Count(item =>
            item.CoastlineCornerCount * 20 >= item.CoastlineBoundaryCellCount);
        Assert.IsGreaterThanOrEqualTo(MinimumCurvedCoastCases, mapsWithCurvedCoasts,
            "At least 75% of the frozen 256-case corpus must show frequent coastline direction changes.");
        int[] allComponentAreas = allCases.SelectMany(item => item.LandComponentAreas).ToArray();
        Assert.IsTrue(allComponentAreas.Any(area => area <= ComponentAreaSmallMaximumSamples),
            "Regional landforms are absent from the frozen campaign.");
        Assert.IsTrue(allComponentAreas.Any(area => area >= ComponentAreaLargeMinimumSamples),
            "Continental landforms are absent from the frozen campaign.");
        int componentAreaResidues = allComponentAreas.Select(area => area % 32).Distinct().Count();
        Assert.IsGreaterThanOrEqualTo(TileModuloDiversityMinimum, componentAreaResidues,
            "Component areas have insufficient dispersion for the declared anti-quantization alarm.");
        Assert.IsTrue(calibrationCases.Any(item => item.InlandBasinSampleCount > 0),
            "The calibration corpus has no reproducible inland-basin witness.");

        int marineWitnessSeed = calibrationCases
            .Where(item => item.InlandBasinSampleCount > 0)
            .OrderByDescending(item => item.InlandBasinSampleCount)
            .ThenBy(item => item.Seed)
            .First()
            .Seed;
        GenerationIdentity identity = L03ATestSupport.Identity(seed, balanced);
        ContinentalFieldModel model = L03ATestSupport.Success(
            ContinentalFieldModel.Create(identity, balanced, settings));
        ContinentalRaster continent = L03ATestSupport.Success(model.Rasterize(
            L03ATestSupport.ProfileGrid(balanced, 192, 192)));
        ContinentalRaster region = L03ATestSupport.Success(model.Rasterize(
            L03ATestSupport.CenteredGrid(
                balanced,
                balanced.AtlasTileSizeBlocks / 4,
                width: 192,
                height: 128)));
        ContinentalTopologyReport continentTopology = ContinentalTopologyAnalyzer.Analyze(continent);
        ContinentalTopologyReport regionTopology = ContinentalTopologyAnalyzer.Analyze(region);
        ContinentalFieldModel marineModel = L03ATestSupport.Success(ContinentalFieldModel.Create(
            L03ATestSupport.Identity(marineWitnessSeed, balanced),
            balanced,
            settings));
        ContinentalRaster marineRaster = L03ATestSupport.Success(marineModel.Rasterize(
            L03ATestSupport.ProfileGrid(balanced, 96, 96)));
        ContinentalTopologyReport marineTopology = ContinentalTopologyAnalyzer.Analyze(marineRaster);

        WorldBounds balancedBounds = L03ATestSupport.Bounds(balanced);
        GeneratedSiteSet atlasSites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            identity,
            balancedBounds,
            new AtlasSiteGenerationSettings(balanced.RequestedSiteCount)));
        AtlasMesh atlas = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            identity,
            balancedBounds,
            atlasSites.Sites,
            new AtlasGeometryBuildOptions(4, GeometryCacheMode.Precomputed)));
        PlateAtlasSnapshot plateSnapshot = L03ATestSupport.Success(PlateAtlasBuilder.Build(
            identity,
            atlas,
            balanced,
            new PlateGenerationSettings(
                7,
                settings,
                maximumCells: balanced.SiteQuota,
                maximumBoundaryEdges: 4_000,
                maximumBoundaryInfluenceEvaluations: 1_000_000)));
        (int coastSegments, int atlasAligned, int tileAligned) =
            L03ATestSupport.CoastlineAlignment(atlas, continent, balanced.AtlasTileSizeBlocks);

        PlateKinematics plateA = new(L03ATestSupport.PlateId(0), new PlateVelocity(1, 0));
        PlateKinematics collisionB = new(L03ATestSupport.PlateId(1), new PlateVelocity(-1, 0));
        PlateKinematics divergenceA = new(L03ATestSupport.PlateId(0), new PlateVelocity(-1, 0));
        PlateKinematics divergenceB = new(L03ATestSupport.PlateId(1), new PlateVelocity(1, 0));
        PlateKinematics shearA = new(L03ATestSupport.PlateId(0), new PlateVelocity(0, 1));
        PlateKinematics shearB = new(L03ATestSupport.PlateId(1), new PlateVelocity(0, -1));
        UnitDirection2 normal = UnitDirection2.FromComponents(1, 0);
        PlateBoundaryEffect collision = PlateBoundaryEvaluator.Evaluate(plateA, collisionB, normal);
        PlateBoundaryEffect reversedCollision = PlateBoundaryEvaluator.Evaluate(collisionB, plateA, normal.Negated());
        PlateBoundaryEffect divergence = PlateBoundaryEvaluator.Evaluate(divergenceA, divergenceB, normal);
        PlateBoundaryEffect shear = PlateBoundaryEvaluator.Evaluate(shearA, shearB, normal);

        Assert.AreEqual(PlateBoundaryKind.Collision, collision.Kind);
        Assert.AreEqual(PlateBoundaryKind.Divergence, divergence.Kind);
        Assert.AreEqual(PlateBoundaryKind.Shear, shear.Kind);
        Assert.AreEqual(collision, reversedCollision);
        AssertBoundaryFinite(collision);
        AssertBoundaryFinite(divergence);
        AssertBoundaryFinite(shear);
        Assert.AreEqual(balanced.Id, plateSnapshot.ScaleProfileId);
        Assert.AreEqual(balanced.ProfileVersion, plateSnapshot.ScaleProfileVersion);
        Assert.AreEqual(balanced.AtlasResolutionBlocks, plateSnapshot.AtlasResolutionBlocks);
        Assert.AreEqual(balanced.AtlasTileSizeBlocks, plateSnapshot.AtlasTileSizeBlocks);
        Assert.IsTrue(plateSnapshot.Plates.Any(plate => plate.CrustKinds.Count > 1));
        Assert.IsGreaterThan(0, coastSegments);
        Assert.IsLessThanOrEqualTo(coastSegments, atlasAligned * 5,
            "More than 20% of coastline transitions align with atlas/Voronoi ownership edges.");
        Assert.IsLessThanOrEqualTo(coastSegments, tileAligned * 5,
            "More than 20% of coastline transitions align with balanced profile storage boundaries.");
        Assert.IsGreaterThan(0, marineTopology.OpenOceanSampleCount);
        Assert.IsGreaterThan(0, marineTopology.InlandBasinSampleCount);
        Assert.HasCount(continentTopology.CoastlineSegmentCount, continentTopology.CoastlineSegments);
        Assert.HasCount(regionTopology.CoastlineSegmentCount, regionTopology.CoastlineSegments);

        byte[] continentBitmap = L03ATestSupport.RenderTopologyBitmap(continent);
        byte[] regionBitmap = L03ATestSupport.RenderTopologyBitmap(region);
        byte[] marineBitmap = L03ATestSupport.RenderMarineBitmap(marineRaster, marineTopology);
        byte[] platesBitmap = L03ATestSupport.RenderPlateLayerBitmap(
            atlas, plateSnapshot, continent, PlateReviewLayer.Plate);
        byte[] crustBitmap = L03ATestSupport.RenderPlateLayerBitmap(
            atlas, plateSnapshot, continent, PlateReviewLayer.Crust);
        byte[] boundaryFieldBitmap = L03ATestSupport.RenderPlateLayerBitmap(
            atlas, plateSnapshot, continent, PlateReviewLayer.BoundaryField);
        byte[] atlasOverlayBitmap = L03ATestSupport.RenderPlateLayerBitmap(
            atlas, plateSnapshot, continent, PlateReviewLayer.ContinentalAtlasOverlay);
        CollectionAssert.AreEqual(new byte[] { (byte)'B', (byte)'M' }, continentBitmap[..2]);
        CollectionAssert.AreNotEqual(continentBitmap, regionBitmap);

        string continentPath = Path.Combine(output, "T03-02-continent.bmp");
        string regionPath = Path.Combine(output, "T03-02-region.bmp");
        string marinePath = Path.Combine(output, "T03-02-marine-domains.bmp");
        string platesPath = Path.Combine(output, "T03-02-plates.bmp");
        string crustPath = Path.Combine(output, "T03-02-crust.bmp");
        string boundaryFieldPath = Path.Combine(output, "T03-02-uplift-subsidence.bmp");
        string atlasOverlayPath = Path.Combine(output, "T03-02-atlas-overlay.bmp");
        string contourPath = Path.Combine(output, "T03-02-contours.csv");
        byte[] contourBytes = Encoding.UTF8.GetBytes(RenderContours(continentTopology, regionTopology));
        WriteAtomic(continentPath, continentBitmap);
        WriteAtomic(regionPath, regionBitmap);
        WriteAtomic(marinePath, marineBitmap);
        WriteAtomic(platesPath, platesBitmap);
        WriteAtomic(crustPath, crustBitmap);
        WriteAtomic(boundaryFieldPath, boundaryFieldBitmap);
        WriteAtomic(atlasOverlayPath, atlasOverlayBitmap);
        WriteAtomic(contourPath, contourBytes);
        Assert.IsTrue(new[]
        {
            continentPath,
            regionPath,
            marinePath,
            platesPath,
            crustPath,
            boundaryFieldPath,
            atlasOverlayPath,
            contourPath,
        }.All(File.Exists));

        string fixturePath = Path.Combine(L03ATestSupport.FindRepositoryRoot(), "registry", "fixtures.json");
        object report = new
        {
            schemaVersion = 1,
            requirementIds = new[] { "R03-01", "R03-02" },
            automatedStatus = "PASS",
            qualitativeReviewStatus = "REVIEW_REQUIRED",
            overallStatus = "REVIEW_REQUIRED",
            reason = "T03-02 has no normative numeric thresholds; hashed region and continent views require independent review.",
            commit,
            configuration,
            targetFramework = "net10.0",
            seed,
            seedCorpus = new
            {
                calibrationSeeds,
                holdoutSeeds,
                fixturesSha256 = L03ATestSupport.Sha256(File.ReadAllBytes(fixturePath)),
                profiles = new[] { ProfileMetrics(balanced), ProfileMetrics(vast) },
            },
            model = new
            {
                bounds = new
                {
                    model.Bounds.MinX,
                    model.Bounds.MinZ,
                    model.Bounds.MaxXExclusive,
                    model.Bounds.MaxZExclusive,
                    model.Bounds.Width,
                    model.Bounds.Length,
                },
                contentChecksum = model.ContentChecksum.ToString(),
                plateSnapshotChecksum = plateSnapshot.ContentChecksum.ToString(),
                plateSnapshot.ScaleProfileId,
                plateSnapshot.ScaleProfileVersion,
                plateSnapshot.AtlasResolutionBlocks,
                plateSnapshot.AtlasTileSizeBlocks,
                algorithmVersion = StatelessRandomV1.AlgorithmVersion,
                settings.MacroFeatureCount,
                settings.RegionalFeatureCount,
                settings.MaximumFeatureCount,
                settings.MaximumRasterSamples,
                normalizedHeightUnit = "parts-per-million",
            },
            t0301 = new
            {
                fixture = "F05-PLATES",
                convention = "delta-v=vB-vA; dot(delta-v,normal-A-to-B)<0 is collision",
                permutation = "swap A/B and negate normal preserves scalar fields and classification",
                collision = BoundaryMetrics(collision),
                divergence = BoundaryMetrics(divergence),
                shear = BoundaryMetrics(shear),
            },
            t0302 = new
            {
                provisionalReviewPolicy = new
                {
                    minimumCorpusWithCornerRatioFivePercent = "75% (192/256 cases)",
                    maximumSingleStraightRunShare = "20%",
                    maximumAtlasEdgeAlignmentShare = "20%",
                    maximumStorageTileAlignmentShare = "20%",
                    componentAreaSmallMaximumSamples = ComponentAreaSmallMaximumSamples,
                    componentAreaLargeMinimumSamples = ComponentAreaLargeMinimumSamples,
                    tileModuloDiversityMinimum = TileModuloDiversityMinimum,
                    note = "Project policy declared before the final campaign; not a normative T03 threshold.",
                },
                policyResults = new
                {
                    totalCases = allCases.Length,
                    mapsWithCurvedCoasts,
                    componentAreaResiduesModulo32 = componentAreaResidues,
                },
                continent = Metrics(continent, continentTopology, Path.GetFileName(continentPath), continentBitmap),
                region = Metrics(region, regionTopology, Path.GetFileName(regionPath), regionBitmap),
                contours = new
                {
                    file = Path.GetFileName(contourPath),
                    sha256 = L03ATestSupport.Sha256(contourBytes),
                    continentCount = continentTopology.CoastlineSegments.Count,
                    regionCount = regionTopology.CoastlineSegments.Count,
                    coordinates = "exact global model coordinates as reduced rationals",
                },
                coastlineAlignment = new
                {
                    profileId = balanced.Id,
                    tileSizeBlocks = balanced.AtlasTileSizeBlocks,
                    coastSegments,
                    atlasAligned,
                    tileAligned,
                    atlasAlignedPpm = (atlasAligned * 1_000_000L) / coastSegments,
                    tileAlignedPpm = (tileAligned * 1_000_000L) / coastSegments,
                },
                reviewMaps = new[]
                {
                    Map("marine-domains", marinePath, marineBitmap),
                    Map("plates", platesPath, platesBitmap),
                    Map("crust", crustPath, crustBitmap),
                    Map("uplift-subsidence", boundaryFieldPath, boundaryFieldBitmap),
                    Map("continent-atlas-overlay", atlasOverlayPath, atlasOverlayBitmap),
                },
                marineWitness = new
                {
                    seed = marineWitnessSeed,
                    profile = ProfileMetrics(balanced),
                    grid = marineRaster.Grid,
                    rasterChecksum = marineRaster.ContentChecksum.ToString(),
                    marineTopology.OpenOceanSampleCount,
                    marineTopology.InlandBasinSampleCount,
                    marineTopology.OpenOceanComponentCount,
                    inlandBasinComponentAreas = marineTopology.InlandBasinComponentAreas,
                },
                campaign = new
                {
                    calibrationCases,
                    holdoutCases,
                },
            },
        };

        // This is deliberately the final operation: PASS is published atomically only after every assertion
        // and every hashed artifact has completed successfully.
        WriteAtomic(reportPath, JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions));
    }

    private static CorpusMetric CorpusMetrics(
        string corpus,
        int seed,
        FrozenScaleProfile profile,
        ContinentalFieldSettings settings)
    {
        GenerationIdentity identity = L03ATestSupport.Identity(seed, profile);
        ContinentalFieldModel model = L03ATestSupport.Success(
            ContinentalFieldModel.Create(identity, profile, settings));
        Assert.AreEqual(profile.WidthBlocks, model.Bounds.Width);
        Assert.AreEqual(profile.LengthBlocks, model.Bounds.Length);
        ContinentalSamplingGrid grid = L03ATestSupport.ProfileGrid(profile, 96, 96);
        ContinentalRaster raster = L03ATestSupport.Success(model.Rasterize(grid));
        ContinentalTopologyReport topology = ContinentalTopologyAnalyzer.Analyze(raster);
        int tileAligned = topology.CoastlineSegments.Count(segment =>
            IsStorageBoundary(segment, profile.AtlasTileSizeBlocks));
        int tileBoundaryLinesTested = StorageTileBoundaryLinesTested(grid, profile.AtlasTileSizeBlocks);
        return new CorpusMetric(
            corpus,
            seed,
            profile.Id,
            profile.ProfileVersion,
            profile.WidthBlocks,
            profile.LengthBlocks,
            profile.AtlasResolutionBlocks,
            profile.AtlasTileSizeBlocks,
            profile.RequestedSiteCount,
            profile.SiteQuota,
            profile.GeographyConfigHash.ToString(),
            grid,
            raster.ContentChecksum.ToString(),
            topology.LandSampleCount,
            topology.OpenOceanSampleCount,
            topology.InlandBasinSampleCount,
            topology.LandComponentAreas.ToArray(),
            topology.OpenOceanComponentCount,
            topology.InlandBasinComponentAreas.ToArray(),
            topology.CoastlineSegmentCount,
            topology.CoastlineBoundaryCellCount,
            topology.CoastlineCornerCount,
            topology.LongestStraightCoastRun,
            tileAligned,
            tileBoundaryLinesTested);
    }

    private static int StorageTileBoundaryLinesTested(ContinentalSamplingGrid grid, long tileSizeBlocks)
    {
        int result = 0;
        for (int x = 0; x < grid.Width - 1; x++)
        {
            long first = checked(grid.OriginX + ((long)x * grid.StepX));
            result += (first + checked(first + grid.StepX)) % (2L * tileSizeBlocks) == 0 ? 1 : 0;
        }

        for (int z = 0; z < grid.Height - 1; z++)
        {
            long first = checked(grid.OriginZ + ((long)z * grid.StepZ));
            result += (first + checked(first + grid.StepZ)) % (2L * tileSizeBlocks) == 0 ? 1 : 0;
        }

        return result;
    }

    private static bool IsStorageBoundary(ContinentalContourSegment segment, long tileSizeBlocks)
    {
        if (segment.Start.X == segment.End.X)
        {
            return segment.Start.X.Numerator % (segment.Start.X.Denominator * tileSizeBlocks) == 0;
        }

        return segment.Start.Z.Numerator % (segment.Start.Z.Denominator * tileSizeBlocks) == 0;
    }

    private static void AssertBoundaryFinite(PlateBoundaryEffect effect)
    {
        Assert.IsTrue(double.IsFinite(effect.NormalRelativeVelocity));
        Assert.IsTrue(double.IsFinite(effect.TangentialRelativeSpeed));
        foreach (double value in new[]
                 {
                     effect.IntensityNormalized,
                     effect.UpliftNormalized,
                     effect.SubsidenceNormalized,
                     effect.ShearNormalized,
                 })
        {
            Assert.IsTrue(double.IsFinite(value));
            Assert.IsGreaterThanOrEqualTo(0, value);
            Assert.IsLessThanOrEqualTo(1, value);
        }
    }

    private static object ProfileMetrics(FrozenScaleProfile profile) => new
    {
        profile.Id,
        profile.ProfileVersion,
        profile.WidthBlocks,
        profile.LengthBlocks,
        profile.HeightBlocks,
        profile.AtlasResolutionBlocks,
        profile.AtlasTileSizeBlocks,
        profile.RequestedSiteCount,
        profile.SiteQuota,
        profile.AtlasMemoryBudgetBytes,
        profile.NativeRuleSetId,
        profile.NativeRuleSetVersion,
        geographyConfigHash = profile.GeographyConfigHash.ToString(),
    };

    private static object Metrics(
        ContinentalRaster raster,
        ContinentalTopologyReport topology,
        string file,
        byte[] bitmap) => new
        {
            grid = raster.Grid,
            contentChecksum = raster.ContentChecksum.ToString(),
            topology.LandSampleCount,
            topology.OpenOceanSampleCount,
            topology.InlandBasinSampleCount,
            landComponentAreas = topology.LandComponentAreas,
            topology.OpenOceanComponentCount,
            inlandBasinComponentAreas = topology.InlandBasinComponentAreas,
            topology.CoastlineSegmentCount,
            topology.CoastlineBoundaryCellCount,
            topology.CoastlineCornerCount,
            topology.LongestStraightCoastRun,
            file,
            sha256 = L03ATestSupport.Sha256(bitmap),
        };

    private static object BoundaryMetrics(PlateBoundaryEffect effect) => new
    {
        kind = effect.Kind.ToString(),
        effect.NormalRelativeVelocity,
        effect.TangentialRelativeSpeed,
        effect.IntensityNormalized,
        effect.UpliftNormalized,
        effect.SubsidenceNormalized,
        effect.ShearNormalized,
        contentChecksum = effect.ContentChecksum.ToString(),
    };

    private static object Map(string layer, string path, byte[] content) => new
    {
        layer,
        file = Path.GetFileName(path),
        sha256 = L03ATestSupport.Sha256(content),
    };

    private static string RenderContours(params ContinentalTopologyReport[] reports)
    {
        var builder = new StringBuilder("view,index,start-x,start-z,end-x,end-z\n");
        for (int view = 0; view < reports.Length; view++)
        {
            for (int index = 0; index < reports[view].CoastlineSegments.Count; index++)
            {
                ContinentalContourSegment segment = reports[view].CoastlineSegments[index];
                builder.Append(view == 0 ? "continent" : "region").Append(',').Append(index).Append(',')
                    .Append(segment.Start.X).Append(',').Append(segment.Start.Z).Append(',')
                    .Append(segment.End.X).Append(',').Append(segment.End.Z).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed record CorpusMetric(
        string Corpus,
        int Seed,
        string ProfileId,
        uint ProfileVersion,
        long WidthBlocks,
        long LengthBlocks,
        int AtlasResolutionBlocks,
        int AtlasTileSizeBlocks,
        int RequestedSiteCount,
        int SiteQuota,
        string GeographyConfigHash,
        ContinentalSamplingGrid Grid,
        string RasterChecksum,
        int LandSampleCount,
        int OpenOceanSampleCount,
        int InlandBasinSampleCount,
        int[] LandComponentAreas,
        int OpenOceanComponentCount,
        int[] InlandBasinComponentAreas,
        int CoastlineSegmentCount,
        int CoastlineBoundaryCellCount,
        int CoastlineCornerCount,
        int LongestStraightCoastRun,
        int TileAlignedCoastlineSegments,
        int StorageTileBoundaryLinesTested);
}
