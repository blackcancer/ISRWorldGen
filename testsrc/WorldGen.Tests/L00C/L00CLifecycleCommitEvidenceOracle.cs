#if L00C_STANDALONE_ORACLE
#nullable enable
using System;
using System.IO;
using System.Threading;
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

// In-memory fault injection against the actual barrier. No saves, native
// processes, account data or filesystem fixtures are created by this oracle.
internal static class L00CLifecycleCommitEvidenceOracle
{
    internal static int Run()
    {
        int cases = 0;
        MissingRegistrationMustNotCommit(); cases++;
        MissingMarkerMustNotCommit(false); cases++;
        MissingMarkerMustNotCommit(true); cases++;
        for (int flag = 0; flag < 8; flag++)
        {
            RejectInvalidRegistration(flag, -1, 0); cases++;
        }
        for (int counter = 0; counter < 4; counter++)
        {
            RejectInvalidRegistration(-1, counter, 1); cases++;
            RejectInvalidRegistration(-1, counter, -1); cases++;
        }
        CompleteEvidenceCommitsWithoutPolling(); cases++;
        ConcurrentCommitIsSingleUse(); cases++;
        return cases;
    }

    // This precise counterexample must fail against the pre-fix source. A
    // compilation failure or an unrelated exception is not a red-test proof.
    internal static void MissingRegistrationMustNotCommit()
    {
        var fixture = new Fixture(true);
        Require(!fixture.State.TryGetCompletedEvidence(fixture.Reservation, out var missing) && missing is null,
            "Polling must report registration evidence as unavailable.");
        Refuse(fixture.Commit, "commit without registration proof");
        RequirePending(fixture);
        fixture.State.CaptureRegistrationRelease(fixture.Lease, CompleteRegistrations());
        fixture.Commit();
        RequireCommitted(fixture);
    }

    private static void MissingMarkerMustNotCommit(bool withRegistrations)
    {
        var fixture = new Fixture(false);
        if (withRegistrations)
            fixture.State.CaptureRegistrationRelease(fixture.Lease, CompleteRegistrations());
        Refuse(fixture.Commit, withRegistrations ? "commit without internal marker" : "commit without either evidence");
        Refuse(() => fixture.State.RequireCompletedEvidence(fixture.Reservation), "incomplete evidence lookup");
        RequirePending(fixture);
    }

    private static void RejectInvalidRegistration(int missingFlag, int counter, int value)
    {
        var fixture = new Fixture(true);
        bool[] flags = { true, true, true, true, true, true, true, true };
        int[] counts = { 0, 0, 0, 0 };
        if (missingFlag >= 0) flags[missingFlag] = false;
        if (counter >= 0) counts[counter] = value;
        var invalid = new L00CLifecycleRegistrationProof(flags[0], flags[1], flags[2],
            flags[3], flags[4], flags[5], flags[6], flags[7], counts[0], counts[1], counts[2], counts[3]);
        Refuse(() => fixture.State.CaptureRegistrationRelease(fixture.Lease, invalid), "incomplete registration publication");
        Require(fixture.Lease.Registrations is null, "Rejected publication changed the lease.");

        // Deliberately corrupt the internal test object to establish that the
        // committing boundary validates content, not merely its presence.
        fixture.Lease.Registrations = invalid;
        Refuse(fixture.Commit, "commit with invalid registration content");
        RequirePending(fixture);
        fixture.Lease.Registrations = null;
        fixture.State.CaptureRegistrationRelease(fixture.Lease, CompleteRegistrations());
        fixture.Commit();
        RequireCommitted(fixture);
    }

    private static void CompleteEvidenceCommitsWithoutPolling()
    {
        var fixture = new Fixture(true);
        fixture.State.CaptureRegistrationRelease(fixture.Lease, CompleteRegistrations());
        // No call to TryGetCompletedEvidence: confirmation owns its invariant.
        fixture.Commit();
        RequireCommitted(fixture);
        Refuse(fixture.Commit, "replayed completed return");
        Refuse(() => fixture.State.RequireCompletedEvidence(fixture.Reservation), "post-commit evidence replay");
        Refuse(() => fixture.State.TryGetCompletedEvidence(fixture.Reservation, out _), "post-commit poll replay");
        var successor = fixture.State.Open(L00CLifecycleShutdownIdentity.Create(2, "successor", Fixture.Guid));
        Require(fixture.State.Close(successor), "A successful commit did not release the next session.");
    }

    private static void ConcurrentCommitIsSingleUse()
    {
        var fixture = new Fixture(true);
        fixture.State.CaptureRegistrationRelease(fixture.Lease, CompleteRegistrations());
        using var start = new ManualResetEventSlim(false);
        int accepted = 0, rejected = 0;
        Exception? firstError = null, secondError = null;
        void Attempt(Action<Exception> unexpected)
        {
            try
            {
                if (!start.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Commit start gate timed out.");
                try { fixture.Commit(); Interlocked.Increment(ref accepted); }
                catch (InvalidOperationException) { Interlocked.Increment(ref rejected); }
            }
            catch (Exception exception) { unexpected(exception); }
        }
        var first = new Thread(() => Attempt(error => firstError = error)) { IsBackground = true };
        var second = new Thread(() => Attempt(error => secondError = error)) { IsBackground = true };
        first.Start(); second.Start(); start.Set();
        bool firstFinished = first.Join(TimeSpan.FromSeconds(10));
        bool secondFinished = second.Join(TimeSpan.FromSeconds(10));
        Require(firstFinished && secondFinished, "Concurrent confirmations did not terminate.");
        Require(firstError is null && secondError is null, "A confirmation thread failed unexpectedly.");
        Require(accepted == 1 && rejected == 1, "A return was either lost or committed more than once.");
        RequireCommitted(fixture);
    }

    private static void RequirePending(Fixture fixture)
    {
        Require(!fixture.Reservation.Committed && !fixture.Reservation.Cancelled && fixture.Reservation.Started,
            "Rejected confirmation changed reservation state.");
        Require(CountCommitEvents(fixture) == 0, "Rejected confirmation published SaveCommitted.");
        Refuse(() => fixture.State.Open(L00CLifecycleShutdownIdentity.Create(2, "premature", Fixture.Guid)),
            "next session before complete evidence");
    }

    private static void RequireCommitted(Fixture fixture)
    {
        Require(fixture.Reservation.Committed && !fixture.Reservation.Cancelled,
            "Complete evidence did not commit the reserved return.");
        Require(CountCommitEvents(fixture) == 1, "SaveCommitted must be published exactly once.");
    }

    private static int CountCommitEvents(Fixture fixture)
    {
        int count = 0;
        foreach (string entry in fixture.State.Trace)
            if (entry.Contains(" state=SaveCommitted ")) count++;
        return count;
    }

    private static L00CLifecycleRegistrationProof CompleteRegistrations()
        => new(true, true, true, true, true, true, true, true, 0, 0, 0, 0);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Refuse(Action action, string label)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected lifecycle refusal: " + label);
    }

    private sealed class Fixture
    {
        internal const string Guid = "11111111-1111-1111-1111-111111111111";
        private const string RunId = "1234567890abcdef1234567890abcdef";
        internal L00CLifecycleShutdownState State { get; } = new();
        internal L00CLifecycleShutdownLease Lease { get; }
        internal L00CLifecycleReturnReservation Reservation { get; }
        private readonly L00CLifecycleSessionObservation committed;
        private readonly L00CNativeSaveQuitReturnProof proof;

        internal Fixture(bool withMarker)
        {
            // A canonical path is input data only: nothing is opened or written.
            string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "l00c-commit-evidence", "ISRWorldGen-L00C-" + RunId + "-iteration-01-a.vcdbs"));
            Lease = State.Open(L00CLifecycleShutdownIdentity.Create(1, "synthetic-evidence", Guid));
            if (withMarker) State.CaptureInternalMarker(Lease, new L00CLifecycleInternalMarkerProof(RunId, Guid, 1));
            State.Arm(Lease);
            var ready = L00CLifecycleSessionObservation.Ready(RunId, 1, 1, path, Guid, true);
            // Exercise the legacy BindReady path as well as the native host's
            // stricter path; neither may bypass the final evidence invariant.
            State.BindReady(Lease, ready);
            Reservation = State.PrepareReturn(ready, path);
            State.BeginNativeReturn(Reservation);
            Require(State.Close(Lease), "Fixture failed to release its server lease.");
            committed = L00CLifecycleSessionObservation.SaveCommitted(RunId, 1, 1, path, Guid, true);
            proof = new L00CNativeSaveQuitReturnProof(true, true, true, true, path, Guid);
        }
        internal void Commit() => State.ConfirmSaveCommitted(Reservation, committed, proof);
    }
}
#endif
