#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CManagerLeaseLifecycleTests
{
    public static int Main()
    {
        ImmediateReady(); DelayedReadyAndOrder(); TimeoutAndLimit(); ResolverAndMismatch(); CancelledTransferAndLateTick(); TerminalizeUnregisterFailure(); RegisterUnregisterAndEnqueueFailures(); DuplicateStart(); InstallTransactionRollback(); SessionCancellationOwnershipTransfers(); RegistrationFailureCompensatesImmediateAndLateHandoff(); LaunchAcquisitionIsIdempotentAndFailClosed();
        return 0;
    }
    private static void ImmediateReady()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.Ready); var l = new L00CManagerLeaseLifecycle(s); l.Start();
        Check(l.Installed && !l.ListenerActive && s.Registers == 0 && s.Installs == 1 && l.Receipts[0] == "ready-immediate" && s.Releases == 1 && s.InstallSawAcquired && !s.LeaseAcquired);
    }
    private static void DelayedReadyAndOrder()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.ManagerUnavailable, L00CManagerResolutionStatus.Ready); var l = new L00CManagerLeaseLifecycle(s); l.Start(); s.FireRegisteredCallback();
        Check(l.Installed && s.Events.SequenceEqual(new[] { "register", "unregister", "install", "release" }) && l.Receipts.Contains("ready") && s.Releases == 1 && s.InstallSawAcquired && !s.LeaseAcquired && s.ActiveCallback is null && s.InstalledManager == s.Manager);
    }
    private static void TimeoutAndLimit()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.GameUnavailable); var l = new L00CManagerLeaseLifecycle(s); l.Start(); s.Now = s.Now.AddSeconds(31); s.FireRegisteredCallback(); Check(l.Terminal && l.Receipts.Contains("timeout"));
        s = new StrictAdapter(L00CManagerResolutionStatus.GameUnavailable); l = new L00CManagerLeaseLifecycle(s); l.Start(); for (int i = 0; i < 601; i++) s.FireRegisteredCallback(); Check(l.Terminal && l.Attempts == 601);
    }
    private static void ResolverAndMismatch()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.GameUnavailable) { ThrowResolveOn = 2 }; var l = new L00CManagerLeaseLifecycle(s); l.Start(); s.FireRegisteredCallback(); Check(l.Terminal && l.Receipts.Contains("resolver-fault"));
        s = new StrictAdapter(L00CManagerResolutionStatus.ApiTypeMismatch); l = new L00CManagerLeaseLifecycle(s); l.Start(); Check(l.Terminal && !l.ListenerActive && l.Receipts.Contains("api-type-mismatch"));
    }
    private static void CancelledTransferAndLateTick()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.RunningScreenUnavailable); var l = new L00CManagerLeaseLifecycle(s); l.Start(); Action late = s.LastCallback ?? throw new InvalidOperationException(); l.Dispose(); int calls = s.Resolves; late(); Check(l.Terminal && !l.ListenerActive && s.ActiveCallback is null && s.Resolves == calls && s.Installs == 0 && s.Releases == 1 && !s.LeaseAcquired && l.Receipts.Contains("disposed"));
    }
    private static void TerminalizeUnregisterFailure()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.ManagerUnavailable) { ThrowUnregister = true };
        var l = new L00CManagerLeaseLifecycle(s); l.Start(); l.Dispose(); int resolves = s.Resolves; s.FireRegisteredCallback();
        Check(l.Terminal && !l.ListenerActive && !l.Installed && s.Installs == 0 && s.Releases == 1 && s.Resolves == resolves && l.Receipts.Contains("disposed-unregister-fault"));
    }
    private static void RegisterUnregisterAndEnqueueFailures()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.ManagerUnavailable) { ThrowRegister = true }; var l = new L00CManagerLeaseLifecycle(s); l.Start(); Check(l.Terminal && l.Receipts.Contains("listener-register-fault") && !s.LeaseAcquired);
        s = new StrictAdapter(L00CManagerResolutionStatus.ManagerUnavailable, L00CManagerResolutionStatus.Ready) { ThrowUnregister = true }; l = new L00CManagerLeaseLifecycle(s); l.Start(); s.FireRegisteredCallback(); Check(l.Terminal && !l.Installed && !l.ListenerActive && l.Receipts.Contains("handoff-unregister-fault") && s.Releases == 1 && s.Installs == 0 && !s.LeaseAcquired);
        s = new StrictAdapter(L00CManagerResolutionStatus.Ready) { ThrowInstall = true }; l = new L00CManagerLeaseLifecycle(s); l.Start(); Check(l.Terminal && !l.Installed && l.Receipts.Contains("install-pump-fault") && s.InstallSawAcquired && s.Releases == 1 && !s.LeaseAcquired);
    }
    private static void DuplicateStart()
    {
        var s = new StrictAdapter(L00CManagerResolutionStatus.ManagerUnavailable); var l = new L00CManagerLeaseLifecycle(s); l.Start(); l.Start(); Check(s.Registers == 1 && s.Installs == 0);
    }
    private static void InstallTransactionRollback()
    {
        var transaction = new L00CProcessCampaignInstallTransaction<object>();
        object first = new object(); object second = new object(); object third = new object();
        try { transaction.Install(first, _ => throw new InvalidOperationException()); throw new InvalidOperationException("expected enqueue failure"); }
        catch (InvalidOperationException) { }
        Check(transaction.Active is null && !transaction.PumpQueued);
        transaction.Install(second, _ => { });
        Check(ReferenceEquals(transaction.Active, second) && transaction.PumpQueued);
        try { transaction.Install(third, _ => { }); throw new InvalidOperationException("expected active conflict"); }
        catch (InvalidOperationException) { }
        transaction.Clear(second);
        transaction.Install(third, _ => { });
        Check(ReferenceEquals(transaction.Active, third) && transaction.PumpQueued);
        bool conflictObserved = false; object? signalled = null;
        try { transaction.TrySignalSameRoot("other-root", _ => "primary-root", value => signalled = value); }
        catch (InvalidOperationException) { conflictObserved = true; }
        Check(conflictObserved && ReferenceEquals(transaction.Active, third) && signalled is null);
        Check(transaction.TrySignalSameRoot("primary-root", _ => "primary-root", value => signalled = value) && ReferenceEquals(signalled, third));
        transaction.Clear(third);
        Check(transaction.Active is null && !transaction.PumpQueued);
    }

    private static void SessionCancellationOwnershipTransfers()
    {
        // Models two session-scoped ModSystems retaining tokens while the
        // process-scoped manager listener remains pending.  The token predicate
        // is the same engine+session-generation comparison as production.
        object engine = new(); object sessionA = new(); object sessionB = new();
        object? activeEngine = engine; object? activeSession = sessionA; int cancellations = 0;
        bool listenerActive = true; bool apiRetained = true;
        L00CManagerLeaseToken tokenA = TokenFor(engine, sessionA, CancelCurrent);
        activeSession = sessionB;
        L00CManagerLeaseToken tokenB = TokenFor(engine, sessionB, CancelCurrent);

        // Old A disposed first: its token is stale and B still owns bootstrap.
        DisposeModSystem(tokenA, sessionA);
        Check(cancellations == 0 && listenerActive && apiRetained && ReferenceEquals(activeEngine, engine) && ReferenceEquals(activeSession, sessionB));
        DisposeModSystem(tokenB, sessionB);
        Check(cancellations == 1 && !listenerActive && !apiRetained && activeEngine is null && activeSession is null);

        // Current B disposed first: it cancels exactly once; late A is inert.
        activeEngine = engine; activeSession = sessionA; cancellations = 0; listenerActive = true; apiRetained = true;
        tokenA = TokenFor(engine, sessionA, CancelCurrent);
        activeSession = sessionB;
        tokenB = TokenFor(engine, sessionB, CancelCurrent);
        DisposeModSystem(tokenB, sessionB); DisposeModSystem(tokenA, sessionA); tokenB.Cancel();
        Check(cancellations == 1 && !listenerActive && !apiRetained && activeEngine is null && activeSession is null);

        void CancelCurrent()
        {
            cancellations++; listenerActive = false; apiRetained = false; activeEngine = null; activeSession = null;
        }

        void DisposeModSystem(L00CManagerLeaseToken token, object session)
        {
            // Exact L00CMenuActionLabModSystem.Dispose ordering: unregister its
            // callback, cancel while ownership is still observable, then retire.
            token.Cancel();
            if (ReferenceEquals(activeSession, session)) activeSession = null;
        }

        L00CManagerLeaseToken TokenFor(object expectedEngine, object expectedSession, Action cancel)
        {
            return new L00CManagerLeaseToken(() =>
            {
                if (!ReferenceEquals(activeEngine, expectedEngine) || !ReferenceEquals(activeSession, expectedSession)) return false;
                cancel();
                return true;
            });
        }
    }

    private static void RegistrationFailureCompensatesImmediateAndLateHandoff()
    {
        RegistrationFailureCompensates(delayed: false);
        RegistrationFailureCompensates(delayed: true);
    }

    private static void RegistrationFailureCompensates(bool delayed)
    {
        object session = new();
        var transaction = new L00CProcessCampaignInstallTransaction<RegistrationCandidate>();
        var candidate = new RegistrationCandidate(transaction, session);
        StrictAdapter adapter = delayed
            ? new StrictAdapter(L00CManagerResolutionStatus.ManagerUnavailable, L00CManagerResolutionStatus.Ready)
            : new StrictAdapter(L00CManagerResolutionStatus.Ready);
        adapter.InstallAction = _ => transaction.Install(candidate, value => value.QueuePump());
        var lifecycle = new L00CManagerLeaseLifecycle(adapter);
        lifecycle.Start();
        if (delayed) adapter.FireRegisteredCallback();
        Check(lifecycle.Installed && !lifecycle.ListenerActive && !adapter.LeaseAcquired &&
            ReferenceEquals(transaction.Active, candidate) && transaction.PumpQueued && candidate.GateOpen && candidate.ReferencesRetained);

        bool aborted = transaction.AbortIfSessionOwned(session, value => value.Session, value => value.Abort());
        Check(aborted && transaction.Active is null && !transaction.PumpQueued && !candidate.GateOpen && !candidate.ReferencesRetained);
    }

    private static void LaunchAcquisitionIsIdempotentAndFailClosed()
    {
        string directory = Path.Combine(Path.GetTempPath(), "l00c-f5-acquisition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            L00CF5LaunchIdentity identity = LaunchIdentity(directory);
            string receipt = Path.Combine(directory, "child-acquisition.json");
            L00CF5LaunchAcquisition.Record(directory, identity, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
            byte[] original = File.ReadAllBytes(receipt);
            DateTime fixedWrite = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(receipt, fixedWrite);

            // The second StartClientSide path reaches this same recorder.  It
            // must validate and return without replacing even one byte.
            L00CF5LaunchAcquisition.Record(directory, identity, new DateTimeOffset(2040, 2, 2, 0, 0, 0, TimeSpan.Zero));
            Check(original.SequenceEqual(File.ReadAllBytes(receipt)) && File.GetLastWriteTimeUtc(receipt) == fixedWrite);

            ReplayMustRefuseWithoutMutation(directory, receipt, original, fixedWrite, LaunchIdentity(directory, processId: 4002));
            ReplayMustRefuseWithoutMutation(directory, receipt, original, fixedWrite, LaunchIdentity(directory, processStartUtc: "2030-01-01T00:00:01.0000000Z"));
            ReplayMustRefuseWithoutMutation(directory, receipt, original, fixedWrite, LaunchIdentity(directory, transactionId: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
            ReplayMustRefuseWithoutMutation(directory, receipt, original, fixedWrite, LaunchIdentity(directory, nonce: new string('B', 64)));
            ReplayMustRefuseWithoutMutation(directory, receipt, original, fixedWrite, LaunchIdentity(directory, arguments: new[] { "--openWorld", "other-world" }));
            ReplayMustRefuseWithoutMutation(directory, receipt, original, fixedWrite, LaunchIdentity(directory, commandLine: "game.exe --different-provenance"));

            string tampered = Encoding.UTF8.GetString(original).Replace("CHILD_ACQUIRED", "CHILD_ACQUIREX");
            File.WriteAllText(receipt, tampered, new UTF8Encoding(false));
            File.SetLastWriteTimeUtc(receipt, fixedWrite);
            byte[] tamperedBytes = File.ReadAllBytes(receipt);
            ReplayMustRefuseWithoutMutation(directory, receipt, tamperedBytes, fixedWrite, identity);

            File.WriteAllBytes(receipt, original);
            File.SetLastWriteTimeUtc(receipt, fixedWrite);
            File.WriteAllText(receipt + ".publishing", "unresolved", new UTF8Encoding(false));
            ReplayMustRefuseWithoutMutation(directory, receipt, original, fixedWrite, identity);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void ReplayMustRefuseWithoutMutation(string directory, string receipt, byte[] expectedBytes, DateTime expectedWrite, L00CF5LaunchIdentity identity)
    {
        bool refused = false;
        try { L00CF5LaunchAcquisition.Record(directory, identity, DateTimeOffset.UtcNow); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused && expectedBytes.SequenceEqual(File.ReadAllBytes(receipt)) && File.GetLastWriteTimeUtc(receipt) == expectedWrite);
    }

    private static L00CF5LaunchIdentity LaunchIdentity(
        string directory,
        string transactionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string? nonce = null,
        int processId = 4001,
        string processStartUtc = "2030-01-01T00:00:00.0000000Z",
        string? commandLine = null,
        IReadOnlyList<string>? arguments = null)
    {
        return new L00CF5LaunchIdentity(
            transactionId,
            nonce ?? new string('A', 64),
            directory,
            Path.Combine(directory, "laboratory"),
            Path.Combine(directory, "ISRWorldGen.sln"),
            3001,
            processId,
            processStartUtc,
            true,
            Path.Combine(directory, "Vintagestory.exe"),
            commandLine ?? "game.exe --openWorld ISRWorldGen_L00C_BOOTSTRAP",
            arguments ?? new[] { "--openWorld", "ISRWorldGen_L00C_BOOTSTRAP" });
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("L00-C lease simulation failed."); }
    private sealed class StrictAdapter : L00CManagerLeaseAdapter
    {
        private readonly Queue<L00CManagerResolutionStatus> results;
        internal StrictAdapter(params L00CManagerResolutionStatus[] values) { results = new Queue<L00CManagerResolutionStatus>(values); Now = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero); }
        internal DateTimeOffset Now { get; set; }
        internal int Registers; internal int Installs; internal int Releases; internal int Resolves; internal int ThrowResolveOn; internal bool ThrowRegister; internal bool ThrowUnregister; internal bool ThrowInstall; internal bool InstallSawAcquired; internal Action<object>? InstallAction; internal List<string> Events { get; } = new(); internal object Manager { get; } = new object(); internal object? InstalledManager; internal Action? ActiveCallback; internal Action? LastCallback;
        protected override DateTimeOffset ReadUtcNow() => Now;
        protected override L00CManagerResolutionStatus ResolveAcquired(out object? manager) { manager = Manager; Resolves++; if (Resolves == ThrowResolveOn) throw new InvalidOperationException(); return results.Count > 1 ? results.Dequeue() : results.Peek(); }
        protected override long RegisterAcquired(Action value) { Events.Add("register"); Registers++; if (ThrowRegister) throw new InvalidOperationException(); ActiveCallback = LastCallback = value; return 7; }
        internal void FireRegisteredCallback() { ActiveCallback?.Invoke(); }
        protected override void UnregisterAcquired(long _) { Events.Add("unregister"); if (ThrowUnregister) throw new InvalidOperationException(); ActiveCallback = null; }
        protected override void InstallAcquired(object value) { Events.Add("install"); Installs++; InstallSawAcquired = LeaseAcquired; InstalledManager = value; if (!ReferenceEquals(value, Manager) || ThrowInstall) throw new InvalidOperationException(); InstallAction?.Invoke(value); }
        protected override void WriteReceipt(string _, string __) { }
        protected override void ReleaseAcquired() { Events.Add("release"); Releases++; ActiveCallback = null; }
    }

    private sealed class RegistrationCandidate
    {
        private readonly L00CProcessCampaignInstallTransaction<RegistrationCandidate> transaction;
        internal RegistrationCandidate(L00CProcessCampaignInstallTransaction<RegistrationCandidate> owner, object session)
        { transaction = owner; Session = session; }
        internal object Session { get; }
        internal bool GateOpen { get; private set; } = true;
        internal bool ReferencesRetained { get; private set; } = true;
        internal void QueuePump() { }
        internal void Abort() { GateOpen = false; ReferencesRetained = false; transaction.Clear(this); }
    }
}
