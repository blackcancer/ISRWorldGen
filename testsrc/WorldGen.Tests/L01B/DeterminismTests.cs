namespace ISRWorldGen.Tests.L01B;

[TestClass]
public sealed class DeterminismTests
{
    [TestMethod]
    public void CanonicalHashes_AreIndependentOfWorkersOrderAndCacheState()
    {
        IReadOnlyList<SeedHashes> hashes = DeterminismFixture.VerifyFullMatrix();

        Assert.HasCount(16, hashes);
        Assert.HasCount(16, hashes.Select(result => result.Seed).Distinct());
    }

    [TestMethod]
    public void ProcessProbe_ProducesCanonicalHashesForRealRestartComparison()
    {
        IReadOnlyList<SeedHashes> hashes = DeterminismFixture.VerifyFullMatrix();

        Assert.HasCount(16, hashes);
        DeterminismFixture.WriteProcessReportIfRequested(hashes);
    }
}
