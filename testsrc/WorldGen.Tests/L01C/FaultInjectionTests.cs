using System.Text.Json;

namespace ISRWorldGen.Tests.L01C;

[TestClass]
public sealed class FaultInjectionTests
{
    private static readonly string[] CancellationBoundaries =
    [
        "validate-input",
        "prepare-fixture",
        "generate-data",
        "validate-snapshot",
        "publish-snapshot",
    ];

    [TestMethod]
    public void CancellationAtEveryStageBoundary_PublishesNoPartialSnapshot()
    {
        foreach (string boundary in CancellationBoundaries)
        {
            string[] arguments = ReplaceOption(
                HarnessCliTestSupport.SuccessfulRunArguments(seed: 42),
                "--fault",
                $"cancel:{boundary}");
            using CliResult result = RunDisposable(arguments);

            AssertFailure(result, "Cancelled", boundary);
        }
    }

    [TestMethod]
    public void BudgetAndNonFiniteFaults_AreTypedAndAtomic()
    {
        string[] budgetArguments = ReplaceOption(
            HarnessCliTestSupport.SuccessfulRunArguments(seed: 42),
            "--budget",
            "1");
        using CliResult budget = RunDisposable(budgetArguments);
        AssertFailure(budget, "BudgetExceeded", "generate-data");

        string[] nonFiniteArguments = ReplaceOption(
            HarnessCliTestSupport.SuccessfulRunArguments(seed: 42),
            "--fault",
            "nonfinite");
        using CliResult nonFinite = RunDisposable(nonFiniteArguments);
        AssertFailure(nonFinite, "InvalidInput", "validate-snapshot");
    }

    [TestMethod]
    public void CleanRerunAfterEveryFault_ReproducesTheExactWitness()
    {
        using CliResult witness = RunDisposable(HarnessCliTestSupport.SuccessfulRunArguments(seed: 42));
        string witnessData = Metric(witness, "dataHash");
        string witnessVoxels = Metric(witness, "voxelHash");

        IEnumerable<string[]> faultRuns = CancellationBoundaries
            .Select(boundary => ReplaceOption(
                HarnessCliTestSupport.SuccessfulRunArguments(seed: 42),
                "--fault",
                $"cancel:{boundary}"))
            .Append(ReplaceOption(
                HarnessCliTestSupport.SuccessfulRunArguments(seed: 42),
                "--fault",
                "nonfinite"))
            .Append(ReplaceOption(
                HarnessCliTestSupport.SuccessfulRunArguments(seed: 42),
                "--budget",
                "1"));
        foreach (string[] faultArguments in faultRuns)
        {
            using CliResult failed = RunDisposable(faultArguments);
            Assert.AreEqual(1, failed.ExitCode);

            using CliResult clean = RunDisposable(HarnessCliTestSupport.SuccessfulRunArguments(seed: 42));
            Assert.AreEqual(witnessData, Metric(clean, "dataHash"));
            Assert.AreEqual(witnessVoxels, Metric(clean, "voxelHash"));
        }
    }

    private static void AssertFailure(CliResult result, string expectedCode, string expectedStage)
    {
        Assert.AreEqual(1, result.ExitCode, result.StandardError);
        Assert.IsNotNull(result.Report);
        JsonElement report = result.Report.RootElement;
        Assert.AreEqual("FAILURE", HarnessCliTestSupport.RequiredString(report, "status"));
        Assert.AreEqual(expectedCode, HarnessCliTestSupport.RequiredString(report.GetProperty("failure"), "code"));
        Assert.AreEqual(expectedStage, HarnessCliTestSupport.RequiredString(report.GetProperty("failure"), "stage"));
        Assert.AreEqual(
            HarnessCliTestSupport.ConfigHash,
            HarnessCliTestSupport.RequiredString(report.GetProperty("failure"), "inputHash"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            HarnessCliTestSupport.RequiredString(report.GetProperty("failure"), "details")));
        JsonElement publication = report.GetProperty("publication");
        Assert.IsFalse(publication.GetProperty("visible").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, publication.GetProperty("metrics").ValueKind);
    }

    private static string Metric(CliResult result, string property)
    {
        Assert.IsNotNull(result.Report, result.StandardError);
        return HarnessCliTestSupport.RequiredString(
            result.Report.RootElement.GetProperty("publication").GetProperty("metrics"),
            property);
    }

    private static CliResult RunDisposable(params string[] arguments) => HarnessCliTestSupport.Run(arguments);

    private static string[] ReplaceOption(string[] arguments, string option, string replacement)
    {
        string[] copy = arguments.ToArray();
        int optionIndex = Array.IndexOf(copy, option);
        Assert.IsTrue(optionIndex >= 0 && optionIndex + 1 < copy.Length);
        copy[optionIndex + 1] = replacement;
        return copy;
    }
}
