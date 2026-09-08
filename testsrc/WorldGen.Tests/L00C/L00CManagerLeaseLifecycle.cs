// Pure deterministic lifecycle oracle for the in-process bootstrap lease.
#nullable enable
using System;
using System.Collections.Generic;

namespace ISRWorldGen.L00C.Laboratory;

internal interface IL00CManagerLeaseSeams
{
    DateTimeOffset UtcNow { get; }
    L00CManagerResolutionStatus Resolve(out object? manager);
    long Register(Action callback);
    void Unregister(long listenerId);
    void Install(object manager);
    void Receipt(string status, string detail);
    void Release();
}

/// <summary>Injectable model of the production lease's terminal ordering.</summary>
internal sealed class L00CManagerLeaseLifecycle
{
    private const int MaximumAttempts = 600;
    private readonly IL00CManagerLeaseSeams seams;
    private readonly DateTimeOffset deadline;
    private long listenerId;
    private int attempts;
    private bool terminal;
    private bool installed;
    internal L00CManagerLeaseLifecycle(IL00CManagerLeaseSeams value) { seams = value; deadline = value.UtcNow.AddSeconds(30); }
    internal List<string> Receipts { get; } = new();
    internal bool ListenerActive => listenerId != 0;
    internal bool Terminal => terminal;
    internal bool Installed => installed;
    internal int Attempts => attempts;

    internal void Start()
    {
        if (terminal || installed || ListenerActive) return;
        TryResolve(initial: true);
    }
    internal void Dispose() => Terminalize("disposed");
    internal void Tick()
    {
        if (terminal || !ListenerActive) return;
        attempts++;
        if (attempts > MaximumAttempts || seams.UtcNow >= deadline) { Terminalize("timeout"); return; }
        TryResolve(initial: false);
    }
    private void TryResolve(bool initial)
    {
        L00CManagerResolutionStatus status;
        object? manager;
        try { status = seams.Resolve(out manager); }
        catch { Terminalize("resolver-fault"); return; }
        if (status == L00CManagerResolutionStatus.Ready)
        {
            // A listener must disappear before the production controller pump is queued.
            if (!StopListener("handoff-unregister-fault")) return;
            // The adapter may have retained the session API solely for this
            // listener. Sever it before any process-lifetime install occurs.
            seams.Release();
            try { seams.Install(manager!); installed = true; Publish(initial ? "ready-immediate" : "ready", "manager-handoff"); }
            catch { Terminalize("install-pump-fault"); }
            return;
        }
        if (status == L00CManagerResolutionStatus.ApiTypeMismatch) { Terminalize("api-type-mismatch"); return; }
        if (!ListenerActive)
        {
            try { listenerId = seams.Register(Tick); Publish("waiting-" + status, status.ToString()); }
            catch { Terminalize("listener-register-fault"); }
        }
    }
    private bool StopListener(string failure)
    {
        if (!ListenerActive) return true;
        long id = listenerId;
        try { seams.Unregister(id); listenerId = 0; return true; }
        catch { listenerId = 0; terminal = true; Publish(failure, "unregister"); seams.Release(); return false; }
    }
    private void Terminalize(string status)
    {
        if (terminal) return;
        terminal = true;
        if (listenerId != 0)
        {
            long id = listenerId; listenerId = 0;
            try { seams.Unregister(id); }
            catch { status += "-unregister-fault"; }
        }
        Publish(status, "terminal");
        seams.Release();
    }
    private void Publish(string status, string detail) { Receipts.Add(status); seams.Receipt(status, detail); }
}
