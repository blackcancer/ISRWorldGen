#if DEBUG
namespace ISRWorldGen.WorldgenProbe;

internal sealed class DelayedShutdownGate
{
    private readonly object gate = new();
    private DelayedShutdownReservation? active;
    private bool closing = true;

    public static int ValidateDelayMilliseconds(int delayMilliseconds)
    {
        if (delayMilliseconds != 0 && (delayMilliseconds < 50 || delayMilliseconds > 60_000))
        {
            throw new InvalidOperationException($"L00-C delayed shutdown must be 0 or within 50..60000 ms, not {delayMilliseconds} ms.");
        }
        return delayMilliseconds;
    }

    public void Open()
    {
        lock (gate)
        {
            if (active is not null)
            {
                throw new InvalidOperationException("A delayed shutdown callback is still active.");
            }
            closing = false;
        }
    }

    public DelayedShutdownReservation Begin(Action onFire)
    {
        ArgumentNullException.ThrowIfNull(onFire);
        lock (gate)
        {
            if (closing || active is not null)
            {
                throw new InvalidOperationException("A delayed shutdown callback cannot be armed in the current state.");
            }
            active = new DelayedShutdownReservation(onFire);
            return active;
        }
    }

    public bool Attach(DelayedShutdownReservation reservation, long listenerId)
    {
        if (listenerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(listenerId));
        }
        Action? fire = null;
        lock (gate)
        {
            if (closing || !ReferenceEquals(active, reservation) || reservation.Cancelled || reservation.Fired)
            {
                return false;
            }
            reservation.ListenerId = listenerId;
            reservation.Attached = true;
            if (reservation.CallbackArrived)
            {
                reservation.Fired = true;
                active = null;
                fire = reservation.OnFire;
            }
        }
        fire?.Invoke();
        return true;
    }

    public void Complete(DelayedShutdownReservation reservation)
    {
        Action? fire = null;
        lock (gate)
        {
            if (closing || !ReferenceEquals(active, reservation) || reservation.Cancelled || reservation.Fired)
            {
                return;
            }
            reservation.CallbackArrived = true;
            if (reservation.Attached)
            {
                reservation.Fired = true;
                active = null;
                fire = reservation.OnFire;
            }
        }
        fire?.Invoke();
    }

    public void Reject(DelayedShutdownReservation reservation)
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

    public long Cancel()
    {
        lock (gate)
        {
            closing = true;
            if (active is null)
            {
                return 0;
            }
            active.Cancelled = true;
            long listenerId = active.Attached ? active.ListenerId : 0;
            active = null;
            return listenerId;
        }
    }
}

internal sealed class DelayedShutdownReservation(Action onFire)
{
    public Action OnFire { get; } = onFire;
    public long ListenerId { get; set; }
    public bool Attached { get; set; }
    public bool CallbackArrived { get; set; }
    public bool Fired { get; set; }
    public bool Cancelled { get; set; }
}
#endif
