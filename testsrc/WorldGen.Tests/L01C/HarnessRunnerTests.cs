using System.Text.Json;

namespace ISRWorldGen.Tests.L01C;

[TestClass]
public sealed class HarnessRunnerTests
{
    [TestMethod]
    public void ExitCodes_DistinguishSuccessFailureUsageAndAbsentFixture()
    {
        using CliResult success = RunDisposable(HarnessCliTestSupport.SuccessfulRunArguments());
        using CliResult failure = RunDisposable(
            ReplaceOption(HarnessCliTestSupport.SuccessfulRunArguments(), "--fault", "nonfinite"));
        using CliResult absent = RunDisposable(
            HarnessCliTestSupport.SuccessfulRunArguments(fixture: "fixture-that-does-not-exist"));
        using CliResult usage = RunDisposable("run", "--fixture", "plane-x");

        Assert.AreEqual(0, success.ExitCode);
        Assert.AreEqual(1, failure.ExitCode);
        Assert.AreEqual(2, usage.ExitCode);
        Assert.AreEqual(3, absent.ExitCode);
        Assert.AreEqual("SUCCESS", Status(success));
        Assert.AreEqual("FAILURE", Status(failure));
        Assert.AreEqual("USAGE_ERROR", Status(usage));
        Assert.AreEqual("TEST_ABSENT", Status(absent));
    }

    [TestMethod]
    public void AnalyticalFixtures_ExportExplicitReproductionAndNumericMetrics()
    {
        foreach (string fixture in new[] { "plane-x", "saddle" })
        {
            using CliResult result = RunDisposable(
                HarnessCliTestSupport.SuccessfulRunArguments(
                    fixture,
                    order: "permuted",
                    workers: 2,
                    cache: "hot",
                    seed: -437_287_116));

            Assert.AreEqual(0, result.ExitCode, result.StandardError);
            JsonElement report = result.Report!.RootElement;
            Assert.AreEqual(1, report.GetProperty("runnerSchemaVersion").GetInt32());
            Assert.AreEqual(1u, report.GetProperty("snapshotSchemaVersion").GetUInt32());
            Assert.AreEqual(1u, report.GetProperty("algorithmVersion").GetUInt32());
            Assert.AreEqual(fixture, HarnessCliTestSupport.RequiredString(report, "fixture"));
            Assert.AreEqual(-437_287_116, report.GetProperty("seed").GetInt32());
            Assert.AreEqual(HarnessCliTestSupport.ConfigHash, HarnessCliTestSupport.RequiredString(report, "configHash"));
            Assert.AreEqual(HarnessCliTestSupport.Commit, HarnessCliTestSupport.RequiredString(report, "commit"));
            Assert.AreEqual("permuted", HarnessCliTestSupport.RequiredString(report, "order"));
            Assert.AreEqual(2, report.GetProperty("workers").GetInt32());
            Assert.AreEqual("hot", HarnessCliTestSupport.RequiredString(report, "cache"));

            JsonElement metrics = report.GetProperty("publication").GetProperty("metrics");
            Assert.IsGreaterThan(0, metrics.GetProperty("sampleCount").GetInt32());
            Assert.IsGreaterThan(0, metrics.GetProperty("voxelCount").GetInt32());
            Assert.IsLessThanOrEqualTo(
                metrics.GetProperty("maximumQuantizedHeight").GetInt64(),
                metrics.GetProperty("minimumQuantizedHeight").GetInt64());
            Assert.AreEqual(64, HarnessCliTestSupport.RequiredString(metrics, "dataHash").Length);
            Assert.AreEqual(64, HarnessCliTestSupport.RequiredString(metrics, "voxelHash").Length);
        }
    }

    [TestMethod]
    public void ExplicitOrderWorkersAndCache_DoNotChangePublishedHashes()
    {
        using CliResult baseline = RunDisposable(HarnessCliTestSupport.SuccessfulRunArguments());
        string baselineData = PublishedMetric(baseline, "dataHash");
        string baselineVoxels = PublishedMetric(baseline, "voxelHash");

        string[][] variants =
        [
            HarnessCliTestSupport.SuccessfulRunArguments(order: "reverse", workers: 2, cache: "cold"),
            HarnessCliTestSupport.SuccessfulRunArguments(order: "permuted", workers: 16, cache: "hot"),
            HarnessCliTestSupport.SuccessfulRunArguments(order: "linear", workers: 1, cache: "hot"),
        ];

        foreach (string[] arguments in variants)
        {
            using CliResult variant = RunDisposable(arguments);
            Assert.AreEqual(0, variant.ExitCode, variant.StandardError);
            Assert.AreEqual(baselineData, PublishedMetric(variant, "dataHash"));
            Assert.AreEqual(baselineVoxels, PublishedMetric(variant, "voxelHash"));
        }
    }

    private static CliResult RunDisposable(params string[] arguments) => HarnessCliTestSupport.Run(arguments);

    private static string Status(CliResult result)
    {
        Assert.IsNotNull(result.Report, result.StandardError);
        return HarnessCliTestSupport.RequiredString(result.Report.RootElement, "status");
    }

    private static string PublishedMetric(CliResult result, string property)
    {
        Assert.IsNotNull(result.Report, result.StandardError);
        return HarnessCliTestSupport.RequiredString(
            result.Report.RootElement.GetProperty("publication").GetProperty("metrics"),
            property);
    }

    private static string[] ReplaceOption(string[] arguments, string option, string replacement)
    {
        string[] copy = arguments.ToArray();
        int optionIndex = Array.IndexOf(copy, option);
        Assert.IsTrue(optionIndex >= 0 && optionIndex + 1 < copy.Length);
        copy[optionIndex + 1] = replacement;
        return copy;
    }
}
