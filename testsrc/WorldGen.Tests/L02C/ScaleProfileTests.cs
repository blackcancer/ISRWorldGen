using System.Collections;
using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Atlas.SpatialIndex;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Tests.L02C;

[TestClass]
public sealed class ScaleProfileTests
{
    [TestMethod]
    public void Catalog_OffersThreeVersionedProposalsWithoutChoosingForThePlayer()
    {
        Assert.HasCount(3, ScaleProfileCatalog.Proposals);
        CollectionAssert.AreEqual(
            new[] { "laboratory", "balanced", "vast-expeditions" },
            ScaleProfileCatalog.Proposals.Select(profile => profile.Id).ToArray());
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.ProfileVersion == 1));
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.WidthBlocks > 0));
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.LengthBlocks > 0));
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.HeightBlocks > 0));
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.AtlasResolutionBlocks > 0));
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.RequestedSiteCount > 0));
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.SiteQuota >= profile.RequestedSiteCount));
        Assert.IsTrue(ScaleProfileCatalog.Proposals.All(profile => profile.AtlasMemoryBudgetBytes > 0));
        Assert.IsNull(typeof(ScaleProfileCatalog).GetProperty("Default"));
        Assert.IsNull(typeof(ScaleProfileCatalog).GetProperty("Selected"));
    }

    [TestMethod]
    public void ProposedProfiles_ValidateAgainstExplicitNativeConstraintsAndBridgeToL02B()
    {
        NativeWorldConstraints constraints = ProfileTestSupport.QualifiedNativeConstraints();

        foreach (ScaleProfileDefinition proposal in ScaleProfileCatalog.Proposals)
        {
            FrozenScaleProfile frozen = ProfileTestSupport.Success(
                ScaleProfileValidator.ValidateAndFreeze(proposal, constraints));

            Assert.AreEqual(proposal.Id, frozen.Id);
            Assert.AreEqual(proposal.HeightBlocks, frozen.VerticalEnvelope.WorldHeight);
            Assert.AreEqual(proposal.SeaLevelBlocks, frozen.VerticalEnvelope.SeaLevel);
            Assert.AreEqual(proposal.WidthBlocks, frozen.AtlasIndexProfile.Width);
            Assert.AreEqual(proposal.LengthBlocks, frozen.AtlasIndexProfile.Length);
            Assert.AreEqual(proposal.AtlasTileSizeBlocks, frozen.AtlasIndexProfile.TileSize);
            Assert.AreEqual(proposal.RequestedSiteCount, frozen.AtlasIndexProfile.RequestedSiteCount);
            Assert.AreEqual(proposal.SiteQuota, frozen.AtlasIndexProfile.SiteQuota);
            Assert.AreEqual(proposal.AtlasMemoryBudgetBytes, frozen.AtlasIndexProfile.MemoryBudgetBytes);
        }
    }

    [TestMethod]
    public void ProposedProfiles_FitTheExistingL02BConservativeColdPlan()
    {
        NativeWorldConstraints constraints = ProfileTestSupport.QualifiedNativeConstraints();

        foreach (ScaleProfileDefinition proposal in ScaleProfileCatalog.Proposals)
        {
            FrozenScaleProfile frozen = ProfileTestSupport.Success(
                ScaleProfileValidator.ValidateAndFreeze(proposal, constraints));
            AtlasMemoryEstimate estimate = ProfileTestSupport.Success(AtlasSpatialIndexPlanner.Estimate(
                ProfileTestSupport.Identity(),
                frozen.AtlasIndexProfile,
                Array.Empty<SpatialPrimitiveDefinition>(),
                new SpatialIndexBuildOptions(1, SpatialIndexCacheMode.Cold)));

            Assert.IsLessThanOrEqualTo(
                frozen.AtlasMemoryBudgetBytes,
                estimate.EstimatedPeakBuildBytes,
                $"Proposal {frozen.Id} must fit the conservative L02-B cold plan before it is offered.");
            Assert.IsLessThanOrEqualTo(
                frozen.AtlasMemoryBudgetBytes - (frozen.AtlasMemoryBudgetBytes / 4),
                estimate.EstimatedPeakBuildBytes,
                $"Proposal {frozen.Id} must retain at least 25% explicit budget headroom.");
            double nominalResolution = Math.Sqrt(
                ((double)frozen.WidthBlocks * frozen.LengthBlocks) / frozen.RequestedSiteCount);
            Assert.IsLessThanOrEqualTo(
                nominalResolution * 0.01,
                Math.Abs(frozen.AtlasResolutionBlocks - nominalResolution),
                $"Proposal {frozen.Id} must expose a resolution consistent with its explicit site count.");
            Console.WriteLine(
                $"L02C_PROFILE_PLAN id={frozen.Id} sites={frozen.RequestedSiteCount} " +
                $"resolution={frozen.AtlasResolutionBlocks} budget={frozen.AtlasMemoryBudgetBytes} " +
                $"estimatedPeak={estimate.EstimatedPeakBuildBytes}");
        }
    }

    [TestMethod]
    public void UnsupportedNativeHeight_IsTypedFailureWithoutClipping()
    {
        ScaleProfileDefinition proposal = ProfileTestSupport.Balanced() with { HeightBlocks = 320 };

        GenerationFailure<FrozenScaleProfile> failure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(proposal, ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.profile.native-height", failure.Error.Stage);
        StringAssert.Contains(failure.Error.Details, "320");
        Assert.AreEqual(320, proposal.HeightBlocks);
    }

    [TestMethod]
    public void ImpossibleSeaLevelAndMargins_AreTypedFailureWithoutClipping()
    {
        ScaleProfileDefinition proposal = ProfileTestSupport.Balanced() with
        {
            SeaLevelBlocks = 370,
            HeightMarginBlocks = 24,
        };

        GenerationFailure<FrozenScaleProfile> failure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(proposal, ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.profile.vertical-envelope", failure.Error.Stage);
        Assert.AreEqual(370L, proposal.SeaLevelBlocks);
    }

    [TestMethod]
    public void InvalidQuotaAndBudget_AreRejectedBeforePublication()
    {
        ScaleProfileDefinition overQuota = ProfileTestSupport.Balanced() with
        {
            RequestedSiteCount = 8_193,
            SiteQuota = 8_192,
        };
        ScaleProfileDefinition noBudget = ProfileTestSupport.Balanced() with { AtlasMemoryBudgetBytes = 0 };

        GenerationFailure<FrozenScaleProfile> quotaFailure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(overQuota, ProfileTestSupport.QualifiedNativeConstraints()));
        GenerationFailure<FrozenScaleProfile> budgetFailure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(noBudget, ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, quotaFailure.Error.Code);
        Assert.AreEqual("atlas.profile.site-quota", quotaFailure.Error.Stage);
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, budgetFailure.Error.Code);
        Assert.AreEqual("atlas.profile.memory-budget", budgetFailure.Error.Stage);
    }

    [TestMethod]
    public void SiteCountsBeyondRuntimeArrayCapacity_AreRejectedByProfileValidation()
    {
        ScaleProfileDefinition proposal = ProfileTestSupport.Balanced() with
        {
            RequestedSiteCount = Array.MaxLength,
            SiteQuota = Array.MaxLength,
            AtlasMemoryBudgetBytes = long.MaxValue,
        };

        GenerationFailure<FrozenScaleProfile> failure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(proposal, ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, failure.Error.Code);
        Assert.AreEqual("atlas.profile.site-quota", failure.Error.Stage);
    }

    [TestMethod]
    public void MalformedUnicodeProfileId_IsTypedFailure()
    {
        ScaleProfileDefinition proposal = ProfileTestSupport.Balanced() with { Id = "bad\ud800" };

        GenerationFailure<FrozenScaleProfile> failure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(proposal, ProfileTestSupport.QualifiedNativeConstraints()));

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.profile.id", failure.Error.Stage);
    }

    [TestMethod]
    [DoNotParallelize]
    public void ImpossibleWorldArea_FailsTypedBeforeLargeConstruction()
    {
        ScaleProfileDefinition proposal = ProfileTestSupport.Balanced() with
        {
            WidthBlocks = long.MaxValue,
            LengthBlocks = 2,
            AtlasResolutionBlocks = 1,
            AtlasTileSizeBlocks = 1,
            RequestedSiteCount = 1,
            SiteQuota = 1,
        };
        var constraints = new NativeWorldConstraints(
            "test-native-v1",
            ruleSetVersion: 1,
            supportedHeights: new[] { 384 },
            minimumHorizontalBlocks: 1,
            maximumHorizontalBlocks: long.MaxValue,
            horizontalStepBlocks: 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        GenerationFailure<FrozenScaleProfile> failure = ProfileTestSupport.Failure(
            ScaleProfileValidator.ValidateAndFreeze(proposal, constraints));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(GenerationFailureCode.InvalidInput, failure.Error.Code);
        Assert.AreEqual("atlas.profile.dimensions", failure.Error.Stage);
        Assert.IsLessThan(64 * 1024L, allocated);
    }

    [TestMethod]
    public void NativeConstraints_AreCanonicalImmutableAndRejectImpossibleCollectionsBeforeEnumeration()
    {
        int[] callerHeights = [512, 256, 384];
        NativeWorldConstraints constraints = new(
            "test-native-v1",
            ruleSetVersion: 1,
            callerHeights,
            minimumHorizontalBlocks: 4_096,
            maximumHorizontalBlocks: 1_024_000,
            horizontalStepBlocks: 512);
        callerHeights[0] = 1;

        CollectionAssert.AreEqual(new[] { 256, 384, 512 }, constraints.SupportedHeights.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<int>)constraints.SupportedHeights)[0] = 1);

        var impossible = new ImpossibleHeightList(int.MaxValue);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new NativeWorldConstraints(
            "test-native-v1",
            ruleSetVersion: 1,
            impossible,
            minimumHorizontalBlocks: 1,
            maximumHorizontalBlocks: 2,
            horizontalStepBlocks: 1));
        Assert.AreEqual(0, impossible.EnumerationCount);
    }

    [TestMethod]
    public void FrozenProfile_PublicSurfaceIsImmutableAndExcludesRuntimeMetadata()
    {
        FrozenScaleProfile frozen = ProfileTestSupport.FreezeBalanced();
        string[] forbiddenTokens = ["Worker", "Clock", "Time", "Path", "Account"];

        Assert.IsTrue(typeof(FrozenScaleProfile).GetProperties().All(property => property.SetMethod is null));
        Assert.IsFalse(typeof(FrozenScaleProfile).GetProperties().Any(property =>
            forbiddenTokens.Any(token => property.Name.Contains(token, StringComparison.OrdinalIgnoreCase))));
    }

    private sealed class ImpossibleHeightList : IReadOnlyList<int>
    {
        internal ImpossibleHeightList(int count) => Count = count;

        public int Count { get; }

        public int this[int index] => throw new NotSupportedException();

        public int EnumerationCount { get; private set; }

        public IEnumerator<int> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("An impossible collection must be rejected before enumeration.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
