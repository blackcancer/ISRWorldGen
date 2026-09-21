using System.Text.Json;
if (args.Length != 1) throw new ArgumentException("Usage: WorldGen.PowerLawChecks <new-output-directory>");
string root = Path.GetFullPath(args[0]);
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Output already exists; never overwrite evidence.");
string[] checks = PowerLawChecks.Run();
Directory.CreateDirectory(root);
File.WriteAllText(Path.Combine(root, "verification.json"), JsonSerializer.Serialize(new {
    status = "PASS_NUMERICAL_ONLY", geographicAcceptance = "NOT_ACCEPTED", erosion = "NOT_RUN",
    checksPassed = checks.Length, checks,
    commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "UNVERIFIED_WORKTREE"
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"POWER_LAW_CHECKS={checks.Length}");
