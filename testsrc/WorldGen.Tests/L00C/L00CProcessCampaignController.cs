// Debug-only, process-lifetime L00-C campaign controller. A ModSystem is
// session-scoped, while the campaign must cross DestroyGameSession. The short
// bootstrap lease is the only object that ever retains ICoreClientAPI; after a
// verified handoff, the controller retains only ScreenManager and laboratory root.
#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ISRWorldGen.L00C.Laboratory;

/// <summary>Abort capability retained by a session ModSystem; it never exposes an API.</summary>
internal sealed class L00CManagerLeaseToken
{
    private Action? cancel;
    internal L00CManagerLeaseToken(Action cancelAction) { cancel = cancelAction; }
    internal void Cancel() { Action? action = cancel; cancel = null; action?.Invoke(); }
    internal void Complete() { cancel = null; }
}

internal sealed class L00CProcessCampaignController
{
    private static readonly object Gate = new();
    private static readonly L00CProcessCampaignInstallTransaction<L00CProcessCampaignController> installTransaction = new();
    private static L00CManagerLeaseLifecycle? bootstrapLease;
    private static string? bootstrapRoot;
    private static L00CCampaignStorage? bootstrapCampaign;
    private readonly object screenManager;
    private readonly string root;
    private readonly L00CCampaignStorage campaign;
    private readonly string evidence;
    private L00CFixtureBootstrap? bootstrap;
    private L00CMenuActionLaboratoryHost? host;
    private bool pumpQueued;
    private bool terminal;
    private int sessionSignals;

    private L00CProcessCampaignController(object manager, L00CCampaignStorage campaignStorage)
    {
        screenManager = manager; campaign = campaignStorage; root = campaign.LaboratoryRoot;
        evidence = campaign.EvidenceDirectory;
        bootstrap = new L00CFixtureBootstrap(campaign);
    }

    internal static L00CProcessCampaignInstallResult InstallOrSignal(ICoreClientAPI api, string laboratoryRoot)
    {
        RequireDebugLaboratory();
        if (api is null) throw new ArgumentNullException(nameof(api));
        string root = Path.GetFullPath(laboratoryRoot);
        lock (Gate)
        {
            if (installTransaction.TrySignalSameRoot(root, value => value.root, value => value.SignalSessionReady())) return L00CProcessCampaignInstallResult.Succeeded(null, "signalled");
            if (bootstrapLease is not null) { RequireSameRoot(bootstrapRoot!, root); return L00CProcessCampaignInstallResult.Succeeded(null, "waiting"); } // one retry listener/pump per process
            // The actual Vanilla menu discovery root.  This is intentionally
            // read-only configuration access: no dataPath override/seam exists.
            L00CCampaignStorage campaign = L00CCampaignStorage.Create(root, GamePaths.Saves);
            var seams = new VintageManagerLeaseSeams(api, campaign);
            var engine = new L00CManagerLeaseLifecycle(seams);
            bootstrapLease = engine; bootstrapRoot = root; bootstrapCampaign = campaign;
            L00CManagerLeaseToken token = new(engine.Dispose);
            seams.Set(engine, token);
            engine.Start();
            return engine.Terminal && !engine.Installed
                ? L00CProcessCampaignInstallResult.Refused("lease-terminal")
                : L00CProcessCampaignInstallResult.Succeeded(bootstrapLease is null ? null : token, "installed-or-waiting");
        }
    }

    // The session ModSystem forwards IClientEventAPI.LevelFinalize here. The
    // process pump owns fixture state, so a disposed world cannot retain it.
    internal static void SignalLevelFinalize()
    {
        lock (Gate)
        {
            if (installTransaction.Active is L00CProcessCampaignController active && !active.terminal)
            {
                active.bootstrap?.SignalLevelFinalize();
                active.QueuePump();
            }
        }
    }

    // Invoked at the ModSystem disposal boundary. This cannot hand off a campaign;
    // it only makes a pending session listener terminal and clears all references.

    private static bool TryInstallResolvedLocked(L00CCampaignStorage campaign, object manager, string phase)
    {
        if (manager is null) { WriteLeaseReceipt(campaign, "install-fault", "null-manager"); return false; }
        L00CProcessCampaignController? candidate = null;
        try
        {
            candidate = new L00CProcessCampaignController(manager, campaign);
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
    private void UnregisterAndClearSingleton() { terminal = true; bootstrap = null; host = null; pumpQueued = false; installTransaction.Clear(this); }

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
    private sealed class VintageManagerLeaseSeams : IL00CManagerLeaseSeams
    {
        private ICoreClientAPI? api;
        private readonly L00CCampaignStorage campaign;
        private L00CManagerLeaseLifecycle? engine;
        private L00CManagerLeaseToken? token;
        internal VintageManagerLeaseSeams(ICoreClientAPI clientApi, L00CCampaignStorage campaignStorage) { api = clientApi; campaign = campaignStorage; }
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        internal void Set(L00CManagerLeaseLifecycle value, L00CManagerLeaseToken leaseToken) { engine = value; token = leaseToken; }
        public L00CManagerResolutionStatus Resolve(out object? manager)
        {
            if (api is null) { manager = null; return L00CManagerResolutionStatus.ApiTypeMismatch; }
            L00CManagerResolution result = L00CMenuActionDriver.ResolveScreenManagerFromClientApi(api);
            manager = result.ScreenManager; return result.Status;
        }
        public long Register(Action callback)
        {
            ICoreClientAPI retained = api ?? throw new InvalidOperationException("L00-C API lease is terminal.");
            return retained.Event.RegisterGameTickListener(_ => callback(), 50);
        }
        public void Unregister(long listenerId)
        {
            ICoreClientAPI retained = api ?? throw new InvalidOperationException("L00-C API lease is terminal.");
            retained.Event.UnregisterGameTickListener(listenerId);
        }
        public void Install(object manager)
        {
            lock (Gate) { if (!TryInstallResolvedLocked(campaign, manager, "lease")) throw new InvalidOperationException("L00-C controller install refused."); }
        }
        public void Receipt(string status, string detail) => WriteLeaseReceipt(campaign, status, detail);
        public void Release()
        {
            api = null; token?.Complete(); token = null;
            lock (Gate) { if (ReferenceEquals(bootstrapLease, engine)) { bootstrapLease = null; bootstrapRoot = null; bootstrapCampaign = null; } }
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
    private L00CProcessCampaignInstallResult(bool accepted, L00CManagerLeaseToken? lease, string diagnostic) { Accepted = accepted; Lease = lease; Diagnostic = diagnostic; }
    internal bool Accepted { get; }
    internal L00CManagerLeaseToken? Lease { get; }
    internal string Diagnostic { get; }
    internal static L00CProcessCampaignInstallResult Succeeded(L00CManagerLeaseToken? lease, string diagnostic) => new(true, lease, diagnostic);
    internal static L00CProcessCampaignInstallResult Refused(string diagnostic) => new(false, null, diagnostic);
}
