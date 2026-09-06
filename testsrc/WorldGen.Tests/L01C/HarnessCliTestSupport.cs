using System.Diagnostics;
using System.Text.Json;

namespace ISRWorldGen.Tests.L01C;

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError, JsonDocument? Report) : IDisposable
{
    public void Dispose() => Report?.Dispose();
}

internal static class HarnessCliTestSupport
{
    internal const string ConfigHash = "9e12605ff5e0e94ccccf28eac0ded526da1e687a1fd3a0650b782da7484906ed";
    internal const string Commit = "1111111111111111111111111111111111111111";

    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    internal static string ToolsDll => Path.Combine(
        RepositoryRoot,
        "src",
        "WorldGen.Tools",
        "bin",
        "Release",
        "net10.0",
        "ISRWorldGen.Tools.dll");

    internal static CliResult Run(params string[] arguments)
    {
        if (!File.Exists(ToolsDll))
        {
            Assert.Fail($"WorldGen.Tools must be built before L01C tests: {ToolsDll}");
        }

        string outputDirectory = Path.Combine(
            RepositoryRoot,
            "artifacts",
            "test-results",
            "L01C",
            "unit");
        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory, $"report-{Environment.ProcessId}-{Guid.NewGuid():N}.json");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ToolsDll);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(reportPath);
        startInfo.Environment.Remove("VINTAGE_STORY");
        startInfo.Environment["VintageStoryPath"] = @"Z:\ISRWorldGen-NoGame";

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet harness.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(30_000), "Harness process did not exit within 30 seconds.");

        JsonDocument? report = File.Exists(reportPath)
            ? JsonDocument.Parse(File.ReadAllBytes(reportPath))
            : null;
        return new CliResult(process.ExitCode, standardOutput, standardError, report);
    }

    internal static string[] SuccessfulRunArguments(
        string fixture = "plane-x",
        string order = "linear",
        int workers = 1,
        string cache = "cold",
        int seed = 73) =>
    [
        "run",
        "--fixture", fixture,
        "--seed", seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--config-hash", ConfigHash,
        "--commit", Commit,
        "--order", order,
        "--workers", workers.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--cache", cache,
        "--fault", "none",
        "--budget", "100000",
    ];

    internal static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new InvalidDataException($"Missing JSON string {property}.");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ISRWorldGen.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ISRWorldGen.sln from the test binary.");
    }
}
