// Debug-only, process-lifetime L00-C campaign controller.  A ModSystem is
// session-scoped in Vintage Story, so its ICoreClientAPI listener cannot drive
// a return-to-menu/reopen campaign.  ScreenManager's static main-thread queue
// is drained by OnNewFrame and is the audited lifetime boundary instead.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Vintagestory.API.Client;

namespace ISRWorldGen.L00C.Laboratory;

internal sealed class L00CProcessCampaignController
{
    private static readonly object Gate = new();
    private static L00CProcessCampaignController? active;

    private readonly object screenManager;
    private readonly string root;
    private readonly string evidence;
    private L00CFixtureBootstrap? bootstrap;
    private L00CMenuActionLaboratoryHost? host;
    private bool pumpQueued;
    private bool terminal;
    private int sessionSignals;

    private L00CProcessCampaignController(object manager, string laboratoryRoot)
    {
        screenManager = manager;
        root = laboratoryRoot;
        evidence = Path.Combine(root, "menu-action-evidence", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
        bootstrap = new L00CFixtureBootstrap(root, evidence);
    }

    internal static void InstallOrSignal(ICoreClientAPI api, string laboratoryRoot)
    {
        RequireDebugLaboratory();
        if (api is null) throw new ArgumentNullException(nameof(api));
        string root = Path.GetFullPath(laboratoryRoot);
        lock (Gate)
        {
            if (active is not null)
            {
                if (!string.Equals(active.root, root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("L00-C refuses a second laboratory root in the same client process.");
                active.SignalSessionReady();
                return;
            }

            if (!L00CMenuActionDriver.TryFindScreenManagerFromClientApi(api, out object? manager) || manager is null)
                throw new InvalidOperationException("L00-C cannot resolve the audited ScreenManager during StartClientSide.");
            active = new L00CProcessCampaignController(manager, root);
            active.SignalSessionReady();
            active.QueuePump();
        }
    }

    // Called by any later StartClientSide after a DestroyGameSession.  It is a
    // signal only: deliberately no API, world, client, or session is retained.
    private void SignalSessionReady()
    {
        checked { sessionSignals++; }
    }

    private void QueuePump()
    {
        if (terminal || pumpQueued) return;
        pumpQueued = true;
        L00CMenuActionDriver.EnqueueMainThreadTask(Pump);
    }

    private void Pump()
    {
        lock (Gate)
        {
            pumpQueued = false;
            if (terminal || !ReferenceEquals(active, this)) return;
            try
            {
                // The fresh StartClientSide signal is a readiness barrier.  The
                // controller additionally waits for concrete audited UI/session
                // objects; it never guesses a fixed number of frames.
                if (sessionSignals == 0) { QueuePump(); return; }
                if (bootstrap is not null)
                {
                    if (bootstrap.TryAdvance(screenManager, out L00CMenuActionLaboratoryHost? completed))
                    {
                        host = completed;
                        bootstrap = null;
                    }
                }
                else if (host is not null && host.TryAdvance(screenManager))
                {
                    Complete();
                    return;
                }
                QueuePump();
            }
            catch (Exception exception)
            {
                Fault(exception);
            }
        }
    }

    private void Complete()
    {
        WriteTerminal("complete", null);
        UnregisterAndClearSingleton();
    }

    private void Fault(Exception exception)
    {
        WriteTerminal("refused", exception.Message);
        UnregisterAndClearSingleton();
    }

    // Queue callbacks cannot be removed individually.  "Unregister" here means
    // no future callback is enqueued, all mutable state is released, and the
    // process singleton is cleared before the current callback returns.
    private void UnregisterAndClearSingleton()
    {
        terminal = true;
        bootstrap = null;
        host = null;
        pumpQueued = false;
        if (ReferenceEquals(active, this)) active = null;
    }

    private void WriteTerminal(string status, string? detail)
    {
        Directory.CreateDirectory(evidence);
        string file = Path.Combine(evidence, "process-campaign-" + status + ".txt");
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write("status="); writer.WriteLine(status);
        writer.Write("sessionSignals="); writer.WriteLine(sessionSignals);
        if (detail is not null) { writer.Write("detail="); writer.WriteLine(detail); }
    }

    private static void RequireDebugLaboratory()
    {
#if !DEBUG
        throw new InvalidOperationException("L00-C process campaign controller is disabled outside a Debug build.");
#else
        if (!Debugger.IsAttached || !string.Equals(Environment.GetEnvironmentVariable("ISR_L00C_LAB"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C process campaign controller requires Debugger.IsAttached and ISR_L00C_LAB=1.");
#endif
    }
}
