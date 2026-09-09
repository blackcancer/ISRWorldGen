// This assembly is intentionally outside the product solution and package.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ISRWorldGen.WorldgenProbe;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ISRWorldGen.L00C.Laboratory;

/// <summary>
/// Debug-only L00-C client harness linked into the existing ISRWorldGen mod.
/// It is inert unless the explicit laboratory switch is enabled.
/// </summary>
public sealed class L00CMenuActionLabModSystem : ModSystem
{
    // This token never exposes an API. It is only a cancellation capability for
    // the short bootstrap listener and becomes inert before controller handoff.
    private L00CManagerLeaseToken? pendingLease;
    private L00CLevelFinalizeSessionLease? finalizeLease;
    private ICoreClientAPI? levelFinalizeApi;

    /// <inheritdoc />
    public override void StartClientSide(ICoreClientAPI api)
    {
#if !DEBUG
        return;
#else
        // This must remain the first observable behaviour.  Ordinary Debug
        // sessions load the same production package but never touch a root,
        // reflection lock, debugger state, save, profile, or session.
        if (!string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal)) return;
        string root = RequireLaboratoryRoot();
        RecordF5LaunchAcquisition(root);
        L00CProcessCampaignInstallResult installed = L00CProcessCampaignController.InstallOrSignal(api, root);
        pendingLease = installed.Lease;
        if (installed.Accepted)
        {
            finalizeLease = installed.FinalizeLease ?? throw new InvalidOperationException("L00-C accepted install omitted its finalize-session lease.");
            api.Event.LevelFinalize += OnLevelFinalize;
            levelFinalizeApi = api;
            Mod.Logger.Notification("L00C_INPROCESS_HARNESS_READY: process-lifetime ScreenManager pump installed or signalled.");
        }
        else
            Mod.Logger.Error("L00C_INPROCESS_HARNESS_REFUSED code=" + installed.Diagnostic);
#endif
    }

    /// <inheritdoc />
    public override void Dispose()
    {
#if DEBUG
        ICoreClientAPI? subscribed = levelFinalizeApi;
        levelFinalizeApi = null;
        if (subscribed is not null) subscribed.Event.LevelFinalize -= OnLevelFinalize;
        L00CLevelFinalizeSessionLease? retainedFinalizeLease = finalizeLease;
        finalizeLease = null;
        if (retainedFinalizeLease is not null) L00CProcessCampaignController.RetireSession(retainedFinalizeLease);
        L00CManagerLeaseToken? retained = pendingLease;
        pendingLease = null;
        retained?.Cancel();
#endif
        base.Dispose();
    }

    private void OnLevelFinalize()
    {
        // Capture happens before entering the process-controller lock. A stale
        // callback already in flight before a menu click can never acquire the
        // epoch published after that click.
        L00CLevelFinalizeSignal? signal = finalizeLease?.Capture();
        L00CProcessCampaignController.SignalLevelFinalize(signal);
    }

    private static string RequireLaboratoryRoot()
    {
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C in-process harness requires Debugger.IsAttached and ISR_L00C_LAB=1.");
        string? root = Environment.GetEnvironmentVariable("ISR_L00C_LAB_ROOT");
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("L00-C laboratory mod requires ISR_L00C_LAB_ROOT; it never chooses a profile or reads a session.");
        return root;
    }

    private static void RecordF5LaunchAcquisition(string laboratoryRoot)
    {
        const string protocol = "l00c-f5-debug-transaction-v2";
        string root = Path.GetFullPath(laboratoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo? local = Directory.GetParent(root);
        DirectoryInfo? repository = local?.Parent;
        if (local is null || repository is null || !string.Equals(local.Name, ".local", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(root), "L00C", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C F5 acquisition requires the repository .local\\L00C root.");

        string transactionId = RequiredEnvironment("ISR_L00C_F5_TRANSACTION_ID");
        string nonce = RequiredEnvironment("ISR_L00C_F5_LAUNCH_NONCE");
        string transactionDirectory = Path.GetFullPath(RequiredEnvironment("ISR_L00C_F5_TRANSACTION_DIRECTORY"));
        string solutionPath = Path.GetFullPath(RequiredEnvironment("ISR_L00C_F5_SOLUTION_PATH"));
        if (!IsLowerHex(transactionId, 32) || !IsUpperHex(nonce, 64))
            throw new InvalidOperationException("L00-C F5 transaction identity is malformed.");
        string expectedTransactionDirectory = Path.Combine(root, "f5-profile-transactions", "f5-" + transactionId);
        string expectedSolution = Path.Combine(repository.FullName, "ISRWorldGen.sln");
        if (!string.Equals(transactionDirectory, expectedTransactionDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(solutionPath, expectedSolution, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C F5 transaction belongs to another directory or solution.");
        DirectoryInfo transaction = new(transactionDirectory);
        if (!transaction.Exists || (transaction.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("L00-C F5 transaction directory is absent or redirected.");
        if (!int.TryParse(RequiredEnvironment("ISR_L00C_F5_VISUAL_STUDIO_PID"), out int visualStudioProcessId) || visualStudioProcessId <= 0)
            throw new InvalidOperationException("L00-C F5 Visual Studio process identity is malformed.");

        using Process current = Process.GetCurrentProcess();
        string executablePath = Path.GetFullPath(Environment.ProcessPath ?? current.MainModule?.FileName ?? throw new InvalidOperationException("L00-C cannot attest its process executable."));
        string[] arguments = Environment.GetCommandLineArgs();
        var json = new StringBuilder(1024);
        json.Append("{\"schemaVersion\":2,\"protocol\":\"").Append(protocol)
            .Append("\",\"status\":\"CHILD_ACQUIRED\",\"transactionId\":\"").Append(transactionId)
            .Append("\",\"nonce\":\"").Append(nonce)
            .Append("\",\"transactionDirectory\":\"").Append(Escape(transactionDirectory))
            .Append("\",\"laboratoryRoot\":\"").Append(Escape(root))
            .Append("\",\"solutionPath\":\"").Append(Escape(solutionPath))
            .Append("\",\"visualStudioProcessId\":").Append(visualStudioProcessId)
            .Append(",\"processId\":").Append(Environment.ProcessId)
            .Append(",\"processStartUtc\":\"").Append(current.StartTime.ToUniversalTime().ToString("o"))
            .Append("\",\"recordedUtc\":\"").Append(DateTimeOffset.UtcNow.ToString("o"))
            .Append("\",\"debuggerAttached\":").Append(Debugger.IsAttached ? "true" : "false")
            .Append(",\"executablePath\":\"").Append(Escape(executablePath))
            .Append("\",\"commandLine\":\"").Append(Escape(Environment.CommandLine))
            .Append("\",\"arguments\":[");
        for (int index = 1; index < arguments.Length; index++)
        {
            if (index > 1) json.Append(',');
            json.Append('"').Append(Escape(arguments[index])).Append('"');
        }
        json.Append("]}");
        byte[] bytes = Encoding.UTF8.GetBytes(json.ToString());
        string receiptPath = Path.Combine(transactionDirectory, "child-acquisition.json");
        string publishingPath = receiptPath + ".publishing";
        if (File.Exists(receiptPath))
            throw new InvalidOperationException("L00-C F5 child acquisition receipt already exists; replay is forbidden before any write.");
        bool publishingCreated = false;
        try
        {
            using (var stream = new FileStream(publishingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                publishingCreated = true;
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            // The final authoritative name is never visible until the complete
            // receipt is durable. A hard stop leaves only .publishing, which
            // never unlocks host-side restoration.
            File.Move(publishingPath, receiptPath);
        }
        catch
        {
            // Normal publication failures are not crashes: remove only our
            // fixed, transaction-owned non-authoritative residue.
            if (publishingCreated && File.Exists(publishingPath)) File.Delete(publishingPath);
            throw;
        }
    }

    private static string RequiredEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("L00-C F5 acquisition requires " + name + ".");
        return value;
    }

    private static bool IsLowerHex(string value, int length)
    {
        if (value.Length != length) return false;
        foreach (char character in value) if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
        return true;
    }

    private static bool IsUpperHex(string value, int length)
    {
        if (value.Length != length) return false;
        foreach (char character in value) if (!((character >= '0' && character <= '9') || (character >= 'A' && character <= 'F'))) return false;
        return true;
    }

    private static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length + 16);
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\': escaped.Append("\\\\"); break;
                case '"': escaped.Append("\\\""); break;
                case '\b': escaped.Append("\\b"); break;
                case '\f': escaped.Append("\\f"); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                default:
                    if (character < 0x20) escaped.Append("\\u").Append(((int)character).ToString("X4"));
                    else escaped.Append(character);
                    break;
            }
        }
        return escaped.ToString();
    }
}
