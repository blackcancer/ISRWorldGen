#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.IO;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

// Deterministic production-state oracle only. It proves the pre-return lease
// and identity contract; it does not claim to execute a Vintage Story client.
internal static class L00CLifecycleShutdownOrderingOracle
{
    internal static int Run()
    {
        string guidA = "11111111-1111-1111-1111-111111111111";
        string guidB = "22222222-2222-2222-2222-222222222222";
        string primary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "l00c-primary.vcdbs"));
        string secondary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "l00c-secondary.vcdbs"));

        foreach (string malformed in new[] { "", "not-guid", "11111111111111111111111111111111", "{11111111-1111-1111-1111-111111111111}" })
            Refuse(() => L00CLifecycleShutdownIdentity.Create(1, "instance", malformed, 1), "malformed server GUID");
        Refuse(() => L00CLifecycleShutdownIdentity.Create(0, "instance", guidA, 1), "invalid run");
        Refuse(() => L00CLifecycleShutdownIdentity.Create(1, "instance", guidA, 0), "invalid sequence");

        var negative = new L00CLifecycleShutdownState();
        L00CLifecycleShutdownIdentity identity = L00CLifecycleShutdownIdentity.Create(7, "instance-a", guidA, 3);
        L00CLifecycleShutdownLease lease = negative.Open(identity);
        Refuse(() => negative.Open(identity), "duplicate open");
        Refuse(() => negative.PrepareReturn(identity, guidA, "activated-primary", primary, primary, 1), "pre-Arm return");
        negative.Arm(lease);
        Refuse(() => negative.PrepareReturn(identity, guidB, "activated-primary", primary, primary, 1), "wrong client GUID");
        Refuse(() => negative.PrepareReturn(identity, guidA, "activated-primary", primary, secondary, 1), "wrong StartServerArgs path");
        Refuse(() => negative.PrepareReturn(identity, guidA, "unknown", primary, primary, 1), "wrong role");
        Refuse(() => negative.PrepareReturn(identity, guidA, "activated-primary", primary, primary, 0), "wrong fixture sequence");
        Refuse(() => negative.PrepareReturn(identity, guidA, "activated-primary", primary, primary, 9), "fixture sequence after campaign");
        Refuse(() => negative.PrepareReturn(identity, guidA, "activated-primary", primary, primary, 2), "primary role in secondary sequence");
        Refuse(() => negative.PrepareReturn(identity, guidA, "activated-secondary", secondary, secondary, 3), "secondary role in primary sequence");
        Refuse(() => negative.PrepareReturn(L00CLifecycleShutdownIdentity.Create(8, "instance-a", guidA, 3), guidA, "activated-primary", primary, primary, 1), "wrong run");
        Refuse(() => negative.PrepareReturn(L00CLifecycleShutdownIdentity.Create(7, "instance-b", guidA, 3), guidA, "activated-primary", primary, primary, 1), "wrong instance");
        Refuse(() => negative.PrepareReturn(L00CLifecycleShutdownIdentity.Create(7, "instance-a", guidA, 4), guidA, "activated-primary", primary, primary, 1), "wrong server sequence");

        L00CLifecycleReturnReservation aborted = negative.PrepareReturn(identity, guidA, "activated-primary", primary, primary.ToUpperInvariant(), 1);
        if (!negative.AbortBeforeNativeReturn(aborted)) throw new InvalidOperationException("Pre-native abort did not clear its exact reservation.");
        Refuse(() => negative.BeginNativeReturn(aborted), "aborted reservation");
        L00CLifecycleReturnReservation pending = negative.PrepareReturn(identity, guidA, "activated-primary", primary, primary, 1);
        Refuse(() => negative.PrepareReturn(identity, guidA, "activated-primary", primary, primary, 1), "duplicate reservation");
        if (!negative.Close(lease)) throw new InvalidOperationException("Current owner did not close.");
        Refuse(() => negative.BeginNativeReturn(pending), "return after close");

        L00CLifecycleShutdownIdentity nextIdentity = L00CLifecycleShutdownIdentity.Create(9, "instance-c", guidB, 1);
        L00CLifecycleShutdownLease nextLease = negative.Open(nextIdentity);
        if (negative.Close(lease)) throw new InvalidOperationException("A stale owner closed the newer session.");
        negative.Arm(nextLease);
        L00CLifecycleReturnReservation next = negative.PrepareReturn(nextIdentity, guidB, "activated-secondary", secondary, secondary, 2);
        negative.BeginNativeReturn(next);
        Refuse(() => negative.BeginNativeReturn(next), "duplicate native return");
        Refuse(() => negative.PrepareReturn(nextIdentity, guidB, "activated-secondary", secondary, secondary, 2), "second reservation after native boundary");
        Refuse(() => negative.Arm(nextLease), "rearm after native boundary");
        if (negative.AbortBeforeNativeReturn(next)) throw new InvalidOperationException("A started native return was rolled back into a replayable state.");
        if (!negative.Close(nextLease)) throw new InvalidOperationException("New owner was lost after stale close.");

        int primaryReopens = 0;
        int authorizedReturns = 0;
        for (int fixtureSequence = 1; fixtureSequence <= 8; fixtureSequence++)
        {
            bool primaryRole = fixtureSequence == 1 || (fixtureSequence >= 3 && fixtureSequence <= 7);
            if (fixtureSequence >= 3 && fixtureSequence <= 7) primaryReopens++;
            string role = primaryRole ? "activated-primary" : "activated-secondary";
            string path = primaryRole ? primary : secondary;
            string guid = primaryRole ? guidA : guidB;
            var state = new L00CLifecycleShutdownState();
            L00CLifecycleShutdownIdentity current = L00CLifecycleShutdownIdentity.Create(fixtureSequence, "instance-" + fixtureSequence, guid, 1);
            L00CLifecycleShutdownLease currentLease = state.Open(current);
            state.Arm(currentLease);
            L00CLifecycleReturnReservation reservation = state.PrepareReturn(current, guid.ToUpperInvariant(), role, path, path, fixtureSequence);
            state.BeginNativeReturn(reservation);
            authorizedReturns++;
            if (!state.Close(currentLease)) throw new InvalidOperationException("Controlled lifecycle owner did not close.");
        }
        if (primaryReopens != 5 || authorizedReturns != 8)
            throw new InvalidOperationException("The lifecycle sequence is not two creations, five primary reopens, and one final secondary reopen.");
        return 0;
    }

    private static void Refuse(Action action, string label)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected lifecycle refusal: " + label);
    }
}
#endif
