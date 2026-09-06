using System.Text.Json;

namespace ISRWorldGen.Tests.L01C;

[TestClass]
public sealed class OfflineDependencyTests
{
    [TestMethod]
    public void CompiledCoreAndTools_HaveNoGameGuiNativeOrPackageDependency()
    {
        using CliResult result = HarnessCliTestSupport.Run(
            "audit-dependencies",
            "--assembly",
            HarnessCliTestSupport.ToolsDll,
            "--commit",
            HarnessCliTestSupport.Commit);

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.IsNotNull(result.Report, "The dependency audit must produce a structured report.");
        JsonElement report = result.Report.RootElement;
        Assert.AreEqual(1, report.GetProperty("runnerSchemaVersion").GetInt32());
        Assert.AreEqual("PASS", HarnessCliTestSupport.RequiredString(report, "status"));
        Assert.AreEqual(HarnessCliTestSupport.Commit, HarnessCliTestSupport.RequiredString(report, "commit"));
        Assert.AreEqual("compiled-metadata-and-deps-json", HarnessCliTestSupport.RequiredString(report, "inspectionMode"));

        string[] roots = report.GetProperty("roots").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        CollectionAssert.Contains(roots, "ISRWorldGen.Tools");
        CollectionAssert.Contains(roots, "ISRWorldGen.Core");

        Assert.IsGreaterThan(0, report.GetProperty("directReferences").GetArrayLength());
        Assert.IsGreaterThan(0, report.GetProperty("transitiveReferences").GetArrayLength());
        Assert.AreEqual(0, report.GetProperty("forbiddenReferences").GetArrayLength());
        Assert.AreEqual(0, report.GetProperty("nativeImports").GetArrayLength());
        Assert.AreEqual(0, report.GetProperty("packageDependencies").GetArrayLength());
    }

    [TestMethod]
    public void DependencyAudit_MissingCompiledAssemblyIsAUsageFailure()
    {
        using CliResult result = HarnessCliTestSupport.Run(
            "audit-dependencies",
            "--assembly",
            Path.Combine(HarnessCliTestSupport.RepositoryRoot, "missing-tools.dll"),
            "--commit",
            HarnessCliTestSupport.Commit);

        Assert.AreEqual(2, result.ExitCode);
        Assert.IsNotNull(result.Report);
        Assert.AreEqual("USAGE_ERROR", HarnessCliTestSupport.RequiredString(result.Report.RootElement, "status"));
    }
}
