using ISRWorldGen.Core.Contracts;
using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Tests.L01B;

[TestClass]
public sealed class CompletedContractTests
{
    [TestMethod]
    public void ScaleModel_PerformsExplicitReversibleUnitConversions()
    {
        var scale = new ScaleModel(
            geologicalUnitName: "kilometre-model",
            blocksPerGeologicalUnit: 16_000,
            altitudeUnitName: "altitude-model",
            blocksPerAltitudeUnit: 2.5);

        Assert.AreEqual(32_000, scale.GeologicalUnitsToBlocks(2));
        Assert.AreEqual(2, scale.BlocksToGeologicalUnits(32_000));
        Assert.AreEqual(10, scale.AltitudeUnitsToBlocks(4));
        Assert.AreEqual(4, scale.BlocksToAltitudeUnits(10));
        Assert.AreEqual(32_000L, scale.GeologicalUnitsToWholeBlocksChecked(2));
    }

    [TestMethod]
    public void ScaleModel_RejectsInvalidFactorsInputsAndOverflow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewScale(0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewScale(-1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewScale(double.NaN, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewScale(double.PositiveInfinity, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewScale(1, 0));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new ScaleModel("kilometre-model\u0301", 1, "altitude-model", 1));

        ScaleModel scale = NewScale(2, 4);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scale.GeologicalUnitsToBlocks(double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => scale.BlocksToAltitudeUnits(double.NegativeInfinity));
        Assert.ThrowsExactly<OverflowException>(() => scale.GeologicalUnitsToBlocks(double.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() => scale.GeologicalUnitsToWholeBlocksChecked(double.MaxValue));
    }

    [TestMethod]
    public void VerticalEnvelope_EnforcesFloorCoverSeaAndMarginInvariants()
    {
        var envelope = new VerticalEnvelope(
            worldHeight: 384,
            seaLevel: 110,
            minimumFloorThickness: 5,
            minimumCavernCover: 3,
            heightMargin: 8);

        Assert.AreEqual(5L, envelope.MinimumUsableLevel);
        Assert.AreEqual(376L, envelope.MaximumExclusiveUsableLevel);
        envelope.EnsureCavernCovered(surfaceLevel: 100, cavernCeilingLevel: 97);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            envelope.EnsureCavernCovered(surfaceLevel: 100, cavernCeilingLevel: 98));
    }

    [TestMethod]
    public void VerticalEnvelope_RejectsImpossibleOrOverflowingLayouts()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewEnvelope(0, 0, 1, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewEnvelope(384, -1, 1, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewEnvelope(384, 4, 5, 1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewEnvelope(384, 376, 5, 1, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewEnvelope(384, 110, 0, 1, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewEnvelope(384, 110, 5, 0, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NewEnvelope(384, 110, 5, 1, -1));
        Assert.ThrowsExactly<OverflowException>(() => NewEnvelope(long.MaxValue, 1, 1, 1, long.MaxValue));
    }

    [TestMethod]
    public void GenerationResult_SuccessContainsOnlyTheValidatedSnapshot()
    {
        HeightSnapshot snapshot = SnapshotTestData.CreateSnapshot(SnapshotTestData.Inputs);
        GenerationResult<HeightSnapshot> result = GenerationResult<HeightSnapshot>.Success(snapshot);

        Assert.IsTrue(result.IsSuccess);
        GenerationSuccess<HeightSnapshot> success = Assert.IsInstanceOfType<GenerationSuccess<HeightSnapshot>>(result);
        Assert.AreSame(snapshot, success.Snapshot);
        Assert.ThrowsExactly<ArgumentNullException>(() => GenerationResult<HeightSnapshot>.Success(null!));
    }

    [TestMethod]
    public void GenerationResult_FailureCodesAreCompleteAndExposeNoPartialSnapshot()
    {
        GenerationFailureCode[] expectedCodes =
        [
            GenerationFailureCode.InvalidInput,
            GenerationFailureCode.UnsupportedVersion,
            GenerationFailureCode.BudgetExceeded,
            GenerationFailureCode.GeometryFailure,
            GenerationFailureCode.NonConvergent,
            GenerationFailureCode.CorruptData,
            GenerationFailureCode.Cancelled,
        ];
        CollectionAssert.AreEquivalent(expectedCodes, Enum.GetValues<GenerationFailureCode>());

        foreach (GenerationFailureCode code in expectedCodes)
        {
            var error = new GenerationError(
                code,
                nativeSeed: -1,
                stage: "l01b.failure-probe",
                zoneId: StableId.Derive(RandomDomain.Sites, StableId.Zero, 1),
                inputHash: Hash256.Compute("input"u8),
                details: "expected injected failure",
                canRetry: code is GenerationFailureCode.BudgetExceeded or GenerationFailureCode.Cancelled);
            GenerationResult<HeightSnapshot> result = GenerationResult<HeightSnapshot>.Failure(error);

            Assert.IsFalse(result.IsSuccess);
            GenerationFailure<HeightSnapshot> failure = Assert.IsInstanceOfType<GenerationFailure<HeightSnapshot>>(result);
            Assert.AreSame(error, failure.Error);
            Assert.IsNull(failure.GetType().GetProperty("Snapshot"));
        }
    }

    private static ScaleModel NewScale(double geological, double altitude) =>
        new("kilometre-model", geological, "altitude-model", altitude);

    private static VerticalEnvelope NewEnvelope(
        long worldHeight,
        long seaLevel,
        long floor,
        long cover,
        long margin) => new(worldHeight, seaLevel, floor, cover, margin);
}
