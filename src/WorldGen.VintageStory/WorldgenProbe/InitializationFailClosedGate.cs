#if DEBUG
namespace ISRWorldGen.WorldgenProbe;

internal sealed class InitializationFailClosedGate
{
    private readonly object gate = new();
    private int currentAttempt;
    private bool failed;

    public Exception? CleanupException { get; private set; }
    public Exception? ShutdownException { get; private set; }

    public int BeginAttempt()
    {
        lock (gate)
        {
            currentAttempt++;
            failed = false;
            CleanupException = null;
            ShutdownException = null;
            return currentAttempt;
        }
    }

    public bool Fail(int attempt, Action closeProbeState, Action requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(closeProbeState);
        ArgumentNullException.ThrowIfNull(requestShutdown);
        lock (gate)
        {
            if (attempt != currentAttempt || failed)
            {
                return false;
            }
            failed = true;
        }

        try
        {
            closeProbeState();
        }
        catch (Exception exception)
        {
            CleanupException = exception;
        }

        try
        {
            requestShutdown();
        }
        catch (Exception exception)
        {
            ShutdownException = exception;
        }
        return true;
    }
}
#endif
