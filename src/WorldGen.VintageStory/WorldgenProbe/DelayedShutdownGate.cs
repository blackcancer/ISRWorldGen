#if DEBUG
using System;

namespace ISRWorldGen.WorldgenProbe;

// Owns the complete registration/unregistration transaction. No caller ever
// receives a listener id to compensate independently.
internal sealed class DelayedShutdownGate
{
    public const int MinimumActiveDelayMilliseconds = 10_000;
    public const int MaximumDelayMilliseconds = 60_000;

    private readonly object gate = new();
    private DelayedShutdownRegistration? active;
    private bool closing = true;
    private bool poisoned;

    public static int ValidateDelayMilliseconds(int delayMilliseconds)
    {
        if (delayMilliseconds != 0 && (delayMilliseconds < 50 || delayMilliseconds > MaximumDelayMilliseconds))
            throw new InvalidOperationException($"L00-C delayed shutdown must be 0 or within 50..{MaximumDelayMilliseconds} ms, not {delayMilliseconds} ms.");
        return delayMilliseconds;
    }

    public static int ValidateActiveDelayMilliseconds(int delayMilliseconds)
    {
        ValidateDelayMilliseconds(delayMilliseconds);
        if (delayMilliseconds < MinimumActiveDelayMilliseconds)
            throw new InvalidOperationException($"L00-C active shutdown delay must be within {MinimumActiveDelayMilliseconds}..{MaximumDelayMilliseconds} ms, not {delayMilliseconds} ms.");
        return delayMilliseconds;
    }

    public void Open()
    {
        lock (gate)
        {
            if (active is not null || poisoned)
                throw new InvalidOperationException("A delayed shutdown registration is active or its rollback is terminal.");
            closing = false;
        }
    }

    public void Schedule(int delayMilliseconds, Func<Action, int, long> register, Action<long> unregister, Action onFire)
    {
        if (register is null) throw new ArgumentNullException(nameof(register));
        if (unregister is null) throw new ArgumentNullException(nameof(unregister));
        if (onFire is null) throw new ArgumentNullException(nameof(onFire));

        DelayedShutdownRegistration registration;
        lock (gate)
        {
            if (closing || poisoned || active is not null)
                throw new InvalidOperationException("A delayed shutdown callback cannot be scheduled in the current state.");
            registration = new DelayedShutdownRegistration(unregister, onFire);
            active = registration;
        }

        long listenerId;
        try
        {
            listenerId = register(() => Complete(registration), delayMilliseconds);
        }
        catch
        {
            lock (gate)
            {
                registration.Cancelled = true;
                if (ReferenceEquals(active, registration)) active = null;
                poisoned = true;
                closing = true;
            }
            throw;
        }

        bool complete;
        bool rollback;
        lock (gate)
        {
            registration.RegisterReturned = true;
            registration.ListenerId = listenerId;
            registration.Attached = listenerId > 0;
            rollback = listenerId <= 0 || closing || registration.Cancelled || !ReferenceEquals(active, registration);
            if (rollback)
            {
                registration.Cancelled = true;
                poisoned = true;
                closing = true;
            }
            complete = !rollback && registration.CallbackArrived;
        }

        if (rollback)
        {
            Compensate(registration);
            throw new InvalidOperationException("L00-C delayed shutdown registration returned an invalid id or crossed a closing boundary.");
        }
        if (complete) CompleteRegistered(registration);
    }

    public void CancelAndUnregister()
    {
        DelayedShutdownRegistration? registration;
        lock (gate)
        {
            closing = true;
            registration = active;
            if (registration is null) return;
            registration.Cancelled = true;
            poisoned = true;
            if (!registration.RegisterReturned)
            {
                // Schedule owns the eventual compensation after register returns.
                return;
            }
        }
        Compensate(registration);
    }

    private void Complete(DelayedShutdownRegistration registration)
    {
        bool complete;
        lock (gate)
        {
            if (!ReferenceEquals(active, registration) || registration.Cancelled || registration.Completed || poisoned || closing)
                return;
            registration.CallbackArrived = true;
            complete = registration.RegisterReturned && registration.Attached;
        }
        if (complete) CompleteRegistered(registration);
    }

    private void CompleteRegistered(DelayedShutdownRegistration registration)
    {
        lock (gate)
        {
            if (!ReferenceEquals(active, registration) || registration.Cancelled || registration.Completed || registration.UnregisterInProgress)
                return;
            registration.UnregisterInProgress = true;
        }

        try
        {
            registration.Unregister(registration.ListenerId);
        }
        catch (Exception first)
        {
            RollbackAfterUnregisterFailure(registration, first);
            return;
        }

        Action? fire = null;
        lock (gate)
        {
            registration.UnregisterInProgress = false;
            registration.Unregistered = true;
            registration.Completed = true;
            if (ReferenceEquals(active, registration)) active = null;
            closing = true;
            if (!registration.Cancelled && !poisoned) fire = registration.OnFire;
        }
        fire?.Invoke();
    }

    private void Compensate(DelayedShutdownRegistration registration)
    {
        lock (gate)
        {
            if (registration.Unregistered)
            {
                if (ReferenceEquals(active, registration)) active = null;
                return;
            }
            if (registration.UnregisterInProgress) return;
            registration.UnregisterInProgress = true;
        }

        try
        {
            registration.Unregister(registration.ListenerId);
        }
        catch (Exception first)
        {
            RollbackAfterUnregisterFailure(registration, first);
            return;
        }

        lock (gate)
        {
            registration.UnregisterInProgress = false;
            registration.Unregistered = true;
            registration.Completed = true;
            if (ReferenceEquals(active, registration)) active = null;
        }
    }

    // A single bounded retry uses the exact same id. The gate remains poisoned
    // and the callback remains cancelled even when the retry succeeds.
    private void RollbackAfterUnregisterFailure(DelayedShutdownRegistration registration, Exception first)
    {
        lock (gate)
        {
            registration.Cancelled = true;
            poisoned = true;
            closing = true;
            // Keep ownership across the retry. A concurrent cancellation may
            // observe the pending compensation, but cannot invoke the same
            // external unregister operation in parallel.
            registration.UnregisterInProgress = true;
        }
        try
        {
            registration.Unregister(registration.ListenerId);
        }
        catch (Exception second)
        {
            lock (gate) registration.UnregisterInProgress = false;
            throw new AggregateException("L00-C delayed shutdown exact-id compensation failed twice.", first, second);
        }
        lock (gate)
        {
            registration.UnregisterInProgress = false;
            registration.Unregistered = true;
            registration.Completed = true;
            if (ReferenceEquals(active, registration)) active = null;
        }
    }
}

internal sealed class DelayedShutdownRegistration
{
    internal DelayedShutdownRegistration(Action<long> unregister, Action onFire) { Unregister = unregister; OnFire = onFire; }
    internal Action<long> Unregister { get; }
    internal Action OnFire { get; }
    internal long ListenerId { get; set; }
    internal bool RegisterReturned { get; set; }
    internal bool Attached { get; set; }
    internal bool CallbackArrived { get; set; }
    internal bool UnregisterInProgress { get; set; }
    internal bool Unregistered { get; set; }
    internal bool Completed { get; set; }
    internal bool Cancelled { get; set; }
}
#endif
