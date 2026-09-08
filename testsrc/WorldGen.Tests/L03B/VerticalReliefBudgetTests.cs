using ISRWorldGen.Core.Atlas.Profiles;
using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Geology.Landscapes;

namespace ISRWorldGen.Tests.L03B;

[TestClass]
public sealed class VerticalReliefBudgetTests
{
    [TestMethod]
    public void EveryFrozenScaleProfileReservesFloorOceanCavernsReliefAndUpperMargin()
    {
        foreach (string profileId in new[] { "laboratory", "balanced", "vast-expeditions" })
        {
            FrozenScaleProfile profile = L03BTestSupport.FrozenProfile(profileId);
            GenerationIdentity identity = L03BTestSupport.Identity(73, profile);
            ReliefVerticalPlan plan = L03BTestSupport.Success(ReliefVerticalBudgetPlanner.Create(
                identity,
                profile,
                new ReliefBudgetRequest(28, 40, 96)));

            Assert.AreEqual(profile.VerticalEnvelope.MinimumFloorThickness, plan.RockFloorTopBlocks);
            Assert.AreEqual(profile.VerticalEnvelope.WorldHeight, plan.WorldHeightBlocks);
            Assert.AreEqual(profile.VerticalEnvelope.SeaLevel, plan.SeaLevelBlocks);
            Assert.AreEqual(profile.VerticalEnvelope.MinimumFloorThickness, plan.RockFloorThicknessBlocks);
            Assert.AreEqual(profile.VerticalEnvelope.MinimumCavernCover, plan.MinimumCavernCoverBlocks);
            Assert.AreEqual(profile.VerticalEnvelope.HeightMargin, plan.UpperMarginBlocks);
            Assert.AreEqual(profile.VerticalEnvelope.SeaLevel - 28, plan.DeepestOceanFloorBlocks);
            Assert.AreEqual(
                plan.DeepestOceanFloorBlocks - profile.VerticalEnvelope.MinimumCavernCover,
                plan.MaximumCavernCeilingBlocks);
            Assert.IsGreaterThanOrEqualTo(
                40,
                plan.MaximumCavernCeilingBlocks - plan.RockFloorTopBlocks);
            Assert.AreEqual(profile.VerticalEnvelope.SeaLevel + 96, plan.HighestReliefBlocks);
            Assert.IsLessThan(profile.VerticalEnvelope.MaximumExclusiveUsableLevel, plan.HighestReliefBlocks);
            Assert.AreEqual(28, plan.Transform.SeaLevelBlocks - plan.Transform.MapModelAltitudeToBlocks(-1));
            Assert.AreEqual(96, plan.Transform.MapModelAltitudeToBlocks(1) - plan.Transform.SeaLevelBlocks);
        }
    }

    [TestMethod]
    public void SharedAltitudeTransformIsStrictlyMonotoneAndRefusesOutOfDomainValues()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("balanced");
        ReliefVerticalPlan plan = L03BTestSupport.Success(ReliefVerticalBudgetPlanner.Create(
            L03BTestSupport.Identity(42, profile),
            profile,
            new ReliefBudgetRequest(64, 48, 128)));

        double previous = plan.Transform.MapModelAltitudeToBlocks(-1);
        Assert.AreEqual(profile.VerticalEnvelope.SeaLevel, plan.Transform.MapModelAltitudeToBlocks(0));
        long previousQuantized = plan.Transform.MapModelAltitudeToQuantizedBlocks(-1);
        for (int index = 1; index <= 2_000; index++)
        {
            double modelAltitude = -1 + (index / 1_000d);
            double current = plan.Transform.MapModelAltitudeToBlocks(modelAltitude);
            long currentQuantized = plan.Transform.MapModelAltitudeToQuantizedBlocks(modelAltitude);
            Assert.IsGreaterThan(previous, current, $"sample {index}");
            Assert.IsGreaterThanOrEqualTo(previousQuantized, currentQuantized, $"quantized sample {index}");
            previous = current;
            previousQuantized = currentQuantized;
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            plan.Transform.MapModelAltitudeToBlocks(-1.000_001));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            plan.Transform.MapModelAltitudeToBlocks(1.000_001));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            plan.Transform.MapModelAltitudeToBlocks(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            plan.Transform.MapModelAltitudeToBlocks(double.PositiveInfinity));
    }

    [TestMethod]
    public void ImpossiblePresetFailsTypedBeforeAnyTransformIsPublished()
    {
        FrozenScaleProfile profile = L03BTestSupport.FrozenProfile("laboratory");
        GenerationIdentity identity = L03BTestSupport.Identity(73, profile);

        GenerationResult<ReliefVerticalPlan> oceanFailure = ReliefVerticalBudgetPlanner.Create(
            identity,
            profile,
            new ReliefBudgetRequest(70, 40, 96));
        GenerationResult<ReliefVerticalPlan> reliefFailure = ReliefVerticalBudgetPlanner.Create(
            identity,
            profile,
            new ReliefBudgetRequest(28, 40, 130));
        GenerationResult<ReliefVerticalPlan> overflowFailure = ReliefVerticalBudgetPlanner.Create(
            identity,
            profile,
            new ReliefBudgetRequest(28, 40, long.MaxValue));

        AssertFailure(oceanFailure, "geology.landscapes.vertical-below-sea");
        AssertFailure(reliefFailure, "geology.landscapes.vertical-above-sea");
        AssertFailure(overflowFailure, "geology.landscapes.vertical-above-sea");
    }

    private static void AssertFailure(GenerationResult<ReliefVerticalPlan> result, string stage)
    {
        Assert.IsInstanceOfType<GenerationFailure<ReliefVerticalPlan>>(result);
        GenerationError error = ((GenerationFailure<ReliefVerticalPlan>)result).Error;
        Assert.AreEqual(GenerationFailureCode.BudgetExceeded, error.Code);
        Assert.AreEqual(stage, error.Stage);
        Assert.IsFalse(error.CanRetry);
    }
}
