// Debug-only, process-lifetime L00-C campaign controller. A ModSystem is
// session-scoped, while the campaign must cross DestroyGameSession. The short
// bootstrap lease is the only object that ever retains ICoreClientAPI; after a
// verified handoff, the controller retains only ScreenManager and laboratory root.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using ISRWorldGen.WorldgenProbe;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ISRWorldGen.L00C.Laboratory;

internal sealed class L00CProcessCampaignController
{
    private static readonly object Gate = new();
    private static readonly L00CProcessCampaignInstallTransaction<L00CProcessCampaignController> installTransaction = new();
    private static L00CManagerLeaseLifecycle? bootstrapLease;
    private static string? bootstrapRoot;
    private static L00CCampaignStorage? bootstrapCampaign;
    private static L00CLevelFinalizeSessionLease? bootstrapFinalizeLease;
    private readonly object screenManager;
    private readonly string root;
    private readonly L00CCampaignStorage campaign;
    private readonly string evidence;
    private L00CFixtureBootstrap? bootstrap;
    private L00CMenuActionLaboratoryHost? host;
    private readonly L00CLevelFinalizeGate levelFinalizeGate = new();
    private L00CLevelFinalizeSessionLease? currentSession;
    private bool pumpQueued;
    private bool terminal;
    private int sessionSignals;

    private L00CProcessCampaignController(object manager, L00CCampaignStorage campaignStorage, L00CLevelFinalizeSessionLease initialSession)
    {
        screenManager = manager; campaign = campaignStorage; root = campaign.LaboratoryRoot;
        evidence = campaign.EvidenceDirectory;
        bootstrap = new L00CFixtureBootstrap(campaign,
            new L00CNativeScenarioHostAdapter(new L00CProductionScenarioComposition(campaign)));
        levelFinalizeGate.AdoptSession(initialSession);
        currentSession = initialSession;
    }

    internal static L00CProcessCampaignInstallResult InstallOrSignal(ICoreClientAPI api, string laboratoryRoot)
    {
        RequireDebugLaboratory();
        if (api is null) throw new ArgumentNullException(nameof(api));
        string root = Path.GetFullPath(laboratoryRoot);
        L00CLevelFinalizeSessionLease finalizeLease = L00CLevelFinalizeGate.CreateSessionLease();
        lock (Gate)
        {
            if (installTransaction.TrySignalSameRoot(root, value => value.root, value => value.SignalSessionReady(finalizeLease)))
                return L00CProcessCampaignInstallResult.Succeeded(null, finalizeLease, "signalled");
            if (bootstrapLease is not null)
            {
                RequireSameRoot(bootstrapRoot!, root);
                bootstrapFinalizeLease?.Revoke();
                bootstrapFinalizeLease = finalizeLease;
                L00CManagerLeaseToken transferredToken = CreateBootstrapCancelToken(bootstrapLease, finalizeLease);
                return L00CProcessCampaignInstallResult.Succeeded(transferredToken, finalizeLease, "waiting");
            } // one retry listener/pump per process
            // The actual Vanilla menu discovery root.  This is intentionally
            // read-only configuration access: no dataPath override/seam exists.
            L00CCampaignStorage campaign = L00CCampaignStorage.Create(root, GamePaths.Saves);
            var seams = new VintageManagerLeaseSeams(api, campaign);
            var engine = new L00CManagerLeaseLifecycle(seams);
            bootstrapLease = engine; bootstrapRoot = root; bootstrapCampaign = campaign; bootstrapFinalizeLease = finalizeLease;
            L00CManagerLeaseToken token = CreateBootstrapCancelToken(engine, finalizeLease);
            seams.Set(engine, token);
            engine.Start();
            return engine.Terminal && !engine.Installed
                ? L00CProcessCampaignInstallResult.Refused("lease-terminal")
                : L00CProcessCampaignInstallResult.Succeeded(bootstrapLease is null ? null : token, finalizeLease, "installed-or-waiting");
        }
    }

    private static L00CManagerLeaseToken CreateBootstrapCancelToken(
        L00CManagerLeaseLifecycle engine,
        L00CLevelFinalizeSessionLease owner)
    {
        return new L00CManagerLeaseToken(() =>
        {
            lock (Gate)
            {
                // A prior ModSystem may outlive the session that superseded it.
                // Only the token matching both the active engine and the latest
                // finalize-session generation is allowed to cancel bootstrap.
                if (!ReferenceEquals(bootstrapLease, engine) || !ReferenceEquals(bootstrapFinalizeLease, owner))
                    return false;
                engine.Dispose();
                return true;
            }
        });
    }

    // The session ModSystem forwards IClientEventAPI.LevelFinalize here. The
    // process pump owns fixture state, so a disposed world cannot retain it.
    internal static void SignalLevelFinalize(L00CLevelFinalizeSignal? signal)
    {
        if (signal is null) return;
        lock (Gate)
        {
            if (installTransaction.Active is L00CProcessCampaignController active && !active.terminal)
            {
                if (!active.levelFinalizeGate.TryAccept(signal, out int fixtureSequence)) return;
                active.bootstrap?.SignalLevelFinalize(fixtureSequence);
                active.host?.SignalLevelFinalize(fixtureSequence);
                active.QueuePump();
            }
        }
    }

    internal static L00CNativeOpenReservation BeginNativeOpen(int fixtureSequence)
    {
        lock (Gate)
        {
            L00CProcessCampaignController active = installTransaction.Active
                ?? throw new InvalidOperationException("L00-C native open has no process campaign owner.");
            if (active.terminal) throw new InvalidOperationException("L00-C native open owner is terminal.");
            return active.levelFinalizeGate.BeginOpen(fixtureSequence);
        }
    }

    internal static void CompleteNativeOpen(L00CNativeOpenReservation reservation)
    {
        lock (Gate)
        {
            L00CProcessCampaignController active = installTransaction.Active
                ?? throw new InvalidOperationException("L00-C native open completion has no process campaign owner.");
            active.levelFinalizeGate.CompleteOpen(reservation);
        }
    }

    internal static bool AbortNativeOpen(L00CNativeOpenReservation reservation)
    {
        lock (Gate)
        {
            return installTransaction.Active is L00CProcessCampaignController active &&
                active.levelFinalizeGate.AbortOpen(reservation);
        }
    }

    internal static void RetireSession(L00CLevelFinalizeSessionLease session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        lock (Gate)
        {
            if (installTransaction.Active is L00CProcessCampaignController active)
            {
                active.levelFinalizeGate.RetireSession(session);
                if (ReferenceEquals(active.currentSession, session)) active.currentSession = null;
            }
            else
            {
                if (ReferenceEquals(bootstrapFinalizeLease, session)) bootstrapFinalizeLease = null;
                session.Revoke();
            }
        }
    }

    internal static void AbortSessionRegistration(L00CLevelFinalizeSessionLease session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        lock (Gate)
        {
            if (installTransaction.AbortIfSessionOwned(
                session,
                value => value.currentSession,
                value =>
                {
                    WriteLeaseReceipt(value.campaign, "session-registration-fault", "level-finalize-subscription");
                    value.UnregisterAndClearSingleton();
                })) return;
            if (ReferenceEquals(bootstrapFinalizeLease, session))
            {
                bootstrapLease?.Dispose();
                return;
            }
            session.Revoke();
        }
    }

    // Invoked at the ModSystem disposal boundary. This cannot hand off a campaign;
    // it only makes a pending session listener terminal and clears all references.

    private static bool TryInstallResolvedLocked(
        L00CCampaignStorage campaign,
        object manager,
        L00CLevelFinalizeSessionLease initialSession,
        string phase)
    {
        if (manager is null) { WriteLeaseReceipt(campaign, "install-fault", "null-manager"); return false; }
        if (initialSession is null || initialSession.Revoked)
        {
            WriteLeaseReceipt(campaign, "install-fault", "revoked-session-context");
            return false;
        }
        L00CProcessCampaignController? candidate = null;
        try
        {
            candidate = new L00CProcessCampaignController(manager, campaign, initialSession);
        }
        catch (Exception exception)
        {
            candidate?.UnregisterAndClearSingleton();
            WriteLeaseReceipt(campaign, L00CCampaignInstallFailure.ConstructionStatus, L00CCampaignInstallFailure.ConstructionDetail(phase, exception));
            return false;
        }
        try
        {
            candidate.SignalSessionReady();
            installTransaction.Install(candidate, value => value.QueuePump());
            return true;
        }
        catch (Exception exception)
        {
            candidate.UnregisterAndClearSingleton();
            WriteLeaseReceipt(campaign, L00CCampaignInstallFailure.PumpEnqueueStatus, L00CCampaignInstallFailure.PumpEnqueueDetail(phase, exception));
            return false;
        }
    }

    private static void RequireSameRoot(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C refuses a second laboratory root in the same client process.");
    }

    private void SignalSessionReady() { checked { sessionSignals++; } }
    private void SignalSessionReady(L00CLevelFinalizeSessionLease session) { levelFinalizeGate.AdoptSession(session); currentSession = session; SignalSessionReady(); }
    private void QueuePump() { if (!terminal && !pumpQueued) { pumpQueued = true; L00CMenuActionDriver.EnqueueMainThreadTask(Pump); } }

    private void Pump()
    {
        lock (Gate)
        {
            pumpQueued = false; installTransaction.PumpDequeued(this);
            if (terminal || !ReferenceEquals(installTransaction.Active, this)) return;
            try
            {
                if (sessionSignals == 0) { QueuePump(); return; }
                if (bootstrap is not null)
                {
                    if (bootstrap.TryAdvance(screenManager, out L00CMenuActionLaboratoryHost? completed)) { host = completed; bootstrap = null; }
                }
                else if (host is not null && host.TryAdvance(screenManager)) { Complete(); return; }
                QueuePump();
            }
            catch (Exception exception) { Fault(exception); }
        }
    }

    private void Complete() { WriteTerminal("complete", null); UnregisterAndClearSingleton(); }
    private void Fault(Exception exception) { WriteTerminal("refused", exception.Message); UnregisterAndClearSingleton(); }
    private void UnregisterAndClearSingleton() { terminal = true; levelFinalizeGate.Close(); currentSession = null; bootstrap = null; host = null; pumpQueued = false; installTransaction.Clear(this); }

    private void WriteTerminal(string status, string? detail)
    {
        Directory.CreateDirectory(evidence);
        string file = Path.Combine(evidence, "process-campaign-" + status + ".txt");
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.WriteLine("status=" + status); writer.WriteLine("sessionSignals=" + sessionSignals);
        if (detail is not null) writer.WriteLine("detail=" + detail);
    }

    private static void WriteLeaseReceipt(L00CCampaignStorage campaign, string status, string detail)
    {
        try
        {
            string directory = campaign.EvidenceDirectory; Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + status + ".txt");
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.WriteLine("schema=l00c-manager-availability-v1"); writer.WriteLine("status=" + status); writer.WriteLine("detail=" + detail);
        }
        catch { /* evidence failure must not revive a refused campaign */ }
    }

    // Production adapter: this is the sole pre-handoff owner of the client API.
    private sealed class VintageManagerLeaseSeams : L00CManagerLeaseAdapter
    {
        private ICoreClientAPI? api;
        private readonly L00CCampaignStorage campaign;
        private L00CManagerLeaseLifecycle? engine;
        private L00CManagerLeaseToken? token;
        internal VintageManagerLeaseSeams(ICoreClientAPI clientApi, L00CCampaignStorage campaignStorage) { api = clientApi; campaign = campaignStorage; }
        protected override DateTimeOffset ReadUtcNow() => DateTimeOffset.UtcNow;
        internal void Set(L00CManagerLeaseLifecycle value, L00CManagerLeaseToken leaseToken) { engine = value; token = leaseToken; }
        protected override L00CManagerResolutionStatus ResolveAcquired(out object? manager)
        {
            ICoreClientAPI retained = api ?? throw new InvalidOperationException("L00-C API lease is terminal.");
            L00CManagerResolution result = L00CMenuActionDriver.ResolveScreenManagerFromClientApi(retained);
            manager = result.ScreenManager; return result.Status;
        }
        protected override long RegisterAcquired(Action callback)
        {
            ICoreClientAPI retained = api ?? throw new InvalidOperationException("L00-C API lease is terminal.");
            return retained.Event.RegisterGameTickListener(_ => callback(), 50);
        }
        protected override void UnregisterAcquired(long listenerId)
        {
            ICoreClientAPI retained = api ?? throw new InvalidOperationException("L00-C API lease is terminal.");
            retained.Event.UnregisterGameTickListener(listenerId);
        }
        protected override void InstallAcquired(object manager)
        {
            lock (Gate)
            {
                if (!ReferenceEquals(bootstrapLease, engine) || !ReferenceEquals(bootstrapCampaign, campaign))
                    throw new InvalidOperationException("L00-C controller install lost its acquired bootstrap ownership.");
                L00CLevelFinalizeSessionLease initialSession = bootstrapFinalizeLease
                    ?? throw new InvalidOperationException("L00-C controller install has no client-session finalize lease.");
                if (initialSession.Revoked)
                    throw new InvalidOperationException("L00-C controller install refuses a revoked client-session context.");
                if (!TryInstallResolvedLocked(campaign, manager, initialSession, "lease"))
                    throw new InvalidOperationException("L00-C controller install refused.");
                if (!ReferenceEquals(bootstrapFinalizeLease, initialSession))
                    throw new InvalidOperationException("L00-C controller install context changed during atomic publication.");
                bootstrapFinalizeLease = null;
            }
        }
        protected override void WriteReceipt(string status, string detail) => WriteLeaseReceipt(campaign, status, detail);
        protected override void ReleaseAcquired()
        {
            api = null; token?.Complete(); token = null;
            lock (Gate)
            {
                if (ReferenceEquals(bootstrapLease, engine))
                {
                    bootstrapLease = null; bootstrapRoot = null; bootstrapCampaign = null;
                    bootstrapFinalizeLease?.Revoke(); bootstrapFinalizeLease = null;
                }
            }
            engine = null;
        }
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

internal sealed class L00CProcessCampaignInstallResult
{
    private L00CProcessCampaignInstallResult(bool accepted, L00CManagerLeaseToken? lease, L00CLevelFinalizeSessionLease? finalizeLease, string diagnostic)
    { Accepted = accepted; Lease = lease; FinalizeLease = finalizeLease; Diagnostic = diagnostic; }
    internal bool Accepted { get; }
    internal L00CManagerLeaseToken? Lease { get; }
    internal L00CLevelFinalizeSessionLease? FinalizeLease { get; }
    internal string Diagnostic { get; }
    internal static L00CProcessCampaignInstallResult Succeeded(L00CManagerLeaseToken? lease, L00CLevelFinalizeSessionLease finalizeLease, string diagnostic) => new(true, lease, finalizeLease, diagnostic);
    internal static L00CProcessCampaignInstallResult Refused(string diagnostic) => new(false, null, null, diagnostic);
}
