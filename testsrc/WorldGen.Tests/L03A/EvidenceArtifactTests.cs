using System.Text.Json;
using System.Text;
using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03A;

[TestClass]
public sealed class EvidenceArtifactTests
{
    [TestMethod]
    [DoNotParallelize]
    public void T03EvidenceRecordsParametersMetricsAndTwoHashedReviewZooms()
    {
        const int seed = 73;
        ContinentalFieldSettings settings = L03ATestSupport.ContinentalSettings();
        ContinentalFieldModel model = L03ATestSupport.Success(ContinentalFieldModel.Create(
            L03ATestSupport.Identity(seed),
            L03ATestSupport.VastBounds(),
            settings));
        ContinentalRaster continent = L03ATestSupport.Success(model.Rasterize(
            new ContinentalSamplingGrid(32, 32, 64, 64, 192, 128)));
        ContinentalRaster region = L03ATestSupport.Success(model.Rasterize(
            new ContinentalSamplingGrid(4_608, 3_072, 16, 16, 192, 128)));
        ContinentalTopologyReport continentTopology = ContinentalTopologyAnalyzer.Analyze(continent);
        ContinentalTopologyReport regionTopology = ContinentalTopologyAnalyzer.Analyze(region);
        const int marineWitnessSeed = -413555959;
        ContinentalFieldModel marineModel = L03ATestSupport.Success(ContinentalFieldModel.Create(
            L03ATestSupport.Identity(marineWitnessSeed),
            L03ATestSupport.VastBounds(),
            settings));
        ContinentalRaster marineRaster = L03ATestSupport.Success(marineModel.Rasterize(
            new ContinentalSamplingGrid(64, 64, 128, 128, 96, 64)));
        ContinentalTopologyReport marineTopology = ContinentalTopologyAnalyzer.Analyze(marineRaster);
        GeneratedSiteSet atlasSites = L03ATestSupport.Success(AtlasSiteGenerator.Generate(
            L03ATestSupport.Identity(seed),
            L03ATestSupport.VastBounds(),
            new AtlasSiteGenerationSettings(64)));
        AtlasMesh atlas = L03ATestSupport.Success(AtlasGeometryBuilder.Build(
            L03ATestSupport.Identity(seed),
            L03ATestSupport.VastBounds(),
            atlasSites.Sites,
            new AtlasGeometryBuildOptions(4, GeometryCacheMode.Precomputed)));
        PlateAtlasSnapshot plateSnapshot = L03ATestSupport.Success(PlateAtlasBuilder.Build(
            L03ATestSupport.Identity(seed),
            atlas,
            new PlateGenerationSettings(7, settings, maximumCells: 1_000, maximumBoundaryEdges: 4_000)));
        (int coastSegments, int atlasAligned, int tileAligned) =
            L03ATestSupport.CoastlineAlignment(atlas, continent, tileSizeSamples: 32);
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
        string output = Path.Combine(L03ATestSupport.FindRepositoryRoot(), ".local", "L03A", "evidence");
        Directory.CreateDirectory(output);
        string continentPath = Path.Combine(output, "T03-02-continent.bmp");
        string regionPath = Path.Combine(output, "T03-02-region.bmp");
        string marinePath = Path.Combine(output, "T03-02-marine-domains.bmp");
        string platesPath = Path.Combine(output, "T03-02-plates.bmp");
        string crustPath = Path.Combine(output, "T03-02-crust.bmp");
        string boundaryFieldPath = Path.Combine(output, "T03-02-uplift-subsidence.bmp");
        string atlasOverlayPath = Path.Combine(output, "T03-02-atlas-overlay.bmp");
        string contourPath = Path.Combine(output, "T03-02-contours.csv");
        File.WriteAllBytes(continentPath, continentBitmap);
        File.WriteAllBytes(regionPath, regionBitmap);
        File.WriteAllBytes(marinePath, marineBitmap);
        File.WriteAllBytes(platesPath, platesBitmap);
        File.WriteAllBytes(crustPath, crustBitmap);
        File.WriteAllBytes(boundaryFieldPath, boundaryFieldBitmap);
        File.WriteAllBytes(atlasOverlayPath, atlasOverlayBitmap);
        byte[] contourBytes = Encoding.UTF8.GetBytes(RenderContours(continentTopology, regionTopology));
        File.WriteAllBytes(contourPath, contourBytes);
        string commit = Environment.GetEnvironmentVariable("ISR_L03A_EVIDENCE_COMMIT") ?? "WORKING_TREE";
        string configuration = Environment.GetEnvironmentVariable("ISR_L03A_EVIDENCE_CONFIGURATION") ?? "UNKNOWN";
        IReadOnlyList<int> calibrationSeeds = L03ATestSupport.SeedCorpus("calibration_seeds");
        IReadOnlyList<int> holdoutSeeds = L03ATestSupport.SeedCorpus("holdout_seeds");
        var balancedCases = calibrationSeeds.Select(item => CorpusMetrics("calibration", item, 128)).Concat(
            holdoutSeeds.Select(item => CorpusMetrics("holdout", item, 128))).ToArray();
        var vastExpeditions = holdoutSeeds.Take(8).Select(item => CorpusMetrics("holdout", item, 64)).ToArray();
        PlateKinematics plateA = new(L03ATestSupport.PlateId(0), new PlateVelocity(1, 0));
        PlateKinematics collisionB = new(L03ATestSupport.PlateId(1), new PlateVelocity(-1, 0));
        PlateKinematics divergenceA = new(L03ATestSupport.PlateId(0), new PlateVelocity(-1, 0));
        PlateKinematics divergenceB = new(L03ATestSupport.PlateId(1), new PlateVelocity(1, 0));
        PlateKinematics shearA = new(L03ATestSupport.PlateId(0), new PlateVelocity(0, 1));
        PlateKinematics shearB = new(L03ATestSupport.PlateId(1), new PlateVelocity(0, -1));
        UnitDirection2 normal = UnitDirection2.FromComponents(1, 0);
        PlateBoundaryEffect collision = PlateBoundaryEvaluator.Evaluate(plateA, collisionB, normal);
        PlateBoundaryEffect divergence = PlateBoundaryEvaluator.Evaluate(divergenceA, divergenceB, normal);
        PlateBoundaryEffect shear = PlateBoundaryEvaluator.Evaluate(shearA, shearB, normal);
        string fixturePath = Path.Combine(L03ATestSupport.FindRepositoryRoot(), "registry", "fixtures.json");

        File.WriteAllText(Path.Combine(output, "T03-01-02.json"), JsonSerializer.Serialize(new
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
                profiles = new[] { "balanced-96x64-step128", "vast-expedition-192x128-step64" },
            },
            model = new
            {
                contentChecksum = model.ContentChecksum.ToString(),
                plateSnapshotChecksum = plateSnapshot.ContentChecksum.ToString(),
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
                    minimumCorpusWithCornerRatioFivePercent = "75%",
                    maximumSingleStraightRunShare = "20%",
                    maximumAtlasEdgeAlignmentShare = "20%",
                    maximumStorageTileAlignmentShare = "20%",
                    componentAreaSmallMaximumSamples = 299,
                    componentAreaLargeMinimumSamples = 751,
                    tileModuloDiversityMinimum = 8,
                    note = "Project policy declared before the final campaign; not a normative T03 threshold.",
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
                    rasterChecksum = marineRaster.ContentChecksum.ToString(),
                    marineTopology.OpenOceanSampleCount,
                    marineTopology.InlandBasinSampleCount,
                    marineTopology.OpenOceanComponentCount,
                    inlandBasinComponentAreas = marineTopology.InlandBasinComponentAreas,
                },
                balancedCases,
                vastExpeditions,
            },
        }, new JsonSerializerOptions { WriteIndented = true }));

        Assert.IsTrue(File.Exists(continentPath));
        Assert.IsTrue(File.Exists(regionPath));
        Assert.IsTrue(File.Exists(contourPath));
        Assert.IsTrue(File.Exists(marinePath));
        Assert.IsTrue(File.Exists(platesPath));
        Assert.IsTrue(File.Exists(crustPath));
        Assert.IsTrue(File.Exists(boundaryFieldPath));
        Assert.IsTrue(File.Exists(atlasOverlayPath));
        CollectionAssert.AreEqual(new byte[] { (byte)'B', (byte)'M' }, continentBitmap[..2]);
        CollectionAssert.AreNotEqual(continentBitmap, regionBitmap);
        Assert.IsGreaterThan(0, marineTopology.InlandBasinSampleCount);
        Assert.IsLessThanOrEqualTo(coastSegments, atlasAligned * 5,
            "More than 20% of coastline transitions align with atlas/Voronoi ownership edges.");
        Assert.IsLessThanOrEqualTo(coastSegments, tileAligned * 5,
            "More than 20% of coastline transitions align with 32-sample storage boundaries.");
        Assert.HasCount(continentTopology.CoastlineSegmentCount, continentTopology.CoastlineSegments);
        Assert.HasCount(regionTopology.CoastlineSegmentCount, regionTopology.CoastlineSegments);

        static object Metrics(
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

        object CorpusMetrics(string corpus, int caseSeed, long step)
        {
            ContinentalFieldModel caseModel = L03ATestSupport.Success(ContinentalFieldModel.Create(
                L03ATestSupport.Identity(caseSeed),
                L03ATestSupport.VastBounds(),
                settings));
            int width = checked((int)(L03ATestSupport.VastBounds().Width / step));
            int height = checked((int)(L03ATestSupport.VastBounds().Length / step));
            ContinentalRaster caseRaster = L03ATestSupport.Success(caseModel.Rasterize(
                new ContinentalSamplingGrid(step / 2, step / 2, step, step, width, height)));
            ContinentalTopologyReport caseTopology = ContinentalTopologyAnalyzer.Analyze(caseRaster);
            return new
            {
                corpus,
                seed = caseSeed,
                profile = step == 128 ? "balanced-96x64-step128" : "vast-expedition-192x128-step64",
                rasterChecksum = caseRaster.ContentChecksum.ToString(),
                caseTopology.LandSampleCount,
                caseTopology.OpenOceanSampleCount,
                caseTopology.InlandBasinSampleCount,
                landComponentAreas = caseTopology.LandComponentAreas,
                caseTopology.OpenOceanComponentCount,
                inlandBasinComponentAreas = caseTopology.InlandBasinComponentAreas,
                caseTopology.CoastlineSegmentCount,
                caseTopology.CoastlineBoundaryCellCount,
                caseTopology.CoastlineCornerCount,
                caseTopology.LongestStraightCoastRun,
            };
        }

        static object BoundaryMetrics(PlateBoundaryEffect effect) => new
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

        static object Map(string layer, string path, byte[] content) => new
        {
            layer,
            file = Path.GetFileName(path),
            sha256 = L03ATestSupport.Sha256(content),
        };

        static string RenderContours(params ContinentalTopologyReport[] reports)
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
    }
}
