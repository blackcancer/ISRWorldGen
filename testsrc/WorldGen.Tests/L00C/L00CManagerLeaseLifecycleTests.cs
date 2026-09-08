#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CManagerLeaseLifecycleTests
{
    public static int Main()
    {
        ImmediateReady(); DelayedReadyAndOrder(); TimeoutAndLimit(); ResolverAndMismatch(); DisposeAndLateTick(); TerminalizeUnregisterFailure(); RegisterUnregisterAndEnqueueFailures(); DuplicateStart(); InstallTransactionRollback();
        return 0;
    }
    private static void ImmediateReady()
    {
        var s = new Fake(L00CManagerResolutionStatus.Ready); var l = new L00CManagerLeaseLifecycle(s); l.Start();
        Check(l.Installed && !l.ListenerActive && s.Registers == 0 && s.Installs == 1 && l.Receipts[0] == "ready-immediate" && s.Releases == 1);
    }
    private static void DelayedReadyAndOrder()
    {
        var s = new Fake(L00CManagerResolutionStatus.ManagerUnavailable, L00CManagerResolutionStatus.Ready); var l = new L00CManagerLeaseLifecycle(s); l.Start(); s.FireRegisteredCallback();
        Check(l.Installed && s.Events.SequenceEqual(new[] { "register", "unregister", "install" }) && l.Receipts.Contains("ready") && s.Releases == 1 && s.InstalledManager == s.Manager);
    }
    private static void TimeoutAndLimit()
    {
        var s = new Fake(L00CManagerResolutionStatus.GameUnavailable); var l = new L00CManagerLeaseLifecycle(s); l.Start(); s.Now = s.Now.AddSeconds(31); s.FireRegisteredCallback(); Check(l.Terminal && l.Receipts.Contains("timeout"));
        s = new Fake(L00CManagerResolutionStatus.GameUnavailable); l = new L00CManagerLeaseLifecycle(s); l.Start(); for (int i = 0; i < 601; i++) s.FireRegisteredCallback(); Check(l.Terminal && l.Attempts == 601);
    }
    private static void ResolverAndMismatch()
    {
        var s = new Fake(L00CManagerResolutionStatus.GameUnavailable) { ThrowResolveOn = 2 }; var l = new L00CManagerLeaseLifecycle(s); l.Start(); s.FireRegisteredCallback(); Check(l.Terminal && l.Receipts.Contains("resolver-fault"));
        s = new Fake(L00CManagerResolutionStatus.ApiTypeMismatch); l = new L00CManagerLeaseLifecycle(s); l.Start(); Check(l.Terminal && !l.ListenerActive && l.Receipts.Contains("api-type-mismatch"));
    }
    private static void DisposeAndLateTick()
    {
        var s = new Fake(L00CManagerResolutionStatus.RunningScreenUnavailable); var l = new L00CManagerLeaseLifecycle(s); l.Start(); l.Dispose(); int calls = s.Resolves; s.FireRegisteredCallback(); Check(l.Terminal && !l.ListenerActive && s.Resolves == calls && l.Receipts.Contains("disposed"));
    }
    private static void TerminalizeUnregisterFailure()
    {
        var s = new Fake(L00CManagerResolutionStatus.ManagerUnavailable) { ThrowUnregister = true };
        var l = new L00CManagerLeaseLifecycle(s); l.Start(); l.Dispose(); int resolves = s.Resolves; s.FireRegisteredCallback();
        Check(l.Terminal && !l.ListenerActive && !l.Installed && s.Installs == 0 && s.Releases == 1 && s.Resolves == resolves && l.Receipts.Contains("disposed-unregister-fault"));
    }
    private static void RegisterUnregisterAndEnqueueFailures()
    {
        var s = new Fake(L00CManagerResolutionStatus.ManagerUnavailable) { ThrowRegister = true }; var l = new L00CManagerLeaseLifecycle(s); l.Start(); Check(l.Terminal && l.Receipts.Contains("listener-register-fault"));
        s = new Fake(L00CManagerResolutionStatus.ManagerUnavailable, L00CManagerResolutionStatus.Ready) { ThrowUnregister = true }; l = new L00CManagerLeaseLifecycle(s); l.Start(); s.FireRegisteredCallback(); Check(l.Terminal && !l.Installed && !l.ListenerActive && l.Receipts.Contains("handoff-unregister-fault") && s.Releases == 1 && s.Installs == 0);
        s = new Fake(L00CManagerResolutionStatus.Ready) { ThrowInstall = true }; l = new L00CManagerLeaseLifecycle(s); l.Start(); Check(l.Terminal && !l.Installed && l.Receipts.Contains("install-pump-fault"));
    }
    private static void DuplicateStart()
    {
        var s = new Fake(L00CManagerResolutionStatus.ManagerUnavailable); var l = new L00CManagerLeaseLifecycle(s); l.Start(); l.Start(); Check(s.Registers == 1 && s.Installs == 0);
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
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("L00-C lease simulation failed."); }
    private sealed class Fake : IL00CManagerLeaseSeams
    {
        private readonly Queue<L00CManagerResolutionStatus> results;
        internal Fake(params L00CManagerResolutionStatus[] values) { results = new Queue<L00CManagerResolutionStatus>(values); Now = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero); }
        internal DateTimeOffset Now { get; set; } public DateTimeOffset UtcNow => Now; internal int Registers; internal int Installs; internal int Releases; internal int Resolves; internal int ThrowResolveOn; internal bool ThrowRegister; internal bool ThrowUnregister; internal bool ThrowInstall; internal List<string> Events { get; } = new(); internal object Manager { get; } = new object(); internal object? InstalledManager; private Action? callback;
        public L00CManagerResolutionStatus Resolve(out object? manager) { manager = Manager; Resolves++; if (Resolves == ThrowResolveOn) throw new InvalidOperationException(); return results.Count > 1 ? results.Dequeue() : results.Peek(); }
        public long Register(Action value) { Events.Add("register"); Registers++; if (ThrowRegister) throw new InvalidOperationException(); callback = value; return 7; }
        internal void FireRegisteredCallback() { callback?.Invoke(); }
        public void Unregister(long _) { Events.Add("unregister"); if (ThrowUnregister) throw new InvalidOperationException(); }
        public void Install(object value) { Events.Add("install"); Installs++; InstalledManager = value; if (!ReferenceEquals(value, Manager) || ThrowInstall) throw new InvalidOperationException(); }
        public void Receipt(string _, string __) { }
        public void Release() { Releases++; }
    }
}
