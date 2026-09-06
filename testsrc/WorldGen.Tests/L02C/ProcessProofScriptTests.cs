namespace ISRWorldGen.Tests.L02C;

[TestClass]
public sealed class ProcessProofScriptTests
{
    [TestMethod]
    public void ProcessProof_DeletesStaleArtifactsAndRequiresExactHeadIdentity()
    {
        string repositoryRoot = PatternTestSupport.FindRepositoryRoot();
        string scriptPath = Path.Combine(
            repositoryRoot,
            "testsrc",
            "WorldGen.Tests",
            "L02C",
            "Invoke-L02CProcessDeterminism.ps1");
        string script = File.ReadAllText(scriptPath);

        StringAssert.Contains(script, "Remove-Item -LiteralPath $reportPath -Force");
        StringAssert.Contains(script, "Remove-Item -LiteralPath $trxPath -Force");
        StringAssert.Contains(script, "Remove-Item -LiteralPath $summaryPath -Force");
        StringAssert.Contains(script, "$report.status -ne \"PASS\"");
        StringAssert.Contains(script, "$report.commit -ne $commit");
        StringAssert.Contains(script, "$report.schemaVersion -ne 1");
    }
}
