using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Geology.Plates;

namespace ISRWorldGen.Tests.L03A;

[TestClass]
public sealed class ContinentalFieldTests
{
    [TestMethod]
    public void FrozenCorpusProducesDeterministicBoundedMultiscaleTopology()
    {
        WorldBounds bounds = L03ATestSupport.VastBounds();
        ContinentalFieldSettings settings = L03ATestSupport.ContinentalSettings();
        var allComponentAreas = new List<int>();
        var scaleClasses = new HashSet<ContinentalFeatureScale>();
        int mapsWithCurvedCoasts = 0;

        IReadOnlyList<int> calibrationSeeds = L03ATestSupport.SeedCorpus("calibration_seeds");
        IReadOnlyList<int> holdoutSeeds = L03ATestSupport.SeedCorpus("holdout_seeds");
        Assert.HasCount(192, calibrationSeeds);
        Assert.HasCount(64, holdoutSeeds);
        foreach (int seed in calibrationSeeds.Concat(holdoutSeeds))
        {
            ContinentalFieldModel first = L03ATestSupport.Success(
                ContinentalFieldModel.Create(L03ATestSupport.Identity(seed), bounds, settings));
            ContinentalFieldModel second = L03ATestSupport.Success(
                ContinentalFieldModel.Create(L03ATestSupport.Identity(seed), bounds, settings));
            ContinentalSamplingGrid grid = new(64, 64, 128, 128, 96, 64);
            ContinentalRaster raster = L03ATestSupport.Success(first.Rasterize(grid));
            ContinentalRaster repeated = L03ATestSupport.Success(second.Rasterize(grid));
            ContinentalTopologyReport report = ContinentalTopologyAnalyzer.Analyze(raster);

            Assert.AreEqual(first.ContentChecksum, second.ContentChecksum, $"model seed {seed}");
            Assert.AreEqual(raster.ContentChecksum, repeated.ContentChecksum, $"raster seed {seed}");
            Assert.IsGreaterThan(0, report.LandSampleCount, $"land seed {seed}");
            Assert.IsGreaterThan(0, report.OpenOceanSampleCount, $"ocean seed {seed}");
            Assert.AreEqual(
                raster.Width * raster.Height,
                report.LandSampleCount + report.OpenOceanSampleCount + report.InlandBasinSampleCount,
                $"surface accounting seed {seed}");
            Assert.IsGreaterThan(0, report.OpenOceanComponentCount, $"edge-connected ocean seed {seed}");
            Assert.IsGreaterThan(0, report.CoastlineSegmentCount, $"coast seed {seed}");
            Assert.HasCount(report.CoastlineSegmentCount, report.CoastlineSegments);
            Assert.HasCount(raster.HeightPpm.Count, report.SurfaceDomains);
            Assert.AreEqual(report.LandSampleCount,
                report.SurfaceDomains.Count(domain => domain == ContinentalSurfaceDomain.Land));
            Assert.AreEqual(report.OpenOceanSampleCount,
                report.SurfaceDomains.Count(domain => domain == ContinentalSurfaceDomain.OpenOcean));
            Assert.AreEqual(report.InlandBasinSampleCount,
                report.SurfaceDomains.Count(domain => domain == ContinentalSurfaceDomain.InlandBasin));
            Assert.IsLessThanOrEqualTo(report.CoastlineSegmentCount, report.LongestStraightCoastRun * 5,
                $"A single straight/polygonal run dominates seed {seed}.");
            if (report.CoastlineCornerCount * 20 >= report.CoastlineBoundaryCellCount)
            {
                mapsWithCurvedCoasts++;
            }

            allComponentAreas.AddRange(report.LandComponentAreas);
            foreach (ContinentalFeatureDescriptor feature in first.Features)
            {
                scaleClasses.Add(feature.Scale);
            }
        }

        CollectionAssert.AreEquivalent(
            new[] { ContinentalFeatureScale.Macro, ContinentalFeatureScale.Regional },
            scaleClasses.ToArray());
        Assert.IsGreaterThanOrEqualTo(12, mapsWithCurvedCoasts,
            "At least 75% of the frozen corpus must show frequent coastline direction changes.");
        Assert.IsTrue(allComponentAreas.Any(area => area < 300), "Regional landforms are absent.");
        Assert.IsTrue(allComponentAreas.Any(area => area > 750), "Continental landforms are absent.");
        Assert.IsGreaterThanOrEqualTo(8, allComponentAreas.Select(area => area % 32).Distinct().Count(),
            "Component areas appear locked to a 32-sample storage tile.");

        foreach (int seed in holdoutSeeds.Take(8))
        {
            ContinentalFieldModel model = L03ATestSupport.Success(ContinentalFieldModel.Create(
                L03ATestSupport.Identity(seed), bounds, settings));
            ContinentalRaster expedition = L03ATestSupport.Success(model.Rasterize(
                new ContinentalSamplingGrid(32, 32, 64, 64, 192, 128)));
            ContinentalTopologyReport report = ContinentalTopologyAnalyzer.Analyze(expedition);
            Assert.AreEqual(expedition.Width * expedition.Height,
                report.LandSampleCount + report.OpenOceanSampleCount + report.InlandBasinSampleCount);
            Assert.IsGreaterThan(0, report.CoastlineSegmentCount);
        }
    }

    [TestMethod]
    public void GlobalSamplingIsInvariantWhenTheSameCoordinatesAreSplitIntoStorageWindows()
    {
        ContinentalFieldModel model = L03ATestSupport.Success(ContinentalFieldModel.Create(
            L03ATestSupport.Identity(-437287116),
            L03ATestSupport.VastBounds(),
            L03ATestSupport.ContinentalSettings()));
        ContinentalRaster full = L03ATestSupport.Success(model.Rasterize(
            new ContinentalSamplingGrid(32, 32, 64, 64, 192, 128)));
        ContinentalRaster left = L03ATestSupport.Success(model.Rasterize(
            new ContinentalSamplingGrid(32, 32, 64, 64, 71, 128)));
        ContinentalRaster right = L03ATestSupport.Success(model.Rasterize(
            new ContinentalSamplingGrid(4_576, 32, 64, 64, 121, 128)));

        for (int z = 0; z < full.Height; z++)
        {
            for (int x = 0; x < full.Width; x++)
            {
                int expected = full.HeightPpm[(z * full.Width) + x];
                int actual = x < left.Width
                    ? left.HeightPpm[(z * left.Width) + x]
                    : right.HeightPpm[(z * right.Width) + (x - left.Width)];
                Assert.AreEqual(expected, actual, $"global sample ({x},{z})");
            }
        }
    }

    [TestMethod]
    public void RasterBudgetAndOutOfWorldWindowsFailBeforeAllocation()
    {
        ContinentalFieldModel model = L03ATestSupport.Success(ContinentalFieldModel.Create(
            L03ATestSupport.Identity(),
            L03ATestSupport.VastBounds(),
            new ContinentalFieldSettings(5, 18, 64, maximumRasterSamples: 10_000)));

        var tooLarge = model.Rasterize(new ContinentalSamplingGrid(0, 0, 1, 1, 101, 100));
        var outside = model.Rasterize(new ContinentalSamplingGrid(0, 0, 64, 64, 193, 128));

        Assert.IsFalse(tooLarge.IsSuccess);
        Assert.IsFalse(outside.IsSuccess);
    }
}
