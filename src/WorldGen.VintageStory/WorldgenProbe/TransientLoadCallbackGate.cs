#if DEBUG
namespace ISRWorldGen.WorldgenProbe;

internal sealed class TransientLoadCallbackGate
{
    private readonly object gate = new();
    private TransientLoadReservation? active;
    private bool closing;

    internal int PendingCount
    {
        get
        {
            lock (gate)
            {
                return active is null ? 0 : 1;
            }
        }
    }

    internal TransientLoadReservation Begin(Action onLoaded)
    {
        ArgumentNullException.ThrowIfNull(onLoaded);
        lock (gate)
        {
            if (closing)
            {
                throw new InvalidOperationException("Transient callbacks are closed.");
            }
            if (active is not null)
            {
                throw new InvalidOperationException("A transient callback is already pending.");
            }

            active = new TransientLoadReservation(onLoaded);
            return active;
        }
    }

    internal void Accept(TransientLoadReservation reservation)
    {
        Action? ready = null;
        lock (gate)
        {
            if (!ReferenceEquals(active, reservation) || reservation.Cancelled || reservation.CallbackInvoked)
            {
                return;
            }

            reservation.Accepted = true;
            if (reservation.CallbackArrived)
            {
                reservation.CallbackInvoked = true;
                active = null;
                ready = reservation.OnLoaded;
            }
        }

        ready?.Invoke();
    }

    internal void Complete(TransientLoadReservation reservation)
    {
        Action? ready = null;
        lock (gate)
        {
            if (!ReferenceEquals(active, reservation) || reservation.Cancelled || reservation.CallbackInvoked)
            {
                return;
            }
            if (closing)
            {
                reservation.Cancelled = true;
                reservation.CallbackInvoked = true;
                active = null;
                return;
            }

            reservation.CallbackArrived = true;
            if (reservation.Accepted)
            {
                reservation.CallbackInvoked = true;
                active = null;
                ready = reservation.OnLoaded;
            }
        }

        ready?.Invoke();
    }

    internal void Reject(TransientLoadReservation reservation)
    {
        lock (gate)
        {
            reservation.Cancelled = true;
            if (ReferenceEquals(active, reservation))
            {
                active = null;
            }
        }
    }

    internal int Reset()
    {
        lock (gate)
        {
            closing = true;
            if (active is null)
            {
                return 0;
            }

            int cancelled = !active.Cancelled && !active.CallbackInvoked ? 1 : 0;
            active.Cancelled = true;
            active = null;
            return cancelled;
        }
    }

    internal void Open()
    {
        lock (gate)
        {
            if (active is not null)
            {
                throw new InvalidOperationException("Transient callbacks cannot reopen while a reservation is pending.");
            }
            closing = false;
        }
    }
}

internal sealed class TransientLoadReservation(Action onLoaded)
{
    internal Action OnLoaded { get; } = onLoaded;
    internal bool Accepted { get; set; }
    internal bool CallbackArrived { get; set; }
    internal bool CallbackInvoked { get; set; }
    internal bool Cancelled { get; set; }
}
#endif
