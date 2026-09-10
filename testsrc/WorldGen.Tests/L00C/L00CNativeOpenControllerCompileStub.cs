#if L00C_STANDALONE_ORACLE
#nullable enable
using ISRWorldGen.WorldgenProbe;

namespace ISRWorldGen.L00C.Laboratory;

// Compile-only seam for the driver/fixture oracles. The real Debug build and
// the executable LevelFinalize oracle use L00CProcessCampaignController.
internal static class L00CProcessCampaignController
{
    private static L00CLevelFinalizeGate gate = new();
    internal static L00CNativeOpenReservation BeginNativeOpen(int fixtureSequence) =>
        gate.BeginOpen(fixtureSequence);
    internal static void CompleteNativeOpen(L00CNativeOpenReservation reservation) =>
        gate.CompleteOpen(reservation);
    internal static bool AbortNativeOpen(L00CNativeOpenReservation reservation) =>
        gate.AbortOpen(reservation);
    internal static void ResetForTests() => gate = new L00CLevelFinalizeGate();
}
#endif
