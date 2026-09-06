using System.Globalization;
using System.Text.Json;
using ISRWorldGen.Core.Contracts;

namespace ISRWorldGen.Tools.TestHarness;

internal static class HarnessApplication
{
    private const int RunnerSchemaVersion = 1;
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;
    private const int UsageExitCode = 2;
    private const int TestAbsentExitCode = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static int Run(string[] arguments)
    {
        string? outputPath = CommandLine.FindOutputPath(arguments);
        try
        {
            if (!CommandLine.TryParse(arguments, out ParsedCommand? command, out string? error))
            {
                return WriteAndReturn(
                    outputPath,
                    new StatusReport(RunnerSchemaVersion, "USAGE_ERROR", UsageExitCode, error ?? "Invalid command line."),
                    UsageExitCode);
            }

            return command switch
            {
                RunCommand run => ExecuteRun(run),
                AuditCommand audit => ExecuteAudit(audit),
                _ => WriteAndReturn(
                    outputPath,
                    new StatusReport(RunnerSchemaVersion, "USAGE_ERROR", UsageExitCode, "Unknown command."),
                    UsageExitCode),
            };
        }
        catch (Exception exception)
        {
            return WriteAndReturn(
                outputPath,
                new StatusReport(RunnerSchemaVersion, "USAGE_ERROR", UsageExitCode, exception.Message),
                UsageExitCode);
        }
    }

    private static int ExecuteRun(RunCommand command)
    {
        if (!AnalyticalFixtureCatalog.Contains(command.Fixture))
        {
            var absent = new HarnessReport(
                RunnerSchemaVersion,
                GenerationIdentity.SupportedSchemaVersion,
                GenerationIdentity.SupportedAlgorithmVersion,
                "TEST_ABSENT",
                TestAbsentExitCode,
                command.Fixture,
                command.Seed,
                command.ConfigHash.ToString(),
                command.Commit,
                command.Order,
                command.Workers,
                command.Cache,
                command.Fault,
                command.Budget,
                new PublicationReport(false, null),
                null,
                "No analytical fixture has the requested name.");
            return WriteAndReturn(command.OutputPath, absent, TestAbsentExitCode);
        }

        GenerationResult<AnalyticalSnapshotBundle> result = AnalyticalPipeline.Run(command);
        if (result is GenerationFailure<AnalyticalSnapshotBundle> failure)
        {
            GenerationError error = failure.Error;
            var report = new HarnessReport(
                RunnerSchemaVersion,
                GenerationIdentity.SupportedSchemaVersion,
                GenerationIdentity.SupportedAlgorithmVersion,
                "FAILURE",
                FailureExitCode,
                command.Fixture,
                command.Seed,
                command.ConfigHash.ToString(),
                command.Commit,
                command.Order,
                command.Workers,
                command.Cache,
                command.Fault,
                command.Budget,
                new PublicationReport(false, null),
                new FailureReport(
                    error.Code.ToString(),
                    error.Stage,
                    error.Details,
                    error.CanRetry,
                    error.InputHash.ToString()),
                null);
            return WriteAndReturn(command.OutputPath, report, FailureExitCode);
        }

        var success = (GenerationSuccess<AnalyticalSnapshotBundle>)result;
        AnalyticalSnapshotBundle snapshot = success.Snapshot;
        byte[] heightBytes = HeightSnapshotBinaryCodec.Serialize(snapshot.Height);
        byte[] voxelBytes = VoxelSnapshotBinaryCodec.Serialize(snapshot.Voxels);
        var metrics = new NumericMetrics(
            snapshot.Height.Samples.Count,
            snapshot.Voxels.Voxels.Count,
            snapshot.Height.Samples.Min(sample => sample.QuantizedHeight),
            snapshot.Height.Samples.Max(sample => sample.QuantizedHeight),
            Hash256.Compute(heightBytes).ToString(),
            Hash256.Compute(voxelBytes).ToString());
        var completed = new HarnessReport(
            RunnerSchemaVersion,
            GenerationIdentity.SupportedSchemaVersion,
            GenerationIdentity.SupportedAlgorithmVersion,
            "SUCCESS",
            SuccessExitCode,
            command.Fixture,
            command.Seed,
            command.ConfigHash.ToString(),
            command.Commit,
            command.Order,
            command.Workers,
            command.Cache,
            command.Fault,
            command.Budget,
            new PublicationReport(true, metrics),
            null,
            null);
        return WriteAndReturn(command.OutputPath, completed, SuccessExitCode);
    }

    private static int ExecuteAudit(AuditCommand command)
    {
        if (!File.Exists(command.AssemblyPath))
        {
            return WriteAndReturn(
                command.OutputPath,
                new StatusReport(
                    RunnerSchemaVersion,
                    "USAGE_ERROR",
                    UsageExitCode,
                    $"Compiled assembly does not exist: {command.AssemblyPath}"),
                UsageExitCode);
        }

        DependencyAuditReport report = CompiledDependencyAuditor.Audit(command.AssemblyPath, command.Commit);
        int exitCode = report.Status == "PASS" ? SuccessExitCode : FailureExitCode;
        return WriteAndReturn(command.OutputPath, report, exitCode);
    }

    private static int WriteAndReturn(string? outputPath, object report, int exitCode)
    {
        string json = JsonSerializer.Serialize(report, JsonOptions);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            string fullPath = Path.GetFullPath(outputPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, json + Environment.NewLine);
        }

        TextWriter writer = exitCode == SuccessExitCode ? Console.Out : Console.Error;
        writer.WriteLine(json);
        return exitCode;
    }

    private sealed record StatusReport(int RunnerSchemaVersion, string Status, int ExitCode, string Message);

    private sealed record HarnessReport(
        int RunnerSchemaVersion,
        uint SnapshotSchemaVersion,
        uint AlgorithmVersion,
        string Status,
        int ExitCode,
        string Fixture,
        int Seed,
        string ConfigHash,
        string Commit,
        string Order,
        int Workers,
        string Cache,
        string Fault,
        long Budget,
        PublicationReport Publication,
        FailureReport? Failure,
        string? Message);

    private sealed record PublicationReport(bool Visible, NumericMetrics? Metrics);

    private sealed record NumericMetrics(
        int SampleCount,
        int VoxelCount,
        long MinimumQuantizedHeight,
        long MaximumQuantizedHeight,
        string DataHash,
        string VoxelHash);

    private sealed record FailureReport(
        string Code,
        string Stage,
        string Details,
        bool CanRetry,
        string InputHash);
}

internal abstract record ParsedCommand(string OutputPath);

internal sealed record RunCommand(
    string Fixture,
    int Seed,
    Hash256 ConfigHash,
    string Commit,
    string Order,
    int Workers,
    string Cache,
    string Fault,
    long Budget,
    string OutputPath) : ParsedCommand(OutputPath);

internal sealed record AuditCommand(
    string AssemblyPath,
    string Commit,
    string OutputPath) : ParsedCommand(OutputPath);

internal static class CommandLine
{
    private static readonly HashSet<string> Orders = new(StringComparer.Ordinal)
    {
        "linear",
        "reverse",
        "permuted",
    };

    private static readonly HashSet<string> CacheModes = new(StringComparer.Ordinal)
    {
        "cold",
        "hot",
    };

    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out ParsedCommand? command,
        out string? error)
    {
        command = null;
        error = null;
        if (arguments.Count == 0)
        {
            error = "A command is required: run or audit-dependencies.";
            return false;
        }

        if (!TryParseOptions(arguments.Skip(1).ToArray(), out Dictionary<string, string>? options, out error))
        {
            return false;
        }

        return arguments[0] switch
        {
            "run" => TryParseRun(options!, out command, out error),
            "audit-dependencies" => TryParseAudit(options!, out command, out error),
            _ => Fail($"Unknown command '{arguments[0]}'.", out command, out error),
        };
    }

    internal static string? FindOutputPath(IReadOnlyList<string> arguments)
    {
        for (int index = arguments.Count - 2; index >= 0; index--)
        {
            if (arguments[index] == "--output")
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static bool TryParseRun(
        Dictionary<string, string> options,
        out ParsedCommand? command,
        out string? error)
    {
        command = null;
        if (!RequireExactOptions(
                options,
                ["fixture", "seed", "config-hash", "commit", "order", "workers", "cache", "fault", "budget", "output"],
                out error))
        {
            return false;
        }

        if (!int.TryParse(options["seed"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            error = "Seed must be a native int32 value.";
            return false;
        }

        Hash256 configHash;
        try
        {
            configHash = Hash256.Parse(options["config-hash"]);
        }
        catch (FormatException)
        {
            error = "Config hash must be 64 lowercase hexadecimal characters.";
            return false;
        }

        if (!IsCanonicalCommit(options["commit"]))
        {
            error = "Commit must be 40 or 64 lowercase hexadecimal characters.";
            return false;
        }

        if (!Orders.Contains(options["order"]))
        {
            error = "Order must be linear, reverse, or permuted.";
            return false;
        }

        if (!int.TryParse(options["workers"], NumberStyles.None, CultureInfo.InvariantCulture, out int workers) ||
            workers is < 1 or > 256)
        {
            error = "Workers must be in [1, 256].";
            return false;
        }

        if (!CacheModes.Contains(options["cache"]))
        {
            error = "Cache must be cold or hot.";
            return false;
        }

        if (!IsKnownFault(options["fault"]))
        {
            error = "Fault must be none, nonfinite, or cancel:<stage-boundary>.";
            return false;
        }

        if (!long.TryParse(options["budget"], NumberStyles.None, CultureInfo.InvariantCulture, out long budget) || budget <= 0)
        {
            error = "Budget must be a positive int64 value.";
            return false;
        }

        command = new RunCommand(
            options["fixture"],
            seed,
            configHash,
            options["commit"],
            options["order"],
            workers,
            options["cache"],
            options["fault"],
            budget,
            options["output"]);
        return true;
    }

    private static bool TryParseAudit(
        Dictionary<string, string> options,
        out ParsedCommand? command,
        out string? error)
    {
        command = null;
        if (!RequireExactOptions(options, ["assembly", "commit", "output"], out error))
        {
            return false;
        }

        if (!IsCanonicalCommit(options["commit"]))
        {
            error = "Commit must be 40 or 64 lowercase hexadecimal characters.";
            return false;
        }

        command = new AuditCommand(Path.GetFullPath(options["assembly"]), options["commit"], options["output"]);
        return true;
    }

    private static bool TryParseOptions(
        IReadOnlyList<string> arguments,
        out Dictionary<string, string>? options,
        out string? error)
    {
        options = new Dictionary<string, string>(StringComparer.Ordinal);
        error = null;
        if (arguments.Count % 2 != 0)
        {
            error = "Every option must have exactly one value.";
            return false;
        }

        for (int index = 0; index < arguments.Count; index += 2)
        {
            string name = arguments[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2)
            {
                error = $"Invalid option name '{name}'.";
                return false;
            }

            name = name[2..];
            if (!options.TryAdd(name, arguments[index + 1]))
            {
                error = $"Duplicate option '--{name}'.";
                return false;
            }
        }

        return true;
    }

    private static bool RequireExactOptions(
        IReadOnlyDictionary<string, string> options,
        IReadOnlyCollection<string> expected,
        out string? error)
    {
        string[] missing = expected.Where(option => !options.ContainsKey(option)).Order().ToArray();
        string[] unknown = options.Keys.Where(option => !expected.Contains(option)).Order().ToArray();
        if (missing.Length == 0 && unknown.Length == 0)
        {
            error = null;
            return true;
        }

        error = $"Missing options: [{string.Join(",", missing)}]; unknown options: [{string.Join(",", unknown)}].";
        return false;
    }

    private static bool IsKnownFault(string fault) =>
        fault is "none" or "nonfinite" ||
        fault is "cancel:validate-input" or
            "cancel:prepare-fixture" or
            "cancel:generate-data" or
            "cancel:validate-snapshot" or
            "cancel:publish-snapshot";

    private static bool IsCanonicalCommit(string commit) =>
        commit.Length is 40 or 64 && commit.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool Fail(string message, out ParsedCommand? command, out string? error)
    {
        command = null;
        error = message;
        return false;
    }
}
