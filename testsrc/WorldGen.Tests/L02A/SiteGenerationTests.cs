using ISRWorldGen.Core.Atlas.Geometry;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L02A;

[TestClass]
public sealed class SiteGenerationTests
{
    private static readonly int[] QuickSeeds =
    [
        0, 1, -1, int.MinValue, int.MaxValue, 42, 73, 20260906,
        -437287116, -1587986303, 1571057779, -1837489763,
        720496888, 1481968978, -307755313, 60159686,
    ];

    [TestMethod]
    public void IrregularSites_AreUniqueBoundedAndStableForFrozenSeedCorpus()
    {
        WorldBounds bounds = new(-128, -96, 129, 97);
        var settings = new AtlasSiteGenerationSettings(64);
        foreach (int seed in QuickSeeds)
        {
            GeneratedSiteSet first = Success(AtlasSiteGenerator.Generate(GeometryTestSupport.Identity(seed), bounds, settings));
            GeneratedSiteSet second = Success(AtlasSiteGenerator.Generate(GeometryTestSupport.Identity(seed), bounds, settings));

            Assert.HasCount(64, first.Sites);
            CollectionAssert.AreEqual(first.Sites.ToArray(), second.Sites.ToArray());
            Assert.AreEqual(64, first.Sites.Select(site => (site.X, site.Z)).Distinct().Count());
            foreach (AtlasSite site in first.Sites)
            {
                Assert.IsTrue(bounds.Contains(site.X, site.Z));
            }

            CollectionAssert.AreEqual(
                first.Sites.OrderBy(site => site.Id, GeometryTestSupport.StableIdComparer.Instance).ToArray(),
                first.Sites.ToArray());
        }
    }

    [TestMethod]
    public void CapacityAndInvalidCounts_FailBeforePublishingSites()
    {
        WorldBounds bounds = new(0, 0, 2, 2);
        GenerationResult<GeneratedSiteSet> overCapacity = AtlasSiteGenerator.Generate(
            GeometryTestSupport.Identity(),
            bounds,
            new AtlasSiteGenerationSettings(5));
        Assert.IsInstanceOfType<GenerationFailure<GeneratedSiteSet>>(overCapacity);
        Assert.AreEqual(
            GenerationFailureCode.BudgetExceeded,
            ((GenerationFailure<GeneratedSiteSet>)overCapacity).Error.Code);

        GenerationResult<GeneratedSiteSet> empty = AtlasSiteGenerator.Generate(
            GeometryTestSupport.Identity(),
            bounds,
            new AtlasSiteGenerationSettings(0));
        Assert.IsInstanceOfType<GenerationFailure<GeneratedSiteSet>>(empty);
        Assert.AreEqual(
            GenerationFailureCode.InvalidInput,
            ((GenerationFailure<GeneratedSiteSet>)empty).Error.Code);
    }

    [TestMethod]
    public void GeneratedSites_ProduceValidAtlasAcrossWorkersAndCaches()
    {
        WorldBounds bounds = new(0, 0, 512, 384);
        GeneratedSiteSet generated = Success(AtlasSiteGenerator.Generate(
            GeometryTestSupport.Identity(-437287116),
            bounds,
            new AtlasSiteGenerationSettings(48)));

        AtlasMesh cold = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(-437287116),
            bounds,
            generated.Sites,
            new AtlasGeometryBuildOptions(1, GeometryCacheMode.Cold)));
        AtlasMesh hot = GeometryTestSupport.Success(AtlasGeometryBuilder.Build(
            GeometryTestSupport.Identity(-437287116),
            bounds,
            generated.Sites.Reverse(),
            new AtlasGeometryBuildOptions(16, GeometryCacheMode.Precomputed)));

        GeometryTestSupport.AssertValidTopology(cold);
        GeometryTestSupport.AssertValidTopology(hot);
        Assert.AreEqual(GeometryTestSupport.Fingerprint(cold), GeometryTestSupport.Fingerprint(hot));
    }

    private static GeneratedSiteSet Success(GenerationResult<GeneratedSiteSet> result)
    {
        Assert.IsInstanceOfType<GenerationSuccess<GeneratedSiteSet>>(result);
        return ((GenerationSuccess<GeneratedSiteSet>)result).Snapshot;
    }
}
