#if DEBUG
using System;
using System.Threading;

namespace ISRWorldGen.WorldgenProbe;

// Correlates a native LevelFinalize callback with both the client ModSystem
// that raised it and the exact menu-open epoch. Capture is deliberately
// lock-free: an event that starts before the click keeps the old/null epoch
// even if delivery blocks behind the process controller's lock.
internal sealed class L00CLevelFinalizeGate
{
    private readonly object gate = new();
    private L00CLevelFinalizeSessionLease? activeSession;
    private L00CNativeOpenReservation? pendingOpen;
    private L00CLevelFinalizeEpoch? expectedEpoch;
    private long openGeneration;
    private bool closed;

    internal static L00CLevelFinalizeSessionLease CreateSessionLease() => new();

    internal void AdoptSession(L00CLevelFinalizeSessionLease session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        lock (gate)
        {
            if (closed || session.Revoked)
                throw new InvalidOperationException("L00-C LevelFinalize session lease is closed or revoked.");
            if (ReferenceEquals(activeSession, session)) return;
            activeSession?.Revoke();
            activeSession = session;
            if (expectedEpoch is not null) session.Bind(expectedEpoch);
        }
    }

    internal void RetireSession(L00CLevelFinalizeSessionLease session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        lock (gate)
        {
            session.Revoke();
            if (ReferenceEquals(activeSession, session)) activeSession = null;
        }
    }

    internal L00CNativeOpenReservation BeginOpen(int fixtureSequence)
    {
        ValidateFixtureSequence(fixtureSequence);
        lock (gate)
        {
            if (closed || pendingOpen is not null || expectedEpoch is not null)
                throw new InvalidOperationException("L00-C native open is closed, duplicate, or still waiting for the prior LevelFinalize.");
            activeSession?.Unbind();
            var reservation = new L00CNativeOpenReservation(this, fixtureSequence, checked(++openGeneration));
            pendingOpen = reservation;
            return reservation;
        }
    }

    internal void CompleteOpen(L00CNativeOpenReservation reservation)
    {
        if (reservation is null) throw new ArgumentNullException(nameof(reservation));
        lock (gate)
        {
            if (closed || !ReferenceEquals(pendingOpen, reservation) || !ReferenceEquals(reservation.Owner, this) ||
                reservation.Cancelled || reservation.Completed)
                throw new InvalidOperationException("L00-C native open completion is stale, closed, or owned by another epoch.");
            reservation.Completed = true;
            pendingOpen = null;
            var epoch = new L00CLevelFinalizeEpoch(this, reservation.FixtureSequence, reservation.Generation);
            expectedEpoch = epoch;
            activeSession?.Bind(epoch);
        }
    }

    internal bool AbortOpen(L00CNativeOpenReservation reservation)
    {
        if (reservation is null) throw new ArgumentNullException(nameof(reservation));
        lock (gate)
        {
            if (!ReferenceEquals(pendingOpen, reservation) || reservation.Completed) return false;
            reservation.Cancelled = true;
            pendingOpen = null;
            return true;
        }
    }

    internal bool TryAccept(L00CLevelFinalizeSignal? signal, out int fixtureSequence)
    {
        fixtureSequence = 0;
        if (signal is null) return false;
        lock (gate)
        {
            if (closed || activeSession is null || expectedEpoch is null ||
                !ReferenceEquals(signal.Session, activeSession) || !ReferenceEquals(signal.Epoch, expectedEpoch) ||
                !ReferenceEquals(signal.Epoch.Owner, this) || signal.Session.Revoked || signal.Epoch.Consumed)
                return false;
            signal.Epoch.Consumed = true;
            fixtureSequence = signal.Epoch.FixtureSequence;
            expectedEpoch = null;
            activeSession.Unbind(signal.Epoch);
            return true;
        }
    }

    internal void Close()
    {
        lock (gate)
        {
            closed = true;
            if (pendingOpen is not null) pendingOpen.Cancelled = true;
            pendingOpen = null;
            if (expectedEpoch is not null) expectedEpoch.Consumed = true;
            expectedEpoch = null;
            activeSession?.Revoke();
            activeSession = null;
        }
    }

    private static void ValidateFixtureSequence(int fixtureSequence)
    {
        if (fixtureSequence < 1 || fixtureSequence > 8)
            throw new InvalidOperationException("L00-C LevelFinalize fixture sequence must be within the exact 1..8 campaign.");
    }
}

internal sealed class L00CLevelFinalizeSessionLease
{
    private static long nextGeneration;
    private L00CLevelFinalizeEpoch? epoch;
    private int revoked;

    internal L00CLevelFinalizeSessionLease() => Generation = Interlocked.Increment(ref nextGeneration);
    internal long Generation { get; }
    internal bool Revoked => Volatile.Read(ref revoked) != 0;

    internal L00CLevelFinalizeSignal? Capture()
    {
        if (Revoked) return null;
        L00CLevelFinalizeEpoch? observed = Volatile.Read(ref epoch);
        return observed is null ? null : new L00CLevelFinalizeSignal(this, observed);
    }

    internal void Bind(L00CLevelFinalizeEpoch value)
    {
        if (Revoked) throw new InvalidOperationException("L00-C cannot bind a revoked LevelFinalize session lease.");
        Volatile.Write(ref epoch, value);
    }

    internal void Unbind() => Volatile.Write(ref epoch, null);

    internal void Unbind(L00CLevelFinalizeEpoch expected)
    {
        _ = Interlocked.CompareExchange(ref epoch, null, expected);
    }

    internal void Revoke()
    {
        Interlocked.Exchange(ref revoked, 1);
        Volatile.Write(ref epoch, null);
    }
}

internal sealed class L00CNativeOpenReservation
{
    internal L00CNativeOpenReservation(L00CLevelFinalizeGate owner, int fixtureSequence, long generation)
    {
        Owner = owner;
        FixtureSequence = fixtureSequence;
        Generation = generation;
    }

    internal L00CLevelFinalizeGate Owner { get; }
    internal int FixtureSequence { get; }
    internal long Generation { get; }
    internal bool Completed { get; set; }
    internal bool Cancelled { get; set; }
}

internal sealed class L00CLevelFinalizeEpoch
{
    internal L00CLevelFinalizeEpoch(L00CLevelFinalizeGate owner, int fixtureSequence, long generation)
    {
        Owner = owner;
        FixtureSequence = fixtureSequence;
        Generation = generation;
    }

    internal L00CLevelFinalizeGate Owner { get; }
    internal int FixtureSequence { get; }
    internal long Generation { get; }
    internal bool Consumed { get; set; }
}

internal sealed class L00CLevelFinalizeSignal
{
    internal L00CLevelFinalizeSignal(L00CLevelFinalizeSessionLease session, L00CLevelFinalizeEpoch epoch)
    {
        Session = session;
        Epoch = epoch;
    }

    internal L00CLevelFinalizeSessionLease Session { get; }
    internal L00CLevelFinalizeEpoch Epoch { get; }
}
#endif
