#if DEBUG
using System;
using System.IO;

namespace ISRWorldGen.WorldgenProbe;

// Process-local handoff between the Debug-only server probe and the Debug-only
// menu controller. It owns only immutable identities/leases: no API, world or
// client session object crosses the native Save & Quit boundary.
internal static class L00CLifecycleShutdownBarrier
{
    private static readonly L00CLifecycleShutdownState State = new();

    internal static L00CLifecycleShutdownLease Open(L00CLifecycleShutdownIdentity identity) => State.Open(identity);
    internal static bool Close(L00CLifecycleShutdownLease lease) => State.Close(lease);
    internal static void Arm(L00CLifecycleShutdownLease lease) => State.Arm(lease);
    internal static L00CLifecycleShutdownIdentity RequireCurrentIdentity() => State.RequireCurrentIdentity();
    internal static L00CLifecycleReturnReservation PrepareReturn(
        L00CLifecycleShutdownIdentity identity,
        string clientSavegameGuid,
        string fixtureRole,
        string expectedSavePath,
        string observedStartServerSavePath,
        int fixtureSequence) => State.PrepareReturn(identity, clientSavegameGuid, fixtureRole, expectedSavePath, observedStartServerSavePath, fixtureSequence);
    internal static void BeginNativeReturn(L00CLifecycleReturnReservation reservation) => State.BeginNativeReturn(reservation);
    internal static bool AbortBeforeNativeReturn(L00CLifecycleReturnReservation reservation) => State.AbortBeforeNativeReturn(reservation);
}

// Instance form used directly by the executable oracle. Production uses the
// single process-local instance above.
internal sealed class L00CLifecycleShutdownState
{
    private readonly object gate = new();
    private L00CLifecycleShutdownLease? activeLease;
    private L00CLifecycleReturnReservation? pendingReturn;
    private bool stable;
    private bool nativeReturnStarted;
    private long generation;

    internal L00CLifecycleShutdownLease Open(L00CLifecycleShutdownIdentity identity)
    {
        if (identity is null) throw new ArgumentNullException(nameof(identity));
        lock (gate)
        {
            if (activeLease is not null)
                throw new InvalidOperationException("L00-C lifecycle shutdown barrier already has an active owner.");
            var lease = new L00CLifecycleShutdownLease(identity, checked(++generation));
            activeLease = lease;
            pendingReturn = null;
            stable = false;
            nativeReturnStarted = false;
            return lease;
        }
    }

    internal bool Close(L00CLifecycleShutdownLease lease)
    {
        if (lease is null) throw new ArgumentNullException(nameof(lease));
        lock (gate)
        {
            if (!ReferenceEquals(activeLease, lease)) return false;
            lease.Closed = true;
            if (pendingReturn is not null) pendingReturn.Cancelled = true;
            pendingReturn = null;
            activeLease = null;
            stable = false;
            nativeReturnStarted = false;
            return true;
        }
    }

    internal void Arm(L00CLifecycleShutdownLease lease)
    {
        if (lease is null) throw new ArgumentNullException(nameof(lease));
        lock (gate)
        {
            if (!ReferenceEquals(activeLease, lease) || lease.Closed || nativeReturnStarted)
                throw new InvalidOperationException("L00-C lifecycle shutdown barrier was armed by a stale owner.");
            stable = true;
        }
    }

    internal L00CLifecycleShutdownIdentity RequireCurrentIdentity()
    {
        lock (gate)
        {
            if (activeLease is null || activeLease.Closed)
                throw new InvalidOperationException("L00-C lifecycle controller has no active immutable server identity.");
            return activeLease.Identity;
        }
    }

    internal L00CLifecycleReturnReservation PrepareReturn(
        L00CLifecycleShutdownIdentity identity,
        string clientSavegameGuid,
        string fixtureRole,
        string expectedSavePath,
        string observedStartServerSavePath,
        int fixtureSequence)
    {
        if (identity is null) throw new ArgumentNullException(nameof(identity));
        string normalizedClientGuid = L00CLifecycleShutdownIdentity.NormalizeGuid(clientSavegameGuid, nameof(clientSavegameGuid));
        string role = NormalizeRole(fixtureRole);
        ValidateFixtureSequence(role, fixtureSequence);
        string expectedPath = NormalizeSavePath(expectedSavePath, nameof(expectedSavePath));
        string observedPath = NormalizeSavePath(observedStartServerSavePath, nameof(observedStartServerSavePath));
        if (!string.Equals(expectedPath, observedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C lifecycle return refused a StartServerArgs save path different from the fixture path.");
        lock (gate)
        {
            if (activeLease is null || activeLease.Closed || !ReferenceEquals(activeLease.Identity, identity) ||
                !stable || nativeReturnStarted || pendingReturn is not null)
                throw new InvalidOperationException("L00-C lifecycle return reservation is stale, premature, duplicate, or owned by another run/sequence.");
            if (!string.Equals(identity.SavegameGuid, normalizedClientGuid, StringComparison.Ordinal))
                throw new InvalidOperationException("L00-C lifecycle server/client save GUID attestation does not match.");
            var reservation = new L00CLifecycleReturnReservation(activeLease, identity, normalizedClientGuid, role, expectedPath, fixtureSequence);
            pendingReturn = reservation;
            return reservation;
        }
    }

    internal void BeginNativeReturn(L00CLifecycleReturnReservation reservation)
    {
        if (reservation is null) throw new ArgumentNullException(nameof(reservation));
        lock (gate)
        {
            if (!ReferenceEquals(pendingReturn, reservation) || activeLease is null ||
                !ReferenceEquals(activeLease, reservation.Lease) || activeLease.Closed ||
                !ReferenceEquals(activeLease.Identity, reservation.Identity) || !stable || reservation.Cancelled || reservation.Started)
                throw new InvalidOperationException("L00-C lifecycle native return authorization is stale, closed, duplicate, or mismatched.");
            reservation.Started = true;
            pendingReturn = null;
            nativeReturnStarted = true;
            stable = false;
        }
    }

    internal bool AbortBeforeNativeReturn(L00CLifecycleReturnReservation reservation)
    {
        if (reservation is null) throw new ArgumentNullException(nameof(reservation));
        lock (gate)
        {
            if (reservation.Started || !ReferenceEquals(pendingReturn, reservation)) return false;
            reservation.Cancelled = true;
            pendingReturn = null;
            return true;
        }
    }

    private static string NormalizeRole(string role)
    {
        if (!string.Equals(role, "activated-primary", StringComparison.Ordinal) &&
            !string.Equals(role, "activated-secondary", StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C lifecycle return role is not a fixture role.");
        return role;
    }

    private static void ValidateFixtureSequence(string role, int fixtureSequence)
    {
        if (fixtureSequence < 1 || fixtureSequence > 8)
            throw new InvalidOperationException("L00-C lifecycle fixture sequence must be within the exact 1..8 campaign.");
        string expectedRole = fixtureSequence is 2 or 8 ? "activated-secondary" : "activated-primary";
        if (!string.Equals(role, expectedRole, StringComparison.Ordinal))
            throw new InvalidOperationException("L00-C lifecycle fixture role does not match its exact campaign sequence.");
    }

    private static string NormalizeSavePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("L00-C lifecycle save path is required.", parameterName);
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), ".vcdbs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("L00-C lifecycle save path must target a .vcdbs fixture.");
        return full;
    }
}

internal sealed class L00CLifecycleShutdownIdentity
{
    private L00CLifecycleShutdownIdentity(long runId, string instanceId, string savegameGuid, int sequence)
    {
        RunId = runId;
        InstanceId = instanceId;
        SavegameGuid = savegameGuid;
        Sequence = sequence;
    }

    internal long RunId { get; }
    internal string InstanceId { get; }
    internal string SavegameGuid { get; }
    internal int Sequence { get; }

    internal static L00CLifecycleShutdownIdentity Create(long runId, string instanceId, string savegameGuid, int sequence)
    {
        if (runId <= 0 || sequence <= 0 || string.IsNullOrWhiteSpace(instanceId))
            throw new InvalidOperationException("L00-C lifecycle shutdown identity has an invalid run, instance, or sequence.");
        return new L00CLifecycleShutdownIdentity(runId, instanceId, NormalizeGuid(savegameGuid, nameof(savegameGuid)), sequence);
    }

    internal static string NormalizeGuid(string value, string parameterName)
    {
        if (!Guid.TryParseExact(value, "D", out Guid parsed))
            throw new ArgumentException("L00-C lifecycle savegame identity must be a canonical D-format GUID.", parameterName);
        return parsed.ToString("D");
    }

}

internal sealed class L00CLifecycleShutdownLease
{
    internal L00CLifecycleShutdownLease(L00CLifecycleShutdownIdentity identity, long generation) { Identity = identity; Generation = generation; }
    internal L00CLifecycleShutdownIdentity Identity { get; }
    internal long Generation { get; }
    internal bool Closed { get; set; }
}

internal sealed class L00CLifecycleReturnReservation
{
    internal L00CLifecycleReturnReservation(L00CLifecycleShutdownLease lease, L00CLifecycleShutdownIdentity identity,
        string clientSavegameGuid, string fixtureRole, string savePath, int fixtureSequence)
    {
        Lease = lease;
        Identity = identity;
        ClientSavegameGuid = clientSavegameGuid;
        FixtureRole = fixtureRole;
        SavePath = savePath;
        FixtureSequence = fixtureSequence;
    }

    internal L00CLifecycleShutdownLease Lease { get; }
    internal L00CLifecycleShutdownIdentity Identity { get; }
    internal string ClientSavegameGuid { get; }
    internal string FixtureRole { get; }
    internal string SavePath { get; }
    internal int FixtureSequence { get; }
    internal bool Started { get; set; }
    internal bool Cancelled { get; set; }
}
#endif
