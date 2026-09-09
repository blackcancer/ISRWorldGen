// This assembly is intentionally outside the product solution and package.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
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
        if (levelFinalizeApi is not null)
        {
            if (!ReferenceEquals(levelFinalizeApi, api))
                throw new InvalidOperationException("L00-C refuses to reuse one ModSystem across distinct client sessions.");
            Mod.Logger.Notification("L00C_INPROCESS_HARNESS_READY: duplicate StartClientSide matched the acquired process/session and made no changes.");
            return;
        }
        L00CProcessCampaignInstallResult installed = L00CProcessCampaignController.InstallOrSignal(api, root);
        if (installed.Accepted)
        {
            L00CLevelFinalizeSessionLease sessionLease = installed.FinalizeLease
                ?? throw new InvalidOperationException("L00-C accepted install omitted its finalize-session lease.");
            pendingLease = installed.Lease;
            finalizeLease = sessionLease;
            levelFinalizeApi = api;
            try
            {
                api.Event.LevelFinalize += OnLevelFinalize;
            }
            catch
            {
                // Event accessors are external code.  A throwing add may have
                // attached before failing, so make one bounded removal attempt.
                try { api.Event.LevelFinalize -= OnLevelFinalize; }
                catch { /* controller/session compensation below is authoritative */ }
                levelFinalizeApi = null;
                finalizeLease = null;
                L00CManagerLeaseToken? retained = pendingLease;
                pendingLease = null;
                retained?.Complete();
                L00CProcessCampaignController.AbortSessionRegistration(sessionLease);
                throw;
            }
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
        // A pending bootstrap token must observe the current session generation
        // before RetireSession clears it.  Superseded tokens are already inert.
        L00CManagerLeaseToken? retained = pendingLease;
        pendingLease = null;
        retained?.Cancel();
        L00CLevelFinalizeSessionLease? retainedFinalizeLease = finalizeLease;
        finalizeLease = null;
        if (retainedFinalizeLease is not null) L00CProcessCampaignController.RetireSession(retainedFinalizeLease);
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
        string[] commandLine = Environment.GetCommandLineArgs();
        var arguments = new string[Math.Max(0, commandLine.Length - 1)];
        if (arguments.Length > 0) Array.Copy(commandLine, 1, arguments, 0, arguments.Length);
        var identity = new L00CF5LaunchIdentity(
            transactionId,
            nonce,
            transactionDirectory,
            root,
            solutionPath,
            visualStudioProcessId,
            current.Id,
            current.StartTime.ToUniversalTime().ToString("o"),
            Debugger.IsAttached,
            executablePath,
            Environment.CommandLine,
            arguments);
        L00CF5LaunchAcquisition.Record(transactionDirectory, identity, DateTimeOffset.UtcNow);
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

}
