#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

internal static class L00CLevelFinalizeGateOracle
{
    internal static int Run()
    {
        var gate = new L00CLevelFinalizeGate();
        L00CLevelFinalizeSessionLease first = L00CLevelFinalizeGate.CreateSessionLease();
        gate.AdoptSession(first);

        L00CNativeOpenReservation firstOpen = gate.BeginOpen(1);
        if (first.Capture() is not null) throw new InvalidOperationException("A pre-click LevelFinalize acquired the pending open epoch.");
        gate.CompleteOpen(firstOpen);
        L00CLevelFinalizeSignal firstAccepted = first.Capture() ?? throw new InvalidOperationException("First finalized session had no epoch.");
        L00CLevelFinalizeSignal delayedDuplicate = first.Capture() ?? throw new InvalidOperationException("Concurrent stale signal was not captured.");
        if (!gate.TryAccept(firstAccepted, out int firstSequence) || firstSequence != 1)
            throw new InvalidOperationException("First finalized session was not accepted exactly.");

        using var staleStarted = new ManualResetEventSlim(false);
        using var releaseStale = new ManualResetEventSlim(false);
        bool staleAccepted = true;
        Task staleDelivery = Task.Run(() =>
        {
            staleStarted.Set();
            releaseStale.Wait();
            staleAccepted = gate.TryAccept(delayedDuplicate, out _);
        });
        staleStarted.Wait();

        // DestroyGameSession retires the old ModSystem before the next menu
        // click. The open epoch must remain pending until the new ModSystem
        // installs its own lease during/after that click.
        gate.RetireSession(first);
        L00CNativeOpenReservation secondOpen = gate.BeginOpen(2);
        if (first.Capture() is not null) throw new InvalidOperationException("A retired owner acquired the second native-click epoch.");
        gate.CompleteOpen(secondOpen);
        L00CLevelFinalizeSessionLease second = L00CLevelFinalizeGate.CreateSessionLease();
        gate.AdoptSession(second);
        releaseStale.Set();
        staleDelivery.GetAwaiter().GetResult();
        if (staleAccepted) throw new InvalidOperationException("A pre-click stale signal was accepted after the next open.");
        if (first.Capture() is not null) throw new InvalidOperationException("The retired client ModSystem owner retained an epoch.");
        L00CLevelFinalizeSignal secondSignal = second.Capture() ?? throw new InvalidOperationException("Current client owner did not receive the second epoch.");
        if (!gate.TryAccept(secondSignal, out int secondSequence) || secondSequence != 2)
            throw new InvalidOperationException("Current client owner was not accepted for the second epoch.");

        int finalizedSessions = 2;
        for (int fixtureSequence = 3; fixtureSequence <= 15; fixtureSequence++)
        {
            gate.RetireSession(second);
            L00CNativeOpenReservation open = gate.BeginOpen(fixtureSequence);
            if (second.Capture() is not null) throw new InvalidOperationException("A retired owner acquired a future epoch.");
            gate.CompleteOpen(open);
            L00CLevelFinalizeSessionLease current = L00CLevelFinalizeGate.CreateSessionLease();
            gate.AdoptSession(current);
            L00CLevelFinalizeSignal accepted = current.Capture() ?? throw new InvalidOperationException("Finalized session epoch was absent.");
            L00CLevelFinalizeSignal duplicate = current.Capture() ?? throw new InvalidOperationException("Duplicate signal capture failed.");
            if (!gate.TryAccept(accepted, out int observed) || observed != fixtureSequence || gate.TryAccept(duplicate, out _))
                throw new InvalidOperationException("Finalized session was not exact-reference one-shot.");
            second = current;
            finalizedSessions++;
        }

        Refuse(() => gate.BeginOpen(0), "sequence zero");
        Refuse(() => gate.BeginOpen(16), "sequence after campaign");
        gate.Close();
        if (second.Capture() is not null) throw new InvalidOperationException("Closed gate retained a session epoch.");
        Refuse(() => gate.BeginOpen(1), "reopen after close");
        if (finalizedSessions != 15) throw new InvalidOperationException("The finalize gate did not execute all fifteen sessions.");
        return finalizedSessions;
    }

    private static void Refuse(Action action, string label)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected LevelFinalize refusal: " + label);
    }
}
#endif
