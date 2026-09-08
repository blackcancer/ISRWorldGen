using System.Diagnostics;
using System.Text.Json;

namespace ISRWorldGen.Tests.L05D;

[TestClass]
public sealed class PlanAdoptionTests
{
    [TestMethod]
    public void T05U_01_RealPreparePlanUpdatePreservesHistoricalStateAndUnknownTask()
    {
        using AdoptionFixture fixture = AdoptionFixture.Create();
        JsonDocument report = fixture.RunPrepare();

        Assert.AreEqual("DOCUMENTATION_PREPARATION_ONLY", report.RootElement.GetProperty("scope").GetString());
        Assert.IsFalse(report.RootElement.GetProperty("active_repository_modified").GetBoolean());
        using JsonDocument before = JsonDocument.Parse(fixture.StateBefore);
        using JsonDocument candidate = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Output, "state.candidate.json")));
        Assert.AreEqual(before.RootElement.GetProperty("metadata").GetProperty("historical_field").GetString(), candidate.RootElement.GetProperty("metadata").GetProperty("historical_field").GetString());
        Assert.AreEqual("IN_PROGRESS", candidate.RootElement.GetProperty("tasks").GetProperty("L05-C").GetProperty("status").GetString());
        Assert.AreEqual("kept", candidate.RootElement.GetProperty("tasks").GetProperty("LOCAL-UNKNOWN").GetProperty("proof").GetString());
        Assert.AreEqual("BACKLOG", candidate.RootElement.GetProperty("tasks").GetProperty("L05-D").GetProperty("status").GetString());
        Assert.AreEqual(fixture.StateBefore, File.ReadAllText(Path.Combine(fixture.Existing, "registry", "state.json")));
    }

    [TestMethod]
    public void T05U_01_RejectsBlankStateAndManifestThatAttemptsStateOrCode()
    {
        using AdoptionFixture fixture = AdoptionFixture.Create();
        File.WriteAllText(Path.Combine(fixture.Existing, "registry", "state.json"), "{}");
        ProcessResult blankState = fixture.RunPrepareExpectingFailure();
        StringAssert.Contains(blankState.Error, "Unsupported active state schema");

        using AdoptionFixture stateManifest = AdoptionFixture.Create();
        stateManifest.ReplaceManifest(new[] { "registry/state.json" });
        StringAssert.Contains(stateManifest.RunPrepareExpectingFailure().Error, "Forbidden active data");

        using AdoptionFixture codeManifest = AdoptionFixture.Create();
        codeManifest.ReplaceManifest(new[] { "src/WorldGen.Core/WorldGen.Core.csproj" });
        StringAssert.Contains(codeManifest.RunPrepareExpectingFailure().Error, "Forbidden active data");
    }

    [TestMethod]
    public void T05U_02_ExistingBoundaryPortCannotReceiveUnversionedSeasonalField()
    {
        JsonElement boundaryPort = JsonDocument.Parse("""
            {"portId":"hydrology:42","algorithmVersion":1,"iteration":8,"dischargeModel":"12.0 L3/Ymod","corridor":"protected"}
            """).RootElement.Clone();
        JsonElement unversionedSeasonal = JsonDocument.Parse("""
            {"portId":"hydrology:42","algorithmVersion":1,"iteration":8,"dischargeModel":"12.0 L3/Ymod","seasonalDischarge":[3,4,5],"corridor":"protected"}
            """).RootElement.Clone();

        Assert.IsTrue(ContractGate.IsCompatibleBoundaryPort(boundaryPort, out string accepted));
        Assert.IsFalse(ContractGate.IsCompatibleBoundaryPort(unversionedSeasonal, out string rejected));
        StringAssert.Contains(rejected, "SeasonalWaterSidecar");
        StringAssert.Contains(accepted, "accepted");
    }

    [TestMethod]
    public void T05U_03_ProspectiveL18AToL11BDependencyIsRejectedAsCycleWithoutWritingState()
    {
        string statePath = Path.Combine(Repository.Root, "registry", "state.json");
        string before = File.ReadAllText(statePath);
        Dictionary<string, string[]> graph = TaskDag.Read(Path.Combine(Repository.Root, "registry", "tasks.json"));
        graph["L18-A"] = graph["L18-A"].Append("L11-B").ToArray();

        IReadOnlyList<string> cycle = TaskDag.FindCycle(graph);
        Assert.IsTrue(cycle.Zip(cycle.Skip(1), (from, to) => (from, to)).Contains(("L18-A", "L11-B")),
            $"The prospective L18-A -> L11-B edge was not retained in cycle {string.Join(" -> ", cycle)}.");
        Assert.AreEqual(before, File.ReadAllText(statePath), "DAG review is prospective and must not modulate execution state.");
    }
}

internal static class ContractGate
{
    private static readonly HashSet<string> BoundaryPortFields = new(StringComparer.Ordinal)
    {
        "portId", "algorithmVersion", "iteration", "dischargeModel", "corridor"
    };

    internal static bool IsCompatibleBoundaryPort(JsonElement port, out string reason)
    {
        foreach (JsonProperty property in port.EnumerateObject())
        {
            if (!BoundaryPortFields.Contains(property.Name))
            {
                reason = $"{property.Name} requires a versioned SeasonalWaterSidecar; C03 BoundaryPort cannot be extended in place.";
                return false;
            }
        }

        reason = "C03 BoundaryPort encoding accepted unchanged.";
        return true;
    }
}

internal static class TaskDag
{
    internal static Dictionary<string, string[]> Read(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("tasks").EnumerateArray().ToDictionary(
            task => task.GetProperty("id").GetString()!,
            task => task.GetProperty("depends_on").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            StringComparer.Ordinal);
    }

    internal static IReadOnlyList<string> FindCycle(IReadOnlyDictionary<string, string[]> graph)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var trail = new List<string>();
        foreach (string task in graph.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            IReadOnlyList<string>? cycle = Visit(task);
            if (cycle is not null) return cycle;
        }
        throw new AssertFailedException("Expected a prospective cycle.");

        IReadOnlyList<string>? Visit(string task)
        {
            if (active.Contains(task))
            {
                int start = trail.IndexOf(task);
                return trail.Skip(start).Append(task).ToArray();
            }
            if (!completed.Add(task)) return null;
            active.Add(task);
            trail.Add(task);
            if (graph.TryGetValue(task, out string[]? prerequisites))
            {
                foreach (string prerequisite in prerequisites.OrderBy(value => value, StringComparer.Ordinal))
                {
                    IReadOnlyList<string>? cycle = Visit(prerequisite);
                    if (cycle is not null) return cycle;
                }
            }
            trail.RemoveAt(trail.Count - 1);
            active.Remove(task);
            return null;
        }
    }
}

internal sealed class AdoptionFixture : IDisposable
{
    private AdoptionFixture(string root, string package, string existing, string output, string stateBefore)
        => (Root, Package, Existing, Output, StateBefore) = (root, package, existing, output, stateBefore);

    internal string Root { get; }
    internal string Package { get; }
    internal string Existing { get; }
    internal string Output { get; }
    internal string StateBefore { get; }

    internal static AdoptionFixture Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "ISRWorldGen-L05D-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "package");
        string existing = Path.Combine(root, "existing");
        string output = Path.Combine(root, "review");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(existing);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Repository.Root, "registry", "update-files.json")));
        foreach (string relative in manifest.RootElement.GetProperty("files").EnumerateArray().Select(item => item.GetString()!))
        {
            Copy(Repository.Root, package, relative);
            Copy(Repository.Root, existing, relative);
        }
        Copy(Repository.Root, package, "registry/update-files.json");
        Copy(Repository.Root, package, "registry/update-r12.json");
        Copy(Repository.Root, package, "registry/tasks.json");
        Copy(Repository.Root, package, "tools/update_baseline_r11.json");
        Copy(Repository.Root, package, "tools/prepare_plan_update.py");
        string state = """
            {"schema_version":1,"metadata":{"historical_field":"retained"},"tasks":{"L03-C":{"status":"DONE","evidence":["old-proof"]},"L05-C":{"status":"IN_PROGRESS","branch":"local/l05-c"},"LOCAL-UNKNOWN":{"status":"LOCAL","proof":"kept"}}}
            """;
        Directory.CreateDirectory(Path.Combine(existing, "registry"));
        File.WriteAllText(Path.Combine(existing, "registry", "state.json"), state);
        return new AdoptionFixture(root, package, existing, output, state);
    }

    internal JsonDocument RunPrepare()
    {
        ProcessResult result = RunPrepareCore();
        Assert.AreEqual(0, result.ExitCode, result.Error);
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "review-report.json")));
    }

    internal ProcessResult RunPrepareExpectingFailure()
    {
        ProcessResult result = RunPrepareCore();
        Assert.AreNotEqual(0, result.ExitCode, result.Output);
        return result;
    }

    internal void ReplaceManifest(string[] files)
        => File.WriteAllText(Path.Combine(Package, "registry", "update-files.json"), JsonSerializer.Serialize(new { schema_version = 1, files }));

    private ProcessResult RunPrepareCore()
    {
        var start = new ProcessStartInfo("python")
        {
            WorkingDirectory = Package, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(Package, "tools", "prepare_plan_update.py"));
        start.ArgumentList.Add("--existing"); start.ArgumentList.Add(Existing);
        start.ArgumentList.Add("--output"); start.ArgumentList.Add(Output);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static void Copy(string sourceRoot, string destinationRoot, string relative)
    {
        string source = Path.Combine(sourceRoot, relative);
        string destination = Path.Combine(destinationRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal static class Repository
{
    internal static string Root { get; } = Find();

    private static string Find()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ISRWorldGen.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("ISRWorldGen repository root was not found.");
    }
}
