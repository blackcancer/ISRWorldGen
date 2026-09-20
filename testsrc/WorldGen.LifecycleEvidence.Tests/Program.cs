using ISRWorldGen.L00C.Laboratory;
using System.Text.Json;

try
{
    if (args.Length == 1 && args[0] == "--regression-only")
    {
        L00CLifecycleCommitEvidenceOracle.MissingRegistrationMustNotCommit();
        Console.WriteLine("MISSING_REGISTRATION_REJECTED");
        return 0;
    }
    if (args.Length != 0) throw new ArgumentException("Only --regression-only is supported.");
    int cases = L00CLifecycleCommitEvidenceOracle.Run();
    if (L00CLifecycleShutdownOrderingOracle.Run() != 0)
        throw new InvalidOperationException("The historical shutdown ordering oracle failed.");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Status = "PASS", Scope = "SYNTHETIC_STATE_MACHINE_ONLY",
        EvidenceCases = cases, HistoricalFinalizedSessions = 15,
        NativeGameTests = "NOT_RUN", VisualStudioMcp = "NOT_RUN",
        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription
    }));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("L00C_EVIDENCE_REGRESSION_FAILED: " + exception);
    return 1;
}
