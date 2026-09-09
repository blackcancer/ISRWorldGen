#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CDelayedShutdownGateOracle
{
    internal static int Run()
    {
        NormalAsynchronous();
        SynchronousCallback();
        RegisterThrows();
        InvalidId();
        CloseBetweenRegisterAndAttach();
        SynchronousThenCloseBeforeAttach();
        UnregisterFirstAttemptThrows();
        ConcurrentCancelCannotOverlapRetry();
        UnregisterTwiceThenExplicitRetry();
        CancelAndLateCallback();
        CallerFailureAfterSchedule();
        DuplicateSchedule();
        return 0;
    }

    private static void NormalAsynchronous()
    {
        var gate = new DelayedShutdownGate();
        var ids = new List<long>(); Action? callback = null; int fires = 0;
        gate.Open();
        gate.Schedule(15000, (value, _) => { callback = value; return 101; }, id => ids.Add(id), () => fires++);
        callback!(); callback!();
        if (fires != 1 || ids.Count != 1 || ids[0] != 101) throw new InvalidOperationException("Async registration was not removed/fired exactly once.");
        gate.Open(); // successful completion alone permits the next world
        gate.CancelAndUnregister();
    }

    private static void SynchronousCallback()
    {
        var gate = new DelayedShutdownGate(); int fires = 0; var ids = new List<long>();
        gate.Open();
        gate.Schedule(15000, (callback, _) => { callback(); return 102; }, id => ids.Add(id), () => fires++);
        if (fires != 1 || ids.Count != 1 || ids[0] != 102) throw new InvalidOperationException("Synchronous callback escaped pre-attach latching.");
    }

    private static void RegisterThrows()
    {
        var gate = new DelayedShutdownGate(); int fires = 0;
        gate.Open();
        Refuse(() => gate.Schedule(15000, (_, _) => throw new InvalidOperationException("register"), _ => throw new InvalidOperationException("unreachable"), () => fires++), "registrar throw");
        Refuse(gate.Open, "reopen after registrar rollback");
        if (fires != 0) throw new InvalidOperationException("Registrar failure fired shutdown.");
    }

    private static void InvalidId()
    {
        var gate = new DelayedShutdownGate(); var ids = new List<long>(); int fires = 0;
        gate.Open();
        Refuse(() => gate.Schedule(15000, (_, _) => 0, id => ids.Add(id), () => fires++), "invalid id");
        if (ids.Count != 1 || ids[0] != 0 || fires != 0) throw new InvalidOperationException("Invalid id was not compensated exactly.");
        gate.CancelAndUnregister();
        if (ids.Count != 1) throw new InvalidOperationException("Invalid-id compensation left a residual registration.");
        Refuse(gate.Open, "reopen after invalid-id rollback");
    }

    private static void CloseBetweenRegisterAndAttach()
    {
        var gate = new DelayedShutdownGate(); var ids = new List<long>(); Action? callback = null; int fires = 0;
        gate.Open();
        Refuse(() => gate.Schedule(15000, (value, _) => { callback = value; gate.CancelAndUnregister(); return 103; }, id => ids.Add(id), () => fires++), "close between register and attach");
        callback!();
        if (ids.Count != 1 || ids[0] != 103 || fires != 0) throw new InvalidOperationException("Close-between-attach left a live callback.");
        Refuse(gate.Open, "reopen after close-between-attach rollback");
    }

    private static void SynchronousThenCloseBeforeAttach()
    {
        var gate = new DelayedShutdownGate(); var ids = new List<long>(); int fires = 0;
        gate.Open();
        Refuse(() => gate.Schedule(15000, (callback, _) => { callback(); gate.CancelAndUnregister(); return 104; }, id => ids.Add(id), () => fires++), "sync callback plus close before attach");
        if (ids.Count != 1 || ids[0] != 104 || fires != 0) throw new InvalidOperationException("Synchronous late callback fired after rollback.");
    }

    private static void UnregisterFirstAttemptThrows()
    {
        var gate = new DelayedShutdownGate(); var attempts = new List<long>(); Action? callback = null; int fires = 0;
        gate.Open();
        gate.Schedule(15000, (value, _) => { callback = value; return 105; }, id => { attempts.Add(id); if (attempts.Count == 1) throw new InvalidOperationException("first unregister"); }, () => fires++);
        callback!(); callback!();
        if (attempts.Count != 2 || attempts[0] != 105 || attempts[1] != 105 || fires != 0)
            throw new InvalidOperationException("Unregister failure did not retry the exact id while suppressing fire.");
        gate.CancelAndUnregister();
        if (attempts.Count != 2) throw new InvalidOperationException("Successful exact-id retry left a residual registration.");
        Refuse(gate.Open, "reopen after unregister rollback");
    }

    private static void UnregisterTwiceThenExplicitRetry()
    {
        var gate = new DelayedShutdownGate(); var attempts = new List<long>(); Action? callback = null; int fires = 0;
        gate.Open();
        gate.Schedule(15000, (value, _) => { callback = value; return 110; }, id =>
        {
            attempts.Add(id);
            if (attempts.Count <= 2) throw new InvalidOperationException("bounded unregister failure");
        }, () => fires++);
        Refuse(gate.CancelAndUnregister, "two unregister failures");
        callback!();
        gate.CancelAndUnregister();
        callback!();
        gate.CancelAndUnregister();
        if (attempts.Count != 3 || attempts.Exists(id => id != 110) || fires != 0)
            throw new InvalidOperationException("Pending compensation did not retain and remove the exact listener id.");
        Refuse(gate.Open, "reopen after deferred unregister rollback");
    }

    private static void ConcurrentCancelCannotOverlapRetry()
    {
        var gate = new DelayedShutdownGate();
        using var retryEntered = new ManualResetEventSlim(false);
        using var releaseRetry = new ManualResetEventSlim(false);
        Action? callback = null;
        int attempts = 0;
        int concurrent = 0;
        int maxConcurrent = 0;
        int fires = 0;
        gate.Open();
        gate.Schedule(15000, (value, _) => { callback = value; return 111; }, id =>
        {
            if (id != 111) throw new InvalidOperationException("Concurrent compensation changed listener id.");
            int active = Interlocked.Increment(ref concurrent);
            int observed;
            while (active > (observed = Volatile.Read(ref maxConcurrent)))
                _ = Interlocked.CompareExchange(ref maxConcurrent, active, observed);
            int attempt = Interlocked.Increment(ref attempts);
            try
            {
                if (attempt == 1) throw new InvalidOperationException("first unregister");
                retryEntered.Set();
                if (!releaseRetry.Wait(5000)) throw new InvalidOperationException("retry release timeout");
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }, () => Interlocked.Increment(ref fires));

        Task completion = Task.Run(() => callback!());
        if (!retryEntered.Wait(5000)) throw new InvalidOperationException("Exact-id retry did not start.");
        Task cancellation = Task.Run(gate.CancelAndUnregister);
        if (!cancellation.Wait(5000)) throw new InvalidOperationException("Concurrent cancellation blocked behind external unregister.");
        releaseRetry.Set();
        completion.GetAwaiter().GetResult();
        if (attempts != 2 || maxConcurrent != 1 || fires != 0)
            throw new InvalidOperationException("Concurrent cancellation overlapped the exact-id unregister retry.");
        gate.CancelAndUnregister();
        if (attempts != 2) throw new InvalidOperationException("Concurrent retry left a residual registration.");
        Refuse(gate.Open, "reopen after concurrent unregister rollback");
    }

    private static void CancelAndLateCallback()
    {
        var gate = new DelayedShutdownGate(); var ids = new List<long>(); Action? callback = null; int fires = 0;
        gate.Open();
        gate.Schedule(15000, (value, _) => { callback = value; return 106; }, id => ids.Add(id), () => fires++);
        gate.CancelAndUnregister(); callback!();
        if (ids.Count != 1 || ids[0] != 106 || fires != 0) throw new InvalidOperationException("Cancel left a delayed callback fireable.");
        Refuse(gate.Open, "reopen after explicit cancel rollback");
    }

    private static void CallerFailureAfterSchedule()
    {
        var gate = new DelayedShutdownGate(); var ids = new List<long>(); Action? callback = null; int fires = 0;
        gate.Open();
        try
        {
            gate.Schedule(15000, (value, _) => { callback = value; return 107; }, id => ids.Add(id), () => fires++);
            throw new InvalidOperationException("simulated caller/log failure");
        }
        catch (InvalidOperationException)
        {
            gate.CancelAndUnregister();
        }
        callback!();
        if (ids.Count != 1 || ids[0] != 107 || fires != 0) throw new InvalidOperationException("Caller failure left a delayed callback live.");
    }

    private static void DuplicateSchedule()
    {
        var gate = new DelayedShutdownGate(); Action? callback = null; var ids = new List<long>();
        gate.Open();
        gate.Schedule(15000, (value, _) => { callback = value; return 108; }, id => ids.Add(id), () => { });
        Refuse(() => gate.Schedule(15000, (_, _) => 109, id => ids.Add(id), () => { }), "duplicate schedule");
        gate.CancelAndUnregister(); callback!();
        if (ids.Count != 1 || ids[0] != 108) throw new InvalidOperationException("Duplicate schedule registered another listener.");
    }

    private static void Refuse(Action action, string label)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        catch (AggregateException) { return; }
        throw new InvalidOperationException("Expected delayed-shutdown refusal: " + label);
    }
}
#endif
