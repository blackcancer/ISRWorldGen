// Debug-only, process-lifetime L00-C campaign controller. A ModSystem is
// session-scoped, while the campaign must cross DestroyGameSession. The short
// bootstrap lease is the only object that ever retains ICoreClientAPI; after a
// verified handoff, the controller retains only ScreenManager and laboratory root.
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
    private static L00CManagerBootstrapLease? bootstrapLease;
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
        screenManager = manager; root = laboratoryRoot;
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
            if (active is not null) { RequireSameRoot(active.root, root); active.SignalSessionReady(); return; }
            if (bootstrapLease is not null) { RequireSameRoot(bootstrapLease.Root, root); return; }
            L00CManagerResolution resolution;
            try { resolution = L00CMenuActionDriver.ResolveScreenManagerFromClientApi(api); }
            catch (Exception exception) { WriteLeaseReceipt(root, "resolver-fault", exception.GetType().Name); return; }
            if (resolution.Status == L00CManagerResolutionStatus.Ready) { InstallResolvedLocked(root, resolution.ScreenManager!); return; }
            if (resolution.Status == L00CManagerResolutionStatus.ApiTypeMismatch) { WriteLeaseReceipt(root, "api-type-mismatch", resolution.Status.ToString()); return; }
            bootstrapLease = new L00CManagerBootstrapLease(api, root);
            try { bootstrapLease.Start(); WriteLeaseReceipt(root, "waiting", resolution.Status.ToString()); }
            catch (Exception exception)
            {
                bootstrapLease.Stop(); bootstrapLease = null;
                WriteLeaseReceipt(root, "listener-register-fault", exception.GetType().Name);
            }
        }
    }

    // Invoked at the ModSystem disposal boundary. This cannot hand off a campaign;
    // it only makes a pending session listener terminal and clears all references.
    internal static void DisposeSession(ICoreClientAPI api)
    {
        lock (Gate)
        {
            if (bootstrapLease is null || !bootstrapLease.Owns(api)) return;
            string root = bootstrapLease.Root;
            bool unregistered = bootstrapLease.Stop(); bootstrapLease = null;
            WriteLeaseReceipt(root, unregistered ? "disposed" : "dispose-unregister-fault", "session-dispose");
        }
    }

    private static void ResolveLeaseTick(L00CManagerBootstrapLease lease)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(bootstrapLease, lease) || lease.IsTerminal) return; // late callback
            if (!lease.TryCountAttempt(out bool expired)) return;
            if (expired) { TerminalizeLeaseLocked(lease, "timeout", "attempt-or-deadline"); return; }
            L00CManagerResolution resolution;
            try { resolution = L00CMenuActionDriver.ResolveScreenManagerFromClientApi(lease.Api!); }
            catch (Exception exception) { TerminalizeLeaseLocked(lease, "resolver-fault", exception.GetType().Name); return; }
            if (resolution.Status == L00CManagerResolutionStatus.Ready)
            {
                string root = lease.Root;
                // Atomic handoff rule: unregistration and all API/delegate clearing
                // complete before the controller is created. Failure means no campaign.
                if (!lease.Stop()) { bootstrapLease = null; WriteLeaseReceipt(root, "handoff-unregister-fault", "no-controller-installed"); return; }
                bootstrapLease = null;
                InstallResolvedLocked(root, resolution.ScreenManager!);
                WriteLeaseReceipt(root, "ready", "listener-unregistered-before-controller");
                return;
            }
            if (resolution.Status == L00CManagerResolutionStatus.ApiTypeMismatch)
                TerminalizeLeaseLocked(lease, "api-type-mismatch", resolution.Status.ToString());
        }
    }

    private static void TerminalizeLeaseLocked(L00CManagerBootstrapLease lease, string status, string detail)
    {
        string root = lease.Root;
        bool unregistered = lease.Stop(); bootstrapLease = null;
        WriteLeaseReceipt(root, unregistered ? status : status + "-unregister-fault", detail);
    }

    private static void InstallResolvedLocked(string laboratoryRoot, object manager)
    {
        if (manager is null) throw new ArgumentNullException(nameof(manager));
        active = new L00CProcessCampaignController(manager, laboratoryRoot);
        active.SignalSessionReady(); active.QueuePump();
    }

    private static void RequireSameRoot(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C refuses a second laboratory root in the same client process.");
    }

    private void SignalSessionReady() { checked { sessionSignals++; } }
    private void QueuePump() { if (!terminal && !pumpQueued) { pumpQueued = true; L00CMenuActionDriver.EnqueueMainThreadTask(Pump); } }

    private void Pump()
    {
        lock (Gate)
        {
            pumpQueued = false;
            if (terminal || !ReferenceEquals(active, this)) return;
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
    private void UnregisterAndClearSingleton() { terminal = true; bootstrap = null; host = null; pumpQueued = false; if (ReferenceEquals(active, this)) active = null; }

    private void WriteTerminal(string status, string? detail)
    {
        Directory.CreateDirectory(evidence);
        string file = Path.Combine(evidence, "process-campaign-" + status + ".txt");
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.WriteLine("status=" + status); writer.WriteLine("sessionSignals=" + sessionSignals);
        if (detail is not null) writer.WriteLine("detail=" + detail);
    }

    private static void WriteLeaseReceipt(string laboratoryRoot, string status, string detail)
    {
        try
        {
            string directory = Path.Combine(laboratoryRoot, "manager-availability-evidence"); Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + status + ".txt");
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.WriteLine("schema=l00c-manager-availability-v1"); writer.WriteLine("status=" + status); writer.WriteLine("detail=" + detail);
        }
        catch { /* evidence failure must not revive a refused campaign */ }
    }

    private sealed class L00CManagerBootstrapLease : IDisposable
    {
        private const int MaximumAttempts = 600; // 30 seconds at 50 ms
        private readonly DateTimeOffset deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30);
        private ICoreClientAPI? api;
        private Action<float>? tick;
        private long listenerId;
        private int attempts;
        private bool terminal;

        internal L00CManagerBootstrapLease(ICoreClientAPI clientApi, string laboratoryRoot) { api = clientApi; Root = laboratoryRoot; }
        internal string Root { get; }
        internal ICoreClientAPI? Api => api;
        internal bool IsTerminal => terminal;
        internal bool Owns(ICoreClientAPI candidate) => ReferenceEquals(api, candidate);
        internal void Start()
        {
            if (terminal || api is null) throw new InvalidOperationException("L00-C bootstrap lease is already terminal.");
            tick = OnTick; listenerId = api.Event.RegisterGameTickListener(tick, 50);
        }
        private void OnTick(float _) => ResolveLeaseTick(this);
        internal bool TryCountAttempt(out bool expired)
        {
            if (terminal) { expired = true; return false; }
            attempts++; expired = attempts > MaximumAttempts || DateTimeOffset.UtcNow >= deadlineUtc; return true;
        }
        // References are cleared in finally even if Vintage throws while unregistering.
        internal bool Stop()
        {
            if (terminal) return true;
            terminal = true; ICoreClientAPI? retainedApi = api; long retainedId = listenerId;
            try { if (retainedApi is not null && retainedId != 0) retainedApi.Event.UnregisterGameTickListener(retainedId); return true; }
            catch { return false; }
            finally { listenerId = 0; tick = null; api = null; }
        }
        void IDisposable.Dispose() { _ = Stop(); }
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
