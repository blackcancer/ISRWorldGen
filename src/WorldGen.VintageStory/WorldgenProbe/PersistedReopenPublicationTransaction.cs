#if DEBUG
namespace ISRWorldGen.WorldgenProbe;

internal static class PersistedReopenPublicationTransaction
{
    public static void Execute<TInspection>(
        Func<TInspection> inspect,
        Action prepareCandidate,
        Action writeInventoryLog,
        Action writeActivationLog,
        Action<TInspection> scheduleForRun,
        Action commit)
    {
        ArgumentNullException.ThrowIfNull(inspect);
        ArgumentNullException.ThrowIfNull(prepareCandidate);
        ArgumentNullException.ThrowIfNull(writeInventoryLog);
        ArgumentNullException.ThrowIfNull(writeActivationLog);
        ArgumentNullException.ThrowIfNull(scheduleForRun);
        ArgumentNullException.ThrowIfNull(commit);

        TInspection inspection = inspect();
        prepareCandidate();
        writeInventoryLog();
        writeActivationLog();
        scheduleForRun(inspection);

        // This must remain the final fallible operation. Once StoreData accepts
        // the payload, no later exception may turn the reopen into a refusal.
        commit();
    }
}
#endif
